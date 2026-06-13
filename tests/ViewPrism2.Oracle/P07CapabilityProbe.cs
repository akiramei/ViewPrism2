using System.Diagnostics;
using SkiaSharp;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services.Similarity;
using ViewPrism2.Infrastructure.Database;
using ViewPrism2.Infrastructure.Imaging;
using Xunit;

namespace ViewPrism2.Oracle;

/// <summary>
/// P-07 能力プローブ(loop-v3-similarity・設計者側・工場非開示)。
/// 「観測のみ」から「target + 片側 Cpk」への格上げ実験(工程能力指数の BomDD 試行)。
/// 特性: 1,000 枚コレクションでの類似検索レイテンシ。
///   - C1(cold 初回): 全 1,000 枚の pHash を decode+計算する初回検索の体感コスト(ヘッドライン・一発)
///   - C2(warm 再検索): 特徴量キャッシュ済みで再検索する繰返しコスト(反復可能=Cpk 対象)
/// 片側 Cpk_upper = (USL − μ) / (3σ)。USL は暫定 UX 目標。合否ゲートにはせず「報告」する
/// (少数サンプル・環境ノイズ支配のため。Cpk は崖っぷち検出の警告灯として使う)。
/// </summary>
[Trait("probe", "P-07")]
public sealed class P07CapabilityProbe(ITestOutputHelper output)
{
    private const int ImageCount = 1000;
    private const int ColdReps = 6;           // 先頭 1 本はディスクキャッシュ温め(σ から除外)
    private const double UslColdMs = 3000.0;   // 暫定 UX 目標: 1,000 枚の類似検索 初回 ≤ 3s

    [Fact] // 探索プローブ: 通常回帰では skip。実行は VP_RUN_P07=1 を設定(~10s)
    public async Task 類似検索1000枚の能力を測定する()
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable("VP_RUN_P07") == "1",
            "P-07 能力プローブ: VP_RUN_P07=1 を設定して明示実行する(~10s)");

        var imageDir = Path.Combine(Path.GetTempPath(), "ViewPrism2.P07", Guid.NewGuid().ToString("D"));
        Directory.CreateDirectory(imageDir);
        try
        {
            // 1,000 枚の構造の異なる画像を生成(一度だけ)
            for (var i = 0; i < ImageCount; i++)
            {
                WriteStructured(Path.Combine(imageDir, $"img-{i:D4}.png"), i);
            }

            var ct = TestContext.Current.CancellationToken;
            var baseId = "img-0000.png";
            var coldSamples = new List<double>();
            var resultCount = 0;
            double warmMs = 0;
            double mem = 0;

            // C1 cold 初回(全 1,000 枚 decode+pHash)を反復測定。各 rep は fresh DB=空特徴量で cold
            for (var r = 0; r < ColdReps; r++)
            {
                using var db = new OracleDb();
                var features = new ImageFeatureRepository(db.Manager);
                var similarities = new ImageSimilarityRepository(db.Manager);
                var service = new SimilaritySearchService(
                    db.Folders, db.Images, features, similarities, new PHashImageReader(), db.Clock);
                var folder = new SyncFolder { Id = "fld", Name = "perf", Path = imageDir };
                Assert.True((await db.Folders.AddAsync(folder)).IsSuccess);
                for (var i = 0; i < ImageCount; i++)
                {
                    var name = $"img-{i:D4}.png";
                    await db.Images.AddAsync(new ImageRecord
                    {
                        Id = name, SyncFolderId = folder.Id, RelativePath = name, FileName = name,
                        FileSize = 100, Hash = i.ToString("x", System.Globalization.CultureInfo.InvariantCulture).PadLeft(64, '0'),
                        Status = ImageStatus.Normal,
                        CreatedDate = "2026-01-01T00:00:00.000Z", ModifiedDate = "2026-01-01T00:00:00.000Z",
                    });
                }

                var sw = Stopwatch.StartNew();
                var results = await service.FindSimilarAsync(baseId, threshold: 70, progress: null, ct: ct);
                sw.Stop();
                if (r == 0)
                {
                    resultCount = results.Count;
                }
                else
                {
                    coldSamples.Add(sw.Elapsed.TotalMilliseconds); // rep0 は σ から除外
                }

                // 最終 rep で warm 再検索(キャッシュ済み)1 本とメモリを採取
                if (r == ColdReps - 1)
                {
                    var wsw = Stopwatch.StartNew();
                    await service.FindSimilarAsync(baseId, threshold: 70, progress: null, ct: ct);
                    wsw.Stop();
                    warmMs = wsw.Elapsed.TotalMilliseconds;
                    mem = GC.GetTotalMemory(forceFullCollection: true) / (1024.0 * 1024.0);
                }
            }

            var mean = coldSamples.Average();
            var variance = coldSamples.Sum(s => (s - mean) * (s - mean)) / (coldSamples.Count - 1);
            var sigma = Math.Sqrt(variance);
            var cpkUpper = sigma > 1e-9 ? (UslColdMs - mean) / (3.0 * sigma) : double.PositiveInfinity;

            var report = string.Join(Environment.NewLine,
                "== P-07 capability (baseline=factory-04 engine) ==",
                $"images={ImageCount} results={resultCount}",
                $"C1 cold first-pass: n={coldSamples.Count} mean={mean:F0} sigma={sigma:F1} min={coldSamples.Min():F0} max={coldSamples.Max():F0} ms",
                $"USL_cold={UslColdMs:F0} ms  Cpk_upper=(USL-mean)/(3*sigma)={cpkUpper:F2}",
                $"C2 warm re-search (cached) = {warmMs:F1} ms (参考: キャッシュ効果)",
                $"managed memory after search = {mem:F1} MB",
                $"verdict(cold): {(cpkUpper >= 1.33 ? "capable (>=1.33)" : cpkUpper >= 1.0 ? "marginal (1.0-1.33)" : "not capable (<1.0)")}");
            output.WriteLine(report);
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "viewprism2-p07-result.txt"), report);

            Assert.True(resultCount > 0); // サニティ: 検索が機能(能力値は報告のみ・ゲートしない)
        }
        finally
        {
            try { Directory.Delete(imageDir, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>index で構造が変わる 32×32 グレースケール画像(distinct pHash 用)。</summary>
    private static void WriteStructured(string path, int i)
    {
        const int n = 32;
        var fx = (i % 5) + 1;
        var fy = (i % 3) + 1;
        var phase = i * 0.7;
        using var bitmap = new SKBitmap(n, n);
        for (var y = 0; y < n; y++)
        {
            for (var x = 0; x < n; x++)
            {
                var v = (byte)Math.Clamp(
                    128 + (90 * Math.Cos((2 * Math.PI * ((fx * x) + (fy * y)) / n) + phase)), 0, 255);
                bitmap.SetPixel(x, y, new SKColor(v, v, v));
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 90);
        using var stream = File.Create(path);
        data.SaveTo(stream);
    }
}
