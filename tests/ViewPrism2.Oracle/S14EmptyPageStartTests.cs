using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services.Viewer;
using Xunit;

namespace ViewPrism2.Oracle;

/// <summary>
/// S-14: 空白ページ開始の複合(total=6・startWithEmptyPage=ON。spec §2.9 REQ-056/057、EQ-001)。
/// 右開き: index0=(L=0,R=空白)→次へで index=1(0→1 特殊送り)=(R=1,L=2)→index=3=(R=3,L=4)→index=5=(R=5,L=空白)。
/// 左開き: index0=(R=0,L=空白)→以後 L/R 鏡像。index>0 では特殊配置なし。
/// </summary>
[Trait("oracle", "S-14")]
public sealed class S14EmptyPageStartTests
{
    private const int Total = 6;

    [Fact]
    public void 右開き空白開始は表紙単独から0to1特殊送りで偶奇が揃う()
    {
        SpreadPair P(int i) => SpreadPairCalculator.Calculate(i, Total, SpreadDirection.Right, startWithEmptyPage: true);

        // index 0: 表紙を進行方向側(右開き=左ページ)に単独、右は空白
        Assert.Equal(0, P(0).LeftIndex);
        Assert.Null(P(0).RightIndex);

        // 0→1 特殊送り
        var i1 = PageTurnCalculator.Next(0, Total, step: 2, startWithEmptyPage: true);
        Assert.Equal(1, i1);
        Assert.Equal((2, 1), (P(1).LeftIndex, P(1).RightIndex)); // (R=1,L=2)

        var i3 = PageTurnCalculator.Next(i1, Total, step: 2, startWithEmptyPage: true);
        Assert.Equal(3, i3);
        Assert.Equal((4, 3), (P(3).LeftIndex, P(3).RightIndex)); // (R=3,L=4)

        var i5 = PageTurnCalculator.Next(i3, Total, step: 2, startWithEmptyPage: true);
        Assert.Equal(5, i5);
        Assert.Equal(5, P(5).RightIndex);
        Assert.Null(P(5).LeftIndex);                            // (R=5,L=空白)
    }

    [Fact]
    public void 左開き空白開始はindex0のみ鏡像配置でindex以上は通常()
    {
        SpreadPair P(int i) => SpreadPairCalculator.Calculate(i, Total, SpreadDirection.Left, startWithEmptyPage: true);

        // index 0: 表紙を進行方向側(左開き=右ページ)に単独、左は空白
        Assert.Equal(0, P(0).RightIndex);
        Assert.Null(P(0).LeftIndex);

        // index>0 は特殊配置なし(左=現在・右=現在+1)
        Assert.Equal((1, 2), (P(1).LeftIndex, P(1).RightIndex));
    }
}
