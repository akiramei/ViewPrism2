using Dapper;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Repositories;

namespace ViewPrism2.Infrastructure.Database;

/// <summary>画像リポジトリ(M-DB-007)。relative_path は正規形のみ格納(INV-005)。</summary>
public sealed class ImageRepository : IImageRepository
{
    private const string SelectColumns = """
        SELECT id AS Id, sync_folder_id AS SyncFolderId, relative_path AS RelativePath,
               file_name AS FileName, file_size AS FileSize, hash AS Hash, status AS Status,
               candidate_link_id AS CandidateLinkId, created_date AS CreatedDate,
               modified_date AS ModifiedDate, notes AS Notes
        FROM images
        """;

    private readonly DatabaseManager _db;

    public ImageRepository(DatabaseManager db)
    {
        _db = db;
    }

    public Task AddAsync(ImageRecord image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return _db.RunAsync(conn => conn.ExecuteAsync("""
            INSERT INTO images (id, sync_folder_id, relative_path, file_name, file_size, hash,
                                status, candidate_link_id, created_date, modified_date, notes)
            VALUES (@Id, @SyncFolderId, @RelativePath, @FileName, @FileSize, @Hash,
                    @Status, @CandidateLinkId, @CreatedDate, @ModifiedDate, @Notes)
            """,
            new
            {
                image.Id,
                image.SyncFolderId,
                image.RelativePath,
                image.FileName,
                image.FileSize,
                image.Hash,
                Status = image.Status.ToDb(),
                image.CandidateLinkId,
                image.CreatedDate,
                image.ModifiedDate,
                image.Notes,
            }));
    }

    public Task<ImageRecord?> GetByIdAsync(string id)
    {
        return _db.RunAsync(async conn =>
        {
            var row = await conn.QuerySingleOrDefaultAsync<Row>(
                $"{SelectColumns} WHERE id = @Id", new { Id = id }).ConfigureAwait(false);
            return ToEntity(row);
        });
    }

    public Task<IReadOnlyList<ImageRecord>> GetByFolderAsync(string syncFolderId)
    {
        return _db.RunAsync<IReadOnlyList<ImageRecord>>(async conn =>
        {
            var rows = await conn.QueryAsync<Row>(
                $"{SelectColumns} WHERE sync_folder_id = @SyncFolderId ORDER BY id",
                new { SyncFolderId = syncFolderId }).ConfigureAwait(false);
            return rows.Select(r => ToEntity(r)!).ToList();
        });
    }

    public Task<IReadOnlyList<ImageRecord>> GetAllNormalAsync()
    {
        // INV-010: 既定の画像一覧は status=normal のみ
        return _db.RunAsync<IReadOnlyList<ImageRecord>>(async conn =>
        {
            var rows = await conn.QueryAsync<Row>(
                $"{SelectColumns} WHERE status = 'normal' ORDER BY id").ConfigureAwait(false);
            return rows.Select(r => ToEntity(r)!).ToList();
        });
    }

    public Task UpdateFileMetaAsync(string id, string hash, long fileSize, string modifiedDate)
    {
        // スキャン規則 (2): status は変更しない(REQ-012)
        return _db.RunAsync(conn => conn.ExecuteAsync(
            "UPDATE images SET hash = @Hash, file_size = @FileSize, modified_date = @ModifiedDate WHERE id = @Id",
            new { Id = id, Hash = hash, FileSize = fileSize, ModifiedDate = modifiedDate }));
    }

    public Task UpdateStatusAsync(string id, ImageStatus status)
    {
        return _db.RunAsync(conn => conn.ExecuteAsync(
            "UPDATE images SET status = @Status WHERE id = @Id", new { Id = id, Status = status.ToDb() }));
    }

    public Task UpdateNotesAsync(string id, string? notes)
    {
        return _db.RunAsync(conn => conn.ExecuteAsync(
            "UPDATE images SET notes = @Notes WHERE id = @Id", new { Id = id, Notes = notes }));
    }

    public Task DeleteAsync(string id)
    {
        return _db.RunAsync(conn => conn.ExecuteAsync("DELETE FROM images WHERE id = @Id", new { Id = id }));
    }

    public Task ApplyRelinkAsync(string missingImageId, string pendingImageId)
    {
        // REQ-017: 単一トランザクション。pending 行を先に削除してから missing 行へ上書きする
        // (UNIQUE(sync_folder_id, relative_path) との競合回避)。missing 側 image_id は不変(INV-001)
        return _db.RunAsync(async conn =>
        {
            using var tx = conn.BeginTransaction();
            var pending = await conn.QuerySingleAsync<Row>(
                $"{SelectColumns} WHERE id = @Id", new { Id = pendingImageId }, tx).ConfigureAwait(false);

            await conn.ExecuteAsync(
                "DELETE FROM images WHERE id = @Id", new { Id = pendingImageId }, tx).ConfigureAwait(false);
            await conn.ExecuteAsync("""
                UPDATE images
                SET relative_path = @RelativePath, file_name = @FileName, file_size = @FileSize,
                    modified_date = @ModifiedDate, created_date = @CreatedDate, hash = @Hash,
                    status = 'normal', candidate_link_id = NULL
                WHERE id = @Id
                """,
                new
                {
                    Id = missingImageId,
                    pending.RelativePath,
                    pending.FileName,
                    pending.FileSize,
                    pending.ModifiedDate,
                    pending.CreatedDate,
                    pending.Hash,
                }, tx).ConfigureAwait(false);
            tx.Commit();
        });
    }

    public Task<IReadOnlyList<string>> GetDistinctNormalTagValuesAsync(string tagId)
    {
        // INV-010: 値抽出は status=normal の画像のみ
        return _db.RunAsync<IReadOnlyList<string>>(async conn =>
        {
            var values = await conn.QueryAsync<string>("""
                SELECT DISTINCT it.value
                FROM image_tags it
                JOIN images i ON i.id = it.image_id
                WHERE it.tag_id = @TagId AND i.status = 'normal' AND it.value IS NOT NULL
                """,
                new { TagId = tagId }).ConfigureAwait(false);
            return values.ToList();
        });
    }

    private sealed record Row(
        string Id, string SyncFolderId, string RelativePath, string FileName, long FileSize,
        string Hash, string Status, string? CandidateLinkId, string CreatedDate,
        string ModifiedDate, string? Notes);

    private static ImageRecord? ToEntity(Row? row)
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
