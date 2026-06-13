using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services.Similarity;
using ViewPrism2.Infrastructure.Database;
using Xunit;

namespace ViewPrism2.Oracle;

/// <summary>
/// S-23: マージ E2E(タグ集約・多元・status・原子・拒否)(spec §2.10.5・OC-17・INV-011/006、EQ-001)。工場非開示。
/// </summary>
[Trait("oracle", "S-23")]
public sealed class S23MergeTests
{
    [Fact]
    public void タグ集約_マージ先優先_NULL補完_多元id昇順先勝ち_simpleUnion()
    {
        // 純粋計算(MergeCalculator)で値決着の隠しケースを凍結する
        var target = new[]
        {
            Tag("s", null),   // simple(値なし)
            Tag("t", null),   // textual だがマージ先は未設定
            Tag("n", "5"),    // numeric(マージ先に値あり)
        };
        var sourceA = new[] { Tag("t", "赤"), Tag("n", "3"), Tag("u", null) }; // id 小
        var sourceB = new[] { Tag("t", "青"), Tag("s", null) };                 // id 大

        var merged = MergeCalculator.Merge(target, [sourceA, sourceB]);
        var byTag = merged.Tags.ToDictionary(t => t.TagId, t => t.Value, StringComparer.Ordinal);

        Assert.Equal(4, merged.Tags.Count);            // s, t, n, u
        Assert.Null(byTag["s"]);                        // simple は存在 union のみ
        Assert.Equal("赤", byTag["t"]);                 // マージ先 NULL→A の '赤'(B の '青' で上書きしない=id 昇順先勝ち)
        Assert.Equal("5", byTag["n"]);                  // マージ先優先(A の '3' は採用しない)
        Assert.Null(byTag["u"]);                        // A から union(simple)
    }

    [Fact]
    public async Task マージE2E_タグ集約とstatus遷移と元タグ保持()
    {
        using var db = new OracleDb();
        var merge = new MergeService(db.Images, db.Tags, new MergeRepository(db.Manager));
        var folder = new SyncFolder { Id = "fld", Name = "c", Path = "C:/oracle-s23" };
        Assert.True((await db.Folders.AddAsync(folder)).IsSuccess);

        await db.Tags.AddAsync(new Tag { Id = "s", Name = "Star", Type = TagType.Simple });
        await db.Tags.AddAsync(new Tag { Id = "t", Name = "Color", Type = TagType.Textual });
        await db.Tags.AddAsync(new Tag { Id = "n", Name = "Rating", Type = TagType.Numeric });
        await db.Tags.AddAsync(new Tag { Id = "u", Name = "Fav", Type = TagType.Simple });

        await AddNormalAsync(db, "img-t", folder.Id); // マージ先
        await AddNormalAsync(db, "img-a", folder.Id); // マージ元(id 小)
        await AddNormalAsync(db, "img-b", folder.Id); // マージ元(id 大)

        await db.Tags.UpsertImageTagAsync(new ImageTag { ImageId = "img-t", TagId = "s", Value = null });
        await db.Tags.UpsertImageTagAsync(new ImageTag { ImageId = "img-t", TagId = "t", Value = null });
        await db.Tags.UpsertImageTagAsync(new ImageTag { ImageId = "img-t", TagId = "n", Value = "5" });
        await db.Tags.UpsertImageTagAsync(new ImageTag { ImageId = "img-a", TagId = "t", Value = "赤" });
        await db.Tags.UpsertImageTagAsync(new ImageTag { ImageId = "img-a", TagId = "n", Value = "3" });
        await db.Tags.UpsertImageTagAsync(new ImageTag { ImageId = "img-a", TagId = "u", Value = null });
        await db.Tags.UpsertImageTagAsync(new ImageTag { ImageId = "img-b", TagId = "t", Value = "青" });
        await db.Tags.UpsertImageTagAsync(new ImageTag { ImageId = "img-b", TagId = "s", Value = null });

        var result = await merge.MergeAsync("img-t", ["img-a", "img-b"]);
        Assert.True(result.IsSuccess);

        // マージ先のタグ集約(マージ先優先・NULL 補完・id 昇順先勝ち)
        var targetTags = (await db.Tags.GetImageTagsAsync("img-t"))
            .ToDictionary(t => t.TagId, t => t.Value, StringComparer.Ordinal);
        Assert.Equal(4, targetTags.Count);
        Assert.Null(targetTags["s"]);
        Assert.Equal("赤", targetTags["t"]);
        Assert.Equal("5", targetTags["n"]);
        Assert.Null(targetTags["u"]);

        // マージ元は Deleted・image_tags は保持・マージ先は Normal
        Assert.Equal(ImageStatus.Deleted, (await db.Images.GetByIdAsync("img-a"))!.Status);
        Assert.Equal(ImageStatus.Deleted, (await db.Images.GetByIdAsync("img-b"))!.Status);
        Assert.Equal(ImageStatus.Normal, (await db.Images.GetByIdAsync("img-t"))!.Status);
        Assert.Contains(await db.Tags.GetImageTagsAsync("img-a"), t => t.TagId == "t" && t.Value == "赤");
    }

    [Fact]
    public async Task マージの拒否_自己マージ_重複_非normalは変更なし()
    {
        using var db = new OracleDb();
        var merge = new MergeService(db.Images, db.Tags, new MergeRepository(db.Manager));
        var folder = new SyncFolder { Id = "fld", Name = "c", Path = "C:/oracle-s23r" };
        Assert.True((await db.Folders.AddAsync(folder)).IsSuccess);
        await db.Tags.AddAsync(new Tag { Id = "t", Name = "Color", Type = TagType.Textual });

        await AddNormalAsync(db, "img-t", folder.Id);
        await AddNormalAsync(db, "img-a", folder.Id);
        await AddNormalAsync(db, "img-del", folder.Id);
        await db.Images.UpdateStatusAsync("img-del", ImageStatus.Deleted);
        await db.Tags.UpsertImageTagAsync(new ImageTag { ImageId = "img-t", TagId = "t", Value = "元" });

        Assert.False((await merge.MergeAsync("img-t", ["img-t"])).IsSuccess);        // 自己マージ
        Assert.False((await merge.MergeAsync("img-t", ["img-a", "img-a"])).IsSuccess); // 重複
        Assert.False((await merge.MergeAsync("img-t", ["img-del"])).IsSuccess);       // 非 normal

        // いずれの拒否でもマージ先・マージ元に変更なし
        Assert.Equal(ImageStatus.Normal, (await db.Images.GetByIdAsync("img-a"))!.Status);
        Assert.Equal("元", Assert.Single(await db.Tags.GetImageTagsAsync("img-t")).Value);
    }

    private static ImageTag Tag(string tagId, string? value)
        => new() { ImageId = string.Empty, TagId = tagId, Value = value };

    private static async Task AddNormalAsync(OracleDb db, string id, string folderId)
        => await db.Images.AddAsync(new ImageRecord
        {
            Id = id,
            SyncFolderId = folderId,
            RelativePath = id + ".jpg",
            FileName = id + ".jpg",
            FileSize = 100,
            Hash = new string('0', 64),
            Status = ImageStatus.Normal,
            CreatedDate = "2026-01-01T00:00:00.000Z",
            ModifiedDate = "2026-01-01T00:00:00.000Z",
        });
}
