using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using Xunit;

namespace ViewPrism2.Oracle;

/// <summary>
/// S-39: 参照切れタグを含むビュー階層保存の拒否(REQ-083 / TAG-008 U-a 裁定・ECO-046、EQ-001)。
/// 存在しないタグを参照するノードを含む保存は NotFound の Result で拒否(FK 違反の
/// 未処理例外にしない)・既存階層は無傷。現存タグのみの保存は成功(回帰)。
/// 書き込み経路の参照切れ耐性= INV-008(読み取り耐性)の書き込み版。
/// </summary>
[Trait("oracle", "S-39")]
public sealed class S39StaleTagHierarchySaveTests
{
    [Fact]
    public async Task 参照切れタグを含む階層保存は拒否され現存タグのみなら成功する()
    {
        using var db = new OracleDb();
        // tags 注入版(production DI と同じ形。未注入は旧テスト互換の縮退= FK が最後の砦)
        var views = new ViewService(db.Views, db.Clock, db.Tags);

        var live = new Tag { Id = IdGenerator.NewId(), Name = "Live", Type = TagType.Simple };
        await db.Tags.AddAsync(live);
        var view = (await views.CreateAsync("oracle view")).Value!;

        HierarchyNode Node(string tagId, int position) => new()
        {
            Id = IdGenerator.NewId(),
            ViewId = view.Id,
            TagId = tagId,
            Position = position,
        };

        // --- 参照切れ(不存在 tag_id)を含む保存= NotFound・無傷 ---
        var result = await views.SaveHierarchyAsync(
            view.Id, new[] { Node(live.Id, 0), Node("ghost-tag-id", 1) }, null);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.NotFound, result.Error);
        Assert.Empty(await views.GetHierarchyAsync(view.Id));   // 部分適用なし

        // --- 現存タグのみの保存= 成功(回帰) ---
        var ok = await views.SaveHierarchyAsync(view.Id, new[] { Node(live.Id, 0) }, null);
        Assert.True(ok.IsSuccess);
        Assert.Single(await views.GetHierarchyAsync(view.Id));
    }
}
