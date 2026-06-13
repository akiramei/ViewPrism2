using ViewPrism2.Core.Services.Similarity;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// CP-PHASH-016(unit): pHash 計算とハミング距離・類似度%変換が仕様 §2.10.1-2・OC-14/15 と一致する(決定的)。
/// pHash は性質ベース(決定性・縮退の手計算値・距離関係)。類似度%変換は exact。
/// 入力は 32×32 BGRA バイト列を合成して直接 Core を呼ぶ(SkiaSharp 非依存。M-PHASH-020 purity)。
/// </summary>
[Trait("cp", "CP-PHASH-016")]
public sealed class CpPhash016Tests
{
    private const int Size = PerceptualHash.Size; // 32

    // ---- OC-14: pHash 決定性・縮退・距離関係 ----

    [Fact]
    public void 決定性_同一ピクセル内容を2回計算すると同一16hex()
    {
        var pixels = Gradient();
        var h1 = PerceptualHash.Compute(pixels);
        var h2 = PerceptualHash.Compute(pixels);

        Assert.Equal(h1, h2);
        Assert.Matches("^[0-9a-f]{16}$", h1); // 16 桁小文字 16 進
    }

    [Fact]
    public void 縮退_単色画像は0x8000000000000000()
    {
        // 単色 → 非 DC 係数は全て 0 → 中央値 0・DC 位置のみ bit=1(行優先・DC=最上位)
        var solid = Solid(0x40, 0x80, 0xC0);
        var hash = PerceptualHash.Compute(solid);

        Assert.Equal("8000000000000000", hash);
        Assert.Equal(0x8000000000000000UL, PerceptualHash.ComputeBits(solid));
    }

    [Fact]
    public void 縮退_非黒の別単色でも同じ0x8000000000000000()
    {
        // 単色なら非 DC 係数は 0(縮退)。DC[0,0]>0 のとき最上位ビットのみ 1 → 0x8000000000000000。
        // 注: 純黒(輝度 0)は DC[0,0]=0 のため DC ビットも 0(0x0…0)になる(>判定)— 黒は例外。
        Assert.Equal("8000000000000000", PerceptualHash.Compute(Solid(0xFF, 0xFF, 0xFF)));
        Assert.Equal("8000000000000000", PerceptualHash.Compute(Solid(0x12, 0x34, 0x56)));
        Assert.Equal("8000000000000000", PerceptualHash.Compute(Solid(0x01, 0x01, 0x01)));
    }

    [Fact]
    public void 縮退_純黒は全0_DC係数も0のため()
    {
        // 純黒は輝度 0 → DC[0,0]=0・中央値 0 → 全ビット 0(>判定で DC も立たない)。意図的な境界。
        Assert.Equal("0000000000000000", PerceptualHash.Compute(Solid(0x00, 0x00, 0x00)));
    }

    [Fact]
    public void 距離0_同一ピクセル内容の別バイト列はハミング距離0()
    {
        // 「別ファイル・別 mtime」相当: ピクセル内容が同一なら距離 0(pHash はピクセルのみに依存)
        var a = Gradient();
        var b = Gradient(); // 別インスタンスだが同一内容
        Assert.NotSame(a, b);

        var ha = PerceptualHash.ComputeBits(a);
        var hb = PerceptualHash.ComputeBits(b);
        Assert.Equal(0, HammingDistance.Between(ha, hb));
    }

    [Fact]
    public void 近傍_わずかな輝度シフト版は距離が小さく既定閾値内に入る()
    {
        // 再エンコード相当: 低周波構造(対角グラデーション)を保ったまま全体に小さな輝度シフトを加える。
        // pHash は中央値比較(スケール・オフセットにほぼ不変)のため距離は小さい(代表ケース: 距離 10 以内)。
        var baseImage = DiagonalGradient(0);
        var shifted = DiagonalGradient(6); // 全画素 +6 の輝度シフト(わずかな変化)

        var distance = HammingDistance.Between(
            PerceptualHash.ComputeBits(baseImage), PerceptualHash.ComputeBits(shifted));
        Assert.True(distance <= 10, $"微小変化の距離は閾値 70(=距離 10)以内のはず。実際: {distance}");
    }

    [Fact]
    public void 非近傍_明確に異なる2画像は距離が大きく類似度50未満()
    {
        // 独立な擬似乱数の低周波場(異なるシード)→ 8×8 低周波の符号パターンが無相関で距離が大きい。
        // pHash は多くの変換に頑健なため、明確な非マッチには低周波エネルギー分布が無相関な 2 枚を使う。
        var a = LowFrequencyField(seed: 12345);
        var b = LowFrequencyField(seed: 99999);

        var distance = HammingDistance.Between(
            PerceptualHash.ComputeBits(a), PerceptualHash.ComputeBits(b));
        var score = SimilarityScore.FromDistance(distance);
        Assert.True(score < 50, $"明確に異なる画像は類似度 50 未満のはず。距離={distance} 類似度={score}");
    }

    // ---- ハミング距離 ----

    [Fact]
    public void ハミング距離_全ビット反転は64_同値は0()
    {
        Assert.Equal(64, HammingDistance.Between(0x0000000000000000UL, 0xFFFFFFFFFFFFFFFFUL));
        Assert.Equal(0, HammingDistance.Between(0xDEADBEEFCAFEBABEUL, 0xDEADBEEFCAFEBABEUL));
    }

    [Fact]
    public void ハミング距離_popcount_XOR()
    {
        // 0x0...1 と 0x0...3 の XOR = 0x2 → popcount 1
        Assert.Equal(1, HammingDistance.Between(0x1UL, 0x3UL));
        // 16hex オーバーロード
        Assert.Equal(64, HammingDistance.Between("0000000000000000", "ffffffffffffffff"));
        Assert.Equal(0, HammingDistance.Between("8000000000000000", "8000000000000000"));
    }

    // ---- OC-15: 類似度%変換(exact) ----

    [Theory]
    [InlineData(0, 100)]
    [InlineData(3, 94)]
    [InlineData(5, 90)]
    [InlineData(7, 82)]
    [InlineData(10, 70)]
    [InlineData(12, 62)]
    [InlineData(15, 50)]
    [InlineData(20, 30)]
    [InlineData(25, 10)]
    [InlineData(26, 10)]
    [InlineData(27, 9)]
    [InlineData(40, 6)]
    [InlineData(64, 0)]
    public void 類似度変換_期待値ベクタと一致する(int distance, int expected)
    {
        Assert.Equal(expected, SimilarityScore.FromDistance(distance));
    }

    [Fact]
    public void 類似度変換_全ブレークポイントで端点s値を採る()
    {
        Assert.Equal(100, SimilarityScore.FromDistance(0));
        Assert.Equal(90, SimilarityScore.FromDistance(5));
        Assert.Equal(70, SimilarityScore.FromDistance(10));
        Assert.Equal(50, SimilarityScore.FromDistance(15));
        Assert.Equal(30, SimilarityScore.FromDistance(20));
        Assert.Equal(10, SimilarityScore.FromDistance(25));
        Assert.Equal(0, SimilarityScore.FromDistance(64));
    }

    [Fact]
    public void 類似度変換_単調非増加である()
    {
        var previous = 101;
        for (var d = 0; d <= 64; d++)
        {
            var score = SimilarityScore.FromDistance(d);
            Assert.True(score <= previous, $"距離 {d} で類似度が増加した({previous}→{score})");
            Assert.InRange(score, 0, 100);
            previous = score;
        }
    }

    // ---- 合成 32×32 BGRA フィクスチャ(SkiaSharp 非依存) ----

    private static byte[] Solid(byte b, byte g, byte r)
    {
        var pixels = new byte[Size * Size * 4];
        for (var i = 0; i < Size * Size; i++)
        {
            pixels[(i * 4) + 0] = b;
            pixels[(i * 4) + 1] = g;
            pixels[(i * 4) + 2] = r;
            pixels[(i * 4) + 3] = 0xFF;
        }

        return pixels;
    }

    private static byte[] Gradient()
    {
        var pixels = new byte[Size * Size * 4];
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var v = (byte)((x + y) * 4);
                var o = (((y * Size) + x) * 4);
                pixels[o] = v;
                pixels[o + 1] = v;
                pixels[o + 2] = v;
                pixels[o + 3] = 0xFF;
            }
        }

        return pixels;
    }

    /// <summary>対角グラデーション(低周波構造)。shift で全画素に一様な輝度オフセットを加える。</summary>
    private static byte[] DiagonalGradient(int shift)
    {
        var pixels = new byte[Size * Size * 4];
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var v = (byte)Math.Clamp(((x + y) * 3) + shift, 0, 255);
                var o = (((y * Size) + x) * 4);
                pixels[o] = v;
                pixels[o + 1] = v;
                pixels[o + 2] = v;
                pixels[o + 3] = 0xFF;
            }
        }

        return pixels;
    }

    /// <summary>低周波の擬似乱数場(数本の低周波正弦波をランダム位相・振幅で合成)。seed で内容が決まる。</summary>
    private static byte[] LowFrequencyField(int seed)
    {
        var rng = new Random(seed);
        // 低周波成分(波数 1〜4)の係数を乱数で生成
        var components = new (int Kx, int Ky, double Amp, double Phase)[6];
        for (var i = 0; i < components.Length; i++)
        {
            components[i] = (
                rng.Next(1, 5),
                rng.Next(1, 5),
                rng.NextDouble(),
                rng.NextDouble() * Math.PI * 2);
        }

        var pixels = new byte[Size * Size * 4];
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var sum = 0.0;
                foreach (var (kx, ky, amp, phase) in components)
                {
                    sum += amp * Math.Sin(((Math.PI * kx * x) / Size) + ((Math.PI * ky * y) / Size) + phase);
                }

                var v = (byte)Math.Clamp((int)(128 + (sum * 40)), 0, 255);
                var o = (((y * Size) + x) * 4);
                pixels[o] = v;
                pixels[o + 1] = v;
                pixels[o + 2] = v;
                pixels[o + 3] = 0xFF;
            }
        }

        return pixels;
    }
}
