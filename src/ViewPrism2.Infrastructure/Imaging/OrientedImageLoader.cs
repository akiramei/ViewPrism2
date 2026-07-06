using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace ViewPrism2.Infrastructure.Imaging;

/// <summary>正立化済みフルサイズ画像のピクセル列(REQ-085)。App 層は SkiaSharp 型に触れない(ADR-0002)。</summary>
public sealed record OrientedImagePixels
{
    /// <summary>BGRA8888(Premul)・行優先・行パディングなし(Width×4 バイト/行)。</summary>
    public required byte[] Bgra8888Premul { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }
}

/// <summary>
/// ビューア向けの EXIF 正立フルサイズ読込(REQ-085 / ECO-049・仕様 §2.5)。
/// Orientation=TopLeft(または判定不能)は null を返し「従来の直読でよい」を示す
/// (高速経路 — EXIF なし画像の挙動・性能を一切変えない)。
/// SkiaSharp は本クラス(Infrastructure)に閉じ、App へはピクセル列を渡す(ADR-0002 層規律)。
/// 元画像へは一切書き込まない(INV-009)。
/// </summary>
public static class OrientedImageLoader
{
    /// <summary>
    /// EXIF 正立化が必要な画像のみ、正立化済みピクセル列を返す。TopLeft・読込不能は null
    /// (呼び出し側は従来経路へフォールバック — 壊れた画像の失敗表示も従来経路が担う)。
    /// </summary>
    public static OrientedImagePixels? LoadOrientedOrNull(string absoluteImagePath, ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(absoluteImagePath);
        try
        {
            // K-WINFS: 他プロセスのロックと共存する読み取り専用オープン(INV-009)
            using var stream = new FileStream(
                absoluteImagePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var codec = SKCodec.Create(stream);
            if (codec is null)
            {
                return null; // 壊れた画像 → 従来経路(Avalonia 直読)の失敗表示に委ねる
            }

            var origin = codec.EncodedOrigin;
            if (!ExifOrientationTransform.RequiresTransform(origin))
            {
                return null; // TopLeft: 従来の直読(高速経路・挙動不変)
            }

            // 表示用に Bgra8888/Premul で全解像度デコード(Avalonia WriteableBitmap と同形式)
            var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var bitmap = new SKBitmap(info);
            var result = codec.GetPixels(info, bitmap.GetPixels());
            if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
            {
                logger?.LogWarning("EXIF 正立デコードに失敗しました(従来経路で継続): {Path}", absoluteImagePath);
                return null;
            }

            var (bytes, width, height) = ExifOrientationTransform.ToUprightBgra(
                bitmap.GetPixelSpan(), info.Width, info.Height, origin);
            return new OrientedImagePixels { Bgra8888Premul = bytes, Width = width, Height = height };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger?.LogWarning(ex, "EXIF 正立読込に失敗しました(従来経路で継続): {Path}", absoluteImagePath);
            return null;
        }
    }
}
