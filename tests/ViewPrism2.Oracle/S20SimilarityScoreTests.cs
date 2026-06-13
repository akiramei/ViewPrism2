using ViewPrism2.Core.Services.Similarity;
using Xunit;

namespace ViewPrism2.Oracle;

/// <summary>
/// S-20: 距離→類似度%変換(spec §2.10.2・OC-15、EQ-001)。工場非開示。
/// 区分線形+Floor(raw+0.5)+[0,100] クランプ。期待値ベクタは spec §2.10.2 から逐条導出。
/// </summary>
[Trait("oracle", "S-20")]
public sealed class S20SimilarityScoreTests
{
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
    public void 距離から類似度への変換が期待ベクタと一致(int distance, int expected)
        => Assert.Equal(expected, SimilarityScore.FromDistance(distance));

    [Fact]
    public void 単調非増加_距離0から64()
    {
        for (var d = 1; d <= 64; d++)
        {
            Assert.True(
                SimilarityScore.FromDistance(d) <= SimilarityScore.FromDistance(d - 1),
                $"d={d} で類似度が増加した");
        }
    }

    [Fact]
    public void 閾値70は距離10以内に対応()
    {
        Assert.True(SimilarityScore.FromDistance(10) >= 70); // d=10 → 70(≧で類似)
        Assert.True(SimilarityScore.FromDistance(11) < 70);  // d=11 → 66(非類似)
    }
}
