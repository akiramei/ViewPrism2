using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services.Repair;
using Xunit;

namespace ViewPrism2.Oracle;

/// <summary>
/// S-29: トラッシュ復元の存在分岐(scope=cross-factory・OC-21・INV-013、仕様 §2.11.3、T6/T7)。工場非開示。
/// deleted 限定。物理存在(IFilePresenceProbe を注入)→ Normal(T6)/ 不在 → Missing(T7・幽霊 normal 防止)。
/// タグ・image_id は不変。normal の復元は ValidationError。純粋関数 TrashTransition.ResolveRestore も検査。
/// </summary>
[Trait("oracle", "S-29")]
[Trait("scope", "cross-factory")]
public sealed class S29TrashRestoreTests
{
    private const string FolderPath = "C:/oracle-s29";

    private sealed class FakeProbe(params string[] existing) : IFilePresenceProbe
    {
        private readonly HashSet<string> _exists = new(existing, StringComparer.OrdinalIgnoreCase);

        public bool Exists(string absoluteImagePath) => _exists.Contains(absoluteImagePath);
    }

    private static string Abs(string rel) => Path.Combine(FolderPath, rel.Replace('/', Path.DirectorySeparatorChar));

    [Fact]
    public async Task 物理存在でNormal_不在でMissing_normalは拒否_タグID不変()
    {
        using var db = new OracleDb();
        Assert.True((await db.Folders.AddAsync(
            new SyncFolder { Id = "fld", Name = "c", Path = FolderPath })).IsSuccess);

        await AddImageAsync(db, "A", "a.png", ImageStatus.Deleted);  // 物理存在させる
        await AddImageAsync(db, "B", "b.png", ImageStatus.Deleted);  // 物理不在
        await AddImageAsync(db, "N", "n.png", ImageStatus.Normal);   // 復元対象外
        await TagAsync(db, "A", "t1");                               // 復元後タグ残存の検査用

        // A のみ物理存在
        var trash = new TrashService(db.Images, db.Folders, new FakeProbe(Abs("a.png")));

        // T6: 存在 → Normal
        var rA = await trash.RestoreAsync("A");
        Assert.True(rA.IsSuccess);
        Assert.Equal(ImageStatus.Normal, rA.Value);
        var a = await db.Images.GetByIdAsync("A");
        Assert.Equal(ImageStatus.Normal, a!.Status);
        Assert.Equal("A", a.Id);                                    // image_id 不変
        Assert.Single(await db.Tags.GetImageTagsAsync("A")); // タグ不変

        // T7: 不在 → Missing(幽霊 normal 防止)
        var rB = await trash.RestoreAsync("B");
        Assert.True(rB.IsSuccess);
        Assert.Equal(ImageStatus.Missing, rB.Value);
        Assert.Equal(ImageStatus.Missing, (await db.Images.GetByIdAsync("B"))!.Status);

        // normal の復元は拒否(deleted 限定)
        var rN = await trash.RestoreAsync("N");
        Assert.False(rN.IsSuccess);
        Assert.Equal(ErrorCode.ValidationError, rN.Error);

        // 純粋関数
        Assert.Equal(ImageStatus.Normal, TrashTransition.ResolveRestore(true));
        Assert.Equal(ImageStatus.Missing, TrashTransition.ResolveRestore(false));
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
