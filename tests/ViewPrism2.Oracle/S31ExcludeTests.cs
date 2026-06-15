using Dapper;
using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services.Repair;
using ViewPrism2.Infrastructure.Database;
using Xunit;

namespace ViewPrism2.Oracle;

/// <summary>
/// S-31: 除外(scope=cross-factory・REQ-073・状態機械 T9、仕様 §2.11.6、ECO-005)。工場非開示。
/// missing→deleted のみ(missing 以外は ValidationError)。status 更新のみで タグ/image_id/特徴量は不変・物理非破壊(INV-009)。
/// 除外した missing は deleted としてトラッシュへ入り、復元(T6/T7)で戻せる可逆操作。
/// </summary>
[Trait("oracle", "S-31")]
[Trait("scope", "cross-factory")]
public sealed class S31ExcludeTests
{
    private sealed class NullProbe : IFilePresenceProbe
    {
        public bool Exists(string absoluteImagePath) => false; // 除外では未使用(status 更新のみ)
    }

    [Fact]
    public async Task missingのみdeleted化_タグ_ID不変_他statusは拒否()
    {
        using var db = new OracleDb();
        Assert.True((await db.Folders.AddAsync(
            new SyncFolder { Id = "fld", Name = "c", Path = "C:/oracle-s31" })).IsSuccess);

        // M=missing(タグ付与あり)/ N=normal / P=pending / D=deleted
        await AddImageAsync(db, "M", "m.png", ImageStatus.Missing);
        await AddImageAsync(db, "N", "n.png", ImageStatus.Normal);
        await AddImageAsync(db, "P", "p.png", ImageStatus.Pending);
        await AddImageAsync(db, "D", "d.png", ImageStatus.Deleted);
        await TagAsync(db, "M", "t1");

        var trash = new TrashService(db.Images, db.Folders, new NullProbe());

        // missing→deleted(T9)。status のみ更新・タグ/ID 不変
        var ok = await trash.ExcludeAsync("M");
        Assert.True(ok.IsSuccess);
        var m = await db.Images.GetByIdAsync("M");
        Assert.Equal("M", m!.Id);                        // image_id 不変(INV-001)
        Assert.Equal(ImageStatus.Deleted, m.Status);     // missing→deleted(T9)
        var mtags = await db.Tags.GetImageTagsAsync("M");
        Assert.Single(mtags);                            // タグ不変(status 更新のみ)

        // 物理行(images)は削除されない(=完全削除 T8 とは別。トラッシュへ移っただけ)
        var rowCnt = await db.Manager.RunAsync(
            conn => conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM images WHERE id='M'"),
            TestContext.Current.CancellationToken);
        Assert.Equal(1, rowCnt);

        // missing 以外の除外は ValidationError・status 不変
        foreach (var (id, status) in new[]
        {
            ("N", ImageStatus.Normal), ("P", ImageStatus.Pending), ("D", ImageStatus.Deleted),
        })
        {
            var rej = await trash.ExcludeAsync(id);
            Assert.False(rej.IsSuccess);
            Assert.Equal(ErrorCode.ValidationError, rej.Error);
            Assert.Equal(status, (await db.Images.GetByIdAsync(id))!.Status);   // status 不変
        }

        // 存在しない ID は NotFound
        var missing = await trash.ExcludeAsync("ZZZ");
        Assert.False(missing.IsSuccess);
        Assert.Equal(ErrorCode.NotFound, missing.Error);
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
