using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using ViewPrism2.Core.Common;

namespace ViewPrism2.Infrastructure.Database;

/// <summary>作成済みスナップショット 1 件(ECO-072 A-1 一覧行)。</summary>
public sealed record SnapshotInfo(
    string FilePath,
    DateTime CreatedAtUtc,
    string? AppVersion,
    long SizeBytes,
    bool IsVerified);

/// <summary>作成中進捗の件数表示(CAD A-1「タグ N / ビュー N / メタデータ N 件」)。</summary>
public sealed record SnapshotCounts(long Tags, long Views, long Images);

/// <summary>スナップショットのサイドカーメタデータ(<c>*.db.meta.json</c>)。</summary>
public sealed record SnapshotMeta(string? AppVersion, string CreatedAt, string ChecksumSha256, long SizeBytes);

/// <summary>
/// カタログ DB スナップショットの作成・一覧・復元前検証・復元予約(ECO-072 案A)。
/// 作成は VACUUM INTO を .partial へ出力→quick_check/foreign_key_check→SHA-256→
/// アトミックリネームの順で行い、中断・検証失敗時に不完全ファイルが正式名にならない。
/// 復元は予約ファイルを書くだけで、実際の差し替えは次回起動時の
/// <see cref="SnapshotRestoreBootstrap"/> が DB 接続確立前に行う(単一共有接続のため)。
/// </summary>
public sealed class SnapshotService
{
    public const string FilePrefix = "snapshot-";
    public const string SafetyFilePrefix = "pre-restore-";
    public const string PartialExtension = ".partial";
    public const string MetaSuffix = ".meta.json";
    public const string PendingRestoreFileName = "restore-pending.json";

    private readonly DatabaseManager _db;
    private readonly IClock _clock;
    private readonly string _appDataDir;
    private readonly string _appVersion;

    public SnapshotService(DatabaseManager db, IClock clock, string appDataDir, string appVersion)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentException.ThrowIfNullOrEmpty(appDataDir);
        _db = db;
        _clock = clock;
        _appDataDir = appDataDir;
        _appVersion = appVersion;
    }

    /// <summary>保存先の既定(SS-002: アプリ共通・%APPDATA%/ViewPrism2/snapshots)。</summary>
    public string DefaultDirectory => Path.Combine(_appDataDir, "snapshots");

    /// <summary>作成中表示用の件数(タグ/ビュー/画像メタデータ)。</summary>
    public Task<SnapshotCounts> CountAsync(CancellationToken ct = default)
        => _db.RunAsync(async conn =>
        {
            var tags = await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM tags").ConfigureAwait(false);
            var views = await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM views").ConfigureAwait(false);
            var images = await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM images").ConfigureAwait(false);
            return new SnapshotCounts(tags, views, images);
        }, ct);

    /// <summary>
    /// スナップショットを作成する。共有接続上の VACUUM INTO なので書き出しは同一時点で一貫する。
    /// 失敗・キャンセル時は .partial を残さない(SS-005)。
    /// </summary>
    public async Task<Result<SnapshotInfo>> CreateAsync(string directory, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        var createdAtIso = _clock.UtcNowIso();
        var createdAt = IsoTimestamp.Parse(createdAtIso);
        var baseName = FilePrefix + createdAt.ToString("yyyyMMdd-HHmmssfff", System.Globalization.CultureInfo.InvariantCulture);
        var partialPath = Path.Combine(directory, baseName + PartialExtension);
        try
        {
            Directory.CreateDirectory(directory);
            // VACUUM INTO の出力先は存在してはならない(SQLite 契約)
            File.Delete(partialPath);
            await _db.RunAsync(conn => conn.ExecuteAsync("VACUUM INTO @path", new { path = partialPath }), ct)
                .ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            var verify = VerifyDatabaseFile(partialPath, quick: true);
            if (!verify.IsSuccess)
            {
                return Result<SnapshotInfo>.Fail(verify.Error ?? ErrorCode.Database, verify.Message);
            }

            ct.ThrowIfCancellationRequested();
            string checksum;
            await using (var stream = File.OpenRead(partialPath))
            {
                checksum = FileHasher.ComputeSha256(stream);
            }

            var finalPath = Path.Combine(directory, baseName + ".db");
            File.Move(partialPath, finalPath);
            var size = new FileInfo(finalPath).Length;
            WriteMeta(finalPath, new SnapshotMeta(_appVersion, createdAtIso, checksum, size));
            return Result<SnapshotInfo>.Ok(new SnapshotInfo(finalPath, createdAt, _appVersion, size, IsVerified: true));
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            TryDelete(partialPath);
            if (ex is OperationCanceledException)
            {
                throw;
            }

            return Result<SnapshotInfo>.Fail(ErrorCode.IoError, ex.Message);
        }
    }

    /// <summary>
    /// 作成済みスナップショットの一覧(新しい順)。検証済み=サイドカーメタが存在し
    /// サイズが一致するもの。メタ欠落・不一致は「検証待ち」(復元不可)として列挙する。
    /// </summary>
    public IReadOnlyList<SnapshotInfo> List(string directory)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var items = new List<SnapshotInfo>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.db"))
        {
            var name = Path.GetFileName(path);
            if (!name.StartsWith(FilePrefix, StringComparison.OrdinalIgnoreCase) &&
                !name.StartsWith(SafetyFilePrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var size = new FileInfo(path).Length;
            var meta = TryReadMeta(path);
            var verified = meta is not null && meta.SizeBytes == size;
            var createdAt = meta is not null
                ? IsoTimestamp.Parse(meta.CreatedAt)
                : File.GetLastWriteTimeUtc(path);
            items.Add(new SnapshotInfo(path, createdAt, meta?.AppVersion, size, verified));
        }

        return [.. items.OrderByDescending(i => i.CreatedAtUtc)];
    }

    /// <summary>
    /// 復元前の受入検証(復元直前は integrity_check・ECO-072 §3.1)。
    /// 併せてスナップショット内 migrations が現行アプリの既知 ID の部分集合であることを検査し、
    /// 未知 ID(=より新しいアプリ由来)は互換性なしとして拒否する。
    /// </summary>
    public Result ValidateForRestore(string snapshotPath)
        => ValidateSnapshotFile(snapshotPath, DatabaseSchema.Migrations);

    internal static Result ValidateSnapshotFile(string snapshotPath, IReadOnlyList<Migration> knownMigrations)
    {
        if (!File.Exists(snapshotPath))
        {
            return Result.Fail(ErrorCode.NotFound, snapshotPath);
        }

        var integrity = VerifyDatabaseFile(snapshotPath, quick: false);
        if (!integrity.IsSuccess)
        {
            return integrity;
        }

        try
        {
            using var conn = OpenReadOnly(snapshotPath);
            var hasMigrations = conn.ExecuteScalar<long>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='migrations'") > 0;
            if (!hasMigrations)
            {
                return Result.Fail(ErrorCode.ValidationError, "migrations テーブルがありません(ViewPrism2 のスナップショットではありません)。");
            }

            var known = knownMigrations.Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
            var unknown = conn.Query<string>("SELECT id FROM migrations")
                .Where(id => !known.Contains(id))
                .ToList();
            return unknown.Count > 0
                ? Result.Fail(ErrorCode.ValidationError, $"未知のマイグレーション: {string.Join(", ", unknown)}(より新しいアプリで作成されています)")
                : Result.Ok();
        }
        catch (SqliteException ex)
        {
            return Result.Fail(ErrorCode.Database, ex.Message);
        }
    }

    /// <summary>
    /// 時点復元を予約する(案A)。実適用は次回起動時の <see cref="SnapshotRestoreBootstrap"/>。
    /// </summary>
    public void RequestRestore(string snapshotPath, string safetyDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(snapshotPath);
        ArgumentException.ThrowIfNullOrEmpty(safetyDirectory);
        var pending = new PendingRestore(snapshotPath, safetyDirectory, _clock.UtcNowIso());
        File.WriteAllText(
            Path.Combine(_appDataDir, PendingRestoreFileName),
            JsonSerializer.Serialize(pending, SnapshotJson.Options));
    }

    /// <summary>読み取り専用+プールなしで開く(検証用。ファイルハンドルを残さない)。</summary>
    internal static SqliteConnection OpenReadOnly(string path)
    {
        var conn = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        conn.Open();
        return conn;
    }

    /// <summary>quick_check(作成時)/ integrity_check(復元直前)+ foreign_key_check。</summary>
    internal static Result VerifyDatabaseFile(string path, bool quick)
    {
        try
        {
            using var conn = OpenReadOnly(path);
            var pragma = quick ? "PRAGMA quick_check" : "PRAGMA integrity_check";
            var check = conn.ExecuteScalar<string>(pragma);
            if (!string.Equals(check, "ok", StringComparison.OrdinalIgnoreCase))
            {
                return Result.Fail(ErrorCode.Database, $"{pragma}: {check}");
            }

            var fkViolations = conn.Query("PRAGMA foreign_key_check").Count();
            return fkViolations > 0
                ? Result.Fail(ErrorCode.Database, $"foreign_key_check: {fkViolations} 件の違反")
                : Result.Ok();
        }
        catch (SqliteException ex)
        {
            return Result.Fail(ErrorCode.Database, ex.Message);
        }
    }

    internal static void WriteMeta(string snapshotPath, SnapshotMeta meta)
        => File.WriteAllText(snapshotPath + MetaSuffix, JsonSerializer.Serialize(meta, SnapshotJson.Options));

    internal static SnapshotMeta? TryReadMeta(string snapshotPath)
    {
        var metaPath = snapshotPath + MetaSuffix;
        if (!File.Exists(metaPath))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SnapshotMeta>(File.ReadAllText(metaPath), SnapshotJson.Options);
        }
        catch (JsonException)
        {
            return null; // 破損メタ=検証待ち扱い(INV-008 と同じ「止めない」方向)
        }
    }

    internal static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // 後始末失敗は結果に影響させない(.partial は正式名にならないことが契約)
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>復元予約(restore-pending.json)の内容。</summary>
public sealed record PendingRestore(string SnapshotPath, string SafetyDirectory, string RequestedAt);

internal static class SnapshotJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}
