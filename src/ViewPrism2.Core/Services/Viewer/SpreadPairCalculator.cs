using ViewPrism2.Core.Models;

namespace ViewPrism2.Core.Services.Viewer;

/// <summary>
/// 見開きの左右ページ index(または空白ページ)を求める(OC-9、仕様 §2.9 REQ-056)。
/// 純粋計算(I/O・Avalonia・DB 依存なし。Core 配置 — M-VIEWERCORE-017)。
/// </summary>
public readonly record struct SpreadPair
{
    /// <summary>左ページの index。null=空白ページ(無地・エラー文言なし)。</summary>
    public int? LeftIndex { get; init; }

    /// <summary>右ページの index。null=空白ページ。</summary>
    public int? RightIndex { get; init; }
}

/// <summary>見開きペアリング計算器(OC-9)。</summary>
public static class SpreadPairCalculator
{
    /// <summary>
    /// 現在 index を基準に見開きの (左, 右) を求める。
    /// 右開き(日本式): 右=現在 index・左=現在+1。左開き: 左=現在 index・右=現在+1(仕様 §2.9 REQ-056)。
    /// 相方が末尾(totalCount-1)を超える場合は空白ページ(null)。
    /// 空白ページ開始 ON かつ index=0 では、最初の画像を進行方向側に単独配置し反対側を空白に
    /// (右開き=左ページ / 左開き=右ページ。表紙を単独で見せ以後の偶奇を揃える)。
    /// </summary>
    public static SpreadPair Calculate(int currentIndex, int totalCount, SpreadDirection direction, bool startWithEmptyPage)
    {
        if (totalCount <= 0)
        {
            return new SpreadPair { LeftIndex = null, RightIndex = null };
        }

        var current = Math.Clamp(currentIndex, 0, totalCount - 1);

        // 空白ページ開始 ON・index 0: 表紙を進行方向側に単独表示(特殊配置は index 0 のみ)
        if (startWithEmptyPage && current == 0)
        {
            return direction == SpreadDirection.Right
                ? new SpreadPair { LeftIndex = 0, RightIndex = null }   // 右開き=左ページ(進行方向側)に 1 枚目
                : new SpreadPair { LeftIndex = null, RightIndex = 0 };  // 左開き=右ページ(進行方向側)に 1 枚目
        }

        var partner = current + 1;
        var partnerIndex = partner <= totalCount - 1 ? partner : (int?)null;

        return direction == SpreadDirection.Right
            ? new SpreadPair { LeftIndex = partnerIndex, RightIndex = current }  // 右=現在・左=現在+1
            : new SpreadPair { LeftIndex = current, RightIndex = partnerIndex }; // 左=現在・右=現在+1
    }
}
