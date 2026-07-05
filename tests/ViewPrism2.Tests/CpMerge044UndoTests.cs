using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services.Similarity;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// ECO-044(IMG-011 裁定③): マージ操作ログと補償 Undo。
/// マージはログ(sources・タグ差分=追加行+NULL補完行・内容指紋)を同一トランザクションで記録し、
/// 「取り消す」はログに基づく補償操作(追加タグ行の削除+補完値の NULL 復帰+sources deleted→normal)。
/// 実行可能条件= sources 行が全て存在 かつ destination/sources の現在指紋がログ指紋と一致 かつ 未取り消し。
/// </summary>
[Trait("cp", "CP-MERGE-018")]
public sealed class CpMerge044UndoTests
{
    [Fact]
    public async Task マージは操作ログを記録する_sources_タグ差分_未取り消し()
    {
        using var db = new TempDb();
        var f = await SeedFolderAsync(db);
        var target = await SeedImageAsync(db, f, "t.jpg");
        var source = await SeedImageAsync(db, f, "s.jpg");
        var (tagA, tagB, tagC) = await SeedThreeTagsAsync(db);
        // target: tagA=ta / tagC=NULL(補完対象)。source: tagB=sb(追加行)・tagC=sc(NULL 補完)
        await db.Tags.UpsertImageTagAsync(new ImageTag { ImageId = target.Id, TagId = tagA.Id, Value = "ta" });
        await db.Tags.UpsertImageTagAsync(new ImageTag { ImageId = target.Id, TagId = tagC.Id, Value = null });
        await db.Tags.UpsertImageTagAsync(new ImageTag { ImageId = source.Id, TagId = tagB.Id, Value = "sb" });
        await db.Tags.UpsertImageTagAsync(new ImageTag { ImageId = source.Id, TagId = tagC.Id, Value = "sc" });

        var service = NewService(db);
        Assert.True((await service.MergeAsync(target.Id, [source.Id])).IsSuccess);

        var op = await service.GetLatestOperationAsync(target.Id);
        Assert.NotNull(op);
        Assert.Equal(target.Id, op!.TargetId);
        Assert.Equal([source.Id], op.SourceIds);
        Assert.Equal([tagB.Id], op.AddedTagIds);              // 追加行= source のみが持っていたタグ
        Assert.Equal([tagC.Id], op.FilledTags.Keys.ToArray()); // NULL 補完行= target が NULL で source 値が入った
        Assert.Null(op.FilledTags[tagC.Id]);                   // 元値(NULL)を保存= Undo で NULL へ復帰
        Assert.Null(op.UndoneAt);
    }

    [Fact]
    public async Task 補償Undoの往復_source復元_追加行削除_補完値NULL復帰_既存タグ不変()
    {
        using var db = new TempDb();
        var f = await SeedFolderAsync(db);
        var target = await SeedImageAsync(db, f, "t.jpg");
        var source = await SeedImageAsync(db, f, "s.jpg");
        var (tagA, tagB, tagC) = await SeedThreeTagsAsync(db);
        await db.Tags.UpsertImageTagAsync(new ImageTag { ImageId = target.Id, TagId = tagA.Id, Value = "ta" });
        await db.Tags.UpsertImageTagAsync(new ImageTag { ImageId = target.Id, TagId = tagC.Id, Value = null });
        await db.Tags.UpsertImageTagAsync(new ImageTag { ImageId = source.Id, TagId = tagB.Id, Value = "sb" });
        await db.Tags.UpsertImageTagAsync(new ImageTag { ImageId = source.Id, TagId = tagC.Id, Value = "sc" });

        var service = NewService(db);
        Assert.True((await service.MergeAsync(target.Id, [source.Id])).IsSuccess);
        var op = await service.GetLatestOperationAsync(target.Id);

        // 直後は実行可能
        Assert.True((await service.EvaluateUndoAsync(op!.Id)).IsSuccess);

        var undo = await service.UndoMergeAsync(op.Id);
        Assert.True(undo.IsSuccess);

        // source: deleted → normal 復元(image_tags は元々不変)
        var restored = await db.Images.GetByIdAsync(source.Id);
        Assert.Equal(ImageStatus.Normal, restored!.Status);
        var sourceTags = await db.Tags.GetImageTagsAsync(source.Id);
        Assert.Equal(2, sourceTags.Count);

        // target: 追加行(tagB)が消え・補完行(tagC)は NULL へ戻り・既存(tagA)は不変
        var targetTags = await db.Tags.GetImageTagsAsync(target.Id);
        Assert.DoesNotContain(targetTags, t => t.TagId == tagB.Id);
        Assert.Null(targetTags.Single(t => t.TagId == tagC.Id).Value);
        Assert.Equal("ta", targetTags.Single(t => t.TagId == tagA.Id).Value);

        // ログは取り消し済みマーク
        var after = await service.GetLatestOperationAsync(target.Id);
        Assert.NotNull(after!.UndoneAt);
    }

    [Fact]
    public async Task destination変化後は取り消し不可_指紋不一致()
    {
        using var db = new TempDb();
        var f = await SeedFolderAsync(db);
        var target = await SeedImageAsync(db, f, "t.jpg");
        var source = await SeedImageAsync(db, f, "s.jpg");
        var (tagA, _, _) = await SeedThreeTagsAsync(db);
        await db.Tags.UpsertImageTagAsync(new ImageTag { ImageId = source.Id, TagId = tagA.Id, Value = "sa" });

        var service = NewService(db);
        Assert.True((await service.MergeAsync(target.Id, [source.Id])).IsSuccess);
        var op = await service.GetLatestOperationAsync(target.Id);

        // マージ後に destination のタグを変更(revision 変化)
        await db.Tags.UpsertImageTagAsync(new ImageTag { ImageId = target.Id, TagId = tagA.Id, Value = "edited" });

        Assert.False((await service.EvaluateUndoAsync(op!.Id)).IsSuccess);
        var undo = await service.UndoMergeAsync(op.Id);
        Assert.False(undo.IsSuccess);
        Assert.Equal(ErrorCode.ValidationError, undo.Error);
    }

    [Fact]
    public async Task source完全削除後は取り消し不可()
    {
        using var db = new TempDb();
        var f = await SeedFolderAsync(db);
        var target = await SeedImageAsync(db, f, "t.jpg");
        var source = await SeedImageAsync(db, f, "s.jpg");
        var (tagA, _, _) = await SeedThreeTagsAsync(db);
        await db.Tags.UpsertImageTagAsync(new ImageTag { ImageId = source.Id, TagId = tagA.Id, Value = "sa" });

        var service = NewService(db);
        Assert.True((await service.MergeAsync(target.Id, [source.Id])).IsSuccess);
        var op = await service.GetLatestOperationAsync(target.Id);

        await db.Images.DeleteAsync(source.Id); // 完全削除(行不在)

        var undo = await service.UndoMergeAsync(op!.Id);
        Assert.False(undo.IsSuccess);
        Assert.Equal(ErrorCode.ValidationError, undo.Error);
    }

    [Fact]
    public async Task 二重Undoは拒否される()
    {
        using var db = new TempDb();
        var f = await SeedFolderAsync(db);
        var target = await SeedImageAsync(db, f, "t.jpg");
        var source = await SeedImageAsync(db, f, "s.jpg");
        var (tagA, _, _) = await SeedThreeTagsAsync(db);
        await db.Tags.UpsertImageTagAsync(new ImageTag { ImageId = source.Id, TagId = tagA.Id, Value = "sa" });

        var service = NewService(db);
        Assert.True((await service.MergeAsync(target.Id, [source.Id])).IsSuccess);
        var op = await service.GetLatestOperationAsync(target.Id);

        Assert.True((await service.UndoMergeAsync(op!.Id)).IsSuccess);
        var second = await service.UndoMergeAsync(op.Id);
        Assert.False(second.IsSuccess);
        Assert.Equal(ErrorCode.ValidationError, second.Error);
    }

    [Fact]
    public async Task 同一destinationへの後続マージで先行ログは取り消し不可になる()
    {
        using var db = new TempDb();
        var f = await SeedFolderAsync(db);
        var target = await SeedImageAsync(db, f, "t.jpg");
        var s1 = await SeedImageAsync(db, f, "s1.jpg");
        var s2 = await SeedImageAsync(db, f, "s2.jpg");
        var (tagA, tagB, _) = await SeedThreeTagsAsync(db);
        await db.Tags.UpsertImageTagAsync(new ImageTag { ImageId = s1.Id, TagId = tagA.Id, Value = "a" });
        await db.Tags.UpsertImageTagAsync(new ImageTag { ImageId = s2.Id, TagId = tagB.Id, Value = "b" });

        var service = NewService(db);
        Assert.True((await service.MergeAsync(target.Id, [s1.Id])).IsSuccess);
        var op1 = await service.GetLatestOperationAsync(target.Id);
        Assert.True((await service.MergeAsync(target.Id, [s2.Id])).IsSuccess); // destination の内容が変化

        Assert.False((await service.EvaluateUndoAsync(op1!.Id)).IsSuccess);   // 先行ログは指紋不一致
        var op2 = await service.GetLatestOperationAsync(target.Id);
        Assert.NotEqual(op1.Id, op2!.Id);
        Assert.True((await service.EvaluateUndoAsync(op2.Id)).IsSuccess);     // 直近ログは可能
    }

    // ---- ヘルパ(CpMerge018Tests と同型) ----

    private static MergeService NewService(TempDb db) => new(db.Images, db.Tags, db.Merges, db.Clock);

    private static async Task<string> SeedFolderAsync(TempDb db)
    {
        var folder = new SyncFolder { Id = "folder-1", Name = "F", Path = "C:/pics" };
        Assert.True((await db.Folders.AddAsync(folder)).IsSuccess);
        return folder.Id;
    }

    private static async Task<ImageRecord> SeedImageAsync(TempDb db, string folderId, string name)
    {
        var image = new ImageRecord
        {
            Id = IdGenerator.NewId(),
            SyncFolderId = folderId,
            RelativePath = name,
            FileName = name,
            FileSize = 10,
            Hash = new string('a', 64),
            Status = ImageStatus.Normal,
            CreatedDate = "2026-01-01T00:00:00.000Z",
            ModifiedDate = "2026-01-01T00:00:00.000Z",
        };
        await db.Images.AddAsync(image);
        return image;
    }

    private static async Task<(Tag A, Tag B, Tag C)> SeedThreeTagsAsync(TempDb db)
    {
        var a = new Tag { Id = "tag-a", Name = "A", Type = TagType.Textual, Color = "#111111" };
        var b = new Tag { Id = "tag-b", Name = "B", Type = TagType.Textual, Color = "#222222" };
        var c = new Tag { Id = "tag-c", Name = "C", Type = TagType.Textual, Color = "#333333" };
        await db.Tags.AddAsync(a);
        await db.Tags.AddAsync(b);
        await db.Tags.AddAsync(c);
        return (a, b, c);
    }
}
