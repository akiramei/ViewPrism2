using ViewPrism2.App.ViewModels;
using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services.Similarity;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// ECO-066 / IMG-020: 類似画像検索の停止・進捗・整理ライフサイクル。
/// R5先行プローブ: 旧検索の遅延完了がReset後へ結果を書き戻すこと、停止導線と段階進捗契約の不在を
/// 是正前に実測し、単一active session+generation/cancellationの製造後に緑へ転じさせる。
/// </summary>
[Trait("cp", "CP-SIMSESSION-029")]
public sealed class CpUi066SimilaritySessionTests : IDisposable
{
    private readonly TempDb _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task 整理終了後に旧検索が完了しても結果状態を復活させない()
    {
        var (vm, reader) = await NewOrganizeVmAsync();

        var running = vm.RunSearchAsync();
        await reader.FirstReadStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        vm.ResetState(); // 整理終了と同じリセット境界
        Assert.False(vm.HasSearched);

        reader.ReleaseFirstRead();
        await running.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.False(vm.HasSearched); // 是正前は旧Taskがtrueを書き戻して不合格
        Assert.Empty(vm.SearchResults);
    }

    [Fact]
    public void 画像タブと作業タブに検索停止コマンドがある()
    {
        Assert.NotNull(typeof(ImageTabViewModel).GetProperty("CancelSearchCommand"));
        Assert.NotNull(typeof(WorkTabViewModel).GetProperty("CancelSearchCommand"));
    }

    [Fact]
    public void 整理検索VMが段階件数進捗を公開する()
    {
        Assert.NotNull(typeof(ImageTabOrganizeViewModel).GetProperty("SearchProgressLabel"));
        Assert.NotNull(typeof(ImageTabOrganizeViewModel).GetProperty("SearchProgressValue"));
        Assert.NotNull(typeof(ImageTabOrganizeViewModel).GetProperty("SearchProgressIndeterminate"));
        Assert.NotNull(typeof(ImageTabOrganizeViewModel).GetProperty("SearchPreparing"));
        Assert.NotNull(typeof(ImageTabOrganizeViewModel).GetProperty("SearchComparing"));
        Assert.NotNull(typeof(ImageTabOrganizeViewModel).GetProperty("SearchCancelling"));
    }

    [Fact]
    public void 新しい検索は旧世代を無効化し停止状態を区別する()
    {
        var session = new SimilaritySearchSession();
        var oldRun = session.Start();
        var currentRun = session.Start();

        Assert.False(session.TryComplete(oldRun));
        Assert.True(session.TryComplete(currentRun));
        session.Finish(oldRun);
        session.Finish(currentRun);

        var cancellingRun = session.Start();
        session.Cancel();
        Assert.True(session.Cancelling);
        Assert.Equal("停止しています…", session.ProgressLabel);
        Assert.False(session.TryComplete(cancellingRun));
        session.Finish(cancellingRun);
        Assert.Equal(SimilaritySearchSessionState.Idle, session.State);
    }

    [Fact]
    public async Task 比較進捗は単調増加し総候補件数で完了する()
    {
        const string folderId = "eco066-progress";
        await _db.Folders.AddAsync(new SyncFolder { Id = folderId, Name = "progress", Path = @"C:\eco066" });
        var baseImage = await SeedAsync(folderId, "progress-base.jpg");
        var one = await SeedAsync(folderId, "progress-one.jpg");
        var two = await SeedAsync(folderId, "progress-two.jpg");
        var reports = new CaptureProgress();
        var service = new SimilaritySearchService(
            _db.Folders, _db.Images, _db.Features, _db.Similarities,
            new ImmediatePHashReader(), _db.Clock);

        await service.FindSimilarInScopeAsync(
            baseImage.Id, 70, new[] { baseImage, one, two },
            detailedProgress: reports,
            ct: TestContext.Current.CancellationToken);

        var comparing = reports.Values
            .Where(value => value.Phase == SimilaritySearchPhase.Comparing)
            .ToList();
        Assert.NotEmpty(comparing);
        Assert.All(comparing, value => Assert.InRange(value.Completed, 0, value.Total));
        Assert.Equal(comparing.OrderBy(value => value.Completed).Select(value => value.Completed),
            comparing.Select(value => value.Completed));
        Assert.Equal(2, comparing[^1].Total);
        Assert.Equal(comparing[^1].Total, comparing[^1].Completed);
    }

    [Fact]
    public async Task 停止前に正常生成した特徴量cacheを次回検索で再利用する()
    {
        const string folderId = "eco066-cache";
        await _db.Folders.AddAsync(new SyncFolder { Id = folderId, Name = "cache", Path = @"C:\eco066" });
        var baseImage = await SeedAsync(folderId, "cache-base.jpg");
        var one = await SeedAsync(folderId, "cache-one.jpg");
        var two = await SeedAsync(folderId, "cache-two.jpg");
        var scope = new[] { baseImage, one, two };
        var reader = new CountingPHashReader();
        var service = new SimilaritySearchService(
            _db.Folders, _db.Images, _db.Features, _db.Similarities, reader, _db.Clock);
        using var cancellation = new CancellationTokenSource();
        var progress = new CaptureProgress(value =>
        {
            if (value.Phase == SimilaritySearchPhase.Comparing && value.Completed == 1)
                cancellation.Cancel();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.FindSimilarInScopeAsync(
                baseImage.Id, 70, scope,
                ct: cancellation.Token,
                detailedProgress: progress));
        Assert.Equal(2, reader.ReadCount); // base + 最初の候補は正常完了済み

        await service.FindSimilarInScopeAsync(
            baseImage.Id, 70, scope,
            ct: TestContext.Current.CancellationToken);
        Assert.Equal(3, reader.ReadCount); // 次回は未処理の2候補目だけdecode
    }

    private async Task<(ImageTabOrganizeViewModel Vm, GatedPHashReader Reader)> NewOrganizeVmAsync()
    {
        const string folderId = "eco066-folder";
        await _db.Folders.AddAsync(new SyncFolder
        {
            Id = folderId,
            Name = "ECO-066",
            Path = @"C:\eco066",
        });

        var baseImage = await SeedAsync(folderId, "base.jpg");
        var candidate = await SeedAsync(folderId, "candidate.jpg");
        var scope = new[] { baseImage, candidate };
        var reader = new GatedPHashReader();
        var similar = new SimilaritySearchService(
            _db.Folders, _db.Images, _db.Features, _db.Similarities, reader, _db.Clock);
        var vm = new ImageTabOrganizeViewModel(
            _db.Images,
            similar,
            new MergeService(_db.Images, _db.Tags, _db.Merges),
            () => folderId,
            () => scope,
            () => { },
            () => { },
            () => Task.CompletedTask);
        vm.SetMergeTarget(baseImage.Id);
        return (vm, reader);
    }

    private async Task<ImageRecord> SeedAsync(string folderId, string name)
    {
        var record = new ImageRecord
        {
            Id = IdGenerator.NewId(),
            SyncFolderId = folderId,
            RelativePath = name,
            FileName = name,
            FileSize = 1,
            Hash = "hash-" + name,
            Status = ImageStatus.Normal,
            CreatedDate = "2026-07-11T00:00:00.000Z",
            ModifiedDate = "2026-07-11T00:00:00.000Z",
        };
        await _db.Images.AddAsync(record);
        return record;
    }

    private sealed class GatedPHashReader : IPHashImageReader
    {
        private readonly TaskCompletionSource _firstReadStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseFirstRead =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _reads;

        public string AdapterId => "eco066-gated-v1";
        public TaskCompletionSource FirstReadStarted => _firstReadStarted;

        public void ReleaseFirstRead() => _releaseFirstRead.TrySetResult();

        public async Task<string?> ComputePHashAsync(string absoluteImagePath)
        {
            if (Interlocked.Increment(ref _reads) == 1)
            {
                _firstReadStarted.TrySetResult();
                await _releaseFirstRead.Task;
            }

            return "0000000000000000";
        }
    }

    private sealed class ImmediatePHashReader : IPHashImageReader
    {
        public string AdapterId => "eco066-immediate-v1";
        public Task<string?> ComputePHashAsync(string absoluteImagePath)
            => Task.FromResult<string?>("0000000000000000");
    }

    private sealed class CountingPHashReader : IPHashImageReader
    {
        private int _reads;
        public string AdapterId => "eco066-counting-v1";
        public int ReadCount => Volatile.Read(ref _reads);
        public Task<string?> ComputePHashAsync(string absoluteImagePath)
        {
            Interlocked.Increment(ref _reads);
            return Task.FromResult<string?>("0000000000000000");
        }
    }

    private sealed class CaptureProgress : IProgress<SimilaritySearchProgress>
    {
        private readonly Action<SimilaritySearchProgress>? _onReport;
        public CaptureProgress(Action<SimilaritySearchProgress>? onReport = null) => _onReport = onReport;
        public List<SimilaritySearchProgress> Values { get; } = new();
        public void Report(SimilaritySearchProgress value)
        {
            Values.Add(value);
            _onReport?.Invoke(value);
        }
    }
}
