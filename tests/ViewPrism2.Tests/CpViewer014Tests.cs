using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services.Viewer;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// CP-VIEWER-014: ビューア計算核(M-VIEWERCORE-017)が仕様 §2.9・OC-9〜13 と一致する。
/// 純粋計算の exact 検査(SpreadPairCalculator / PageTurnCalculator / ScrollPositionTracker /
/// SpreadHeightCalculator / ViewerModeMemory / ViewerSettingsModel)。
/// </summary>
[Trait("cp", "CP-VIEWER-014")]
public sealed class CpViewer014Tests
{
    // ============ OC-9: 見開きペアリング(SpreadPairCalculator) ============

    [Fact]
    public void OC9_右開き通常_先頭は左に相方右に現在()
    {
        // index=0,total=5 → (L=1,R=0)
        var pair = SpreadPairCalculator.Calculate(0, 5, SpreadDirection.Right, startWithEmptyPage: false);
        Assert.Equal(1, pair.LeftIndex);
        Assert.Equal(0, pair.RightIndex);
    }

    [Fact]
    public void OC9_右開き_相方が末尾超で空白()
    {
        // index=4,total=5 → (L=空白,R=4)
        var pair = SpreadPairCalculator.Calculate(4, 5, SpreadDirection.Right, startWithEmptyPage: false);
        Assert.Null(pair.LeftIndex);
        Assert.Equal(4, pair.RightIndex);
    }

    [Fact]
    public void OC9_左開き通常_左に現在右に相方()
    {
        // index=0 → (L=0,R=1)
        var pair = SpreadPairCalculator.Calculate(0, 5, SpreadDirection.Left, startWithEmptyPage: false);
        Assert.Equal(0, pair.LeftIndex);
        Assert.Equal(1, pair.RightIndex);
    }

    [Fact]
    public void OC9_左開き_相方が末尾超で空白()
    {
        // index=4,total=5 → (L=4,R=空白)
        var pair = SpreadPairCalculator.Calculate(4, 5, SpreadDirection.Left, startWithEmptyPage: false);
        Assert.Equal(4, pair.LeftIndex);
        Assert.Null(pair.RightIndex);
    }

    [Fact]
    public void OC9_空白開始ON_index0_進行方向側に1枚目()
    {
        // 右開き → (L=0,R=空白) / 左開き → (L=空白,R=0)
        var right = SpreadPairCalculator.Calculate(0, 5, SpreadDirection.Right, startWithEmptyPage: true);
        Assert.Equal(0, right.LeftIndex);
        Assert.Null(right.RightIndex);

        var left = SpreadPairCalculator.Calculate(0, 5, SpreadDirection.Left, startWithEmptyPage: true);
        Assert.Null(left.LeftIndex);
        Assert.Equal(0, left.RightIndex);
    }

    [Fact]
    public void OC9_空白開始ON_indexが0より大は通常規則と同じ()
    {
        // index>0 は通常規則(特殊配置は index 0 のみ)
        var on = SpreadPairCalculator.Calculate(1, 5, SpreadDirection.Right, startWithEmptyPage: true);
        var off = SpreadPairCalculator.Calculate(1, 5, SpreadDirection.Right, startWithEmptyPage: false);
        Assert.Equal(off.LeftIndex, on.LeftIndex);
        Assert.Equal(off.RightIndex, on.RightIndex);
        Assert.Equal(2, on.LeftIndex);
        Assert.Equal(1, on.RightIndex);
    }

    [Fact]
    public void OC9_total1_右開きは左空白右に0()
    {
        var pair = SpreadPairCalculator.Calculate(0, 1, SpreadDirection.Right, startWithEmptyPage: false);
        Assert.Null(pair.LeftIndex);
        Assert.Equal(0, pair.RightIndex);
    }

    [Fact]
    public void OC9_total0は両側空白()
    {
        var right = SpreadPairCalculator.Calculate(0, 0, SpreadDirection.Right, startWithEmptyPage: false);
        Assert.Null(right.LeftIndex);
        Assert.Null(right.RightIndex);
    }

    [Fact]
    public void OC9_境界_末尾index_左開き総数偶数()
    {
        // index=total-1=5,total=6,左開き → (L=5,R=空白)
        var pair = SpreadPairCalculator.Calculate(5, 6, SpreadDirection.Left, startWithEmptyPage: false);
        Assert.Equal(5, pair.LeftIndex);
        Assert.Null(pair.RightIndex);
    }

    // ============ OC-10: ページ送り(PageTurnCalculator) ============

    [Theory]
    [InlineData(0, 10, 2, 2)]    // index=0,step=2 → 2
    [InlineData(7, 10, 2, 9)]    // index=7,step=2 → 9(末尾相方)
    [InlineData(8, 10, 2, 9)]    // index=8,step=2 → 9(クランプ)
    [InlineData(9, 10, 2, 9)]    // index=9 → 9(停止・変化なし)
    public void OC10_次へ_境界クランプと停止(int index, int total, int step, int expected)
    {
        Assert.Equal(expected, PageTurnCalculator.Next(index, total, step, startWithEmptyPage: false));
    }

    [Theory]
    [InlineData(2, 2, 0)]    // index=2,step=2 → 0
    [InlineData(1, 2, 0)]    // index=1,step=2 → 0(クランプ)
    [InlineData(0, 2, 0)]    // index=0 → 0(停止)
    public void OC10_前へ_境界クランプと停止(int index, int step, int expected)
    {
        Assert.Equal(expected, PageTurnCalculator.Prev(index, step));
    }

    [Fact]
    public void OC10_空白開始ON_index0次へは1へ_FMEA017()
    {
        // step=2 でも 0→1(特殊送り)
        Assert.Equal(1, PageTurnCalculator.Next(0, 10, step: 2, startWithEmptyPage: true));
        // SHIFT(step=1)でも → 1(通常規則と同値)
        Assert.Equal(1, PageTurnCalculator.Next(0, 10, step: 1, startWithEmptyPage: true));
    }

    [Fact]
    public void OC10_singlePage_偶奇ずれを維持_再アラインしない()
    {
        // step=1: index=0 → 1 → 2
        var a = PageTurnCalculator.Next(0, 10, step: 1, startWithEmptyPage: false);
        Assert.Equal(1, a);
        var b = PageTurnCalculator.Next(a, 10, step: 1, startWithEmptyPage: false);
        Assert.Equal(2, b);
    }

    [Fact]
    public void OC10_境界_total0とtotal1()
    {
        Assert.Equal(0, PageTurnCalculator.Next(0, 0, step: 2, startWithEmptyPage: false));
        Assert.Equal(0, PageTurnCalculator.Next(0, 1, step: 2, startWithEmptyPage: false)); // 既に末尾(=0)で停止
        Assert.Equal(0, PageTurnCalculator.Prev(0, 2));
    }

    // ============ OC-11: スクロール現在位置(ScrollPositionTracker) ============

    [Fact]
    public void OC11_中央最近傍()
    {
        // 3 画像(top/height)。viewport 高さ 100、スクロール 250 → 中央=300
        // img0: center=100 / img1: center=300(最近傍) / img2: center=500
        var rects = new List<(double, double)> { (50, 100), (250, 100), (450, 100) };
        Assert.Equal(1, ScrollPositionTracker.FindCurrent(rects, viewportHeight: 100, scrollOffset: 250));
    }

    [Fact]
    public void OC11_同距離は若いindex_M2()
    {
        // viewport 中央 = 0 + 200/2 = 100。img0 center=50(距離50)、img1 center=150(距離50)→ 同距離 → 若い 0
        var rects = new List<(double, double)> { (0, 100), (100, 100) };
        Assert.Equal(0, ScrollPositionTracker.FindCurrent(rects, viewportHeight: 200, scrollOffset: 0));
    }

    [Fact]
    public void OC11_境界_先頭表示は0_最下端は最終index()
    {
        var rects = new List<(double, double)> { (0, 300), (300, 300), (600, 300) };
        // スクロール 0(先頭表示。viewport 300 → 中央 150)→ img0 center=150 → 0
        Assert.Equal(0, ScrollPositionTracker.FindCurrent(rects, viewportHeight: 300, scrollOffset: 0));
        // 最下端(scrollOffset=600、viewport 300 → 中央 750)→ img2 center=750 → 2
        Assert.Equal(2, ScrollPositionTracker.FindCurrent(rects, viewportHeight: 300, scrollOffset: 600));
    }

    [Fact]
    public void OC11_空リストは0()
    {
        Assert.Equal(0, ScrollPositionTracker.FindCurrent([], viewportHeight: 100, scrollOffset: 0));
    }

    [Fact]
    public void OC11_単一画像は0()
    {
        var rects = new List<(double, double)> { (0, 500) };
        Assert.Equal(0, ScrollPositionTracker.FindCurrent(rects, viewportHeight: 100, scrollOffset: 9999));
    }

    // ============ OC-12: 見開き高さ統一(SpreadHeightCalculator) ============

    [Fact]
    public void OC12_matchLargerHeight_高い方へ_90パーセント上限内()
    {
        // (800,1000) → 1000(viewport 大なら上限に当たらない)
        var h = SpreadHeightCalculator.Calculate(
            new ImageSize(600, 800), new ImageSize(700, 1000), ResizeMode.MatchLargerHeight, viewportHeight: 2000);
        Assert.Equal(1000, h);
    }

    [Fact]
    public void OC12_matchSmallerHeight_低い方へ()
    {
        var h = SpreadHeightCalculator.Calculate(
            new ImageSize(600, 800), new ImageSize(700, 1000), ResizeMode.MatchSmallerHeight, viewportHeight: 2000);
        Assert.Equal(800, h);
    }

    [Fact]
    public void OC12_90パーセント上限が効く()
    {
        // 統一 1000 だが viewport 1000 → 上限 900
        var h = SpreadHeightCalculator.Calculate(
            new ImageSize(600, 800), new ImageSize(700, 1000), ResizeMode.MatchLargerHeight, viewportHeight: 1000);
        Assert.Equal(900, h);
    }

    [Fact]
    public void OC12_片側空白_単独高さに90パーセント上限()
    {
        // (800, null) → 800(viewport 大)
        var h = SpreadHeightCalculator.Calculate(
            new ImageSize(600, 800), null, ResizeMode.MatchLargerHeight, viewportHeight: 2000);
        Assert.Equal(800, h);

        // 上限適用: 単独 800 だが viewport 500 → 上限 450
        var capped = SpreadHeightCalculator.Calculate(
            new ImageSize(600, 800), null, ResizeMode.MatchLargerHeight, viewportHeight: 500);
        Assert.Equal(450, capped);
    }

    [Fact]
    public void OC12_noResizeは統一なしnull_片側空白でも()
    {
        Assert.Null(SpreadHeightCalculator.Calculate(
            new ImageSize(600, 800), new ImageSize(700, 1000), ResizeMode.NoResize, viewportHeight: 2000));
        Assert.Null(SpreadHeightCalculator.Calculate(
            new ImageSize(600, 800), null, ResizeMode.NoResize, viewportHeight: 2000));
    }

    [Fact]
    public void OC12_両側空白はnull()
    {
        Assert.Null(SpreadHeightCalculator.Calculate(
            null, null, ResizeMode.MatchLargerHeight, viewportHeight: 2000));
    }

    // ============ OC-13: モード別位置記憶(ViewerModeMemory) ============

    [Fact]
    public void OC13_操作列_モード別に独立記憶_FMEA020()
    {
        // 起動 index=5 → 全モード初期値 5
        var memory = new ViewerModeMemory(initialIndex: 5);
        Assert.Equal(5, memory.Get(ViewerMode.Normal));
        Assert.Equal(5, memory.Get(ViewerMode.Scroll));
        Assert.Equal(5, memory.Get(ViewerMode.SpreadRight));
        Assert.Equal(5, memory.Get(ViewerMode.SpreadLeft));

        // scroll で 20 へ
        memory.Set(ViewerMode.Scroll, 20);

        // spread-right 切替 → 5(spread の記憶=起動初期値のまま)
        Assert.Equal(5, memory.Get(ViewerMode.SpreadRight));

        // scroll へ戻す → 20(共通 index 引き継ぎではない)
        Assert.Equal(20, memory.Get(ViewerMode.Scroll));
    }

    [Fact]
    public void OC13_境界_初期index0()
    {
        var memory = new ViewerModeMemory(0);
        Assert.Equal(0, memory.Get(ViewerMode.Normal));
        memory.Set(ViewerMode.SpreadLeft, 0);
        Assert.Equal(0, memory.Get(ViewerMode.SpreadLeft));
    }

    // ============ ViewerSettingsModel: 列挙⇔文字列・既定化 ============

    [Fact]
    public void SettingsModel_文字列ラウンドトリップ()
    {
        Assert.Equal(ViewerMode.SpreadRight, ViewerSettingsModel.ParseMode("spread-right"));
        Assert.Equal("spread-right", ViewerSettingsModel.ToString(ViewerMode.SpreadRight));
        Assert.Equal(ViewerMode.SpreadLeft, ViewerSettingsModel.ParseMode("spread-left"));
        Assert.Equal("noResize", ViewerSettingsModel.ToString(ResizeMode.NoResize));
        Assert.Equal(ResizeMode.MatchLargerHeight, ViewerSettingsModel.ParseResize("matchLargerHeight"));
        Assert.Equal(AlignMode.Bottom, ViewerSettingsModel.ParseAlign("bottom"));
        Assert.Equal(GapMode.Loose, ViewerSettingsModel.ParseGap("loose"));
        Assert.Equal(PageTurnMode.SinglePage, ViewerSettingsModel.ParseTurn("singlePage"));
    }

    [Fact]
    public void SettingsModel_列挙外文字列とnullは既定()
    {
        Assert.Equal(ViewerMode.Normal, ViewerSettingsModel.ParseMode("xyz"));
        Assert.Equal(ViewerMode.Normal, ViewerSettingsModel.ParseMode(null));
        Assert.Equal(ResizeMode.NoResize, ViewerSettingsModel.ParseResize("???"));
        Assert.Equal(AlignMode.Middle, ViewerSettingsModel.ParseAlign(null));
        Assert.Equal(GapMode.Tight, ViewerSettingsModel.ParseGap("x"));
        Assert.Equal(PageTurnMode.DoublePage, ViewerSettingsModel.ParseTurn("x"));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(100, 100)]
    [InlineData(50, 50)]
    [InlineData(-1, 0)]
    [InlineData(101, 0)]
    [InlineData(9999, 0)]
    public void SettingsModel_customGapPx範囲外は0(int input, int expected)
    {
        Assert.Equal(expected, ViewerSettingsModel.NormalizeGapPx(input));
    }

    [Fact]
    public void SettingsModel_AppSettingsとの往復()
    {
        var settings = new AppSettings
        {
            ViewerMode = "spread-left",
            ViewerResizeMode = "matchSmallerHeight",
            ViewerAlignMode = "top",
            ViewerGapMode = "loose",
            ViewerCustomGapPx = 16,
            ViewerPageTurnMode = "singlePage",
            ViewerStartWithEmptyPage = true,
        };

        var model = ViewerSettingsModel.FromSettings(settings);
        Assert.Equal(ViewerMode.SpreadLeft, model.Mode);
        Assert.Equal(ResizeMode.MatchSmallerHeight, model.ResizeMode);
        Assert.Equal(AlignMode.Top, model.AlignMode);
        Assert.Equal(GapMode.Loose, model.GapMode);
        Assert.Equal(16, model.CustomGapPx);
        Assert.Equal(PageTurnMode.SinglePage, model.PageTurnMode);
        Assert.True(model.StartWithEmptyPage);

        var back = new AppSettings();
        model.ApplyTo(back);
        Assert.Equal("spread-left", back.ViewerMode);
        Assert.Equal("matchSmallerHeight", back.ViewerResizeMode);
        Assert.Equal("top", back.ViewerAlignMode);
        Assert.Equal("loose", back.ViewerGapMode);
        Assert.Equal(16, back.ViewerCustomGapPx);
        Assert.Equal("singlePage", back.ViewerPageTurnMode);
        Assert.True(back.ViewerStartWithEmptyPage);
    }

    [Fact]
    public void SettingsModel_既定値()
    {
        var model = ViewerSettingsModel.FromSettings(new AppSettings());
        Assert.Equal(ViewerMode.Normal, model.Mode);
        Assert.Equal(ResizeMode.NoResize, model.ResizeMode);
        Assert.Equal(AlignMode.Middle, model.AlignMode);
        Assert.Equal(GapMode.Tight, model.GapMode);
        Assert.Equal(0, model.CustomGapPx);
        Assert.Equal(PageTurnMode.DoublePage, model.PageTurnMode);
        Assert.False(model.StartWithEmptyPage);
    }
}
