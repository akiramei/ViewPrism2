using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services.Repair;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// CP-TRASH-022(ECO-018 / TrashService.DeleteToTrashAsync): 削除モードの「ゴミ箱へ移動」=
/// ユーザー起点のソフト削除(normal→deleted)。normal 限定・status 更新のみ(タグ/ID/特徴量不変)・
/// 物理ファイル非破壊(INV-009)。normal 以外は ValidationError。復元(T6/T7)で戻せる入口。
/// </summary>
[Trait("cp", "CP-TRASH-022")]
public sealed class CpTrash022Tests
{
    private const string Folder = "folder-1";

    private sealed class NullProbe : IFilePresenceProbe
    {
        public bool Exists(string absoluteImagePath) => false;
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
    public async Task ゴミ箱へ移動_normalからdeleted_タグとID不変()
    {
        using var db = new TempDb();
        await SeedFolderAsync(db);
        var image = Image("img-1", ImageStatus.Normal);
        await db.Images.AddAsync(image);
        var tag = new Tag { Id = "tag-1", Name = "T", Type = TagType.Simple };
        await db.Tags.AddAsync(tag);
        await db.Tags.UpsertImageTagAsync(new ImageTag { ImageId = image.Id, TagId = tag.Id });

        var service = new TrashService(db.Images, db.Folders, new NullProbe());
        var result = await service.DeleteToTrashAsync(image.Id);

        Assert.True(result.IsSuccess);
        var moved = await db.Images.GetByIdAsync(image.Id);
        Assert.Equal("img-1", moved!.Id);                 // ID 不変
        Assert.Equal(ImageStatus.Deleted, moved.Status);  // normal→deleted
        var tags = await db.Tags.GetImageTagsAsync(image.Id); // タグ不変(復元で戻せる)
        Assert.Contains(tags, t => t.TagId == tag.Id);
    }

    [Fact]
    public async Task ゴミ箱へ移動_normal以外はValidationErrorで状態不変()
    {
        using var db = new TempDb();
        await SeedFolderAsync(db);
        await db.Images.AddAsync(Image("d", ImageStatus.Deleted));
        await db.Images.AddAsync(Image("m", ImageStatus.Missing));
        await db.Images.AddAsync(Image("p", ImageStatus.Pending));

        var service = new TrashService(db.Images, db.Folders, new NullProbe());

        foreach (var (id, status) in new[] { ("d", ImageStatus.Deleted), ("m", ImageStatus.Missing), ("p", ImageStatus.Pending) })
        {
            var result = await service.DeleteToTrashAsync(id);
            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCode.ValidationError, result.Error);
            Assert.Equal(status, (await db.Images.GetByIdAsync(id))!.Status); // 状態不変
        }
    }

    [Fact]
    public async Task ゴミ箱へ移動_存在しないIDはNotFound()
    {
        using var db = new TempDb();
        await SeedFolderAsync(db);
        var service = new TrashService(db.Images, db.Folders, new NullProbe());

        var result = await service.DeleteToTrashAsync("missing-id");
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.NotFound, result.Error);
    }
}
