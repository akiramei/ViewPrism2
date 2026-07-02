using ViewPrism2.App.ViewModels;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// ECO-025 β: ファイル一覧の列/セル構築(<see cref="ListColumnBuilder"/>・REQ-081)。
/// display_columns→列定義(名前先頭固定・kind 解決・削除タグ除去)と型別セル描画データ。描画は golden(maintainer)。
/// </summary>
[Trait("cp", "CP-UI-G2")]
public sealed class CpListColumnBuilderTests
{
    private static readonly Tag Rating = new() { Id = "t-rating", Name = "評価", Type = TagType.Numeric, Color = "#e8b931" };
    private static readonly Tag Job = new() { Id = "t-job", Name = "職種", Type = TagType.Textual, Color = "#2f6bed" };
    private static readonly Tag Gacha = new() { Id = "t-featured", Name = "おすすめ", Type = TagType.Simple, Color = "#8b5cf6" };

    private static IReadOnlyDictionary<string, Tag> Tags() => new Dictionary<string, Tag>(StringComparer.Ordinal)
    {
        [Rating.Id] = Rating, [Job.Id] = Job, [Gacha.Id] = Gacha,
    };

    private static string BasicLabel(string key) => key switch
    {
        "name" => "名前", "size" => "サイズ", "modified_date" => "更新日", _ => key,
    };

    private static IReadOnlyList<ListColumnDef> Build(string? json) =>
        ListColumnBuilder.Build(json, Tags(), BasicLabel);

    private static ImageEntry Entry(string id, string name, long size,
        (string TagId, TagType Type, string? Value)[]? tags = null)
    {
        var rec = new ImageRecord
        {
            Id = id, SyncFolderId = "sf", RelativePath = name, FileName = name, FileSize = size,
            Hash = new string('0', 64), CreatedDate = "2026-01-01T00:00:00.000Z", ModifiedDate = "2026-02-03T00:00:00.000Z",
        };
        var t = (tags ?? []).Select(x => new EvalTagValue(x.TagId, x.Type, x.Value)).ToList();
        return new ImageEntry(rec, $"C:/{name}", t);
    }

    [Fact]
    public void 既定は3列でnameが先頭固定かつ伸縮幅()
    {
        var cols = Build(null);

        Assert.Equal(["name", "size", "modified_date"], cols.Select(c => c.Key));
        Assert.True(cols[0].IsNameLocked);
        Assert.Equal(ListCellKind.BasicName, cols[0].Kind);
        Assert.Equal("1.7*", cols[0].Width);   // 名前=伸縮
        Assert.Equal("120", cols[1].Width);     // size=固定
        Assert.Equal("150", cols[2].Width);     // date=固定
    }

    [Fact]
    public void タグ列はタグ型からkindと色と幅を解決する()
    {
        var json = """
            [{"type":"basic","key":"name"},{"type":"tag","key":"t-rating"},
             {"type":"tag","key":"t-job"},{"type":"tag","key":"t-featured"}]
            """;
        var cols = Build(json);

        Assert.Equal([ListCellKind.BasicName, ListCellKind.Num, ListCellKind.Text, ListCellKind.Simple],
            cols.Select(c => c.Kind));
        Assert.Equal("#e8b931", cols[1].Color);
        Assert.Equal("140", cols[1].Width); // num
        Assert.Equal("152", cols[2].Width); // text
        Assert.Equal("110", cols[3].Width); // simple
        Assert.Equal("t-rating", cols[1].TagId);
    }

    [Fact]
    public void nameが無ければ先頭に補われ途中nameは先頭へ()
    {
        var json = """[{"type":"tag","key":"t-rating"},{"type":"basic","key":"name"}]""";
        var cols = Build(json);

        Assert.Equal("name", cols[0].Key);
        Assert.Equal(["name", "t-rating"], cols.Select(c => c.Key));
    }

    [Fact]
    public void 削除済みタグ列は捨てる()
    {
        var json = """[{"type":"basic","key":"name"},{"type":"tag","key":"deleted"},{"type":"basic","key":"size"}]""";
        var cols = Build(json);

        Assert.Equal(["name", "size"], cols.Select(c => c.Key));
    }

    [Fact]
    public void ColumnTemplateは各列幅をカンマ連結する()
    {
        var cols = Build("""[{"type":"basic","key":"name"},{"type":"tag","key":"t-rating"}]""");
        Assert.Equal("1.7*,140", ListColumnBuilder.ColumnTemplate(cols));
    }

    // ---- セル描画 ----

    [Fact]
    public void 数値セルは星と数値_未設定はHasValuefalse()
    {
        var cols = Build("""[{"type":"basic","key":"name"},{"type":"tag","key":"t-rating"}]""");

        var withRating = ListColumnBuilder.BuildCells(
            Entry("a", "a.png", 2_000_000, [("t-rating", TagType.Numeric, "3")]),
            cols, s => $"{s}B", d => d);
        Assert.Equal(ListCellKind.Num, withRating[1].Kind);
        Assert.True(withRating[1].HasValue);
        Assert.Equal(3, withRating[1].Stars);
        Assert.Equal("3", withRating[1].Text);

        var noRating = ListColumnBuilder.BuildCells(Entry("b", "b.png", 0), cols, s => $"{s}B", d => d);
        Assert.False(noRating[1].HasValue); // 未設定
        Assert.Equal(0, noRating[1].Stars);
    }

    [Fact]
    public void テキストセルは値とタグ色_未設定はHasValuefalse()
    {
        var cols = Build("""[{"type":"basic","key":"name"},{"type":"tag","key":"t-job"}]""");

        var withJob = ListColumnBuilder.BuildCells(
            Entry("a", "a.png", 0, [("t-job", TagType.Textual, "エンジニア")]), cols, s => $"{s}B", d => d);
        Assert.True(withJob[1].HasValue);
        Assert.Equal("エンジニア", withJob[1].Text);
        Assert.Equal("#2f6bed", withJob[1].Color);

        var noJob = ListColumnBuilder.BuildCells(Entry("b", "b.png", 0), cols, s => $"{s}B", d => d);
        Assert.False(noJob[1].HasValue); // —
    }

    [Fact]
    public void シンプルセルは有無で付与ラベルとオフ()
    {
        var cols = Build("""[{"type":"basic","key":"name"},{"type":"tag","key":"t-featured"}]""");

        var on = ListColumnBuilder.BuildCells(
            Entry("a", "a.png", 0, [("t-featured", TagType.Simple, null)]), cols, s => $"{s}B", d => d);
        Assert.True(on[1].HasValue);
        Assert.Equal("おすすめ", on[1].Text); // 付与=ラベル
        Assert.Equal("#8b5cf6", on[1].Color);

        var off = ListColumnBuilder.BuildCells(Entry("b", "b.png", 0), cols, s => $"{s}B", d => d);
        Assert.False(off[1].HasValue); // オフ表示
    }

    [Fact]
    public void 基本セルはサイズと日付を整形器で描く()
    {
        var cols = Build(null);
        var cells = ListColumnBuilder.BuildCells(
            Entry("a", "photo.png", 1234), cols, s => $"[{s}]", d => $"<{d}>");

        Assert.Equal("photo.png", cells[0].Text);
        Assert.Equal("[1234]", cells[1].Text);                     // size 整形器
        Assert.Equal("<2026-02-03T00:00:00.000Z>", cells[2].Text); // date 整形器
    }
}
