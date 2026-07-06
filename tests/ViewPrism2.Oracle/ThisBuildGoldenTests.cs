using SkiaSharp;
using ViewPrism2.Core.Services.Similarity;
using ViewPrism2.Infrastructure.Imaging;
using Xunit;

namespace ViewPrism2.Oracle;

/// <summary>
/// このビルドの production pHash adapter golden(scope=this-build・P-09)。
/// 横断契約(EQ-RANK=S-19/S-25)とは別の層: **現 production adapter(scaled-decode v2)の決定的出力**を凍結し、
/// decode 経路/レシピ/SkiaSharp 版が pHash 値を動かす変更を回帰として検出する。
/// adapter 世代交代のたびに再凍結する(CPOL-103: 他工場とのビット一致は不要・このビルド回帰専用)。
/// </summary>
[Trait("oracle", "this-build-golden")]
[Trait("scope", "this-build")]
public sealed class ThisBuildGoldenTests
{
    /// <summary>production に DI される adapter の世代識別子。差し替え時はこの値と golden を再凍結する。</summary>
    // ECO-054 再凍結(台帳ライセンス=CP-PHASH-ADAPTER-019「世代交代ごとに再凍結」): v1→v2(経路一貫性=
    // 全フォーマット一様の中間縮小段)。StructuredGolden 値は capture 実測で不変(512² fixture は codec 縮小で
    // ちょうど長辺 64 に到達し中間段が非発動)— 変わったのは本定数のみ。
    private const string ProductionAdapterId = "skia-scaled-decode-v2";

    /// <summary>
    /// 固定フィクスチャ(512×512 JPEG q90・pattern 3)を production adapter で decode した pHash の凍結値。
    /// SkiaSharp 3.119.4 / scaled-decode v2 の決定的出力。版・経路変更時は再凍結。
    /// </summary>
    private const string StructuredGolden = "b7ff8800d0de12d5";

    [Fact]
    public void production_adapter_id_は_scaled_decode_v2()
    {
        // production が想定どおりの adapter 世代であることを固定(差し替え検出)。
        Assert.Equal(ProductionAdapterId, new PHashImageReaderScaledDecode().AdapterId);
    }

    [Fact]
    public async Task 単色画像は_production_adapter_の実decode経路でも0x8000000000000000()
    {
        // S-19b(core)の縮退を production の実 decode パイプライン経由で再確認(adapter 再凍結の一部)。
        // 単色は scale 不変=早期縮小でも単色のまま → DC のみ 1。
        var dir = Path.Combine(Path.GetTempPath(), "ViewPrism2.golden", Guid.NewGuid().ToString("D"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "solid.png");
            OracleImages.WriteEncoded(path, 256, 256, SKEncodedImageFormat.Png, new SKColor(128, 128, 128));
            var hash = await new PHashImageReaderScaledDecode().ComputePHashAsync(path);
            Assert.Equal("8000000000000000", hash);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task 構造画像の_production_adapter_golden値を凍結する()
    {
        // 固定フィクスチャに対する production adapter の実 decode 出力を凍結(this-build 回帰)。
        var dir = Path.Combine(Path.GetTempPath(), "ViewPrism2.golden", Guid.NewGuid().ToString("D"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "fixture.jpg");
            OracleImages.WriteStructured(path, 512, SKEncodedImageFormat.Jpeg, quality: 90, patternIndex: 3, brightnessShift: 0);
            var hash = await new PHashImageReaderScaledDecode().ComputePHashAsync(path);

            // capture(初回凍結用): 実値を temp へ書き出す。凍結後は Assert.Equal が回帰を守る。
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "viewprism2-golden-capture.txt"), hash ?? "null");

            Assert.Equal(StructuredGolden, hash);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }
}
