using SkiaSharp;
using ViewPrism2.Core.Services.Similarity;
using ViewPrism2.Infrastructure.Imaging;
using Xunit;

namespace ViewPrism2.Oracle;

/// <summary>
/// S-42: scaled-decode のフォーマット間経路一貫性(REQ-061 系・ECO-054、EQ-002)。工場非開示。
/// 同一内容の PNG(原寸)× JPEG(50% 縮小)複製ペアについて、production(scaled-decode)経路の
/// フォーマット間距離が full-decode 経路の距離+2 を超えない(経路非対称の系統誤差を作らない)こと、
/// および滑らかな内容では絶対距離も小さいことを凍結する。
/// 是正前実績: 高周波内容で scaled=30 vs full=24(実ユーザーペアでは scaled=8 vs full=2 = -18 点)。
/// </summary>
[Trait("oracle", "S-42")]
public sealed class S42FormatConsistencyTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "ViewPrism2.Oracle", "s42-" + Guid.NewGuid().ToString("D"));

    public S42FormatConsistencyTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public async Task 高周波内容でもscaledのフォーマット間距離はfullを悪化させない()
    {
        var (png, jpg) = WritePair(2388, 1668, busy: true);
        var dScaled = await DistanceAsync(new PHashImageReaderScaledDecode(), png, jpg);
        var dFull = await DistanceAsync(new PHashImageReader(), png, jpg);
        Assert.True(dScaled <= dFull + 2,
            $"scaled のフォーマット間距離 {dScaled} が full {dFull}+2 を超過(経路一貫性の破れ)");
    }

    [Fact]
    public async Task 滑らかな内容の複製ペアはscaledでも小距離()
    {
        var (png, jpg) = WritePair(1194, 834, busy: false);
        var d = await DistanceAsync(new PHashImageReaderScaledDecode(), png, jpg);
        Assert.True(d <= 5, $"scaled の png×jpg 距離が {d}(期待 ≤5 = スコア 90 以上)");
    }

    // ---- ヘルパ(オラクル側フィクスチャ — 決定的生成) ----

    private static async Task<int> DistanceAsync(IPHashImageReader reader, string a, string b)
    {
        var ha = await reader.ComputePHashAsync(a);
        var hb = await reader.ComputePHashAsync(b);
        Assert.NotNull(ha);
        Assert.NotNull(hb);
        return HammingDistance.Between(ha, hb);
    }

    /// <summary>同一内容の PNG(原寸)と JPEG q92(50% 縮小)。busy=高密度マルチスケール テクスチャ。</summary>
    private (string Png, string Jpg) WritePair(int width, int height, bool busy)
    {
        using var bmp = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                double v = busy
                    ? 128
                        + (38 * Math.Sin(x * 0.31) * Math.Cos(y * 0.27))
                        + (26 * Math.Sin((x * 0.071) + (y * 0.043)))
                        + (18 * Math.Sin((x + (2 * y)) * 0.013))
                        + ((((x * 7919) + (y * 104729)) % 41 - 20) * 0.9)
                    : (x * 255.0 / width) + (y * 96.0 / height)
                        + (x < width / 4 && y < height / 8 ? 160 : 0);
                var b = (byte)Math.Clamp(v, 0, 255);
                bmp.SetPixel(x, y, new SKColor(b, b, b));
            }
        }

        var png = Path.Combine(_directory, "pair.png");
        using (var image = SKImage.FromBitmap(bmp))
        using (var pd = image.Encode(SKEncodedImageFormat.Png, 100))
        using (var fs = File.Create(png))
        {
            pd.SaveTo(fs);
        }

        using var half = bmp.Resize(
            new SKImageInfo(width / 2, height / 2, SKColorType.Bgra8888, SKAlphaType.Unpremul),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        var jpg = Path.Combine(_directory, "pair.jpg");
        using (var image = SKImage.FromBitmap(half!))
        using (var jd = image.Encode(SKEncodedImageFormat.Jpeg, 92))
        using (var fs = File.Create(jpg))
        {
            jd.SaveTo(fs);
        }

        return (png, jpg);
    }
}
