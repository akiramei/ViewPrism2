using System.Text.Json;
using Dapper;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Repositories;

namespace ViewPrism2.Infrastructure.Database;

/// <summary>
/// マージの原子適用(M-MERGE-022、仕様 §2.10.5)。単一トランザクションで:
///   1. マージ後タグをマージ先へ UPSERT(REQ-026 の ON CONFLICT 意味論)
///   2. 各マージ元の status を 'deleted' にする(image_tags は削除しない)
///   3. (ECO-044) マージ操作ログ merge_operations を 1 行記録する
/// 物理ファイルには一切触れない(INV-009)。失敗時は全ロールバック。
/// 補償 Undo(ECO-044/IMG-011 裁定③)も単一トランザクションで原子適用する。
/// </summary>
public sealed class MergeRepository : IMergeRepository
{
    private const string UpsertImageTagSql = """
        INSERT INTO image_tags (image_id, tag_id, value)
        VALUES (@ImageId, @TagId, @Value)
        ON CONFLICT(image_id, tag_id) DO UPDATE SET value = excluded.value
        """;

    private const string SelectOperationColumns = """
        SELECT id, target_id, source_ids, added_tag_ids, filled_tags, executed_at,
               target_fingerprint, source_fingerprints, undone_at
        FROM merge_operations
        """;

    private readonly DatabaseManager _db;

    public MergeRepository(DatabaseManager db)
    {
        _db = db;
    }

    public Task ApplyMergeAsync(
        string targetId, IReadOnlyList<ImageTag> mergedTags, IReadOnlyList<string> sourceIds)
        => ApplyMergeAsync(targetId, mergedTags, sourceIds, operation: null);

    public Task ApplyMergeAsync(
        string targetId, IReadOnlyList<ImageTag> mergedTags, IReadOnlyList<string> sourceIds,
        MergeOperationRecord? operation)
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

                // 3) 操作ログ(ECO-044 — マージと同一トランザクション)
                if (operation is not null)
                {
                    await conn.ExecuteAsync("""
                        INSERT INTO merge_operations
                            (id, target_id, source_ids, added_tag_ids, filled_tags, executed_at,
                             target_fingerprint, source_fingerprints, undone_at)
                        VALUES (@Id, @TargetId, @SourceIds, @AddedTagIds, @FilledTags, @ExecutedAt,
                                @TargetFingerprint, @SourceFingerprints, NULL)
                        """,
                        new
                        {
                            operation.Id,
                            operation.TargetId,
                            SourceIds = JsonSerializer.Serialize(operation.SourceIds),
                            AddedTagIds = JsonSerializer.Serialize(operation.AddedTagIds),
                            FilledTags = JsonSerializer.Serialize(operation.FilledTags),
                            operation.ExecutedAt,
                            operation.TargetFingerprint,
                            SourceFingerprints = JsonSerializer.Serialize(operation.SourceFingerprints),
                        }, tx).ConfigureAwait(false);
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

    public Task<MergeOperationRecord?> GetOperationAsync(string operationId)
    {
        ArgumentException.ThrowIfNullOrEmpty(operationId);
        return _db.RunAsync(async conn =>
        {
            var row = await conn.QuerySingleOrDefaultAsync<OperationRow>(
                SelectOperationColumns + " WHERE id = @Id",
                new { Id = operationId }).ConfigureAwait(false);
            return Map(row);
        });
    }

    public Task<MergeOperationRecord?> GetLatestOperationAsync(string targetId)
    {
        ArgumentException.ThrowIfNullOrEmpty(targetId);
        return _db.RunAsync(async conn =>
        {
            // executed_at の同時刻タイ(テストの固定時計等)は rowid=挿入順で決着
            var row = await conn.QuerySingleOrDefaultAsync<OperationRow>(
                SelectOperationColumns + " WHERE target_id = @Id ORDER BY executed_at DESC, rowid DESC LIMIT 1",
                new { Id = targetId }).ConfigureAwait(false);
            return Map(row);
        });
    }

    public Task ApplyUndoAsync(MergeOperationRecord operation, string undoneAtIso)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentException.ThrowIfNullOrEmpty(undoneAtIso);

        // 補償は原子(単一トランザクション・失敗時全ロールバック)
        return _db.RunAsync(async conn =>
        {
            using var tx = conn.BeginTransaction();
            try
            {
                // 1) マージで追加されたタグ行を destination から削除
                foreach (var tagId in operation.AddedTagIds)
                {
                    await conn.ExecuteAsync(
                        "DELETE FROM image_tags WHERE image_id = @ImageId AND tag_id = @TagId",
                        new { ImageId = operation.TargetId, TagId = tagId }, tx).ConfigureAwait(false);
                }

                // 2) NULL/空 補完で値が入った行を元値へ戻す
                foreach (var (tagId, originalValue) in operation.FilledTags)
                {
                    await conn.ExecuteAsync(
                        "UPDATE image_tags SET value = @Value WHERE image_id = @ImageId AND tag_id = @TagId",
                        new { Value = originalValue, ImageId = operation.TargetId, TagId = tagId }, tx).ConfigureAwait(false);
                }

                // 3) sources を deleted → normal へ復元
                foreach (var sourceId in operation.SourceIds)
                {
                    await conn.ExecuteAsync(
                        "UPDATE images SET status = 'normal' WHERE id = @Id",
                        new { Id = sourceId }, tx).ConfigureAwait(false);
                }

                // 4) 取り消し済みマーク(二重 Undo 拒否の根拠)
                await conn.ExecuteAsync(
                    "UPDATE merge_operations SET undone_at = @UndoneAt WHERE id = @Id",
                    new { UndoneAt = undoneAtIso, operation.Id }, tx).ConfigureAwait(false);

                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        });
    }

    // ---- 行マッピング(JSON 列の復元) ----

    private sealed record OperationRow(
        string id, string target_id, string source_ids, string added_tag_ids, string filled_tags,
        string executed_at, string target_fingerprint, string source_fingerprints, string? undone_at);

    private static MergeOperationRecord? Map(OperationRow? row)
        => row is null
            ? null
            : new MergeOperationRecord
            {
                Id = row.id,
                TargetId = row.target_id,
                SourceIds = JsonSerializer.Deserialize<List<string>>(row.source_ids) ?? [],
                AddedTagIds = JsonSerializer.Deserialize<List<string>>(row.added_tag_ids) ?? [],
                FilledTags = JsonSerializer.Deserialize<Dictionary<string, string?>>(row.filled_tags) ?? [],
                ExecutedAt = row.executed_at,
                TargetFingerprint = row.target_fingerprint,
                SourceFingerprints = JsonSerializer.Deserialize<Dictionary<string, string>>(row.source_fingerprints) ?? [],
                UndoneAt = row.undone_at,
            };
}
