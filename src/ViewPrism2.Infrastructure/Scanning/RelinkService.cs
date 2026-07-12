using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Repositories;
using ViewPrism2.Core.Services.Repair;

namespace ViewPrism2.Infrastructure.Scanning;

/// <summary>
/// 再リンクサービス(M-SCAN-005 + M-RELINK-025 拡張、REQ-017/REQ-069、仕様 §2.11.2)。
/// missing 画像に対し、①同一フォルダ・同ハッシュの pending(exact-hash 候補)と
/// ②criteria 検索結果(status ∈ {Pending, Normal})を候補として提示し、確定で missing 行へ
/// 候補のパス情報を上書きする(missing 側 image_id は不変 — INV-001 / INV-015)。
/// **タグ安全ガード(INV-015)**: 置換に使える候補は user タグを持たないもの(pending または
/// untagged-normal)に限る。タグ付き候補を消費するとそのタグが失われるため relink を拒否し、
/// マージ(§2.10.5)を案内する。
/// </summary>
/// <summary>自動修復ペア(GF-075-01: missing とその一意候補。単一パスで確定する)。</summary>
public sealed record AutoRepairPair(string MissingImageId, string CandidateImageId);

public sealed class RelinkService
{
    private readonly IImageRepository _images;
    private readonly ITagRepository? _tags;

    /// <summary>
    /// 構築する。<paramref name="tags"/>(INV-015 のタグ安全ガード用)は production DI で必ず注入する。
    /// 既存の同ハッシュ pending のみの V1 経路(criteria なし・タグ判定不要)互換のため省略可能とするが、
    /// criteria 候補・タグ安全ガードを使う production ではコンストラクタ注入する(App.axaml.cs)。
    /// </summary>
    public RelinkService(IImageRepository images, ITagRepository? tags = null)
    {
        _images = images;
        _tags = tags;
    }

    /// <summary>
    /// 候補列挙(REQ-069 / 仕様 §2.11.2): exact-hash pending に加え、<paramref name="criteria"/> 指定時は
    /// criteria 検索結果(status ∈ {Pending, Normal})を候補化する。**user タグを持つ候補は除外**(タグ損失防止)。
    /// 安定順(relative_path 昇順・同値 id 昇順)。対象が missing でなければ空列。
    /// </summary>
    public async Task<IReadOnlyList<RelinkCandidate>> GetCandidatesAsync(
        string missingImageId,
        SearchCriteria? criteria = null)
    {
        var missing = await _images.GetByIdAsync(missingImageId).ConfigureAwait(false);
        if (missing is null || missing.Status != ImageStatus.Missing)
        {
            return [];
        }

        var inFolder = await _images.GetByFolderAsync(missing.SyncFolderId).ConfigureAwait(false);
        var byId = inFolder.ToDictionary(i => i.Id, StringComparer.Ordinal);

        // ① exact-hash pending 候補(従来の ScanJudge 3a 経路)
        var candidateIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var image in inFolder.Where(i =>
                     i.Status == ImageStatus.Pending &&
                     string.Equals(i.Hash, missing.Hash, StringComparison.Ordinal)))
        {
            candidateIds.Add(image.Id);
        }

        // ② criteria 検索結果(status ∈ {Pending, Normal}・同一コレクション)
        // GF-075-01: CriteriaSearchService 経由(フォルダ全行の再ロード)をやめ、同じ inFolder 行で
        // CriteriaMatcher を直接評価する(1 呼び出し=単一ロード。意味論は同一)
        if (criteria is not null)
        {
            var statusTargets = new HashSet<ImageStatus> { ImageStatus.Pending, ImageStatus.Normal };
            foreach (var id in CriteriaMatcher.Match(inFolder, criteria, statusTargets))
            {
                candidateIds.Add(id);
            }
        }

        // missing 自身は候補にしない
        candidateIds.Remove(missingImageId);

        // タグ安全ガード(INV-015): user タグを持つ候補は除外する。
        // _tags 未注入(V1 互換経路)は exact-hash pending のみで構成され元々未タグのため全採用。
        var safe = new List<ImageRecord>();
        foreach (var id in candidateIds)
        {
            if (!byId.TryGetValue(id, out var record))
            {
                continue;
            }

            if (_tags is null)
            {
                safe.Add(record);
                continue;
            }

            var imageTags = await _tags.GetImageTagsAsync(id).ConfigureAwait(false);
            if (imageTags.Count == 0)
            {
                safe.Add(record);
            }
        }

        return safe
            .OrderBy(i => i.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.Id, StringComparer.Ordinal)
            .Select(i => new RelinkCandidate
            {
                ImageId = i.Id,
                RelativePath = i.RelativePath,
                FileSize = i.FileSize,
                ModifiedDate = i.ModifiedDate,
            })
            .ToList();
    }

    /// <summary>
    /// ECO-075: フォルダ内 missing 全件の「自動修復可能」数(hash+拡張子+サイズで候補がちょうど 1 件)を
    /// **単一ロードの集合演算**で数える。missing ごとの GetCandidatesAsync(毎回フォルダ全行×2 ロード=
    /// O(M×N))を置き換える。判定意味論は GetCandidatesAsync+自動修復 criteria と同一:
    /// exact-hash pending ∪ criteria(hash+拡張子+サイズ・Pending∪Normal)− 自身 − タグ付き(INV-015)。
    /// </summary>
    public async Task<int> CountAutoRepairableAsync(string folderId)
        => (await GetAutoRepairablePairsAsync(folderId).ConfigureAwait(false)).Count;

    /// <summary>
    /// GF-075-01: 自動修復ペア(missing → 一意候補)を単一パスで確定する。「すべて自動修復」が
    /// missing ごとに GetCandidatesAsync(全行再ロード)を繰り返す O(M×N) を置き換える。
    /// 同一候補を取り合う 2 組目以降は CommitRelinkAsync 側の検証が拒否する(旧逐次探索と同じ帰結)。
    /// </summary>
    public async Task<IReadOnlyList<AutoRepairPair>> GetAutoRepairablePairsAsync(string folderId)
    {
        var inFolder = await _images.GetByFolderAsync(folderId).ConfigureAwait(false);
        var statusTargets = new HashSet<ImageStatus> { ImageStatus.Pending, ImageStatus.Normal };
        var byHash = new Dictionary<string, List<ImageRecord>>(StringComparer.Ordinal);
        foreach (var record in inFolder)
        {
            if (record.Status is ImageStatus.Pending or ImageStatus.Normal)
            {
                (byHash.TryGetValue(record.Hash, out var list) ? list : byHash[record.Hash] = []).Add(record);
            }
        }

        var taggedMemo = new Dictionary<string, bool>(StringComparer.Ordinal);
        var pairs = new List<AutoRepairPair>();
        foreach (var missing in inFolder)
        {
            if (missing.Status != ImageStatus.Missing || !byHash.TryGetValue(missing.Hash, out var bucket))
            {
                continue;
            }

            var criteria = new SearchCriteria
            {
                Hash = missing.Hash,
                Extension = Path.GetExtension(missing.FileName),
                SizeMin = missing.FileSize,
                SizeMax = missing.FileSize,
            };
            var candidateIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var image in bucket)
            {
                if (image.Status == ImageStatus.Pending)
                {
                    candidateIds.Add(image.Id); // ① exact-hash pending(GetCandidatesAsync と同一)
                }
            }

            foreach (var id in CriteriaMatcher.Match(bucket, criteria, statusTargets))
            {
                candidateIds.Add(id); // ② criteria 検索(同一ハッシュ bucket 内で十分=hash 条件が含まれる)
            }

            candidateIds.Remove(missing.Id);

            // タグ付き除外(INV-015)。「ちょうど 1 件」判定に必要な分だけ照会し memo 化する
            var safe = 0;
            string? single = null;
            foreach (var id in candidateIds)
            {
                if (safe > 1)
                {
                    break;
                }

                if (!taggedMemo.TryGetValue(id, out var hasTags))
                {
                    hasTags = _tags is not null
                        && (await _tags.GetImageTagsAsync(id).ConfigureAwait(false)).Count > 0;
                    taggedMemo[id] = hasTags;
                }

                if (!hasTags)
                {
                    safe++;
                    single = id;
                }
            }

            if (safe == 1)
            {
                pairs.Add(new AutoRepairPair(missing.Id, single!));
            }
        }

        return pairs;
    }

    /// <summary>
    /// 確定(REQ-069 / T4・単一トランザクション・原子): missing 行へ replacement 行のパス情報を上書きし
    /// status=normal・candidate_link_id=NULL、replacement 行は削除する。missing 側 image_id は不変(INV-001)。
    /// replacement は pending または untagged-normal に限る。タグ付き replacement は
    /// <see cref="ErrorCode.ValidationError"/>(タグ損失防止・マージ案内 — INV-015)。
    /// 遷移表(T4)外の入力(missing でない/別コレクション/許可外 status)は拒否する。
    /// </summary>
    public async Task<Result> CommitRelinkAsync(string missingImageId, string replacementImageId)
    {
        var missing = await _images.GetByIdAsync(missingImageId).ConfigureAwait(false);
        if (missing is null)
        {
            return Result.Fail(ErrorCode.NotFound, "missing 画像が存在しません。");
        }

        var replacement = await _images.GetByIdAsync(replacementImageId).ConfigureAwait(false);
        if (replacement is null)
        {
            return Result.Fail(ErrorCode.NotFound, "置換候補の画像が存在しません。");
        }

        if (missing.Status != ImageStatus.Missing)
        {
            return Result.Fail(ErrorCode.ValidationError, "対象はリンク切れ(missing)画像である必要があります。");
        }

        // 置換候補は pending または untagged-normal のみ(T4 / INV-015)
        if (replacement.Status is not (ImageStatus.Pending or ImageStatus.Normal))
        {
            return Result.Fail(ErrorCode.ValidationError, "置換候補は pending または normal 画像である必要があります。");
        }

        // 別コレクションは拒否(同一 sync_folder 内に限定)
        if (!string.Equals(missing.SyncFolderId, replacement.SyncFolderId, StringComparison.Ordinal))
        {
            return Result.Fail(ErrorCode.ValidationError, "同一コレクション内の候補のみ再リンクできます。");
        }

        // タグ安全ガード(INV-015): タグ付き候補の消費は拒否し、マージへ案内する
        // (_tags 未注入の V1 互換経路は same-hash pending のみで未タグのため判定不要)
        if (_tags is not null)
        {
            var replacementTags = await _tags.GetImageTagsAsync(replacementImageId).ConfigureAwait(false);
            if (replacementTags.Count > 0)
            {
                return Result.Fail(
                    ErrorCode.ValidationError,
                    "タグの付いた候補は再リンクできません(タグが失われます)。マージをご利用ください。");
            }
        }

        await _images.ApplyRelinkAsync(missingImageId, replacementImageId).ConfigureAwait(false);
        return Result.Ok();
    }
}
