using System.Diagnostics;
using SkiaSharp;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services.Similarity;
using ViewPrism2.Infrastructure.Database;
using ViewPrism2.Infrastructure.Imaging;
using Xunit;

namespace ViewPrism2.Oracle;

/// <summary>
/// 実 A/B(loop-v3-similarity・生産技術・工場非開示)。capability-discipline.md の Pareto 判定を実走する。
/// 対決する 2 工場(同一最終レシピ=32×32 双線形→PerceptualHash。**decode 経路だけが差**):
///   - factory-A = <see cref="PHashImageReader"/>(SKBitmap.Decode でフル解像度→Resize)
///   - factory-B = <see cref="PHashImageReaderScaledDecode"/>(SKCodec scaled-decode で早期縮小)
/// 単一仮説: factory-B の early-shrink は大きな写真の pHash レイテンシ(μ)を下げる。
///
/// 2 つの [Fact]:
///   (1) 同等性ゲート(常時実行): factory-B が S-25 と同じ**順位等価の横断契約**(EQ-RANK)を満たすか。
///       pHash 値は factory-A と一致しなくてよい(CPOL-103 preserve_with_adapter)。順位が等価ならば正しい。
///   (2) μ 対決(VP_RUN_AB=1 で明示実行): 代表ワークロード=大きな JPEG 写真で μ_A 対 μ_B を ABBA 計測。
///       μ=選択の主レバー、Cpk=安定性の番兵(synthesize 健全性: B が σ を膨らませていないか)。
/// </summary>
[Trait("ab", "decode-strategy")]
public sealed class ABDecodeStrategyProbe(ITestOutputHelper output)
{
    // ---- 同等性ゲート用(代表サイズ=factory-B の早期縮小が engage する大判) ----
    private const int GatePhotoW = 1280;
    private const int GatePhotoH = 960;

    // ---- μ 対決用(代表ワークロード=大きな写真) ----
    private const int PhotoCount = 40;
    private const int PhotoWidth = 2000;
    private const int PhotoHeight = 1500;     // 3:2 ≈ 3M px。decode コストが実在する大きさ
    private const int JpegQuality = 85;       // 実写真の代表品質
    private const int Reps = 6;               // 各工場 6 計測。先頭 1 は warmup として σ/μ から除外
    private const double PerPhotoUslMs = 40.0; // 暫定 UX 目標(写真 1 枚あたり)→ USL = PhotoCount×40ms

    /// <summary>
    /// (1) 同等性ゲート: **両工場とも**順位等価の横断契約(EQ-RANK)を満たすことを代表サイズで検証。
    /// EQ-RANK の本質は threshold 非依存の**順位**(近傍=同一構造の再エンコードが無関係より上位)。
    /// 絶対閾値メンバーシップ(threshold:70)は factory-04 固有の較正であり this-build に属するため、
    /// ここでは threshold を緩め(=1)て全件を候補化し、純粋な順位で等価を検査する。
    /// 大判 1280×960 で factory-B の早期縮小を実際に engage させた上での順位保存を確認する。
    /// これが緑でなければ μ 対決は無意味(correctness が必須条件 — capability-discipline §A/B)。
    /// </summary>
    [Fact]
    [Trait("scope", "cross-factory")]
    public async Task 両工場が順位等価の横断契約を満たす_correctness_gate()
    {
        var imageDir = Path.Combine(Path.GetTempPath(), "ViewPrism2.ABgate", Guid.NewGuid().ToString("D"));
        Directory.CreateDirectory(imageDir);
        try
        {
            // base + 近傍 3 枚(同一構造 pattern0 の JPEG 品質違い再エンコード)+ 無関係 5 枚(構造の異なるパターン)
            const string baseName = "base.jpg";
            WriteLargePhoto(Path.Combine(imageDir, baseName), GatePhotoW, GatePhotoH, patternIndex: 0, quality: 92);
            var near = new[] { "near-q88.jpg", "near-q80.jpg", "near-q72.jpg" };
            var nearQ = new[] { 88, 80, 72 };
            for (var i = 0; i < near.Length; i++)
            {
                WriteLargePhoto(Path.Combine(imageDir, near[i]), GatePhotoW, GatePhotoH, patternIndex: 0, quality: nearQ[i]);
            }

            var unrelated = new[] { "u1.jpg", "u2.jpg", "u3.jpg", "u4.jpg", "u5.jpg" };
            var unrelatedP = new[] { 7, 11, 13, 17, 19 };
            for (var i = 0; i < unrelated.Length; i++)
            {
                WriteLargePhoto(Path.Combine(imageDir, unrelated[i]), GatePhotoW, GatePhotoH, patternIndex: unrelatedP[i], quality: 88);
            }

            var files = new List<string> { baseName };
            files.AddRange(near);
            files.AddRange(unrelated);
            var nearSet = near.ToHashSet(StringComparer.Ordinal);
            var ct = TestContext.Current.CancellationToken;

            // 両工場とも同一ワークロードで EQ-RANK を満たすこと(共有の横断契約)
            await AssertRankingEquivalenceAsync(new PHashImageReader(), "factory-A", imageDir, files, baseName, nearSet, ct);
            await AssertRankingEquivalenceAsync(new PHashImageReaderScaledDecode(), "factory-B", imageDir, files, baseName, nearSet, ct);
        }
        finally
        {
            try { Directory.Delete(imageDir, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>
    /// 指定 reader で base を検索し、近傍集合が無関係より厳密に上位にランクされる(EQ-RANK)ことを表明する。
    /// threshold=1 で全件を候補化し、絶対閾値較正に依存しない純粋な順位等価を検査する。
    /// </summary>
    private static async Task AssertRankingEquivalenceAsync(
        IPHashImageReader reader, string label, string imageDir, IReadOnlyList<string> files,
        string baseName, HashSet<string> nearSet, CancellationToken ct)
    {
        using var db = new OracleDb();
        var features = new ImageFeatureRepository(db.Manager);
        var similarities = new ImageSimilarityRepository(db.Manager);
        Assert.True((await db.Folders.AddAsync(
            new SyncFolder { Id = "fld", Name = "abgate", Path = imageDir })).IsSuccess);
        foreach (var name in files)
        {
            await db.Images.AddAsync(new ImageRecord
            {
                Id = name,
                SyncFolderId = "fld",
                RelativePath = name,
                FileName = name,
                FileSize = new FileInfo(Path.Combine(imageDir, name)).Length,
                Hash = new string('0', 64),
                Status = ImageStatus.Normal,
                CreatedDate = "2026-01-01T00:00:00.000Z",
                ModifiedDate = "2026-01-01T00:00:00.000Z",
            });
        }

        var service = new SimilaritySearchService(db.Folders, db.Images, features, similarities, reader, db.Clock);
        var results = await service.FindSimilarAsync(baseName, threshold: 1, progress: null, ct: ct);

        foreach (var id in nearSet)
        {
            Assert.Contains(results, r => r.ImageId == id); // 近傍は必ず候補に入る
        }

        var top = results.Take(nearSet.Count).Select(r => r.ImageId).ToHashSet(StringComparer.Ordinal);
        Assert.True(nearSet.SetEquals(top), $"{label}: 上位 {nearSet.Count} が近傍集合でない(順位等価の破れ)");

        var nearMin = results.Where(r => nearSet.Contains(r.ImageId)).Min(r => r.Score);
        var others = results.Where(r => !nearSet.Contains(r.ImageId)).ToList();
        if (others.Count > 0)
        {
            Assert.True(nearMin > others.Max(r => r.Score), $"{label}: 近傍が無関係より上位でない(順位等価の破れ)");
        }
    }

    /// <summary>
    /// (2) μ 対決: 代表ワークロード(大きな JPEG 写真 N 枚)で factory-A 対 factory-B の
    /// pHash 計算レイテンシを ABBA で計測し、μ/σ/Cpk と speedup を報告する(報告のみ・ゲートしない)。
    /// </summary>
    [Fact] // 探索プローブ: 通常回帰では skip。実行は VP_RUN_AB=1(~数秒)
    public async Task AB_decode戦略のμ対決()
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable("VP_RUN_AB") == "1",
            "A/B μ 対決: VP_RUN_AB=1 を設定して明示実行する(~数秒)");

        var imageDir = Path.Combine(Path.GetTempPath(), "ViewPrism2.AB", Guid.NewGuid().ToString("D"));
        Directory.CreateDirectory(imageDir);
        try
        {
            var photos = new List<string>(PhotoCount);
            for (var i = 0; i < PhotoCount; i++)
            {
                var p = Path.Combine(imageDir, $"photo-{i:D3}.jpg");
                WriteLargePhoto(p, PhotoWidth, PhotoHeight, patternIndex: i, JpegQuality);
                photos.Add(p);
            }

            var readerA = new PHashImageReader();
            var readerB = new PHashImageReaderScaledDecode();

            // --- 妥当性 + 適合(adapter)ドリフト測定(非計時): 両工場の hash を突き合わせる ---
            int nullA = 0, nullB = 0;
            var drift = new List<int>();
            foreach (var p in photos)
            {
                var ha = await readerA.ComputePHashAsync(p);
                var hb = await readerB.ComputePHashAsync(p);
                if (ha is null) { nullA++; }
                if (hb is null) { nullB++; }
                if (ha is not null && hb is not null) { drift.Add(HammingDistance.Between(ha, hb)); }
            }

            // --- μ 対決(ABBA で線形ドリフトを相殺) ---
            var samplesA = new List<double>();
            var samplesB = new List<double>();
            var order = new List<char>();
            for (var i = 0; i < Reps; i++)
            {
                if (i % 2 == 0) { order.Add('A'); order.Add('B'); }
                else { order.Add('B'); order.Add('A'); }
            }

            var firstA = true;
            var firstB = true;
            foreach (var who in order)
            {
                var reader = who == 'A' ? (IPHashImageReader)readerA : readerB;
                var ms = await MeasureBatchAsync(reader, photos);
                if (who == 'A')
                {
                    if (firstA) { firstA = false; } else { samplesA.Add(ms); } // 先頭 1 本は warmup 除外
                }
                else
                {
                    if (firstB) { firstB = false; } else { samplesB.Add(ms); }
                }
            }

            var (muA, sigA, cpkA) = Stats(samplesA, PhotoCount * PerPhotoUslMs);
            var (muB, sigB, cpkB) = Stats(samplesB, PhotoCount * PerPhotoUslMs);
            var speedup = muB > 1e-9 ? muA / muB : double.PositiveInfinity;
            var driftMean = drift.Count > 0 ? drift.Average() : 0;
            var driftMax = drift.Count > 0 ? drift.Max() : 0;

            var report = string.Join(Environment.NewLine,
                "== 実 A/B: decode 戦略 μ 対決 ==",
                $"workload: {PhotoCount} photos {PhotoWidth}x{PhotoHeight} jpeg q{JpegQuality} / batch=全{PhotoCount}枚 pHash / n={samplesA.Count} (ABBA, warmup除外)",
                $"USL_batch={PhotoCount * PerPhotoUslMs:F0}ms ({PerPhotoUslMs:F0}ms/枚)",
                "",
                $"factory-A (full-decode)    : mu={muA:F1}ms sigma={sigA:F1} min={Min(samplesA):F1} max={Max(samplesA):F1}  Cpk={Fmt(cpkA)}",
                $"factory-B (scaled-decode)  : mu={muB:F1}ms sigma={sigB:F1} min={Min(samplesB):F1} max={Max(samplesB):F1}  Cpk={Fmt(cpkB)}",
                "",
                $"SPEEDUP mu_A/mu_B = {speedup:F2}x  ({(muA - muB) / PhotoCount:F2}ms/枚 改善)",
                $"correctness: null_A={nullA} null_B={nullB}(両者全枚数で有効 hash であること)",
                $"adapter drift (A vs B hamming): mean={driftMean:F1} max={driftMax} / 64bit(CPOL-103 preserve_with_adapter — 値は不一致でよい)",
                $"stability guard: Cpk_B {(cpkB >= cpkA * 0.5 ? "健全(σ崩壊なし)" : "要注意(B が σ を膨張)")}");
            output.WriteLine(report);
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "viewprism2-ab-result.txt"), report);

            Assert.Equal(0, nullA + nullB); // サニティ: 両工場とも全枚数で有効 hash(能力値は報告のみ)
        }
        finally
        {
            try { Directory.Delete(imageDir, recursive: true); } catch (IOException) { }
        }
    }

    private static async Task<double> MeasureBatchAsync(IPHashImageReader reader, IReadOnlyList<string> photos)
    {
        var sw = Stopwatch.StartNew();
        foreach (var p in photos)
        {
            _ = await reader.ComputePHashAsync(p); // 逐次=単一スレッド decode の公平比較
        }

        sw.Stop();
        return sw.Elapsed.TotalMilliseconds;
    }

    private static (double Mu, double Sigma, double Cpk) Stats(List<double> samples, double usl)
    {
        var mu = samples.Average();
        var variance = samples.Count > 1
            ? samples.Sum(s => (s - mu) * (s - mu)) / (samples.Count - 1)
            : 0.0;
        var sigma = Math.Sqrt(variance);
        var cpk = sigma > 1e-9 ? (usl - mu) / (3.0 * sigma) : double.PositiveInfinity;
        return (mu, sigma, cpk);
    }

    private static double Min(List<double> s) => s.Count > 0 ? s.Min() : 0;

    private static double Max(List<double> s) => s.Count > 0 ? s.Max() : 0;

    private static string Fmt(double cpk) => double.IsInfinity(cpk) ? "inf" : cpk.ToString("F2");

    /// <summary>
    /// 大きな JPEG 写真を生成する(decode コストが実在する代表ワークロード)。
    /// 256×256 の多周波構造パターン(patternIndex で distinct)を双線形で大判へ拡大し JPEG 化する。
    /// 直接 200 万 px へ SetPixel すると生成が遅いため、小パターン→拡大→エンコードで賄う。
    /// </summary>
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
