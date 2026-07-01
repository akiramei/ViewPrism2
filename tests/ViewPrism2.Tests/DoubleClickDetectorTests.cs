using ViewPrism2.App.ViewModels;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// ダブルクリック判定フォールバック(DF-4 堅牢化: ClickCount 非依存の自前検出)。
/// ECO-024 で原典画像タブ code-behind を撤去したが DoubleClickDetector は新 surface
/// (ImageTabView.axaml.cs)も使用する存続部品のため、本 unit は CpUiG1SelectionTests から
/// 分離して保全する。時間ウィンドウ内の同一アイテム連続=ダブル / 時間超過・別アイテム・
/// 修飾キー付き(選択操作)は非ダブルでリセット。
/// </summary>
[Trait("cp", "CP-UI-G1")]
public sealed class DoubleClickDetectorTests
{
    [Fact]
    public void 同一アイテムへの時間内連続クリックはダブルクリックと判定する()
    {
        var detector = new DoubleClickDetector();
        var item = new object();

        Assert.False(detector.ObserveClick(item, 1000, 500));
        Assert.True(detector.ObserveClick(item, 1300, 500));  // 300ms 後 → ダブル
        Assert.False(detector.ObserveClick(item, 1400, 500)); // 成立後はリセット(3 連打で再成立しない)
    }

    [Fact]
    public void 時間超過や別アイテムはダブルクリックにならない()
    {
        var detector = new DoubleClickDetector();
        var a = new object();
        var b = new object();

        Assert.False(detector.ObserveClick(a, 1000, 500));
        Assert.False(detector.ObserveClick(a, 1600, 500)); // 600ms 後 → 時間超過
        Assert.False(detector.ObserveClick(b, 1700, 500)); // 別アイテム
        Assert.True(detector.ObserveClick(b, 1800, 500));  // b の 2 回目
    }

    [Fact]
    public void 修飾キー付きクリックは判定対象外で状態をリセットする()
    {
        var detector = new DoubleClickDetector();
        var item = new object();

        Assert.False(detector.ObserveClick(item, 1000, 500));
        Assert.False(detector.ObserveClick(item, 1100, 500, hasModifiers: true)); // Ctrl/Shift は選択操作
        Assert.False(detector.ObserveClick(item, 1200, 500)); // リセット済みなので 1 回目扱い
        Assert.True(detector.ObserveClick(item, 1300, 500));
    }
}
