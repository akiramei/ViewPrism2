using System.Text;
using ViewPrism2.App.Services;
using ViewPrism2.App.ViewModels;
using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using ViewPrism2.Core.Services.Repair;
using ViewPrism2.Infrastructure.Scanning;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// ECO-139/CP-SCAN-004: high-confidence=new+candidate の厳格選別と、
/// 候補 missing への原子バッチ再リンク・曖昧候補除外を固定する。
/// </summary>
[Trait("cp", "CP-PENDING-AUTO-035")]
public sealed class CpPendingAutoAdjudicationTests : IDisposable
{
    private readonly TempDb _db = new();

    public void Dispose() => _db.Dispose();

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
        var image = new ImageRecord
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
        await _db.Images.AddAsync(image);
        return image;
    }

    [Fact]
    [Trait("cp", "CP-SCAN-004")]
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

        Assert.True(PendingReviewService.IsHighConfidence(eligible));
        Assert.False(PendingReviewService.IsHighConfidence(noCandidate));
        Assert.False(PendingReviewService.IsHighConfidence(changed));
        Assert.False(PendingReviewService.IsHighConfidence(reappeared));
        Assert.False(PendingReviewService.IsHighConfidence(normal));
    }

    [Fact]
    [Trait("cp", "CP-SCAN-004")]
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

        var review = new PendingReviewService(_db.Images);
        var result = await review.RelinkHighConfidenceAsync([first, excluded, second]);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(2, result.Value);
        var relinkedFirst = Assert.IsType<ImageRecord>(await _db.Images.GetByIdAsync(firstMissing.Id));
        Assert.Equal(firstMissing.Id, relinkedFirst.Id);
        Assert.Equal("first.jpg", relinkedFirst.RelativePath);
        Assert.Equal(ImageStatus.Normal, relinkedFirst.Status);
        Assert.Null(relinkedFirst.CandidateLinkId);
        Assert.Null(relinkedFirst.PendingOrigin);
        Assert.Single(await _db.Tags.GetImageTagsAsync(firstMissing.Id));
        Assert.Null(await _db.Images.GetByIdAsync(first.Id));

        var relinkedSecond = Assert.IsType<ImageRecord>(await _db.Images.GetByIdAsync(secondMissing.Id));
        Assert.Equal(secondMissing.Id, relinkedSecond.Id);
        Assert.Equal("second.jpg", relinkedSecond.RelativePath);
        Assert.Equal(ImageStatus.Normal, relinkedSecond.Status);
        Assert.Null(await _db.Images.GetByIdAsync(second.Id));

        var unchanged = Assert.IsType<ImageRecord>(await _db.Images.GetByIdAsync(excluded.Id));
        Assert.Equal(ImageStatus.Pending, unchanged.Status);
        Assert.Equal(PendingOrigin.Changed, unchanged.PendingOrigin);
        Assert.Equal(firstMissing.Id, unchanged.CandidateLinkId);
    }

    [Fact]
    [Trait("cp", "CP-SCAN-004")]
    public async Task 同じmissingへ複数newが一致する曖昧候補は自動対象から除外()
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

        var result = await new PendingReviewService(_db.Images)
            .RelinkHighConfidenceAsync([ambiguousFirst, unique, ambiguousSecond]);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(1, result.Value);
        Assert.Equal(ImageStatus.Missing, (await _db.Images.GetByIdAsync(ambiguousMissing.Id))!.Status);
        Assert.Equal(ImageStatus.Pending, (await _db.Images.GetByIdAsync(ambiguousFirst.Id))!.Status);
        Assert.Equal(ImageStatus.Pending, (await _db.Images.GetByIdAsync(ambiguousSecond.Id))!.Status);
        Assert.Equal(ImageStatus.Normal, (await _db.Images.GetByIdAsync(uniqueMissing.Id))!.Status);
        Assert.Null(await _db.Images.GetByIdAsync(unique.Id));

        // UI snapshot 後に同じ candidate を指す行が増えた競合も repository 境界で拒否する。
        var staleSnapshot = await new PendingReviewService(_db.Images)
            .RelinkHighConfidenceAsync([ambiguousFirst]);
        Assert.False(staleSnapshot.IsSuccess);
        Assert.Equal(ImageStatus.Missing, (await _db.Images.GetByIdAsync(ambiguousMissing.Id))!.Status);
        Assert.Equal(ImageStatus.Pending, (await _db.Images.GetByIdAsync(ambiguousFirst.Id))!.Status);
        Assert.Equal(ImageStatus.Pending, (await _db.Images.GetByIdAsync(ambiguousSecond.Id))!.Status);
    }

    [Fact]
    [Trait("cp", "CP-SCAN-004")]
    public async Task 候補付与後にpendingのhashが変わったstale組は再リンクせず全件保持()
    {
        var folder = await AddFolderAsync();
        var missing = await AddImageAsync(folder, "missing.jpg", ImageStatus.Missing);
        var pending = await AddImageAsync(
            folder, "pending.jpg", ImageStatus.Pending, PendingOrigin.New, missing.Id);
        await _db.Images.UpdateFileMetaAsync(
            pending.Id, new string('b', 64), pending.FileSize, pending.ModifiedDate);

        var result = await new PendingReviewService(_db.Images).RelinkHighConfidenceAsync([pending]);

        Assert.False(result.IsSuccess);
        Assert.Equal(ImageStatus.Missing, (await _db.Images.GetByIdAsync(missing.Id))!.Status);
        var unchanged = Assert.IsType<ImageRecord>(await _db.Images.GetByIdAsync(pending.Id));
        Assert.Equal(ImageStatus.Pending, unchanged.Status);
        Assert.Equal(new string('b', 64), unchanged.Hash);
        Assert.Equal(missing.Id, unchanged.CandidateLinkId);
    }

    [Theory]
    [InlineData("status")]
    [InlineData("origin")]
    [InlineData("candidate")]
    [Trait("cp", "CP-SCAN-004")]
    public async Task postSnapshotでpending条件が変われば先行する有効組も含め全件rollback(
        string staleKind)
    {
        var folder = await AddFolderAsync();
        var validMissing = await AddImageAsync(folder, "valid-missing.jpg", ImageStatus.Missing);
        var validPending = await AddImageAsync(
            folder, "valid-pending.jpg", ImageStatus.Pending, PendingOrigin.New, validMissing.Id);
        var staleMissing = await AddImageAsync(folder, "stale-missing.jpg", ImageStatus.Missing);
        var stalePending = await AddImageAsync(
            folder, "stale-pending.jpg", ImageStatus.Pending, PendingOrigin.New, staleMissing.Id);
        var otherMissing = await AddImageAsync(folder, "other-missing.jpg", ImageStatus.Missing);
        var staleSnapshot = stalePending;
        switch (staleKind)
        {
            case "status":
                await _db.Images.UpdateStatusAsync(stalePending.Id, ImageStatus.Normal);
                break;
            case "origin":
                await _db.Images.ApplyScanBatchAsync(new ScanMutationBatch(
                    [], [], [new ScanStatusUpdate(
                        stalePending.Id, ImageStatus.Pending, PendingOrigin.Changed)], []));
                break;
            case "candidate":
                staleSnapshot = stalePending with { CandidateLinkId = otherMissing.Id };
                break;
            default:
                throw new InvalidOperationException(staleKind);
        }

        var result = await new PendingReviewService(_db.Images)
            .RelinkHighConfidenceAsync([validPending, staleSnapshot]);

        Assert.False(result.IsSuccess);
        Assert.Equal(ImageStatus.Missing, (await _db.Images.GetByIdAsync(validMissing.Id))!.Status);
        Assert.Equal(ImageStatus.Pending, (await _db.Images.GetByIdAsync(validPending.Id))!.Status);
        Assert.Equal(validMissing.Id, (await _db.Images.GetByIdAsync(validPending.Id))!.CandidateLinkId);
        Assert.Equal(ImageStatus.Missing, (await _db.Images.GetByIdAsync(staleMissing.Id))!.Status);
    }

    [Fact]
    [Trait("cp", "CP-SCAN-004")]
    public async Task バッチ中にmissing限定を満たさない行があれば全件rollback()
    {
        var folder = await AddFolderAsync();
        var pairs = Enumerable.Range(0, 501).Select(index =>
        {
            var missingId = IdGenerator.NewId();
            var missing = new ImageRecord
            {
                Id = missingId,
                SyncFolderId = folder.Id,
                RelativePath = $"missing-{index:000}.jpg",
                FileName = $"missing-{index:000}.jpg",
                FileSize = 10,
                Hash = new string('b', 64),
                Status = ImageStatus.Missing,
                CreatedDate = "2026-07-23T00:00:00.000Z",
                ModifiedDate = "2026-07-23T00:00:00.000Z",
            };
            var pending = missing with
            {
                Id = IdGenerator.NewId(),
                RelativePath = $"pending-{index:000}.jpg",
                FileName = $"pending-{index:000}.jpg",
                Status = ImageStatus.Pending,
                PendingOrigin = PendingOrigin.New,
                CandidateLinkId = missingId,
            };
            return (Missing: missing, Pending: pending);
        }).ToList();
        await _db.Images.ApplyScanBatchAsync(new ScanMutationBatch(
            pairs.SelectMany(pair => new[] { pair.Missing, pair.Pending }).ToList(), [], [], []));
        var first = pairs[0];
        var stale = pairs[^1]; // missing 限定検証でバッチ全体を拒否させる
        await _db.Images.UpdateStatusAsync(stale.Missing.Id, ImageStatus.Normal);

        var review = new PendingReviewService(_db.Images);
        var result = await review.RelinkHighConfidenceAsync(pairs.Select(pair => pair.Pending));

        Assert.False(result.IsSuccess);
        Assert.Equal(ImageStatus.Missing, (await _db.Images.GetByIdAsync(first.Missing.Id))!.Status);
        Assert.Equal(ImageStatus.Pending, (await _db.Images.GetByIdAsync(first.Pending.Id))!.Status);
        Assert.Equal(first.Missing.Id, (await _db.Images.GetByIdAsync(first.Pending.Id))!.CandidateLinkId);
        Assert.Equal(ImageStatus.Normal, (await _db.Images.GetByIdAsync(stale.Missing.Id))!.Status);
        Assert.Equal(ImageStatus.Pending, (await _db.Images.GetByIdAsync(stale.Pending.Id))!.Status);
    }

    [Fact]
    [Trait("cp", "CP-SCAN-004")]
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
            var moved = (await _db.Images.GetByFolderAsync(folder.Id)).Single(i => i.FileName == "moved.jpg");
            Assert.True(PendingReviewService.IsHighConfidence(moved));
            var originalId = moved.CandidateLinkId!;

            var firstRelink = await new PendingReviewService(_db.Images).RelinkHighConfidenceAsync([moved]);
            Assert.True(firstRelink.IsSuccess, firstRelink.Message);
            var afterFirst = Assert.IsType<ImageRecord>(await _db.Images.GetByIdAsync(originalId));
            Assert.Equal(ImageStatus.Normal, afterFirst.Status);
            Assert.Equal("moved.jpg", afterFirst.RelativePath);
            Assert.Null(await _db.Images.GetByIdAsync(moved.Id));

            var movedAgainPath = Path.Combine(root, "moved-again.jpg");
            File.Move(movedPath, movedAgainPath);
            Assert.True((await scan.ScanAsync(folder.Id, null, TestContext.Current.CancellationToken)).IsSuccess);
            var movedAgain = (await _db.Images.GetByFolderAsync(folder.Id))
                .Single(i => i.FileName == "moved-again.jpg");
            Assert.Equal(originalId, movedAgain.CandidateLinkId);

            var secondRelink = await new PendingReviewService(_db.Images)
                .RelinkHighConfidenceAsync([movedAgain]);
            Assert.True(secondRelink.IsSuccess, secondRelink.Message);
            var afterSecond = Assert.IsType<ImageRecord>(await _db.Images.GetByIdAsync(originalId));
            Assert.Equal(ImageStatus.Normal, afterSecond.Status);
            Assert.Equal("moved-again.jpg", afterSecond.RelativePath);
            Assert.Null(await _db.Images.GetByIdAsync(movedAgain.Id));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(root)!, recursive: true);
        }
    }
}

/// <summary>
/// ECO-139/CP-UI-G1: callout・グループ・確認対象・適用対象が同じ集合を共有することを固定する。
/// </summary>
[Trait("cp", "CP-PENDING-AUTO-035")]
public sealed class CpPendingAutoAdjudicationViewModelTests : IDisposable
{
    private readonly TempDb _db = new();

    public void Dispose() => _db.Dispose();

    private async Task<(PendingReviewViewModel Vm, RecordingWindows Windows, SyncFolder Folder)>
        BuildVmAsync(
            bool confirm,
            bool includeHighConfidence = true,
            bool includeIndividual = true)
    {
        var folder = new SyncFolder { Id = "col-1", Name = "fixture", Path = @"C:\Photos" };
        Assert.True((await _db.Folders.AddAsync(folder)).IsSuccess);
        var candidate = new ImageRecord
        {
            Id = "candidate-1",
            SyncFolderId = folder.Id,
            RelativePath = "archive/original.jpg",
            FileName = "original.jpg",
            FileSize = 10,
            Hash = "same",
            Status = ImageStatus.Missing,
            CreatedDate = "2026-07-23T00:00:00.000Z",
            ModifiedDate = "2026-07-23T00:00:00.000Z",
        };
        await _db.Images.AddAsync(candidate);
        var tag = new Tag { Id = "tag-1", Name = "kept", Type = TagType.Simple };
        await _db.Tags.AddAsync(tag);
        await _db.Tags.UpsertImageTagAsync(new ImageTag { ImageId = candidate.Id, TagId = tag.Id });
        if (includeHighConfidence)
        {
            await AddPendingAsync(folder, "z-auto.jpg", PendingOrigin.New, candidate.Id);
        }

        if (includeIndividual)
        {
            await AddPendingAsync(folder, "a-changed.jpg", PendingOrigin.Changed, null);
            await AddPendingAsync(folder, "b-new.jpg", PendingOrigin.New, null);
        }

        var windows = new RecordingWindows { ConfirmResult = confirm };
        var vm = new PendingReviewViewModel(
            new PendingReviewService(_db.Images), _db.Images, _db.Tags,
            TestLoc.Ja(), windows, folder);
        await vm.LoadAsync();
        return (vm, windows, folder);
    }

    private async Task AddPendingAsync(
        SyncFolder folder, string name, PendingOrigin origin, string? candidateId)
    {
        await _db.Images.AddAsync(new ImageRecord
        {
            Id = IdGenerator.NewId(),
            SyncFolderId = folder.Id,
            RelativePath = name,
            FileName = name,
            FileSize = 10,
            Hash = candidateId is null ? "hash-" + name : "same",
            Status = ImageStatus.Pending,
            PendingOrigin = origin,
            CandidateLinkId = candidateId,
            CreatedDate = "2026-07-23T00:00:00.000Z",
            ModifiedDate = "2026-07-23T00:00:00.000Z",
        });
    }

    [Fact]
    [Trait("cp", "CP-UI-G1")]
    public async Task 高信頼を先頭グループへ分けcallout件数と一致させる()
    {
        var (vm, _, _) = await BuildVmAsync(confirm: false);

        Assert.True(vm.HasHighConfidence);
        Assert.Equal(1, vm.HighConfidenceCount);
        Assert.Equal(2, vm.IndividualCount);
        Assert.Equal("z-auto.jpg", Assert.Single(vm.HighConfidenceItems).FileName);
        Assert.Equal("original.jpg", vm.HighConfidenceItems[0].CandidateFileName);
        Assert.Equal(["z-auto.jpg", "a-changed.jpg", "b-new.jpg"], vm.Items.Select(i => i.FileName));
        Assert.Equal(vm.HighConfidenceCount, vm.HighConfidenceItems.Count);
    }

    [Fact]
    [Trait("cp", "CP-UI-G1")]
    public async Task 確認キャンセルは無変更_確認再リンクは提示N件をそのまま一括適用()
    {
        var (cancelVm, cancelWindows, cancelFolder) = await BuildVmAsync(confirm: false);
        var pendingId = (await _db.Images.GetPendingByFolderAsync(
                cancelFolder.Id, TestContext.Current.CancellationToken))
            .Single(image => image.FileName == "z-auto.jpg").Id;
        await cancelVm.AutoAdjudicateCommand.ExecuteAsync(null);
        Assert.Equal(cancelVm.HighConfidenceCount, cancelWindows.Items.Count);
        Assert.Equal("original.jpg へ再リンク", Assert.Single(cancelWindows.Items).SecondaryText);
        Assert.Equal("この 1 件をまとめて再リンクします", cancelWindows.Lead);
        Assert.Contains("元の画像に付け替え、タグと ID を保持", cancelWindows.SupportingMessage);
        Assert.Contains("見つからない状態（リンク切れ）も解消", cancelWindows.SupportingMessage);
        Assert.Equal("再リンク", cancelWindows.ConfirmLabel);
        Assert.Equal(3, (await _db.Images.GetPendingByFolderAsync(
            cancelFolder.Id, TestContext.Current.CancellationToken)).Count);

        cancelWindows.ConfirmResult = true;
        var expected = cancelVm.HighConfidenceCount;
        await cancelVm.AutoAdjudicateCommand.ExecuteAsync(null);

        Assert.Equal(expected, cancelWindows.Items.Count);          // PD-6 N = callout N
        Assert.Equal(0, cancelVm.HighConfidenceCount);              // 適用 N も同じ
        Assert.False(cancelVm.HasHighConfidence);
        Assert.Equal(2, cancelVm.Items.Count);
        Assert.True(cancelVm.Adjudicated);
        Assert.Equal(2, (await _db.Images.GetPendingByFolderAsync(
            cancelFolder.Id, TestContext.Current.CancellationToken)).Count);
        var relinked = Assert.IsType<ImageRecord>(await _db.Images.GetByIdAsync("candidate-1"));
        Assert.Equal("candidate-1", relinked.Id);
        Assert.Equal("z-auto.jpg", relinked.RelativePath);
        Assert.Equal(ImageStatus.Normal, relinked.Status);
        Assert.Single(await _db.Tags.GetImageTagsAsync("candidate-1"));
        Assert.Null(await _db.Images.GetByIdAsync(pendingId));
    }

    [Fact]
    [Trait("cp", "CP-UI-G1")]
    public async Task 曖昧候補は個別確認へ回しcallout確認適用Nから除外()
    {
        var folder = new SyncFolder { Id = "col-1", Name = "fixture", Path = @"C:\Photos" };
        Assert.True((await _db.Folders.AddAsync(folder)).IsSuccess);
        foreach (var id in new[] { "ambiguous-missing", "unique-missing" })
        {
            await _db.Images.AddAsync(new ImageRecord
            {
                Id = id,
                SyncFolderId = folder.Id,
                RelativePath = id + ".jpg",
                FileName = id + ".jpg",
                FileSize = 10,
                Hash = "same",
                Status = ImageStatus.Missing,
                CreatedDate = "2026-07-23T00:00:00.000Z",
                ModifiedDate = "2026-07-23T00:00:00.000Z",
            });
        }

        await AddPendingAsync(folder, "ambiguous-a.jpg", PendingOrigin.New, "ambiguous-missing");
        await AddPendingAsync(folder, "ambiguous-b.jpg", PendingOrigin.New, "ambiguous-missing");
        await AddPendingAsync(folder, "unique.jpg", PendingOrigin.New, "unique-missing");
        var windows = new RecordingWindows { ConfirmResult = true };
        var vm = new PendingReviewViewModel(
            new PendingReviewService(_db.Images), _db.Images, _db.Tags,
            TestLoc.Ja(), windows, folder);

        await vm.LoadAsync();

        Assert.Equal(1, vm.HighConfidenceCount);
        Assert.Equal("unique.jpg", Assert.Single(vm.HighConfidenceItems).FileName);
        Assert.Equal(2, vm.IndividualCount);
        Assert.All(vm.IndividualItems, item => Assert.StartsWith("ambiguous-", item.FileName));

        await vm.AutoAdjudicateCommand.ExecuteAsync(null);

        Assert.Single(windows.Items);
        Assert.Equal(2, vm.IndividualCount);
        Assert.Equal(ImageStatus.Pending, (await _db.Images.GetPendingByFolderAsync(
                folder.Id, TestContext.Current.CancellationToken))
            .Single(image => image.FileName == "ambiguous-a.jpg").Status);
        Assert.Equal(ImageStatus.Pending, (await _db.Images.GetPendingByFolderAsync(
                folder.Id, TestContext.Current.CancellationToken))
            .Single(image => image.FileName == "ambiguous-b.jpg").Status);
        Assert.Equal(ImageStatus.Normal, (await _db.Images.GetByIdAsync("unique-missing"))!.Status);
    }

    [Fact]
    [Trait("cp", "CP-UI-G1")]
    public async Task candidateがmissingでないかhash不一致ならfreshLoadでも個別確認へ除外()
    {
        var (vm, _, folder) = await BuildVmAsync(
            confirm: false, includeHighConfidence: true, includeIndividual: false);

        await _db.Images.UpdateStatusAsync("candidate-1", ImageStatus.Normal);
        await vm.LoadAsync();
        Assert.False(vm.HasHighConfidence);
        Assert.Single(vm.IndividualItems);

        await _db.Images.UpdateStatusAsync("candidate-1", ImageStatus.Missing);
        var pending = (await _db.Images.GetPendingByFolderAsync(
            folder.Id, TestContext.Current.CancellationToken)).Single();
        await _db.Images.UpdateFileMetaAsync(
            pending.Id, "changed-after-candidate", pending.FileSize, pending.ModifiedDate);
        await vm.LoadAsync();
        Assert.False(vm.HasHighConfidence);
        Assert.Single(vm.IndividualItems);
    }

    [Theory]
    [InlineData("accept")]
    [InlineData("delete")]
    [InlineData("treat-as-new")]
    [Trait("cp", "CP-UI-G1")]
    public async Task 曖昧2件の片方を個別裁定すると残る一意組をその場で自動対象へ再分類(
        string action)
    {
        var folder = new SyncFolder { Id = "col-1", Name = "fixture", Path = @"C:\Photos" };
        Assert.True((await _db.Folders.AddAsync(folder)).IsSuccess);
        await _db.Images.AddAsync(new ImageRecord
        {
            Id = "shared-missing",
            SyncFolderId = folder.Id,
            RelativePath = "shared-missing.jpg",
            FileName = "shared-missing.jpg",
            FileSize = 10,
            Hash = "same",
            Status = ImageStatus.Missing,
            CreatedDate = "2026-07-23T00:00:00.000Z",
            ModifiedDate = "2026-07-23T00:00:00.000Z",
        });
        await AddPendingAsync(folder, "ambiguous-a.jpg", PendingOrigin.New, "shared-missing");
        await AddPendingAsync(folder, "ambiguous-b.jpg", PendingOrigin.New, "shared-missing");
        var vm = new PendingReviewViewModel(
            new PendingReviewService(_db.Images), _db.Images, _db.Tags,
            TestLoc.Ja(), new RecordingWindows(), folder);
        await vm.LoadAsync();
        Assert.Equal(2, vm.IndividualCount);
        vm.Selected = vm.IndividualItems[0];

        switch (action)
        {
            case "accept":
                await vm.AcceptCommand.ExecuteAsync(null);
                break;
            case "delete":
                await vm.DeleteCommand.ExecuteAsync(null);
                break;
            case "treat-as-new":
                await vm.TreatAsNewCommand.ExecuteAsync(null);
                break;
            default:
                throw new InvalidOperationException(action);
        }

        Assert.Equal(1, vm.HighConfidenceCount);
        Assert.Empty(vm.IndividualItems);
        Assert.True(Assert.Single(vm.Items).IsHighConfidence);
        Assert.True(vm.HasHighConfidence);
    }

    [Fact]
    [Trait("cp", "CP-UI-G1")]
    public async Task 対象0件ではcalloutも対象グループも非表示()
    {
        var (vm, windows, _) = await BuildVmAsync(confirm: false, includeHighConfidence: false);

        Assert.False(vm.HasHighConfidence);
        Assert.Equal(0, vm.HighConfidenceCount);
        Assert.Empty(vm.HighConfidenceItems);
        Assert.Equal(2, vm.IndividualCount);
        await vm.AutoAdjudicateCommand.ExecuteAsync(null);
        Assert.Equal(0, windows.ConfirmCalls);
    }

    [Fact]
    [Trait("cp", "CP-UI-G1")]
    public void 自動裁定のja_en文言は一括再リンクと元IDタグ保持とリンク切れ解消を示す()
    {
        var args = new Dictionary<string, string> { ["count"] = "8", ["name"] = "original.jpg" };
        var ja = TestLoc.Ja();
        Assert.Equal("自動裁定（8 件を再リンク）", ja.T("pending.autoButton", args));
        Assert.Equal("この 8 件をまとめて再リンクします", ja.T("pending.autoConfirmLead", args));
        Assert.Equal("original.jpg へ再リンク", ja.T("pending.autoMatch", args));
        Assert.Equal("再リンク", ja.T("pending.autoConfirmAction"));
        Assert.Contains("元の画像に付け替え、タグと ID を保持", ja.T("pending.autoConfirmSupport", args));
        Assert.Contains("見つからない状態（リンク切れ）も解消", ja.T("pending.autoConfirmSupport", args));

        var en = TestLoc.En();
        Assert.Equal("Auto-adjudicate (relink 8)", en.T("pending.autoButton", args));
        Assert.Equal("Relink these 8 images together", en.T("pending.autoConfirmLead", args));
        Assert.Equal("Relink to original.jpg", en.T("pending.autoMatch", args));
        Assert.Equal("Relink", en.T("pending.autoConfirmAction"));
        Assert.Contains("repointed to the original images", en.T("pending.autoConfirmSupport", args));
        Assert.Contains("resolves the missing state (broken link)", en.T("pending.autoConfirmSupport", args));
    }

    [Fact]
    [Trait("cp", "CP-UI-G1")]
    public async Task 高信頼だけの一覧を一括再リンクするとPD4空状態へ遷移()
    {
        var (vm, _, folder) = await BuildVmAsync(
            confirm: true, includeHighConfidence: true, includeIndividual: false);

        await vm.AutoAdjudicateCommand.ExecuteAsync(null);

        Assert.True(vm.IsEmpty);
        Assert.Null(vm.Selected);
        Assert.Empty(vm.Items);
        Assert.Empty(await _db.Images.GetPendingByFolderAsync(
            folder.Id, TestContext.Current.CancellationToken));
    }

    private sealed class RecordingWindows : IWindowService
    {
        public bool ConfirmResult { get; set; }
        public int ConfirmCalls { get; private set; }
        public IReadOnlyList<ConfirmationListItem> Items { get; private set; } = [];
        public string Lead { get; private set; } = string.Empty;
        public string SupportingMessage { get; private set; } = string.Empty;
        public string ConfirmLabel { get; private set; } = string.Empty;

        public Task<bool> ConfirmListAsync(
            string title,
            string lead,
            string supportingMessage,
            string confirmLabel,
            IReadOnlyList<ConfirmationListItem> items,
            string? cancelLabel = null)
        {
            ConfirmCalls++;
            Items = items;
            Lead = lead;
            SupportingMessage = supportingMessage;
            ConfirmLabel = confirmLabel;
            return Task.FromResult(ConfirmResult);
        }

        public Task<bool> ConfirmAsync(string title, string message, string confirmLabel,
            bool destructive = false, string? cancelLabel = null) => Task.FromResult(ConfirmResult);

        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
        public Task ShowFolderManagementAsync() => Task.CompletedTask;
        public Task ShowSettingsAsync() => Task.CompletedTask;
        public Task ShowSnapshotsAsync() => Task.CompletedTask;
        public Task<bool> ShowTagEditorAsync(Tag? existing) => Task.FromResult(false);
        public Task<bool> ShowViewEditDialogAsync(View? existing) => Task.FromResult(false);
        public Task<IReadOnlyList<string>?> ShowNumericValueDialogAsync(
            Tag tag, NumericTagSettings? settings, int imageCount)
            => Task.FromResult<IReadOnlyList<string>?>(null);
        public Task<NodeConditionResult?> ShowNodeConditionDialogAsync(
            Tag tag, HierarchyConditionType? conditionType, string? conditionValueJson)
            => Task.FromResult<NodeConditionResult?>(null);
        public Task ShowRelinkAsync(string folderId) => Task.CompletedTask;
        public void ShowViewer(IReadOnlyList<ImageEntry> ordered, int startIndex)
        {
        }
    }
}
