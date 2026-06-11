using Dapper;
using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Repositories;

namespace ViewPrism2.Infrastructure.Database;

/// <summary>同期フォルダリポジトリ(M-DB-007)。path の一意性は COLLATE NOCASE+UNIQUE で担保。</summary>
public sealed class SyncFolderRepository : ISyncFolderRepository
{
    private const string SelectColumns = """
        SELECT id AS Id, name AS Name, path AS Path, is_active AS IsActive,
               include_subfolders AS IncludeSubfolders, exclude_patterns AS ExcludePatterns,
               last_scan AS LastScan
        FROM sync_folders
        """;

    private readonly DatabaseManager _db;

    public SyncFolderRepository(DatabaseManager db)
    {
        _db = db;
    }

    public Task<Result> AddAsync(SyncFolder folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        return _db.RunAsync(async conn =>
        {
            // path はシステム内で一意(大文字小文字無視)。重複登録は明示エラーで拒否(REQ-010)
            var duplicate = await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM sync_folders WHERE path = @Path", new { folder.Path })
                .ConfigureAwait(false);
            if (duplicate > 0)
            {
                return Result.Fail(ErrorCode.DuplicateFolderPath, $"パス '{folder.Path}' は既に登録されています。");
            }

            await conn.ExecuteAsync("""
                INSERT INTO sync_folders (id, name, path, is_active, include_subfolders, exclude_patterns, last_scan)
                VALUES (@Id, @Name, @Path, @IsActive, @IncludeSubfolders, @ExcludePatterns, @LastScan)
                """,
                new
                {
                    folder.Id,
                    folder.Name,
                    folder.Path,
                    folder.IsActive,
                    folder.IncludeSubfolders,
                    ExcludePatterns = DbMapping.ToJsonArray(folder.ExcludePatterns),
                    folder.LastScan,
                }).ConfigureAwait(false);
            return Result.Ok();
        });
    }

    public Task<SyncFolder?> GetByIdAsync(string id)
    {
        return _db.RunAsync(async conn =>
        {
            var row = await conn.QuerySingleOrDefaultAsync<Row>(
                $"{SelectColumns} WHERE id = @Id", new { Id = id }).ConfigureAwait(false);
            return ToEntity(row);
        });
    }

    public Task<SyncFolder?> GetByPathAsync(string path)
    {
        return _db.RunAsync(async conn =>
        {
            // COLLATE NOCASE 列のため = 比較が case-insensitive(K-SQLITE)
            var row = await conn.QuerySingleOrDefaultAsync<Row>(
                $"{SelectColumns} WHERE path = @Path", new { Path = path }).ConfigureAwait(false);
            return ToEntity(row);
        });
    }

    public Task<IReadOnlyList<SyncFolder>> GetAllAsync()
    {
        return _db.RunAsync<IReadOnlyList<SyncFolder>>(async conn =>
        {
            var rows = await conn.QueryAsync<Row>($"{SelectColumns} ORDER BY id").ConfigureAwait(false);
            return rows.Select(r => ToEntity(r)!).ToList();
        });
    }

    public Task UpdateAsync(SyncFolder folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        return _db.RunAsync(conn => conn.ExecuteAsync("""
            UPDATE sync_folders
            SET name = @Name, path = @Path, is_active = @IsActive,
                include_subfolders = @IncludeSubfolders, exclude_patterns = @ExcludePatterns,
                last_scan = @LastScan
            WHERE id = @Id
            """,
            new
            {
                folder.Id,
                folder.Name,
                folder.Path,
                folder.IsActive,
                folder.IncludeSubfolders,
                ExcludePatterns = DbMapping.ToJsonArray(folder.ExcludePatterns),
                folder.LastScan,
            }));
    }

    public Task DeleteAsync(string id)
    {
        // 配下 images は FK CASCADE(仕様 §2.0)
        return _db.RunAsync(conn => conn.ExecuteAsync(
            "DELETE FROM sync_folders WHERE id = @Id", new { Id = id }));
    }

    public Task UpdateLastScanAsync(string id, string lastScan)
    {
        return _db.RunAsync(conn => conn.ExecuteAsync(
            "UPDATE sync_folders SET last_scan = @LastScan WHERE id = @Id", new { Id = id, LastScan = lastScan }));
    }

    /// <summary>SQLite ネイティブ型(INTEGER=long)での受け取り行。bool への変換は ToEntity で行う。</summary>
    private sealed record Row(
        string Id, string Name, string Path, long IsActive, long IncludeSubfolders,
        string ExcludePatterns, string? LastScan);

    private static SyncFolder? ToEntity(Row? row)
    {
        return row is null
            ? null
            : new SyncFolder
            {
                Id = row.Id,
                Name = row.Name,
                Path = row.Path,
                IsActive = row.IsActive != 0,
                IncludeSubfolders = row.IncludeSubfolders != 0,
                ExcludePatterns = DbMapping.FromJsonArray(row.ExcludePatterns),
                LastScan = row.LastScan,
            };
    }
}
