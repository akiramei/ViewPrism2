using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services.Viewer;
using Xunit;

namespace ViewPrism2.Oracle;

/// <summary>
/// S-34: OFF-identity 回帰アンカー(OC-24・spec §2.12.2、EQ-001)。設計者受入=工場非開示の独立導出。
/// 全画像アクション無しのとき、タグ制御ページプランの各見開きはフェーズ1 の SpreadPairCalculator(OC-9)と
/// 全位置で一致する(右開き/左開き × 空白ページ開始 ON/OFF)。enableTagControl OFF 経路の回帰保証点。
/// </summary>
[Trait("oracle", "S-34")]
public sealed class S34TagOffIdentityTests
{
    [Theory]
    [InlineData(SpreadDirection.Right, false)]
    [InlineData(SpreadDirection.Right, true)]
    [InlineData(SpreadDirection.Left, false)]
    [InlineData(SpreadDirection.Left, true)]
    public void アクション無しプランはSpreadPairCalculatorと全位置一致(SpreadDirection dir, bool emptyStart)
    {
        for (var total = 1; total <= 6; total++)
        {
            var items = new (int, ViewerTagAction?)[total];
            for (var i = 0; i < total; i++)
            {
                items[i] = (i, null);
            }

            var plan = TagControlLayoutCalculator.Build(items, dir, emptyStart);

            foreach (var spread in plan.Spreads)
            {
                // 各見開きの canonical 現在画像を SpreadPairCalculator のアンカー index として照合
                var expected = SpreadPairCalculator.Calculate(spread.CanonicalImage, total, dir, emptyStart);
                Assert.Equal(expected.LeftIndex, spread.LeftIndex);
                Assert.Equal(expected.RightIndex, spread.RightIndex);
                Assert.False(spread.IsSpread); // アクション無しに spread 占有は生じない
            }

            // 非 skip 総数は total と一致(skip が無いため)
            Assert.Equal(total, plan.NonSkipCount);
        }
    }
}
