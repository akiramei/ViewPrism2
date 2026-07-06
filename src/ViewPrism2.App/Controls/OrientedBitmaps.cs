using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ViewPrism2.Infrastructure.Imaging;

namespace ViewPrism2.App.Controls;

/// <summary>
/// 原本フルサイズ画像の EXIF 正立読込(REQ-085 / ECO-049)。
/// Orientation=TopLeft(または判定不能)は従来の直読(Avalonia Bitmap)=高速経路・挙動不変。
/// 正立が必要な場合のみ Infrastructure のピクセル列(SkiaSharp は Infrastructure に閉じる —
/// ADR-0002)から WriteableBitmap を組む。UI スレッド外での実行を想定(呼び出し側が Task.Run)。
/// 失敗時の例外面は従来の new Bitmap(path) と同一(呼び出し側の catch 節を変えない)。
/// </summary>
internal static class OrientedBitmaps
{
    public static Bitmap Load(string path)
    {
        var oriented = OrientedImageLoader.LoadOrientedOrNull(path);
        if (oriented is null)
        {
            return new Bitmap(path); // TopLeft・判定不能: 従来経路(壊れた画像の失敗もここで従来どおり発生)
        }

        var bitmap = new WriteableBitmap(
            new PixelSize(oriented.Width, oriented.Height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);
        using var buffer = bitmap.Lock();
        var rowBytes = oriented.Width * 4;
        for (var y = 0; y < oriented.Height; y++)
        {
            // 行パディング差(RowBytes)を考慮して行単位でコピー
            Marshal.Copy(oriented.Bgra8888Premul, y * rowBytes, buffer.Address + (y * buffer.RowBytes), rowBytes);
        }

        return bitmap;
    }
}
