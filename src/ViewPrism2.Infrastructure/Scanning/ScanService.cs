using System.Collections.Concurrent;
using System.IO.Enumeration;
using Microsoft.Extensions.Logging;
using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Repositories;
using ViewPrism2.Core.Services;

namespace ViewPrism2.Infrastructure.Scanning;

/// <summary>
/// スキャンサービス(M-SCAN-005、仕様 §2.1 手順 1〜6)。
/// 画像ファイル本体へは一切書き込まない(INV-009)。遷移は仕様 §2.1 遷移表のみ。
/// スキャン二重起動(同一フォルダ)は <see cref="ErrorCode.ScanInProgress"/> で拒否。
/// アプリ全体で単一インスタンスとして使用する(二重起動ガードの前提)。
/// </summary>
public sealed class ScanService
{
    /// <summary>
    /// ECO-059: SQLite commit回数と未flush変更量をともに有界にする。
    /// ファイル読取はHDDのランダムseekを増やさないよう直列のまま維持する。
    /// </summary>
    private const int ScanBatchSize = 512;

    /// <summary>対象拡張子(小文字化して比較、REQ-011)。</summary>
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.Ordinal)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp",
    };

    private readonly ISyncFolderRepository _folders;
    private readonly IImageRepository _images;
    private readonly IClock _clock;
    private readonly ILogger<ScanService>? _logger;
    private readonly ScanJudge _judge = new();
    private readonly ConcurrentDictionary<string, byte> _inFlight = new(StringComparer.Ordinal);

    public ScanService(
        ISyncFolderRepository folders,
        IImageRepository images,
        IClock clock,
        ILogger<ScanService>? logger = null)
    {
        _folders = folders;
        _images = images;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<ScanSummary>> ScanAsync(
        string folderId,
        IProgress<int>? progress,
        CancellationToken ct,
        IProgress<ScanBatchCommitted>? committed = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(folderId);

        // 実行中は同一フォルダの再スキャン要求を拒否(仕様 §2.1)
        if (!_inFlight.TryAdd(folderId, 0))
        {
            return Result<ScanSummary>.Fail(ErrorCode.ScanInProgress, "このフォルダはスキャン実行中です。");
        }

        try
        {
            var folder = await _folders.GetByIdAsync(folderId).ConfigureAwait(false);
            if (folder is null)
            {
                return Result<ScanSummary>.Fail(ErrorCode.NotFound, "同期フォルダが存在しません。");
            }

            if (!folder.IsActive)
            {
                // is_active=false のフォルダはスキャン対象外(REQ-010)
                return Result<ScanSummary>.Fail(ErrorCode.ValidationError, "無効化されたフォルダはスキャンできません。");
            }

            try
            {
                // ECO-059/GF-059-01: Microsoft.Data.Sqlite の async API は同期完了し得る。
                // ConfigureAwait(false) だけでは呼出元(UI)threadから離れないため、列挙・hash・
                // DB batchを明示的にbackgroundへ移す。Progress<T>は生成元UI contextへpostする。
                return await Task.Run(
                    () => ScanCoreAsync(folder, progress, committed, ct),
                    ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
            {
                _logger?.LogWarning(ex, "スキャンが I/O エラーで中断しました: {FolderId}", folderId);
                return Result<ScanSummary>.Fail(ErrorCode.IoError, ex.Message);
            }
            finally
            {
                // 完了時(例外時も)last_scan を現在時刻に更新(REQ-015 手順 6)
                await _folders.UpdateLastScanAsync(folderId, _clock.UtcNowIso()).ConfigureAwait(false);
            }
        }
        finally
        {
            _inFlight.TryRemove(folderId, out _);
        }
    }

    /// <summary>
    /// 差分計算(ECO-130/REQ-100・再スキャン用): 手順 1〜5 の判定を DB 完全無変更で実行し、
    /// 変更案を遷移別に集計して返す。last_scan も更新しない(適用/破棄前の観測のみ)。
    /// キャンセルは OperationCanceledException(DB 無変更のため任意時点で安全)。
    /// <paramref name="processed"/> は処理済みファイル件数の通知(percent は捏造しない=ECO-060 規律)。
    /// </summary>
    public async Task<Result<ScanStaging>> StageAsync(
        string folderId, IProgress<int>? processed, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(folderId);

        if (!_inFlight.TryAdd(folderId, 0))
        {
            return Result<ScanStaging>.Fail(ErrorCode.ScanInProgress, "このフォルダはスキャン実行中です。");
        }

        try
        {
            var folder = await _folders.GetByIdAsync(folderId).ConfigureAwait(false);
            if (folder is null)
            {
                return Result<ScanStaging>.Fail(ErrorCode.NotFound, "同期フォルダが存在しません。");
            }

            if (!folder.IsActive)
            {
                return Result<ScanStaging>.Fail(ErrorCode.ValidationError, "無効化されたフォルダはスキャンできません。");
            }

            try
            {
                // ECO-059/GF-059-01 と同じ理由で列挙・hash を background へ明示分離する
                return await Task.Run(() => StageCoreAsync(folder, processed, ct), ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
            {
                _logger?.LogWarning(ex, "差分計算が I/O エラーで中断しました: {FolderId}", folderId);
                return Result<ScanStaging>.Fail(ErrorCode.IoError, ex.Message);
            }

            // 注: ScanAsync と異なり last_scan は更新しない(REQ-100。破棄で無かったことにできる)
        }
        finally
        {
            _inFlight.TryRemove(folderId, out _);
        }
    }

    private async Task<Result<ScanStaging>> StageCoreAsync(
        SyncFolder folder, IProgress<int>? processed, CancellationToken ct)
    {
        var root = folder.Path;
        if (!Directory.Exists(root))
        {
            return Result<ScanStaging>.Fail(ErrorCode.IoError, $"フォルダ '{root}' にアクセスできません。");
        }

        var enumerable = CreateEnumerable(folder, root);
        var existing = await _images.GetByFolderAsync(folder.Id).ConfigureAwait(false);
        var isInitialScan = folder.LastScan is null;

        var statusUpdates = new List<ScanStatusUpdate>();
        var deletes = new List<string>();
        var adds = new List<ImageRecord>();
        var metaUpdates = new List<ScanFileMetaUpdate>();
        var examples = new List<ScanTransitionExample>();
        var exampleCounts = new Dictionary<ScanTransitionKind, int>();
        void AddExample(ScanTransitionKind kind, string relativePath)
        {
            exampleCounts.TryGetValue(kind, out var n);
            if (n < ScanStaging.ExamplesPerKind)
            {
                examples.Add(new ScanTransitionExample(kind, relativePath));
                exampleCounts[kind] = n + 1;
            }
        }

        int missingFromNormal = 0, missingFromPending = 0;
        var examined = 0; // 処理済み件数(DB 行の実在確認+ファイル判定の合算。SC-1 の表示単位)

        // 手順 4・5 相当(仕様 §2.1 実行順序・v5.0): DB へは書かず、変更案とメモリ上の判定用ビューを作る
        var current = new List<ImageRecord>(existing.Count);
        foreach (var record in existing)
        {
            ct.ThrowIfCancellationRequested();
            examined++;
            if (examined % ProcessedReportInterval == 0)
            {
                processed?.Report(examined);
            }

            var fullPath = Path.Combine(root, record.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (record.Status is ImageStatus.Normal or ImageStatus.Pending && !File.Exists(fullPath))
            {
                statusUpdates.Add(new ScanStatusUpdate(record.Id, ImageStatus.Missing));
                current.Add(record with
                {
                    Status = ImageStatus.Missing,
                    CandidateLinkId = null,
                    PendingOrigin = null,
                });
                if (record.Status == ImageStatus.Normal)
                {
                    missingFromNormal++;
                    AddExample(ScanTransitionKind.MissingFromNormal, record.RelativePath);
                }
                else
                {
                    missingFromPending++;
                    AddExample(ScanTransitionKind.MissingFromPending, record.RelativePath);
                }
            }
            else
            {
                current.Add(record);
            }
        }

        var byPath = current.ToDictionary(i => i.RelativePath, StringComparer.OrdinalIgnoreCase);
        var missingInFolder = current.Where(i => i.Status == ImageStatus.Missing).ToList();

        int unchanged = 0, contentChanged = 0, reappeared = 0, readFailures = 0;
        int deletedUnchanged = 0, deletedMetaRefreshed = 0, pendedWithoutMeta = 0;
        var addedPending = 0;
        var scannedFiles = 0;

        // 手順 2〜3 相当: 判定のみ(OC-5 の器は不変)。DB へは書かない
        foreach (var file in enumerable)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var relativePath = PathNormalizer.ToRelative(root, file.FullPath);
                var facts = new ScanFileFacts(
                    relativePath,
                    file.Length,
                    IsoTimestamp.Format(file.LastWriteTimeUtc),
                    IsoTimestamp.Format(file.CreationTimeUtc),
                    () => ComputeHash(file.FullPath));
                var dbFacts = new ScanDbFacts(
                    byPath.TryGetValue(relativePath, out var row) ? row : null,
                    missingInFolder);

                var decision = _judge.Judge(facts, dbFacts, isInitialScan);
                switch (decision.Kind)
                {
                    case ScanDecisionKind.Skip:
                        // deleted 行への一致は再登録しない(従来どおり)。サマリーでは除外として別計上(REQ-100)
                        if (row!.Status == ImageStatus.Deleted)
                        {
                            deletedUnchanged++;
                        }
                        else
                        {
                            unchanged++;
                        }

                        break;

                    case ScanDecisionKind.UpdateMeta:
                        // pending 起点=メタ追随(内容変更に計上)/deleted 起点=除外(メタ更新のみ適用)
                        metaUpdates.Add(new ScanFileMetaUpdate(row!.Id, decision.Hash!, file.Length, facts.ModifiedDate));
                        if (row.Status == ImageStatus.Deleted)
                        {
                            deletedMetaRefreshed++;
                        }
                        else
                        {
                            contentChanged++;
                            AddExample(ScanTransitionKind.ContentChanged, relativePath);
                        }

                        break;

                    case ScanDecisionKind.UpdateMetaAndPend:
                        metaUpdates.Add(new ScanFileMetaUpdate(row!.Id, decision.Hash!, file.Length, facts.ModifiedDate));
                        statusUpdates.Add(new ScanStatusUpdate(row.Id, ImageStatus.Pending, decision.PendingOrigin));
                        if (decision.PendingOrigin == PendingOrigin.Reappeared)
                        {
                            reappeared++;
                            AddExample(ScanTransitionKind.Reappeared, relativePath);
                        }
                        else
                        {
                            contentChanged++;
                            AddExample(ScanTransitionKind.ContentChanged, relativePath);
                        }

                        break;

                    case ScanDecisionKind.PendInPlace:
                        statusUpdates.Add(new ScanStatusUpdate(row!.Id, ImageStatus.Pending, decision.PendingOrigin));
                        reappeared++;
                        pendedWithoutMeta++; // R8 所見5: 一段階の updated++ と適用後サマリーを揃える
                        AddExample(ScanTransitionKind.Reappeared, relativePath);
                        break;

                    case ScanDecisionKind.AddNormal:
                        // R8 所見6: 再スキャン専用のステージングでは到達しない(規則 3-初回= isInitialScan のみ)。
                        // 「normal を挿入しつつ pending と表示」する自己矛盾を作らないため明示的に封じる
                        throw new InvalidOperationException(
                            "StageAsync は再スキャン専用です(初回スキャンは ScanAsync=REQ-086/SCAN-001)。");

                    case ScanDecisionKind.AddPending:
                    default:
                        adds.Add(NewRecord(folder.Id, facts, decision, ImageStatus.Pending));
                        addedPending++;
                        AddExample(ScanTransitionKind.AddedPending, relativePath);
                        break;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 読み取り不能は変更に数えない(REQ-100)。DB 非変更・次回スキャンで再試行
                readFailures++;
                _logger?.LogWarning(ex, "読み取り不能ファイルをスキップしました: {Path}", file.FullPath);
            }

            scannedFiles++;
            examined++;
            if (examined % ProcessedReportInterval == 0)
            {
                processed?.Report(examined);
            }
        }

        processed?.Report(examined);

        return Result<ScanStaging>.Ok(new ScanStaging
        {
            FolderId = folder.Id,
            ManagedTotal = existing.Count,
            ScannedFiles = scannedFiles,
            Unchanged = unchanged,
            ContentChanged = contentChanged,
            AddedPending = addedPending,
            Reappeared = reappeared,
            MissingFromNormal = missingFromNormal,
            MissingFromPending = missingFromPending,
            DeletedUnchanged = deletedUnchanged,
            DeletedMetaRefreshed = deletedMetaRefreshed,
            PendedWithoutMeta = pendedWithoutMeta,
            ReadFailures = readFailures,
            Adds = adds,
            MetaUpdates = metaUpdates,
            StatusUpdates = statusUpdates,
            Deletes = deletes,
            Examples = examples,
        });
    }

    /// <summary>
    /// ステージング適用(ECO-130/REQ-100): 変更案を有界バッチで一括反映し last_scan を更新する。
    /// 適用後状態は ScanAsync 直接実行と同値(パリティ=判定を再解釈しない)。
    /// 段階的公開(committed)は行わない(初回スキャン専用の契約 — REQ-086)。
    /// 部分適用を作らないため、適用開始後はキャンセルを受け付けない(ct は開始前のみ確認)。
    /// </summary>
    public async Task<Result<ScanSummary>> ApplyStagedAsync(
        ScanStaging staging, IProgress<int>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(staging);
        ct.ThrowIfCancellationRequested();

        if (!_inFlight.TryAdd(staging.FolderId, 0))
        {
            return Result<ScanSummary>.Fail(ErrorCode.ScanInProgress, "このフォルダはスキャン実行中です。");
        }

        try
        {
            var folder = await _folders.GetByIdAsync(staging.FolderId).ConfigureAwait(false);
            if (folder is null)
            {
                return Result<ScanSummary>.Fail(ErrorCode.NotFound, "同期フォルダが存在しません。");
            }

            // GF-059-01 と同じ理由で DB バッチ適用を UI thread から明示分離する
            // (sqlite の async は同期完了し得る=ConfigureAwait(false) だけでは離れない)
            return await Task.Run(() => ApplyCoreAsync(staging, progress)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // R8 所見4: 適用中の失敗を throw で漏らさない(VM が失敗理由表示+Summary 復帰できる形で返す)。
            // バッチは 512 件単位の独立トランザクションのため部分適用があり得る(REQ-100 に明記・
            // 残余は次回スキャンの差分計算が収束させる)
            _logger?.LogError(ex, "ステージング適用が失敗しました: {FolderId}", staging.FolderId);
            return Result<ScanSummary>.Fail(ErrorCode.IoError, ex.Message);
        }
        finally
        {
            _inFlight.TryRemove(staging.FolderId, out _);
        }
    }

    private async Task<Result<ScanSummary>> ApplyCoreAsync(ScanStaging staging, IProgress<int>? progress)
    {
        {
            // 有界バッチで一括適用。順序は一段階スキャンと同じ(手順 4/5 → 手順 3 の変更)
            var total = staging.StatusUpdates.Count + staging.Deletes.Count
                + staging.MetaUpdates.Count + staging.Adds.Count;
            var applied = 0;
            var lastPercent = -1;
            void Report(int count)
            {
                applied += count;
                if (progress is null || total == 0)
                {
                    return;
                }

                var percent = (int)(applied * 100L / total);
                if (percent != lastPercent)
                {
                    lastPercent = percent;
                    progress.Report(percent);
                }
            }

            foreach (var chunk in Chunk(staging.StatusUpdates, ScanBatchSize))
            {
                await _images.ApplyScanBatchAsync(new ScanMutationBatch([], [], chunk, [])).ConfigureAwait(false);
                Report(chunk.Length);
            }

            foreach (var chunk in Chunk(staging.Deletes, ScanBatchSize))
            {
                await _images.ApplyScanBatchAsync(new ScanMutationBatch([], [], [], chunk)).ConfigureAwait(false);
                Report(chunk.Length);
            }

            foreach (var chunk in Chunk(staging.MetaUpdates, ScanBatchSize))
            {
                await _images.ApplyScanBatchAsync(new ScanMutationBatch([], chunk, [], [])).ConfigureAwait(false);
                Report(chunk.Length);
            }

            foreach (var chunk in Chunk(staging.Adds, ScanBatchSize))
            {
                await _images.ApplyScanBatchAsync(new ScanMutationBatch(chunk, [], [], [])).ConfigureAwait(false);
                Report(chunk.Length);
            }

            await _folders.UpdateLastScanAsync(staging.FolderId, _clock.UtcNowIso()).ConfigureAwait(false);

            // R8 所見5: 一段階スキャンの計上規則と一致させる(updated= メタ更新+PendInPlace/
            // skipped= 変更なし+deleted 規則1+読み取り失敗。deleted 規則2 は updated 側のみ=二重計上しない)
            return Result<ScanSummary>.Ok(new ScanSummary
            {
                Added = 0, // 再スキャン専用(normal 登録は初回のみ=REQ-101)
                Missing = staging.MissingFromNormal + staging.MissingFromPending,
                Pending = staging.AddedPending,
                Updated = staging.MetaUpdates.Count + staging.PendedWithoutMeta,
                Skipped = staging.Unchanged + staging.DeletedUnchanged + staging.ReadFailures,
            });
        }
    }

    private const int ProcessedReportInterval = 256;

    private static IEnumerable<T[]> Chunk<T>(IReadOnlyList<T> source, int size)
    {
        for (var offset = 0; offset < source.Count; offset += size)
        {
            var length = Math.Min(size, source.Count - offset);
            var chunk = new T[length];
            for (var i = 0; i < length; i++)
            {
                chunk[i] = source[offset + i];
            }

            yield return chunk;
        }
    }

    private async Task<Result<ScanSummary>> ScanCoreAsync(
        SyncFolder folder,
        IProgress<int>? progress,
        IProgress<ScanBatchCommitted>? committed,
        CancellationToken ct)
    {
        var root = folder.Path;
        if (!Directory.Exists(root))
        {
            // ルート消失(ドライブ未接続等)は手順 4 の一括 missing 化をせず中断する(誤判定防止)
            return Result<ScanSummary>.Fail(ErrorCode.IoError, $"フォルダ '{root}' にアクセスできません。");
        }

        // 手順 1: 列挙(K-WINFS 規約: リパースポイントを辿らない・アクセス不能は無視)
        var enumerable = CreateEnumerable(folder, root);
        var existing = await _images.GetByFolderAsync(folder.Id).ConfigureAwait(false);
        var isInitialScan = folder.LastScan is null;

        int added = 0, missing = 0, pending = 0, updated = 0, skipped = 0;
        var batch = new ScanBatchBuffer(_images, folder.Id, committed, ScanBatchSize);

        // 手順 4: status=normal で物理ファイルが存在しない行 → missing
        // 手順 5(v5.0=ECO-129): 物理ファイルが存在しない status=pending の行 → missing
        //   (行削除を廃止=タグを持ち得る pending の保全。candidate/origin はクリア=適用側の契約)
        // 注: 判定(手順 3)より先に適用する。リネーム(旧ファイル消失+新ファイル出現)を単一スキャンで
        //     missing+pending にするため(FMEA-004 / 遷移表)。判定規則 3-再は missing 化後の状態を参照する。
        var current = new List<ImageRecord>(existing.Count);
        foreach (var record in existing)
        {
            ct.ThrowIfCancellationRequested();
            var fullPath = Path.Combine(root, record.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (record.Status is ImageStatus.Normal or ImageStatus.Pending && !File.Exists(fullPath))
            {
                batch.AddStatus(record.Id, ImageStatus.Missing);
                current.Add(record with
                {
                    Status = ImageStatus.Missing,
                    CandidateLinkId = null,
                    PendingOrigin = null,
                });
                missing++;
            }
            else
            {
                current.Add(record);
            }

            await batch.FlushIfFullAsync().ConfigureAwait(false);
        }

        // missing化を新規ファイル判定より先にDBへ反映する(ECO-001の実行順序を維持)。
        await batch.FlushAsync().ConfigureAwait(false);

        var byPath = current.ToDictionary(i => i.RelativePath, StringComparer.OrdinalIgnoreCase);
        var missingInFolder = current.Where(i => i.Status == ImageStatus.Missing).ToList();

        // 手順 2〜3: 相対パス算出と優先順判定(OC-5)
        var processed = 0;
        foreach (var file in enumerable)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var relativePath = PathNormalizer.ToRelative(root, file.FullPath);
                var facts = new ScanFileFacts(
                    relativePath,
                    file.Length,
                    IsoTimestamp.Format(file.LastWriteTimeUtc),
                    IsoTimestamp.Format(file.CreationTimeUtc),
                    () => ComputeHash(file.FullPath));
                var dbFacts = new ScanDbFacts(
                    byPath.TryGetValue(relativePath, out var row) ? row : null,
                    missingInFolder);

                var decision = _judge.Judge(facts, dbFacts, isInitialScan);
                switch (decision.Kind)
                {
                    case ScanDecisionKind.Skip:
                        skipped++;
                        break;

                    case ScanDecisionKind.UpdateMeta:
                        batch.AddFileMeta(row!.Id, decision.Hash!, file.Length, facts.ModifiedDate);
                        updated++;
                        break;

                    case ScanDecisionKind.UpdateMetaAndPend:
                        // v5.0: 内容変更(normal 起点)/再出現(missing 起点)= メタ更新+pending 化
                        batch.AddFileMeta(row!.Id, decision.Hash!, file.Length, facts.ModifiedDate);
                        batch.AddStatus(row.Id, ImageStatus.Pending, decision.PendingOrigin);
                        updated++;
                        break;

                    case ScanDecisionKind.PendInPlace:
                        // v5.0: missing 起点・メタ一致の再出現= pending 化のみ
                        batch.AddStatus(row!.Id, ImageStatus.Pending, decision.PendingOrigin);
                        updated++;
                        break;

                    case ScanDecisionKind.AddNormal:
                        batch.Add(NewRecord(folder.Id, facts, decision, ImageStatus.Normal));
                        added++;
                        break;

                    case ScanDecisionKind.AddPending:
                    default:
                        batch.Add(NewRecord(folder.Id, facts, decision, ImageStatus.Pending));
                        pending++;
                        break;
                }

                await batch.FlushIfFullAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 読み取り不能ファイルは DB を変更せずスキップ+警告ログ。次回スキャンで再試行(REQ-012)
                skipped++;
                _logger?.LogWarning(ex, "読み取り不能ファイルをスキップしました: {Path}", file.FullPath);
            }

            processed++;
        }

        await batch.FlushAsync().ConfigureAwait(false);
        if (processed > 0)
        {
            progress?.Report(100);
        }

        return Result<ScanSummary>.Ok(new ScanSummary
        {
            Added = added,
            Missing = missing,
            Pending = pending,
            Updated = updated,
            Skipped = skipped,
        });
    }

    /// <summary>
    /// 手順 1 の列挙(K-WINFS: リパースポイントを辿らない・アクセス不能は無視)。
    /// ECO-059: 列挙時にOSが既に返しているサイズ/日時を保持し、各ファイルのFileInfo再照会を避ける。
    /// ECO-060: streaming 列挙(全件 ToList しない)。ECO-130: 一段階(ScanCoreAsync)と差分計算
    /// (StageCoreAsync)で同一の列挙器を共有する(判定パリティの前提)。
    /// </summary>
    private static FileSystemEnumerable<ScanFileEntry> CreateEnumerable(SyncFolder folder, string root)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = folder.IncludeSubfolders,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };
        var excludeNames = new HashSet<string>(folder.ExcludePatterns, StringComparer.OrdinalIgnoreCase);
        return new FileSystemEnumerable<ScanFileEntry>(
            root,
            (ref FileSystemEntry entry) => new ScanFileEntry(
                entry.ToFullPath(),
                entry.Length,
                entry.LastWriteTimeUtc.UtcDateTime,
                entry.CreationTimeUtc.UtcDateTime),
            options)
        {
            ShouldIncludePredicate = (ref FileSystemEntry entry) =>
                !entry.IsDirectory &&
                ImageExtensions.Contains(Path.GetExtension(entry.FileName).ToString().ToLowerInvariant()) &&
                !excludeNames.Contains(entry.FileName.ToString()),
        };
    }

    private static ImageRecord NewRecord(
        string folderId, ScanFileFacts facts, ScanDecision decision, ImageStatus status)
    {
        return new ImageRecord
        {
            Id = IdGenerator.NewId(),
            SyncFolderId = folderId,
            RelativePath = facts.RelativePath,
            FileName = facts.RelativePath[(facts.RelativePath.LastIndexOf('/') + 1)..],
            FileSize = facts.FileSize,
            Hash = decision.Hash!,
            Status = status,
            CandidateLinkId = decision.CandidateLinkId,
            PendingOrigin = decision.PendingOrigin,
            CreatedDate = facts.CreatedDate,
            ModifiedDate = facts.ModifiedDate,
        };
    }

    private static string ComputeHash(string path)
    {
        // K-WINFS: 他プロセスのロックと共存する読み取り。INV-009: 読み取り専用
        // ECO-059: HDDの連続読取をOSへ明示し、64KiBバッファで細粒度readを抑える。
        using var stream = new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.ReadWrite | FileShare.Delete,
            BufferSize = 64 * 1024,
            Options = FileOptions.SequentialScan,
        });
        return FileHasher.ComputeSha256(stream);
    }

    private sealed record ScanFileEntry(
        string FullPath,
        long Length,
        DateTime LastWriteTimeUtc,
        DateTime CreationTimeUtc);

    /// <summary>ECO-059: ScanService内だけで使う有界ミューテーションバッファ。</summary>
    private sealed class ScanBatchBuffer
    {
        private readonly IImageRepository _images;
        private readonly string _folderId;
        private readonly IProgress<ScanBatchCommitted>? _committed;
        private readonly int _capacity;
        private readonly List<ImageRecord> _adds = [];
        private readonly List<ScanFileMetaUpdate> _fileMetaUpdates = [];
        private readonly List<ScanStatusUpdate> _statusUpdates = [];
        private readonly List<string> _deletes = [];

        public ScanBatchBuffer(
            IImageRepository images,
            string folderId,
            IProgress<ScanBatchCommitted>? committed,
            int capacity)
        {
            _images = images;
            _folderId = folderId;
            _committed = committed;
            _capacity = capacity;
        }

        private int Count => _adds.Count + _fileMetaUpdates.Count + _statusUpdates.Count + _deletes.Count;

        public void Add(ImageRecord image) => _adds.Add(image);

        public void AddFileMeta(string id, string hash, long fileSize, string modifiedDate)
            => _fileMetaUpdates.Add(new ScanFileMetaUpdate(id, hash, fileSize, modifiedDate));

        public void AddStatus(string id, ImageStatus status, PendingOrigin? origin = null)
            => _statusUpdates.Add(new ScanStatusUpdate(id, status, origin));

        public void AddDelete(string id) => _deletes.Add(id);

        public Task FlushIfFullAsync() => Count >= _capacity ? FlushAsync() : Task.CompletedTask;

        public async Task FlushAsync()
        {
            if (Count == 0)
            {
                return;
            }

            var mutations = new ScanMutationBatch(
                _adds.ToArray(),
                _fileMetaUpdates.ToArray(),
                _statusUpdates.ToArray(),
                _deletes.ToArray());
            var publishable = _adds
                .Where(image => image.Status == ImageStatus.Normal)
                .ToArray();
            await _images.ApplyScanBatchAsync(mutations).ConfigureAwait(false);
            if (publishable.Length > 0)
            {
                // 唯一の公開境界: repository transactionが成功してから通知する。
                _committed?.Report(new ScanBatchCommitted(_folderId, publishable));
            }
            _adds.Clear();
            _fileMetaUpdates.Clear();
            _statusUpdates.Clear();
            _deletes.Clear();
        }
    }
}
