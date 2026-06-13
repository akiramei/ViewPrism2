using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services.Viewer;
using Xunit;

namespace ViewPrism2.Oracle;

/// <summary>
/// S-13: 見開き読書 E2E(右開き・奇数枚数 total=7。spec §2.9 REQ-056/057、EQ-001)。
/// index=0 から doublePage(step=2)で末尾まで送り、SHIFT で 1 戻す。
/// ペア遷移: (R=0,L=1)→(R=2,L=3)→(R=4,L=5)→(R=6,L=空白)。末尾でさらに次へ→変化なし(停止)。
/// SHIFT 前へ 1 → index=5 で (R=5,L=6)(偶奇ずれの維持・再アラインなし)。
/// </summary>
[Trait("oracle", "S-13")]
public sealed class S13SpreadReadingTests
{
    private const int Total = 7;

    private static SpreadPair Pair(int index) =>
        SpreadPairCalculator.Calculate(index, Total, SpreadDirection.Right, startWithEmptyPage: false);

    [Fact]
    public void 右開き奇数枚のペア遷移と末尾停止とSHIFT戻しの偶奇維持()
    {
        // 送りで通過する index 列(doublePage=2)
        var index = 0;
        var visited = new List<int> { index };
        for (var i = 0; i < 5; i++) // 末尾到達後も数回送って停止を確認
        {
            index = PageTurnCalculator.Next(index, Total, step: 2, startWithEmptyPage: false);
            visited.Add(index);
        }
        // 0→2→4→6→6→6→6(末尾 6 で停止・変化なし)
        Assert.Equal(new[] { 0, 2, 4, 6, 6, 6 }, visited);

        // 各停止位置のペア
        Assert.Equal((1, 0), (Pair(0).LeftIndex, Pair(0).RightIndex));   // (R=0,L=1)
        Assert.Equal((3, 2), (Pair(2).LeftIndex, Pair(2).RightIndex));   // (R=2,L=3)
        Assert.Equal((5, 4), (Pair(4).LeftIndex, Pair(4).RightIndex));   // (R=4,L=5)

        var last = Pair(6);
        Assert.Equal(6, last.RightIndex);
        Assert.Null(last.LeftIndex);                                     // (R=6,L=空白)

        // SHIFT 前へ 1 → index=5。ペアは (R=5,L=6)(偶奇ずれの維持)
        var back = PageTurnCalculator.Prev(currentIndex: 6, step: 1);
        Assert.Equal(5, back);
        var shifted = Pair(5);
        Assert.Equal(5, shifted.RightIndex);
        Assert.Equal(6, shifted.LeftIndex);
    }
}
