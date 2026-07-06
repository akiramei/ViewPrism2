using ViewPrism2.App.Services;
using ViewPrism2.App.ViewModels;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using ViewPrism2.Core.Services.Repair;
using ViewPrism2.Core.Services.Similarity;
using ViewPrism2.Infrastructure.Imaging;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// ECO-050(REQ-064/065): 類似しきい値の既定値=仕様値 70 の pin。
/// 潜伏様式「既定値がどの検査面にも載っていない」を直接塞ぐ(整理トレイ=80/作業タブ=90 で潜伏した実績)。
/// 既定値の検査のみのため InitializeAsync は不要(構築直後の値=ユーザーが最初に見る値)。
/// </summary>
[Trait("cp", "CP-SIM-017")]
public sealed class CpUi050ThresholdDefaultTests : IDisposable
{
    private readonly TempDb _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public void 画像タブ整理トレイの既定しきい値は仕様値70()
    {
        var vm = new ImageTabViewModel(
            _db.Folders, _db.Images, _db.Tags, new ImageSorter(),
            new ViewService(_db.Views, _db.Clock), new NodeGraphBuilder(),
            new PathConditionConverter(), new ConditionEvaluator(),
            new SimilaritySearchService(_db.Folders, _db.Images, _db.Features, _db.Similarities, new FakePHashImageReader(), _db.Clock),
            new MergeService(_db.Images, _db.Tags, _db.Merges),
            new TrashService(_db.Images, _db.Folders, new FilePresenceProbe()),
            new StubWindowService(), new AppSettings(), new WorkspaceService(_db.Workspaces, _db.Clock), TestLoc.Empty());

        Assert.Equal(70, vm.SimilarThreshold); // REQ-064/065: 既定 70(範囲 50〜100)

        // クランプ 50〜100(REQ-064/065 — ECO-051: 撤去した旧モーダル VM 検査からの移行)
        vm.SimilarThreshold = 10;
        Assert.Equal(50, vm.SimilarThreshold);
        vm.SimilarThreshold = 999;
        Assert.Equal(100, vm.SimilarThreshold);
        vm.SimilarThreshold = 80;
        Assert.Equal(80, vm.SimilarThreshold);
    }

    [Fact]
    public void 作業タブの既定しきい値は仕様値70()
    {
        var vm = new WorkTabViewModel(
            new WorkspaceService(_db.Workspaces, _db.Clock), _db.Folders, _db.Tags,
            new SimilaritySearchService(_db.Folders, _db.Images, _db.Features, _db.Similarities, new FakePHashImageReader(), _db.Clock),
            new MergeService(_db.Images, _db.Tags, _db.Merges),
            new TrashService(_db.Images, _db.Folders, new FilePresenceProbe()),
            new StubWindowService(), new ImageSorter(), new AppSettings());

        Assert.Equal(70, vm.SimilarThreshold); // REQ-064/065: 既定 70(整理トレイと同値=転写ドリフト防止)

        // クランプ 50〜100(REQ-064/065 — ECO-051: 撤去した旧モーダル VM 検査からの移行)
        vm.SimilarThreshold = 10;
        Assert.Equal(50, vm.SimilarThreshold);
        vm.SimilarThreshold = 999;
        Assert.Equal(100, vm.SimilarThreshold);
    }

    private sealed class StubWindowService : IWindowService
    {
        public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(true);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
        public Task ShowFolderManagementAsync() => Task.CompletedTask;
        public Task ShowSettingsAsync() => Task.CompletedTask;
        public Task<bool> ShowTagEditorAsync(Tag? existing) => Task.FromResult(false);
        public Task<bool> ShowViewEditDialogAsync(View? existing) => Task.FromResult(false);
        public Task<IReadOnlyList<string>?> ShowNumericValueDialogAsync(Tag tag, NumericTagSettings? settings, int selectionCount)
            => Task.FromResult<IReadOnlyList<string>?>(null);
        public Task<NodeConditionResult?> ShowNodeConditionDialogAsync(Tag tag, HierarchyConditionType? currentType, string? currentValueJson)
            => Task.FromResult<NodeConditionResult?>(null);
        public Task ShowRelinkAsync(string folderId) => Task.CompletedTask;
        public void ShowViewer(IReadOnlyList<ImageEntry> ordered, int startIndex) { }
        public Task ShowSimilarSearchAsync(ImageEntry baseImage, IReadOnlyList<ImageEntry> collectionEntries) => Task.CompletedTask;
        public Task<bool> ShowMergeAsync(ImageEntry target, IReadOnlyList<ImageEntry> sources) => Task.FromResult(false);
        public Task ShowTrashAsync(string collectionId) => Task.CompletedTask;
    }
}
