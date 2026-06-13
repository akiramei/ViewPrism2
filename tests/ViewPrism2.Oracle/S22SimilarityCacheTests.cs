using System.Security.Cryptography;
using System.Text;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services.Similarity;
using ViewPrism2.Infrastructure.Database;
using Xunit;

namespace ViewPrism2.Oracle;

/// <summary>
/// S-22: 特徴量・類似度キャッシュの正規化・連鎖無効化・CASCADE(spec §2.10.3・OC-18、EQ-001)。工場非開示。
/// </summary>
[Trait("oracle", "S-22")]
public sealed class S22SimilarityCacheTests
{
    private sealed class FixedReader(string hex) : IPHashImageReader
    {
        public Task<string?> ComputePHashAsync(string absoluteImagePath) => Task.FromResult<string?>(hex);
    }

    [Fact]
    public async Task ペアは正規化され順不同で同一キャッシュを指す()
    {
        using var db = new OracleDb();
        var similarities = new ImageSimilarityRepository(db.Manager);
        var folder = new SyncFolder { Id = "fld", Name = "c", Path = "C:/oracle-s22a" };
        Assert.True((await db.Folders.AddAsync(folder)).IsSuccess);
        await AddImageAsync(db, "img-a", folder.Id, ImageStatus.Normal);
        await AddImageAsync(db, "img-b", folder.Id, ImageStatus.Normal);

        // 逆順で保存しても (a,b)=(b,a) を指す
        await similarities.UpsertAsync("img-b", "img-a", 55, db.Clock.UtcNowIso());

        var byAb = await similarities.GetAsync("img-a", "img-b");
        var byBa = await similarities.GetAsync("img-b", "img-a");
        Assert.NotNull(byAb);
        Assert.NotNull(byBa);
        Assert.Equal(55, byAb.SimilarityScore);
        Assert.Equal(55, byBa.SimilarityScore);
        Assert.Equal("img-a-img-b", byAb.CacheKey); // {min}-{max}(序数)
    }

    [Fact]
    public async Task 特徴量再計算で関与ペアが連鎖無効化される()
    {
        using var db = new OracleDb();
        var features = new ImageFeatureRepository(db.Manager);
        var similarities = new ImageSimilarityRepository(db.Manager);
        var folder = new SyncFolder { Id = "fld", Name = "c", Path = "C:/oracle-s22b" };
        Assert.True((await db.Folders.AddAsync(folder)).IsSuccess);

        await AddImageAsync(db, "img-a", folder.Id, ImageStatus.Normal);
        var imgB = await AddImageAsync(db, "img-b", folder.Id, ImageStatus.Normal);
        await AddImageAsync(db, "img-c", folder.Id, ImageStatus.Normal);

        // A,C は fresh、B は stale(feature.Hash が現行と不一致 → 再計算される)
        await UpsertFeatureAsync(features, "img-a", "0000000000000000", fresh: true, db, "img-a");
        await UpsertFeatureAsync(features, "img-b", "1111111111111111", fresh: false, db, "img-b");
        await UpsertFeatureAsync(features, "img-c", "0000000000000000", fresh: true, db, "img-c");

        // B-C のペア類似度を事前キャッシュ
        await similarities.UpsertAsync("img-b", "img-c", 50, db.Clock.UtcNowIso());
        Assert.NotNull(await similarities.GetAsync("img-b", "img-c"));

        // 検索で候補 B の特徴量が再計算され、B が関与する類似度(B-C)が連鎖削除される
        var service = new SimilaritySearchService(
            db.Folders, db.Images, features, similarities, new FixedReader("0000000000000000"), db.Clock);
        _ = await service.FindSimilarAsync(
            "img-a", threshold: 0, progress: null, ct: TestContext.Current.CancellationToken);

        Assert.Null(await similarities.GetAsync("img-b", "img-c")); // 連鎖無効化
        var refreshed = await features.GetAsync("img-b");
        Assert.NotNull(refreshed);
        Assert.Equal("0000000000000000", refreshed.PHash);       // 再計算済み
        Assert.Equal(imgB.Hash, refreshed.Hash);                  // 現行内容で fresh 化
    }

    [Fact]
    public async Task 画像削除で特徴量と類似度がCASCADE削除される()
    {
        using var db = new OracleDb();
        var features = new ImageFeatureRepository(db.Manager);
        var similarities = new ImageSimilarityRepository(db.Manager);
        var folder = new SyncFolder { Id = "fld", Name = "c", Path = "C:/oracle-s22c" };
        Assert.True((await db.Folders.AddAsync(folder)).IsSuccess);
        await AddImageAsync(db, "img-x", folder.Id, ImageStatus.Normal);
        await AddImageAsync(db, "img-y", folder.Id, ImageStatus.Normal);
        await UpsertFeatureAsync(features, "img-x", "0000000000000000", fresh: true, db, "img-x");
        await similarities.UpsertAsync("img-x", "img-y", 80, db.Clock.UtcNowIso());

        await db.Images.DeleteAsync("img-x");

        Assert.Null(await features.GetAsync("img-x"));               // image_features CASCADE
        Assert.Null(await similarities.GetAsync("img-x", "img-y"));  // image_similarity CASCADE
    }

    private static async Task<ImageRecord> AddImageAsync(OracleDb db, string id, string folderId, ImageStatus status)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(id)));
        var image = new ImageRecord
        {
            Id = id,
            SyncFolderId = folderId,
            RelativePath = id + ".jpg",
            FileName = id + ".jpg",
            FileSize = 100,
            Hash = hash,
            Status = status,
            CreatedDate = "2026-01-01T00:00:00.000Z",
            ModifiedDate = "2026-01-01T00:00:00.000Z",
        };
        await db.Images.AddAsync(image);
        return image;
    }

    private static async Task UpsertFeatureAsync(
        ImageFeatureRepository features, string id, string phash, bool fresh, OracleDb db, string imageId)
    {
        var image = await db.Images.GetByIdAsync(imageId);
        await features.UpsertAsync(new ImageFeature
        {
            ImageId = id,
            PHash = phash,
            FileSize = image!.FileSize,
            ModifiedDate = image.ModifiedDate,
            Hash = fresh ? image.Hash : "stale-hash-mismatch", // fresh=false で内容不一致→再計算
            LastCalculated = "2026-01-01T00:00:00.000Z",
        });
    }
}
