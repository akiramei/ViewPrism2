using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services.Viewer;
using Xunit;

namespace ViewPrism2.Oracle;

/// <summary>
/// S-35: seed 抑止・両面空白不在・canonical/画像対応(spec §2.12.2 不変条件・§2.12.4、EQ-001)。
/// 設計者受入=工場非開示の独立導出。G2' で潰した blocker(空白開始 ON × 先頭配置アクションで両面空白の
/// 見開き→canonical 未定義)が再発しないことを凍結する。両面空白の見開きは構築上一切生じない。
/// </summary>
[Trait("oracle", "S-35")]
public sealed class S35TagSeedSuppressionTests
{
    private static readonly ViewerTagAction[] PlacementActions =
    {
        ViewerTagAction.ForceLeftPage,
        ViewerTagAction.ForceRightPage,
        ViewerTagAction.LeftPageEmpty,
        ViewerTagAction.RightPageEmpty,
        ViewerTagAction.Spread,
    };

    [Fact]
    public void 空白開始ON_先頭が配置アクションでも両面空白の見開きを生成しない()
    {
        foreach (var dir in new[] { SpreadDirection.Right, SpreadDirection.Left })
        {
            foreach (var first in PlacementActions)
            {
                var items = new (int, ViewerTagAction?)[]
                {
                    (0, first), (1, null), (2, null), (3, null),
                };
                var plan = TagControlLayoutCalculator.Build(items, dir, startWithEmptyPage: true);

                foreach (var s in plan.Spreads)
                {
                    // 両面空白(両スロット空白で spread 占有でもない)は禁止
                    var bothBlank = s.LeftIndex is null && s.RightIndex is null && !s.IsSpread;
                    Assert.False(bothBlank, $"両面空白の見開きが生じた: dir={dir} first={first}");

                    // canonical は常に有効な画像 index(>=0)
                    Assert.True(s.CanonicalImage >= 0, $"canonical 未定義: dir={dir} first={first}");
                }
            }
        }
    }

    [Fact]
    public void 空白開始ON_先頭forceRightPageは右開きでseed抑止し単一見開きに収まる()
    {
        // [A=forceRightPage, B] 右開き・空白開始ON → seed 抑止 → スロット[A,B] → [右:A(0)|左:B(1)]
        var items = new (int, ViewerTagAction?)[] { (0, ViewerTagAction.ForceRightPage), (1, null) };
        var plan = TagControlLayoutCalculator.Build(items, SpreadDirection.Right, startWithEmptyPage: true);

        Assert.Single(plan.Spreads);
        Assert.Equal((1, 0), (plan.Spreads[0].LeftIndex, plan.Spreads[0].RightIndex));
        Assert.Equal(0, plan.Spreads[0].CanonicalImage);
    }

    [Fact]
    public void 各非skip画像はちょうど1つの見開きに属する()
    {
        // skip 混在の一般列で ImageToSpread の一意性を確認
        var items = new (int, ViewerTagAction?)[]
        {
            (0, null),
            (1, ViewerTagAction.Skip),
            (2, ViewerTagAction.ForceRightPage),
            (3, null),
            (4, ViewerTagAction.Spread),
            (5, null),
        };
        var plan = TagControlLayoutCalculator.Build(items, SpreadDirection.Right, startWithEmptyPage: false);

        // skip した 1 は対応に含まれない。非 skip(0,2,3,4,5)はすべて含まれ、見開き index は範囲内
        Assert.False(plan.ImageToSpread.ContainsKey(1));
        foreach (var img in new[] { 0, 2, 3, 4, 5 })
        {
            Assert.True(plan.ImageToSpread.ContainsKey(img), $"画像 {img} が見開きに対応しない");
            var sp = plan.ImageToSpread[img];
            Assert.InRange(sp, 0, plan.Spreads.Count - 1);
        }

        Assert.Equal(5, plan.NonSkipCount); // 非 skip は 5 枚
    }
}
