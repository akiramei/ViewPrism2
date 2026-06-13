using ViewPrism2.Core.Services.Viewer;
using Xunit;

namespace ViewPrism2.Oracle;

/// <summary>
/// S-16: スクロール中央追跡の境界(spec §2.9 REQ-055、OC-11、EQ-001)。
/// 等高画像 3 枚(gap 0)で 2 枚が表示中心から完全等距離になるオフセット+先頭・最下端。
/// 等距離 → 若い index。先頭(offset=0)→ index 0 / 最下端 → 最終 index。
/// </summary>
[Trait("oracle", "S-16")]
public sealed class S16ScrollTrackTests
{
    // 等高 100px ×3、ギャップ 0。中心は 50 / 150 / 250。
    private static readonly (double Top, double Height)[] Rects =
    [
        (0.0, 100.0),
        (100.0, 100.0),
        (200.0, 100.0),
    ];

    private const double ViewportHeight = 100.0;

    [Fact]
    public void 同距離は若いindexを現在位置とする()
    {
        // scrollOffset=50 → 表示中心=100。画像中心 50/150/250 への距離= 50/50/150 → 0 と 1 が等距離 → 若い 0
        Assert.Equal(0, ScrollPositionTracker.FindCurrent(Rects, ViewportHeight, scrollOffset: 50.0));
    }

    [Fact]
    public void 先頭は先頭index最下端は最終index()
    {
        // 先頭(offset=0)→ 表示中心=50 → 画像0 中心に一致 → 0
        Assert.Equal(0, ScrollPositionTracker.FindCurrent(Rects, ViewportHeight, scrollOffset: 0.0));

        // 最下端(content=300・viewport=100 → max offset=200)→ 表示中心=250 → 画像2 中心に一致 → 2
        Assert.Equal(2, ScrollPositionTracker.FindCurrent(Rects, ViewportHeight, scrollOffset: 200.0));
    }
}
