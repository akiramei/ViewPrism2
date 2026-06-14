using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services.Repair;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// CP-TRASH-020(M-TRASH-026 / OC-21・OC-22 / T6-T8): トラッシュ復元の存在分岐と
/// 完全削除の CASCADE・deleted 限定(仕様 §2.11.3-4 / INV-013・INV-014)。
/// 一時 SQLite + IFilePresenceProbe フェイク(存在/不在を注入)で復元後 status・
/// 完全削除後の各テーブル件数を exact 検査する。
/// </summary>
[Trait("cp", "CP-TRASH-020")]
public sealed class CpTrash020Tests
{
    private const string Folder = "folder-1";

    /// <summary>存在/不在を固定で返すフェイク存在プローブ。</summary>
    private sealed class FakeProbe(bool exists) : IFilePresenceProbe
    {
        public bool Exists(string absoluteImagePath) => exists;
    }

    private static ImageRecord Image(string id, ImageStatus status) => new()
    {
        Id = id,
        SyncFolderId = Folder,
        RelativePath = $"{id}.jpg",
        FileName = $"{id}.jpg",
        FileSize = 100,
        Hash = "h",
        Status = status,
        CreatedDate = "2026-01-01T00:00:00.000Z",
        ModifiedDate = "2026-01-01T00:00:00.000Z",
    };

    private static async Task SeedFolderAsync(TempDb db)
        => await db.Folders.AddAsync(new SyncFolder { Id = Folder, Name = "F", Path = "C:/f" });

    [Fact]
    public async Task 復元_物理存在_deletedからNormal_T6_タグとID不変()
    {
        using var db = new TempDb();
        await SeedFolderAsync(db);
        var image = Image("img-1", ImageStatus.Deleted);
        await db.Images.AddAsync(image);
        var tag = new Tag { Id = "tag-1", Name = "T", Type = TagType.Simple };
        await db.Tags.AddAsync(tag);
        await db.Tags.UpsertImageTagAsync(new ImageTag { ImageId = image.Id, TagId = tag.Id });

        var service = new TrashService(db.Images, db.Folders, new FakeProbe(exists: true));
        var result = await service.RestoreAsync(image.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(ImageStatus.Normal, result.Value);

        var restored = await db.Images.GetByIdAsync(image.Id);
        Assert.Equal("img-1", restored!.Id);                  // ID 不変
        Assert.Equal(ImageStatus.Normal, restored.Status);
        var tags = await db.Tags.GetImageTagsAsync(image.Id); // タグ不変
        Assert.Contains(tags, t => t.TagId == tag.Id);
    }

    [Fact]
    public async Task 復元_物理不在_deletedからMissing_T7_幽霊normal防止()
    {
        using var db = new TempDb();
        await SeedFolderAsync(db);
        var image = Image("img-1", ImageStatus.Deleted);
        await db.Images.AddAsync(image);

        var service = new TrashService(db.Images, db.Folders, new FakeProbe(exists: false));
        var result = await service.RestoreAsync(image.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(ImageStatus.Missing, result.Value);
        Assert.Equal(ImageStatus.Missing, (await db.Images.GetByIdAsync(image.Id))!.Status);
    }

    [Fact]
    public async Task 復元_deleted以外はValidationError()
    {
        using var db = new TempDb();
        await SeedFolderAsync(db);
        var normal = Image("img-1", ImageStatus.Normal);
        await db.Images.AddAsync(normal);

        var service = new TrashService(db.Images, db.Folders, new FakeProbe(exists: true));
        var result = await service.RestoreAsync(normal.Id);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.ValidationError, result.Error);
        // status は変化しない
        Assert.Equal(ImageStatus.Normal, (await db.Images.GetByIdAsync(normal.Id))!.Status);
    }

    [Fact]
    public async Task 完全削除_images行削除でimageTagsもCASCADEで0件_孤児ゼロ()
    {
        using var db = new TempDb();
        await SeedFolderAsync(db);
        var image = Image("img-1", ImageStatus.Deleted);
        await db.Images.AddAsync(image);

        // image_tags / image_features を付与しておき、CASCADE で消えることを確認する
        var tag = new Tag { Id = "tag-1", Name = "T", Type = TagType.Simple };
        await db.Tags.AddAsync(tag);
        await db.Tags.UpsertImageTagAsync(new ImageTag { ImageId = image.Id, TagId = tag.Id });
        await db.Features.UpsertAsync(new ImageFeature
        {
            ImageId = image.Id,
            PHash = "0000000000000000",
            HashAdapter = "skia-scaled-decode-v1",
            FileSize = 100,
            ModifiedDate = "2026-01-01T00:00:00.000Z",
            Hash = "h",
            LastCalculated = "2026-01-01T00:00:00.000Z",
        });

        var service = new TrashService(db.Images, db.Folders, new FakeProbe(exists: false));
        var result = await service.PermanentDeleteAsync(image.Id);
        Assert.True(result.IsSuccess);

        // images 行削除 + image_tags / image_features は CASCADE で 0 件(孤児ゼロ)
        Assert.Null(await db.Images.GetByIdAsync(image.Id));
        Assert.Empty(await db.Tags.GetImageTagsAsync(image.Id));
        Assert.Null(await db.Features.GetAsync(image.Id));
    }

    [Fact]
    public async Task 完全削除_deleted以外はValidationError_normalPendingMissingは対象外()
    {
        using var db = new TempDb();
        await SeedFolderAsync(db);
        await db.Images.AddAsync(Image("n", ImageStatus.Normal));
        await db.Images.AddAsync(Image("p", ImageStatus.Pending));
        await db.Images.AddAsync(Image("m", ImageStatus.Missing));

        var service = new TrashService(db.Images, db.Folders, new FakeProbe(exists: false));

        foreach (var id in new[] { "n", "p", "m" })
        {
            var result = await service.PermanentDeleteAsync(id);
            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCode.ValidationError, result.Error);
            Assert.NotNull(await db.Images.GetByIdAsync(id)); // 行は残る
        }
    }

    [Fact]
    public void TrashTransition_ResolveRestore_存在Normal_不在Missing_純粋関数ベクタ()
    {
        Assert.Equal(ImageStatus.Normal, TrashTransition.ResolveRestore(fileExists: true));
        Assert.Equal(ImageStatus.Missing, TrashTransition.ResolveRestore(fileExists: false));
    }
}
