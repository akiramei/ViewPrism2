using SkiaSharp;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services.Similarity;
using ViewPrism2.Infrastructure.Database;
using ViewPrism2.Infrastructure.Imaging;
using Xunit;

namespace ViewPrism2.Oracle;

/// <summary>
/// S-25: 検索順位等価オラクル(scope=cross-factory・**A/B 共通の同等性契約**)。spec §2.10.4・CPOL-103。
/// 「実 decode パイプライン経由で、既知の類似構造を持つ画像集合に対し、近傍(同一構造の再エンコード/
/// 微小変化)が無関係画像より上位にランクされる」という、pHash のビット値に依存しない**順位等価**を凍結する。
/// factory-04 の実装に限らず、CPOL-103 を満たす任意の工場(例: factory-B=DecodeToWidth 早期縮小)が
/// 満たすべき横断ゲート。値レベルの pHash は S-19b(this-build)に分離済み。
/// </summary>
[Trait("oracle", "S-25")]
[Trait("scope", "cross-factory")]
public sealed class S25RankingEquivalenceTests
{
    private const int Size = 64;

    [Fact]
    public async Task 近傍は無関係より上位にランクされる_pHash値に依存しない順位等価()
    {
        using var db = new OracleDb();
        var imageDir = Path.Combine(Path.GetTempPath(), "ViewPrism2.S25", Guid.NewGuid().ToString("D"));
        Directory.CreateDirectory(imageDir);
        try
        {
            var features = new ImageFeatureRepository(db.Manager);
            var similarities = new ImageSimilarityRepository(db.Manager);
            var folder = new SyncFolder { Id = "fld", Name = "c", Path = imageDir };
            Assert.True((await db.Folders.AddAsync(folder)).IsSuccess);

            // 基準と、同一構造(pattern 0)の近傍 3 枚(再エンコード/微小輝度変化)
            await AddAsync(db, imageDir, "base.png", SKEncodedImageFormat.Png, 90, patternIndex: 0, shift: 0);
            var nearIds = new[]
            {
                await AddAsync(db, imageDir, "near-shift.png", SKEncodedImageFormat.Png, 90, 0, shift: 20),
                await AddAsync(db, imageDir, "near-jpeg90.jpg", SKEncodedImageFormat.Jpeg, 90, 0, shift: 0),
                await AddAsync(db, imageDir, "near-jpeg80.jpg", SKEncodedImageFormat.Jpeg, 80, 0, shift: 0),
            };
            // 構造の異なる無関係 5 枚
            foreach (var (i, p) in new[] { (1, 7), (2, 11), (3, 13), (4, 17), (5, 19) })
            {
                await AddAsync(db, imageDir, $"u{i}.png", SKEncodedImageFormat.Png, 90, patternIndex: p, shift: 0);
            }

            var service = new SimilaritySearchService(
                db.Folders, db.Images, features, similarities, new PHashImageReader(), db.Clock);
            var results = await service.FindSimilarAsync(
                "base.png", threshold: 70, progress: null, ct: TestContext.Current.CancellationToken);

            // 順位等価(横断契約): 近傍 3 枚がすべて結果に含まれ、かつ上位 3 位を占める
            var nearSet = nearIds.ToHashSet(StringComparer.Ordinal);
            foreach (var id in nearIds)
            {
                Assert.Contains(results, r => r.ImageId == id); // 近傍は必ず類似に入る(membership)
            }

            var top3 = results.Take(3).Select(r => r.ImageId).ToHashSet(StringComparer.Ordinal);
            Assert.Equal(nearSet, top3); // 上位 3 = 近傍集合(無関係より上位=順位等価)

            // 近傍の最小スコア > 結果内の無関係の最大スコア(明確な分離)
            var nearMinScore = results.Where(r => nearSet.Contains(r.ImageId)).Min(r => r.Score);
            var othersInResult = results.Where(r => !nearSet.Contains(r.ImageId)).ToList();
            if (othersInResult.Count > 0)
            {
                Assert.True(nearMinScore > othersInResult.Max(r => r.Score),
                    "近傍が無関係より高スコアでない(順位等価の破れ)");
            }
        }
        finally
        {
            try { Directory.Delete(imageDir, recursive: true); } catch (IOException) { }
        }
    }

    private static async Task<string> AddAsync(
        OracleDb db, string dir, string name, SKEncodedImageFormat format, int quality, int patternIndex, int shift)
    {
        var path = Path.Combine(dir, name);
        OracleImages.WriteStructured(path, Size, format, quality, patternIndex, shift);
        await db.Images.AddAsync(new ImageRecord
        {
            Id = name,
            SyncFolderId = "fld",
            RelativePath = name,
            FileName = name,
            FileSize = new FileInfo(path).Length,
            Hash = new string('0', 64),
            Status = ImageStatus.Normal,
            CreatedDate = "2026-01-01T00:00:00.000Z",
            ModifiedDate = "2026-01-01T00:00:00.000Z",
        });
        return name;
    }
}
