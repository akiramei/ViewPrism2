using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Repositories;

namespace ViewPrism2.Core.Services.Repair;

/// <summary>ECO-140: 統合裁定面で表示する 3 グループ。</summary>
public enum IntegrityReviewGroup
{
    Automatic,
    Individual,
    Missing,
}

/// <summary>ECO-140: pending/missing 行から組み立てる事象の種類。</summary>
public enum IntegrityReviewKind
{
    Moved,
    Reappeared,
    Changed,
    New,
    Restored,
    Missing,
}

/// <summary>reappeared の裁定時 on-demand hash 確認結果。Pending は未確認。</summary>
public enum IntegrityReviewHashOutcome
{
    NotApplicable,
    Pending,
    Match,
    Mismatch,
    Failed,
}

/// <summary>統合裁定面の 1 行。移動だけ Counterpart=missing 行を持つ。</summary>
public sealed record IntegrityReviewEvent(
    ImageRecord Primary,
    ImageRecord? Counterpart,
    IntegrityReviewKind Kind,
    IntegrityReviewGroup Group,
    IntegrityReviewHashOutcome HashOutcome = IntegrityReviewHashOutcome.NotApplicable);

/// <summary>IR-7 の進捗(k/n)。固定時間閾値は持たない。</summary>
public sealed record IntegrityReviewHashProgress(int Completed, int Total);

/// <summary>統合裁定面の分類結果。</summary>
public sealed record IntegrityReviewSnapshot(
    IReadOnlyList<IntegrityReviewEvent> Events,
    bool HashCheckComplete)
{
    public IReadOnlyList<IntegrityReviewEvent> AutomaticEvents =>
        Events.Where(e => e.Group == IntegrityReviewGroup.Automatic).ToList();
}

/// <summary>混在バッチで T13 受け入れする reappeared と、確認時の裁定基準 hash。</summary>
public sealed record IntegrityReviewAcceptTarget(string ImageId, string ExpectedHash);

/// <summary>E-RELINK-007 の自動確定ペア。missing 側 ID を残し candidate 行を消費する。</summary>
public sealed record AutoRepairPair(string MissingImageId, string CandidateImageId);

/// <summary>
/// E-RELINK-007 が返す選別結果。Relinkable は個別 T4 可、Unique はそのうち自動 T4 可。
/// </summary>
public sealed record RelinkSelection(
    IReadOnlySet<string> RelinkablePendingIds,
    IReadOnlySet<string> UniquelyRelinkablePendingIds);

/// <summary>
/// REQ-103: reappeared hash 再計算の注入 seam。ScanJudge/ScanStaging は本契約を消費しない。
/// </summary>
public interface IIntegrityReviewHashProvider
{
    Task<string> ComputeSha256Async(string absolutePath, CancellationToken ct);
}

/// <summary>
/// E-RELINK-007 の Core 側契約。選別と単発/原子バッチ確定を同じ所有者へ束ねる。
/// </summary>
public interface IRelinkService
{
    IReadOnlyList<ImageRecord> SelectUniquelyRelinkable(
        IEnumerable<ImageRecord> images,
        IReadOnlyDictionary<string, ImageRecord>? candidateRows = null);

    Task<IReadOnlyList<ImageRecord>> GetUniquelyRelinkableAsync(
        IEnumerable<ImageRecord> images,
        IReadOnlyDictionary<string, ImageRecord>? candidateRows = null,
        CancellationToken ct = default);

    Task<RelinkSelection> GetRelinkSelectionAsync(
        IEnumerable<ImageRecord> images,
        IReadOnlyDictionary<string, ImageRecord>? candidateRows = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<AutoRepairPair>> GetAutoRepairablePairsAsync(string folderId);

    Task<IReadOnlyList<RelinkCandidate>> GetCandidatesAsync(
        string missingImageId,
        SearchCriteria? criteria = null);

    Task<Result> CommitRelinkAsync(string missingImageId, string replacementImageId);

    Task<Result<int>> ApplyRelinkBatchAsync(IEnumerable<ImageRecord> images);

    Task<Result<int>> ApplyIntegrityReviewBatchAsync(
        IReadOnlyCollection<AutoRepairPair> relinks,
        IReadOnlyCollection<IntegrityReviewAcceptTarget> accepts);
}

/// <summary>
/// ECO-140/REQ-102/103: 統合裁定の計算核。
/// 母集合は repository の pending∪missing 限定 API から取得し、分類は pure な
/// <see cref="Classify"/>、reappeared の hash 再計算は注入 seam 経由で行う。
/// </summary>
public sealed class IntegrityReviewService
{
    private readonly IImageRepository _images;
    private readonly IRelinkService _relink;
    private readonly IIntegrityReviewHashProvider _hashProvider;
    private readonly Dictionary<string, VerifiedHash> _verifiedHashes =
        new(StringComparer.Ordinal);

    private sealed record VerifiedHash(
        string BaselineHash,
        string RecordedHash,
        string RelativePath,
        long FileSize,
        string ModifiedDate,
        IntegrityReviewHashOutcome Outcome);

    public IntegrityReviewService(
        IImageRepository images,
        IRelinkService relink,
        IIntegrityReviewHashProvider hashProvider)
    {
        _images = images;
        _relink = relink;
        _hashProvider = hashProvider;
    }

    /// <summary>
    /// pending∪missing 行を 3 グループへ決定論的に分類する。DB/ファイル I/O は行わない。
    /// </summary>
    public static IReadOnlyList<IntegrityReviewEvent> Classify(
        IEnumerable<ImageRecord> source,
        IReadOnlyCollection<string> uniquelyRelinkablePendingIds,
        IReadOnlyCollection<string>? relinkablePendingIds = null,
        IReadOnlyDictionary<string, IntegrityReviewHashOutcome>? reappearedHashOutcomes = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(uniquelyRelinkablePendingIds);

        var rows = source
            .Where(r => r.Status is ImageStatus.Pending or ImageStatus.Missing)
            .DistinctBy(r => r.Id, StringComparer.Ordinal)
            .ToList();
        var byId = rows.ToDictionary(r => r.Id, StringComparer.Ordinal);
        var uniqueIds = uniquelyRelinkablePendingIds.ToHashSet(StringComparer.Ordinal);
        var relinkableIds = (relinkablePendingIds ?? uniquelyRelinkablePendingIds)
            .ToHashSet(StringComparer.Ordinal);
        var hashOutcomes = reappearedHashOutcomes
            ?? new Dictionary<string, IntegrityReviewHashOutcome>(StringComparer.Ordinal);

        // missing 単独=一致する pending が無いもの。曖昧な複数 new も missing を別事象へ
        // 二重計上せず、各 pending を個別確認へ回す。
        var referencedMissingIds = rows
            .Where(r => relinkableIds.Contains(r.Id)
                        && r.CandidateLinkId is not null
                        && byId.TryGetValue(r.CandidateLinkId, out var candidate)
                        && candidate.Status == ImageStatus.Missing)
            .Select(r => r.CandidateLinkId!)
            .ToHashSet(StringComparer.Ordinal);

        var events = new List<IntegrityReviewEvent>();
        foreach (var pending in rows.Where(r => r.Status == ImageStatus.Pending))
        {
            byId.TryGetValue(pending.CandidateLinkId ?? string.Empty, out var counterpart);
            if (relinkableIds.Contains(pending.Id) && counterpart?.Status == ImageStatus.Missing)
            {
                events.Add(new IntegrityReviewEvent(
                    pending,
                    counterpart,
                    IntegrityReviewKind.Moved,
                    uniqueIds.Contains(pending.Id)
                        ? IntegrityReviewGroup.Automatic
                        : IntegrityReviewGroup.Individual));
                continue;
            }

            if (pending.PendingOrigin == PendingOrigin.Reappeared)
            {
                var outcome = hashOutcomes.GetValueOrDefault(
                    pending.Id, IntegrityReviewHashOutcome.Pending);
                events.Add(new IntegrityReviewEvent(
                    pending,
                    null,
                    IntegrityReviewKind.Reappeared,
                    outcome == IntegrityReviewHashOutcome.Match
                        ? IntegrityReviewGroup.Automatic
                        : IntegrityReviewGroup.Individual,
                    outcome));
                continue;
            }

            var kind = pending.PendingOrigin switch
            {
                PendingOrigin.Changed => IntegrityReviewKind.Changed,
                PendingOrigin.Restored => IntegrityReviewKind.Restored,
                _ => IntegrityReviewKind.New,
            };
            events.Add(new IntegrityReviewEvent(
                pending, counterpart, kind, IntegrityReviewGroup.Individual));
        }

        events.AddRange(rows
            .Where(r => r.Status == ImageStatus.Missing && !referencedMissingIds.Contains(r.Id))
            .Select(r => new IntegrityReviewEvent(
                r, null, IntegrityReviewKind.Missing, IntegrityReviewGroup.Missing)));

        return events
            .OrderBy(e => e.Group)
            .ThenBy(e => e.Primary.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Primary.Id, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// 面を開いた時の母集合取得と reappeared hash 確認。hash 確認自体は DB を変更しない。
    /// </summary>
    public async Task<IntegrityReviewSnapshot> LoadAsync(
        SyncFolder folder,
        CancellationToken ct = default,
        IProgress<IntegrityReviewHashProgress>? progress = null,
        IProgress<IntegrityReviewSnapshot>? interimSnapshots = null,
        bool reuseVerifiedHashes = false)
    {
        ArgumentNullException.ThrowIfNull(folder);
        var rows = await _images.GetIntegrityReviewByFolderAsync(folder.Id, ct).ConfigureAwait(false);
        var candidateRows = rows
            .Where(r => r.Status == ImageStatus.Missing)
            .ToDictionary(r => r.Id, StringComparer.Ordinal);
        var relinkSelection = await _relink
            .GetRelinkSelectionAsync(rows, candidateRows, ct)
            .ConfigureAwait(false);

        var reappeared = rows
            .Where(r => r.Status == ImageStatus.Pending
                        && r.PendingOrigin == PendingOrigin.Reappeared)
            .ToList();
        if (!reuseVerifiedHashes)
        {
            _verifiedHashes.Clear();
        }

        var outcomes = new Dictionary<string, IntegrityReviewHashOutcome>(StringComparer.Ordinal);
        var needsHash = new List<ImageRecord>();
        foreach (var row in reappeared)
        {
            var baselineHash = row.PendingBaselineHash ?? row.Hash;
            if (reuseVerifiedHashes
                && _verifiedHashes.TryGetValue(row.Id, out var verified)
                && verified.BaselineHash == baselineHash
                && verified.RecordedHash == row.Hash
                && verified.RelativePath == row.RelativePath
                && verified.FileSize == row.FileSize
                && verified.ModifiedDate == row.ModifiedDate)
            {
                outcomes[row.Id] = verified.Outcome;
            }
            else
            {
                needsHash.Add(row);
            }
        }

        var currentIds = reappeared.Select(row => row.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var staleId in _verifiedHashes.Keys.Where(id => !currentIds.Contains(id)).ToList())
        {
            _verifiedHashes.Remove(staleId);
        }

        // IR-7: hash I/O の完了を待たず、未確認 reappeared を個別グループとして先に公開する。
        // UI は IsHashChecking 中だけ自動グループ/CTA を隠すが、他の個別裁定は継続できる。
        if (needsHash.Count > 0)
        {
            interimSnapshots?.Report(new IntegrityReviewSnapshot(
                Classify(
                    rows,
                    relinkSelection.UniquelyRelinkablePendingIds,
                    relinkSelection.RelinkablePendingIds,
                    outcomes),
                HashCheckComplete: false));
        }
        var alreadyVerified = reappeared.Count - needsHash.Count;
        progress?.Report(new IntegrityReviewHashProgress(alreadyVerified, reappeared.Count));
        for (var index = 0; index < needsHash.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            var row = needsHash[index];
            var absolutePath = Path.Combine(
                folder.Path, row.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            try
            {
                var currentHash = await _hashProvider
                    .ComputeSha256Async(absolutePath, ct)
                    .ConfigureAwait(false);
                var baselineHash = row.PendingBaselineHash ?? row.Hash;
                outcomes[row.Id] = string.Equals(currentHash, baselineHash, StringComparison.Ordinal)
                    ? IntegrityReviewHashOutcome.Match
                    : IntegrityReviewHashOutcome.Mismatch;
                _verifiedHashes[row.Id] = new VerifiedHash(
                    baselineHash,
                    row.Hash,
                    row.RelativePath,
                    row.FileSize,
                    row.ModifiedDate,
                    outcomes[row.Id]);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is IOException
                                       or UnauthorizedAccessException
                                       or System.Security.SecurityException
                                       or NotSupportedException)
            {
                outcomes[row.Id] = IntegrityReviewHashOutcome.Failed;
                _verifiedHashes.Remove(row.Id);
            }

            progress?.Report(new IntegrityReviewHashProgress(
                alreadyVerified + index + 1,
                reappeared.Count));
        }

        return new IntegrityReviewSnapshot(
            Classify(
                rows,
                relinkSelection.UniquelyRelinkablePendingIds,
                relinkSelection.RelinkablePendingIds,
                outcomes),
            HashCheckComplete: true);
    }

    /// <summary>
    /// 自動グループを混在単一トランザクションで適用する。
    /// 移動=T4、reappeared hash 一致=T13。stale 1 件で全 rollback。
    /// </summary>
    public Task<Result<int>> ApplyAutomaticAsync(
        IReadOnlyCollection<IntegrityReviewEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (events.Any(e => e.Group != IntegrityReviewGroup.Automatic))
        {
            return Task.FromResult(Result<int>.Fail(
                ErrorCode.ValidationError, "自動裁定できない事象が含まれています。"));
        }

        var relinks = events
            .Where(e => e.Kind == IntegrityReviewKind.Moved && e.Counterpart is not null)
            .Select(e => new AutoRepairPair(e.Counterpart!.Id, e.Primary.Id))
            .ToList();
        var accepts = events
            .Where(e => e.Kind == IntegrityReviewKind.Reappeared
                        && e.HashOutcome == IntegrityReviewHashOutcome.Match)
            .Select(e => new IntegrityReviewAcceptTarget(
                e.Primary.Id,
                e.Primary.PendingBaselineHash ?? e.Primary.Hash))
            .ToList();
        if (relinks.Count + accepts.Count != events.Count)
        {
            return Task.FromResult(Result<int>.Fail(
                ErrorCode.ValidationError, "自動裁定対象の事象が不正です。"));
        }

        return _relink.ApplyIntegrityReviewBatchAsync(relinks, accepts);
    }
}
