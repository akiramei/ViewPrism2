using System.Collections.Concurrent;
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
        string folderId, IProgress<int>? progress, CancellationToken ct)
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
                return await ScanCoreAsync(folder, progress, ct).ConfigureAwait(false);
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
        SyncFolder folder, IProgress<int>? progress, CancellationToken ct)
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
        var files = Directory.EnumerateFiles(root, "*", options)
            .Where(path => ImageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()))
            .Where(path => !excludeNames.Contains(Path.GetFileName(path)))
            .ToList();

        var existing = await _images.GetByFolderAsync(folder.Id).ConfigureAwait(false);
        var isInitialScan = folder.LastScan is null;

        int added = 0, missing = 0, pending = 0, updated = 0, skipped = 0;

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
                await _images.UpdateStatusAsync(record.Id, ImageStatus.Missing).ConfigureAwait(false);
                current.Add(record with { Status = ImageStatus.Missing });
                missing++;
            }
            else if (record.Status == ImageStatus.Pending && !File.Exists(fullPath))
            {
                await _images.DeleteAsync(record.Id).ConfigureAwait(false); // 候補が消えたため
            }
            else
            {
                current.Add(record);
            }
        }

        var byPath = current.ToDictionary(i => i.RelativePath, StringComparer.OrdinalIgnoreCase);
        var missingInFolder = current.Where(i => i.Status == ImageStatus.Missing).ToList();

        // 手順 2〜3: 相対パス算出と優先順判定(OC-5)
        var processed = 0;
        foreach (var path in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var info = new FileInfo(path);
                var relativePath = PathNormalizer.ToRelative(root, path);
                var facts = new ScanFileFacts(
                    relativePath,
                    info.Length,
                    IsoTimestamp.Format(info.LastWriteTimeUtc),
                    IsoTimestamp.Format(info.CreationTimeUtc),
                    () => ComputeHash(path));
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
                        await _images.UpdateFileMetaAsync(row!.Id, decision.Hash!, info.Length, facts.ModifiedDate)
                            .ConfigureAwait(false);
                        updated++;
                        break;

                    case ScanDecisionKind.AddNormal:
                        await _images.AddAsync(NewRecord(folder.Id, facts, decision, ImageStatus.Normal))
                            .ConfigureAwait(false);
                        added++;
                        break;

                    case ScanDecisionKind.AddPending:
                    default:
                        await _images.AddAsync(NewRecord(folder.Id, facts, decision, ImageStatus.Pending))
                            .ConfigureAwait(false);
                        pending++;
                        break;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 読み取り不能ファイルは DB を変更せずスキップ+警告ログ。次回スキャンで再試行(REQ-012)
                skipped++;
                _logger?.LogWarning(ex, "読み取り不能ファイルをスキップしました: {Path}", path);
            }

            processed++;
            progress?.Report(processed * 100 / files.Count);
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
        using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return FileHasher.ComputeSha256(stream);
    }
}
