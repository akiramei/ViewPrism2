using SkiaSharp;
using ViewPrism2.Infrastructure.Imaging;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// CP-THUMB-007 拡張(ECO-049 / REQ-085): EXIF Orientation の表示系適用。
/// EXIF Orientation=6(90° CW 表示指示)の JPEG フィクスチャはテスト側で合成する
/// (SkiaSharp でエンコード後、SOI 直後へ APP1(Exif/TIFF/IFD0: tag 0x0112=6)を挿入 — 独立実装)。
/// </summary>
[Trait("cp", "CP-THUMB-007")]
public sealed class CpThumb049ExifTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "ViewPrism2.Tests", Guid.NewGuid().ToString("D"));

    public CpThumb049ExifTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public async Task 対照_EXIFなしjpgのサムネと寸法は従来どおり()
    {
        var source = Path.Combine(_dir, "plain.jpg");
        WriteJpeg(source, 400, 200, orientation: 1);

        var service = new ThumbnailService(Path.Combine(_dir, "cache"));
        var thumb = await service.GetOrCreateAsync(source);
        Assert.NotNull(thumb);
        Assert.Equal((256, 128), DecodeDims(thumb)); // 長辺 256 へ inside-fit(REQ-040)

        Assert.Equal((400, 200), await service.GetDimensionsAsync(source));
    }

    [Fact]
    public async Task EXIF回転6のjpgはサムネが正立_縦長になる()
    {
        var source = Path.Combine(_dir, "exif6.jpg");
        WriteJpeg(source, 400, 200, orientation: 6);
        AssertFixtureHasOrigin(source, SKEncodedOrigin.RightTop); // 前提較正: Skia が APP1 を読めている

        var service = new ThumbnailService(Path.Combine(_dir, "cache"));
        var thumb = await service.GetOrCreateAsync(source);
        Assert.NotNull(thumb);

        // 実効 200×400(90° CW)→ inside-fit 128×256(REQ-085: 表示系は EXIF 適用)
        Assert.Equal((128, 256), DecodeDims(thumb));
    }

    [Fact]
    public async Task EXIF回転6のjpgの寸法メタは実効寸法を返す()
    {
        var source = Path.Combine(_dir, "exif6-dims.jpg");
        WriteJpeg(source, 400, 200, orientation: 6);
        AssertFixtureHasOrigin(source, SKEncodedOrigin.RightTop);

        var service = new ThumbnailService(Path.Combine(_dir, "cache"));
        // Orientation 5〜8 は W/H 入替の実効寸法(REQ-085 案 B)
        Assert.Equal((200, 400), await service.GetDimensionsAsync(source));
    }

    [Fact]
    public async Task キャッシュ世代移行_旧世代ファイルは参照されず新世代で正立生成される()
    {
        var source = Path.Combine(_dir, "exif6-gen.jpg");
        WriteJpeg(source, 400, 200, orientation: 6);

        var cacheDir = Path.Combine(_dir, "cache");
        Directory.CreateDirectory(cacheDir);

        // 旧世代(ECO-049 以前)のキャッシュ名 = MD5(小文字絶対パス).jpg — 横倒し想定のダミーを事前配置
        var oldKey = Convert.ToHexStringLower(
            System.Security.Cryptography.MD5.HashData(
                System.Text.Encoding.UTF8.GetBytes(source.ToLowerInvariant())));
        var oldPath = Path.Combine(cacheDir, oldKey + ".jpg");
        File.WriteAllBytes(oldPath, [0x00]); // 中身は読まれないことが期待(参照されたら IsReadableImage で落ちる)

        var service = new ThumbnailService(cacheDir);
        Assert.EndsWith("-v2.jpg", service.GetCachePath(source), StringComparison.Ordinal); // 世代サフィックス

        var thumb = await service.GetOrCreateAsync(source);
        Assert.NotNull(thumb);
        Assert.NotEqual(oldPath, thumb);                 // 旧世代を返さない
        Assert.Equal((128, 256), DecodeDims(thumb));     // 新世代は正立で生成
    }

    [Fact]
    public void 正立ローダ_EXIF回転6は正立ピクセルを返し向きと内容が一致する()
    {
        var source = Path.Combine(_dir, "exif6-loader.jpg");
        WriteJpeg(source, 400, 200, orientation: 6);
        AssertFixtureHasOrigin(source, SKEncodedOrigin.RightTop);

        var oriented = OrientedImageLoader.LoadOrientedOrNull(source);
        Assert.NotNull(oriented);
        Assert.Equal(200, oriented.Width);  // 実効寸法(90° CW)
        Assert.Equal(400, oriented.Height);

        // 内容検査: 元画像の左上明矩形(x<100,y<50)は 90° CW 後は右上域へ移る
        // dst(199,0) ← src(0,0)=白 / dst(0,0) ← src(0,199)=勾配左端=暗
        Assert.True(PixelB(oriented, x: 199, y: 0) > 200, "右上が明でない(回転が適用されていない)");
        Assert.True(PixelB(oriented, x: 0, y: 0) < 60, "左上が暗でない(回転の向きが誤り)");
    }

    [Fact]
    public void 正立ローダ_EXIFなしはnull_従来の直読経路を変えない()
    {
        var source = Path.Combine(_dir, "plain-loader.jpg");
        WriteJpeg(source, 400, 200, orientation: 1);

        Assert.Null(OrientedImageLoader.LoadOrientedOrNull(source)); // TopLeft = 高速経路(挙動不変)
    }

    // ---- ヘルパ(独立実装 — 製品コードの EXIF 処理に依存しない) ----

    private static byte PixelB(OrientedImagePixels pixels, int x, int y)
        => pixels.Bgra8888Premul[((y * pixels.Width) + x) * 4]; // B チャネル(グレースケール画像なので代表)

    private static void AssertFixtureHasOrigin(string path, SKEncodedOrigin expected)
    {
        using var stream = File.OpenRead(path);
        using var codec = SKCodec.Create(stream);
        Assert.NotNull(codec);
        Assert.Equal(expected, codec.EncodedOrigin);
    }

    private static (int Width, int Height) DecodeDims(string path)
    {
        using var stream = File.OpenRead(path);
        using var codec = SKCodec.Create(stream);
        Assert.NotNull(codec);
        return (codec.Info.Width, codec.Info.Height);
    }

    /// <summary>
    /// 非対称パターンの JPEG を書き出す。orientation>1 なら SOI 直後へ APP1(EXIF)を挿入する。
    /// </summary>
    private static void WriteJpeg(string path, int width, int height, int orientation)
    {
        using var bmp = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var v = (byte)(x * 255 / width);
                if (x < width / 4 && y < height / 4)
                {
                    v = 255; // 左上の明矩形(非対称)
                }

                bmp.SetPixel(x, y, new SKColor(v, v, v));
            }
        }

        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        var jpeg = data.ToArray();

        var bytes = orientation > 1 ? InsertExifOrientation(jpeg, (ushort)orientation) : jpeg;
        File.WriteAllBytes(path, bytes);
    }

    /// <summary>
    /// JPEG の SOI(FFD8)直後へ APP1(Exif: TIFF リトルエンディアン・IFD0 に 0x0112=orientation のみ)を挿入。
    /// </summary>
    private static byte[] InsertExifOrientation(byte[] jpeg, ushort orientation)
    {
        // TIFF: "II" 0x002A + IFD0 オフセット 8 → IFD0: エントリ 1 件(tag 0x0112, SHORT, count 1, value)→ 次 IFD なし
        byte[] tiff =
        [
            0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00, // II*\0 + IFD0 offset=8
            0x01, 0x00,                                     // エントリ数 1
            0x12, 0x01, 0x03, 0x00, 0x01, 0x00, 0x00, 0x00, // tag 0x0112, type SHORT, count 1
            (byte)(orientation & 0xFF), (byte)(orientation >> 8), 0x00, 0x00, // 値(下位詰め)
            0x00, 0x00, 0x00, 0x00,                         // 次 IFD なし
        ];
        byte[] exifHeader = [0x45, 0x78, 0x69, 0x66, 0x00, 0x00]; // "Exif\0\0"
        var payloadLength = exifHeader.Length + tiff.Length;
        var segmentLength = payloadLength + 2; // 長さフィールド自身を含む

        using var ms = new MemoryStream();
        ms.Write(jpeg, 0, 2); // SOI (FFD8)
        ms.WriteByte(0xFF);
        ms.WriteByte(0xE1); // APP1
        ms.WriteByte((byte)(segmentLength >> 8));
        ms.WriteByte((byte)(segmentLength & 0xFF));
        ms.Write(exifHeader);
        ms.Write(tiff);
        ms.Write(jpeg, 2, jpeg.Length - 2);
        return ms.ToArray();
    }
}
