using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services.Viewer;
using Xunit;

namespace ViewPrism2.Oracle;

/// <summary>
/// S-15: モード別位置記憶の操作列(spec §2.9 REQ-054、OC-13、EQ-001)。
/// 起動 index=5 → scroll で 20 へ → spread-right へ切替(=初期値 5)→ 2 回送る(step2)=9 →
/// normal へ切替(=初期値 5 のまま)→ scroll へ戻る(=20)。各モードの記憶が独立(共通 index 引き継ぎは違反)。
/// </summary>
[Trait("oracle", "S-15")]
public sealed class S15ModeMemoryTests
{
    private const int Total = 30; // scroll 20・送り 5→7→9 が収まる総数

    [Fact]
    public void モード別に記憶が独立し未訪問モードは起動indexから始まる()
    {
        var memory = new ViewerModeMemory(initialIndex: 5);

        // 全モードの初期値は起動 index
        Assert.Equal(5, memory.Get(ViewerMode.Normal));
        Assert.Equal(5, memory.Get(ViewerMode.Scroll));
        Assert.Equal(5, memory.Get(ViewerMode.SpreadRight));
        Assert.Equal(5, memory.Get(ViewerMode.SpreadLeft));

        // scroll で 20 へ移動
        memory.Set(ViewerMode.Scroll, 20);

        // spread-right へ切替 → 初期値 5(scroll の 20 を引き継がない)
        Assert.Equal(5, memory.Get(ViewerMode.SpreadRight));

        // spread-right で 2 回送る(doublePage step=2): 5→7→9
        var i = memory.Get(ViewerMode.SpreadRight);
        i = PageTurnCalculator.Next(i, Total, step: 2, startWithEmptyPage: false);
        memory.Set(ViewerMode.SpreadRight, i);
        i = PageTurnCalculator.Next(i, Total, step: 2, startWithEmptyPage: false);
        memory.Set(ViewerMode.SpreadRight, i);
        Assert.Equal(9, memory.Get(ViewerMode.SpreadRight));

        // normal へ切替 → 初期値 5 のまま(他モードの移動の影響を受けない)
        Assert.Equal(5, memory.Get(ViewerMode.Normal));

        // scroll へ戻る → 20(scroll の記憶)
        Assert.Equal(20, memory.Get(ViewerMode.Scroll));
    }
}
