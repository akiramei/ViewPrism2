using ViewPrism2.Core.Services.Similarity;
using Xunit;

namespace ViewPrism2.Oracle;

/// <summary>
/// S-19b: このビルドの pHash 係数レベル exact 値(scope=this-build・**A/B 横断ゲートにしない**)。
/// CPOL-103 により原典・他工場とのビット一致は不要。ここで凍結するのは「factory-04 が採用した
/// 当方レシピ(orthonormal DCT-II・DC 除外中央値・行優先・CHEAT-01 量子化)の決定的出力」であり、
/// **このビルドの回帰検査専用**。別実装(factory-B=DecodeToWidth 等)はこの値を満たす必要がない
/// (横断契約は S-19=順序/分類・S-25=順位等価)。
/// </summary>
[Trait("oracle", "S-19b")]
[Trait("scope", "this-build")]
public sealed class S19bThisBuildPHashTests
{
    private const int N = 32;

    [Fact]
    public void 単色画像の縮退_DC位置のみ1で0x8000000000000000_このビルド固有()
    {
        // 全係数が等しい(非 DC=0)→ 中央値 0・c=m=0 で bit=0、DC のみ 1(行優先 MSB)。
        // この exact 16hex は当方レシピ(CHEAT-01 量子化込み)の出力であり this-build 回帰専用。
        var solid = new byte[N * N * 4];
        for (var i = 0; i < solid.Length; i += 4)
        {
            solid[i] = 128; solid[i + 1] = 128; solid[i + 2] = 128; solid[i + 3] = 255;
        }

        Assert.Equal("8000000000000000", PerceptualHash.Compute(solid));
    }
}
