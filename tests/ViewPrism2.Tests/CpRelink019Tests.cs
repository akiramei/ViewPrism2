using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services.Repair;
using ViewPrism2.Infrastructure.Scanning;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// CP-RELINK-019(M-RELINK-025 / OC-20 / T4): relink が候補=pending∪untagged-normal・
/// タグ付き拒否・missing 側 ID 不変・候補消費削除で確定する(仕様 §2.11.2 / INV-015)。
/// 一時 SQLite に missing+pending+normal(タグ有/無)を投入して確定後 DB 状態を exact 検査する。
/// </summary>
[Trait("cp", "CP-RELINK-019")]
public sealed class CpRelink019Tests
{
    private const string Folder = "folder-1";

    private static ImageRecord Image(
        string id,
        string relativePath,
        ImageStatus status,
        string hash = "samehash",
        long fileSize = 100,
        string syncFolderId = Folder) => new()
        {
            Id = id,
            SyncFolderId = syncFolderId,
            RelativePath = relativePath,
            FileName = Path.GetFileName(relativePath),
            FileSize = fileSize,
            Hash = hash,
            Status = status,
            CreatedDate = "2026-01-01T00:00:00.000Z",
            ModifiedDate = "2026-01-01T00:00:00.000Z",
        };

    private static async Task SeedFolderAsync(TempDb db)
        => await db.Folders.AddAsync(new SyncFolder { Id = Folder, Name = "F", Path = "C:/f" });

    [Fact]
    public async Task 候補列挙_exactHashPendingとcriteria結果_安定順_タグ付き候補は除外()
    {
        using var db = new TempDb();
        await SeedFolderAsync(db);

        var missing = Image("missing-1", "old.jpg", ImageStatus.Missing, hash: "H1");
        await db.Images.AddAsync(missing);

        // exact-hash pending(候補)
        await db.Images.AddAsync(Image("pending-1", "b_new.jpg", ImageStatus.Pending, hash: "H1"));
        // criteria でヒットする untagged-normal(候補)
        await db.Images.AddAsync(Image("normal-1", "a_other.jpg", ImageStatus.Normal, hash: "H2"));
        // criteria でヒットするがタグ付き normal(除外される)
        var tagged = Image("normal-tagged", "c_tagged.jpg", ImageStatus.Normal, hash: "H2");
        await db.Images.AddAsync(tagged);
        var tag = new Tag { Id = "tag-1", Name = "T", Type = TagType.Simple };
        await db.Tags.AddAsync(tag);
        await db.Tags.UpsertImageTagAsync(new ImageTag { ImageId = tagged.Id, TagId = tag.Id });

        var service = new RelinkService(db.Images, db.Tags);
        // criteria=拡張子 jpg(全候補対象)。pending(H1)も criteria 候補(H2 normal)も拾う
        var candidates = await service.GetCandidatesAsync(
            missing.Id, new SearchCriteria { Extension = "jpg" });

        // 候補は pending-1 と normal-1。タグ付き normal-tagged は除外。
        // 安定順: relative_path 昇順 a_other.jpg(normal-1) < b_new.jpg(pending-1)
        Assert.Equal(["normal-1", "pending-1"], candidates.Select(c => c.ImageId));
    }

    [Fact]
    public async Task 確定_pending消費_missing行は候補パスメタとstatusNormal_candidateLinkNull_pending行削除_id不変()
    {
        using var db = new TempDb();
        await SeedFolderAsync(db);

        var missing = Image("missing-1", "old.jpg", ImageStatus.Missing, hash: "H1") with { CandidateLinkId = null };
        await db.Images.AddAsync(missing);
        var pending = Image("pending-1", "new_path.jpg", ImageStatus.Pending, hash: "H1", fileSize: 222);
        await db.Images.AddAsync(pending);

        // missing 行へタグを付けておき、image_id 不変=タグ参照保全を確認する
        var tag = new Tag { Id = "tag-1", Name = "T", Type = TagType.Simple };
        await db.Tags.AddAsync(tag);
        await db.Tags.UpsertImageTagAsync(new ImageTag { ImageId = missing.Id, TagId = tag.Id });

        var service = new RelinkService(db.Images, db.Tags);
        var result = await service.CommitRelinkAsync(missing.Id, pending.Id);
        Assert.True(result.IsSuccess);

        var relinked = await db.Images.GetByIdAsync(missing.Id);
        Assert.NotNull(relinked);
        Assert.Equal("missing-1", relinked!.Id);                     // image_id 不変(INV-001)
        Assert.Equal(ImageStatus.Normal, relinked.Status);            // status=normal
        Assert.Equal("new_path.jpg", relinked.RelativePath);          // 候補のパス上書き
        Assert.Equal(222, relinked.FileSize);                         // 候補のメタ上書き
        Assert.Null(relinked.CandidateLinkId);                        // candidate_link_id=NULL

        Assert.Null(await db.Images.GetByIdAsync(pending.Id));        // pending 行削除

        // missing 側 image_id のタグが保全されている
        var tags = await db.Tags.GetImageTagsAsync(missing.Id);
        Assert.Contains(tags, t => t.TagId == tag.Id);
    }

    [Fact]
    public async Task 確定_untaggedNormal消費_同様にnormal候補行を削除()
    {
        using var db = new TempDb();
        await SeedFolderAsync(db);

        var missing = Image("missing-1", "old.jpg", ImageStatus.Missing, hash: "H1");
        await db.Images.AddAsync(missing);
        var normal = Image("normal-1", "kept.jpg", ImageStatus.Normal, hash: "H2", fileSize: 333);
        await db.Images.AddAsync(normal);

        var service = new RelinkService(db.Images, db.Tags);
        var result = await service.CommitRelinkAsync(missing.Id, normal.Id);
        Assert.True(result.IsSuccess);

        var relinked = await db.Images.GetByIdAsync(missing.Id);
        Assert.Equal(ImageStatus.Normal, relinked!.Status);
        Assert.Equal("kept.jpg", relinked.RelativePath);
        Assert.Equal(333, relinked.FileSize);
        Assert.Null(await db.Images.GetByIdAsync(normal.Id));         // normal 候補行削除
    }

    [Fact]
    public async Task タグ付き候補の確定要求はValidationError_DB無変化()
    {
        using var db = new TempDb();
        await SeedFolderAsync(db);

        var missing = Image("missing-1", "old.jpg", ImageStatus.Missing, hash: "H1");
        await db.Images.AddAsync(missing);
        var tagged = Image("normal-tagged", "tagged.jpg", ImageStatus.Normal, hash: "H2");
        await db.Images.AddAsync(tagged);
        var tag = new Tag { Id = "tag-1", Name = "T", Type = TagType.Simple };
        await db.Tags.AddAsync(tag);
        await db.Tags.UpsertImageTagAsync(new ImageTag { ImageId = tagged.Id, TagId = tag.Id });

        var service = new RelinkService(db.Images, db.Tags);
        var result = await service.CommitRelinkAsync(missing.Id, tagged.Id);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.ValidationError, result.Error);

        // DB 無変化: missing は missing のまま、tagged は normal のまま存在
        Assert.Equal(ImageStatus.Missing, (await db.Images.GetByIdAsync(missing.Id))!.Status);
        var taggedAfter = await db.Images.GetByIdAsync(tagged.Id);
        Assert.NotNull(taggedAfter);
        Assert.Equal(ImageStatus.Normal, taggedAfter!.Status);
    }

    [Fact]
    public async Task 遷移表外_missingでない_別コレクション_deleted候補はValidationError()
    {
        using var db = new TempDb();
        await SeedFolderAsync(db);
        await db.Folders.AddAsync(new SyncFolder { Id = "other", Name = "O", Path = "C:/o" });

        var normalTarget = Image("normal-target", "n.jpg", ImageStatus.Normal, hash: "H1");
        await db.Images.AddAsync(normalTarget);
        var missing = Image("missing-1", "old.jpg", ImageStatus.Missing, hash: "H1");
        await db.Images.AddAsync(missing);
        var otherFolderPending = Image("p-other", "p.jpg", ImageStatus.Pending, hash: "H1", syncFolderId: "other");
        await db.Images.AddAsync(otherFolderPending);
        var deletedCandidate = Image("deleted-1", "d.jpg", ImageStatus.Deleted, hash: "H1");
        await db.Images.AddAsync(deletedCandidate);
        var pending = Image("pending-1", "valid.jpg", ImageStatus.Pending, hash: "H1");
        await db.Images.AddAsync(pending);

        var service = new RelinkService(db.Images, db.Tags);

        // 対象が missing でない(normal を対象に)→ 拒否
        var notMissing = await service.CommitRelinkAsync(normalTarget.Id, pending.Id);
        Assert.Equal(ErrorCode.ValidationError, notMissing.Error);

        // 別コレクションの候補 → 拒否
        var crossCollection = await service.CommitRelinkAsync(missing.Id, otherFolderPending.Id);
        Assert.Equal(ErrorCode.ValidationError, crossCollection.Error);

        // deleted 候補(許可外 status)→ 拒否
        var deleted = await service.CommitRelinkAsync(missing.Id, deletedCandidate.Id);
        Assert.Equal(ErrorCode.ValidationError, deleted.Error);

        // missing は無変化
        Assert.Equal(ImageStatus.Missing, (await db.Images.GetByIdAsync(missing.Id))!.Status);
    }

    [Fact]
    public async Task GetCandidates_対象がmissingでなければ空列()
    {
        using var db = new TempDb();
        await SeedFolderAsync(db);
        var normal = Image("normal-1", "n.jpg", ImageStatus.Normal, hash: "H1");
        await db.Images.AddAsync(normal);

        var service = new RelinkService(db.Images, db.Tags);
        var candidates = await service.GetCandidatesAsync(normal.Id, new SearchCriteria { Extension = "jpg" });

        Assert.Empty(candidates);
    }
}
