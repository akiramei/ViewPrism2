using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services.Viewer;
using Xunit;

namespace ViewPrism2.Oracle;

/// <summary>
/// S-33: タグ制御ページプラン構築の worked example(OC-24・spec §2.12.2-3、EQ-001)。
/// 設計者受入=工場非開示の独立導出。雑誌広告除外・先頭アンカー・漫画見開き・spread×pad の代表例を
/// 期待見開き列(右/左ページ index・spread 占有)で exact 固定する。
/// </summary>
[Trait("oracle", "S-33")]
public sealed class S33TagLayoutTests
{
    private static (int idx, ViewerTagAction? act) I(int idx, ViewerTagAction? act = null) => (idx, act);

    // 見開きを (左, 右, spread?) に正規化(null=空白)。
    private static (int? L, int? R, bool S) Spr(TagControlSpread s) => (s.LeftIndex, s.RightIndex, s.IsSpread);

    [Fact]
    public void 右開き_広告skip_keeperをforceRightPageで右に固定()
    {
        // idx[0:A,1:広告,2:B,3:C] 広告=skip, B=forceRightPage
        var items = new[] { I(0), I(1, ViewerTagAction.Skip), I(2, ViewerTagAction.ForceRightPage), I(3) };
        var plan = TagControlLayoutCalculator.Build(items, SpreadDirection.Right, startWithEmptyPage: false);

        // [右:A(0)|左:空], [右:B(2)|左:C(3)]
        Assert.Equal(2, plan.Spreads.Count);
        Assert.Equal((null, 0, false), Spr(plan.Spreads[0]));
        Assert.Equal((3, 2, false), Spr(plan.Spreads[1]));
        Assert.Equal(0, plan.Spreads[0].CanonicalImage);
        Assert.Equal(2, plan.Spreads[1].CanonicalImage);
    }

    [Fact]
    public void 右開き_広告skip_facingをleftPageEmptyで空白に()
    {
        // idx[0:A,1:広告,2:B,3:C] 広告=skip, A=leftPageEmpty → 同じ結果に収束
        var items = new[] { I(0, ViewerTagAction.LeftPageEmpty), I(1, ViewerTagAction.Skip), I(2), I(3) };
        var plan = TagControlLayoutCalculator.Build(items, SpreadDirection.Right, startWithEmptyPage: false);

        Assert.Equal(2, plan.Spreads.Count);
        Assert.Equal((null, 0, false), Spr(plan.Spreads[0]));
        Assert.Equal((3, 2, false), Spr(plan.Spreads[1]));
    }

    [Fact]
    public void 右開き_先頭広告skip_forceLeftPageで開始アンカー_以後無タグで流れる()
    {
        // idx[0:広告,1:A,2:B,3:C] 広告=skip, A=forceLeftPage
        var items = new[] { I(0, ViewerTagAction.Skip), I(1, ViewerTagAction.ForceLeftPage), I(2), I(3) };
        var plan = TagControlLayoutCalculator.Build(items, SpreadDirection.Right, startWithEmptyPage: false);

        // [右:空|左:A(1)], [右:B(2)|左:C(3)]
        Assert.Equal(2, plan.Spreads.Count);
        Assert.Equal((1, null, false), Spr(plan.Spreads[0]));
        Assert.Equal((3, 2, false), Spr(plan.Spreads[1]));
        Assert.Equal(1, plan.Spreads[0].CanonicalImage);
    }

    [Fact]
    public void 右開き_漫画見開き_空白開始ONで左右2枚が同一見開きに揃う()
    {
        // idx[0:表紙,1:右半,2:左半,3:p] 空白開始ON 無アクション
        var items = new[] { I(0), I(1), I(2), I(3) };
        var plan = TagControlLayoutCalculator.Build(items, SpreadDirection.Right, startWithEmptyPage: true);

        // [右:空|左:表紙(0)], [右:右半(1)|左:左半(2)], [右:p(3)|左:空]
        Assert.Equal(3, plan.Spreads.Count);
        Assert.Equal((0, null, false), Spr(plan.Spreads[0]));
        Assert.Equal((2, 1, false), Spr(plan.Spreads[1])); // 右半=1 が右・左半=2 が左
        Assert.Equal((null, 3, false), Spr(plan.Spreads[2]));
    }

    [Fact]
    public void 右開き_spread占有はpadToParityで単独見開きを占める()
    {
        // idx[0:X,1:Y] Y=spread 空白開始OFF
        var items = new[] { I(0), I(1, ViewerTagAction.Spread) };
        var plan = TagControlLayoutCalculator.Build(items, SpreadDirection.Right, startWithEmptyPage: false);

        // [右:X(0)|左:空], [Y(1) 見開き占有]
        Assert.Equal(2, plan.Spreads.Count);
        Assert.Equal((null, 0, false), Spr(plan.Spreads[0]));
        Assert.True(plan.Spreads[1].IsSpread);
        Assert.Equal(1, plan.Spreads[1].LeftIndex);
        Assert.Equal(1, plan.Spreads[1].RightIndex);
        Assert.Equal(1, plan.Spreads[1].CanonicalImage);
    }
}
