using SkiaSharp;
using ViewPrism2.Infrastructure.Imaging;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// CP-THUMB-007: サムネイル生成・キャッシュ・失敗時動作が REQ-040 と一致する(L2)。
/// 出力ファイルのデコード結果(寸法・形式)とファイル存在・再生成有無を検査する。
/// 画像フィクスチャはテスト内生成(1920x1080 jpg / 100x50 png / 壊れた jpg / gif / bmp / webp)。
/// </summary>
[Trait("cp", "CP-THUMB-007")]
public sealed class CpThumb007Tests : IDisposable
{
    private readonly string _root;
    private readonly string _cacheDir;
    private readonly ThumbnailService _service;

    public CpThumb007Tests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ViewPrism2.Tests", Guid.NewGuid().ToString("D"));
        _cacheDir = Path.Combine(_root, "thumbnails");
        Directory.CreateDirectory(_root);
        _service = new ThumbnailService(_cacheDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static (int Width, int Height, SKEncodedImageFormat Format) DecodeInfo(string path)
    {
        using var codec = SKCodec.Create(path);
        Assert.NotNull(codec);
        return (codec.Info.Width, codec.Info.Height, codec.EncodedFormat);
    }

    [Fact]
    public async Task Jpg1920x1080は256x144のJpegになる()
    {
        var source = Path.Combine(_root, "wide.jpg");
        ImageFixtures.WriteEncoded(source, 1920, 1080, SKEncodedImageFormat.Jpeg);

        var thumb = await _service.GetOrCreateAsync(source);

        Assert.NotNull(thumb);
        Assert.EndsWith(".jpg", thumb, StringComparison.Ordinal);
        var (w, h, format) = DecodeInfo(thumb);
        Assert.Equal(SKEncodedImageFormat.Jpeg, format);
        // 長辺 ≦256・アスペクト比維持(±1px)
        Assert.InRange(w, 255, 256);
        Assert.InRange(h, 143, 145);
    }

    [Fact]
    public async Task Png100x50は拡大されずPngのまま_FMEA012()
    {
        var source = Path.Combine(_root, "small.png");
        ImageFixtures.WriteEncoded(source, 100, 50, SKEncodedImageFormat.Png);

        var thumb = await _service.GetOrCreateAsync(source);

        Assert.NotNull(thumb);
        Assert.EndsWith(".png", thumb, StringComparison.Ordinal);
        var (w, h, format) = DecodeInfo(thumb);
        Assert.Equal(SKEncodedImageFormat.Png, format);
        Assert.Equal(100, w); // 拡大しない(REQ-040)
        Assert.Equal(50, h);
    }

    [Fact]
    public async Task GifBmpWebpはJpeg出力になる()
    {
        var gif = Path.Combine(_root, "a.gif");
        var bmp = Path.Combine(_root, "b.bmp");
        var webp = Path.Combine(_root, "c.webp");
        ImageFixtures.WriteGif(gif);
        ImageFixtures.WriteBmp(bmp, 64, 32);
        ImageFixtures.WriteEncoded(webp, 300, 300, SKEncodedImageFormat.Webp);

        foreach (var source in new[] { gif, bmp, webp })
        {
            var thumb = await _service.GetOrCreateAsync(source);
            Assert.NotNull(thumb);
            Assert.EndsWith(".jpg", thumb, StringComparison.Ordinal);
            var (_, _, format) = DecodeInfo(thumb);
            Assert.Equal(SKEncodedImageFormat.Jpeg, format);
        }
    }

    [Fact]
    public async Task 縦横比維持で縮小し丸めはHalfAwayFromZero最小1px()
    {
        // 2000x1 → scale=0.128 → 高さ 0.128 → round 0 → 最小 1px(K-SKIA)
        var line = Path.Combine(_root, "line.png");
        ImageFixtures.WriteEncoded(line, 2000, 1, SKEncodedImageFormat.Png);

        var thumb = await _service.GetOrCreateAsync(line);

        Assert.NotNull(thumb);
        var (w, h, _) = DecodeInfo(thumb);
        Assert.Equal(256, w);
        Assert.Equal(1, h);

        // 511x100 → scale=256/511 → 高さ 50.097…→ 50(half away from zero)
        var odd = Path.Combine(_root, "odd.jpg");
        ImageFixtures.WriteEncoded(odd, 511, 100, SKEncodedImageFormat.Jpeg);
        var oddThumb = await _service.GetOrCreateAsync(odd);
        Assert.NotNull(oddThumb);
        var (ow, oh, _) = DecodeInfo(oddThumb);
        Assert.Equal(256, ow);
        Assert.Equal(50, oh);
    }

    [Fact]
    public async Task キャッシュヒットで再生成しない()
    {
        var source = Path.Combine(_root, "cached.jpg");
        ImageFixtures.WriteEncoded(source, 800, 600, SKEncodedImageFormat.Jpeg);

        var first = await _service.GetOrCreateAsync(source);
        Assert.NotNull(first);
        var mtime = File.GetLastWriteTimeUtc(first);

        await Task.Delay(50, TestContext.Current.CancellationToken);
        var second = await _service.GetOrCreateAsync(source);

        Assert.Equal(first, second);
        Assert.Equal(mtime, File.GetLastWriteTimeUtc(second!)); // mtime 不変=再生成なし
    }

    [Fact]
    public async Task キャッシュキーはMD5小文字絶対パスで大文字小文字を同一視する()
    {
        var source = Path.Combine(_root, "Key.JPG");
        ImageFixtures.WriteEncoded(source, 300, 200, SKEncodedImageFormat.Jpeg);

        var lower = await _service.GetOrCreateAsync(source.ToLowerInvariant());
        var upper = await _service.GetOrCreateAsync(source.ToUpperInvariant());

        Assert.NotNull(lower);
        Assert.Equal(lower, upper); // MD5(小文字絶対パス)で同一キー(REQ-040)
    }

    [Fact]
    public async Task 壊れたJpgはNullでキャッシュ記録なし_FMEA012()
    {
        var source = Path.Combine(_root, "broken.jpg");
        ImageFixtures.WriteBroken(source);

        var thumb = await _service.GetOrCreateAsync(source);

        Assert.Null(thumb); // 例外なし・null
        Assert.False(File.Exists(_service.GetCachePath(source))); // キャッシュへ記録しない

        // 次回表示時に再試行される(キャッシュなしのため再度 null)
        Assert.Null(await _service.GetOrCreateAsync(source));
    }

    [Fact]
    public async Task 破損キャッシュは削除して再生成する()
    {
        var source = Path.Combine(_root, "regen.jpg");
        ImageFixtures.WriteEncoded(source, 640, 480, SKEncodedImageFormat.Jpeg);

        var first = await _service.GetOrCreateAsync(source);
        Assert.NotNull(first);

        File.WriteAllBytes(first, []); // 0 バイト化(読み取り不能キャッシュ)

        var second = await _service.GetOrCreateAsync(source);

        Assert.Equal(first, second);
        Assert.True(new FileInfo(second!).Length > 0); // 削除+再生成済み
        var (w, _, _) = DecodeInfo(second!);
        Assert.Equal(256, w);
    }

    [Fact]
    public async Task 解像度取得はフルデコードなしで寸法を返す()
    {
        var source = Path.Combine(_root, "dim.jpg");
        ImageFixtures.WriteEncoded(source, 1920, 1080, SKEncodedImageFormat.Jpeg);

        Assert.Equal((1920, 1080), await _service.GetDimensionsAsync(source));

        var broken = Path.Combine(_root, "dim-broken.jpg");
        ImageFixtures.WriteBroken(broken);
        Assert.Null(await _service.GetDimensionsAsync(broken)); // 失敗時 null・例外なし
    }
}
