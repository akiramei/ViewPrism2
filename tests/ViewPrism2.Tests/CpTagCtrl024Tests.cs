using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services.Viewer;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// CP-TAGCTRL-024: タグ制御配置核(M-TAGCTRL-028)が仕様 §2.12・OC-23/24/25 と一致する。
/// 純粋計算の exact 検査(TagActionResolver / TagControlLayoutCalculator / TagControlNavigator)。
/// ECO-022。OFF-identity(アクション無し列=SpreadPairCalculator 全位置一致)を含む。
/// </summary>
[Trait("cp", "CP-TAGCTRL-024")]
public sealed class CpTagCtrl024Tests
{
    private const string TSkip = "tag-skip";
    private const string TSpread = "tag-spread";
    private const string TForceLeft = "tag-force-left";
    private const string TForceRight = "tag-force-right";
    private const string TLeftEmpty = "tag-left-empty";
    private const string TRightEmpty = "tag-right-empty";

    private static IReadOnlyDictionary<ViewerTagAction, string?> FullMap() =>
        new Dictionary<ViewerTagAction, string?>
        {
            [ViewerTagAction.Skip] = TSkip,
            [ViewerTagAction.Spread] = TSpread,
            [ViewerTagAction.ForceLeftPage] = TForceLeft,
            [ViewerTagAction.ForceRightPage] = TForceRight,
            [ViewerTagAction.LeftPageEmpty] = TLeftEmpty,
            [ViewerTagAction.RightPageEmpty] = TRightEmpty,
        };

    // ==================== OC-23: アクション解決 ====================

    [Fact]
    public void OC23_競合順_skip_spread_でskipが支配()
    {
        var map = FullMap();
        Assert.Equal(ViewerTagAction.Skip, TagActionResolver.Resolve(new[] { TSkip, TSpread }, map));
    }

    [Fact]
    public void OC23_競合順_spread_forceLeft_でspreadが支配()
    {
        var map = FullMap();
        Assert.Equal(ViewerTagAction.Spread, TagActionResolver.Resolve(new[] { TSpread, TForceLeft }, map));
    }

    [Fact]
    public void OC23_競合順_forceRight_leftEmpty_でforceRightが支配()
    {
        var map = FullMap();
        Assert.Equal(
            ViewerTagAction.ForceRightPage,
            TagActionResolver.Resolve(new[] { TForceRight, TLeftEmpty }, map));
    }

    [Fact]
    public void OC23_全順序_支配優先の網羅()
    {
        var map = FullMap();
        // skip > spread > forceLeft > forceRight > leftEmpty > rightEmpty
        Assert.Equal(ViewerTagAction.ForceLeftPage,
            TagActionResolver.Resolve(new[] { TForceLeft, TForceRight, TLeftEmpty, TRightEmpty }, map));
        Assert.Equal(ViewerTagAction.ForceRightPage,
            TagActionResolver.Resolve(new[] { TForceRight, TLeftEmpty, TRightEmpty }, map));
        Assert.Equal(ViewerTagAction.LeftPageEmpty,
            TagActionResolver.Resolve(new[] { TLeftEmpty, TRightEmpty }, map));
        Assert.Equal(ViewerTagAction.RightPageEmpty,
            TagActionResolver.Resolve(new[] { TRightEmpty }, map));
    }

    [Fact]
    public void OC23_無視_未マッピング_map不在_現存しないタグ_タグ無し()
    {
        var map = FullMap();
        // map に無い tag_id(別タグ)→ アクション無し
        Assert.Null(TagActionResolver.Resolve(new[] { "unrelated-tag" }, map));
        // タグ無し画像 → 無し
        Assert.Null(TagActionResolver.Resolve(Array.Empty<string>(), map));
        // 未割り当て(map の値が null)のアクション先タグを持っていても無視
        var partial = new Dictionary<ViewerTagAction, string?>
        {
            [ViewerTagAction.Skip] = null,
            [ViewerTagAction.Spread] = TSpread,
        };
        // skip は未割り当てなので、spread が支配
        Assert.Equal(ViewerTagAction.Spread, TagActionResolver.Resolve(new[] { TSkip, TSpread }, partial));
    }

    // ==================== OC-24: ページプラン構築 ====================

    private static (int, ViewerTagAction?) It(int idx, ViewerTagAction? a = null) => (idx, a);

    [Fact]
    public void OC24_worked_example_右開き_forceRight()
    {
        // [A=0, 広告=1 skip, B=2 forceRight, C=3] → [右:A|左:空][右:B|左:C]
        var items = new[]
        {
            It(0),
            It(1, ViewerTagAction.Skip),
            It(2, ViewerTagAction.ForceRightPage),
            It(3),
        };
        var plan = TagControlLayoutCalculator.Build(items, SpreadDirection.Right, startWithEmptyPage: false);

        Assert.Equal(2, plan.Spreads.Count);
        // 見開き0: 右=A(0), 左=空
        Assert.Equal(0, plan.Spreads[0].RightIndex);
        Assert.Null(plan.Spreads[0].LeftIndex);
        // 見開き1: 右=B(2), 左=C(3)
        Assert.Equal(2, plan.Spreads[1].RightIndex);
        Assert.Equal(3, plan.Spreads[1].LeftIndex);
        Assert.Equal(3, plan.NonSkipCount);
        Assert.Equal(0, plan.Spreads[0].CanonicalImage);
    }

    [Fact]
    public void OC24_worked_example_右開き_leftPageEmpty()
    {
        // [A=0 leftPageEmpty, 広告=1 skip, B=2, C=3] → [右:A|左:空][右:B|左:C]
        var items = new[]
        {
            It(0, ViewerTagAction.LeftPageEmpty),
            It(1, ViewerTagAction.Skip),
            It(2),
            It(3),
        };
        var plan = TagControlLayoutCalculator.Build(items, SpreadDirection.Right, startWithEmptyPage: false);

        Assert.Equal(2, plan.Spreads.Count);
        Assert.Equal(0, plan.Spreads[0].RightIndex);
        Assert.Null(plan.Spreads[0].LeftIndex);
        Assert.Equal(2, plan.Spreads[1].RightIndex);
        Assert.Equal(3, plan.Spreads[1].LeftIndex);
    }

    [Fact]
    public void OC24_開始アンカー_右開き_forceLeftPage()
    {
        // [広告=0 skip, A=1 forceLeft, B=2, C=3] → [右:空|左:A][右:B|左:C]
        var items = new[]
        {
            It(0, ViewerTagAction.Skip),
            It(1, ViewerTagAction.ForceLeftPage),
            It(2),
            It(3),
        };
        var plan = TagControlLayoutCalculator.Build(items, SpreadDirection.Right, startWithEmptyPage: false);

        Assert.Equal(2, plan.Spreads.Count);
        // 見開き0: 右=空, 左=A(1)
        Assert.Null(plan.Spreads[0].RightIndex);
        Assert.Equal(1, plan.Spreads[0].LeftIndex);
        // 見開き1: 右=B(2), 左=C(3)
        Assert.Equal(2, plan.Spreads[1].RightIndex);
        Assert.Equal(3, plan.Spreads[1].LeftIndex);
        // canonical: 先読み面 S[0]=空白なので後読み面 A
        Assert.Equal(1, plan.Spreads[0].CanonicalImage);
    }

    [Fact]
    public void OC24_spread_padToParity_空白開始OFF()
    {
        // [X=0, Y=1 spread] → [右:X|左:空][Y 見開き占有]
        var items = new[] { It(0), It(1, ViewerTagAction.Spread) };
        var plan = TagControlLayoutCalculator.Build(items, SpreadDirection.Right, startWithEmptyPage: false);

        Assert.Equal(2, plan.Spreads.Count);
        // 見開き0: 右=X(0), 左=空(spread の padToParity(0) で X は片側空白に確定)
        Assert.Equal(0, plan.Spreads[0].RightIndex);
        Assert.Null(plan.Spreads[0].LeftIndex);
        Assert.False(plan.Spreads[0].IsSpread);
        // 見開き1: Y 占有
        Assert.True(plan.Spreads[1].IsSpread);
        Assert.Equal(1, plan.Spreads[1].LeftIndex);
        Assert.Equal(1, plan.Spreads[1].RightIndex);
        Assert.Equal(1, plan.Spreads[1].CanonicalImage);
        // 画像→見開き対応
        Assert.Equal(0, plan.ImageToSpread[0]);
        Assert.Equal(1, plan.ImageToSpread[1]);
    }

    [Fact]
    public void OC24_漫画見開き_右開き_空白開始ON()
    {
        // [表紙=0, 右半=1, 左半=2, p=3] 空白開始ON → [右:空|左:表紙][右:右半|左:左半][右:p|左:空]
        var items = new[] { It(0), It(1), It(2), It(3) };
        var plan = TagControlLayoutCalculator.Build(items, SpreadDirection.Right, startWithEmptyPage: true);

        Assert.Equal(3, plan.Spreads.Count);
        // [右:空|左:表紙]
        Assert.Null(plan.Spreads[0].RightIndex);
        Assert.Equal(0, plan.Spreads[0].LeftIndex);
        // [右:右半|左:左半]
        Assert.Equal(1, plan.Spreads[1].RightIndex);
        Assert.Equal(2, plan.Spreads[1].LeftIndex);
        // [右:p|左:空]
        Assert.Equal(3, plan.Spreads[2].RightIndex);
        Assert.Null(plan.Spreads[2].LeftIndex);
    }

    [Fact]
    public void OC24_seed抑止_両面空白封じ込め()
    {
        // 空白開始ON・[A=0 forceRight, B=1] → [右:A|左:B](両面空白の見開きを生成しない)。canonical=A
        var items = new[] { It(0, ViewerTagAction.ForceRightPage), It(1) };
        var plan = TagControlLayoutCalculator.Build(items, SpreadDirection.Right, startWithEmptyPage: true);

        Assert.Single(plan.Spreads);
        Assert.Equal(0, plan.Spreads[0].RightIndex);
        Assert.Equal(1, plan.Spreads[0].LeftIndex);
        Assert.Equal(0, plan.Spreads[0].CanonicalImage);
    }

    // ==================== OC-24: OFF-identity(回帰保証) ====================

    [Theory]
    [InlineData(SpreadDirection.Right, false)]
    [InlineData(SpreadDirection.Right, true)]
    [InlineData(SpreadDirection.Left, false)]
    [InlineData(SpreadDirection.Left, true)]
    public void OC24_OFF_identity_全アクション無し列がSpreadPairCalculatorと全位置一致(
        SpreadDirection direction, bool startWithEmptyPage)
    {
        for (var total = 0; total <= 7; total++)
        {
            var items = new List<(int, ViewerTagAction?)>(total);
            for (var i = 0; i < total; i++)
            {
                items.Add((i, null));
            }

            var plan = TagControlLayoutCalculator.Build(items, direction, startWithEmptyPage);

            // SpreadPairCalculator(OC-9)の各 canonical 見開きを再現し、プランと全位置一致するか照合。
            // OC-9 のナビは index アンカー(空白開始 ON は index 0 が表紙単独・以降 (1,2)(3,4)…)。
            var expected = new List<SpreadPair>();
            if (total > 0)
            {
                // 空白開始 ON: index 0(表紙単独)→ index 1 → 3 → 5 … (奇数アンカー)
                // 空白開始 OFF: index 0 → 2 → 4 … (偶数アンカー)
                var idx = 0;
                expected.Add(SpreadPairCalculator.Calculate(idx, total, direction, startWithEmptyPage));
                idx = startWithEmptyPage ? 1 : 2;
                while (idx <= total - 1)
                {
                    expected.Add(SpreadPairCalculator.Calculate(idx, total, direction, startWithEmptyPage));
                    idx += 2;
                }
            }

            Assert.Equal(expected.Count, plan.Spreads.Count);
            for (var k = 0; k < expected.Count; k++)
            {
                Assert.Equal(expected[k].LeftIndex, plan.Spreads[k].LeftIndex);
                Assert.Equal(expected[k].RightIndex, plan.Spreads[k].RightIndex);
            }
        }
    }

    // ==================== OC-24: canonical / 対応 ====================

    [Fact]
    public void OC24_canonical_先読み面優先_片側空白は単独()
    {
        // 右開き [A=0]: slots [0, 空] → 見開き0 右=A 左=空・canonical=A
        var plan = TagControlLayoutCalculator.Build(new[] { It(0) }, SpreadDirection.Right, false);
        Assert.Single(plan.Spreads);
        Assert.Equal(0, plan.Spreads[0].CanonicalImage);
        Assert.Equal(0, plan.ImageToSpread[0]);
    }

    [Fact]
    public void OC24_画像対応_一意()
    {
        // [0,1,2,3,4] 右開き OFF
        var items = new[] { It(0), It(1), It(2), It(3), It(4) };
        var plan = TagControlLayoutCalculator.Build(items, SpreadDirection.Right, false);
        // 各画像はちょうど 1 見開きに属する
        for (var i = 0; i <= 4; i++)
        {
            Assert.True(plan.ImageToSpread.ContainsKey(i));
        }

        Assert.Equal(0, plan.ImageToSpread[0]);
        Assert.Equal(0, plan.ImageToSpread[1]);
        Assert.Equal(1, plan.ImageToSpread[2]);
        Assert.Equal(1, plan.ImageToSpread[3]);
        Assert.Equal(2, plan.ImageToSpread[4]);
    }

    // ==================== OC-25: ナビゲーション ====================

    [Fact]
    public void OC25_送り_次へ前へ_端クランプ()
    {
        // cur=0 → 次へ=1 / 末尾 → 変化なし / cur=0 前へ=0(クランプ)
        Assert.Equal(1, TagControlNavigator.Next(0, 3));
        Assert.Equal(2, TagControlNavigator.Next(2, 3)); // 末尾で変化なし
        Assert.Equal(0, TagControlNavigator.Prev(0, 3)); // 先頭で変化なし
        Assert.Equal(1, TagControlNavigator.Prev(2, 3));
    }

    [Fact]
    public void OC25_モード復元_画像indexからそれを含む見開き()
    {
        var items = new[] { It(0), It(1), It(2), It(3), It(4) };
        var plan = TagControlLayoutCalculator.Build(items, SpreadDirection.Right, false);
        Assert.Equal(1, TagControlNavigator.SpreadOfImage(plan, 2));
        Assert.Equal(2, TagControlNavigator.SpreadOfImage(plan, 4));
        // canonical 取得
        Assert.Equal(2, plan.Spreads[1].CanonicalImage); // 先読み面 S[2]=2(右開き)
        Assert.Equal(2, TagControlNavigator.CanonicalImage(plan, 1));
    }

    // ==================== 境界網羅 ====================

    [Fact]
    public void 境界_total0_プラン空()
    {
        var plan = TagControlLayoutCalculator.Build(Array.Empty<(int, ViewerTagAction?)>(), SpreadDirection.Right, false);
        Assert.Empty(plan.Spreads);
        Assert.Equal(0, plan.NonSkipCount);
        Assert.Equal(0, TagControlNavigator.Next(0, 0));
        Assert.Equal(-1, TagControlNavigator.CanonicalImage(plan, 0));
    }

    [Fact]
    public void 境界_total1()
    {
        // 右開き [0] → [右:0|左:空]
        var plan = TagControlLayoutCalculator.Build(new[] { It(0) }, SpreadDirection.Right, false);
        Assert.Single(plan.Spreads);
        Assert.Equal(0, plan.Spreads[0].RightIndex);
        Assert.Null(plan.Spreads[0].LeftIndex);
        Assert.Equal(1, plan.NonSkipCount);
    }

    [Fact]
    public void 境界_全skip_プラン空()
    {
        var items = new[]
        {
            It(0, ViewerTagAction.Skip),
            It(1, ViewerTagAction.Skip),
        };
        var plan = TagControlLayoutCalculator.Build(items, SpreadDirection.Right, false);
        Assert.Empty(plan.Spreads);
        Assert.Equal(0, plan.NonSkipCount);
    }

    [Fact]
    public void 境界_先頭skip連続_空白開始ON_seed判定は最初の非skip基準()
    {
        // [skip, skip, A=2, B=3] 空白開始ON・最初の非skip A はアクション無し → seed 予約
        var items = new[]
        {
            It(0, ViewerTagAction.Skip),
            It(1, ViewerTagAction.Skip),
            It(2),
            It(3),
        };
        var plan = TagControlLayoutCalculator.Build(items, SpreadDirection.Right, startWithEmptyPage: true);
        // slots [空, 2, 3] → 末尾 pad → [空,2,3,空]
        // 見開き0: 右=空 左=2 / 見開き1: 右=3 左=空
        Assert.Equal(2, plan.Spreads.Count);
        Assert.Null(plan.Spreads[0].RightIndex);
        Assert.Equal(2, plan.Spreads[0].LeftIndex);
        Assert.Equal(3, plan.Spreads[1].RightIndex);
        Assert.Null(plan.Spreads[1].LeftIndex);
        Assert.Equal(2, plan.NonSkipCount);
    }

    [Fact]
    public void 境界_左開きミラー_worked_example()
    {
        // 左開き [A=0, 広告=1 skip, B=2 forceLeft, C=3] → 画面 [左:A|右:空][左:B|右:C]
        // 左開き: S[2k]=左ページ・S[2k+1]=右ページ。画面左パリティ=偶(0)。
        var items = new[]
        {
            It(0),
            It(1, ViewerTagAction.Skip),
            It(2, ViewerTagAction.ForceLeftPage),
            It(3),
        };
        var plan = TagControlLayoutCalculator.Build(items, SpreadDirection.Left, startWithEmptyPage: false);
        // A(無): emit s0(左) / B(forceLeft=偶): 次s1奇→pad空白 s1, emit B s2 / C: s3
        // slots [A, 空, B, C] → 見開き0 (左=A 右=空) 見開き1 (左=B 右=C)
        Assert.Equal(2, plan.Spreads.Count);
        Assert.Equal(0, plan.Spreads[0].LeftIndex);
        Assert.Null(plan.Spreads[0].RightIndex);
        Assert.Equal(2, plan.Spreads[1].LeftIndex);
        Assert.Equal(3, plan.Spreads[1].RightIndex);
    }

    [Fact]
    public void OC25_位置総数_非skip基準()
    {
        // [A=0, 広告=1 skip, B=2, C=3] → 非skip = [0,2,3]、位置 1,2,3
        var items = new[]
        {
            It(0),
            It(1, ViewerTagAction.Skip),
            It(2),
            It(3),
        };
        var plan = TagControlLayoutCalculator.Build(items, SpreadDirection.Right, false);
        Assert.Equal(3, plan.NonSkipCount);
        Assert.Equal(1, plan.NonSkipPosition[0]);
        Assert.Equal(2, plan.NonSkipPosition[2]);
        Assert.Equal(3, plan.NonSkipPosition[3]);
        Assert.False(plan.NonSkipPosition.ContainsKey(1)); // skip は番号なし
    }
}
