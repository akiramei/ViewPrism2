using ViewPrism2.Core.Services.Similarity;
using Xunit;

namespace ViewPrism2.Oracle;

/// <summary>
/// S-19: pHash 決定性と距離関係(spec §2.10.1・OC-14・ADR-0008・K-PHASH、EQ-001)。
/// 工場非開示の凍結シナリオ。pHash の 16hex は性質ベース(決定性・縮退の手計算値・距離関係)で凍結し、
/// 係数レベルの exact 値は ADR pin(本書外)に委ねる(CPOL-103 adapter)。
/// </summary>
[Trait("oracle", "S-19")]
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
    public void 単色画像の縮退_DC位置のみ1で0x8000000000000000()
    {
        // 全係数が等しい(非 DC=0)→ 中央値 0・c=m=0 で bit=0、DC のみ 1(行優先 MSB)
        var solid = Bgra((_, __) => 128);
        Assert.Equal("8000000000000000", PerceptualHash.Compute(solid));
    }

    [Fact]
    public void 同一内容は距離0_加算輝度シフトに頑健_構造が異なれば距離大()
    {
        // 構造の豊かな基準(rand)。加算定数の輝度シフトは AC 係数不変=pHash 不変(DC のみ変化・
        // 中央値は DC 除外)で距離 0 が数学的に保証される=「微小変化に頑健」。構造の異なる画像は距離大。
        var a = Bgra(Rand);
        var aCopy = Bgra(Rand);                              // 同一内容(別パス/別 mtime 相当)
        var near = Bgra((x, y) => Rand(x, y) + 20);          // 加算輝度シフト(クランプなし)→ 距離 0
        var far = Bgra((x, y) =>                              // 構造の全く異なる低周波画像
            (int)(128 + (60 * Math.Cos(2 * Math.PI * x / 32)) + (60 * Math.Cos(2 * Math.PI * y / 32))));

        var ha = PerceptualHash.ComputeBits(a);
        Assert.Equal(0, HammingDistance.Between(ha, PerceptualHash.ComputeBits(aCopy))); // 同一内容=距離 0

        var dNear = HammingDistance.Between(ha, PerceptualHash.ComputeBits(near));
        var dFar = HammingDistance.Between(ha, PerceptualHash.ComputeBits(far));
        Assert.True(dNear <= 10, $"近傍距離 {dNear} が 10 超");
        Assert.True(dNear < dFar, $"近傍 {dNear} が遠傍 {dFar} 以上");
        Assert.True(dFar > 25, $"遠傍距離 {dFar} が 25 以下");
    }

    /// <summary>構造の豊かな決定的擬似乱数パターン(2 値)。</summary>
    private static int Rand(int x, int y) => (((x * 2654435761L) + (y * 40503L)) % 256) > 128 ? 230 : 25;
}
