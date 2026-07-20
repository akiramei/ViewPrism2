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
/// ECO-041: タグ追加の検索(mock 権威の意味論 — 画像タブ.dc.html L811/819-820/871):
/// タグ名部分一致(trim・大文字小文字無視)で種別グループ内を絞り込み・グループ構造維持・
/// 空になったグループはグループごと非表示・クリアで全復帰。画像タブ/作業タブ(β-2 再利用)の両 VM。
/// </summary>
[Trait("cp", "CP-TAGUI-013")]
public sealed class CpTagUi013AddSearchTests : IDisposable
{
    private readonly TempDb _db = new();

    public void Dispose() => _db.Dispose();

    private sealed class StubWindowService : IWindowService
    {
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
        public Task ShowTrashAsync(string collectionId) => Task.CompletedTask;
    }

    /// <summary>タグ 3 種: Alpha/beta(シンプル)+ Gamma(テキスト)— 部分一致・大小無視・空グループ検査用。</summary>
    private async Task SeedTagsAsync()
    {
        await _db.Tags.AddAsync(new Tag { Id = "t-alpha", Name = "Alpha", Type = TagType.Simple, Color = "#e5484d" });
        await _db.Tags.AddAsync(new Tag { Id = "t-beta", Name = "beta", Type = TagType.Simple, Color = "#8b5cf6" });
        await _db.Tags.AddAsync(new Tag { Id = "t-gamma", Name = "Gamma", Type = TagType.Textual, Color = "#2f6bed" });
    }

    private async Task<ImageTabViewModel> NewImageTabVmAsync()
    {
        var col = new SyncFolder { Id = IdGenerator.NewId(), Name = "C", Path = @"C:\col" };
        await _db.Folders.AddAsync(col);
        await _db.Images.AddAsync(new ImageRecord
        {
            Id = "img-a", SyncFolderId = col.Id, RelativePath = "a.jpg", FileName = "a.jpg",
            FileSize = 10, Hash = new string('0', 64), Status = ImageStatus.Normal,
            CreatedDate = "2026-06-11T00:00:00.000Z", ModifiedDate = "2026-06-11T00:00:00.000Z",
        });
        var vm = new ImageTabViewModel(
            _db.Folders, _db.Images, _db.Tags, new ImageSorter(),
            new ViewService(_db.Views, _db.Clock), new NodeGraphBuilder(),
            new PathConditionConverter(), new ConditionEvaluator(),
            new SimilaritySearchService(_db.Folders, _db.Images, _db.Features, _db.Similarities, new FakePHashImageReader(), _db.Clock),
            new MergeService(_db.Images, _db.Tags, _db.Merges),
            new TrashService(_db.Images, _db.Folders, new FilePresenceProbe()),
            new StubWindowService(), new AppSettings(), new WorkspaceService(_db.Workspaces, _db.Clock), TestLoc.Ja());
        await vm.InitializeAsync(col.Id);
        vm.ToggleEditCommand.Execute(null);
        vm.HandleItemClick(vm.Items.Single(i => !i.IsFolder), false, false);
        vm.TabAddCommand.Execute(null);
        Assert.True(vm.PanelActive && vm.OnAddTab);
        return vm;
    }

    private async Task<WorkTabViewModel> NewWorkTabVmAsync()
    {
        await _db.Folders.AddAsync(new SyncFolder { Id = "f1", Name = "F", Path = @"C:\f" });
        await _db.Images.AddAsync(new ImageRecord
        {
            Id = "img-a", SyncFolderId = "f1", RelativePath = "a.jpg", FileName = "a.jpg",
            FileSize = 10, Hash = new string('0', 64), Status = ImageStatus.Normal,
            CreatedDate = "2026-06-11T00:00:00.000Z", ModifiedDate = "2026-06-11T00:00:00.000Z",
        });
        var workspaces = new WorkspaceService(_db.Workspaces, _db.Clock);
        await workspaces.AddImagesToDefaultAsync(new[] { "img-a" });
        var vm = new WorkTabViewModel(
            workspaces, _db.Folders, _db.Tags,
            new SimilaritySearchService(_db.Folders, _db.Images, _db.Features, _db.Similarities, new FakePHashImageReader(), _db.Clock),
            new MergeService(_db.Images, _db.Tags, _db.Merges),
            new TrashService(_db.Images, _db.Folders, new FilePresenceProbe()),
            new StubWindowService(), new ImageSorter(), new AppSettings(),
            TestLoc.Ja());
        await vm.InitializeAsync();
        vm.ToggleEditCommand.Execute(null);
        vm.HandleItemClick(vm.Items.Single(i => i.Id == "img-a"), ctrl: false, shift: false);
        vm.TabAddCommand.Execute(null);
        Assert.True(vm.PanelActive && vm.OnAddTab);
        return vm;
    }

    private static string[] GroupLabels(System.Collections.ObjectModel.ObservableCollection<AddGroupVM> groups)
        => groups.Select(g => g.Label).ToArray();

    [Fact]
    public async Task 画像タブ_タグ追加検索は部分一致で絞り込み空グループは消えクリアで全復帰()
    {
        await SeedTagsAsync();
        var vm = await NewImageTabVmAsync();

        // 初期: 2 グループ(シンプル= Alpha,beta / テキスト= Gamma)
        Assert.Equal(new[] { "シンプル", "テキスト" }, GroupLabels(vm.AddGroups));
        Assert.Equal(2, vm.AddGroups.Single(g => g.Label == "シンプル").Tags.Count);

        // trim+大文字小文字無視の部分一致: " ALP " → Alpha のみ・テキストグループはグループごと消える
        vm.AddQuery = " ALP ";
        Assert.Equal(new[] { "シンプル" }, GroupLabels(vm.AddGroups));
        Assert.Equal(new[] { "Alpha" }, vm.AddGroups.Single().Tags.Select(t => t.Name).ToArray());

        // 全グループ不一致 → 空
        vm.AddQuery = "zzz";
        Assert.Empty(vm.AddGroups);

        // クリアで全復帰
        vm.AddQuery = "";
        Assert.Equal(new[] { "シンプル", "テキスト" }, GroupLabels(vm.AddGroups));
        Assert.Equal(2, vm.AddGroups.Single(g => g.Label == "シンプル").Tags.Count);
    }

    [Fact]
    public async Task 作業タブ_タグ追加検索は画像タブと同一意味論()
    {
        await SeedTagsAsync();
        var vm = await NewWorkTabVmAsync();

        Assert.Equal(new[] { "シンプル", "テキスト" }, GroupLabels(vm.AddGroups));

        vm.AddQuery = " gAmM ";
        Assert.Equal(new[] { "テキスト" }, GroupLabels(vm.AddGroups));
        Assert.Equal(new[] { "Gamma" }, vm.AddGroups.Single().Tags.Select(t => t.Name).ToArray());

        vm.AddQuery = "";
        Assert.Equal(new[] { "シンプル", "テキスト" }, GroupLabels(vm.AddGroups));
    }
}
