using ViewPrism2.App.ViewModels;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// ECO-025 β-2: 表示列ポップオーバーの列ピッカー(<see cref="ColumnPickerViewModel"/>)。
/// 決定論コアは <see cref="ViewColumnModel"/>(CP-VIEW-012 で検査済み)を消費するため、ここでは β-2 net-new =
/// 「追加元の単一マージリスト(基本情報→タグ・種別チップ+色ドット)」「編集での <c>Changed</c> 発火(VE-003 書き戻し駆動)」
/// 「上限/追加元なしの表示状態」を検査する。描画は golden(承認者 maintainer)。
/// </summary>
[Trait("cp", "CP-COLPICKER-025")]
public sealed class CpColumnPickerViewModelTests
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

    private static LocalizationService CreateLoc()
        => new(new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
        {
            ["ja"] = new Dictionary<string, string>
            {
                ["view.columnCount"] = "合計 {count} 列 / 最大 {max}",
                ["view.columnChipBasic"] = "基本",
                ["view.columnChipTag"] = "タグ",
                ["view.columnFixed"] = "固定",
                ["view.displayColumns"] = "表示列",
                ["view.displayColumnsToFileList"] = "画像タブのファイル一覧に表示",
                ["filelist.addColumn"] = "列を追加",
                ["filelist.atMaxNote"] = "最大 {max} 列です。追加するには列を削除してください。",
                ["filelist.noAvailableColumns"] = "追加できる列はありません",
                ["tag.type.numeric"] = "数値",
                ["tag.type.textual"] = "テキスト",
                ["tag.type.simple"] = "シンプル",
                ["common.name"] = "名前",
                ["common.size"] = "サイズ",
                ["common.modifiedDate"] = "更新日",
            },
        });

    private static ColumnPickerViewModel Create(string? json)
        => new(json, ViewTags(), CreateLoc());

    [Fact]
    public void 追加元は基本情報が先タグが後の単一リストで種別チップと色ドットを持つ()
    {
        // 選択=name のみ → 追加元 = size/modified_date(基本)→ rating/job/featured(タグ)
        var vm = Create("""[{"type":"basic","key":"name"}]""");

        Assert.Equal(
            ["size", "modified_date", "tag-rating", "tag-job", "tag-featured"],
            vm.AvailableColumns.Select(c => c.Key));

        // 基本情報は種別チップ「基本」・色ドットなし
        var size = vm.AvailableColumns[0];
        Assert.Equal("基本", size.KindLabel);
        Assert.False(size.IsTag);
        Assert.False(size.ShowColorDot);

        // タグは型別チップ + 色ドット
        var rating = vm.AvailableColumns.First(c => c.Key == "tag-rating");
        Assert.Equal("数値", rating.KindLabel);
        Assert.True(rating.IsTag);
        Assert.True(rating.ShowColorDot);
        Assert.Equal("#e8b931", rating.Color);
        Assert.Equal("シンプル", vm.AvailableColumns.First(c => c.Key == "tag-featured").KindLabel);
    }

    [Fact]
    public void 選択済み先頭は名前固定で移動不可_VE001()
    {
        var vm = Create("""[{"type":"basic","key":"name"},{"type":"basic","key":"size"}]""");

        var name = vm.SelectedColumns[0];
        Assert.True(name.IsNameLocked);
        Assert.False(name.CanMoveUp);
        Assert.False(name.CanMoveDown);

        // index1(size)は name の直後で上へ動けない(VE-001)
        Assert.False(vm.SelectedColumns[1].CanMoveUp);
    }

    [Fact]
    public void 追加は末尾に足しChangedを発火する()
    {
        var vm = Create("""[{"type":"basic","key":"name"}]""");
        int changed = 0;
        vm.Changed += (_, _) => changed++;

        var rating = vm.AvailableColumns.First(c => c.Key == "tag-rating");
        vm.AddColumnCommand.Execute(rating);

        Assert.Equal(["name", "tag-rating"], vm.SelectedColumns.Select(c => c.Key));
        Assert.Equal(1, changed);
        // 追加した列は追加元から消える
        Assert.DoesNotContain(vm.AvailableColumns, c => c.Key == "tag-rating");
    }

    [Fact]
    public void 削除と並べ替えはChangedを発火し名前削除はno_op()
    {
        var vm = Create("""[{"type":"basic","key":"name"},{"type":"basic","key":"size"},{"type":"tag","key":"tag-rating"}]""");
        int changed = 0;
        vm.Changed += (_, _) => changed++;

        // 名前削除は no-op(VE-001)= Changed 出ない
        var name = vm.SelectedColumns[0];
        vm.RemoveColumnCommand.Execute(name);
        Assert.Equal(0, changed);
        Assert.Equal(3, vm.SelectedColumns.Count);

        // rating を上へ → size と入替(Changed 1)
        var rating = vm.SelectedColumns.First(c => c.Key == "tag-rating");
        vm.MoveColumnUpCommand.Execute(rating);
        Assert.Equal(["name", "tag-rating", "size"], vm.SelectedColumns.Select(c => c.Key));
        Assert.Equal(1, changed);

        // size を削除(Changed 2)
        var size = vm.SelectedColumns.First(c => c.Key == "size");
        vm.RemoveColumnCommand.Execute(size);
        Assert.Equal(["name", "tag-rating"], vm.SelectedColumns.Select(c => c.Key));
        Assert.Equal(2, changed);
    }

    [Fact]
    public void 上限5でIsAtLimitと件数バッジと警告文が出て追加元なしは出ない_VE002()
    {
        // name + size + 3 タグ = 5 列
        var vm = Create("""
            [{"type":"basic","key":"name"},{"type":"basic","key":"size"},
             {"type":"tag","key":"tag-rating"},{"type":"tag","key":"tag-job"},{"type":"tag","key":"tag-featured"}]
            """);

        Assert.True(vm.IsAtLimit);
        Assert.Equal("合計 5 列 / 最大 5", vm.ColumnCountText);
        Assert.Equal("最大 5 列です。追加するには列を削除してください。", vm.AtMaxNote);
        // 追加元は残っている(modified_date)が上限なので「追加できる列はありません」は出さない(警告優先)
        Assert.False(vm.ShowNoAvailable);
    }

    [Fact]
    public void 追加元を出し切ると追加できる列はありませんを出す()
    {
        // ビュー内タグ無し → 選択=name/size/modified_date で基本情報も尽き、追加元ゼロ
        var loc = CreateLoc();
        var vm = new ColumnPickerViewModel(
            """[{"type":"basic","key":"name"},{"type":"basic","key":"size"},{"type":"basic","key":"modified_date"}]""",
            [], loc);

        Assert.False(vm.IsAtLimit);      // 3 列 < 5
        Assert.False(vm.HasAvailable);
        Assert.True(vm.ShowNoAvailable);
        Assert.Equal("追加できる列はありません", vm.NoAvailableLabel);
    }

    [Fact]
    public void SerializeはDisplayColumnParserで往復できる_VE003書き戻し()
    {
        var vm = Create("""[{"type":"basic","key":"name"},{"type":"basic","key":"size"}]""");

        var rating = vm.AvailableColumns.First(c => c.Key == "tag-rating");
        vm.AddColumnCommand.Execute(rating);

        var tagById = ViewTags().ToDictionary(t => t.Id, StringComparer.Ordinal);
        var parsed = DisplayColumnParser.Parse(vm.Serialize(), tagById);

        Assert.Equal(["name", "size", "tag-rating"], parsed.Select(c => c.Key));
        Assert.Equal(DisplayColumnKind.Tag, parsed[2].Kind);
    }
}
