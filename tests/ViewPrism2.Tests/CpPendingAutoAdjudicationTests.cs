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
/// pending 限定・全件成功または全件 rollback の原子バッチ受入を固定する。
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
    public async Task バッチ受入は対象だけnormal化しIDとタグを保持して非対象を変えない()
    {
        var folder = await AddFolderAsync();
        var candidate = await AddImageAsync(folder, "missing.jpg", ImageStatus.Missing);
        var first = await AddImageAsync(
            folder, "first.jpg", ImageStatus.Pending, PendingOrigin.New, candidate.Id);
        var second = await AddImageAsync(
            folder, "second.jpg", ImageStatus.Pending, PendingOrigin.New, candidate.Id);
        var excluded = await AddImageAsync(
            folder, "excluded.jpg", ImageStatus.Pending, PendingOrigin.Changed, candidate.Id);
        var tag = new Tag { Id = IdGenerator.NewId(), Name = "kept", Type = TagType.Simple };
        await _db.Tags.AddAsync(tag);
        await _db.Tags.UpsertImageTagAsync(new ImageTag { ImageId = first.Id, TagId = tag.Id });

        var review = new PendingReviewService(_db.Images);
        var result = await review.AcceptHighConfidenceAsync([first, excluded, second]);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(2, result.Value);
        foreach (var id in new[] { first.Id, second.Id })
        {
            var accepted = Assert.IsType<ImageRecord>(await _db.Images.GetByIdAsync(id));
            Assert.Equal(ImageStatus.Normal, accepted.Status);
            Assert.Null(accepted.CandidateLinkId);
            Assert.Null(accepted.PendingOrigin);
        }

        Assert.Single(await _db.Tags.GetImageTagsAsync(first.Id));
        var unchanged = Assert.IsType<ImageRecord>(await _db.Images.GetByIdAsync(excluded.Id));
        Assert.Equal(ImageStatus.Pending, unchanged.Status);
        Assert.Equal(PendingOrigin.Changed, unchanged.PendingOrigin);
        Assert.Equal(candidate.Id, unchanged.CandidateLinkId);
    }

    [Fact]
    [Trait("cp", "CP-SCAN-004")]
    public async Task バッチ中にpending限定を満たさない行があれば全件rollback()
    {
        var folder = await AddFolderAsync();
        var candidate = await AddImageAsync(folder, "missing.jpg", ImageStatus.Missing);
        var targets = Enumerable.Range(0, 501).Select(index => new ImageRecord
        {
            Id = IdGenerator.NewId(),
            SyncFolderId = folder.Id,
            RelativePath = $"batch-{index:000}.jpg",
            FileName = $"batch-{index:000}.jpg",
            FileSize = 10,
            Hash = new string('b', 64),
            Status = ImageStatus.Pending,
            PendingOrigin = PendingOrigin.New,
            CandidateLinkId = candidate.Id,
            CreatedDate = "2026-07-23T00:00:00.000Z",
            ModifiedDate = "2026-07-23T00:00:00.000Z",
        }).ToList();
        await _db.Images.ApplyScanBatchAsync(new ScanMutationBatch(targets, [], [], []));
        var first = targets[0];
        var stale = targets[^1]; // 500 件目 chunk 適用後の次 chunk で拒否させる
        await _db.Images.UpdateStatusAsync(stale.Id, ImageStatus.Normal);

        var review = new PendingReviewService(_db.Images);
        var result = await review.AcceptHighConfidenceAsync(targets);

        Assert.False(result.IsSuccess);
        Assert.Equal(ImageStatus.Pending, (await _db.Images.GetByIdAsync(first.Id))!.Status);
        Assert.Equal(candidate.Id, (await _db.Images.GetByIdAsync(first.Id))!.CandidateLinkId);
        Assert.Equal(ImageStatus.Pending, (await _db.Images.GetByIdAsync(targets[499].Id))!.Status);
        Assert.Equal(ImageStatus.Normal, (await _db.Images.GetByIdAsync(stale.Id))!.Status);
    }

    [Fact]
    [Trait("cp", "CP-SCAN-004")]
    public async Task 一括受入後も既存スキャン経路でpendingへ戻せる()
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

            var accepted = await new PendingReviewService(_db.Images).AcceptHighConfidenceAsync([moved]);
            Assert.True(accepted.IsSuccess, accepted.Message);
            Assert.Equal(ImageStatus.Normal, (await _db.Images.GetByIdAsync(moved.Id))!.Status);

            await File.WriteAllBytesAsync(
                movedPath, Encoding.UTF8.GetBytes("changed-content"), TestContext.Current.CancellationToken);
            Assert.True((await scan.ScanAsync(folder.Id, null, TestContext.Current.CancellationToken)).IsSuccess);
            var readjudicable = Assert.IsType<ImageRecord>(await _db.Images.GetByIdAsync(moved.Id));
            Assert.Equal(ImageStatus.Pending, readjudicable.Status);
            Assert.Equal(PendingOrigin.Changed, readjudicable.PendingOrigin);
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
            Hash = "hash-" + name,
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
    public async Task 確認キャンセルは無変更_確認受入は提示N件をそのまま一括適用()
    {
        var (cancelVm, cancelWindows, cancelFolder) = await BuildVmAsync(confirm: false);
        await cancelVm.AutoAdjudicateCommand.ExecuteAsync(null);
        Assert.Equal(cancelVm.HighConfidenceCount, cancelWindows.Items.Count);
        Assert.Contains("original.jpg と一致", Assert.Single(cancelWindows.Items).SecondaryText);
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
    public async Task 高信頼だけの一覧を一括受入するとPD4空状態へ遷移()
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
