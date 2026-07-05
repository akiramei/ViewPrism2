using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Repositories;

namespace ViewPrism2.Core.Services;

/// <summary>
/// ビュー管理サービス(M-VIEWSVC-012、仕様 §2.3/2.4)。
/// CRUD・modified_at 規則(REQ-032: 本体・条件・階層の変更で更新、閲覧では不変)・
/// お気に入り/最近(REQ-033)・階層ノード CRUD+循環拒否(REQ-034 / INV-004)。
/// </summary>
public sealed class ViewService
{
    private readonly IViewRepository _views;
    private readonly IClock _clock;
    private readonly ITagRepository? _tags;

    /// <summary>
    /// tags は階層保存の参照切れ検証用(REQ-083/ECO-046)。optional 注入は既存テスト互換のため
    /// (V4 CHEAT-01 前例)— production DI は必ず注入する。null 時は保存前検証をスキップし
    /// FK が最後の砦になる(未処理例外に戻る)ため、新規呼び出しは注入すること。
    /// </summary>
    public ViewService(IViewRepository views, IClock clock, ITagRepository? tags = null)
    {
        _views = views;
        _clock = clock;
        _tags = tags;
    }

    // ---- ビュー本体(REQ-030/032) ----

    /// <summary>ビュー作成。name 必須(空白のみ拒否)。description は v1.2 ダイアログの「説明」(null 可)。</summary>
    public async Task<Result<View>> CreateAsync(
        string name,
        bool isFavorite = false,
        SortField sortField = SortField.Name,
        SortDirection sortDirection = SortDirection.Asc,
        string? displayColumns = null,
        string? homeTagId = null,
        string? description = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result<View>.Fail(ErrorCode.ValidationError, "ビュー名が空白のみです。");
        }

        var view = new View
        {
            Id = IdGenerator.NewId(),
            Name = name,
            Description = description,
            IsFavorite = isFavorite,
            SortField = sortField,
            SortDirection = sortDirection,
            DisplayColumns = displayColumns,
            HomeTagId = homeTagId,
            ModifiedAt = _clock.UtcNowIso(),
        };
        await _views.AddAsync(view).ConfigureAwait(false);
        return Result<View>.Ok(view);
    }

    /// <summary>ビュー更新(本体の変更 → modified_at 更新)。</summary>
    public async Task<Result<View>> UpdateAsync(View view)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (string.IsNullOrWhiteSpace(view.Name))
        {
            return Result<View>.Fail(ErrorCode.ValidationError, "ビュー名が空白のみです。");
        }

        if (await _views.GetByIdAsync(view.Id).ConfigureAwait(false) is null)
        {
            return Result<View>.Fail(ErrorCode.NotFound, "ビューが存在しません。");
        }

        var updated = view with { ModifiedAt = _clock.UtcNowIso() };
        await _views.UpdateAsync(updated).ConfigureAwait(false);
        return Result<View>.Ok(updated);
    }

    /// <summary>ビュー削除(条件・階層は FK CASCADE で連鎖削除)。</summary>
    public async Task<Result> DeleteAsync(string id)
    {
        if (await _views.GetByIdAsync(id).ConfigureAwait(false) is null)
        {
            return Result.Fail(ErrorCode.NotFound, "ビューが存在しません。");
        }

        await _views.DeleteAsync(id).ConfigureAwait(false);
        return Result.Ok();
    }

    /// <summary>取得(閲覧)。modified_at は更新しない(REQ-032)。</summary>
    public Task<View?> GetAsync(string id) => _views.GetByIdAsync(id);

    /// <summary>全ビュー一覧(閲覧。v1.2 タグタブ「ビュー管理」の供給元)。modified_at は更新しない。</summary>
    public Task<IReadOnlyList<View>> GetAllAsync() => _views.GetAllAsync();

    /// <summary>お気に入り一覧: is_favorite=true を name 昇順(同値 id 昇順)(REQ-033)。</summary>
    public async Task<IReadOnlyList<View>> GetFavoritesAsync()
    {
        var all = await _views.GetAllAsync().ConfigureAwait(false);
        return all
            .Where(v => v.IsFavorite)
            .OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(v => v.Id, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>最近一覧: modified_at 降順 limit 件(既定 10、同値 id 昇順)(REQ-033)。</summary>
    public async Task<IReadOnlyList<View>> GetRecentAsync(int limit = 10)
    {
        var all = await _views.GetAllAsync().ConfigureAwait(false);
        return all
            .OrderByDescending(v => v.ModifiedAt, StringComparer.Ordinal)
            .ThenBy(v => v.Id, StringComparer.Ordinal)
            .Take(limit)
            .ToList();
    }

    // ---- 絞り込み条件(REQ-031 の入力管理) ----

    public async Task<Result<ViewCondition>> AddConditionAsync(
        string viewId, string? tagId, ConditionOperator op, string? value = null, string? value2 = null)
    {
        if (await _views.GetByIdAsync(viewId).ConfigureAwait(false) is null)
        {
            return Result<ViewCondition>.Fail(ErrorCode.NotFound, "ビューが存在しません。");
        }

        var condition = new ViewCondition
        {
            Id = IdGenerator.NewId(),
            ViewId = viewId,
            TagId = tagId,
            Operator = op,
            Value = value,
            Value2 = value2,
        };
        await _views.AddConditionAsync(condition).ConfigureAwait(false);
        await TouchAsync(viewId).ConfigureAwait(false);
        return Result<ViewCondition>.Ok(condition);
    }

    public async Task<Result> UpdateConditionAsync(ViewCondition condition)
    {
        ArgumentNullException.ThrowIfNull(condition);
        if (await _views.GetConditionByIdAsync(condition.Id).ConfigureAwait(false) is null)
        {
            return Result.Fail(ErrorCode.NotFound, "条件が存在しません。");
        }

        await _views.UpdateConditionAsync(condition).ConfigureAwait(false);
        await TouchAsync(condition.ViewId).ConfigureAwait(false);
        return Result.Ok();
    }

    public async Task<Result> DeleteConditionAsync(string conditionId)
    {
        var condition = await _views.GetConditionByIdAsync(conditionId).ConfigureAwait(false);
        if (condition is null)
        {
            return Result.Fail(ErrorCode.NotFound, "条件が存在しません。");
        }

        await _views.DeleteConditionAsync(conditionId).ConfigureAwait(false);
        await TouchAsync(condition.ViewId).ConfigureAwait(false);
        return Result.Ok();
    }

    /// <summary>条件一覧(閲覧)。modified_at は更新しない。</summary>
    public Task<IReadOnlyList<ViewCondition>> GetConditionsAsync(string viewId) => _views.GetConditionsAsync(viewId);

    // ---- タグ階層ノード(REQ-034) ----

    public async Task<Result<HierarchyNode>> AddNodeAsync(
        string viewId,
        string tagId,
        string? parentId,
        int position,
        string? alias = null,
        HierarchyConditionType? conditionType = null,
        string? conditionValue = null)
    {
        if (await _views.GetByIdAsync(viewId).ConfigureAwait(false) is null)
        {
            return Result<HierarchyNode>.Fail(ErrorCode.NotFound, "ビューが存在しません。");
        }

        if (parentId is not null)
        {
            var parent = await _views.GetNodeByIdAsync(parentId).ConfigureAwait(false);
            if (parent is null || !string.Equals(parent.ViewId, viewId, StringComparison.Ordinal))
            {
                return Result<HierarchyNode>.Fail(ErrorCode.NotFound, "親ノードが存在しません。");
            }
        }

        var node = new HierarchyNode
        {
            Id = IdGenerator.NewId(),
            ViewId = viewId,
            TagId = tagId,
            ParentId = parentId,
            Position = position,
            Alias = alias,
            ConditionType = conditionType,
            ConditionValue = conditionValue,
        };
        await _views.AddNodeAsync(node).ConfigureAwait(false);
        await TouchAsync(viewId).ConfigureAwait(false);
        return Result<HierarchyNode>.Ok(node);
    }

    /// <summary>ノード更新(alias・条件等)。親の変更を含む場合は循環を拒否(INV-004)。</summary>
    public async Task<Result> UpdateNodeAsync(HierarchyNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var current = await _views.GetNodeByIdAsync(node.Id).ConfigureAwait(false);
        if (current is null)
        {
            return Result.Fail(ErrorCode.NotFound, "ノードが存在しません。");
        }

        if (!string.Equals(current.ParentId, node.ParentId, StringComparison.Ordinal) &&
            await CreatesCycleAsync(node.Id, node.ParentId, node.ViewId).ConfigureAwait(false))
        {
            return Result.Fail(ErrorCode.CircularReference, "自己または子孫を親に指定することはできません。");
        }

        await _views.UpdateNodeAsync(node).ConfigureAwait(false);
        await TouchAsync(node.ViewId).ConfigureAwait(false);
        return Result.Ok();
    }

    /// <summary>ノード移動。自己・子孫を親に指定 → CircularReference(REQ-034)。</summary>
    public async Task<Result> MoveNodeAsync(string nodeId, string? newParentId, int newPosition)
    {
        var node = await _views.GetNodeByIdAsync(nodeId).ConfigureAwait(false);
        if (node is null)
        {
            return Result.Fail(ErrorCode.NotFound, "ノードが存在しません。");
        }

        if (await CreatesCycleAsync(nodeId, newParentId, node.ViewId).ConfigureAwait(false))
        {
            return Result.Fail(ErrorCode.CircularReference, "自己または子孫を親に指定することはできません。");
        }

        await _views.UpdateNodeAsync(node with { ParentId = newParentId, Position = newPosition })
            .ConfigureAwait(false);
        await TouchAsync(node.ViewId).ConfigureAwait(false);
        return Result.Ok();
    }

    public async Task<Result> DeleteNodeAsync(string nodeId)
    {
        var node = await _views.GetNodeByIdAsync(nodeId).ConfigureAwait(false);
        if (node is null)
        {
            return Result.Fail(ErrorCode.NotFound, "ノードが存在しません。");
        }

        await _views.DeleteNodeAsync(nodeId).ConfigureAwait(false);
        await TouchAsync(node.ViewId).ConfigureAwait(false);
        return Result.Ok();
    }

    /// <summary>階層一覧(閲覧)。modified_at は更新しない。</summary>
    public Task<IReadOnlyList<HierarchyNode>> GetHierarchyAsync(string viewId) => _views.GetHierarchyAsync(viewId);

    /// <summary>
    /// 配置タグ数(=階層ノード数)を取得する(ECO-007/E1 ビュー行のタグ数バッジ)。閲覧のみ。
    /// </summary>
    public async Task<int> GetHierarchyCountAsync(string viewId)
    {
        var nodes = await _views.GetHierarchyAsync(viewId).ConfigureAwait(false);
        return nodes.Count;
    }

    /// <summary>
    /// タグ階層の一括置換保存(v1.2 階層エディタのバッチ保存)。
    /// メモリ内編集の結果を単一トランザクションで全置換し、ホームタグ(home_tag_id=階層ノード id)と
    /// modified_at を保存時に 1 回だけ更新する(REQ-032)。循環・不正親参照は拒否(INV-004)。
    /// </summary>
    public async Task<Result> SaveHierarchyAsync(
        string viewId, IReadOnlyList<HierarchyNode> nodes, string? homeNodeId)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        if (await _views.GetByIdAsync(viewId).ConfigureAwait(false) is null)
        {
            return Result.Fail(ErrorCode.NotFound, "ビューが存在しません。");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            if (!string.Equals(node.ViewId, viewId, StringComparison.Ordinal))
            {
                return Result.Fail(ErrorCode.ValidationError, "別ビューのノードが含まれています。");
            }

            if (!ids.Add(node.Id))
            {
                return Result.Fail(ErrorCode.ValidationError, "ノード id が重複しています。");
            }
        }

        var byId = nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            if (node.ParentId is { } parentId && !byId.ContainsKey(parentId))
            {
                return Result.Fail(ErrorCode.ValidationError, "親ノードが集合内に存在しません。");
            }

            // 自己・祖先経路に自分が現れたら循環(INV-004)
            var seen = new HashSet<string>(StringComparer.Ordinal) { node.Id };
            var cursor = node.ParentId;
            while (cursor is not null)
            {
                if (!seen.Add(cursor))
                {
                    return Result.Fail(ErrorCode.CircularReference, "階層に循環があります。");
                }

                cursor = byId[cursor].ParentId;
            }
        }

        // 参照切れタグの配置は FK 例外でなく Result で拒否(REQ-083/ECO-046: 書き込み経路の
        // 参照切れ耐性= INV-008 の書き込み版。未保存編集中にタグ定義が消えた場合の最後の防御層)
        if (_tags is not null && nodes.Count > 0)
        {
            var known = (await _tags.GetAllAsync().ConfigureAwait(false))
                .Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
            if (nodes.Any(n => !known.Contains(n.TagId)))
            {
                return Result.Fail(ErrorCode.NotFound, "削除済みタグの配置が含まれています。");
            }
        }

        // 参照切れホームは保存しない(REQ-037 のフォールバック方針に合わせて null 化。エラーにしない)
        var home = homeNodeId is not null && byId.ContainsKey(homeNodeId) ? homeNodeId : null;
        await _views.ReplaceHierarchyAsync(viewId, nodes, home, _clock.UtcNowIso()).ConfigureAwait(false);
        return Result.Ok();
    }

    /// <summary>modified_at 更新(REQ-032: 本体・条件・階層のいずれの変更でも更新)。</summary>
    private Task TouchAsync(string viewId) => _views.SetModifiedAtAsync(viewId, _clock.UtcNowIso());

    /// <summary>親付け替えが循環(自己・子孫を親に指定)を作るか(INV-004)。</summary>
    private async Task<bool> CreatesCycleAsync(string nodeId, string? newParentId, string viewId)
    {
        if (newParentId is null)
        {
            return false;
        }

        var nodes = await _views.GetHierarchyAsync(viewId).ConfigureAwait(false);
        var byId = nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var cursor = newParentId;
        while (cursor is not null)
        {
            if (string.Equals(cursor, nodeId, StringComparison.Ordinal))
            {
                return true;
            }

            if (!seen.Add(cursor) || !byId.TryGetValue(cursor, out var parent))
            {
                return false; // 既存循環・参照切れは別経路で防御(INV-008)
            }

            cursor = parent.ParentId;
        }

        return false;
    }
}
