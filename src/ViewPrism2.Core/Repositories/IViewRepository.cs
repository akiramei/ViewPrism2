using ViewPrism2.Core.Models;

namespace ViewPrism2.Core.Repositories;

/// <summary>ビュー・条件・タグ階層の永続化(M-DB-007。インターフェースは Core、実装は Infrastructure)。</summary>
public interface IViewRepository
{
    Task AddAsync(View view);

    Task<View?> GetByIdAsync(string id);

    Task<IReadOnlyList<View>> GetAllAsync();

    Task UpdateAsync(View view);

    /// <summary>削除する。条件・階層ノードは FK CASCADE で連鎖削除(REQ-030)。</summary>
    Task DeleteAsync(string id);

    /// <summary>modified_at のみ更新する(REQ-032 の更新トリガ実現用)。</summary>
    Task SetModifiedAtAsync(string viewId, string modifiedAt);

    Task AddConditionAsync(ViewCondition condition);

    Task<ViewCondition?> GetConditionByIdAsync(string id);

    Task<IReadOnlyList<ViewCondition>> GetConditionsAsync(string viewId);

    Task UpdateConditionAsync(ViewCondition condition);

    Task DeleteConditionAsync(string id);

    Task AddNodeAsync(HierarchyNode node);

    Task<HierarchyNode?> GetNodeByIdAsync(string id);

    /// <summary>ビューの全階層ノード(position 昇順・同値 id 昇順)。</summary>
    Task<IReadOnlyList<HierarchyNode>> GetHierarchyAsync(string viewId);

    Task UpdateNodeAsync(HierarchyNode node);

    /// <summary>削除する。子ノードの parent_id は FK SET NULL(仕様 §2.0)。</summary>
    Task DeleteNodeAsync(string id);
}
