using Dapper;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Repositories;

namespace ViewPrism2.Infrastructure.Database;

/// <summary>
/// マージの原子適用(M-MERGE-022、仕様 §2.10.5)。単一トランザクションで:
///   1. マージ後タグをマージ先へ UPSERT(REQ-026 の ON CONFLICT 意味論)
///   2. 各マージ元の status を 'deleted' にする(image_tags は削除しない)
/// 物理ファイルには一切触れない(INV-009)。失敗時は全ロールバック。
/// </summary>
public sealed class MergeRepository : IMergeRepository
{
    private const string UpsertImageTagSql = """
        INSERT INTO image_tags (image_id, tag_id, value)
        VALUES (@ImageId, @TagId, @Value)
        ON CONFLICT(image_id, tag_id) DO UPDATE SET value = excluded.value
        """;

    private readonly DatabaseManager _db;

    public MergeRepository(DatabaseManager db)
    {
        _db = db;
    }

    public Task ApplyMergeAsync(
        string targetId, IReadOnlyList<ImageTag> mergedTags, IReadOnlyList<string> sourceIds)
    {
        ArgumentException.ThrowIfNullOrEmpty(targetId);
        ArgumentNullException.ThrowIfNull(mergedTags);
        ArgumentNullException.ThrowIfNull(sourceIds);

        // INV-006: 単一トランザクション、失敗時全ロールバック
        return _db.RunAsync(async conn =>
        {
            using var tx = conn.BeginTransaction();
            try
            {
                // 1) タグ集約(union)をマージ先へ UPSERT
                foreach (var tag in mergedTags)
                {
                    await conn.ExecuteAsync(
                        UpsertImageTagSql,
                        new { ImageId = targetId, tag.TagId, tag.Value }, tx).ConfigureAwait(false);
                }

                // 2) 各マージ元の status を deleted に(image_tags は削除しない)
                foreach (var sourceId in sourceIds)
                {
                    await conn.ExecuteAsync(
                        "UPDATE images SET status = 'deleted' WHERE id = @Id",
                        new { Id = sourceId }, tx).ConfigureAwait(false);
                }

                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        });
    }
}
