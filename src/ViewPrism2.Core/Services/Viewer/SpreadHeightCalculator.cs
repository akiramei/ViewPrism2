using ViewPrism2.Core.Models;

namespace ViewPrism2.Core.Services.Viewer;

/// <summary>
/// 画像の自然寸法(SkiaSharp 非依存。Core は BCL のみ — ADR-0002)。
/// </summary>
public readonly record struct ImageSize(int Width, int Height);

/// <summary>
/// 見開き高さ統一計算器(OC-12、仕様 §2.9 REQ-058)。純粋計算(M-VIEWERCORE-017)。
/// match 系のみ統一高さを返す: ペアの高い方(matchLargerHeight)/低い方(matchSmallerHeight)へ
/// 両者を統一する。片側が空白ページ(null)の場合は有効画像 1 枚の自然高さを統一高さとする
/// (match 系のみの規則)。統一高さは表示領域高さの 90% を上限とする。
/// noResize は統一高さ概念を持たず null を返す(片側空白でも同じ。表示領域超過時に領域内へ収めるのは
/// UI 側の責務で 100% 上限 — match 系 90% との非対称は原典準拠の意図的差)。
/// アスペクト比は常に維持(高さを決めれば幅は比例 — 描画側が算出)。
/// </summary>
public static class SpreadHeightCalculator
{
    /// <summary>
    /// 統一表示高さを返す。match 系のみ値を返し、noResize は null(=統一なし)。
    /// 両側 null(画像なし)も null。
    /// </summary>
    public static double? Calculate(ImageSize? left, ImageSize? right, ResizeMode mode, double viewportHeight)
    {
        if (mode == ResizeMode.NoResize)
        {
            return null; // 統一なし(match 系のみの規則 — 片側空白でも同じ)
        }

        var leftHeight = left?.Height;
        var rightHeight = right?.Height;

        // 両側空白(画像なし)→ 統一対象なし
        if (leftHeight is null && rightHeight is null)
        {
            return null;
        }

        double unified;
        if (leftHeight is { } lh && rightHeight is { } rh)
        {
            unified = mode == ResizeMode.MatchLargerHeight ? Math.Max(lh, rh) : Math.Min(lh, rh);
        }
        else
        {
            // 片側空白: 有効画像 1 枚の自然高さを統一高さとする
            unified = (leftHeight ?? rightHeight)!.Value;
        }

        // 表示領域高さの 90% を上限(match 系のみ)
        var cap = viewportHeight * 0.9;
        return Math.Min(unified, cap);
    }
}
