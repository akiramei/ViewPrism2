using Microsoft.Extensions.Logging;
using SkiaSharp;
using ViewPrism2.Core.Services.Similarity;

namespace ViewPrism2.Infrastructure.Imaging;

/// <summary>
/// 画像ファイル → 32×32 BGRA → pHash(M-SIMSEARCH-021、K-SKIA v3.0)。
/// SkiaSharp は本クラス(Infrastructure)に閉じ、Core の <see cref="PerceptualHash"/> に
/// バイト列を渡すだけにする(ADR-0002 層規律。ThumbnailService と同じ分離)。
/// 縮小は SKFilterMode.Linear(双線形)を明示し Mipmap なし(決定性 — ADR-0008)。
/// 元画像へは一切書き込まない・一時ファイルも作らない(INV-009)。
/// </summary>
public sealed class PHashImageReader : IPHashImageReader
{
    private readonly ILogger<PHashImageReader>? _logger;

    public PHashImageReader(ILogger<PHashImageReader>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>full-decode 世代の adapter 識別子(P-09)。scaled-decode(早期縮小)とは pHash 値が異なる。</summary>
    public string AdapterId => "skia-full-decode-v1";

    /// <summary>絶対パスの画像から 16hex pHash を計算する。壊れた画像・読み取り失敗は null。</summary>
    public Task<string?> ComputePHashAsync(string absoluteImagePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(absoluteImagePath);
        return Task.Run(() => Compute(absoluteImagePath));
    }

    private string? Compute(string absoluteImagePath)
    {
        try
        {
            // K-WINFS: 他プロセスのロックと共存する読み取り専用オープン(INV-009)
            using var stream = new FileStream(
                absoluteImagePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            // K-SKIA: SKBitmap.Decode が null を返したら「壊れた画像」(例外を投げない)
            using var bitmap = SKBitmap.Decode(stream);
            if (bitmap is null)
            {
                _logger?.LogWarning("壊れた画像のため pHash を計算できません: {Path}", absoluteImagePath);
                return null;
            }

            // K-SKIA v3.0: 32×32 BGRA8888 Unpremul へ双線形縮小(Mipmap なし)。決定性のため版を exact ピン。
            var info = new SKImageInfo(
                PerceptualHash.Size, PerceptualHash.Size, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            using var resized = bitmap.Resize(info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
            if (resized is null)
            {
                _logger?.LogWarning("pHash 用リサイズに失敗しました: {Path}", absoluteImagePath);
                return null;
            }

            // GetPixelSpan() で BGRA 各バイトを一括取得(GetPixel ループは遅い)。アルファは無視
            var pixels = resized.GetPixelSpan();
            return PerceptualHash.Compute(pixels);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(ex, "pHash 計算に失敗しました: {Path}", absoluteImagePath);
            return null;
        }
    }
}
