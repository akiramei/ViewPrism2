using Dapper;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Repositories;

namespace ViewPrism2.Infrastructure.Database;

/// <summary>画像リポジトリ(M-DB-007)。relative_path は正規形のみ格納(INV-005)。</summary>
public sealed class ImageRepository : IImageRepository
{
    private const string InsertSql = """
        INSERT INTO images (id, sync_folder_id, relative_path, file_name, file_size, hash,
                            status, candidate_link_id, created_date, modified_date, notes, pending_origin)
        VALUES (@Id, @SyncFolderId, @RelativePath, @FileName, @FileSize, @Hash,
                @Status, @CandidateLinkId, @CreatedDate, @ModifiedDate, @Notes, @PendingOrigin)
        """;

    private const string SelectColumns = """
        SELECT id AS Id, sync_folder_id AS SyncFolderId, relative_path AS RelativePath,
               file_name AS FileName, file_size AS FileSize, hash AS Hash, status AS Status,
               candidate_link_id AS CandidateLinkId, created_date AS CreatedDate,
               modified_date AS ModifiedDate, notes AS Notes, pending_origin AS PendingOrigin
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
        return _db.RunAsync(conn => conn.ExecuteAsync(InsertSql, ToInsertParameters(image)));
    }

    public Task ApplyScanBatchAsync(ScanMutationBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (batch.Count == 0)
        {
            return Task.CompletedTask;
        }

        return _db.RunAsync(async conn =>
        {
            using var tx = conn.BeginTransaction();
            if (batch.StatusUpdates.Count > 0)
            {
                // ECO-129/REQ-101: pending_origin は遷移ごとに明示上書き(NULL=クリア)。
                // pending 以外へ遷移する行は candidate_link_id もクリアする(候補関係の失効=手順 5)
                await conn.ExecuteAsync("""
                    UPDATE images
                    SET status = @Status,
                        pending_origin = @PendingOrigin,
                        candidate_link_id = CASE WHEN @Status <> 'pending' THEN NULL ELSE candidate_link_id END
                    WHERE id = @Id
                    """,
                    batch.StatusUpdates.Select(update => new
                    {
                        update.Id,
                        Status = update.Status.ToDb(),
                        PendingOrigin = update.PendingOrigin.ToDb(),
                    }), tx).ConfigureAwait(false);
            }

            if (batch.Deletes.Count > 0)
            {
                await conn.ExecuteAsync(
                    "DELETE FROM images WHERE id = @Id",
                    batch.Deletes.Select(id => new { Id = id }), tx).ConfigureAwait(false);
            }

            if (batch.FileMetaUpdates.Count > 0)
            {
                await conn.ExecuteAsync(
                    "UPDATE images SET hash = @Hash, file_size = @FileSize, modified_date = @ModifiedDate WHERE id = @Id",
                    batch.FileMetaUpdates, tx).ConfigureAwait(false);
            }

            if (batch.Adds.Count > 0)
            {
                await conn.ExecuteAsync(
                    InsertSql,
                    batch.Adds.Select(ToInsertParameters), tx).ConfigureAwait(false);
            }

            tx.Commit();
        });
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

    public Task<IReadOnlyDictionary<string, int>> GetNormalCountsByFolderAsync(CancellationToken ct = default)
    {
        return _db.RunAsync<IReadOnlyDictionary<string, int>>(async conn =>
        {
            var rows = await conn.QueryAsync(
                "SELECT sync_folder_id AS FolderId, COUNT(*) AS NormalCount FROM images WHERE status = 'normal' GROUP BY sync_folder_id")
                .ConfigureAwait(false);
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var row in rows)
            {
                var values = (IDictionary<string, object?>)row;
                if (values["FolderId"] is string folderId)
                {
                    result[folderId] = Convert.ToInt32(values["NormalCount"], System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            return result;
        }, ct);
    }

    public Task<IReadOnlyList<ImageRecord>> GetNormalByFolderAsync(
        string syncFolderId, CancellationToken ct = default)
    {
        return _db.RunAsync<IReadOnlyList<ImageRecord>>(async conn =>
        {
            var rows = await conn.QueryAsync<Row>(
                $"{SelectColumns} WHERE sync_folder_id = @SyncFolderId AND status = 'normal' ORDER BY id",
                new { SyncFolderId = syncFolderId }).ConfigureAwait(false);
            return rows.Select(r => ToEntity(r)!).ToList();
        }, ct);
    }

    public Task<IReadOnlyList<ImageRecord>> GetDeletedByFolderAsync(
        string syncFolderId, CancellationToken ct = default)
    {
        // ECO-098: hidden status の規模に比例させず、ゴミ箱対象だけを DB 境界で限定する。
        return _db.RunAsync<IReadOnlyList<ImageRecord>>(async conn =>
        {
            var rows = await conn.QueryAsync<Row>(
                $"{SelectColumns} WHERE sync_folder_id = @SyncFolderId AND status = 'deleted' " +
                "ORDER BY file_name COLLATE NOCASE, id",
                new { SyncFolderId = syncFolderId }).ConfigureAwait(false);
            return rows.Select(r => ToEntity(r)!).ToList();
        }, ct);
    }

    public Task<int> CountByFolderAndStatusAsync(
        string syncFolderId, ImageStatus status, CancellationToken ct = default)
    {
        return _db.RunAsync(async conn =>
        {
            var count = await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM images WHERE sync_folder_id = @SyncFolderId AND status = @Status",
                new { SyncFolderId = syncFolderId, Status = status.ToDb() }).ConfigureAwait(false);
            return checked((int)count);
        }, ct);
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

    public Task RestoreStatusAsync(string id, ImageStatus status, PendingOrigin? origin)
    {
        // ECO-128 T6'/T7: status と pending_origin を単一 UPDATE で原子適用(deleted→pending は
        // origin=Restored / deleted→missing は origin=NULL)。candidate_link_id は不変(deleted 行は NULL)。
        return _db.RunAsync(conn => conn.ExecuteAsync(
            "UPDATE images SET status = @Status, pending_origin = @Origin WHERE id = @Id",
            new { Id = id, Status = status.ToDb(), Origin = origin.ToDb() }));
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

    public Task<IReadOnlyList<ImageRecord>> GetPendingByFolderAsync(
        string syncFolderId, CancellationToken ct = default)
    {
        // ECO-129/E-UI-PENDING-049: pending だけを DB 境界で限定(全行 materialize 禁止=ECO-098 同型)
        return _db.RunAsync<IReadOnlyList<ImageRecord>>(async conn =>
        {
            var rows = await conn.QueryAsync<Row>(
                $"{SelectColumns} WHERE sync_folder_id = @SyncFolderId AND status = 'pending' " +
                "ORDER BY file_name COLLATE NOCASE, id",
                new { SyncFolderId = syncFolderId }).ConfigureAwait(false);
            return rows.Select(r => ToEntity(r)!).ToList();
        }, ct);
    }

    public Task<IReadOnlyList<ImageRecord>> GetByIdsAsync(
        IReadOnlyCollection<string> ids, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var distinctIds = ids
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (distinctIds.Length == 0)
        {
            return Task.FromResult<IReadOnlyList<ImageRecord>>([]);
        }

        // SQLite のバインド変数上限を越えないよう分割する。PD-6 の 1 万件運用でも N+1 にしない。
        return _db.RunAsync<IReadOnlyList<ImageRecord>>(async conn =>
        {
            var result = new List<ImageRecord>(distinctIds.Length);
            foreach (var chunk in distinctIds.Chunk(500))
            {
                ct.ThrowIfCancellationRequested();
                var rows = await conn.QueryAsync<Row>(
                    $"{SelectColumns} WHERE id IN @Ids", new { Ids = chunk }).ConfigureAwait(false);
                result.AddRange(rows.Select(r => ToEntity(r)!));
            }

            return result;
        }, ct);
    }

    public Task<bool> AdjudicatePendingAsync(string id, ImageStatus status)
    {
        // ECO-129 T13/T15: pending 限定を UPDATE の WHERE で原子的に強制(0 行=拒否)。
        // candidate_link_id/pending_origin はクリア(履歴の厳密管理はしない)
        return _db.RunAsync(async conn =>
        {
            var affected = await conn.ExecuteAsync("""
                UPDATE images
                SET status = @Status, candidate_link_id = NULL, pending_origin = NULL
                WHERE id = @Id AND status = 'pending'
                """,
                new { Id = id, Status = status.ToDb() }).ConfigureAwait(false);
            return affected == 1;
        });
    }

    public Task<bool> AdjudicatePendingBatchAsync(
        IReadOnlyCollection<string> ids, ImageStatus status)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var distinctIds = ids
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (distinctIds.Length == 0)
        {
            return Task.FromResult(true);
        }

        // ECO-139: chunk は SQLite 変数上限対策だけで、トランザクションはバッチ全体で 1 つ。
        // 各 chunk の affected 数が要求数と違えば、pending 限定拒否としてそれ以前も全 rollback。
        return _db.RunAsync(async conn =>
        {
            using var tx = conn.BeginTransaction();
            foreach (var chunk in distinctIds.Chunk(500))
            {
                var affected = await conn.ExecuteAsync("""
                    UPDATE images
                    SET status = @Status, candidate_link_id = NULL, pending_origin = NULL
                    WHERE id IN @Ids AND status = 'pending'
                    """,
                    new { Status = status.ToDb(), Ids = chunk }, tx).ConfigureAwait(false);
                if (affected != chunk.Length)
                {
                    tx.Rollback();
                    return false;
                }
            }

            tx.Commit();
            return true;
        });
    }

    public Task<bool> ReplacePendingAsync(string oldId, ImageRecord replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);

        // ECO-129 T14(別画像として扱う=PEND-001 裁定): 単一トランザクションの原子的な行置換。
        // 旧行 DELETE(image_tags/image_features/image_similarity は FK CASCADE 消滅)→ 新行 INSERT。
        // DELETE の WHERE で pending 限定を強制(0 行=拒否・rollback)。1 パス 1 行の不変を維持
        // (UNIQUE(sync_folder_id, relative_path) は DELETE→INSERT の順で衝突しない)
        return _db.RunAsync(async conn =>
        {
            using var tx = conn.BeginTransaction();
            var affected = await conn.ExecuteAsync(
                "DELETE FROM images WHERE id = @Id AND status = 'pending'",
                new { Id = oldId }, tx).ConfigureAwait(false);
            if (affected != 1)
            {
                tx.Rollback();
                return false;
            }

            await conn.ExecuteAsync(InsertSql, ToInsertParameters(replacement), tx).ConfigureAwait(false);
            tx.Commit();
            return true;
        });
    }

    private sealed record Row(
        string Id, string SyncFolderId, string RelativePath, string FileName, long FileSize,
        string Hash, string Status, string? CandidateLinkId, string CreatedDate,
        string ModifiedDate, string? Notes, string? PendingOrigin);

    private static object ToInsertParameters(ImageRecord image)
    {
        return new
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
            PendingOrigin = image.PendingOrigin.ToDb(),
        };
    }

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
                PendingOrigin = DbMapping.ToPendingOrigin(row.PendingOrigin),
            };
    }
}
