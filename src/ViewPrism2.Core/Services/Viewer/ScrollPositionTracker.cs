namespace ViewPrism2.Core.Services.Viewer;

/// <summary>
/// スクロール現在位置追跡器(OC-11、仕様 §2.9 REQ-055)。純粋計算(M-VIEWERCORE-017)。
/// 各画像の表示矩形(縦位置 top・高さ height)列と表示領域(高さ・スクロール量)から、
/// 表示領域の垂直中央に最も近い画像(画像矩形中心と表示領域中心の距離最小)を現在位置とする。
/// 同距離の場合は一覧 index の小さい方(タイブレーク — 仕様 §2.9 / OC-11 M-2)。
/// </summary>
public static class ScrollPositionTracker
{
    /// <summary>
    /// 中央最近傍の image index を返す。imageRects 空なら 0。
    /// </summary>
    public static int FindCurrent(
        IReadOnlyList<(double Top, double Height)> imageRects, double viewportHeight, double scrollOffset)
    {
        ArgumentNullException.ThrowIfNull(imageRects);
        if (imageRects.Count == 0)
        {
            return 0;
        }

        var viewportCenter = scrollOffset + viewportHeight / 2.0;

        var bestIndex = 0;
        var bestDistance = double.PositiveInfinity;
        for (var i = 0; i < imageRects.Count; i++)
        {
            var (top, height) = imageRects[i];
            var center = top + height / 2.0;
            var distance = Math.Abs(center - viewportCenter);

            // 距離最小・同距離は若い index(strict less-than でタイブレークを若い index に固定)
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = i;
            }
        }

        return bestIndex;
    }
}
