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
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = folder.IncludeSubfolders,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };
        var excludeNames = new HashSet<string>(folder.ExcludePatterns, StringComparer.OrdinalIgnoreCase);
        // ECO-059: 列挙時にOSが既に返しているサイズ/日時を保持し、各ファイルのFileInfo再照会を避ける。
        // ECO-060: 全件ToList後の処理では最初の公開が列挙完了まで遅れるため、列挙結果をそのまま
        // 直列hash処理へ流す。総件数不明の間はpercentを捏造せず、完了時100だけを通知する。
        var enumerable = new FileSystemEnumerable<ScanFileEntry>(
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
        var existing = await _images.GetByFolderAsync(folder.Id).ConfigureAwait(false);
        var isInitialScan = folder.LastScan is null;

        int added = 0, missing = 0, pending = 0, updated = 0, skipped = 0;
        var batch = new ScanBatchBuffer(_images, folder.Id, committed, ScanBatchSize);

        // 手順 4: status=normal で物理ファイルが存在しない行 → missing
        // 手順 5: 物理ファイルが存在しない status=pending の行 → 行削除
        // 注: 判定(手順 3)より先に適用する。リネーム(旧ファイル消失+新ファイル出現)を単一スキャンで
        //     missing+pending にするため(FMEA-004 / 遷移表)。判定規則 3a は missing 化後の状態を参照する。
        var current = new List<ImageRecord>(existing.Count);
        foreach (var record in existing)
        {
            ct.ThrowIfCancellationRequested();
            var fullPath = Path.Combine(root, record.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (record.Status == ImageStatus.Normal && !File.Exists(fullPath))
            {
                batch.AddStatus(record.Id, ImageStatus.Missing);
                current.Add(record with { Status = ImageStatus.Missing });
                missing++;
            }
            else if (record.Status == ImageStatus.Pending && !File.Exists(fullPath))
            {
                batch.AddDelete(record.Id); // 候補が消えたため
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

        public void AddStatus(string id, ImageStatus status)
            => _statusUpdates.Add(new ScanStatusUpdate(id, status));

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
