using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using Xunit;

namespace ViewPrism2.Oracle;

/// <summary>
/// S-07: バッチ原子性(spec §2.2 REQ-027 / INV-006、EQ-001)。
/// 3 画像へ numeric タグ(min=1,max=5)を value=3 で一括付与。うち 1 画像は事前に削除済み id を混入。
/// 期待: 全体ロールバック(0 件適用)+NotFound 系エラー。残り 2 画像にも付与されない。
/// </summary>
[Trait("oracle", "S-07")]
public sealed class S07BatchAtomicityTests
{
    [Fact]
    public async Task 削除済みid混入の一括付与は全体ロールバックされる()
    {
        using var db = new OracleDb();

        var folder = new SyncFolder { Id = IdGenerator.NewId(), Name = "pics", Path = "C:/oracle-s07" };
        Assert.True((await db.Folders.AddAsync(folder)).IsSuccess);

        var alive1 = await SeedImageAsync(db, folder.Id, "a.jpg");
        var alive2 = await SeedImageAsync(db, folder.Id, "b.jpg");
        var doomed = await SeedImageAsync(db, folder.Id, "c.jpg");

        // 事前に削除して「削除済み id」を作る
        await db.Images.DeleteAsync(doomed.Id);

        var service = new TagService(db.Tags);
        var created = await service.CreateAsync("rating", TagType.Numeric);
        Assert.True(created.IsSuccess);
        var tagId = created.Value!.Id;
        Assert.True((await service.SetNumericSettingsAsync(tagId, min: 1, max: 5, step: null, unit: null)).IsSuccess);

        // 一括付与(削除済み id を末尾に混入 → 先行 2 件の適用後に失敗させて原子性を実測)
        var result = await service.TagImagesAsync([alive1.Id, alive2.Id, doomed.Id], tagId, "3");

        // NotFound 系エラー
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.NotFound, result.Error);

        // 全体ロールバック: 残り 2 画像にも付与されない(0 件適用)
        Assert.Empty(await db.Tags.GetImageTagsAsync(alive1.Id));
        Assert.Empty(await db.Tags.GetImageTagsAsync(alive2.Id));
        Assert.Empty(await db.Tags.GetAllImageTagsAsync());
    }

    private static async Task<ImageRecord> SeedImageAsync(OracleDb db, string folderId, string name)
    {
        var image = new ImageRecord
        {
            Id = IdGenerator.NewId(),
            SyncFolderId = folderId,
            RelativePath = name,
            FileName = name,
            FileSize = 10,
            Hash = new string('0', 64),
            Status = ImageStatus.Normal,
            CreatedDate = "2026-01-01T00:00:00.000Z",
            ModifiedDate = "2026-01-01T00:00:00.000Z",
        };
        await db.Images.AddAsync(image);
        return image;
    }
}
