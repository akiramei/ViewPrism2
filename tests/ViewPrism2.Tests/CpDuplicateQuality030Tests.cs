using SkiaSharp;
using ViewPrism2.App.ViewModels;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services.Similarity;
using ViewPrism2.Infrastructure.Imaging;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// CP-DUPQUALITY-030 / ECO-067: 一般的な見た目の類似と、同一原画像由来の重複関係を分離する。
/// fixture は全てテスト内で生成し、商用画像・利用者画像を保存しない。
/// </summary>
[Trait("cp", "CP-DUPQUALITY-030")]
public sealed class CpDuplicateQuality030Tests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "ViewPrism2.Tests", "dup-quality", Guid.NewGuid().ToString("D"));

    public CpDuplicateQuality030Tests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public async Task 同一fileと正規化画素一致だけが決定的同一性になる()
    {
        var png = PathOf("base.png");
        var copy = PathOf("copy.png");
        var samePixels = PathOf("same-pixels.png");
        using (var image = CreateStructured())
        {
            Save(image, png, SKEncodedImageFormat.Png, 100);
            Save(image, samePixels, SKEncodedImageFormat.Png, 100);
        }
        File.Copy(png, copy);
        await File.AppendAllBytesAsync(samePixels, [0x56, 0x50, 0x32], TestContext.Current.CancellationToken); // PNG終端後の非画像metadata相当。表示画素不変・byte列差

        var verifier = new DuplicateRelationshipVerifier();
        Assert.Equal(DuplicateRelationship.SameFile,
            (await verifier.VerifyAsync(png, copy, TestContext.Current.CancellationToken)).Relationship);
        Assert.Equal(DuplicateRelationship.ImageContentMatch,
            (await verifier.VerifyAsync(png, samePixels, TestContext.Current.CancellationToken)).Relationship);
    }

    [Fact]
    public async Task 再encode_解像度変更_回転鏡像は実質同一になる()
    {
        var png = PathOf("base.png");
        var jpeg = PathOf("reencoded.jpg");
        var resized = PathOf("resized.png");
        var rotated = PathOf("rotated.png");
        var mirrored = PathOf("mirrored.png");
        using (var image = CreateStructured())
        {
            Save(image, png, SKEncodedImageFormat.Png, 100);
            Save(image, jpeg, SKEncodedImageFormat.Jpeg, 86);
            using var small = image.Resize(new SKImageInfo(96, 72),
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))!;
            Save(small, resized, SKEncodedImageFormat.Png, 100);
            using var rot = Rotate90(image);
            Save(rot, rotated, SKEncodedImageFormat.Png, 100);
            using var mirror = FlipHorizontal(image);
            Save(mirror, mirrored, SKEncodedImageFormat.Png, 100);
        }

        var verifier = new DuplicateRelationshipVerifier();
        foreach (var candidate in new[] { jpeg, resized, rotated, mirrored })
        {
            var result = await verifier.VerifyAsync(png, candidate, TestContext.Current.CancellationToken);
            Assert.Equal(DuplicateRelationship.SubstantiallySame, result.Relationship);
        }
    }

    [Fact]
    public async Task trimは部分重複だが局所内容変更は重複にならない()
    {
        var png = PathOf("base.png");
        var crop = PathOf("crop.png");
        var cropSameSize = PathOf("crop-same-size.png");
        var localEdit = PathOf("local-edit.png");
        using (var image = CreateStructured())
        {
            Save(image, png, SKEncodedImageFormat.Png, 100);
            using var cropped = new SKBitmap(128, 96);
            using (var canvas = new SKCanvas(cropped))
            {
                canvas.DrawBitmap(image, new SKRect(16, 12, 144, 108), new SKRect(0, 0, 128, 96));
            }
            Save(cropped, crop, SKEncodedImageFormat.Png, 100);
            using var croppedSameSize = new SKBitmap(160, 120);
            using (var canvas = new SKCanvas(croppedSameSize))
                canvas.DrawBitmap(image, new SKRect(16, 12, 144, 108), new SKRect(0, 0, 160, 120));
            Save(croppedSameSize, cropSameSize, SKEncodedImageFormat.Png, 100);

            using var edited = image.Copy();
            using (var canvas = new SKCanvas(edited))
            using (var paint = new SKPaint { Color = SKColors.Cyan, IsAntialias = false })
            {
                canvas.DrawRect(62, 43, 34, 22, paint); // 顔の目/口等に相当する局所置換
            }
            Save(edited, localEdit, SKEncodedImageFormat.Png, 100);
        }

        var verifier = new DuplicateRelationshipVerifier();
        Assert.Equal(DuplicateRelationship.PartialOverlap,
            (await verifier.VerifyAsync(png, crop, TestContext.Current.CancellationToken)).Relationship);
        Assert.Equal(DuplicateRelationship.PartialOverlap,
            (await verifier.VerifyAsync(png, cropSameSize, TestContext.Current.CancellationToken)).Relationship);
        Assert.Equal(DuplicateRelationship.Similar,
            (await verifier.VerifyAsync(png, localEdit, TestContext.Current.CancellationToken)).Relationship);
    }

    [Fact]
    public async Task pHash距離0でも局所表示内容が異なれば100または重複にしない()
    {
        var opaque = PathOf("opaque.png");
        var alphaEdit = PathOf("alpha-edit.png");
        using (var image = CreateStructured())
        {
            using var red = image.Copy();
            using (var redCanvas = new SKCanvas(red))
            using (var redPaint = new SKPaint { Color = new SKColor(255, 0, 0), IsAntialias = false })
                redCanvas.DrawRect(58, 42, 42, 30, redPaint);
            Save(red, opaque, SKEncodedImageFormat.Png, 100);
            using var edited = image.Copy();
            using (var editCanvas = new SKCanvas(edited))
            using (var editPaint = new SKPaint { Color = new SKColor(0, 130, 0), IsAntialias = false })
                editCanvas.DrawRect(58, 42, 42, 30, editPaint); // 8bit輝度は赤と同じ76、表示色は異なる
            Save(edited, alphaEdit, SKEncodedImageFormat.Png, 100);
        }

        var reader = new PHashImageReaderScaledDecode();
        var a = await reader.ComputePHashAsync(opaque);
        var b = await reader.ComputePHashAsync(alphaEdit);
        Assert.Equal(0, HammingDistance.Between(a!, b!)); // 症状を自作fixtureで再現

        var verified = await new DuplicateRelationshipVerifier().VerifyAsync(
            opaque, alphaEdit, TestContext.Current.CancellationToken);
        Assert.Equal(DuplicateRelationship.Similar, verified.Relationship);
        Assert.NotEqual(DuplicateRelationship.ImageContentMatch, verified.Relationship);
        Assert.False(verified.CanDisplayOneHundredPercent);
    }

    [Fact]
    public async Task production検索は重複関係をcacheしUIへ百分率でなく関係語彙を渡す()
    {
        using var db = new TempDb();
        var folder = new SyncFolder { Id = "dup-folder", Name = "dup", Path = _dir };
        Assert.True((await db.Folders.AddAsync(folder)).IsSuccess);

        var aPath = PathOf("search-a.png");
        var bPath = PathOf("search-b.png");
        using (var image = CreateStructured())
        {
            using var red = image.Copy();
            using (var canvas = new SKCanvas(red))
            using (var paint = new SKPaint { Color = new SKColor(255, 0, 0), IsAntialias = false })
                canvas.DrawRect(58, 42, 42, 30, paint);
            Save(red, aPath, SKEncodedImageFormat.Png, 100);
            using var green = image.Copy();
            using (var canvas = new SKCanvas(green))
            using (var paint = new SKPaint { Color = new SKColor(0, 130, 0), IsAntialias = false })
                canvas.DrawRect(58, 42, 42, 30, paint);
            Save(green, bPath, SKEncodedImageFormat.Png, 100);
        }

        var a = await SeedImageAsync(db, folder.Id, "search-a.png", "aaa");
        var b = await SeedImageAsync(db, folder.Id, "search-b.png", "bbb");
        var verifier = new DuplicateRelationshipVerifier();
        var service = new SimilaritySearchService(
            db.Folders, db.Images, db.Features, db.Similarities,
            new PHashImageReaderScaledDecode(), db.Clock, verifier);

        var result = Assert.Single(await service.FindSimilarAsync(
            a.Id, 70, ct: TestContext.Current.CancellationToken));
        Assert.Equal(b.Id, result.ImageId);
        Assert.Equal(DuplicateRelationship.Similar, result.Relationship);
        var cached = await db.Similarities.GetAsync(a.Id, b.Id);
        Assert.Equal(DuplicateRelationship.Similar, cached!.DuplicateRelationship);
        Assert.Equal(verifier.AdapterId, cached.VerifierAdapter);

        var vm = new OrganizeResultVM(b.Id, b.FileName, bPath, "1 KB", result.Score,
            isCriteria: false, added: false, result.Relationship);
        Assert.Equal("類似（重複ではありません）", vm.ScoreText);
        Assert.DoesNotContain('%', vm.ScoreText);
    }

    private static async Task<ImageRecord> SeedImageAsync(TempDb db, string folderId, string name, string id)
    {
        var path = Path.Combine((await db.Folders.GetByIdAsync(folderId))!.Path, name);
        var info = new FileInfo(path);
        var image = new ImageRecord
        {
            Id = id,
            SyncFolderId = folderId,
            RelativePath = name,
            FileName = name,
            FileSize = info.Length,
            Hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(
                path, TestContext.Current.CancellationToken))),
            Status = ImageStatus.Normal,
            CreatedDate = "2026-07-11T00:00:00.000Z",
            ModifiedDate = "2026-07-11T00:00:00.000Z",
        };
        await db.Images.AddAsync(image);
        return image;
    }

    private string PathOf(string name) => Path.Combine(_dir, name);

    private static SKBitmap CreateStructured()
    {
        var bitmap = new SKBitmap(new SKImageInfo(160, 120, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(232, 221, 186));
        using var paint = new SKPaint { IsAntialias = false };
        paint.Color = new SKColor(35, 46, 63); canvas.DrawRect(0, 0, 160, 15, paint);
        paint.Color = new SKColor(236, 86, 105); canvas.DrawOval(new SKRect(30, 18, 132, 112), paint);
        paint.Color = new SKColor(253, 225, 178); canvas.DrawOval(new SKRect(43, 28, 121, 108), paint);
        paint.Color = new SKColor(75, 38, 52); canvas.DrawOval(new SKRect(55, 46, 72, 68), paint);
        canvas.DrawOval(new SKRect(93, 46, 110, 68), paint);
        paint.Color = new SKColor(196, 49, 77); canvas.DrawRect(70, 83, 24, 7, paint);
        paint.Color = new SKColor(32, 122, 170); canvas.DrawRect(8, 82, 27, 30, paint);
        paint.Color = new SKColor(244, 205, 54); canvas.DrawCircle(145, 99, 12, paint);
        return bitmap;
    }

    private static void Save(SKBitmap bitmap, string path, SKEncodedImageFormat format, int quality)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, quality);
        using var stream = File.OpenWrite(path);
        data.SaveTo(stream);
    }

    private static SKBitmap Rotate90(SKBitmap source)
    {
        var result = new SKBitmap(source.Height, source.Width);
        for (var y = 0; y < source.Height; y++)
        for (var x = 0; x < source.Width; x++)
            result.SetPixel(source.Height - 1 - y, x, source.GetPixel(x, y));
        return result;
    }

    private static SKBitmap FlipHorizontal(SKBitmap source)
    {
        var result = new SKBitmap(source.Width, source.Height);
        for (var y = 0; y < source.Height; y++)
        for (var x = 0; x < source.Width; x++)
            result.SetPixel(source.Width - 1 - x, y, source.GetPixel(x, y));
        return result;
    }
}
