using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services.Viewer;
using Xunit;

namespace ViewPrism2.Oracle;

/// <summary>
/// S-36: タグ制御ナビゲーション・位置/総数(OC-25・spec §2.12.4、EQ-001)。設計者受入=工場非開示の独立導出。
/// プラン見開き単位の送り(±1 クランプ)・モード復元の画像→見開き解決・canonical 現在画像・非 skip 位置/総数。
/// </summary>
[Trait("oracle", "S-36")]
public sealed class S36TagNavigationTests
{
    // S-33(1) と同じプラン: [右:A(0)|左:空], [右:B(2)|左:C(3)](広告 idx1=skip)
    private static TagControlPlan Plan() => TagControlLayoutCalculator.Build(
        new (int, ViewerTagAction?)[]
        {
            (0, null), (1, ViewerTagAction.Skip), (2, ViewerTagAction.ForceRightPage), (3, null),
        },
        SpreadDirection.Right,
        startWithEmptyPage: false);

    [Fact]
    public void 送りはプラン見開き単位で端クランプ()
    {
        var plan = Plan();
        var n = plan.Spreads.Count; // 2
        Assert.Equal(2, n);

        Assert.Equal(1, TagControlNavigator.Next(0, n));     // 0 → 1
        Assert.Equal(1, TagControlNavigator.Next(1, n));     // 末尾 → 変化なし(クランプ)
        Assert.Equal(0, TagControlNavigator.Prev(1, n));     // 1 → 0
        Assert.Equal(0, TagControlNavigator.Prev(0, n));     // 先頭 → 変化なし(クランプ)
    }

    [Fact]
    public void モード復元は画像indexを含む見開きへ解決()
    {
        var plan = Plan();
        Assert.Equal(0, TagControlNavigator.SpreadOfImage(plan, 0)); // A → P0
        Assert.Equal(1, TagControlNavigator.SpreadOfImage(plan, 2)); // B → P1
        Assert.Equal(1, TagControlNavigator.SpreadOfImage(plan, 3)); // C → P1
    }

    [Fact]
    public void canonical現在画像は先読み面優先で一意()
    {
        var plan = Plan();
        Assert.Equal(0, TagControlNavigator.CanonicalImage(plan, 0)); // P0 先読み=A(0)
        Assert.Equal(2, TagControlNavigator.CanonicalImage(plan, 1)); // P1 先読み=B(2)
    }

    [Fact]
    public void 位置総数はskip除外_非skip列の1起点位置()
    {
        var plan = Plan();
        Assert.Equal(3, plan.NonSkipCount); // A,B,C(広告は除外)

        // 非 skip 位置: A=1, B=2, C=3(広告は対応なし)
        Assert.Equal(1, plan.NonSkipPosition[0]);
        Assert.Equal(2, plan.NonSkipPosition[2]);
        Assert.Equal(3, plan.NonSkipPosition[3]);
        Assert.False(plan.NonSkipPosition.ContainsKey(1)); // 広告 skip は位置を持たない

        // P0=片側空白→単独表示「1 / 3」/ P1=「2-3 / 3」(読み順=先読み面 B 先)を位置から構成可能
        // (表示文字列の生成は surface=ViewerViewModel 側。ここでは位置の素材が正しいことを検査)
    }

    [Fact]
    public void 空プランの送りとcanonicalは安全()
    {
        // 全 skip → プラン空
        var empty = TagControlLayoutCalculator.Build(
            new (int, ViewerTagAction?)[] { (0, ViewerTagAction.Skip), (1, ViewerTagAction.Skip) },
            SpreadDirection.Right, startWithEmptyPage: false);
        Assert.Empty(empty.Spreads);
        Assert.Equal(0, empty.NonSkipCount);
        Assert.Equal(0, TagControlNavigator.Next(0, empty.Spreads.Count)); // 例外なし
        Assert.Equal(-1, TagControlNavigator.CanonicalImage(empty, 0));    // 現在画像なし
    }
}
