using ViewPrism2.App.Services;
using ViewPrism2.App.ViewModels;
using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Repositories;
using ViewPrism2.Core.Services;
using ViewPrism2.Core.Services.Repair;
using ViewPrism2.Core.Services.Similarity;
using ViewPrism2.Infrastructure.Imaging;
using ViewPrism2.Infrastructure.Scanning;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>ECO-060 / REQ-086: fully-hashed batchの段階公開・完了時sort・類似検索gate。</summary>
[Trait("cp", "CP-UI-G1")]
[Trait("cp", "CP-UI-G9")]
public sealed class CpUiEco060ProgressiveScanTests : IDisposable
{
    private readonly TempDb _db = new();
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"vp2-eco060-{Guid.NewGuid():N}");

    public CpUiEco060ProgressiveScanTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        _db.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task firstBatchを先行表示しscan中sortを保留して完了時に適用する()
    {
        const int fileCount = 513;
        for (var i = 0; i < fileCount; i++)
        {
            await File.WriteAllTextAsync(
                Path.Combine(_root, $"image-{i:D3}.jpg"),
                $"content-{i}",
                TestContext.Current.CancellationToken);
        }

        var folder = new SyncFolder { Id = IdGenerator.NewId(), Name = "C", Path = _root };
        Assert.True((await _db.Folders.AddAsync(folder)).IsSuccess);

        using var secondBatchEntered = new ManualResetEventSlim();
        using var releaseSecondBatch = new ManualResetEventSlim();
        var pausingImages = new PausingImageRepository(_db.Images, secondBatchEntered, releaseSecondBatch);
        var scan = new ScanService(_db.Folders, pausingImages, _db.Clock);
        var coordinator = new ScanCoordinator(scan);
        var vm = NewImageTab(pausingImages, coordinator);
        await vm.InitializeAsync(folder.Id);

        var scanTask = coordinator.ScanAsync(folder.Id, null, TestContext.Current.CancellationToken);
        Assert.True(
            secondBatchEntered.Wait(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken),
            "第2batchまで到達しませんでした。");
        await WaitUntilAsync(() => vm.Items.Count == 512 && vm.IsSelectedCollectionScanning);

        var orderBeforeSort = FileNames(vm);
        vm.SelectColumnSortCommand.Execute("name");
        vm.SelectColumnSortCommand.Execute("name"); // 最新sort条件=名前降順
        Assert.Equal(orderBeforeSort, FileNames(vm)); // scan中は再配列しない

        vm.ToggleOrganizeCommand.Execute(null);
        var target = vm.Items.First(x => !x.IsFolder);
        vm.HandleItemClick(target, ctrl: false, shift: false);
        Assert.True(vm.SimilarSearchBlocked);
        Assert.False(vm.CanRunSearch);
        await vm.RunSearchCommand.ExecuteAsync(null);
        Assert.Equal("スキャン完了後に利用できます", vm.ScanNotice);

        releaseSecondBatch.Set();
        var result = await scanTask;
        Assert.True(result.IsSuccess, result.Message);
        await WaitUntilAsync(() => vm.Items.Count == fileCount && !vm.IsSelectedCollectionScanning);

        var completed = FileNames(vm);
        Assert.Equal(completed.OrderByDescending(x => x, StringComparer.OrdinalIgnoreCase), completed);
        Assert.False(vm.SimilarSearchBlocked);
    }

    [Fact]
    public async Task スキャン中に段階公開された画像も選択操作のentry解決が通る()
    {
        // ECO-113 R8 所見1: スキャン append(OnScanUpdated)が _entryById を維持しないと、
        // 段階公開直後の画像は「クリック・選択可能なのに entry 解決(場所を開く/選択パネル/
        // 整理トレイ)から無言で脱落」する。是正前=不合格(RevealedPaths が空)。
        const int fileCount = 513; // batch 512 + 1(第2batchで一時停止させ scan 中状態を固定)
        for (var i = 0; i < fileCount; i++)
        {
            await File.WriteAllTextAsync(
                Path.Combine(_root, $"image-{i:D3}.jpg"),
                $"content-{i}",
                TestContext.Current.CancellationToken);
        }

        var folder = new SyncFolder { Id = IdGenerator.NewId(), Name = "C", Path = _root };
        Assert.True((await _db.Folders.AddAsync(folder)).IsSuccess);

        using var secondBatchEntered = new ManualResetEventSlim();
        using var releaseSecondBatch = new ManualResetEventSlim();
        var pausingImages = new PausingImageRepository(_db.Images, secondBatchEntered, releaseSecondBatch);
        var scan = new ScanService(_db.Folders, pausingImages, _db.Clock);
        var coordinator = new ScanCoordinator(scan);
        var fileOps = new FakeFileOps();
        var vm = new ImageTabViewModel(
            _db.Folders, pausingImages, _db.Tags, new ImageSorter(),
            new ViewService(_db.Views, _db.Clock), new NodeGraphBuilder(),
            new PathConditionConverter(), new ConditionEvaluator(),
            new SimilaritySearchService(_db.Folders, pausingImages, _db.Features, _db.Similarities, new FakePHashImageReader(), _db.Clock),
            new MergeService(pausingImages, _db.Tags, _db.Merges),
            new TrashService(pausingImages, _db.Folders, new FilePresenceProbe()),
            new StubWindowService(), new AppSettings(), new WorkspaceService(_db.Workspaces, _db.Clock), TestLoc.Ja(),
            coordinator, fileOps);
        await vm.InitializeAsync(folder.Id);

        var scanTask = coordinator.ScanAsync(folder.Id, null, TestContext.Current.CancellationToken);
        Assert.True(
            secondBatchEntered.Wait(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken),
            "第2batchまで到達しませんでした。");
        await WaitUntilAsync(() => vm.Items.Count == 512 && vm.IsSelectedCollectionScanning);

        vm.EnterFileOpsCommand.Execute(null);
        var target = vm.Items.First(x => !x.IsFolder);
        vm.HandleItemClick(target, ctrl: false, shift: false);
        Assert.True(vm.ShowOpenLocation);

        vm.OpenFileLocationCommand.Execute(null);
        Assert.Single(fileOps.RevealedPaths); // 是正前: EntryById が段階公開分を解決できず空=赤

        vm.ExitFileOpsCommand.Execute(null);
        releaseSecondBatch.Set();
        var result = await scanTask;
        Assert.True(result.IsSuccess, result.Message);
    }

    /// <summary>
    /// ECO-125(結合点棚卸し B-3): スキャン開始(Started)は母集合不変(画像はまだ 1 枚も
    /// 公開されていない)=表示中コレクションの Items を再構築しない。必要なのはコレクション行の
    /// スキャンバッジ+通知のみ(同メソッド内 BatchCommitted の部分更新様式が正解既在)。
    /// </summary>
    [Fact]
    public async Task 別コレクションのスキャン開始は表示中Itemsを再構築しない()
    {
        // 表示中= folder1(DB シードのみ・スキャン非対象)
        var folder1 = new SyncFolder { Id = IdGenerator.NewId(), Name = "C1", Path = @"C:\col1" };
        Assert.True((await _db.Folders.AddAsync(folder1)).IsSuccess);
        foreach (var name in new[] { "a.jpg", "b.jpg" })
        {
            await _db.Images.AddAsync(new ImageRecord
            {
                Id = IdGenerator.NewId(),
                SyncFolderId = folder1.Id,
                RelativePath = name,
                FileName = name,
                FileSize = 10,
                Hash = new string('0', 64),
                Status = ImageStatus.Normal,
                CreatedDate = "2026-06-11T00:00:00.000Z",
                ModifiedDate = "2026-06-11T00:00:00.000Z",
            });
        }
        // スキャン対象= folder2(513 ファイル=第2バッチで一時停止し Started+第1バッチ後の状態を保持)
        const int fileCount = 513;
        for (var i = 0; i < fileCount; i++)
        {
            await File.WriteAllTextAsync(
                Path.Combine(_root, $"scan-{i:D3}.jpg"), $"content-{i}", TestContext.Current.CancellationToken);
        }
        var folder2 = new SyncFolder { Id = IdGenerator.NewId(), Name = "C2", Path = _root };
        Assert.True((await _db.Folders.AddAsync(folder2)).IsSuccess);

        using var secondBatchEntered = new ManualResetEventSlim();
        using var releaseSecondBatch = new ManualResetEventSlim();
        var pausingImages = new PausingImageRepository(_db.Images, secondBatchEntered, releaseSecondBatch);
        var scan = new ScanService(_db.Folders, pausingImages, _db.Clock);
        var coordinator = new ScanCoordinator(scan);
        var vm = NewImageTab(pausingImages, coordinator);
        await vm.InitializeAsync(folder1.Id);
        Assert.Equal(2, vm.Items.Count(x => !x.IsFolder));
        var before = vm.Items.First(x => !x.IsFolder && x.Name == "a.jpg");

        var scanTask = coordinator.ScanAsync(folder2.Id, null, TestContext.Current.CancellationToken);
        Assert.True(
            secondBatchEntered.Wait(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken),
            "第2batchまで到達しませんでした。");
        // Started の反映(folder2 行のスキャンバッジ)を待ってから同一性を検査する
        await WaitUntilAsync(() => vm.Collections.Any(c => c.Id == folder2.Id && c.IsScanning));

        Assert.Same(before, vm.Items.First(x => !x.IsFolder && x.Name == "a.jpg")); // 母集合不変=再構築しない
        Assert.Equal(2, vm.Items.Count(x => !x.IsFolder)); // 他コレクションの段階公開分は現ビューへ混入しない

        releaseSecondBatch.Set();
        var result = await scanTask;
        Assert.True(result.IsSuccess, result.Message);
    }

    private sealed class FakeFileOps : IFileOperationsService
    {
        public List<string> RevealedPaths { get; } = new();
        public Task CopyTextAsync(string text) => Task.CompletedTask;
        public void RevealInFileManager(string absolutePath) => RevealedPaths.Add(absolutePath);
    }

    private ImageTabViewModel NewImageTab(IImageRepository images, ScanCoordinator coordinator)
        => new(
            _db.Folders, images, _db.Tags, new ImageSorter(),
            new ViewService(_db.Views, _db.Clock), new NodeGraphBuilder(),
            new PathConditionConverter(), new ConditionEvaluator(),
            new SimilaritySearchService(_db.Folders, images, _db.Features, _db.Similarities, new FakePHashImageReader(), _db.Clock),
            new MergeService(images, _db.Tags, _db.Merges),
            new TrashService(images, _db.Folders, new FilePresenceProbe()),
            new StubWindowService(), new AppSettings(), new WorkspaceService(_db.Workspaces, _db.Clock), TestLoc.Ja(), coordinator);

    private static string[] FileNames(ImageTabViewModel vm)
        => vm.Items.Where(x => !x.IsFolder).Select(x => x.Name).ToArray();

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timeout = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow >= timeout) throw new TimeoutException("UI状態の反映待ちがタイムアウトしました。");
            await Task.Delay(10);
        }
    }

    private sealed class PausingImageRepository : IImageRepository
    {
        private readonly IImageRepository _inner;
        private readonly ManualResetEventSlim _secondBatchEntered;
        private readonly ManualResetEventSlim _releaseSecondBatch;
        private int _batchCount;

        public PausingImageRepository(
            IImageRepository inner,
            ManualResetEventSlim secondBatchEntered,
            ManualResetEventSlim releaseSecondBatch)
        {
            _inner = inner;
            _secondBatchEntered = secondBatchEntered;
            _releaseSecondBatch = releaseSecondBatch;
        }

        public async Task ApplyScanBatchAsync(ScanMutationBatch batch)
        {
            if (Interlocked.Increment(ref _batchCount) == 2)
            {
                _secondBatchEntered.Set();
                if (!_releaseSecondBatch.Wait(TimeSpan.FromSeconds(15)))
                    throw new TimeoutException("第2batchの解放待ちがタイムアウトしました。");
            }
            await _inner.ApplyScanBatchAsync(batch);
        }

        public Task AddAsync(ImageRecord image) => _inner.AddAsync(image);
        public Task<ImageRecord?> GetByIdAsync(string id) => _inner.GetByIdAsync(id);
        public Task<IReadOnlyList<ImageRecord>> GetByFolderAsync(string syncFolderId) => _inner.GetByFolderAsync(syncFolderId);
        public Task<IReadOnlyList<ImageRecord>> GetAllNormalAsync() => _inner.GetAllNormalAsync();
        public Task<IReadOnlyDictionary<string, int>> GetNormalCountsByFolderAsync(CancellationToken ct = default) => _inner.GetNormalCountsByFolderAsync(ct);
        public Task<IReadOnlyList<ImageRecord>> GetNormalByFolderAsync(string syncFolderId, CancellationToken ct = default) => _inner.GetNormalByFolderAsync(syncFolderId, ct);
        public Task<IReadOnlyList<ImageRecord>> GetDeletedByFolderAsync(string syncFolderId, CancellationToken ct = default) => _inner.GetDeletedByFolderAsync(syncFolderId, ct);
        public Task<int> CountByFolderAndStatusAsync(string syncFolderId, ImageStatus status, CancellationToken ct = default) => _inner.CountByFolderAndStatusAsync(syncFolderId, status, ct);
        public Task UpdateFileMetaAsync(string id, string hash, long fileSize, string modifiedDate) => _inner.UpdateFileMetaAsync(id, hash, fileSize, modifiedDate);
        public Task UpdateStatusAsync(string id, ImageStatus status) => _inner.UpdateStatusAsync(id, status);
        public Task UpdateNotesAsync(string id, string? notes) => _inner.UpdateNotesAsync(id, notes);
        public Task DeleteAsync(string id) => _inner.DeleteAsync(id);
        public Task ApplyRelinkAsync(string missingImageId, string pendingImageId) => _inner.ApplyRelinkAsync(missingImageId, pendingImageId);
        public Task<IReadOnlyList<string>> GetDistinctNormalTagValuesAsync(string tagId) => _inner.GetDistinctNormalTagValuesAsync(tagId);
    }

    private sealed class StubWindowService : IWindowService
    {
        public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(true);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
        public Task ShowFolderManagementAsync() => Task.CompletedTask;
        public Task ShowSettingsAsync() => Task.CompletedTask;

        public Task ShowSnapshotsAsync() => Task.CompletedTask;

        public Task ShowCollectionExportAsync(string collectionId) => Task.CompletedTask;

        public Task ShowCollectionImportAsync(string collectionId) => Task.CompletedTask;
        public Task<bool> ShowTagEditorAsync(Tag? existing) => Task.FromResult(false);
        public Task<bool> ShowViewEditDialogAsync(View? existing) => Task.FromResult(false);
        public Task<IReadOnlyList<string>?> ShowNumericValueDialogAsync(Tag tag, NumericTagSettings? settings, int selectionCount) => Task.FromResult<IReadOnlyList<string>?>(null);
        public Task<NodeConditionResult?> ShowNodeConditionDialogAsync(Tag tag, HierarchyConditionType? currentType, string? currentValueJson) => Task.FromResult<NodeConditionResult?>(null);
        public Task ShowRelinkAsync(string folderId) => Task.CompletedTask;
        public void ShowViewer(IReadOnlyList<ImageEntry> ordered, int startIndex) { }
        public Task ShowSimilarSearchAsync(ImageEntry baseImage, IReadOnlyList<ImageEntry> collectionEntries) => Task.CompletedTask;
        public Task<bool> ShowMergeAsync(ImageEntry target, IReadOnlyList<ImageEntry> sources) => Task.FromResult(false);
        public Task ShowTrashAsync(string collectionId) => Task.CompletedTask;
    }
}
