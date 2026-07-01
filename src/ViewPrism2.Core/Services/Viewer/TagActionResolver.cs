namespace ViewPrism2.Core.Services.Viewer;

/// <summary>
/// タグアクション解決器(OC-23・仕様 §2.12.1)。画像のタグ ID 集合とマッピング
/// <c>action→tag_id?</c> から支配アクション 1 つを決定する。ECO-022 新設・純粋計算 Core
/// (M-TAGCTRL-028)。
/// </summary>
public static class TagActionResolver
{
    /// <summary>
    /// 全順序の支配優先(仕様 §2.12.1・TC-3):
    /// <c>skip &gt; spread &gt; forceLeftPage &gt; forceRightPage &gt; leftPageEmpty &gt; rightPageEmpty</c>。
    /// 配列の先頭ほど強い。
    /// </summary>
    private static readonly ViewerTagAction[] DominanceOrder =
    {
        ViewerTagAction.Skip,
        ViewerTagAction.Spread,
        ViewerTagAction.ForceLeftPage,
        ViewerTagAction.ForceRightPage,
        ViewerTagAction.LeftPageEmpty,
        ViewerTagAction.RightPageEmpty,
    };

    /// <summary>
    /// 画像のタグ ID 集合とマッピングから支配アクションを解決する。
    /// 画像のタグ ID にマッピング先 tag_id が含まれるアクションを収集し、全順序で支配 1 つを返す。
    /// 該当なし(タグ無し・未マッピング・map に無い・現存しない tag_id)は <c>null</c>=アクション無し。
    /// </summary>
    /// <remarks>
    /// 入力はタグ ID 集合とマッピングのみ。タグ存在台帳は取らない(M-TAGCTRL-028 major-1 補正)。
    /// 削除済みタグを指すマッピングは「画像が当該 tag_id を持たない=自然に無視」で結果整合する。
    /// 現存タグでマッピングを濾す責務は UI 層(picker=GetAllAsync 現存のみ)。
    /// 決定的(原典の非決定的探索ループは採らない — ECO-022 §0.1)。
    /// </remarks>
    /// <param name="tagIds">画像に付与されたタグの ID 集合。</param>
    /// <param name="map">アクション→割り当て tag_id(<c>null</c>=未割り当て)。</param>
    public static ViewerTagAction? Resolve(
        IReadOnlyCollection<string> tagIds,
        IReadOnlyDictionary<ViewerTagAction, string?> map)
    {
        ArgumentNullException.ThrowIfNull(tagIds);
        ArgumentNullException.ThrowIfNull(map);

        if (tagIds.Count == 0)
        {
            return null;
        }

        // 高速な所属判定のため一度だけ HashSet 化(序数比較=tag_id は UUID 小文字)。
        var owned = tagIds as HashSet<string> ?? new HashSet<string>(tagIds, StringComparer.Ordinal);

        // 全順序の強い方から走査し、最初に一致したアクションを返す(支配 1 つ・決定的)。
        foreach (var action in DominanceOrder)
        {
            if (map.TryGetValue(action, out var tagId)
                && tagId is not null
                && owned.Contains(tagId))
            {
                return action;
            }
        }

        return null;
    }
}
