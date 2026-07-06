using System.Runtime.InteropServices;
using SkiaSharp;

namespace ViewPrism2.Infrastructure.Imaging;

/// <summary>
/// EXIF Orientation(SKEncodedOrigin)の正立化変換(REQ-085 / ECO-049・仕様 §2.5・K-SKIA v4.1)。
/// SKEncodedOrigin の番号は EXIF Orientation 値と同一(TopLeft=1 〜 LeftBottom=8)。
/// 変換は D4 の添字置換のみ(PHashOrientations と同じ変換族・決定的・S-41)。
/// 適用は表示系に限る — pHash 入力には適用しない(adapter 世代 P-09 を発動させない — 仕様 §2.5)。
/// </summary>
public static class ExifOrientationTransform
{
    private const int BytesPerPixel = 4;

    /// <summary>正立化に変換が必要か(TopLeft 以外)。</summary>
    public static bool RequiresTransform(SKEncodedOrigin origin) => origin != SKEncodedOrigin.TopLeft;

    /// <summary>Orientation 5〜8 は 90° 系=実効寸法が W/H 入替になる(REQ-085)。</summary>
    public static bool SwapsDimensions(SKEncodedOrigin origin)
        => origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;

    /// <summary>実効寸法(表示上の幅・高さ)。1〜4 は不変・5〜8 は W/H 入替。</summary>
    public static (int Width, int Height) ToEffectiveDimensions(int width, int height, SKEncodedOrigin origin)
        => SwapsDimensions(origin) ? (height, width) : (width, height);

    /// <summary>
    /// BGRA8888 ピクセル列を正立化した (ピクセル列, 幅, 高さ) を返す(S-41 契約の正)。
    /// dst の各ピクセル (x,y) に対応する src 座標 (sx,sy):
    ///   2=flipH 3=rotate180 4=flipV 5=transpose 6=rotate90(時計回り) 7=transverse 8=rotate270。
    /// </summary>
    public static (byte[] Bgra, int Width, int Height) ToUprightBgra(
        ReadOnlySpan<byte> bgra, int width, int height, SKEncodedOrigin origin)
    {
        if (bgra.Length != width * height * BytesPerPixel)
        {
            throw new ArgumentException(
                $"BGRA バイト列は {width}x{height}x{BytesPerPixel}={width * height * BytesPerPixel} バイトである必要があります。",
                nameof(bgra));
        }

        if (!RequiresTransform(origin))
        {
            return (bgra.ToArray(), width, height);
        }

        var (dstWidth, dstHeight) = ToEffectiveDimensions(width, height, origin);
        var dst = new byte[bgra.Length];
        for (var y = 0; y < dstHeight; y++)
        {
            for (var x = 0; x < dstWidth; x++)
            {
                var (sx, sy) = origin switch
                {
                    SKEncodedOrigin.TopRight => (width - 1 - x, y),                  // 2: flipH(左右反転)
                    SKEncodedOrigin.BottomRight => (width - 1 - x, height - 1 - y), // 3: rotate180
                    SKEncodedOrigin.BottomLeft => (x, height - 1 - y),              // 4: flipV(上下反転)
                    SKEncodedOrigin.LeftTop => (y, x),                              // 5: transpose(主対角転置)
                    SKEncodedOrigin.RightTop => (y, height - 1 - x),                // 6: rotate90(時計回り)
                    SKEncodedOrigin.RightBottom => (width - 1 - y, height - 1 - x), // 7: transverse(反対角転置)
                    SKEncodedOrigin.LeftBottom => (width - 1 - y, x),               // 8: rotate270
                    _ => (x, y),                                                    // 防御(TopLeft は上で返済)
                };

                var srcOffset = ((sy * width) + sx) * BytesPerPixel;
                var dstOffset = ((y * dstWidth) + x) * BytesPerPixel;
                bgra.Slice(srcOffset, BytesPerPixel).CopyTo(dst.AsSpan(dstOffset, BytesPerPixel));
            }
        }

        return (dst, dstWidth, dstHeight);
    }

    /// <summary>
    /// SKBitmap を正立化した新しい SKBitmap を返す(呼び出し側が破棄。入力は破棄しない)。
    /// 変換表は <see cref="ToUprightBgra"/> に一本化(実装分岐を作らない)。
    /// Bgra8888 への正規化コピーに失敗した場合は null(呼び出し側は未変換で継続=クラッシュさせない)。
    /// </summary>
    public static SKBitmap? ToUpright(SKBitmap source, SKEncodedOrigin origin)
    {
        SKBitmap? normalized = null;
        try
        {
            var bgraSource = source;
            if (source.ColorType != SKColorType.Bgra8888)
            {
                normalized = source.Copy(SKColorType.Bgra8888);
                if (normalized is null)
                {
                    return null;
                }

                bgraSource = normalized;
            }

            var (bytes, width, height) = ToUprightBgra(
                bgraSource.GetPixelSpan(), bgraSource.Width, bgraSource.Height, origin);

            var dst = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, bgraSource.AlphaType));
            Marshal.Copy(bytes, 0, dst.GetPixels(), bytes.Length);
            return dst;
        }
        finally
        {
            normalized?.Dispose();
        }
    }
}
