using ViewPrism2.Core.Services.Similarity;
using Xunit;

namespace ViewPrism2.Oracle;

/// <summary>
/// S-19: pHash の横断契約(scope=cross-factory・A/B 共通ゲート)。spec §2.10.1・OC-14・CPOL-103。
/// 「正しい知覚ハッシュ実装ならどの工場でも満たすべき性質」だけを凍結する:
///   - 決定性(同一ピクセル→同一ハッシュ)
///   - 同一内容→距離 0
///   - 近傍=類似分類(score≥70)・遠傍=非類似分類(score&lt;50)・近傍&lt;遠傍(順序)
/// **このビルド固有の係数レベル exact 値(単色=0x8000…)は S-19b へ分離**(CPOL-103: ビット一致不要)。
/// 実 decode パイプライン経由の順位等価は S-25(cross-factory)。
/// </summary>
[Trait("oracle", "S-19")]
[Trait("scope", "cross-factory")]
public sealed class S19PerceptualHashTests
{
    private const int N = 32; // PerceptualHash.Size

    /// <summary>brightness(x,y) を 0〜255 にクランプして 32×32 BGRA(行優先・グレー)を作る。</summary>
    private static byte[] Bgra(Func<int, int, int> brightness)
    {
        var buf = new byte[N * N * 4];
        for (var y = 0; y < N; y++)
        {
            for (var x = 0; x < N; x++)
            {
                var v = (byte)Math.Clamp(brightness(x, y), 0, 255);
                var o = ((y * N) + x) * 4;
                buf[o] = v;       // B
                buf[o + 1] = v;   // G
                buf[o + 2] = v;   // R
                buf[o + 3] = 255; // A
            }
        }

        return buf;
    }

    [Fact]
    public void 決定性_同一入力で同一pHash()
    {
        var a = Bgra((x, _) => x * 8);
        Assert.Equal(PerceptualHash.Compute(a), PerceptualHash.Compute(a));
    }

    [Fact]
    public void 同一内容は距離0_近傍は類似分類_遠傍は非類似分類_近傍が近い()
    {
        // 横断契約: exact 距離値ではなく「順序」と「類似/非類似の分類」を凍結する(A/B 実装で値は変わってよい)。
        var a = Bgra(Rand);
        var aCopy = Bgra(Rand);                              // 同一内容(別パス/別 mtime 相当)
        var near = Bgra((x, y) => Rand(x, y) + 20);          // 加算輝度シフト=微小変化
        var far = Bgra((x, y) =>                              // 構造の全く異なる低周波画像
            (int)(128 + (60 * Math.Cos(2 * Math.PI * x / 32)) + (60 * Math.Cos(2 * Math.PI * y / 32))));

        var ha = PerceptualHash.ComputeBits(a);
        Assert.Equal(0, HammingDistance.Between(ha, PerceptualHash.ComputeBits(aCopy))); // 同一内容=距離 0

        var dNear = HammingDistance.Between(ha, PerceptualHash.ComputeBits(near));
        var dFar = HammingDistance.Between(ha, PerceptualHash.ComputeBits(far));
        Assert.True(dNear < dFar, $"近傍 {dNear} が遠傍 {dFar} 以上(順序破れ)");
        Assert.True(SimilarityScore.FromDistance(dNear) >= 70, $"近傍が類似分類(≥70)に入らない: score={SimilarityScore.FromDistance(dNear)}");
        Assert.True(SimilarityScore.FromDistance(dFar) < 50, $"遠傍が非類似(<50)でない: score={SimilarityScore.FromDistance(dFar)}");
    }

    /// <summary>構造の豊かな決定的擬似乱数パターン(2 値)。</summary>
    private static int Rand(int x, int y) => (((x * 2654435761L) + (y * 40503L)) % 256) > 128 ? 230 : 25;
}
