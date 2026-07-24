using System.Text;
using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services.Repair;
using ViewPrism2.Infrastructure.Scanning;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// ECO-139 から ECO-140/E-RELINK-007 へ移管した意味論ベクタ。
/// 高信頼選別・曖昧除外・原子 batch・可逆性を RelinkService API で固定する。
/// </summary>
[Trait("cp", "CP-INTEGRITY-036")]
public sealed class CpRelinkUnifiedBatchTests : IDisposable
{
    private readonly TempDb _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task 高信頼はpendingのnewかつcandidateありだけ()
    {
        var folder = await AddFolderAsync();
        var candidate = await AddImageAsync(folder, "missing.jpg", ImageStatus.Missing);
        var eligible = await AddImageAsync(
            folder, "eligible.jpg", ImageStatus.Pending, PendingOrigin.New, candidate.Id);
        var noCandidate = await AddImageAsync(
            folder, "no-candidate.jpg", ImageStatus.Pending, PendingOrigin.New);
        var changed = await AddImageAsync(
            folder, "changed.jpg", ImageStatus.Pending, PendingOrigin.Changed, candidate.Id);
        var reappeared = await AddImageAsync(
            folder, "reappeared.jpg", ImageStatus.Pending, PendingOrigin.Reappeared, candidate.Id);
        var normal = await AddImageAsync(
            folder, "normal.jpg", ImageStatus.Normal, PendingOrigin.New, candidate.Id);

        Assert.True(RelinkService.IsHighConfidence(eligible));
        Assert.False(RelinkService.IsHighConfidence(noCandidate));
        Assert.False(RelinkService.IsHighConfidence(changed));
        Assert.False(RelinkService.IsHighConfidence(reappeared));
        Assert.False(RelinkService.IsHighConfidence(normal));
    }

    [Fact]
    public async Task タグ付き高信頼候補は自動選別から除外され確定時に増えた場合も全件拒否する()
    {
        var folder = await AddFolderAsync();
        var missing = await AddImageAsync(folder, "missing.jpg", ImageStatus.Missing);
        var candidate = await AddImageAsync(
            folder, "candidate.jpg", ImageStatus.Pending, PendingOrigin.New, missing.Id);
        var tag = new Tag { Id = IdGenerator.NewId(), Name = "guard", Type = TagType.Simple };
        await _db.Tags.AddAsync(tag);
        await _db.Tags.UpsertImageTagAsync(new ImageTag { ImageId = candidate.Id, TagId = tag.Id });
        var relink = Relink();

        var selected = await relink.GetUniquelyRelinkableAsync(
            [missing, candidate],
            new Dictionary<string, ImageRecord>(StringComparer.Ordinal) { [missing.Id] = missing },
            TestContext.Current.CancellationToken);
        var result = await relink.ApplyIntegrityReviewBatchAsync(
            [new AutoRepairPair(missing.Id, candidate.Id)],
            []);

        Assert.Empty(selected);
        Assert.False(result.IsSuccess);
        Assert.Equal(2, await _db.Images.CountIntegrityReviewEventsAsync(
            folder.Id, TestContext.Current.CancellationToken));
        Assert.Equal(ImageStatus.Missing, (await _db.Images.GetByIdAsync(missing.Id))!.Status);
        Assert.Equal(ImageStatus.Pending, (await _db.Images.GetByIdAsync(candidate.Id))!.Status);
        Assert.Single(await _db.Tags.GetImageTagsAsync(candidate.Id));
    }

    [Fact]
    public async Task バッチ再リンクはmissing側IDとタグを保持しpendingを削除して非対象を変えない()
    {
        var folder = await AddFolderAsync();
        var firstMissing = await AddImageAsync(folder, "missing-first.jpg", ImageStatus.Missing);
        var secondMissing = await AddImageAsync(folder, "missing-second.jpg", ImageStatus.Missing);
        var first = await AddImageAsync(
            folder, "first.jpg", ImageStatus.Pending, PendingOrigin.New, firstMissing.Id);
        var second = await AddImageAsync(
            folder, "second.jpg", ImageStatus.Pending, PendingOrigin.New, secondMissing.Id);
        var excluded = await AddImageAsync(
            folder, "excluded.jpg", ImageStatus.Pending, PendingOrigin.Changed, firstMissing.Id);
        var tag = new Tag { Id = IdGenerator.NewId(), Name = "kept", Type = TagType.Simple };
        await _db.Tags.AddAsync(tag);
        await _db.Tags.UpsertImageTagAsync(new ImageTag { ImageId = firstMissing.Id, TagId = tag.Id });

        var result = await Relink().ApplyRelinkBatchAsync([first, excluded, second]);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(2, result.Value);
        var relinkedFirst = Assert.IsType<ImageRecord>(await _db.Images.GetByIdAsync(firstMissing.Id));
        Assert.Equal(firstMissing.Id, relinkedFirst.Id);
        Assert.Equal("first.jpg", relinkedFirst.RelativePath);
        Assert.Equal(ImageStatus.Normal, relinkedFirst.Status);
        Assert.Single(await _db.Tags.GetImageTagsAsync(firstMissing.Id));
        Assert.Null(await _db.Images.GetByIdAsync(first.Id));
        Assert.Equal(ImageStatus.Normal, (await _db.Images.GetByIdAsync(secondMissing.Id))!.Status);
        Assert.Null(await _db.Images.GetByIdAsync(second.Id));
        Assert.Equal(ImageStatus.Pending, (await _db.Images.GetByIdAsync(excluded.Id))!.Status);
    }

    [Fact]
    public async Task 同じmissingへ複数newが一致する曖昧候補は全て自動対象から除外()
    {
        var folder = await AddFolderAsync();
        var ambiguousMissing = await AddImageAsync(folder, "ambiguous-missing.jpg", ImageStatus.Missing);
        var ambiguousFirst = await AddImageAsync(
            folder, "ambiguous-first.jpg", ImageStatus.Pending, PendingOrigin.New, ambiguousMissing.Id);
        var ambiguousSecond = await AddImageAsync(
            folder, "ambiguous-second.jpg", ImageStatus.Pending, PendingOrigin.New, ambiguousMissing.Id);
        var uniqueMissing = await AddImageAsync(folder, "unique-missing.jpg", ImageStatus.Missing);
        var unique = await AddImageAsync(
            folder, "unique.jpg", ImageStatus.Pending, PendingOrigin.New, uniqueMissing.Id);

        var result = await Relink().ApplyRelinkBatchAsync([ambiguousFirst, unique, ambiguousSecond]);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(1, result.Value);
        Assert.Equal(ImageStatus.Missing, (await _db.Images.GetByIdAsync(ambiguousMissing.Id))!.Status);
        Assert.Equal(ImageStatus.Pending, (await _db.Images.GetByIdAsync(ambiguousFirst.Id))!.Status);
        Assert.Equal(ImageStatus.Pending, (await _db.Images.GetByIdAsync(ambiguousSecond.Id))!.Status);
        Assert.Equal(ImageStatus.Normal, (await _db.Images.GetByIdAsync(uniqueMissing.Id))!.Status);
    }

    [Fact]
    public async Task candidate付与後にhashが変わったstale組は全件保持する()
    {
        var folder = await AddFolderAsync();
        var validMissing = await AddImageAsync(folder, "valid-missing.jpg", ImageStatus.Missing);
        var valid = await AddImageAsync(
            folder, "valid.jpg", ImageStatus.Pending, PendingOrigin.New, validMissing.Id);
        var staleMissing = await AddImageAsync(folder, "stale-missing.jpg", ImageStatus.Missing);
        var stale = await AddImageAsync(
            folder, "stale.jpg", ImageStatus.Pending, PendingOrigin.New, staleMissing.Id);
        await _db.Images.UpdateFileMetaAsync(
            stale.Id, new string('b', 64), stale.FileSize, stale.ModifiedDate);

        var result = await Relink().ApplyRelinkBatchAsync([valid, stale]);

        Assert.False(result.IsSuccess);
        Assert.Equal(ImageStatus.Missing, (await _db.Images.GetByIdAsync(validMissing.Id))!.Status);
        Assert.Equal(ImageStatus.Pending, (await _db.Images.GetByIdAsync(valid.Id))!.Status);
        Assert.Equal(ImageStatus.Missing, (await _db.Images.GetByIdAsync(staleMissing.Id))!.Status);
        Assert.Equal(ImageStatus.Pending, (await _db.Images.GetByIdAsync(stale.Id))!.Status);
    }

    [Theory]
    [InlineData("status")]
    [InlineData("origin")]
    [InlineData("candidate")]
    public async Task postSnapshot条件が変われば先行する有効組も含め全rollback(string staleKind)
    {
        var folder = await AddFolderAsync();
        var validMissing = await AddImageAsync(folder, "valid-missing.jpg", ImageStatus.Missing);
        var valid = await AddImageAsync(
            folder, "valid.jpg", ImageStatus.Pending, PendingOrigin.New, validMissing.Id);
        var staleMissing = await AddImageAsync(folder, "stale-missing.jpg", ImageStatus.Missing);
        var stale = await AddImageAsync(
            folder, "stale.jpg", ImageStatus.Pending, PendingOrigin.New, staleMissing.Id);
        var otherMissing = await AddImageAsync(folder, "other.jpg", ImageStatus.Missing);
        var staleSnapshot = stale;
        switch (staleKind)
        {
            case "status":
                await _db.Images.UpdateStatusAsync(stale.Id, ImageStatus.Normal);
                break;
            case "origin":
                await _db.Images.ApplyScanBatchAsync(new ScanMutationBatch(
                    [], [], [new ScanStatusUpdate(
                        stale.Id, ImageStatus.Pending, PendingOrigin.Changed)], []));
                break;
            case "candidate":
                staleSnapshot = stale with { CandidateLinkId = otherMissing.Id };
                break;
        }

        var result = await Relink().ApplyRelinkBatchAsync([valid, staleSnapshot]);

        Assert.False(result.IsSuccess);
        Assert.Equal(ImageStatus.Missing, (await _db.Images.GetByIdAsync(validMissing.Id))!.Status);
        Assert.Equal(ImageStatus.Pending, (await _db.Images.GetByIdAsync(valid.Id))!.Status);
        Assert.Equal(ImageStatus.Missing, (await _db.Images.GetByIdAsync(staleMissing.Id))!.Status);
    }

    [Fact]
    public async Task SQLite変数分割を跨ぐバッチでもstale1件なら全rollback()
    {
        var folder = await AddFolderAsync();
        var pairs = Enumerable.Range(0, 501).Select(index =>
        {
            var missing = NewImage(folder, $"missing-{index:000}.jpg", ImageStatus.Missing);
            var pending = NewImage(
                folder,
                $"pending-{index:000}.jpg",
                ImageStatus.Pending,
                PendingOrigin.New,
                missing.Id);
            return (Missing: missing, Pending: pending);
        }).ToList();
        await _db.Images.ApplyScanBatchAsync(new ScanMutationBatch(
            pairs.SelectMany(pair => new[] { pair.Missing, pair.Pending }).ToList(), [], [], []));
        await _db.Images.UpdateStatusAsync(pairs[^1].Missing.Id, ImageStatus.Normal);

        var result = await Relink().ApplyRelinkBatchAsync(pairs.Select(pair => pair.Pending));

        Assert.False(result.IsSuccess);
        Assert.Equal(ImageStatus.Missing, (await _db.Images.GetByIdAsync(pairs[0].Missing.Id))!.Status);
        Assert.Equal(ImageStatus.Pending, (await _db.Images.GetByIdAsync(pairs[0].Pending.Id))!.Status);
        Assert.Equal(ImageStatus.Normal, (await _db.Images.GetByIdAsync(pairs[^1].Missing.Id))!.Status);
    }

    [Fact]
    public async Task 一括再リンク後も既存スキャン経路で再リンクし直せる()
    {
        var root = Path.Combine(
            Path.GetTempPath(), "ViewPrism2.Tests", Guid.NewGuid().ToString("D"), "files");
        Directory.CreateDirectory(root);
        try
        {
            var oldPath = Path.Combine(root, "old.jpg");
            await File.WriteAllBytesAsync(
                oldPath, Encoding.UTF8.GetBytes("same"), TestContext.Current.CancellationToken);
            var folder = await AddFolderAsync(root);
            var scan = new ScanService(_db.Folders, _db.Images, _db.Clock);
            Assert.True((await scan.ScanAsync(folder.Id, null, TestContext.Current.CancellationToken)).IsSuccess);

            var movedPath = Path.Combine(root, "moved.jpg");
            File.Move(oldPath, movedPath);
            Assert.True((await scan.ScanAsync(folder.Id, null, TestContext.Current.CancellationToken)).IsSuccess);
            var moved = (await _db.Images.GetByFolderAsync(folder.Id))
                .Single(i => i.FileName == "moved.jpg");
            Assert.True(RelinkService.IsHighConfidence(moved));
            var originalId = moved.CandidateLinkId!;
            Assert.True((await Relink().ApplyRelinkBatchAsync([moved])).IsSuccess);

            var movedAgainPath = Path.Combine(root, "moved-again.jpg");
            File.Move(movedPath, movedAgainPath);
            Assert.True((await scan.ScanAsync(folder.Id, null, TestContext.Current.CancellationToken)).IsSuccess);
            var movedAgain = (await _db.Images.GetByFolderAsync(folder.Id))
                .Single(i => i.FileName == "moved-again.jpg");
            Assert.Equal(originalId, movedAgain.CandidateLinkId);
            Assert.True((await Relink().ApplyRelinkBatchAsync([movedAgain])).IsSuccess);
            Assert.Equal("moved-again.jpg", (await _db.Images.GetByIdAsync(originalId))!.RelativePath);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(root)!, recursive: true);
        }
    }

    private RelinkService Relink() => new(_db.Images, _db.Tags);

    private async Task<SyncFolder> AddFolderAsync(string? path = null)
    {
        var folder = new SyncFolder
        {
            Id = IdGenerator.NewId(),
            Name = "fixture",
            Path = path ?? @"C:\fixture",
        };
        Assert.True((await _db.Folders.AddAsync(folder)).IsSuccess);
        return folder;
    }

    private async Task<ImageRecord> AddImageAsync(
        SyncFolder folder,
        string name,
        ImageStatus status,
        PendingOrigin? origin = null,
        string? candidateId = null)
    {
        var image = NewImage(folder, name, status, origin, candidateId);
        await _db.Images.AddAsync(image);
        return image;
    }

    private static ImageRecord NewImage(
        SyncFolder folder,
        string name,
        ImageStatus status,
        PendingOrigin? origin = null,
        string? candidateId = null) => new()
        {
            Id = IdGenerator.NewId(),
            SyncFolderId = folder.Id,
            RelativePath = name,
            FileName = name,
            FileSize = 10,
            Hash = new string('a', 64),
            Status = status,
            PendingOrigin = origin,
            CandidateLinkId = candidateId,
            CreatedDate = "2026-07-23T00:00:00.000Z",
            ModifiedDate = "2026-07-23T00:00:00.000Z",
        };
}
