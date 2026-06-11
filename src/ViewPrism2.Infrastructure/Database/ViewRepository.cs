using Dapper;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Repositories;

namespace ViewPrism2.Infrastructure.Database;

/// <summary>ビュー・条件・タグ階層リポジトリ(M-DB-007)。連鎖削除は FK CASCADE(REQ-030)。</summary>
public sealed class ViewRepository : IViewRepository
{
    private const string SelectViewColumns = """
        SELECT id AS Id, name AS Name, description AS Description, is_favorite AS IsFavorite,
               sort_field AS SortField, sort_direction AS SortDirection, display_columns AS DisplayColumns,
               home_tag_id AS HomeTagId, modified_at AS ModifiedAt
        FROM views
        """;

    private const string SelectConditionColumns = """
        SELECT id AS Id, view_id AS ViewId, tag_id AS TagId, operator AS Operator,
               value AS Value, value2 AS Value2
        FROM view_conditions
        """;

    private const string SelectNodeColumns = """
        SELECT id AS Id, view_id AS ViewId, tag_id AS TagId, parent_id AS ParentId,
               position AS Position, alias AS Alias, condition_type AS ConditionType,
               condition_value AS ConditionValue
        FROM view_tag_hierarchies
        """;

    private readonly DatabaseManager _db;

    public ViewRepository(DatabaseManager db)
    {
        _db = db;
    }

    // ---- views ----

    public Task AddAsync(View view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return _db.RunAsync(conn => conn.ExecuteAsync("""
            INSERT INTO views (id, name, description, is_favorite, sort_field, sort_direction, display_columns, home_tag_id, modified_at)
            VALUES (@Id, @Name, @Description, @IsFavorite, @SortField, @SortDirection, @DisplayColumns, @HomeTagId, @ModifiedAt)
            """,
            new
            {
                view.Id,
                view.Name,
                view.Description,
                view.IsFavorite,
                SortField = view.SortField.ToDb(),
                SortDirection = view.SortDirection.ToDb(),
                view.DisplayColumns,
                view.HomeTagId,
                view.ModifiedAt,
            }));
    }

    public Task<View?> GetByIdAsync(string id)
    {
        return _db.RunAsync(async conn =>
        {
            var row = await conn.QuerySingleOrDefaultAsync<ViewRow>(
                $"{SelectViewColumns} WHERE id = @Id", new { Id = id }).ConfigureAwait(false);
            return ToView(row);
        });
    }

    public Task<IReadOnlyList<View>> GetAllAsync()
    {
        return _db.RunAsync<IReadOnlyList<View>>(async conn =>
        {
            var rows = await conn.QueryAsync<ViewRow>($"{SelectViewColumns} ORDER BY id").ConfigureAwait(false);
            return rows.Select(r => ToView(r)!).ToList();
        });
    }

    public Task UpdateAsync(View view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return _db.RunAsync(conn => conn.ExecuteAsync("""
            UPDATE views
            SET name = @Name, description = @Description, is_favorite = @IsFavorite, sort_field = @SortField,
                sort_direction = @SortDirection, display_columns = @DisplayColumns,
                home_tag_id = @HomeTagId, modified_at = @ModifiedAt
            WHERE id = @Id
            """,
            new
            {
                view.Id,
                view.Name,
                view.Description,
                view.IsFavorite,
                SortField = view.SortField.ToDb(),
                SortDirection = view.SortDirection.ToDb(),
                view.DisplayColumns,
                view.HomeTagId,
                view.ModifiedAt,
            }));
    }

    public Task DeleteAsync(string id)
    {
        return _db.RunAsync(conn => conn.ExecuteAsync("DELETE FROM views WHERE id = @Id", new { Id = id }));
    }

    public Task SetModifiedAtAsync(string viewId, string modifiedAt)
    {
        return _db.RunAsync(conn => conn.ExecuteAsync(
            "UPDATE views SET modified_at = @ModifiedAt WHERE id = @Id",
            new { Id = viewId, ModifiedAt = modifiedAt }));
    }

    // ---- view_conditions ----

    public Task AddConditionAsync(ViewCondition condition)
    {
        ArgumentNullException.ThrowIfNull(condition);
        return _db.RunAsync(conn => conn.ExecuteAsync("""
            INSERT INTO view_conditions (id, view_id, tag_id, operator, value, value2)
            VALUES (@Id, @ViewId, @TagId, @Operator, @Value, @Value2)
            """,
            new
            {
                condition.Id,
                condition.ViewId,
                condition.TagId,
                Operator = condition.Operator.ToDb(),
                condition.Value,
                condition.Value2,
            }));
    }

    public Task<ViewCondition?> GetConditionByIdAsync(string id)
    {
        return _db.RunAsync(async conn =>
        {
            var row = await conn.QuerySingleOrDefaultAsync<ConditionRow>(
                $"{SelectConditionColumns} WHERE id = @Id", new { Id = id }).ConfigureAwait(false);
            return ToCondition(row);
        });
    }

    public Task<IReadOnlyList<ViewCondition>> GetConditionsAsync(string viewId)
    {
        return _db.RunAsync<IReadOnlyList<ViewCondition>>(async conn =>
        {
            var rows = await conn.QueryAsync<ConditionRow>(
                $"{SelectConditionColumns} WHERE view_id = @ViewId ORDER BY id", new { ViewId = viewId })
                .ConfigureAwait(false);
            return rows.Select(r => ToCondition(r)!).ToList();
        });
    }

    public Task UpdateConditionAsync(ViewCondition condition)
    {
        ArgumentNullException.ThrowIfNull(condition);
        return _db.RunAsync(conn => conn.ExecuteAsync("""
            UPDATE view_conditions
            SET tag_id = @TagId, operator = @Operator, value = @Value, value2 = @Value2
            WHERE id = @Id
            """,
            new
            {
                condition.Id,
                condition.TagId,
                Operator = condition.Operator.ToDb(),
                condition.Value,
                condition.Value2,
            }));
    }

    public Task DeleteConditionAsync(string id)
    {
        return _db.RunAsync(conn => conn.ExecuteAsync(
            "DELETE FROM view_conditions WHERE id = @Id", new { Id = id }));
    }

    // ---- view_tag_hierarchies ----

    public Task AddNodeAsync(HierarchyNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return _db.RunAsync(conn => conn.ExecuteAsync("""
            INSERT INTO view_tag_hierarchies (id, view_id, tag_id, parent_id, position, alias, condition_type, condition_value)
            VALUES (@Id, @ViewId, @TagId, @ParentId, @Position, @Alias, @ConditionType, @ConditionValue)
            """,
            new
            {
                node.Id,
                node.ViewId,
                node.TagId,
                node.ParentId,
                node.Position,
                node.Alias,
                ConditionType = node.ConditionType.ToDb(),
                node.ConditionValue,
            }));
    }

    public Task<HierarchyNode?> GetNodeByIdAsync(string id)
    {
        return _db.RunAsync(async conn =>
        {
            var row = await conn.QuerySingleOrDefaultAsync<NodeRow>(
                $"{SelectNodeColumns} WHERE id = @Id", new { Id = id }).ConfigureAwait(false);
            return ToNode(row);
        });
    }

    public Task<IReadOnlyList<HierarchyNode>> GetHierarchyAsync(string viewId)
    {
        return _db.RunAsync<IReadOnlyList<HierarchyNode>>(async conn =>
        {
            var rows = await conn.QueryAsync<NodeRow>(
                $"{SelectNodeColumns} WHERE view_id = @ViewId ORDER BY position, id", new { ViewId = viewId })
                .ConfigureAwait(false);
            return rows.Select(r => ToNode(r)!).ToList();
        });
    }

    public Task UpdateNodeAsync(HierarchyNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return _db.RunAsync(conn => conn.ExecuteAsync("""
            UPDATE view_tag_hierarchies
            SET tag_id = @TagId, parent_id = @ParentId, position = @Position, alias = @Alias,
                condition_type = @ConditionType, condition_value = @ConditionValue
            WHERE id = @Id
            """,
            new
            {
                node.Id,
                node.TagId,
                node.ParentId,
                node.Position,
                node.Alias,
                ConditionType = node.ConditionType.ToDb(),
                node.ConditionValue,
            }));
    }

    public Task DeleteNodeAsync(string id)
    {
        // 子ノードの parent_id は FK SET NULL(仕様 §2.0)
        return _db.RunAsync(conn => conn.ExecuteAsync(
            "DELETE FROM view_tag_hierarchies WHERE id = @Id", new { Id = id }));
    }

    public Task ReplaceHierarchyAsync(
        string viewId, IReadOnlyList<HierarchyNode> nodes, string? homeNodeId, string modifiedAt)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        // 階層の一括置換(v1.2 バッチ保存): 単一トランザクションで全置換+home_tag_id+modified_at を 1 回更新
        // (REQ-027 の原子性意味論・REQ-032 の保存時 1 回更新)
        return _db.RunAsync(async conn =>
        {
            using var tx = conn.BeginTransaction();
            try
            {
                await conn.ExecuteAsync(
                    "DELETE FROM view_tag_hierarchies WHERE view_id = @ViewId",
                    new { ViewId = viewId }, tx).ConfigureAwait(false);

                // parent_id の自己参照 FK は即時検査のため、親→子の順(トポロジカル順)で挿入する
                foreach (var node in OrderParentsFirst(nodes))
                {
                    await conn.ExecuteAsync("""
                        INSERT INTO view_tag_hierarchies (id, view_id, tag_id, parent_id, position, alias, condition_type, condition_value)
                        VALUES (@Id, @ViewId, @TagId, @ParentId, @Position, @Alias, @ConditionType, @ConditionValue)
                        """,
                        new
                        {
                            node.Id,
                            node.ViewId,
                            node.TagId,
                            node.ParentId,
                            node.Position,
                            node.Alias,
                            ConditionType = node.ConditionType.ToDb(),
                            node.ConditionValue,
                        }, tx).ConfigureAwait(false);
                }

                await conn.ExecuteAsync(
                    "UPDATE views SET home_tag_id = @HomeNodeId, modified_at = @ModifiedAt WHERE id = @ViewId",
                    new { ViewId = viewId, HomeNodeId = homeNodeId, ModifiedAt = modifiedAt }, tx)
                    .ConfigureAwait(false);

                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        });
    }

    /// <summary>親が先に来る順序へ並べ替える(自己参照 FK 対応)。循環・参照切れ親の残余は末尾に回す。</summary>
    private static List<HierarchyNode> OrderParentsFirst(IReadOnlyList<HierarchyNode> nodes)
    {
        var remaining = new List<HierarchyNode>(nodes);
        var emitted = new HashSet<string>(StringComparer.Ordinal);
        var ordered = new List<HierarchyNode>(nodes.Count);
        var ids = nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);

        while (remaining.Count > 0)
        {
            var progressed = false;
            for (var i = 0; i < remaining.Count;)
            {
                var node = remaining[i];
                if (node.ParentId is null || emitted.Contains(node.ParentId) || !ids.Contains(node.ParentId))
                {
                    ordered.Add(node);
                    emitted.Add(node.Id);
                    remaining.RemoveAt(i);
                    progressed = true;
                }
                else
                {
                    i++;
                }
            }

            if (!progressed)
            {
                ordered.AddRange(remaining); // 循環(サービス層で拒否済み)の防御: FK 違反として表面化させる
                break;
            }
        }

        return ordered;
    }

    /// <summary>SQLite ネイティブ型(INTEGER=long)での受け取り行。bool/int への変換は ToXxx で行う。</summary>
    private sealed record ViewRow(
        string Id, string Name, string? Description, long IsFavorite, string SortField, string SortDirection,
        string? DisplayColumns, string? HomeTagId, string ModifiedAt);

    private sealed record ConditionRow(
        string Id, string ViewId, string? TagId, string Operator, string? Value, string? Value2);

    private sealed record NodeRow(
        string Id, string ViewId, string TagId, string? ParentId, long Position, string? Alias,
        string? ConditionType, string? ConditionValue);

    private static View? ToView(ViewRow? row)
    {
        return row is null
            ? null
            : new View
            {
                Id = row.Id,
                Name = row.Name,
                Description = row.Description,
                IsFavorite = row.IsFavorite != 0,
                SortField = DbMapping.ToSortField(row.SortField),
                SortDirection = DbMapping.ToSortDirection(row.SortDirection),
                DisplayColumns = row.DisplayColumns,
                HomeTagId = row.HomeTagId,
                ModifiedAt = row.ModifiedAt,
            };
    }

    private static ViewCondition? ToCondition(ConditionRow? row)
    {
        return row is null
            ? null
            : new ViewCondition
            {
                Id = row.Id,
                ViewId = row.ViewId,
                TagId = row.TagId,
                Operator = DbMapping.ToConditionOperator(row.Operator),
                Value = row.Value,
                Value2 = row.Value2,
            };
    }

    private static HierarchyNode? ToNode(NodeRow? row)
    {
        return row is null
            ? null
            : new HierarchyNode
            {
                Id = row.Id,
                ViewId = row.ViewId,
                TagId = row.TagId,
                ParentId = row.ParentId,
                Position = (int)row.Position,
                Alias = row.Alias,
                ConditionType = DbMapping.ToHierarchyConditionType(row.ConditionType),
                ConditionValue = row.ConditionValue,
            };
    }
}
