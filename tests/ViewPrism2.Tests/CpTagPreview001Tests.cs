using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// CP-DISPLAY-PARITY-022 / DC-TAGPREVIEW-001(ECO-007/E5): タグ作成/編集ダイアログの付与プレビュー
/// 整形器(核側ヘルパ TagAssignmentPreviewBuilder)が種別別の付与表現を返すことを exact 検査する。
/// ★モード判定(単位=★ かつ span 0..9 整数)・テキスト候補チップ(先頭選択)・数値プレーン・
/// シンプルの出力。視覚描画は golden(G-6)。pixel-exact 不採用。
/// </summary>
[Trait("cp", "CP-DISPLAY-PARITY-022")]
public sealed class CpTagPreview001Tests
{
    private const string Placeholder = "タグ名";

    private static NumericTagSettings Numeric(double? min, double? max, double? step, string? unit)
        => new() { TagId = "t", Min = min, Max = max, Step = step, Unit = unit };

    // ---- シンプル ----

    [Fact]
    public void Simple_色ドットと名前のみ_値表現なし()
    {
        var preview = TagAssignmentPreviewBuilder.Build(
            TagType.Simple, "地域", "#30a46c", null, null, Placeholder);

        Assert.Equal(TagPreviewKind.Simple, preview.Kind);
        Assert.Equal("地域", preview.Name);
        Assert.Equal("#30a46c", preview.Color);
        Assert.Empty(preview.Chips);
        Assert.Equal(string.Empty, preview.NumericLabel);
    }

    [Fact]
    public void 名前が空白のみ_プレースホルダに置換()
    {
        var preview = TagAssignmentPreviewBuilder.Build(
            TagType.Simple, "   ", null, null, null, Placeholder);

        Assert.Equal(Placeholder, preview.Name);
        Assert.Null(preview.Color); // 空色は null
    }

    // ---- テキスト ----

    [Fact]
    public void Textual_候補値チップ_先頭が選択強調()
    {
        var preview = TagAssignmentPreviewBuilder.Build(
            TagType.Textual, "季節", "#2f6bed", ["春", "夏", "秋", "冬"], null, Placeholder);

        Assert.Equal(TagPreviewKind.Textual, preview.Kind);
        Assert.Equal(4, preview.Chips.Count);
        Assert.Equal("春", preview.Chips[0].Label);
        Assert.True(preview.Chips[0].IsSelected);    // 先頭=選択(塗り)
        Assert.False(preview.Chips[1].IsSelected);
        Assert.False(preview.Chips[3].IsSelected);
    }

    [Fact]
    public void Textual_候補ゼロ件_チップ空()
    {
        var preview = TagAssignmentPreviewBuilder.Build(
            TagType.Textual, "種別", null, [], null, Placeholder);

        Assert.Equal(TagPreviewKind.Textual, preview.Kind);
        Assert.Empty(preview.Chips);
    }

    // ---- 数値・★モード ----

    [Fact]
    public void Numeric_星モード_単位星かつ範囲1から5()
    {
        // モック既定: min=1,max=5,step=1,unit=★ → ★モード(span=4 ∈ [0,9] 整数)
        var preview = TagAssignmentPreviewBuilder.Build(
            TagType.Numeric, "評価", "#e8b931", null, Numeric(1, 5, 1, "★"), Placeholder);

        Assert.Equal(TagPreviewKind.NumericStar, preview.Kind);
        Assert.Equal(5, preview.TotalStars);     // 1..5 = 5 個
        Assert.Equal(3, preview.FilledStars);    // 代表値=round((1+5)/2)=3 → v<=3 が点灯
        Assert.Equal("3 ★", preview.NumericLabel);
    }

    [Fact]
    public void Numeric_星モード_範囲0から9境界()
    {
        // span=9 は境界内 → ★モード
        var preview = TagAssignmentPreviewBuilder.Build(
            TagType.Numeric, "段階", null, null, Numeric(0, 9, 1, "★"), Placeholder);

        Assert.Equal(TagPreviewKind.NumericStar, preview.Kind);
        Assert.Equal(10, preview.TotalStars);    // 0..9 = 10 個
    }

    [Fact]
    public void Numeric_星単位でも範囲10超はプレーン()
    {
        // unit=★ だが span=10 > 9 → ★モードにならない(プレーン)
        var preview = TagAssignmentPreviewBuilder.Build(
            TagType.Numeric, "値", null, null, Numeric(0, 10, 1, "★"), Placeholder);

        Assert.Equal(TagPreviewKind.NumericPlain, preview.Kind);
    }

    [Fact]
    public void Numeric_星単位でも非整数範囲はプレーン()
    {
        var preview = TagAssignmentPreviewBuilder.Build(
            TagType.Numeric, "値", null, null, Numeric(0.5, 5, 1, "★"), Placeholder);

        Assert.Equal(TagPreviewKind.NumericPlain, preview.Kind);
    }

    // ---- 数値・プレーン ----

    [Fact]
    public void Numeric_プレーン_単位パーセント()
    {
        // 0..100 % → プレーン(±ステッパ+数値ラベル)。代表値=50
        var preview = TagAssignmentPreviewBuilder.Build(
            TagType.Numeric, "進捗", "#2f6bed", null, Numeric(0, 100, 5, "%"), Placeholder);

        Assert.Equal(TagPreviewKind.NumericPlain, preview.Kind);
        Assert.Equal("50 %", preview.NumericLabel);
        Assert.Empty(preview.Chips);
    }

    [Fact]
    public void Numeric_設定なし_既定0から100のプレーン()
    {
        // 未設定 → min=0,max=100,unit="" → プレーン・単位なしラベル
        var preview = TagAssignmentPreviewBuilder.Build(
            TagType.Numeric, "数値", null, null, null, Placeholder);

        Assert.Equal(TagPreviewKind.NumericPlain, preview.Kind);
        Assert.Equal("50", preview.NumericLabel); // 単位なし
    }

    [Fact]
    public void Numeric_単位pt_範囲0から10は星単位でないためプレーン()
    {
        // pt 0..10(span=10): 単位が★でない → プレーン
        var preview = TagAssignmentPreviewBuilder.Build(
            TagType.Numeric, "ポイント", null, null, Numeric(0, 10, 1, "pt"), Placeholder);

        Assert.Equal(TagPreviewKind.NumericPlain, preview.Kind);
        Assert.Equal("5 pt", preview.NumericLabel);
    }
}
