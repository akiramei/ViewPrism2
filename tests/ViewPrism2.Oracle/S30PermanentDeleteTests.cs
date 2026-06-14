using Dapper;
using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services.Repair;
using ViewPrism2.Infrastructure.Database;
using Xunit;

namespace ViewPrism2.Oracle;

/// <summary>
/// S-30: 完全削除の CASCADE・対象限定(scope=cross-factory・OC-22・INV-014、仕様 §2.11.4、T8)。工場非開示。
/// deleted 限定。images 行削除 → image_tags/image_features/image_similarity が FK CASCADE で 0 件(孤児ゼロ)。
/// normal の完全削除は ValidationError。物理非破壊は S-26 で別途実証。
/// </summary>
[Trait("oracle", "S-30")]
[Trait("scope", "cross-factory")]
public sealed class S30PermanentDeleteTests
{
    private sealed class NullProbe : IFilePresenceProbe
    {
        public bool Exists(string absoluteImagePath) => false; // 完全削除では未使用
    }

    [Fact]
    public async Task deleted行削除でtags_features_similarityがCASCADE消滅_normalは拒否()
    {
        using var db = new OracleDb();
        var features = new ImageFeatureRepository(db.Manager);
        var similarities = new ImageSimilarityRepository(db.Manager);
        Assert.True((await db.Folders.AddAsync(
            new SyncFolder { Id = "fld", Name = "c", Path = "C:/oracle-s30" })).IsSuccess);

        // X=deleted(タグ・特徴量・類似度ペアを持つ)/ Y=relation 相手 / N=normal
        await AddImageAsync(db, "X", "x.png", ImageStatus.Deleted);
        await AddImageAsync(db, "Y", "y.png", ImageStatus.Normal);
        await AddImageAsync(db, "N", "n.png", ImageStatus.Normal);
        await TagAsync(db, "X", "t1");
        await features.UpsertAsync(new ImageFeature
        {
            ImageId = "X",
            PHash = "0000000000000000",
            HashAdapter = "skia-scaled-decode-v1",
            FileSize = 100,
            ModifiedDate = "2026-01-01T00:00:00.000Z",
            Hash = new string('0', 64),
            LastCalculated = "2026-01-01T00:00:00.000Z",
        });
        await similarities.UpsertAsync("X", "Y", 50, db.Clock.UtcNowIso());

        var trash = new TrashService(db.Images, db.Folders, new NullProbe());

        // 完全削除(T8): images 行削除 → CASCADE
        var del = await trash.PermanentDeleteAsync("X");
        Assert.True(del.IsSuccess);
        Assert.Null(await db.Images.GetByIdAsync("X"));

        var (imgCnt, tagCnt, featCnt, simCnt) = await db.Manager.RunAsync(async conn =>
        {
            var i = await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM images WHERE id='X'");
            var t = await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM image_tags WHERE image_id='X'");
            var f = await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM image_features WHERE image_id='X'");
            var s = await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM image_similarity WHERE image_id1='X' OR image_id2='X'");
            return (i, t, f, s);
        }, TestContext.Current.CancellationToken);

        Assert.Equal(0, imgCnt);   // images 行削除
        Assert.Equal(0, tagCnt);   // image_tags CASCADE
        Assert.Equal(0, featCnt);  // image_features CASCADE
        Assert.Equal(0, simCnt);   // image_similarity CASCADE(孤児ゼロ)

        // normal の完全削除は拒否(deleted 限定)
        var rejN = await trash.PermanentDeleteAsync("N");
        Assert.False(rejN.IsSuccess);
        Assert.Equal(ErrorCode.ValidationError, rejN.Error);
        Assert.NotNull(await db.Images.GetByIdAsync("N"));
    }

    private static async Task AddImageAsync(OracleDb db, string id, string rel, ImageStatus status)
        => await db.Images.AddAsync(new ImageRecord
        {
            Id = id,
            SyncFolderId = "fld",
            RelativePath = rel,
            FileName = rel,
            FileSize = 100,
            Hash = new string('0', 64),
            Status = status,
            CreatedDate = "2026-01-01T00:00:00.000Z",
            ModifiedDate = "2026-01-01T00:00:00.000Z",
        });

    private static async Task TagAsync(OracleDb db, string imageId, string tagId)
    {
        await db.Tags.AddAsync(new Tag { Id = tagId, Name = tagId, Type = TagType.Simple });
        await db.Tags.UpsertImageTagAsync(new ImageTag { ImageId = imageId, TagId = tagId, Value = null });
    }
}
