using Dapper;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Repositories;

namespace ViewPrism2.Infrastructure.Database;

/// <summary>
/// 作業スペースリポジトリ(ECO-020 / M-DB-007)。デフォルト回転(INV-W1)・移動(INV-W5)は
/// 単一トランザクションで原子に行う。所属画像の取得は images と JOIN し status=normal のみ返す(INV-W2)。
/// </summary>
public sealed class WorkspaceRepository : IWorkspaceRepository
{
    private const string SelectColumns = """
        SELECT id AS Id, name AS Name, is_default AS IsDefault, seq AS Seq, created_at AS CreatedAt
        FROM workspaces
        """;

    // 所属画像取得は images の列を ImageRepository と同形でマップする(workspace_images と JOIN)
    private const string SelectImageColumns = """
        SELECT i.id AS Id, i.sync_folder_id AS SyncFolderId, i.relative_path AS RelativePath,
               i.file_name AS FileName, i.file_size AS FileSize, i.hash AS Hash, i.status AS Status,
               i.candidate_link_id AS CandidateLinkId, i.created_date AS CreatedDate,
               i.modified_date AS ModifiedDate, i.notes AS Notes
        FROM workspace_images wi
        JOIN images i ON i.id = wi.image_id
        """;

    private readonly DatabaseManager _db;

    public WorkspaceRepository(DatabaseManager db)
    {
        _db = db;
    }

    public Task<IReadOnlyList<WorkspaceWithCount>> GetAllWithNormalCountsAsync()
    {
        return _db.RunAsync<IReadOnlyList<WorkspaceWithCount>>(async conn =>
        {
            // 件数は status=normal のみ(INV-W2)。LEFT JOIN で空スペースも 0 件で含める。
            // 並びは seq 降順(新しいほど上=スタックのトップ)。デフォルトは常に最大 seq なので必ず最上段。
            var rows = await conn.QueryAsync<WorkspaceRow, long, WorkspaceWithCount>(
                $"""
                SELECT w.id AS Id, w.name AS Name, w.is_default AS IsDefault, w.seq AS Seq, w.created_at AS CreatedAt,
                       COUNT(i.id) AS NormalCount
                FROM workspaces w
                LEFT JOIN workspace_images wi ON wi.workspace_id = w.id
                LEFT JOIN images i ON i.id = wi.image_id AND i.status = 'normal'
                GROUP BY w.id
                ORDER BY w.seq DESC
                """,
                (row, count) => new WorkspaceWithCount(ToEntity(row)!, (int)count),
                splitOn: "NormalCount").ConfigureAwait(false);
            return rows.ToList();
        });
    }

    public Task<Workspace?> GetByIdAsync(string id)
    {
        return _db.RunAsync(async conn =>
        {
            var row = await conn.QuerySingleOrDefaultAsync<WorkspaceRow>(
                $"{SelectColumns} WHERE id = @Id", new { Id = id }).ConfigureAwait(false);
            return ToEntity(row);
        });
    }

    public Task<Workspace?> GetDefaultAsync()
    {
        return _db.RunAsync(async conn =>
        {
            var row = await conn.QuerySingleOrDefaultAsync<WorkspaceRow>(
                $"{SelectColumns} WHERE is_default = 1").ConfigureAwait(false);
            return ToEntity(row);
        });
    }

    public Task<int> GetMaxSeqAsync()
    {
        return _db.RunAsync(conn => conn.ExecuteScalarAsync<int>(
            "SELECT COALESCE(MAX(seq), 0) FROM workspaces"));
    }

    public Task AddAsync(Workspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        return _db.RunAsync(conn => conn.ExecuteAsync("""
            INSERT INTO workspaces (id, name, is_default, seq, created_at)
            VALUES (@Id, @Name, @IsDefault, @Seq, @CreatedAt)
            """,
            new { workspace.Id, workspace.Name, workspace.IsDefault, workspace.Seq, workspace.CreatedAt }));
    }

    public Task RenameAsync(string id, string name)
    {
        return _db.RunAsync(conn => conn.ExecuteAsync(
            "UPDATE workspaces SET name = @Name WHERE id = @Id", new { Id = id, Name = name }));
    }

    public Task DeleteAsync(string id)
    {
        return _db.RunAsync(conn =>
        {
            using var tx = conn.BeginTransaction();
            // 先に所属を除去 → スペース行を削除(画像は物理非破壊・INV-W4)
            conn.Execute("DELETE FROM workspace_images WHERE workspace_id = @Id", new { Id = id }, tx);
            conn.Execute("DELETE FROM workspaces WHERE id = @Id", new { Id = id }, tx);
            tx.Commit();
            return Task.CompletedTask;
        });
    }

    public Task CreateRotatingDefaultAsync(Workspace newDefault, string oldDefaultId, string oldDefaultNewName)
    {
        ArgumentNullException.ThrowIfNull(newDefault);
        return _db.RunAsync(conn =>
        {
            using var tx = conn.BeginTransaction();
            // 先に旧デフォルトを降格(部分 UNIQUE 索引の衝突回避)→ 改名 → 新デフォルト挿入(INV-W1)
            conn.Execute(
                "UPDATE workspaces SET is_default = 0, name = @Name WHERE id = @Id",
                new { Id = oldDefaultId, Name = oldDefaultNewName }, tx);
            conn.Execute("""
                INSERT INTO workspaces (id, name, is_default, seq, created_at)
                VALUES (@Id, @Name, 1, @Seq, @CreatedAt)
                """,
                new { newDefault.Id, newDefault.Name, newDefault.Seq, newDefault.CreatedAt }, tx);
            tx.Commit();
            return Task.CompletedTask;
        });
    }

    public Task<IReadOnlyList<ImageRecord>> GetNormalImagesAsync(string workspaceId)
    {
        return _db.RunAsync<IReadOnlyList<ImageRecord>>(async conn =>
        {
            var rows = await conn.QueryAsync<ImageRow>(
                $"""
                {SelectImageColumns}
                WHERE wi.workspace_id = @WorkspaceId AND i.status = 'normal'
                ORDER BY wi.added_at, i.id
                """,
                new { WorkspaceId = workspaceId }).ConfigureAwait(false);
            return rows.Select(r => ToImage(r)!).ToList();
        });
    }

    public Task<IReadOnlyList<ImageRecord>> GetDeletedImagesAsync(string workspaceId)
    {
        return _db.RunAsync<IReadOnlyList<ImageRecord>>(async conn =>
        {
            var rows = await conn.QueryAsync<ImageRow>(
                $"""
                {SelectImageColumns}
                WHERE wi.workspace_id = @WorkspaceId AND i.status = 'deleted'
                ORDER BY i.file_name COLLATE NOCASE, i.id
                """,
                new { WorkspaceId = workspaceId }).ConfigureAwait(false);
            return rows.Select(r => ToImage(r)!).ToList();
        });
    }

    public Task AddImagesAsync(string workspaceId, IReadOnlyList<string> imageIds, string addedAt)
    {
        ArgumentNullException.ThrowIfNull(imageIds);
        if (imageIds.Count == 0) return Task.CompletedTask;
        return _db.RunAsync(conn =>
        {
            using var tx = conn.BeginTransaction();
            InsertImages(conn, tx, workspaceId, imageIds, addedAt);
            tx.Commit();
            return Task.CompletedTask;
        });
    }

    public Task MoveImagesAsync(string fromWorkspaceId, string toWorkspaceId, IReadOnlyList<string> imageIds, string addedAt)
    {
        ArgumentNullException.ThrowIfNull(imageIds);
        if (imageIds.Count == 0) return Task.CompletedTask;
        return _db.RunAsync(conn =>
        {
            using var tx = conn.BeginTransaction();
            // 元から除去 → 移動先へ和集合追加(原子・INV-W5)
            conn.Execute(
                "DELETE FROM workspace_images WHERE workspace_id = @From AND image_id IN @Ids",
                new { From = fromWorkspaceId, Ids = imageIds }, tx);
            InsertImages(conn, tx, toWorkspaceId, imageIds, addedAt);
            tx.Commit();
            return Task.CompletedTask;
        });
    }

    // 和集合追加(重複は無視=集合・INV-W2)。呼び出し側のトランザクション内で実行する。
    private static void InsertImages(
        Microsoft.Data.Sqlite.SqliteConnection conn, Microsoft.Data.Sqlite.SqliteTransaction tx,
        string workspaceId, IReadOnlyList<string> imageIds, string addedAt)
    {
        foreach (var imageId in imageIds)
        {
            conn.Execute("""
                INSERT OR IGNORE INTO workspace_images (workspace_id, image_id, added_at)
                VALUES (@WorkspaceId, @ImageId, @AddedAt)
                """,
                new { WorkspaceId = workspaceId, ImageId = imageId, AddedAt = addedAt }, tx);
        }
    }

    private sealed record WorkspaceRow(string Id, string Name, long IsDefault, long Seq, string CreatedAt);

    private static Workspace? ToEntity(WorkspaceRow? row)
    {
        return row is null
            ? null
            : new Workspace
            {
                Id = row.Id,
                Name = row.Name,
                IsDefault = row.IsDefault != 0,
                Seq = (int)row.Seq,
                CreatedAt = row.CreatedAt,
            };
    }

    private sealed record ImageRow(
        string Id, string SyncFolderId, string RelativePath, string FileName, long FileSize,
        string Hash, string Status, string? CandidateLinkId, string CreatedDate,
        string ModifiedDate, string? Notes);

    private static ImageRecord? ToImage(ImageRow? row)
    {
        return row is null
            ? null
            : new ImageRecord
            {
                Id = row.Id,
                SyncFolderId = row.SyncFolderId,
                RelativePath = row.RelativePath,
                FileName = row.FileName,
                FileSize = row.FileSize,
                Hash = row.Hash,
                Status = DbMapping.ToImageStatus(row.Status),
                CandidateLinkId = row.CandidateLinkId,
                CreatedDate = row.CreatedDate,
                ModifiedDate = row.ModifiedDate,
                Notes = row.Notes,
            };
    }
}
