using CommunityToolkit.Mvvm.Input;
using ViewPrism2.App.Services;
using ViewPrism2.App.ViewModels;
using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using ViewPrism2.Core.Services.Repair;
using ViewPrism2.Core.Services.Similarity;
using ViewPrism2.Infrastructure.Imaging;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// ECO-140/CP-INTEGRITY-036: ⋯ メニューの裁定入口を「要確認の画像」1 本へ統合し、
/// 選択 collection ID で開くことを固定する。
/// </summary>
[Trait("cp", "CP-UI-G1")]
public sealed class CpUiG1MaintenanceMenuTests : IDisposable
{
    private readonly TempDb _db = new();
    public void Dispose() => _db.Dispose();

    private sealed class CapturingWindowService : IWindowService
    {
        public List<string> TrashCalls { get; } = new();
        public List<string> IntegrityReviewCalls { get; } = new();
        public Task<bool> ConfirmAsync(string title, string message, string confirmLabel, bool destructive = false, string? cancelLabel = null) => Task.FromResult(true);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
        public Task ShowFolderManagementAsync() => Task.CompletedTask;
        public Task ShowSettingsAsync() => Task.CompletedTask;

        public Task ShowSnapshotsAsync() => Task.CompletedTask;

        public Task ShowCollectionExportAsync(string collectionId) => Task.CompletedTask;

        public Task ShowCollectionImportAsync(string collectionId) => Task.CompletedTask;
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
        public Task ShowTrashAsync(string collectionId) { TrashCalls.Add(collectionId); return Task.CompletedTask; }
        public Task<bool> ShowIntegrityReviewAsync(string collectionId)
        {
            IntegrityReviewCalls.Add(collectionId);
            return Task.FromResult(false);
        }
    }

    private ImageTabViewModel NewVm(CapturingWindowService win)
        => new(
            _db.Folders, _db.Images, _db.Tags, new ImageSorter(),
            new ViewService(_db.Views, _db.Clock), new NodeGraphBuilder(),
            new PathConditionConverter(), new ConditionEvaluator(),
            new SimilaritySearchService(_db.Folders, _db.Images, _db.Features, _db.Similarities, new FakePHashImageReader(), _db.Clock),
            new MergeService(_db.Images, _db.Tags, _db.Merges),
            new TrashService(_db.Images, _db.Folders, new FilePresenceProbe()),
            win, new AppSettings(), new WorkspaceService(_db.Workspaces, _db.Clock), TestLoc.Ja());

    private async Task<SyncFolder> SeedCollectionAsync()
    {
        var col = new SyncFolder { Id = IdGenerator.NewId(), Name = "C", Path = @"C:\col" };
        await _db.Folders.AddAsync(col);
        await _db.Images.AddAsync(new ImageRecord
        {
            Id = IdGenerator.NewId(),
            SyncFolderId = col.Id,
            RelativePath = "a.jpg",
            FileName = "a.jpg",
            FileSize = 10,
            Hash = new string('0', 64),
            Status = ImageStatus.Normal,
            CreatedDate = "2026-06-11T00:00:00.000Z",
            ModifiedDate = "2026-06-11T00:00:00.000Z",
        });
        return col;
    }

    [Fact]
    public async Task コレクション選択時はゴミ箱をinPaneで開き統合裁定面を単一入口で開く()
    {
        var col = await SeedCollectionAsync();
        var win = new CapturingWindowService();
        var vm = NewVm(win);
        await vm.InitializeAsync(col.Id);

        Assert.True(vm.CanOpenMaintenance);
        await vm.OpenTrashCommand.ExecuteAsync(null);
        await vm.OpenIntegrityReviewCommand.ExecuteAsync(null);

        Assert.True(vm.TrashOpen);               // ゴミ箱=画像タブ内ポップアップ(ECO-019・モーダルは呼ばない)
        Assert.Empty(win.TrashCalls);            // 既存トラッシュモーダルは開かない
        Assert.Equal([col.Id], win.IntegrityReviewCalls);
        Assert.False(vm.MoreMenuOpen);           // 起動でメニューは閉じる
    }

    [Fact]
    public async Task コレクション未選択ではメンテナンスを開かない()
    {
        await SeedCollectionAsync();
        var win = new CapturingWindowService();
        var vm = NewVm(win);
        await vm.InitializeAsync(); // 未選択(先頭自動選択しない=REQ-053)

        Assert.False(vm.IsCollectionSelected);
        Assert.False(vm.CanOpenMaintenance);

        await vm.OpenTrashCommand.ExecuteAsync(null);
        await vm.OpenIntegrityReviewCommand.ExecuteAsync(null);

        Assert.False(vm.TrashOpen);     // ゴミ箱ポップアップも開かない
        Assert.Empty(win.TrashCalls);
        Assert.Empty(win.IntegrityReviewCalls);
    }

    [Fact]
    public async Task その他メニューは軸ソートメニューと排他に開閉する()
    {
        var col = await SeedCollectionAsync();
        var win = new CapturingWindowService();
        var vm = NewVm(win);
        await vm.InitializeAsync(col.Id);

        vm.ToggleMoreMenuCommand.Execute(null);
        Assert.True(vm.MoreMenuOpen);

        vm.ToggleAxisMenuCommand.Execute(null);  // 軸を開くと ⋯ は閉じる
        Assert.False(vm.MoreMenuOpen);
        Assert.True(vm.AxisMenuOpen);

        vm.ToggleMoreMenuCommand.Execute(null);  // ⋯ を開くと軸は閉じる
        Assert.True(vm.MoreMenuOpen);
        Assert.False(vm.AxisMenuOpen);

        vm.CloseMenusFromDismiss();
        Assert.False(vm.MoreMenuOpen);
    }
}
