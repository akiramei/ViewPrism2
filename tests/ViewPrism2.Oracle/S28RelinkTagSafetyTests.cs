using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services.Repair;
using ViewPrism2.Infrastructure.Scanning;
using Xunit;

namespace ViewPrism2.Oracle;

/// <summary>
/// S-28: relink 拡張 E2E(scope=cross-factory・OC-20・INV-001/015、仕様 §2.11.2)。工場非開示。
/// 候補=pending(exact-hash)∪ criteria 一致の untagged-normal。**タグ付き候補は除外/確定拒否**(タグ損失防止)。
/// 確定で missing 行へ候補のパス/メタを上書き・status=Normal・candidate_link_id=NULL・候補行削除・
/// **missing 側 image_id 不変・タグ残存**。遷移表 T4 外は ValidationError。
/// </summary>
[Trait("oracle", "S-28")]
[Trait("scope", "cross-factory")]
public sealed class S28RelinkTagSafetyTests
{
    [Fact]
    public async Task 候補はpending_untagged_normalのみ_タグ付き拒否_確定でID不変タグ残存()
    {
        using var db = new OracleDb();
        Assert.True((await db.Folders.AddAsync(
            new SyncFolder { Id = "fld", Name = "c", Path = "C:/oracle-s28" })).IsSuccess);
        Assert.True((await db.Folders.AddAsync(
            new SyncFolder { Id = "fld2", Name = "c2", Path = "C:/oracle-s28b" })).IsSuccess);

        // missing M(タグ 2 種付与)
        await AddImageAsync(db, "M", "fld", "m.png", "H-MISS", ImageStatus.Missing);
        await TagAsync(db, "M", "t1");
        await TagAsync(db, "M", "t2");
        // pending P(同ハッシュ=exact-hash 候補・untagged)
        await AddImageAsync(db, "P", "fld", "p.png", "H-MISS", ImageStatus.Pending);
        // untagged-normal UN(criteria 名前一致・untagged)
        await AddImageAsync(db, "UN", "fld", "photo-un.png", "H-OTHER", ImageStatus.Normal);
        // tagged-normal TN(criteria 名前一致だがタグ付き → 除外/拒否)
        await AddImageAsync(db, "TN", "fld", "photo-tn.png", "H-OTHER2", ImageStatus.Normal);
        await TagAsync(db, "TN", "t3");
        // 別コレクションの normal(criteria では拾われない・確定は別コレクション拒否)
        await AddImageAsync(db, "X", "fld2", "photo-x.png", "H-OTHER3", ImageStatus.Normal);
        // 同コレクションの deleted(確定で status 拒否)
        await AddImageAsync(db, "D", "fld", "photo-d.png", "H-OTHER4", ImageStatus.Deleted);

        var relink = new RelinkService(db.Images, db.Tags);
        var criteria = new SearchCriteria { NameContains = "photo" };

        // 候補: P(exact-hash)+ UN(criteria untagged)。TN(タグ付き)・D(別 status)・X(別コレクション)は除外
        // 安定順: "p.png" < "photo-un.png"('.'(0x2E) < 'h'(0x68))
        var candidates = await relink.GetCandidatesAsync("M", criteria);
        Assert.Equal(["P", "UN"], candidates.Select(c => c.ImageId));

        // タグ付き候補 TN の確定 → ValidationError(タグ損失防止)。DB 無変化
        var rejTagged = await relink.CommitRelinkAsync("M", "TN");
        Assert.False(rejTagged.IsSuccess);
        Assert.Equal(ErrorCode.ValidationError, rejTagged.Error);
        Assert.Equal(ImageStatus.Missing, (await db.Images.GetByIdAsync("M"))!.Status);

        // deleted 候補 D / 別コレクション X の確定 → ValidationError
        Assert.Equal(ErrorCode.ValidationError, (await relink.CommitRelinkAsync("M", "D")).Error);
        Assert.Equal(ErrorCode.ValidationError, (await relink.CommitRelinkAsync("M", "X")).Error);

        // untagged-normal UN で確定 → 成功
        var ok = await relink.CommitRelinkAsync("M", "UN");
        Assert.True(ok.IsSuccess);

        var mAfter = await db.Images.GetByIdAsync("M");
        Assert.NotNull(mAfter);
        Assert.Equal("M", mAfter.Id);                       // image_id 不変(INV-001)
        Assert.Equal(ImageStatus.Normal, mAfter.Status);    // T4: normal 化
        Assert.Equal("photo-un.png", mAfter.RelativePath);  // 候補のパスを所有
        Assert.Null(mAfter.CandidateLinkId);                // candidate_link_id=NULL
        Assert.Null(await db.Images.GetByIdAsync("UN"));    // 候補行は消費削除
        Assert.Equal(2, (await db.Tags.GetImageTagsAsync("M")).Count); // タグ 2 種残存
    }

    private static async Task AddImageAsync(OracleDb db, string id, string folderId, string rel, string hash, ImageStatus status)
        => await db.Images.AddAsync(new ImageRecord
        {
            Id = id,
            SyncFolderId = folderId,
            RelativePath = rel,
            FileName = rel,
            FileSize = 100,
            Hash = hash,
            Status = status,
            CreatedDate = "2026-01-01T00:00:00.000Z",
            ModifiedDate = "2026-01-01T00:00:00.000Z",
        });

    private static async Task TagAsync(OracleDb db, string imageId, string tagId)
    {
        if (await db.Tags.GetByIdAsync(tagId) is null)
        {
            await db.Tags.AddAsync(new Tag { Id = tagId, Name = tagId, Type = TagType.Simple });
        }

        await db.Tags.UpsertImageTagAsync(new ImageTag { ImageId = imageId, TagId = tagId, Value = null });
    }
}
