using System.Security.Cryptography;
using SkiaSharp;
using ViewPrism2.Core.Services.Similarity;

namespace ViewPrism2.Infrastructure.Imaging;

/// <summary>
/// ECO-067 / IMG-021: pHash候補に対する重複関係検証器。
/// 第0段=byte/正規化表示画素exact、第2段=D4位置合わせ後のRGBA局所差とcrop重複を評価する。
/// 平均差だけでなくchanged/severe画素率と局所block最大差を併用し、表情・文字等の局所置換を
/// 「実質同一」へ希釈しない。画像へ書込まず、一時fileも作らない(INV-009)。
/// </summary>
public sealed class DuplicateRelationshipVerifier : IDuplicateRelationshipVerifier
{
    private const int CanonicalSize = 64;
    private const int BlockSize = 8;

    public string AdapterId => "skia-duplicate-relationship-v1";

    public async Task<DuplicateVerificationResult> VerifyAsync(
        string absolutePathA,
        string absolutePathB,
        CancellationToken cancellationToken = default,
        bool bytesKnownDifferent = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(absolutePathA);
        ArgumentException.ThrowIfNullOrEmpty(absolutePathB);

        if (!bytesKnownDifferent
            && await BytesEqualAsync(absolutePathA, absolutePathB, cancellationToken).ConfigureAwait(false))
        {
            return Result(DuplicateRelationship.SameFile, 100);
        }

        return await Task.Run(() => VerifyPixels(absolutePathA, absolutePathB, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
    }

    private static DuplicateVerificationResult VerifyPixels(
        string pathA, string pathB, CancellationToken cancellationToken)
    {
        using var a = DecodeUpright(pathA);
        using var b = DecodeUpright(pathB);
        if (a is null || b is null)
        {
            return Result(DuplicateRelationship.NonSimilar, 0);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (PixelsEqual(a, b))
        {
            return Result(DuplicateRelationship.ImageContentMatch, 100);
        }

        using var aCanonical = Resize(a, CanonicalSize, CanonicalSize);
        using var bCanonical = Resize(b, CanonicalSize, CanonicalSize);
        if (aCanonical is null || bCanonical is null)
        {
            return Result(DuplicateRelationship.NonSimilar, 0);
        }

        var bestFull = Metrics.Worst;
        var bVariants = CreateD4Variants(bCanonical);
        try
        {
            foreach (var variant in bVariants)
            {
                cancellationToken.ThrowIfCancellationRequested();
                bestFull = Metrics.Better(bestFull, Compare(aCanonical, variant));
            }

            // 再圧縮/resize/回転鏡像: 全体は整合し、局所置換の強いblockを持たない。
            if (bestFull.Mean <= 12.0 && bestFull.SevereFraction <= 0.015
                && bestFull.ChangedFraction <= 0.12 && bestFull.MaxBlockMean <= 30.0)
            {
                return Result(DuplicateRelationship.SubstantiallySame, Score(99, bestFull.Mean, 12));
            }

            var geometryChanged = a.Width != b.Width || a.Height != b.Height;
            var bestCrop = Metrics.Worst;
            var shouldTryCrop = geometryChanged || bestFull.Mean > 18.0 || bestFull.MaxBlockMean > 30.0;
            if (shouldTryCrop)
            {
                bestCrop = BestCropMetrics(aCanonical, bVariants, cancellationToken);
                var reverseVariants = CreateD4Variants(aCanonical);
                try
                {
                    bestCrop = Metrics.Better(bestCrop, BestCropMetrics(bCanonical, reverseVariants, cancellationToken));
                }
                finally
                {
                    DisposeAll(reverseVariants);
                }
            }

            // crop/余白差: 十分な面積(55%以上)を位置合わせしたときだけ部分重複。
            var cropMateriallyImprovesAlignment = bestCrop.Mean <= bestFull.Mean * 0.95;
            if (shouldTryCrop && cropMateriallyImprovesAlignment
                && bestCrop.Mean <= 40.0 && bestCrop.SevereFraction <= 0.20
                && bestCrop.ChangedFraction <= 0.50 && bestCrop.MaxBlockMean <= 150.0)
            {
                return Result(DuplicateRelationship.PartialOverlap, Score(79, bestCrop.Mean, 16));
            }

            // pHash候補として大局的に近くても上記precision条件を満たさないものは「類似」。
            // 局所置換はmeanが低くてもmax block/severe率でここへ落ちる。
            if (bestFull.Mean <= 55.0 || bestCrop.Mean <= 42.0)
            {
                var metric = Math.Min(bestFull.Mean, bestCrop.Mean);
                return Result(DuplicateRelationship.Similar, Score(49, metric, 55));
            }

            return Result(DuplicateRelationship.NonSimilar, 0);
        }
        finally
        {
            DisposeAll(bVariants);
        }
    }

    private static async Task<bool> BytesEqualAsync(string pathA, string pathB, CancellationToken ct)
    {
        var aInfo = new FileInfo(pathA);
        var bInfo = new FileInfo(pathB);
        if (!aInfo.Exists || !bInfo.Exists || aInfo.Length != bInfo.Length)
        {
            return false;
        }

        await using var a = new FileStream(pathA, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var b = new FileStream(pathB, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var ah = await SHA256.HashDataAsync(a, ct).ConfigureAwait(false);
        var bh = await SHA256.HashDataAsync(b, ct).ConfigureAwait(false);
        return CryptographicOperations.FixedTimeEquals(ah, bh);
    }

    private static SKBitmap? DecodeUpright(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var codec = SKCodec.Create(stream);
            if (codec is null) return null;
            var info = new SKImageInfo(codec.Info.Width, codec.Info.Height,
                SKColorType.Bgra8888, SKAlphaType.Unpremul, SKColorSpace.CreateSrgb());
            using var decoded = new SKBitmap(info);
            var result = codec.GetPixels(info, decoded.GetPixels());
            if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput)) return null;
            return ExifOrientationTransform.RequiresTransform(codec.EncodedOrigin)
                ? ExifOrientationTransform.ToUpright(decoded, codec.EncodedOrigin)
                : decoded.Copy();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool PixelsEqual(SKBitmap a, SKBitmap b)
        => a.Width == b.Width && a.Height == b.Height
            && a.GetPixelSpan().SequenceEqual(b.GetPixelSpan());

    private static SKBitmap? Resize(SKBitmap source, int width, int height)
        => source.Resize(
            new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul, SKColorSpace.CreateSrgb()),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));

    private static List<SKBitmap> CreateD4Variants(SKBitmap source)
    {
        var result = new List<SKBitmap>(8);
        for (var kind = 0; kind < 8; kind++)
        {
            var dst = new SKBitmap(CanonicalSize, CanonicalSize, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            for (var y = 0; y < CanonicalSize; y++)
            for (var x = 0; x < CanonicalSize; x++)
            {
                var (sx, sy) = kind switch
                {
                    0 => (x, y),
                    1 => (y, CanonicalSize - 1 - x),
                    2 => (CanonicalSize - 1 - x, CanonicalSize - 1 - y),
                    3 => (CanonicalSize - 1 - y, x),
                    4 => (CanonicalSize - 1 - x, y),
                    5 => (x, CanonicalSize - 1 - y),
                    6 => (y, x),
                    _ => (CanonicalSize - 1 - y, CanonicalSize - 1 - x),
                };
                dst.SetPixel(x, y, source.GetPixel(sx, sy));
            }
            result.Add(dst);
        }
        return result;
    }

    private static Metrics BestCropMetrics(SKBitmap whole, IReadOnlyList<SKBitmap> candidates, CancellationToken ct)
    {
        var best = Metrics.Worst;
        foreach (var ratio in new[] { 0.55, 0.65, 0.75, 0.80, 0.85, 0.90 })
        {
            var size = Math.Max(8, (int)Math.Round(CanonicalSize * ratio, MidpointRounding.AwayFromZero));
            var slack = CanonicalSize - size;
            foreach (var ox in new[] { 0.0, 0.5, 1.0 })
            foreach (var oy in new[] { 0.0, 0.5, 1.0 })
            {
                ct.ThrowIfCancellationRequested();
                var x = (int)Math.Round(slack * ox, MidpointRounding.AwayFromZero);
                var y = (int)Math.Round(slack * oy, MidpointRounding.AwayFromZero);
                using var subset = new SKBitmap(size, size, SKColorType.Bgra8888, SKAlphaType.Unpremul);
                if (!whole.ExtractSubset(subset, new SKRectI(x, y, x + size, y + size))) continue;
                using var normalized = Resize(subset, CanonicalSize, CanonicalSize);
                if (normalized is null) continue;
                foreach (var candidate in candidates)
                    best = Metrics.Better(best, Compare(normalized, candidate));
            }
        }
        return best;
    }

    private static Metrics Compare(SKBitmap a, SKBitmap b)
    {
        double total = 0;
        var changed = 0;
        var severe = 0;
        var blockSums = new double[CanonicalSize / BlockSize, CanonicalSize / BlockSize];
        for (var y = 0; y < CanonicalSize; y++)
        for (var x = 0; x < CanonicalSize; x++)
        {
            var ac = a.GetPixel(x, y);
            var bc = b.GetPixel(x, y);
            var max = Math.Max(Math.Max(Math.Abs(ac.Red - bc.Red), Math.Abs(ac.Green - bc.Green)),
                Math.Max(Math.Abs(ac.Blue - bc.Blue), Math.Abs(ac.Alpha - bc.Alpha)));
            var mean = (Math.Abs(ac.Red - bc.Red) + Math.Abs(ac.Green - bc.Green)
                + Math.Abs(ac.Blue - bc.Blue) + Math.Abs(ac.Alpha - bc.Alpha)) / 4.0;
            total += mean;
            blockSums[y / BlockSize, x / BlockSize] += mean;
            if (max > 20) changed++;
            if (max > 60) severe++;
        }

        var pixels = CanonicalSize * CanonicalSize;
        var maxBlock = 0.0;
        foreach (var sum in blockSums) maxBlock = Math.Max(maxBlock, sum / (BlockSize * BlockSize));
        return new Metrics(total / pixels, (double)changed / pixels, (double)severe / pixels, maxBlock);
    }

    private static int Score(int ceiling, double mean, double range)
        => Math.Clamp((int)Math.Round(ceiling - (mean / Math.Max(1, range) * 9), MidpointRounding.AwayFromZero), 1, ceiling);

    private static DuplicateVerificationResult Result(DuplicateRelationship relationship, int score)
        => new() { Relationship = relationship, CandidateScore = score };

    private static void DisposeAll(IEnumerable<SKBitmap> bitmaps)
    {
        foreach (var bitmap in bitmaps) bitmap.Dispose();
    }

    private readonly record struct Metrics(double Mean, double ChangedFraction, double SevereFraction, double MaxBlockMean)
    {
        public static Metrics Worst => new(double.MaxValue, 1, 1, double.MaxValue);
        public static Metrics Better(Metrics a, Metrics b)
        {
            // まず局所差を含む総合risk、同値ならmeanを優先。
            var ar = a.Mean + (a.SevereFraction * 120) + (a.MaxBlockMean * 0.15);
            var br = b.Mean + (b.SevereFraction * 120) + (b.MaxBlockMean * 0.15);
            return br < ar ? b : a;
        }
    }
}
