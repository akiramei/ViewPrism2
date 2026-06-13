namespace ViewPrism2.Core.Services.Viewer;

/// <summary>
/// 見開きのページ送り計算器(OC-10、仕様 §2.9 REQ-057)。純粋計算(M-VIEWERCORE-017)。
/// step は呼び出し側が SHIFT/pageTurnMode から解決する(SHIFT=1 / doublePage=2 / singlePage=1)。
/// 方向反転(右開きの矢印)・クリック半面の解釈は ViewModel 側の責務であり、本計算器は
/// 論理的な「次へ(index 増)/前へ(index 減)」のみを扱う。
/// </summary>
public static class PageTurnCalculator
{
    /// <summary>
    /// 次へ(index 増)。現在+step が末尾(totalCount-1)を超えるなら末尾へクランプ。
    /// 既に末尾なら何もしない(変化なし)。空白ページ開始 ON・index 0 の次へは index 1 へ
    /// (0→1 特殊送り。以後の偶奇を揃える — 仕様 §2.9 REQ-057。FMEA-017)。
    /// </summary>
    public static int Next(int currentIndex, int totalCount, int step, bool startWithEmptyPage)
    {
        if (totalCount <= 0)
        {
            return 0;
        }

        var last = totalCount - 1;
        var current = Math.Clamp(currentIndex, 0, last);

        // 空白ページ開始 ON・index 0 の次へは 1 へ(0→1 特殊送り。step が 1 でも 2 でも同値)
        if (startWithEmptyPage && current == 0)
        {
            return Math.Min(1, last);
        }

        if (current >= last)
        {
            return last; // 既に末尾なら停止(変化なし)
        }

        return Math.Min(current + step, last);
    }

    /// <summary>
    /// 前へ(index 減)。現在-step が 0 未満なら 0 へクランプ。既に先頭なら 0(停止)。
    /// </summary>
    public static int Prev(int currentIndex, int step)
    {
        return Math.Max(0, currentIndex - step);
    }
}
