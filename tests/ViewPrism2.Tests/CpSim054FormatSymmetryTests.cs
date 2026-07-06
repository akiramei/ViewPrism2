using SkiaSharp;
using ViewPrism2.Core.Services.Similarity;
using ViewPrism2.Infrastructure.Imaging;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// CP-PHASH-ADAPTER-019 拡張(ECO-054): scaled-decode のフォーマット間経路一貫性。
/// 同一ピクセル内容の PNG/JPEG 複製ペアは、production reader(scaled-decode)でも
/// 小距離(スコア 90 台)で照合される — JPEG のみ早期縮小・PNG は全解像度一発縮小という
/// 経路非対称が系統誤差(実測 -18 点)を作らないこと。full-decode は対照(較正)。
/// </summary>
[Trait("cp", "CP-PHASH-ADAPTER-019")]
public sealed class CpSim054FormatSymmetryTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "ViewPrism2.Tests", Guid.NewGuid().ToString("D"));

    public CpSim054FormatSymmetryTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public async Task 対照_fullDecodeでは同一内容のpngとjpgは小距離()
    {
        var (png, jpg) = WritePair(512);
        var reader = new PHashImageReader();
        var d = await DistanceAsync(reader, png, jpg);
        Assert.True(d <= 5, $"full-decode の png×jpg 距離が {d}(期待 ≤5 — 較正: 知覚的実距離は小さい)");
    }

    [Fact]
    public async Task scaledDecodeでも滑らかな同一内容のpngとjpgは小距離()
    {
        var (png, jpg) = WritePair(512);
        var reader = new PHashImageReaderScaledDecode();
        var d = await DistanceAsync(reader, png, jpg);
        Assert.True(d <= 5, $"scaled-decode の png×jpg 距離が {d}(期待 ≤5)");
    }

    [Fact]
    public async Task 高密度テクスチャでもscaledのフォーマット間距離はfullを悪化させない_経路一貫性()
    {
        // ECO-054 プローブ: 高周波成分を持つ内容で経路非対称が顕在化する(実測: 実ペアで scaled=8/full=2)。
        // 性質= 「scaled 経路のフォーマット間距離は full-decode 経路の距離+2 を超えない」
        // (是正前実測: busy 合成で scaled=30 vs full=24 → 30 > 26 で不合格。実ペアも 8 > 4 で同性質違反)
        var (png, jpg) = WriteBusyPair(width: 2388, height: 1668);
        var dScaled = await DistanceAsync(new PHashImageReaderScaledDecode(), png, jpg);
        var dFull = await DistanceAsync(new PHashImageReader(), png, jpg);
        Assert.True(dScaled <= dFull + 2,
            $"scaled のフォーマット間距離 {dScaled} が full {dFull}+2 を超過(経路非対称の系統誤差・ECO-054)");
    }

    // ---- ヘルパ ----

    private static async Task<int> DistanceAsync(IPHashImageReader reader, string a, string b)
    {
        var ha = await reader.ComputePHashAsync(a);
        var hb = await reader.ComputePHashAsync(b);
        Assert.NotNull(ha);
        Assert.NotNull(hb);
        return HammingDistance.Between(ha, hb);
    }

    /// <summary>
    /// 高密度マルチスケール テクスチャ(正弦波の重ね+決定的擬似ノイズ)の複製ペア:
    /// PNG=原寸・JPEG q92=50% 縮小(実ペア orientation_fixture_06 と同構図。高周波内容で経路非対称が顕在化する)。
    /// </summary>
    private (string Png, string Jpg) WriteBusyPair(int width, int height)
    {
        using var bmp = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var v = 128
                    + (38 * Math.Sin(x * 0.31) * Math.Cos(y * 0.27))
                    + (26 * Math.Sin((x * 0.071) + (y * 0.043)))
                    + (18 * Math.Sin((x + (2 * y)) * 0.013))
                    + ((((x * 7919) + (y * 104729)) % 41 - 20) * 0.9);
                var b = (byte)Math.Clamp(v, 0, 255);
                bmp.SetPixel(x, y, new SKColor(b, b, b));
            }
        }

        var png = Path.Combine(_dir, "busy.png");
        using (var image = SKImage.FromBitmap(bmp))
        using (var pd = image.Encode(SKEncodedImageFormat.Png, 100))
        using (var fs = File.Create(png))
        {
            pd.SaveTo(fs);
        }

        using var half = bmp.Resize(
            new SKImageInfo(width / 2, height / 2, SKColorType.Bgra8888, SKAlphaType.Unpremul),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        var jpg = Path.Combine(_dir, "busy-half.jpg");
        using (var image = SKImage.FromBitmap(half!))
        using (var jd = image.Encode(SKEncodedImageFormat.Jpeg, 92))
        using (var fs = File.Create(jpg))
        {
            jd.SaveTo(fs);
        }

        return (png, jpg);
    }

    /// <summary>同一ピクセル内容を PNG と JPEG q90 の両形式で書き出す(長辺 > 64 = 早期縮小の対象域)。</summary>
    private (string Png, string Jpg) WritePair(int size)
    {
        using var bmp = new SKBitmap(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                // 多スケール構造(勾配+粗い矩形+帯)— JPEG q90 で保存される構造・回転非対称
                var v = (byte)((x * 255 / size + y * 96 / size) % 256);
                if (x < size / 4 && y < size / 8)
                {
                    v = 255;
                }
                else if (y > size * 3 / 4 && x > size / 2)
                {
                    v = (byte)(v / 4);
                }

                bmp.SetPixel(x, y, new SKColor(v, v, v));
            }
        }

        var png = Path.Combine(_dir, "pair.png");
        var jpg = Path.Combine(_dir, "pair.jpg");
        using (var image = SKImage.FromBitmap(bmp))
        {
            using var pd = image.Encode(SKEncodedImageFormat.Png, 100);
            using var fs1 = File.Create(png);
            pd.SaveTo(fs1);
            using var jd = image.Encode(SKEncodedImageFormat.Jpeg, 90);
            using var fs2 = File.Create(jpg);
            jd.SaveTo(fs2);
        }

        return (png, jpg);
    }
}
