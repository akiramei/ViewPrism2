using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// CP-VIEW-012(Run 4 追加分): ビューの description(REQ-030 / v1.2 ダイアログ)と
/// タグ階層の一括置換保存(SaveHierarchyAsync — v1.2 バッチ保存の core 意味論)。
/// 原子性・modified_at 1 回更新(REQ-032)・循環拒否(INV-004)を exact 検査する。
/// </summary>
[Trait("cp", "CP-VIEW-012")]
public sealed class CpView012HierarchySaveTests : IDisposable
{
    private readonly FakeClock _clock = new(new DateTime(2026, 6, 11, 0, 0, 0));
    private readonly TempDb _db;
    private readonly ViewService _views;
    private readonly TagService _tags;

    public CpView012HierarchySaveTests()
    {
        _db = new TempDb(_clock);
        _views = new ViewService(_db.Views, _clock);
        _tags = new TagService(_db.Tags);
    }

    public void Dispose() => _db.Dispose();

    private async Task<Tag> NewTagAsync(string name)
        => (await _tags.CreateAsync(name, TagType.Simple)).Value!;

    private HierarchyNode NewNode(string viewId, string tagId, string? parentId = null, int position = 0)
        => new()
        {
            Id = IdGenerator.NewId(),
            ViewId = viewId,
            TagId = tagId,
            ParentId = parentId,
            Position = position,
        };

    // ---- 参照切れタグの保存拒否(REQ-083 / ECO-046・TAG-008 U-a 裁定の Core 防御層) ----

    [Fact]
    public async Task 削除済みタグの配置を含む保存はNotFoundで拒否されDBは無傷()
    {
        // ECO-046 プローブ: 是正前は view_tag_hierarchies.tag_id の FK 違反が未処理例外で伝播していた
        // (是正前実測= ViewService に ITagRepository 注入口が無い CS1729)
        var viewsGuarded = new ViewService(_db.Views, _clock, _db.Tags);
        var view = (await viewsGuarded.CreateAsync("V")).Value!;
        var live = await NewTagAsync("live");
        var nodes = new[]
        {
            NewNode(view.Id, live.Id, position: 0),
            NewNode(view.Id, "ghost-tag-id", position: 1),   // 存在しないタグ(削除済み相当)
        };

        var result = await viewsGuarded.SaveHierarchyAsync(view.Id, nodes, null);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.NotFound, result.Error);
        Assert.Empty(await _views.GetHierarchyAsync(view.Id));   // 部分適用なし(無傷)
    }

    [Fact]
    public async Task descriptionは作成更新でラウンドトリップする()
    {
        var created = await _views.CreateAsync("V", description: "最初の説明");
        Assert.True(created.IsSuccess);
        Assert.Equal("最初の説明", (await _views.GetAsync(created.Value!.Id))!.Description);

        var updated = await _views.UpdateAsync(created.Value with { Description = "改訂した説明" });
        Assert.True(updated.IsSuccess);
        Assert.Equal("改訂した説明", (await _views.GetAsync(created.Value.Id))!.Description);

        var cleared = await _views.UpdateAsync(created.Value with { Description = null });
        Assert.True(cleared.IsSuccess);
        Assert.Null((await _views.GetAsync(created.Value.Id))!.Description);
    }

    [Fact]
    public async Task 一括置換保存は旧階層を全て置き換えhome_tag_idとmodified_atを1回更新する()
    {
        var view = (await _views.CreateAsync("V")).Value!;
        var tagA = await NewTagAsync("A");
        var tagB = await NewTagAsync("B");

        // 旧階層(置換前)
        await _views.AddNodeAsync(view.Id, tagA.Id, null, 0);
        Assert.Single(await _views.GetHierarchyAsync(view.Id));

        // 新階層: root(B) → child(A)
        var root = NewNode(view.Id, tagB.Id);
        var child = NewNode(view.Id, tagA.Id, root.Id);
        _clock.Advance(TimeSpan.FromMinutes(5));
        var saveTime = _clock.UtcNowIso();
        var result = await _views.SaveHierarchyAsync(view.Id, [root, child], root.Id);

        Assert.True(result.IsSuccess);
        var nodes = await _views.GetHierarchyAsync(view.Id);
        Assert.Equal(2, nodes.Count);
        Assert.Contains(nodes, n => n.Id == root.Id && n.ParentId is null);
        Assert.Contains(nodes, n => n.Id == child.Id && n.ParentId == root.Id);

        var saved = (await _views.GetAsync(view.Id))!;
        Assert.Equal(root.Id, saved.HomeTagId);
        Assert.Equal(saveTime, saved.ModifiedAt); // 保存時に 1 回(REQ-032)
    }

    [Fact]
    public async Task 循環や集合外の親は拒否され何も適用されない()
    {
        var view = (await _views.CreateAsync("V")).Value!;
        var tag = await NewTagAsync("A");
        await _views.AddNodeAsync(view.Id, tag.Id, null, 0); // 既存階層(置換されないこと)
        var before = await _views.GetHierarchyAsync(view.Id);

        // 相互親(循環)
        var n1 = NewNode(view.Id, tag.Id);
        var n2 = NewNode(view.Id, tag.Id);
        var cyc1 = n1 with { ParentId = n2.Id };
        var cyc2 = n2 with { ParentId = n1.Id };
        var cycle = await _views.SaveHierarchyAsync(view.Id, [cyc1, cyc2], null);
        Assert.False(cycle.IsSuccess);
        Assert.Equal(ErrorCode.CircularReference, cycle.Error);

        // 集合外の親
        var orphan = NewNode(view.Id, tag.Id, parentId: "no-such-node");
        var invalid = await _views.SaveHierarchyAsync(view.Id, [orphan], null);
        Assert.False(invalid.IsSuccess);
        Assert.Equal(ErrorCode.ValidationError, invalid.Error);

        // 拒否時は既存階層が無傷(原子性)
        var after = await _views.GetHierarchyAsync(view.Id);
        Assert.Equal(before.Select(n => n.Id), after.Select(n => n.Id));
    }

    [Fact]
    public async Task 集合外のホーム指定はnullへフォールバックして保存される()
    {
        var view = (await _views.CreateAsync("V")).Value!;
        var tag = await NewTagAsync("A");
        var node = NewNode(view.Id, tag.Id);

        var result = await _views.SaveHierarchyAsync(view.Id, [node], "deleted-node-id");

        Assert.True(result.IsSuccess);
        Assert.Null((await _views.GetAsync(view.Id))!.HomeTagId); // REQ-037 のフォールバック方針
    }

    [Fact]
    public async Task 存在しないビューへの保存はNotFound()
    {
        var result = await _views.SaveHierarchyAsync("no-such-view", [], null);
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.NotFound, result.Error);
    }

    /// <summary>ECO-043(VE-004 裁定 2026-07-05): ビュー名は必須 — 空白のみの名前は作成・更新とも ValidationError。</summary>
    [Fact]
    public async Task 空白のみのビュー名は作成も更新も拒否される()
    {
        // 作成: 空白のみ → ValidationError
        var created = await _views.CreateAsync("   ");
        Assert.False(created.IsSuccess);
        Assert.Equal(ErrorCode.ValidationError, created.Error);

        // 更新: 既存ビューの名前を空白のみへ → ValidationError・元の名前が無傷
        var view = (await _views.CreateAsync("V")).Value!;
        var updated = await _views.UpdateAsync(view with { Name = " " });
        Assert.False(updated.IsSuccess);
        Assert.Equal(ErrorCode.ValidationError, updated.Error);
        Assert.Equal("V", (await _views.GetAsync(view.Id))!.Name);
    }
}
