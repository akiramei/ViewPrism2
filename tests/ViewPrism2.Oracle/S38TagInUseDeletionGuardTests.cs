using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using Xunit;

namespace ViewPrism2.Oracle;

/// <summary>
/// S-38: 使用中タグ定義の削除拒否(REQ-082 / TAG-008 裁定・ECO-045、EQ-001)。
/// 付与/配置/条件参照のいずれかを持つタグは TagInUse で拒否・定義と参照は無傷。
/// 子タグのみを持つタグと未使用タグは削除成功(子はルート化)。
/// FK カスケード(REQ-028/S-11)は削除が実行された場合の防御層として不変。
/// </summary>
[Trait("oracle", "S-38")]
public sealed class S38TagInUseDeletionGuardTests
{
    [Fact]
    public async Task 使用中タグ定義の削除は拒否され未使用タグは削除できる()
    {
        using var db = new OracleDb();
        var service = new TagService(db.Tags);

        // --- フィクスチャ: 画像 1 枚+参照種別を 1 種ずつ持つタグ 3 種 ---
        var folder = new SyncFolder { Id = IdGenerator.NewId(), Name = "pics", Path = "C:/oracle-s38" };
        Assert.True((await db.Folders.AddAsync(folder)).IsSuccess);

        var image = new ImageRecord
        {
            Id = IdGenerator.NewId(),
            SyncFolderId = folder.Id,
            RelativePath = "a.jpg",
            FileName = "a.jpg",
            FileSize = 10,
            Hash = new string('0', 64),
            Status = ImageStatus.Normal,
            CreatedDate = "2026-01-01T00:00:00.000Z",
            ModifiedDate = "2026-01-01T00:00:00.000Z",
        };
        await db.Images.AddAsync(image);

        var tagged = new Tag { Id = IdGenerator.NewId(), Name = "Tagged", Type = TagType.Textual };
        var placed = new Tag { Id = IdGenerator.NewId(), Name = "Placed", Type = TagType.Simple };
        var referenced = new Tag { Id = IdGenerator.NewId(), Name = "Referenced", Type = TagType.Simple };
        await db.Tags.AddAsync(tagged);
        await db.Tags.AddAsync(placed);
        await db.Tags.AddAsync(referenced);

        await db.Tags.UpsertImageTagAsync(new ImageTag { ImageId = image.Id, TagId = tagged.Id, Value = "v" });

        var views = new ViewService(db.Views, db.Clock);
        var view = (await views.CreateAsync("oracle view")).Value!;
        Assert.True((await views.AddNodeAsync(view.Id, placed.Id, parentId: null, position: 0)).IsSuccess);
        Assert.True((await views.AddConditionAsync(view.Id, referenced.Id, ConditionOperator.Exists)).IsSuccess);

        // --- 付与/配置/条件参照: 3 種とも TagInUse で拒否・定義は無傷 ---
        foreach (var tag in new[] { tagged, placed, referenced })
        {
            var result = await service.DeleteAsync(tag.Id);
            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCode.TagInUse, result.Error);
            Assert.NotNull(await db.Tags.GetByIdAsync(tag.Id));
        }

        // 参照も無傷(付与行・階層ノード・条件 tag_id)
        Assert.Single(await db.Tags.GetImageTagsAsync(image.Id));
        Assert.Single(await views.GetHierarchyAsync(view.Id));
        Assert.Equal(referenced.Id, Assert.Single(await views.GetConditionsAsync(view.Id)).TagId);

        // --- 子タグのみ=削除成功・子はルート化(4a 裁定: 親であることは「使用」でない) ---
        var parentOnly = new Tag { Id = IdGenerator.NewId(), Name = "ParentOnly", Type = TagType.Simple };
        await db.Tags.AddAsync(parentOnly);
        var child = new Tag { Id = IdGenerator.NewId(), Name = "Child", Type = TagType.Simple, ParentId = parentOnly.Id };
        await db.Tags.AddAsync(child);

        Assert.True((await service.DeleteAsync(parentOnly.Id)).IsSuccess);
        Assert.Null((await db.Tags.GetByIdAsync(child.Id))!.ParentId);

        // --- 未使用=削除成功 ---
        var unused = new Tag { Id = IdGenerator.NewId(), Name = "Unused", Type = TagType.Simple };
        await db.Tags.AddAsync(unused);
        Assert.True((await service.DeleteAsync(unused.Id)).IsSuccess);
        Assert.Null(await db.Tags.GetByIdAsync(unused.Id));
    }
}
