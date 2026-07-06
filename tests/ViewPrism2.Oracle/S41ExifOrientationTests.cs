using SkiaSharp;
using ViewPrism2.Infrastructure.Imaging;
using Xunit;

namespace ViewPrism2.Oracle;

/// <summary>
/// S-41: EXIF Orientation の表示系適用(REQ-085 / ECO-049 案 B、EQ-002)。工場非開示。
/// ①正立変換(orientation 2〜8)がテスト側独立実装の画素置換と完全一致・決定的
/// ②実効寸法(5〜8 は W/H 入替・1〜4 不変)
/// ③orientation=6 の合成 JPEG(APP1 手書き挿入)のサムネイルが実効縦横比で生成され、
///   EXIF なし(TopLeft)は従来出力と同一(回帰・S-10 と整合)。
/// </summary>
[Trait("oracle", "S-41")]
public sealed class S41ExifOrientationTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "ViewPrism2.Oracle", "s41-" + Guid.NewGuid().ToString("D"));

    public S41ExifOrientationTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void 正立変換は独立実装と完全一致し決定的_実効寸法は5から8でWH入替()
    {
        const int width = 5;
        const int height = 3;
        var src = SequentialGrid(width, height);

        for (var orientation = 1; orientation <= 8; orientation++)
        {
            var origin = (SKEncodedOrigin)orientation;

            var (actual, aw, ah) = ExifOrientationTransform.ToUprightBgra(src, width, height, origin);
            var (expected, ew, eh) = IndependentUpright(src, width, height, orientation);

            Assert.Equal((ew, eh), (aw, ah));
            Assert.Equal(expected, actual); // 画素置換の完全一致(exact)

            // 決定的: 再計算で同値
            var (again, _, _) = ExifOrientationTransform.ToUprightBgra(src, width, height, origin);
            Assert.Equal(actual, again);

            // 実効寸法契約: 1〜4 不変・5〜8 W/H 入替
            var swapped = orientation >= 5;
            Assert.Equal(
                swapped ? (height, width) : (width, height),
                ExifOrientationTransform.ToEffectiveDimensions(width, height, origin));
        }
    }

    [Fact]
    public async Task orientation6の合成jpegのサムネイルは実効縦横比で生成される()
    {
        var source = Path.Combine(_directory, "exif6.jpg");
        WriteJpegWithOrientation(source, 320, 160, orientation: 6);

        var service = new ThumbnailService(Path.Combine(_directory, "cache"));
        var thumbnailPath = await service.GetOrCreateAsync(source);
        Assert.NotNull(thumbnailPath);

        // 実効 160×320 → inside-fit 128×256(EQ-002: 寸法 ±1px)
        var (w, h) = Decode(thumbnailPath);
        Assert.True(Math.Abs(128 - w) <= 1, $"幅が期待 128(±1)に対し {w}");
        Assert.True(Math.Abs(256 - h) <= 1, $"高さが期待 256(±1)に対し {h}");
    }

    [Fact]
    public async Task EXIFなしのサムネイルは従来どおり_回帰()
    {
        var source = Path.Combine(_directory, "plain.jpg");
        WriteJpegWithOrientation(source, 320, 160, orientation: 1);

        var service = new ThumbnailService(Path.Combine(_directory, "cache"));
        var thumbnailPath = await service.GetOrCreateAsync(source);
        Assert.NotNull(thumbnailPath);

        var (w, h) = Decode(thumbnailPath);
        Assert.True(Math.Abs(256 - w) <= 1, $"幅が期待 256(±1)に対し {w}");
        Assert.True(Math.Abs(128 - h) <= 1, $"高さが期待 128(±1)に対し {h}");
    }

    // ---- 独立実装(オラクル側の正 — 製品コードの変換表に依存しない) ----

    /// <summary>各ピクセルが一意値を持つ BGRA 格子(置換の取り違えを exact 検出)。</summary>
    private static byte[] SequentialGrid(int width, int height)
    {
        var grid = new byte[width * height * 4];
        for (var i = 0; i < width * height; i++)
        {
            grid[i * 4] = (byte)i;            // B
            grid[(i * 4) + 1] = (byte)(i * 7); // G
            grid[(i * 4) + 2] = (byte)(i * 13); // R
            grid[(i * 4) + 3] = 255;
        }

        return grid;
    }

    /// <summary>EXIF Orientation 1〜8 の正立化(独立実装)。dst(x,y) ← src(sx,sy)。</summary>
    private static (byte[] Bgra, int Width, int Height) IndependentUpright(
        byte[] src, int width, int height, int orientation)
    {
        var swap = orientation >= 5;
        var dw = swap ? height : width;
        var dh = swap ? width : height;
        var dst = new byte[src.Length];
        for (var y = 0; y < dh; y++)
        {
            for (var x = 0; x < dw; x++)
            {
                var (sx, sy) = orientation switch
                {
                    2 => (width - 1 - x, y),               // flipH
                    3 => (width - 1 - x, height - 1 - y),  // rotate180
                    4 => (x, height - 1 - y),              // flipV
                    5 => (y, x),                           // transpose
                    6 => (y, height - 1 - x),              // rotate90(時計回り)
                    7 => (width - 1 - y, height - 1 - x),  // transverse
                    8 => (width - 1 - y, x),               // rotate270
                    _ => (x, y),                           // 1: identity
                };
                Array.Copy(src, ((sy * width) + sx) * 4, dst, ((y * dw) + x) * 4, 4);
            }
        }

        return (dst, dw, dh);
    }

    private static (int Width, int Height) Decode(string path)
    {
        using var stream = File.OpenRead(path);
        using var codec = SKCodec.Create(stream);
        Assert.NotNull(codec);
        return (codec.Info.Width, codec.Info.Height);
    }

    /// <summary>非対称 JPEG を書き出し、orientation>1 なら SOI 直後へ APP1(EXIF)を挿入(独立実装)。</summary>
    private static void WriteJpegWithOrientation(string path, int width, int height, int orientation)
    {
        using var bmp = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var v = (byte)(x * 255 / width);
                bmp.SetPixel(x, y, new SKColor(v, v, v));
            }
        }

        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        var jpeg = data.ToArray();
        File.WriteAllBytes(path, orientation > 1 ? InsertExif(jpeg, (ushort)orientation) : jpeg);
    }

    private static byte[] InsertExif(byte[] jpeg, ushort orientation)
    {
        byte[] tiff =
        [
            0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00,
            0x01, 0x00,
            0x12, 0x01, 0x03, 0x00, 0x01, 0x00, 0x00, 0x00,
            (byte)(orientation & 0xFF), (byte)(orientation >> 8), 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
        ];
        byte[] exifHeader = [0x45, 0x78, 0x69, 0x66, 0x00, 0x00]; // "Exif\0\0"
        var segmentLength = exifHeader.Length + tiff.Length + 2;

        using var ms = new MemoryStream();
        ms.Write(jpeg, 0, 2);
        ms.WriteByte(0xFF);
        ms.WriteByte(0xE1);
        ms.WriteByte((byte)(segmentLength >> 8));
        ms.WriteByte((byte)(segmentLength & 0xFF));
        ms.Write(exifHeader);
        ms.Write(tiff);
        ms.Write(jpeg, 2, jpeg.Length - 2);
        return ms.ToArray();
    }
}
