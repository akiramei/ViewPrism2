using ViewPrism2.Core.Services.Viewer;
using Xunit;

namespace ViewPrism2.Oracle;

/// <summary>
/// S-32: タグアクション解決(OC-23・spec §2.12.1・TC-3、EQ-001)。設計者受入=工場非開示の独立導出。
/// 全順序 skip&gt;spread&gt;forceLeftPage&gt;forceRightPage&gt;leftPageEmpty&gt;rightPageEmpty で支配 1 つ。
/// 未マッピング/map に無い/現存しない tag_id/タグ無しは null。
/// </summary>
[Trait("oracle", "S-32")]
public sealed class S32TagActionResolveTests
{
    // 完全マッピング(各アクション→専用 tag_id)。
    private static IReadOnlyDictionary<ViewerTagAction, string?> FullMap() => new Dictionary<ViewerTagAction, string?>
    {
        [ViewerTagAction.Skip] = "t-skip",
        [ViewerTagAction.Spread] = "t-spread",
        [ViewerTagAction.ForceLeftPage] = "t-fl",
        [ViewerTagAction.ForceRightPage] = "t-fr",
        [ViewerTagAction.LeftPageEmpty] = "t-le",
        [ViewerTagAction.RightPageEmpty] = "t-re",
    };

    private static ViewerTagAction? Resolve(params string[] tagIds) =>
        TagActionResolver.Resolve(tagIds, FullMap());

    [Fact]
    public void 全順序の支配_競合する複数制御タグで強い方が勝つ()
    {
        // (1) skip と spread 両方 → skip(最上位)
        Assert.Equal(ViewerTagAction.Skip, Resolve("t-skip", "t-spread"));
        // (2) spread と forceLeftPage → spread
        Assert.Equal(ViewerTagAction.Spread, Resolve("t-spread", "t-fl"));
        // (3) forceRightPage と leftPageEmpty → forceRightPage
        Assert.Equal(ViewerTagAction.ForceRightPage, Resolve("t-fr", "t-le"));
        // 隣接対の確認
        Assert.Equal(ViewerTagAction.ForceLeftPage, Resolve("t-fl", "t-fr"));
        Assert.Equal(ViewerTagAction.LeftPageEmpty, Resolve("t-le", "t-re"));
        // 全 6 種付与 → skip
        Assert.Equal(ViewerTagAction.Skip, Resolve("t-skip", "t-spread", "t-fl", "t-fr", "t-le", "t-re"));
    }

    [Fact]
    public void 単一マッピングは当該アクションに解決する()
    {
        Assert.Equal(ViewerTagAction.ForceLeftPage, Resolve("t-fl"));
        Assert.Equal(ViewerTagAction.RightPageEmpty, Resolve("t-re"));
        Assert.Equal(ViewerTagAction.Spread, Resolve("t-spread"));
    }

    [Fact]
    public void 未マッピング_不在_タグ無しは無し()
    {
        // (4) マッピングに無いタグのみ → 無し
        Assert.Null(Resolve("unmapped-tag"));
        // (6) タグ無し → 無し
        Assert.Null(Resolve());
        // map に無い画像タグ(混在しても支配タグが無ければ無し)
        Assert.Null(Resolve("x", "y", "z"));
    }

    [Fact]
    public void 現存しないtag_idを指すマッピングは画像タグ基準で自然無視()
    {
        // (5) map は削除済みタグ "t-deleted" を skip に割当てているが、画像はそれを持たない
        var mapWithDeleted = new Dictionary<ViewerTagAction, string?>
        {
            [ViewerTagAction.Skip] = "t-deleted",      // 現存しない tag_id
            [ViewerTagAction.ForceRightPage] = "t-fr",
        };
        // 画像タグ={t-fr} → forceRightPage(skip は画像が t-deleted を持たないため発火しない)
        Assert.Equal(ViewerTagAction.ForceRightPage, TagActionResolver.Resolve(new[] { "t-fr" }, mapWithDeleted));
        // 画像タグ={other} → 無し
        Assert.Null(TagActionResolver.Resolve(new[] { "other" }, mapWithDeleted));
    }

    [Fact]
    public void 未割り当て_nullマッピングは無視される()
    {
        var partial = new Dictionary<ViewerTagAction, string?>
        {
            [ViewerTagAction.Skip] = null,             // 未割り当て
            [ViewerTagAction.ForceLeftPage] = "t-fl",
        };
        Assert.Equal(ViewerTagAction.ForceLeftPage, TagActionResolver.Resolve(new[] { "t-fl" }, partial));
        // skip は未割り当てのため、画像が何を持っても skip に解決しない
        Assert.Null(TagActionResolver.Resolve(new[] { "t-skip" }, partial));
    }
}
