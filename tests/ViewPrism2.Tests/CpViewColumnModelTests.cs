using ViewPrism2.App.ViewModels;
using ViewPrism2.Core.Models;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// ECO-025 α: 表示列 進化モデル(<see cref="ViewColumnModel"/>・REQ-079/080)の決定論ロジック。
/// 名前固定(VE-001)・上限5(VE-002)・タグ母集合限定・追加/削除/並べ替え・display_columns 直列化(VE-003)。
/// 描画は golden(承認者 maintainer)。
/// </summary>
[Trait("cp", "CP-VIEW-012")]
public sealed class CpViewColumnModelTests
{
    private static readonly Tag RatingTag = new()
    {
        Id = "tag-rating", Name = "評価", Type = TagType.Numeric, Color = "#e8b931",
    };

    private static readonly Tag JobTag = new()
    {
        Id = "tag-job", Name = "職種", Type = TagType.Textual, Color = "#2f6bed",
    };

    private static readonly Tag GachaTag = new()
    {
        Id = "tag-featured", Name = "おすすめ", Type = TagType.Simple, Color = "#8b5cf6",
    };

    private static IReadOnlyList<Tag> ViewTags() => [RatingTag, JobTag, GachaTag];

    [Fact]
    public void 既定はname_size_modified_dateの3列でnameが先頭固定()
    {
        var model = ViewColumnModel.Create(null, ViewTags());

        Assert.Equal(["name", "size", "modified_date"], model.Selected.Select(c => c.Key));
        Assert.True(model.Selected[0].IsNameLocked);
        Assert.All(model.Selected, c => Assert.Equal(ColumnSource.Basic, c.Source));
        Assert.Equal(2, model.Selected[0].Star); // name=2*、他=1*
        Assert.Equal(1, model.Selected[1].Star);
    }

    [Fact]
    public void JSON定義どおりに並びタグ列は色と型を持つ()
    {
        var json = """
            [{"type":"basic","key":"name","width":2},
             {"type":"basic","key":"size","width":1},
             {"type":"tag","key":"tag-rating","width":1}]
            """;

        var model = ViewColumnModel.Create(json, ViewTags());

        Assert.Equal(["name", "size", "tag-rating"], model.Selected.Select(c => c.Key));
        var rating = model.Selected[2];
        Assert.Equal(ColumnSource.Tag, rating.Source);
        Assert.Equal(TagType.Numeric, rating.TagType);
        Assert.Equal("#e8b931", rating.Color);
    }

    [Fact]
    public void nameがJSONの途中にあっても先頭へ固定される_VE001()
    {
        var json = """
            [{"type":"tag","key":"tag-rating"},
             {"type":"basic","key":"name"},
             {"type":"basic","key":"size"}]
            """;

        var model = ViewColumnModel.Create(json, ViewTags());

        Assert.Equal("name", model.Selected[0].Key);
        Assert.Equal(["name", "tag-rating", "size"], model.Selected.Select(c => c.Key));
    }

    [Fact]
    public void 削除済みや母集合外のタグ列は捨てる()
    {
        var json = """
            [{"type":"basic","key":"name"},
             {"type":"tag","key":"tag-not-in-view"},
             {"type":"tag","key":"tag-job"}]
            """;

        var model = ViewColumnModel.Create(json, ViewTags());

        Assert.Equal(["name", "tag-job"], model.Selected.Select(c => c.Key));
    }

    [Fact]
    public void 破損JSONや有効列なしは既定3列にフォールバック()
    {
        Assert.Equal(3, ViewColumnModel.Create("{{{", ViewTags()).Selected.Count);
        Assert.Equal(3, ViewColumnModel.Create("""{"not":"array"}""", ViewTags()).Selected.Count);
        Assert.Equal(3, ViewColumnModel.Create("""[{"type":"basic","key":"unknown"}]""", ViewTags()).Selected.Count);
    }

    [Fact]
    public void 追加元は選択済みを除いた基本情報とビュー内タグ_nameは追加元に出ない()
    {
        var model = ViewColumnModel.Create("""[{"type":"basic","key":"name"}]""", ViewTags());

        // 選択=name のみ → 追加元 basics=size/modified_date(name は出ない)
        Assert.Equal(["size", "modified_date"], model.AvailableBasics.Select(c => c.Key));
        // 追加元 tags=ビュー内タグ全部
        Assert.Equal(["tag-rating", "tag-job", "tag-featured"], model.AvailableTags.Select(c => c.Key));
    }

    [Fact]
    public void Addは末尾に追加し重複は無視する()
    {
        var model = ViewColumnModel.Create("""[{"type":"basic","key":"name"}]""", ViewTags());

        Assert.True(model.Add(new ViewColumn("tag-rating", ColumnSource.Tag, 1, "#e8b931", TagType.Numeric)));
        Assert.Equal(["name", "tag-rating"], model.Selected.Select(c => c.Key));

        // 重複は無視
        Assert.False(model.Add(new ViewColumn("tag-rating", ColumnSource.Tag, 1)));
        Assert.Equal(2, model.Selected.Count);
    }

    [Fact]
    public void 上限5に達すると追加できずAtLimitがtrue_VE002()
    {
        // name + size + 3 タグ = 5 列
        var json = """
            [{"type":"basic","key":"name"},{"type":"basic","key":"size"},
             {"type":"tag","key":"tag-rating"},{"type":"tag","key":"tag-job"},{"type":"tag","key":"tag-featured"}]
            """;
        var model = ViewColumnModel.Create(json, ViewTags());

        Assert.Equal(5, model.Selected.Count);
        Assert.True(model.AtLimit);
        Assert.False(model.Add(new ViewColumn("modified_date", ColumnSource.Basic, 1)));
        Assert.Equal(5, model.Selected.Count);
    }

    [Fact]
    public void Removeはnameを除去できない_VE001()
    {
        var model = ViewColumnModel.Create("""[{"type":"basic","key":"name"},{"type":"basic","key":"size"}]""", ViewTags());

        Assert.False(model.Remove("name"));       // 名前固定
        Assert.Equal(2, model.Selected.Count);

        Assert.True(model.Remove("size"));
        Assert.Equal(["name"], model.Selected.Select(c => c.Key));
    }

    [Fact]
    public void Moveはnameをまたがずindexをずらす_VE001()
    {
        var json = """
            [{"type":"basic","key":"name"},{"type":"basic","key":"size"},{"type":"tag","key":"tag-rating"}]
            """;
        var model = ViewColumnModel.Create(json, ViewTags());

        // name(0)は動かせない・index1(size)は name の直後で上へ動けない
        Assert.False(model.MoveUp("name"));
        Assert.False(model.MoveUp("size"));
        Assert.False(model.MoveDown("name"));

        // rating(2)を上へ → size と入替
        Assert.True(model.MoveUp("tag-rating"));
        Assert.Equal(["name", "tag-rating", "size"], model.Selected.Select(c => c.Key));

        // 末尾は下へ動けない
        Assert.False(model.MoveDown("size"));
    }

    [Fact]
    public void SerializeはDisplayColumnParserで往復できる()
    {
        var json = """
            [{"type":"basic","key":"name"},{"type":"basic","key":"size"},{"type":"tag","key":"tag-rating"}]
            """;
        var model = ViewColumnModel.Create(json, ViewTags());

        var serialized = model.Serialize();
        var tagById = ViewTags().ToDictionary(t => t.Id, StringComparer.Ordinal);
        var parsed = DisplayColumnParser.Parse(serialized, tagById);

        Assert.Equal(["name", "size", "tag-rating"], parsed.Select(c => c.Key));
        Assert.Equal(2, parsed[0].Star);  // name=2*
        Assert.Equal(1, parsed[1].Star);
        Assert.Equal(DisplayColumnKind.Tag, parsed[2].Kind);
        Assert.Equal("評価", parsed[2].Label); // tag ラベルは tag.Name フォールバック
    }
}
