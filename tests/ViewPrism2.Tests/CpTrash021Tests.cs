using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services.Repair;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// CP-TRASH-021(M-TRASH-026 / OC-19 / T9): 除外(missing→deleted)の missing 限定・物理非破壊・
/// タグ/ID 不変(仕様 §2.11.0 T9 / INV-009 / ECO-005)。PermanentDeleteAsync と同型の構造を検査する。
/// 一時 SQLite で除外後 status と他 status 拒否時の不変を exact 検査する(物理ファイルには触れない)。
/// </summary>
[Trait("cp", "CP-TRASH-021")]
public sealed class CpTrash021Tests
{
    private const string Folder = "folder-1";

    /// <summary>除外は status 更新のみで probe を使わないが、TrashService 構築のため固定フェイクを渡す。</summary>
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

    private static TrashService CreateService(TempDb db)
        => new(db.Images, db.Folders, new FakeProbe(exists: false));

    [Fact]
    public async Task 除外_missingからDeleted_T9_タグとID不変()
    {
        using var db = new TempDb();
        await SeedFolderAsync(db);
        var image = Image("img-1", ImageStatus.Missing);
        await db.Images.AddAsync(image);
        var tag = new Tag { Id = "tag-1", Name = "T", Type = TagType.Simple };
        await db.Tags.AddAsync(tag);
        await db.Tags.UpsertImageTagAsync(new ImageTag { ImageId = image.Id, TagId = tag.Id });

        var result = await CreateService(db).ExcludeAsync(image.Id);

        Assert.True(result.IsSuccess);
        var excluded = await db.Images.GetByIdAsync(image.Id);
        Assert.Equal("img-1", excluded!.Id);                  // ID 不変
        Assert.Equal(ImageStatus.Deleted, excluded.Status);   // missing→deleted(トラッシュへ)
        var tags = await db.Tags.GetImageTagsAsync(image.Id); // タグ不変(復元で戻せる)
        Assert.Contains(tags, t => t.TagId == tag.Id);
    }

    [Fact]
    public async Task 除外_missing以外はValidationError_normalPendingDeletedは対象外()
    {
        using var db = new TempDb();
        await SeedFolderAsync(db);
        await db.Images.AddAsync(Image("n", ImageStatus.Normal));
        await db.Images.AddAsync(Image("p", ImageStatus.Pending));
        await db.Images.AddAsync(Image("d", ImageStatus.Deleted));

        var service = CreateService(db);

        foreach (var (id, status) in new[]
        {
            ("n", ImageStatus.Normal),
            ("p", ImageStatus.Pending),
            ("d", ImageStatus.Deleted),
        })
        {
            var result = await service.ExcludeAsync(id);
            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCode.ValidationError, result.Error);
            // status は変化しない(missing 以外は拒否)
            Assert.Equal(status, (await db.Images.GetByIdAsync(id))!.Status);
        }
    }

    [Fact]
    public async Task 除外_存在しない画像はNotFound()
    {
        using var db = new TempDb();
        await SeedFolderAsync(db);

        var result = await CreateService(db).ExcludeAsync("missing-id");

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.NotFound, result.Error);
    }
}
