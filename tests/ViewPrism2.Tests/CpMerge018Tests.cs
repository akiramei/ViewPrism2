using Dapper;
using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Repositories;
using ViewPrism2.Core.Services.Similarity;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// CP-MERGE-018(unit): マージ計算・マージサービスのタグ集約・status 遷移・原子性が
/// 仕様 §2.10.5・OC-17 と一致する。MergeCalculator(純粋計算)+MergeService(一時 SQLite)。
/// </summary>
[Trait("cp", "CP-MERGE-018")]
public sealed class CpMerge018Tests
{
    // ---- OC-17: MergeCalculator(純粋計算) ----

    [Fact]
    public void タグunion_マージ元のみのタグがマージ先へ付与される()
    {
        var target = new[] { Tag("t1", "a") };
        var source = new[] { Tag("t2", "b") };

        var result = MergeCalculator.Merge(target, [source]);

        Assert.Equal(2, result.Tags.Count);
        Assert.Equal("a", ValueOf(result, "t1"));
        Assert.Equal("b", ValueOf(result, "t2"));
    }

    [Fact]
    public void 衝突_マージ先優先_両方に同一tag_idの値違いはマージ先の値を保持()
    {
        var target = new[] { Tag("t1", "target-value") };
        var source = new[] { Tag("t1", "source-value") };

        var result = MergeCalculator.Merge(target, [source]);

        Assert.Single(result.Tags);
        Assert.Equal("target-value", ValueOf(result, "t1")); // FMEA-024: マージ元で上書きしない
    }

    [Fact]
    public void NULL補完_マージ先の値がNULLや空でマージ元に値ありならマージ元値を採用()
    {
        var targetNull = new[] { Tag("t1", null) };
        var targetEmpty = new[] { Tag("t2", string.Empty) };
        var source = new[] { Tag("t1", "src1"), Tag("t2", "src2") };

        var result = MergeCalculator.Merge([.. targetNull, .. targetEmpty], [source]);

        Assert.Equal("src1", ValueOf(result, "t1"));
        Assert.Equal("src2", ValueOf(result, "t2"));
    }

    [Fact]
    public void simple_union_値なしタグは存在のみunion()
    {
        var target = new[] { Tag("s1", null) };
        var source = new[] { Tag("s1", null), Tag("s2", null) };

        var result = MergeCalculator.Merge(target, [source]);

        Assert.Equal(2, result.Tags.Count);
        Assert.Null(ValueOf(result, "s1"));
        Assert.Null(ValueOf(result, "s2"));
    }

    [Fact]
    public void 多元id昇順先勝ち_マージ元2つが同タグに異値ならid小の非空値を採用()
    {
        // マージ先は当該タグを持たない(または NULL)。マージ元は id 昇順で渡される
        var target = Array.Empty<ImageTag>();
        var sourceIdSmall = new[] { Tag("t1", "from-small") }; // id 小(先)
        var sourceIdLarge = new[] { Tag("t1", "from-large") }; // id 大(後)

        var result = MergeCalculator.Merge(target, [sourceIdSmall, sourceIdLarge]);

        Assert.Single(result.Tags);
        Assert.Equal("from-small", ValueOf(result, "t1")); // id 小の非空値を採用・以降は上書きしない
    }

    [Fact]
    public void 多元_先のマージ元がNULLなら次の非空を採用()
    {
        var target = Array.Empty<ImageTag>();
        var sourceIdSmall = new[] { Tag("t1", null) };       // id 小だが NULL
        var sourceIdLarge = new[] { Tag("t1", "from-large") }; // id 大で非空

        var result = MergeCalculator.Merge(target, [sourceIdSmall, sourceIdLarge]);

        Assert.Equal("from-large", ValueOf(result, "t1")); // 最初の非空(id 大側)を採用
    }

    // ---- MergeService(DB): status・原子性・拒否 ----

    [Fact]
    public async Task status_マージ元はDeleted_image_tagsは残存_マージ先はNormalのまま()
    {
        using var db = new TempDb();
        var folder = await SeedFolderAsync(db);
        var target = await SeedImageAsync(db, folder, "target.jpg");
        var source = await SeedImageAsync(db, folder, "source.jpg");

        var (tagA, tagB) = await SeedTwoTagsAsync(db);
        await db.Tags.UpsertImageTagAsync(new ImageTag { ImageId = target.Id, TagId = tagA.Id, Value = "ta" });
        await db.Tags.UpsertImageTagAsync(new ImageTag { ImageId = source.Id, TagId = tagB.Id, Value = "sb" });

        var service = NewService(db);
        var result = await service.MergeAsync(target.Id, [source.Id]);
        Assert.True(result.IsSuccess);

        var targetAfter = await db.Images.GetByIdAsync(target.Id);
        var sourceAfter = await db.Images.GetByIdAsync(source.Id);
        Assert.Equal(ImageStatus.Normal, targetAfter!.Status);
        Assert.Equal(ImageStatus.Deleted, sourceAfter!.Status);

        // タグ集約: マージ先に両タグ
        var targetTags = await db.Tags.GetImageTagsAsync(target.Id);
        Assert.Equal(2, targetTags.Count);
        Assert.Contains(targetTags, t => t.TagId == tagA.Id && t.Value == "ta");
        Assert.Contains(targetTags, t => t.TagId == tagB.Id && t.Value == "sb");

        // マージ元の image_tags は残存(削除しない)
        var sourceTags = await db.Tags.GetImageTagsAsync(source.Id);
        Assert.Contains(sourceTags, t => t.TagId == tagB.Id);
    }

    [Fact]
    public async Task 衝突_DB経由でもマージ先の値を保持しマージ元では上書きしない()
    {
        using var db = new TempDb();
        var folder = await SeedFolderAsync(db);
        var target = await SeedImageAsync(db, folder, "target.jpg");
        var source = await SeedImageAsync(db, folder, "source.jpg");
        var (tagA, _) = await SeedTwoTagsAsync(db);

        await db.Tags.UpsertImageTagAsync(new ImageTag { ImageId = target.Id, TagId = tagA.Id, Value = "keep" });
        await db.Tags.UpsertImageTagAsync(new ImageTag { ImageId = source.Id, TagId = tagA.Id, Value = "overwrite" });

        var service = NewService(db);
        Assert.True((await service.MergeAsync(target.Id, [source.Id])).IsSuccess);

        var targetTags = await db.Tags.GetImageTagsAsync(target.Id);
        Assert.Single(targetTags);
        Assert.Equal("keep", targetTags[0].Value);
    }

    [Fact]
    public async Task 原子性_途中失敗で全ロールバック_マージ先タグとマージ元statusとも変化なし()
    {
        using var db = new TempDb();
        var folder = await SeedFolderAsync(db);
        var target = await SeedImageAsync(db, folder, "target.jpg");
        var source = await SeedImageAsync(db, folder, "source.jpg");
        var (tagA, _) = await SeedTwoTagsAsync(db);
        await db.Tags.UpsertImageTagAsync(new ImageTag { ImageId = source.Id, TagId = tagA.Id, Value = "s" });

        // FK 違反を誘発する故障注入リポジトリ(存在しない tag_id をマージ後タグに混ぜる)
        var faulty = new FaultyMergeRepository(db.Manager);
        var service = new MergeService(db.Images, db.Tags, faulty);

        await Assert.ThrowsAnyAsync<Exception>(() => service.MergeAsync(target.Id, [source.Id]));

        // ロールバック: マージ先タグ 0 件・マージ元は Normal のまま
        var targetTags = await db.Tags.GetImageTagsAsync(target.Id);
        Assert.Empty(targetTags);
        var sourceAfter = await db.Images.GetByIdAsync(source.Id);
        Assert.Equal(ImageStatus.Normal, sourceAfter!.Status);
    }

    [Fact]
    public async Task 拒否_マージ先イコールマージ元はValidationError()
    {
        using var db = new TempDb();
        var folder = await SeedFolderAsync(db);
        var img = await SeedImageAsync(db, folder, "a.jpg");
        var service = NewService(db);

        var result = await service.MergeAsync(img.Id, [img.Id]);
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.ValidationError, result.Error);
    }

    [Fact]
    public async Task 拒否_マージ元重複はValidationError()
    {
        using var db = new TempDb();
        var folder = await SeedFolderAsync(db);
        var target = await SeedImageAsync(db, folder, "t.jpg");
        var source = await SeedImageAsync(db, folder, "s.jpg");
        var service = NewService(db);

        var result = await service.MergeAsync(target.Id, [source.Id, source.Id]);
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.ValidationError, result.Error);
    }

    [Fact]
    public async Task 拒否_非normal指定はValidationError()
    {
        using var db = new TempDb();
        var folder = await SeedFolderAsync(db);
        var target = await SeedImageAsync(db, folder, "t.jpg");
        var missing = await SeedImageAsync(db, folder, "m.jpg", ImageStatus.Missing);
        var service = NewService(db);

        // マージ元が非 normal
        var r1 = await service.MergeAsync(target.Id, [missing.Id]);
        Assert.False(r1.IsSuccess);
        Assert.Equal(ErrorCode.ValidationError, r1.Error);

        // マージ先が非 normal
        var normal = await SeedImageAsync(db, folder, "n.jpg");
        var r2 = await service.MergeAsync(missing.Id, [normal.Id]);
        Assert.False(r2.IsSuccess);
        Assert.Equal(ErrorCode.ValidationError, r2.Error);
    }

    [Fact]
    public async Task 多元_DB経由_id昇順先勝ちが成立する()
    {
        using var db = new TempDb();
        var folder = await SeedFolderAsync(db);
        var target = await SeedImageAsync(db, folder, "target.jpg");
        // id を固定して昇順を制御
        var src1 = await SeedImageWithIdAsync(db, folder, "img-aaa", "s1.jpg");
        var src2 = await SeedImageWithIdAsync(db, folder, "img-zzz", "s2.jpg");
        var (tagA, _) = await SeedTwoTagsAsync(db);

        await db.Tags.UpsertImageTagAsync(new ImageTag { ImageId = src1.Id, TagId = tagA.Id, Value = "first" });
        await db.Tags.UpsertImageTagAsync(new ImageTag { ImageId = src2.Id, TagId = tagA.Id, Value = "second" });

        var service = NewService(db);
        // 引数順を逆に渡してもサービスが id 昇順で処理する
        Assert.True((await service.MergeAsync(target.Id, [src2.Id, src1.Id])).IsSuccess);

        var targetTags = await db.Tags.GetImageTagsAsync(target.Id);
        Assert.Single(targetTags);
        Assert.Equal("first", targetTags[0].Value); // id 小(img-aaa)の値
    }

    // ---- ヘルパ ----

    private static MergeService NewService(TempDb db) => new(db.Images, db.Tags, db.Merges);

    private static ImageTag Tag(string tagId, string? value)
        => new() { ImageId = "ignored", TagId = tagId, Value = value };

    private static string? ValueOf(MergedTags merged, string tagId)
        => merged.Tags.Single(t => t.TagId == tagId).Value;

    private static async Task<string> SeedFolderAsync(TempDb db)
    {
        var folder = new SyncFolder { Id = "folder-1", Name = "F", Path = "C:/pics" };
        Assert.True((await db.Folders.AddAsync(folder)).IsSuccess);
        return folder.Id;
    }

    private static Task<ImageRecord> SeedImageAsync(
        TempDb db, string folderId, string name, ImageStatus status = ImageStatus.Normal)
        => SeedImageWithIdAsync(db, folderId, IdGenerator.NewId(), name, status);

    private static async Task<ImageRecord> SeedImageWithIdAsync(
        TempDb db, string folderId, string id, string name, ImageStatus status = ImageStatus.Normal)
    {
        var image = new ImageRecord
        {
            Id = id,
            SyncFolderId = folderId,
            RelativePath = name,
            FileName = name,
            FileSize = 10,
            Hash = new string('a', 64),
            Status = status,
            CreatedDate = "2026-01-01T00:00:00.000Z",
            ModifiedDate = "2026-01-01T00:00:00.000Z",
        };
        await db.Images.AddAsync(image);
        return image;
    }

    private static async Task<(Tag A, Tag B)> SeedTwoTagsAsync(TempDb db)
    {
        var a = new Tag { Id = "tag-a", Name = "TagA", Type = TagType.Textual };
        var b = new Tag { Id = "tag-b", Name = "TagB", Type = TagType.Textual };
        await db.Tags.AddAsync(a);
        await db.Tags.AddAsync(b);
        return (a, b);
    }
}

/// <summary>原子性検査用: マージ後タグに存在しない tag_id を混ぜて FK 違反を誘発する故障リポジトリ。</summary>
internal sealed class FaultyMergeRepository : IMergeRepository
{
    private readonly ViewPrism2.Infrastructure.Database.MergeRepository _inner;

    public FaultyMergeRepository(ViewPrism2.Infrastructure.Database.DatabaseManager db)
    {
        _inner = new ViewPrism2.Infrastructure.Database.MergeRepository(db);
    }

    public Task ApplyMergeAsync(
        string targetId, IReadOnlyList<ImageTag> mergedTags, IReadOnlyList<string> sourceIds)
    {
        // 末尾に存在しないタグを 1 件追加 → UPSERT 時に FK 違反 → 全ロールバック
        var poisoned = mergedTags
            .Append(new ImageTag { ImageId = targetId, TagId = "nonexistent-tag", Value = "x" })
            .ToList();
        return _inner.ApplyMergeAsync(targetId, poisoned, sourceIds);
    }
}
