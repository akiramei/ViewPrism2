using ViewPrism2.App.ViewModels;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// ECO-025 β: ファイル一覧 列ヘッダーソートの不変条件(<see cref="ViewColumnSorter"/>・REQ-081)。
/// 空値末尾・型別比較(数値/localeCompare('ja')/有無)・同値タイブレーク=名前昇順。描画は golden(maintainer)。
/// </summary>
[Trait("cp", "CP-UI-G2")]
public sealed class CpViewColumnSorterTests
{
    private const string RatingTag = "tag-rating";   // numeric
    private const string JobTag = "tag-job";         // textual
    private const string GachaTag = "tag-featured";     // simple

    private static ImageEntry Entry(
        string id, string name, long size = 0, string? modified = null,
        (string TagId, TagType Type, string? Value)[]? tags = null)
    {
        var record = new ImageRecord
        {
            Id = id,
            SyncFolderId = "sf",
            RelativePath = name,
            FileName = name,
            FileSize = size,
            Hash = new string('0', 64),
            CreatedDate = "2026-01-01T00:00:00.000Z",
            ModifiedDate = modified ?? "2026-01-01T00:00:00.000Z",
        };
        var evalTags = (tags ?? [])
            .Select(t => new EvalTagValue(t.TagId, t.Type, t.Value))
            .ToList();
        return new ImageEntry(record, $"C:/{name}", evalTags);
    }

    private static IReadOnlyList<string> Order(IReadOnlyList<ImageEntry> sorted) =>
        sorted.Select(e => e.Record.Id).ToList();

    // ---- 数値タグ ----

    [Fact]
    public void 数値タグは数値順で未設定は昇順降順とも末尾()
    {
        var entries = new[]
        {
            Entry("a", "a", tags: [(RatingTag, TagType.Numeric, "10")]),
            Entry("b", "b", tags: [(RatingTag, TagType.Numeric, "2")]),
            Entry("c", "c"), // 未設定
        };
        var col = ViewSortColumn.ForTag(RatingTag, TagType.Numeric);

        // 昇順: 2, 10, (未設定末尾)。辞書順なら "10" < "2" になるが数値順で 2 < 10
        Assert.Equal(["b", "a", "c"], Order(ViewColumnSorter.Sort(entries, col, SortDirection.Asc)));
        // 降順: 10, 2, (未設定は方向に関わらず末尾)
        Assert.Equal(["a", "b", "c"], Order(ViewColumnSorter.Sort(entries, col, SortDirection.Desc)));
    }

    // ---- テキストタグ ----

    [Fact]
    public void テキストタグはlocaleCompareで未設定は末尾()
    {
        var entries = new[]
        {
            Entry("a", "a", tags: [(JobTag, TagType.Textual, "さ")]),
            Entry("b", "b", tags: [(JobTag, TagType.Textual, "あ")]),
            Entry("c", "c"), // 未設定
            Entry("d", "d", tags: [(JobTag, TagType.Textual, "か")]),
        };
        var col = ViewSortColumn.ForTag(JobTag, TagType.Textual);

        Assert.Equal(["b", "d", "a", "c"], Order(ViewColumnSorter.Sort(entries, col, SortDirection.Asc)));
        // 降順でも未設定は末尾
        Assert.Equal(["a", "d", "b", "c"], Order(ViewColumnSorter.Sort(entries, col, SortDirection.Desc)));
    }

    // ---- シンプルタグ ----

    [Fact]
    public void シンプルタグは有無順で付与を先_空扱いにしない()
    {
        var entries = new[]
        {
            Entry("a", "a"),
            Entry("b", "b", tags: [(GachaTag, TagType.Simple, null)]),
            Entry("c", "c"),
            Entry("d", "d", tags: [(GachaTag, TagType.Simple, null)]),
        };
        var col = ViewSortColumn.ForTag(GachaTag, TagType.Simple);

        // 昇順=付与を先(b,d)→未付与(a,c)。各群は名前昇順で安定化
        Assert.Equal(["b", "d", "a", "c"], Order(ViewColumnSorter.Sort(entries, col, SortDirection.Asc)));
        // 降順=付与を後(空扱いにしないので反転する)
        Assert.Equal(["a", "c", "b", "d"], Order(ViewColumnSorter.Sort(entries, col, SortDirection.Desc)));
    }

    // ---- タイブレーク ----

    [Fact]
    public void 同値は名前の昇順で安定化する()
    {
        var entries = new[]
        {
            Entry("z", "zebra", tags: [(RatingTag, TagType.Numeric, "3")]),
            Entry("a", "apple", tags: [(RatingTag, TagType.Numeric, "3")]),
            Entry("m", "mango", tags: [(RatingTag, TagType.Numeric, "3")]),
        };
        var col = ViewSortColumn.ForTag(RatingTag, TagType.Numeric);

        // 全て評価3で同値 → 名前昇順(apple, mango, zebra)。降順でもタイブレークは名前昇順(反転しない)
        Assert.Equal(["a", "m", "z"], Order(ViewColumnSorter.Sort(entries, col, SortDirection.Asc)));
        Assert.Equal(["a", "m", "z"], Order(ViewColumnSorter.Sort(entries, col, SortDirection.Desc)));
    }

    // ---- 基本情報列 ----

    [Fact]
    public void 基本情報sizeは数値順_nameとdateは文字列順()
    {
        var entries = new[]
        {
            Entry("a", "b.png", size: 1000, modified: "2026-03-01T00:00:00.000Z"),
            Entry("b", "a.png", size: 200, modified: "2026-01-01T00:00:00.000Z"),
            Entry("c", "c.png", size: 30, modified: "2026-02-01T00:00:00.000Z"),
        };

        // size 昇順: 30, 200, 1000(数値順=c,b,a)
        Assert.Equal(["c", "b", "a"],
            Order(ViewColumnSorter.Sort(entries, ViewSortColumn.ForBasic("size"), SortDirection.Asc)));
        // name 昇順: a.png, b.png, c.png
        Assert.Equal(["b", "a", "c"],
            Order(ViewColumnSorter.Sort(entries, ViewSortColumn.ForBasic("name"), SortDirection.Asc)));
        // modified_date 昇順(ISO 文字列順=時系列): 01,02,03 → b,c,a
        Assert.Equal(["b", "c", "a"],
            Order(ViewColumnSorter.Sort(entries, ViewSortColumn.ForBasic("modified_date"), SortDirection.Asc)));
    }

    [Fact]
    public void 数値タグの非数値値は未設定扱いで末尾()
    {
        var entries = new[]
        {
            Entry("a", "a", tags: [(RatingTag, TagType.Numeric, "5")]),
            Entry("b", "b", tags: [(RatingTag, TagType.Numeric, "not-a-number")]),
        };
        var col = ViewSortColumn.ForTag(RatingTag, TagType.Numeric);

        Assert.Equal(["a", "b"], Order(ViewColumnSorter.Sort(entries, col, SortDirection.Asc)));
        Assert.Equal(["a", "b"], Order(ViewColumnSorter.Sort(entries, col, SortDirection.Desc))); // 非数値は末尾維持
    }
}
