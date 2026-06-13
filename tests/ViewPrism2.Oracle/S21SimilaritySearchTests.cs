using System.Security.Cryptography;
using System.Text;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services.Similarity;
using ViewPrism2.Infrastructure.Database;
using Xunit;

namespace ViewPrism2.Oracle;

/// <summary>
/// S-21: 類似検索エンジン E2E(spec §2.10.4・OC-16・REQ-053、EQ-001)。工場非開示。
/// 候補=同一コレクションの status=Normal のみ・基準自身/別コレクション/deleted/missing/pending を除外、
/// 閾値以上(≧)を類似度降順・同値 id 昇順で返す。特徴量を直接投入し pHash を決定的に制御する
/// (リーダーは throw=特徴量が使われ呼ばれないことを保証)。
/// </summary>
[Trait("oracle", "S-21")]
public sealed class S21SimilaritySearchTests
{
    private sealed class ThrowingReader : IPHashImageReader
    {
        public Task<string?> ComputePHashAsync(string absoluteImagePath)
            => throw new InvalidOperationException("特徴量が fresh のためリーダーは呼ばれてはならない。");
    }

    [Fact]
    public async Task 候補はnormal限定で閾値以上を降順安定で返す()
    {
        using var db = new OracleDb();
        var features = new ImageFeatureRepository(db.Manager);
        var similarities = new ImageSimilarityRepository(db.Manager);

        var folder1 = new SyncFolder { Id = "fld-1", Name = "c1", Path = "C:/oracle-s21-1" };
        var folder2 = new SyncFolder { Id = "fld-2", Name = "c2", Path = "C:/oracle-s21-2" };
        Assert.True((await db.Folders.AddAsync(folder1)).IsSuccess);
        Assert.True((await db.Folders.AddAsync(folder2)).IsSuccess);

        // 基準(距離 0)
        await AddAsync(db, features, "img-a", folder1.Id, "0000000000000000", ImageStatus.Normal);
        // 近傍(距離 3 → 94)。タイbrake 検査のため同スコアを 2 枚(id 昇順 n1<n3)
        await AddAsync(db, features, "img-n1", folder1.Id, "0000000000000007", ImageStatus.Normal); // 3bit
        await AddAsync(db, features, "img-n3", folder1.Id, "0000000000000700", ImageStatus.Normal); // 3bit
        // 境界(距離 10 → 70。≧ で含む)
        await AddAsync(db, features, "img-n2", folder1.Id, "00000000000003ff", ImageStatus.Normal); // 10bit
        // 中間(距離 15 → 50。<70 除外)
        await AddAsync(db, features, "img-m", folder1.Id, "0000000000007fff", ImageStatus.Normal); // 15bit
        // 遠傍(除外)
        await AddAsync(db, features, "img-f1", folder1.Id, "000000003fffffff", ImageStatus.Normal); // 30bit
        await AddAsync(db, features, "img-f2", folder1.Id, "000000ffffffffff", ImageStatus.Normal); // 40bit
        // 非 normal(距離 0=score100 だが status で除外)
        await AddAsync(db, features, "img-del", folder1.Id, "0000000000000000", ImageStatus.Deleted);
        await AddAsync(db, features, "img-mis", folder1.Id, "0000000000000000", ImageStatus.Missing);
        await AddAsync(db, features, "img-pen", folder1.Id, "0000000000000000", ImageStatus.Pending);
        // 別コレクション(距離 0 だが collection 境界で除外)
        await AddAsync(db, features, "img-other", folder2.Id, "0000000000000000", ImageStatus.Normal);

        var service = new SimilaritySearchService(
            db.Folders, db.Images, features, similarities, new ThrowingReader(), db.Clock);

        var results = await service.FindSimilarAsync(
            "img-a", threshold: 70, progress: null, ct: TestContext.Current.CancellationToken);

        // 結果=n1(94)・n3(94)・n2(70)。94 同値は id 昇順(n1<n3)、その後 70
        Assert.Equal(["img-n1", "img-n3", "img-n2"], results.Select(r => r.ImageId));
        Assert.Equal([94, 94, 70], results.Select(r => r.Score));
    }

    private static async Task AddAsync(
        OracleDb db, ImageFeatureRepository features, string id, string folderId, string phash, ImageStatus status)
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
        await features.UpsertAsync(new ImageFeature
        {
            ImageId = id,
            PHash = phash,
            FileSize = image.FileSize,
            ModifiedDate = image.ModifiedDate,
            Hash = image.Hash,
            LastCalculated = "2026-01-01T00:00:00.000Z",
        });
    }
}
