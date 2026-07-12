using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using ViewPrism2.Core.Common;

namespace ViewPrism2.Infrastructure.Database;

/// <summary>復元ブートストラップの結果種別。</summary>
public enum SnapshotRestoreStatus
{
    /// <summary>予約なし(通常起動)。</summary>
    NotRequested,

    /// <summary>差し替え成功(この後の DatabaseManager.Open が未適用 migration を前進適用する)。</summary>
    Applied,

    /// <summary>失敗。現行 DB のまま(必要なら退避から巻き戻し済み)起動を継続する。</summary>
    Failed,
}

public sealed record SnapshotRestoreResult(SnapshotRestoreStatus Status, string? Message = null, string? SnapshotPath = null);

/// <summary>
/// 時点復元の実適用(ECO-072 案A)。アプリ起動時、DB 接続確立**前**に呼ぶ。
/// 予約(restore-pending.json)は読んだ時点で削除する(一回限り — 失敗時に再試行ループしない)。
/// 手順: 予約読取→スナップショット検証(integrity_check/foreign_key_check/未知 migration 拒否)→
/// 現行 DB の安全スナップショット作成→現行退避→差し替え→差し替え先検証→失敗時は退避へ巻き戻し。
/// 全接続は Pooling=False で開き、ファイル差し替え前にハンドルを残さない。
/// </summary>
public static class SnapshotRestoreBootstrap
{
    public static SnapshotRestoreResult Apply(string appDataDir, string? appVersion = null, string dbFileName = "viewprism2.db")
    {
        ArgumentException.ThrowIfNullOrEmpty(appDataDir);
        var pendingPath = Path.Combine(appDataDir, SnapshotService.PendingRestoreFileName);
        if (!File.Exists(pendingPath))
        {
            return new SnapshotRestoreResult(SnapshotRestoreStatus.NotRequested);
        }

        PendingRestore? pending;
        try
        {
            pending = JsonSerializer.Deserialize<PendingRestore>(File.ReadAllText(pendingPath), SnapshotJson.Options);
        }
        catch (JsonException ex)
        {
            SnapshotService.TryDelete(pendingPath);
            return new SnapshotRestoreResult(SnapshotRestoreStatus.Failed, $"予約ファイルが不正です: {ex.Message}");
        }

        SnapshotService.TryDelete(pendingPath); // 一回限り(失敗しても次回起動は通常起動)
        if (pending is null || string.IsNullOrEmpty(pending.SnapshotPath))
        {
            return new SnapshotRestoreResult(SnapshotRestoreStatus.Failed, "予約ファイルが不正です。");
        }

        var validation = SnapshotService.ValidateSnapshotFile(pending.SnapshotPath, DatabaseSchema.Migrations);
        if (!validation.IsSuccess)
        {
            return new SnapshotRestoreResult(SnapshotRestoreStatus.Failed, validation.Message, pending.SnapshotPath);
        }

        var dbPath = Path.Combine(appDataDir, dbFileName);
        var retiredPath = dbPath + ".pre-restore";
        try
        {
            if (File.Exists(dbPath))
            {
                // 現行 DB の安全スナップショット(巻き戻しのさらに前の状態を保全)
                var safety = CreateSafetySnapshot(dbPath, pending.SafetyDirectory, appVersion);
                if (!safety.IsSuccess)
                {
                    return new SnapshotRestoreResult(
                        SnapshotRestoreStatus.Failed, $"安全スナップショットの作成に失敗: {safety.Message}", pending.SnapshotPath);
                }

                File.Delete(retiredPath);
                File.Move(dbPath, retiredPath);
                SnapshotService.TryDelete(dbPath + "-wal");
                SnapshotService.TryDelete(dbPath + "-shm");
            }

            File.Copy(pending.SnapshotPath, dbPath, overwrite: false);
            var verify = SnapshotService.VerifyDatabaseFile(dbPath, quick: true);
            if (!verify.IsSuccess)
            {
                Rollback(dbPath, retiredPath);
                return new SnapshotRestoreResult(
                    SnapshotRestoreStatus.Failed, $"差し替え後の検証に失敗(退避 DB へ戻しました): {verify.Message}", pending.SnapshotPath);
            }

            SnapshotService.TryDelete(retiredPath);
            return new SnapshotRestoreResult(SnapshotRestoreStatus.Applied, SnapshotPath: pending.SnapshotPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException)
        {
            Rollback(dbPath, retiredPath);
            return new SnapshotRestoreResult(
                SnapshotRestoreStatus.Failed, $"復元に失敗(現行の状態で起動します): {ex.Message}", pending.SnapshotPath);
        }
    }

    /// <summary>共有接続の外(起動前)で現行 DB のスナップショットを作る。SnapshotService.CreateAsync と同じ .partial→検証→リネーム契約。</summary>
    private static Result CreateSafetySnapshot(string dbPath, string directory, string? appVersion)
    {
        var createdAt = DateTime.UtcNow;
        var baseName = SnapshotService.SafetyFilePrefix +
            createdAt.ToString("yyyyMMdd-HHmmssfff", System.Globalization.CultureInfo.InvariantCulture);
        var partialPath = Path.Combine(directory, baseName + SnapshotService.PartialExtension);
        try
        {
            Directory.CreateDirectory(directory);
            File.Delete(partialPath);
            using (var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
            {
                conn.Open();
                conn.Execute("VACUUM INTO @path", new { path = partialPath });
            }

            var verify = SnapshotService.VerifyDatabaseFile(partialPath, quick: true);
            if (!verify.IsSuccess)
            {
                SnapshotService.TryDelete(partialPath);
                return verify;
            }

            string checksum;
            using (var stream = File.OpenRead(partialPath))
            {
                checksum = FileHasher.ComputeSha256(stream);
            }

            var finalPath = Path.Combine(directory, baseName + ".db");
            File.Move(partialPath, finalPath);
            SnapshotService.WriteMeta(
                finalPath,
                new SnapshotMeta(appVersion, IsoTimestamp.Format(createdAt), checksum, new FileInfo(finalPath).Length));
            return Result.Ok();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException)
        {
            SnapshotService.TryDelete(partialPath);
            return Result.Fail(ErrorCode.IoError, ex.Message);
        }
    }

    /// <summary>差し替え失敗時: 差し替え先を捨てて退避 DB を戻す。</summary>
    private static void Rollback(string dbPath, string retiredPath)
    {
        if (!File.Exists(retiredPath))
        {
            return;
        }

        SnapshotService.TryDelete(dbPath);
        try
        {
            File.Move(retiredPath, dbPath);
        }
        catch (IOException)
        {
            // 戻せない場合も退避ファイル自体は残る(手動復旧可能な状態を維持)
        }
    }
}
