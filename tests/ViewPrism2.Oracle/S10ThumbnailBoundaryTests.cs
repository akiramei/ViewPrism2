using SkiaSharp;
using ViewPrism2.Infrastructure.Imaging;
using Xunit;

namespace ViewPrism2.Oracle;

/// <summary>
/// S-10: サムネイル境界(spec §2.5 REQ-040 / K-SKIA、EQ-002、L2)。
/// 100x50 png(小画像)→ 100x50 png(拡大なし)/ 256x256 ちょうどの jpg → 256x256 jpg(scale=1.0)。
/// EQ-002: 出力を再デコードして寸法(±1px)・エンコード形式のみ比較する。
/// </summary>
[Trait("oracle", "S-10")]
public sealed class S10ThumbnailBoundaryTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "ViewPrism2.Oracle", "s10-" + Guid.NewGuid().ToString("D"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public async Task 小画像100x50pngは拡大されず100x50pngのまま()
    {
        var sourcePath = Path.Combine(_directory, "small.png");
        OracleImages.WriteEncoded(sourcePath, 100, 50, SKEncodedImageFormat.Png, new SKColor(0x3B, 0x82, 0xF6));

        var service = new ThumbnailService(Path.Combine(_directory, "cache"));
        var thumbnailPath = await service.GetOrCreateAsync(sourcePath);

        Assert.NotNull(thumbnailPath);
        var (width, height, format) = Decode(thumbnailPath);
        AssertDimension(100, width);
        AssertDimension(50, height);
        Assert.Equal(SKEncodedImageFormat.Png, format); // PNG 入力 → PNG 出力
    }

    [Fact]
    public async Task 長辺256ちょうどのjpgはscale1で256x256jpgのまま()
    {
        var sourcePath = Path.Combine(_directory, "exact.jpg");
        OracleImages.WriteEncoded(sourcePath, 256, 256, SKEncodedImageFormat.Jpeg, new SKColor(0x16, 0xA3, 0x4A));

        var service = new ThumbnailService(Path.Combine(_directory, "cache"));
        var thumbnailPath = await service.GetOrCreateAsync(sourcePath);

        Assert.NotNull(thumbnailPath);
        var (width, height, format) = Decode(thumbnailPath);
        AssertDimension(256, width);
        AssertDimension(256, height);
        Assert.Equal(SKEncodedImageFormat.Jpeg, format); // PNG 以外 → JPEG
    }

    /// <summary>EQ-002: 寸法は ±1px を許容して比較する。</summary>
    private static void AssertDimension(int expected, int actual)
    {
        Assert.True(Math.Abs(expected - actual) <= 1,
            $"寸法が期待 {expected}(±1px)に対し {actual} でした。");
    }

    private static (int Width, int Height, SKEncodedImageFormat Format) Decode(string path)
    {
        using var stream = File.OpenRead(path);
        using var codec = SKCodec.Create(stream);
        Assert.NotNull(codec);
        return (codec.Info.Width, codec.Info.Height, codec.EncodedFormat);
    }
}
