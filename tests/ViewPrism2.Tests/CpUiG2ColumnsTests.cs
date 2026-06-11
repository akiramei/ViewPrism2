using ViewPrism2.App.ViewModels;
using ViewPrism2.Core.Models;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// CP-UI-G2(unit 部分): display_columns の解釈(M-UI-013、REQ-042)。
/// 既定 3 列・basic/tag 列・削除済みタグ列の無視と残り列での全幅按分(AUDIT-102)。
/// 行選択・描画は golden(承認者 maintainer)。
/// </summary>
[Trait("cp", "CP-UI-G2")]
public sealed class CpUiG2ColumnsTests
{
    private static readonly Tag ColorTag = new()
    {
        Id = "tag-color",
        Name = "色",
        Type = TagType.Textual,
    };

    private static IReadOnlyDictionary<string, Tag> Tags() =>
        new Dictionary<string, Tag>(StringComparer.Ordinal) { [ColorTag.Id] = ColorTag };

    [Fact]
    public void 既定は3列でnameが2スター()
    {
        var columns = DisplayColumnParser.Parse(null, Tags());

        Assert.Equal(3, columns.Count); // 既定 3 列(REQ-042)
        Assert.Equal(["name", "size", "modified_date"], columns.Select(c => c.Key));
        Assert.All(columns, c => Assert.Equal(DisplayColumnKind.Basic, c.Kind));
        Assert.Equal(2, columns[0].Star); // name=2*、他=1*(M-BOM)
        Assert.Equal(1, columns[1].Star);
        Assert.Equal(1, columns[2].Star);
    }

    [Fact]
    public void JSON定義どおりの列構成になる()
    {
        var json = """
            [
              {"type":"basic","key":"name","label":"ファイル","width":3},
              {"type":"tag","key":"tag-color","label":"色ラベル","width":2},
              {"type":"basic","key":"size"}
            ]
            """;

        var columns = DisplayColumnParser.Parse(json, Tags());

        Assert.Equal(3, columns.Count);
        Assert.Equal(DisplayColumnKind.Basic, columns[0].Kind);
        Assert.Equal("ファイル", columns[0].Label);
        Assert.Equal(3, columns[0].Star);
        Assert.Equal(DisplayColumnKind.Tag, columns[1].Kind);
        Assert.Equal("tag-color", columns[1].Key);
        Assert.Equal("色ラベル", columns[1].Label);
        Assert.Equal(2, columns[1].Star);
        Assert.Equal(1, columns[2].Star); // width 省略は既定 1*
    }

    [Fact]
    public void 削除済みタグの列は無視され残り列で全幅を按分する_AUDIT102()
    {
        var json = """
            [
              {"type":"basic","key":"name"},
              {"type":"tag","key":"deleted-tag-id","label":"消えたタグ"},
              {"type":"basic","key":"size"}
            ]
            """;

        var columns = DisplayColumnParser.Parse(json, Tags());

        Assert.Equal(2, columns.Count); // 削除済みタグ列は無視して描画(REQ-042)
        Assert.Equal(["name", "size"], columns.Select(c => c.Key));
        Assert.Equal(3, columns.Sum(c => c.Star)); // 残り star(2+1)がそのまま全幅按分の分母になる
    }

    [Fact]
    public void タグ列のラベル省略はタグ名になる()
    {
        var json = """[{"type":"tag","key":"tag-color"}]""";

        var columns = DisplayColumnParser.Parse(json, Tags());

        Assert.Single(columns);
        Assert.Equal("色", columns[0].Label);
    }

    [Fact]
    public void 不正JSONや未知キーは既定列にフォールバックする()
    {
        Assert.Equal(3, DisplayColumnParser.Parse("{{{", Tags()).Count);
        Assert.Equal(3, DisplayColumnParser.Parse("""{"not":"array"}""", Tags()).Count);
        // 未知の basic キーのみ → 列 0 件 → 既定列
        Assert.Equal(3, DisplayColumnParser.Parse("""[{"type":"basic","key":"unknown"}]""", Tags()).Count);
    }
}
