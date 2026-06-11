using Dapper;
using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// CP-VIEW-012: ビュー管理(CRUD・modified_at 規則・一覧・階層ノード)が仕様 §2.3/2.4 と一致する。
/// 一時 SQLite に対する DB 状態・並び順の完全一致。
/// </summary>
[Trait("cp", "CP-VIEW-012")]
public sealed class CpView012Tests : IDisposable
{
    private readonly FakeClock _clock = new(new DateTime(2026, 6, 11, 0, 0, 0, DateTimeKind.Utc));
    private readonly TempDb _db;
    private readonly ViewService _service;
    private readonly TagService _tags;

    public CpView012Tests()
    {
        _db = new TempDb(_clock);
        _service = new ViewService(_db.Views, _clock);
        _tags = new TagService(_db.Tags);
    }

    public void Dispose() => _db.Dispose();

    private async Task<Tag> SeedTagAsync(string name = "tag")
    {
        var result = await _tags.CreateAsync(name, TagType.Simple);
        Assert.True(result.IsSuccess);
        return result.Value!;
    }

    // ---- CRUD(REQ-030) ----

    [Fact]
    public async Task 空白のみの名前はValidationError()
    {
        var created = await _service.CreateAsync("  \t ");
        Assert.False(created.IsSuccess);
        Assert.Equal(ErrorCode.ValidationError, created.Error);

        var view = (await _service.CreateAsync("v")).Value!;
        var updated = await _service.UpdateAsync(view with { Name = "   " });
        Assert.Equal(ErrorCode.ValidationError, updated.Error);
    }

    [Fact]
    public async Task ビュー削除で条件と階層ノードが連鎖削除される_孤児ゼロ()
    {
        var tag = await SeedTagAsync();
        var view = (await _service.CreateAsync("v")).Value!;
        Assert.True((await _service.AddConditionAsync(view.Id, tag.Id, ConditionOperator.Exists)).IsSuccess);
        var node = (await _service.AddNodeAsync(view.Id, tag.Id, parentId: null, position: 0)).Value!;
        Assert.True((await _service.AddNodeAsync(view.Id, tag.Id, parentId: node.Id, position: 0)).IsSuccess);

        Assert.True((await _service.DeleteAsync(view.Id)).IsSuccess);

        var (conditions, nodes) = await _db.Manager.RunAsync(async conn =>
        {
            var c = await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM view_conditions");
            var n = await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM view_tag_hierarchies");
            return (c, n);
        }, TestContext.Current.CancellationToken);
        Assert.Equal(0, conditions);
        Assert.Equal(0, nodes);
    }

    // ---- modified_at 規則(REQ-032) ----

    [Fact]
    public async Task 条件追加とノード移動とalias変更でmodified_atが更新され閲覧では不変()
    {
        var tag = await SeedTagAsync();
        var view = (await _service.CreateAsync("v")).Value!;
        var t0 = view.ModifiedAt;

        // 条件追加 → 更新
        _clock.Advance(TimeSpan.FromMinutes(1));
        await _service.AddConditionAsync(view.Id, tag.Id, ConditionOperator.Exists);
        var t1 = (await _service.GetAsync(view.Id))!.ModifiedAt;
        Assert.True(string.CompareOrdinal(t1, t0) > 0);

        // 階層ノード追加+移動 → 更新
        _clock.Advance(TimeSpan.FromMinutes(1));
        var parent = (await _service.AddNodeAsync(view.Id, tag.Id, null, 0)).Value!;
        var child = (await _service.AddNodeAsync(view.Id, tag.Id, parent.Id, 0)).Value!;
        var t2 = (await _service.GetAsync(view.Id))!.ModifiedAt;
        _clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True((await _service.MoveNodeAsync(child.Id, null, 1)).IsSuccess);
        var t3 = (await _service.GetAsync(view.Id))!.ModifiedAt;
        Assert.True(string.CompareOrdinal(t3, t2) > 0);

        // alias 変更 → 更新
        _clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True((await _service.UpdateNodeAsync(child with { ParentId = null, Alias = "別名" })).IsSuccess);
        var t4 = (await _service.GetAsync(view.Id))!.ModifiedAt;
        Assert.True(string.CompareOrdinal(t4, t3) > 0);

        // 閲覧(取得系)では不変
        _clock.Advance(TimeSpan.FromMinutes(1));
        _ = await _service.GetAsync(view.Id);
        _ = await _service.GetConditionsAsync(view.Id);
        _ = await _service.GetHierarchyAsync(view.Id);
        _ = await _service.GetFavoritesAsync();
        _ = await _service.GetRecentAsync();
        Assert.Equal(t4, (await _service.GetAsync(view.Id))!.ModifiedAt);
    }

    [Fact]
    public async Task ビュー本体の更新でmodified_atが更新される()
    {
        var view = (await _service.CreateAsync("v")).Value!;
        _clock.Advance(TimeSpan.FromMinutes(1));

        var updated = await _service.UpdateAsync(view with { Name = "renamed", IsFavorite = true });

        Assert.True(updated.IsSuccess);
        Assert.True(string.CompareOrdinal(updated.Value!.ModifiedAt, view.ModifiedAt) > 0);
    }

    // ---- 一覧(REQ-033) ----

    [Fact]
    public async Task お気に入りはname昇順で最近はmodified_at降順limit付き同値はid昇順()
    {
        // お気に入り 3 件(name 昇順・大文字小文字無視)
        var fb = (await _service.CreateAsync("beta", isFavorite: true)).Value!;
        var fa = (await _service.CreateAsync("Alpha", isFavorite: true)).Value!;
        var fc = (await _service.CreateAsync("alpha2", isFavorite: true)).Value!;
        _ = (await _service.CreateAsync("not-favorite")).Value!;

        var favorites = await _service.GetFavoritesAsync();
        Assert.Equal([fa.Id, fc.Id, fb.Id], favorites.Select(v => v.Id));

        // 最近: modified_at 降順・同値 id 昇順・limit
        _clock.Advance(TimeSpan.FromMinutes(1));
        var same1 = (await _service.CreateAsync("same-a")).Value!;
        var same2 = (await _service.CreateAsync("same-b")).Value!; // 同時刻
        _clock.Advance(TimeSpan.FromMinutes(1));
        var newest = (await _service.CreateAsync("newest")).Value!;

        var recent = await _service.GetRecentAsync(limit: 3);
        var sameOrdered = new[] { same1.Id, same2.Id }.Order(StringComparer.Ordinal).ToArray();
        Assert.Equal([newest.Id, sameOrdered[0], sameOrdered[1]], recent.Select(v => v.Id));

        // 既定 limit 10
        var all = await _service.GetRecentAsync();
        Assert.Equal(7, all.Count);
    }

    // ---- 階層ノード(REQ-034 / INV-004) ----

    [Fact]
    public async Task ノード移動で自己や子孫を親に指定するとCircularReference()
    {
        var tag = await SeedTagAsync();
        var view = (await _service.CreateAsync("v")).Value!;
        var root = (await _service.AddNodeAsync(view.Id, tag.Id, null, 0)).Value!;
        var mid = (await _service.AddNodeAsync(view.Id, tag.Id, root.Id, 0)).Value!;
        var leaf = (await _service.AddNodeAsync(view.Id, tag.Id, mid.Id, 0)).Value!;

        var self = await _service.MoveNodeAsync(root.Id, root.Id, 0);
        Assert.Equal(ErrorCode.CircularReference, self.Error);

        var descendant = await _service.MoveNodeAsync(root.Id, leaf.Id, 0); // 孫を親に
        Assert.Equal(ErrorCode.CircularReference, descendant.Error);

        var valid = await _service.MoveNodeAsync(leaf.Id, root.Id, 1); // 正当な移動
        Assert.True(valid.IsSuccess);
        var moved = await _db.Views.GetNodeByIdAsync(leaf.Id);
        Assert.Equal(root.Id, moved!.ParentId);
        Assert.Equal(1, moved.Position);
    }

    [Fact]
    public async Task ノード削除で子のparent_idはSET_NULLされる()
    {
        var tag = await SeedTagAsync();
        var view = (await _service.CreateAsync("v")).Value!;
        var parent = (await _service.AddNodeAsync(view.Id, tag.Id, null, 0)).Value!;
        var child = (await _service.AddNodeAsync(view.Id, tag.Id, parent.Id, 0)).Value!;

        Assert.True((await _service.DeleteNodeAsync(parent.Id)).IsSuccess);

        var orphan = await _db.Views.GetNodeByIdAsync(child.Id);
        Assert.NotNull(orphan);
        Assert.Null(orphan.ParentId); // 仕様 §2.0: 自己参照 FK は SET NULL
    }

    [Fact]
    public async Task 階層ノードのcondition_typeとcondition_valueがラウンドトリップする()
    {
        var tag = await SeedTagAsync();
        var view = (await _service.CreateAsync("v")).Value!;
        var node = (await _service.AddNodeAsync(
            view.Id, tag.Id, null, 0,
            alias: "範囲",
            conditionType: HierarchyConditionType.Range,
            conditionValue: """{"valueFrom":1,"valueTo":5}""")).Value!;

        var loaded = await _db.Views.GetNodeByIdAsync(node.Id);

        Assert.NotNull(loaded);
        Assert.Equal(HierarchyConditionType.Range, loaded.ConditionType);
        Assert.Equal("""{"valueFrom":1,"valueTo":5}""", loaded.ConditionValue);
        Assert.Equal("範囲", loaded.Alias);
    }

    // ---- display_columns(REQ-042 の格納) ----

    [Fact]
    public async Task display_columnsはbasic列とtag列を含めてラウンドトリップする()
    {
        const string columns = """
            [{"type":"basic","key":"name","label":"名前","width":2},
             {"type":"tag","key":"tag-id-1","label":"色","width":1}]
            """;
        var view = (await _service.CreateAsync("v", displayColumns: columns)).Value!;

        var loaded = await _service.GetAsync(view.Id);

        Assert.NotNull(loaded);
        Assert.Equal(columns, loaded.DisplayColumns);

        // ソート設定の既定値(REQ-038: name / asc)
        Assert.Equal(SortField.Name, loaded.SortField);
        Assert.Equal(SortDirection.Asc, loaded.SortDirection);
    }
}
