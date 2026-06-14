using System.Diagnostics;
using SkiaSharp;
using ViewPrism2.Core.Services.Similarity;
using ViewPrism2.Infrastructure.Imaging;
using Xunit;

namespace ViewPrism2.Oracle;

/// <summary>
/// pHash decode 戦略の latency 退行ガード(P-09・default-on の軽量能力ゲート)。
/// production adapter(scaled-decode)が full-decode より明確に速いことを**相対比**で守る
/// (machine 速度に非依存)。production reader を誤って full-decode へ戻したら ratio が崩れて検出する。
/// 主判定=相対 μ 比(P-08 実測 6.29× に対し安全側の ≥2×)。μ/max/memory は報告のみ・Cpk は出さない
/// (capability-discipline: μ=主・Cpk=番兵で、ここでは小 n のため相対比を主ゲートにする)。
/// </summary>
[Trait("oracle", "latency-guard")]
[Trait("scope", "this-build")]
public sealed class PHashLatencyGuardTests(ITestOutputHelper output)
{
    private const int PhotoCount = 8;
    private const int Width = 1280;
    private const int Height = 960;
    private const int Reps = 4;          // 先頭 1 本は warmup 除外
    private const double MinSpeedup = 2.0; // 退行ガード: B は A の 2 倍以上速い(観測 6.29× に対し広い余裕)

    [Fact]
    public async Task production_scaled_decodeはfull_decodeより明確に速い_退行ガード()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ViewPrism2.latguard", Guid.NewGuid().ToString("D"));
        Directory.CreateDirectory(dir);
        try
        {
            var photos = new List<string>(PhotoCount);
            for (var i = 0; i < PhotoCount; i++)
            {
                var p = Path.Combine(dir, $"p{i:D2}.jpg");
                WriteLargePhoto(p, Width, Height, patternIndex: i, quality: 85);
                photos.Add(p);
            }

            var full = new PHashImageReader();              // 参照(旧 adapter)
            var scaled = new PHashImageReaderScaledDecode(); // production adapter

            var samplesFull = new List<double>();
            var samplesScaled = new List<double>();
            for (var r = 0; r < Reps; r++)
            {
                var (msFull, okFull) = await MeasureAsync(full, photos);
                var (msScaled, okScaled) = await MeasureAsync(scaled, photos);
                Assert.Equal(PhotoCount, okFull);   // 全枚数で有効 hash(壊れていない)
                Assert.Equal(PhotoCount, okScaled);
                if (r > 0)
                {
                    samplesFull.Add(msFull);
                    samplesScaled.Add(msScaled);
                }
            }

            var muFull = samplesFull.Average();
            var muScaled = samplesScaled.Average();
            var speedup = muScaled > 1e-9 ? muFull / muScaled : double.PositiveInfinity;
            var mem = GC.GetTotalMemory(forceFullCollection: true) / (1024.0 * 1024.0);

            var report = string.Join(Environment.NewLine,
                "== P-09 latency 退行ガード(scaled-decode 対 full-decode)==",
                $"workload: {PhotoCount} photos {Width}x{Height} jpeg / n={samplesScaled.Count}",
                $"full-decode  : mu={muFull:F1}ms max={samplesFull.Max():F1}",
                $"scaled-decode: mu={muScaled:F1}ms max={samplesScaled.Max():F1}",
                $"SPEEDUP={speedup:F2}x (>= {MinSpeedup:F1}x 要求) / memory={mem:F1}MB");
            output.WriteLine(report);

            Assert.True(speedup >= MinSpeedup,
                $"production scaled-decode の速度優位が {MinSpeedup:F1}x を割った(full-decode へ退行?): {speedup:F2}x");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    private static async Task<(double Ms, int Ok)> MeasureAsync(IPHashImageReader reader, IReadOnlyList<string> photos)
    {
        var ok = 0;
        var sw = Stopwatch.StartNew();
        foreach (var p in photos)
        {
            if (await reader.ComputePHashAsync(p) is not null) { ok++; }
        }

        sw.Stop();
        return (sw.Elapsed.TotalMilliseconds, ok);
    }

    /// <summary>大判 JPEG を生成(256×256 多周波パターンを拡大して decode コストを実在させる)。</summary>
    private static void WriteLargePhoto(string path, int width, int height, int patternIndex, int quality)
    {
        const int design = 256;
        var fx1 = (patternIndex % 5) + 1;
        var fy1 = (patternIndex % 3) + 1;
        var fx2 = (patternIndex % 4) + 2;
        var fy2 = (patternIndex % 6) + 1;
        var ph = patternIndex * 0.7;
        using var small = new SKBitmap(design, design);
        var u = 2 * Math.PI / design;
        for (var y = 0; y < design; y++)
        {
            for (var x = 0; x < design; x++)
            {
                var val = (byte)Math.Clamp(
                    128
                    + (45 * Math.Cos((u * ((fx1 * x) + (fy1 * y))) + ph))
                    + (35 * Math.Cos((u * ((fx2 * x) + (fy2 * y))) + (ph * 1.3))),
                    0, 255);
                small.SetPixel(x, y, new SKColor(val, val, val));
            }
        }

        using var large = small.Resize(
            new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        using var image = SKImage.FromBitmap(large);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, quality)
            ?? throw new InvalidOperationException("jpeg encode 失敗");
        using var stream = File.Create(path);
        data.SaveTo(stream);
    }
}
