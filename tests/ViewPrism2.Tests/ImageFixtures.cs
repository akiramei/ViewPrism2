using SkiaSharp;

namespace ViewPrism2.Tests;

/// <summary>
/// 画像フィクスチャ生成(M-HARNESS-015 / CP-THUMB-007)。
/// jpg/png/webp は SkiaSharp でテスト内生成。SkiaSharp は GIF/BMP のエンコードを
/// サポートしないため、gif/bmp のみ最小のバイト列を直接生成する(SKBitmap.Decode 可能)。
/// </summary>
internal static class ImageFixtures
{
    /// <summary>単色画像を生成して path へ保存する(jpg/png/webp)。</summary>
    public static void WriteEncoded(string path, int width, int height, SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(new SKColor(0x3B, 0x82, 0xF6));
            using var paint = new SKPaint { Color = new SKColor(0xDC, 0x26, 0x26) };
            canvas.DrawRect(0, 0, width / 2f, height / 2f, paint);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90) ?? throw new InvalidOperationException($"encode 失敗: {format}");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        data.SaveTo(stream);
    }

    /// <summary>1x1 の最小 GIF89a。</summary>
    public static void WriteGif(string path)
    {
        byte[] gif =
        [
            0x47, 0x49, 0x46, 0x38, 0x39, 0x61,             // GIF89a
            0x01, 0x00, 0x01, 0x00, 0x80, 0x00, 0x00,       // 1x1, GCT 2 色
            0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF,             // パレット
            0x2C, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, // Image Descriptor
            0x02, 0x02, 0x44, 0x01, 0x00,                   // LZW data
            0x3B,                                           // Trailer
        ];
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, gif);
    }

    /// <summary>幅×高さの 24bit 無圧縮 BMP。</summary>
    public static void WriteBmp(string path, int width, int height)
    {
        var rowSize = (width * 3 + 3) / 4 * 4;
        var dataSize = rowSize * height;
        var fileSize = 54 + dataSize;
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((byte)'B');
        w.Write((byte)'M');
        w.Write(fileSize);
        w.Write(0);
        w.Write(54);            // pixel data offset
        w.Write(40);            // BITMAPINFOHEADER
        w.Write(width);
        w.Write(height);
        w.Write((short)1);      // planes
        w.Write((short)24);     // bpp
        w.Write(0);             // BI_RGB
        w.Write(dataSize);
        w.Write(2835);
        w.Write(2835);
        w.Write(0);
        w.Write(0);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                w.Write((byte)0xF6);
                w.Write((byte)0x82);
                w.Write((byte)0x3B);
            }

            for (var pad = width * 3; pad < rowSize; pad++)
            {
                w.Write((byte)0);
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, ms.ToArray());
    }

    /// <summary>壊れた画像(デコード不能なバイト列)。</summary>
    public static void WriteBroken(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, "this is not an image"u8.ToArray());
    }
}
