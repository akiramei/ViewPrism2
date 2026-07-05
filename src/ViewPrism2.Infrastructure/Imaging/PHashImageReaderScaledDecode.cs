using Microsoft.Extensions.Logging;
using SkiaSharp;
using ViewPrism2.Core.Services.Similarity;

namespace ViewPrism2.Infrastructure.Imaging;

/// <summary>
/// 画像ファイル → SKCodec scaled-decode(長辺約64px・短辺32px未満へ縮めない) → 32×32 BGRA → pHash。
/// Factory-B 仮説: decode 時 early-shrink により、大きな写真の pHash レイテンシを下げる。
/// SkiaSharp は本クラス(Infrastructure)に閉じ、Core の <see cref="PerceptualHash"/> に
/// バイト列を渡すだけにする(ADR-0002 層規律。Factory-A <see cref="PHashImageReader"/> と同じ分離)。
/// 最終縮小は SKFilterMode.Linear(双線形)を明示し Mipmap なし(Factory-A と同一)。
/// 元画像へは一切書き込まない・一時ファイルも作らない(INV-009)。
/// </summary>
public sealed class PHashImageReaderScaledDecode : IPHashImageReader
{
    /// <summary>中間デコードの目標長辺(px)。短辺はクランプで 32px 未満にしない。</summary>
    private const int IntermediateLongEdge = 64;

    private readonly ILogger<PHashImageReaderScaledDecode>? _logger;

    public PHashImageReaderScaledDecode(ILogger<PHashImageReaderScaledDecode>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// scaled-decode(早期縮小)世代の adapter 識別子(P-09)。full-decode とは pHash 値が異なる。
    /// 8 変種の追加(ECO-048)は identity pHash の絶対値を動かさないため世代交代しない(仕様 §2.10.3)。
    /// </summary>
    public string AdapterId => "skia-scaled-decode-v1";

    /// <summary>8 オリエンテーション変種に対応する(REQ-084 / ECO-048)。</summary>
    public bool SupportsOrientationVariants => true;

    /// <summary>絶対パスの画像から 16hex pHash を計算する。壊れた画像・読み取り失敗は null。</summary>
    public Task<string?> ComputePHashAsync(string absoluteImagePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(absoluteImagePath);
        return Task.Run(() =>
        {
            var pixels = DecodePixels(absoluteImagePath);
            return pixels is null ? null : PerceptualHash.Compute(pixels);
        });
    }

    /// <summary>8 オリエンテーション変種の pHash(仕様 §2.10.1a・[0]=identity)。失敗は null。</summary>
    public Task<IReadOnlyList<string>?> ComputePHashVariantsAsync(string absoluteImagePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(absoluteImagePath);
        return Task.Run(() =>
        {
            var pixels = DecodePixels(absoluteImagePath);
            return pixels is null ? null : (IReadOnlyList<string>?)PHashOrientations.ComputeAll(pixels);
        });
    }

    /// <summary>画像を scaled-decode 経由で 32×32 BGRA へ縮小したピクセル列を返す。失敗は null。</summary>
    private byte[]? DecodePixels(string absoluteImagePath)
    {
        try
        {
            // K-WINFS: 他プロセスのロックと共存する読み取り専用オープン(INV-009)
            using var stream = new FileStream(
                absoluteImagePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            // SKCodec.Create が null を返したら「壊れた画像」(例外を投げない)
            using var codec = SKCodec.Create(stream);
            if (codec is null)
            {
                _logger?.LogWarning("壊れた画像のため pHash を計算できません: {Path}", absoluteImagePath);
                return null;
            }

            // 中間サイズ算出: 長辺を ~64px へ落とすが短辺は 32px 未満にしない
            var (scaledW, scaledH) = GetScaledDecodeSize(codec);
            var scaledInfo = new SKImageInfo(scaledW, scaledH, SKColorType.Bgra8888, SKAlphaType.Unpremul);

            // scaled-decode: フル解像度デコード→縮小を避けて codec に縮小デコードさせる
            using var bitmap = new SKBitmap(scaledInfo);
            var result = codec.GetPixels(scaledInfo, bitmap.GetPixels());
            if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
            {
                _logger?.LogWarning("pHash 用 scaled-decode に失敗しました: {Path}", absoluteImagePath);
                return null;
            }

            // 最終 32×32 縮小処理は Factory-A と共通(双線形・Mipmap なし・決定性)。
            // ただし入力画素が early-shrink で異なるため出力 pHash 値は Factory-A と一致しない(adapter drift)。
            var info = new SKImageInfo(
                PerceptualHash.Size, PerceptualHash.Size, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            using var resized = bitmap.Resize(info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
            if (resized is null)
            {
                _logger?.LogWarning("pHash 用リサイズに失敗しました: {Path}", absoluteImagePath);
                return null;
            }

            // GetPixelSpan() で BGRA 各バイトを一括取得(Factory-A と同一)
            return resized.GetPixelSpan().ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(ex, "pHash 計算に失敗しました: {Path}", absoluteImagePath);
            return null;
        }
    }

    /// <summary>
    /// codec がサポートする縮小寸法のうち、長辺が <see cref="IntermediateLongEdge"/> に
    /// 最も近く、かつ両辺が <see cref="PerceptualHash.Size"/> 以上となるサイズを返す。
    /// 元画像が既に小さい場合は原寸(拡大なし)。
    /// </summary>
    private static (int Width, int Height) GetScaledDecodeSize(SKCodec codec)
    {
        var source = codec.Info;
        var width = source.Width;
        var height = source.Height;
        var longEdge = Math.Max(width, height);

        // 元画像が目標長辺以下なら縮小不要(原寸デコード)
        if (longEdge <= IntermediateLongEdge)
        {
            return (width, height);
        }

        // JPEG は DCT ベースで 1/2, 1/4, 1/8 ... をサポート。
        // 短辺が PerceptualHash.Size より大きい場合のみスケールを制限してクランプする。
        var shortEdge = Math.Min(width, height);
        var longEdgeScale = (float)IntermediateLongEdge / longEdge;
        var shortEdgeMinScale = shortEdge > PerceptualHash.Size
            ? (float)PerceptualHash.Size / shortEdge
            : 1.0f;

        // 短辺が 32px 未満にならない最小 scale を下限として codec に問い合わせる
        var scale = Math.Max(longEdgeScale, shortEdgeMinScale);
        scale = Math.Min(scale, 1.0f); // 拡大禁止

        var scaled = codec.GetScaledDimensions(scale);

        // codec が返した寸法が安全かチェック(codec によっては端数で 32 を下回る場合がある)
        if ((width >= PerceptualHash.Size && scaled.Width < PerceptualHash.Size)
            || (height >= PerceptualHash.Size && scaled.Height < PerceptualHash.Size))
        {
            // 安全側倒し: 原寸デコードにフォールバック
            return (width, height);
        }

        return (scaled.Width, scaled.Height);
    }
}
