namespace ViewPrism2.Core.Services.Similarity;

/// <summary>
/// ハミング距離 → 類似度%変換(M-PHASH-020 / E-PHASH-031 / OC-15、仕様 §2.10.2)。
/// 距離の単調非増加な区分線形関数。ブレークポイント (距離, 類似度%):
///   (0,100) (5,90) (10,70) (15,50) (20,30) (25,10) (64,0)。
/// 変換式: 区間 [d_lo,d_hi]→[s_lo,s_hi] に対し
///   raw = s_lo + (s_hi - s_lo)·(d - d_lo)/(d_hi - d_lo)、
///   sim = (int)Math.Floor(raw + 0.5)(0.5 切り上げ=AwayFromZero)で整数化し [0,100] にクランプ。
/// 距離値のみの関数で pHash のビット表現には依存しない(INV-012)。
/// </summary>
public static class SimilarityScore
{
    // (距離, 類似度%)のブレークポイント(距離昇順)。
    private static readonly (int Distance, int Score)[] Breakpoints =
    [
        (0, 100),
        (5, 90),
        (10, 70),
        (15, 50),
        (20, 30),
        (25, 10),
        (64, 0),
    ];

    /// <summary>ハミング距離(0〜64)から類似度%(0〜100)を返す(OC-15)。</summary>
    public static int FromDistance(int hamming)
    {
        // 範囲外はクランプ(防御。距離は popcount により 0〜64)
        if (hamming <= Breakpoints[0].Distance)
        {
            return Breakpoints[0].Score;
        }

        var last = Breakpoints[^1];
        if (hamming >= last.Distance)
        {
            return last.Score;
        }

        // 距離が含まれる区間 [d_lo, d_hi] を探す
        for (var i = 0; i < Breakpoints.Length - 1; i++)
        {
            var lo = Breakpoints[i];
            var hi = Breakpoints[i + 1];
            if (hamming < lo.Distance || hamming > hi.Distance)
            {
                continue;
            }

            // ブレークポイント上の距離はその区間の端点 s をそのまま採る
            if (hamming == lo.Distance)
            {
                return lo.Score;
            }

            if (hamming == hi.Distance)
            {
                return hi.Score;
            }

            var raw = lo.Score + ((double)(hi.Score - lo.Score) * (hamming - lo.Distance) / (hi.Distance - lo.Distance));
            var sim = (int)Math.Floor(raw + 0.5);
            return Math.Clamp(sim, 0, 100);
        }

        return last.Score; // 到達しない(防御)
    }
}
