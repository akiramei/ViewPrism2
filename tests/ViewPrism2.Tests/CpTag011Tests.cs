using Dapper;
using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// CP-TAG-011: タグ管理(検証・UPSERT・バッチ原子性・カスケード)が仕様 §2.2 と一致する。
/// 一時 SQLite に対する操作後の DB 状態・エラーコードの完全一致。
/// </summary>
[Trait("cp", "CP-TAG-011")]
public sealed class CpTag011Tests : IDisposable
{
    private readonly TempDb _db = new();
    private readonly TagService _service;

    public CpTag011Tests()
    {
        _service = new TagService(_db.Tags);
    }

    public void Dispose() => _db.Dispose();

    private async Task<ImageRecord> SeedImageAsync(string id = "img-1")
    {
        var folder = await _db.Folders.GetByPathAsync("C:/pics");
        if (folder is null)
        {
            folder = new SyncFolder { Id = "folder-1", Name = "pics", Path = "C:/pics" };
            Assert.True((await _db.Folders.AddAsync(folder)).IsSuccess);
        }

        var image = new ImageRecord
        {
            Id = id,
            SyncFolderId = folder.Id,
            RelativePath = id + ".jpg",
            FileName = id + ".jpg",
            FileSize = 1,
            Hash = new string('0', 64),
            Status = ImageStatus.Normal,
            CreatedDate = "2026-01-01T00:00:00.000Z",
            ModifiedDate = "2026-01-01T00:00:00.000Z",
        };
        await _db.Images.AddAsync(image);
        return image;
    }

    // ---- 名前(REQ-021) ----

    [Fact]
    public async Task 重複名はDuplicateTagNameで大文字小文字違いは別名として許可()
    {
        Assert.True((await _service.CreateAsync("Tag", TagType.Simple)).IsSuccess);

        var duplicate = await _service.CreateAsync("Tag", TagType.Simple);
        Assert.False(duplicate.IsSuccess);
        Assert.Equal(ErrorCode.DuplicateTagName, duplicate.Error);

        var lower = await _service.CreateAsync("tag", TagType.Simple); // case-sensitive 一意
        Assert.True(lower.IsSuccess);
    }

    [Fact]
    public async Task 空白のみの名前はValidationError()
    {
        var result = await _service.CreateAsync("   ", TagType.Simple);
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.ValidationError, result.Error);
    }

    // ---- color(REQ-023) ----

    [Fact]
    public async Task colorは6桁hex形式のみ受理しNULL可()
    {
        var ok = await _service.CreateAsync("c1", TagType.Simple, color: "#1A2b3c");
        Assert.True(ok.IsSuccess);
        Assert.Equal("#1A2b3c", ok.Value!.Color);

        var named = await _service.CreateAsync("c2", TagType.Simple, color: "red");
        Assert.Equal(ErrorCode.ValidationError, named.Error);

        var invalidHex = await _service.CreateAsync("c3", TagType.Simple, color: "#GGGGGG");
        Assert.Equal(ErrorCode.ValidationError, invalidHex.Error);

        var nullColor = await _service.CreateAsync("c4", TagType.Simple, color: null);
        Assert.True(nullColor.IsSuccess);
        Assert.Null(nullColor.Value!.Color);
    }

    // ---- numeric 範囲(REQ-025) ----

    [Fact]
    public async Task numericの範囲は両端を含み範囲外を拒否する()
    {
        var image = await SeedImageAsync();
        var tag = (await _service.CreateAsync("rating", TagType.Numeric)).Value!;
        Assert.True((await _service.SetNumericSettingsAsync(tag.Id, 1, 5, null, "点")).IsSuccess);

        Assert.True((await _service.TagImageAsync(image.Id, tag.Id, "1")).IsSuccess);  // 下限含む
        Assert.True((await _service.TagImageAsync(image.Id, tag.Id, "5")).IsSuccess);  // 上限含む
        Assert.Equal(ErrorCode.ValidationError, (await _service.TagImageAsync(image.Id, tag.Id, "0")).Error);
        Assert.Equal(ErrorCode.ValidationError, (await _service.TagImageAsync(image.Id, tag.Id, "6")).Error);
        Assert.Equal(ErrorCode.ValidationError, (await _service.TagImageAsync(image.Id, tag.Id, "abc")).Error);
    }

    [Fact]
    public async Task numericの設定なしは任意の数値を受理する()
    {
        var image = await SeedImageAsync();
        var tag = (await _service.CreateAsync("free", TagType.Numeric)).Value!;

        Assert.True((await _service.TagImageAsync(image.Id, tag.Id, "-273.15")).IsSuccess);
        Assert.True((await _service.TagImageAsync(image.Id, tag.Id, "1000000")).IsSuccess);
    }

    // ---- predefined_values(REQ-024) ----

    [Fact]
    public async Task predefined_valuesは順序を保持してラウンドトリップする()
    {
        var tag = (await _service.CreateAsync("color", TagType.Textual)).Value!;
        var values = new[] { "zeta", "alpha", "midway", "alpha2" };

        Assert.True((await _service.SetTextualSettingsAsync(tag.Id, values)).IsSuccess);
        var loaded = await _db.Tags.GetTextualSettingsAsync(tag.Id);

        Assert.NotNull(loaded);
        Assert.Equal(values, loaded.PredefinedValues); // 順序保持

        // リスト外の値の付与も許可(入力補助のみ)
        var image = await SeedImageAsync();
        Assert.True((await _service.TagImageAsync(image.Id, tag.Id, "not-in-list")).IsSuccess);
    }

    // ---- UPSERT(REQ-026 / INV-003) ----

    [Fact]
    public async Task 再付与は行数1のまま値が上書きされる()
    {
        var image = await SeedImageAsync();
        var tag = (await _service.CreateAsync("color", TagType.Textual)).Value!;

        Assert.True((await _service.TagImageAsync(image.Id, tag.Id, "red")).IsSuccess);
        Assert.True((await _service.TagImageAsync(image.Id, tag.Id, "blue")).IsSuccess);

        var rows = await _db.Tags.GetImageTagsAsync(image.Id);
        var row = Assert.Single(rows); // 行は増えない
        Assert.Equal("blue", row.Value); // 値上書き
    }

    [Fact]
    public async Task 存在しない付与の解除は成功する_冪等()
    {
        var image = await SeedImageAsync();
        var tag = (await _service.CreateAsync("t", TagType.Simple)).Value!;

        var result = await _service.UntagImageAsync(image.Id, tag.Id); // 付与なしで解除
        Assert.True(result.IsSuccess);

        var batch = await _service.UntagImagesAsync([image.Id, "no-such-image"], tag.Id);
        Assert.True(batch.IsSuccess);
    }

    // ---- バッチ原子性(REQ-027 / INV-006) ----

    [Fact]
    public async Task バッチ3画像中1失敗で全ロールバック()
    {
        var img1 = await SeedImageAsync("img-1");
        var img2 = await SeedImageAsync("img-2");
        var tag = (await _service.CreateAsync("batch", TagType.Simple)).Value!;

        // 2 件目に存在しない画像 → FK 違反 → 全ロールバック(0 適用)
        var result = await _service.TagImagesAsync([img1.Id, "no-such-image", img2.Id], tag.Id, null);

        Assert.False(result.IsSuccess);
        var count = await _db.Manager.RunAsync(conn =>
            conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM image_tags"),
            TestContext.Current.CancellationToken);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task バッチ成功時は全件に適用される()
    {
        var img1 = await SeedImageAsync("img-1");
        var img2 = await SeedImageAsync("img-2");
        var img3 = await SeedImageAsync("img-3");
        var tag = (await _service.CreateAsync("batch", TagType.Textual)).Value!;

        var result = await _service.TagImagesAsync([img1.Id, img2.Id, img3.Id], tag.Id, "v");

        Assert.True(result.IsSuccess);
        var count = await _db.Manager.RunAsync(conn =>
            conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM image_tags WHERE value = 'v'"),
            TestContext.Current.CancellationToken);
        Assert.Equal(3, count);
    }

    // ---- 階層と循環(REQ-022 / INV-004) ----

    [Fact]
    public async Task 自己親と孫を親に指定はCircularReference()
    {
        var grandparent = (await _service.CreateAsync("gp", TagType.Simple)).Value!;
        var parent = (await _service.CreateAsync("p", TagType.Simple, parentId: grandparent.Id)).Value!;
        var child = (await _service.CreateAsync("c", TagType.Simple, parentId: parent.Id)).Value!;

        var self = await _service.UpdateAsync(grandparent with { ParentId = grandparent.Id });
        Assert.Equal(ErrorCode.CircularReference, self.Error);

        var descendant = await _service.UpdateAsync(grandparent with { ParentId = child.Id }); // 孫を親に
        Assert.Equal(ErrorCode.CircularReference, descendant.Error);

        var valid = await _service.UpdateAsync(child with { ParentId = grandparent.Id }); // 正当な付け替え
        Assert.True(valid.IsSuccess);
    }

    // ---- 削除カスケード(REQ-028 / FMEA-003) ----

    [Fact]
    public async Task タグ削除のカスケードが仕様どおり()
    {
        var image = await SeedImageAsync();
        var parent = (await _service.CreateAsync("parent", TagType.Textual)).Value!;
        var child = (await _service.CreateAsync("child", TagType.Simple, parentId: parent.Id)).Value!;
        await _service.TagImageAsync(image.Id, parent.Id, "v");

        var view = new View { Id = "view-1", Name = "v", ModifiedAt = _db.Clock.UtcNowIso() };
        await _db.Views.AddAsync(view);
        await _db.Views.AddConditionAsync(new ViewCondition
        {
            Id = "cond-1", ViewId = view.Id, TagId = parent.Id, Operator = ConditionOperator.Exists,
        });
        await _db.Views.AddNodeAsync(new HierarchyNode
        {
            Id = "node-1", ViewId = view.Id, TagId = parent.Id, Position = 0,
        });

        Assert.True((await _service.DeleteAsync(parent.Id)).IsSuccess);

        Assert.Empty(await _db.Tags.GetImageTagsAsync(image.Id));                  // image_tags 消滅
        Assert.Null((await _db.Views.GetConditionByIdAsync("cond-1"))!.TagId);     // 条件は SET NULL
        Assert.Empty(await _db.Views.GetHierarchyAsync(view.Id));                  // 階層ノード消滅
        Assert.Null((await _db.Tags.GetByIdAsync(child.Id))!.ParentId);            // 子 parent_id NULL
    }

    // ---- 付与値の型規則(REQ-020) ----

    [Fact]
    public async Task simpleは値なしのみtextualは文字列必須()
    {
        var image = await SeedImageAsync();
        var simple = (await _service.CreateAsync("s", TagType.Simple)).Value!;
        var textual = (await _service.CreateAsync("t", TagType.Textual)).Value!;

        Assert.True((await _service.TagImageAsync(image.Id, simple.Id, null)).IsSuccess);
        Assert.Equal(ErrorCode.ValidationError, (await _service.TagImageAsync(image.Id, simple.Id, "x")).Error);
        Assert.True((await _service.TagImageAsync(image.Id, textual.Id, "x")).IsSuccess);
        Assert.Equal(ErrorCode.ValidationError, (await _service.TagImageAsync(image.Id, textual.Id, null)).Error);
    }

    // ---- 一覧と使用数(REQ-029) ----

    [Fact]
    public async Task 一覧はname昇順OrdinalIgnoreCaseで使用数はdistinct画像数()
    {
        var img1 = await SeedImageAsync("img-1");
        var img2 = await SeedImageAsync("img-2");
        var beta = (await _service.CreateAsync("beta", TagType.Textual)).Value!;
        var alpha = (await _service.CreateAsync("Alpha", TagType.Simple)).Value!;
        var zero = (await _service.CreateAsync("zero-usage", TagType.Simple)).Value!;

        await _service.TagImageAsync(img1.Id, beta.Id, "a");
        await _service.TagImageAsync(img1.Id, beta.Id, "b"); // UPSERT: 同一画像の再付与は使用数に効かない
        await _service.TagImageAsync(img2.Id, beta.Id, "c");
        await _service.TagImageAsync(img1.Id, alpha.Id, null);

        var list = await _service.GetAllWithUsageAsync();

        Assert.Equal(["Alpha", "beta", "zero-usage"], list.Select(t => t.Tag.Name)); // 大文字小文字無視の昇順
        Assert.Equal(1, list[0].UsageCount);
        Assert.Equal(2, list[1].UsageCount); // distinct image 数
        Assert.Equal(0, list[2].UsageCount);
        _ = zero;
    }
}
