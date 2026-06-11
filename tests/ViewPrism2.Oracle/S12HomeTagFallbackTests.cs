using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using Xunit;

namespace ViewPrism2.Oracle;

/// <summary>
/// S-12: ホームタグ参照切れ(spec §2.4 REQ-037、EQ-001)。
/// home_tag_id が指す階層ノードを削除後にビューを開くと、ルートから開始しエラーにしない。
/// </summary>
[Trait("oracle", "S-12")]
public sealed class S12HomeTagFallbackTests
{
    [Fact]
    public async Task ホームノード削除後はルートから開始しエラーにしない()
    {
        using var db = new OracleDb();

        var tagHome = new Tag { Id = IdGenerator.NewId(), Name = "Home", Type = TagType.Simple };
        var tagOther = new Tag { Id = IdGenerator.NewId(), Name = "Other", Type = TagType.Simple };
        await db.Tags.AddAsync(tagHome);
        await db.Tags.AddAsync(tagOther);

        var views = new ViewService(db.Views, db.Clock);
        var view = (await views.CreateAsync("oracle home view")).Value!;
        var homeNode = (await views.AddNodeAsync(view.Id, tagHome.Id, parentId: null, position: 0)).Value!;
        var otherNode = (await views.AddNodeAsync(view.Id, tagOther.Id, parentId: null, position: 1)).Value!;
        Assert.True((await views.UpdateAsync(view with { HomeTagId = homeNode.Id })).IsSuccess);

        var builder = new NodeGraphBuilder();
        var emptyValues = TagValueIndex.FromValues(
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal));
        var tagsById = (await db.Tags.GetAllAsync()).ToDictionary(t => t.Id, StringComparer.Ordinal);

        // 正の対照: 削除前は home_tag_id が該当ノードへ解決される
        var before = builder.BuildGraph(await views.GetHierarchyAsync(view.Id), tagsById, emptyValues);
        var resolvedBefore = builder.ResolveHome(before.Root, homeNode.Id);
        Assert.NotNull(resolvedBefore);
        Assert.Equal(homeNode.Id, resolvedBefore.HierarchyNodeId);

        // --- home_tag_id が指す階層ノードを削除 ---
        Assert.True((await views.DeleteNodeAsync(homeNode.Id)).IsSuccess);

        // --- ビューを開く(例外が出ればテスト失敗として記録される) ---
        var reloaded = await views.GetAsync(view.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(homeNode.Id, reloaded.HomeTagId); // 参照切れの home_tag_id が残っている状況

        var after = builder.BuildGraph(await views.GetHierarchyAsync(view.Id), tagsById, emptyValues);

        // 解決不能 → null(呼び出し側はルートから開始、REQ-037)。エラーにしない
        var resolvedAfter = builder.ResolveHome(after.Root, reloaded.HomeTagId);
        Assert.Null(resolvedAfter);

        // ルートは通常どおり利用可能(残ノードが展開されている)
        Assert.Equal(NodeKind.Root, after.Root.Kind);
        var remaining = Assert.Single(after.Root.Children);
        Assert.Equal(otherNode.Id, remaining.HierarchyNodeId);
    }
}
