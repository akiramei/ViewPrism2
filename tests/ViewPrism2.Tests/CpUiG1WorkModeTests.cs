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
/// CP-UI-G1(unit 部分・ECO-017): 画像タブ「作業」モード=3つ目の排他文脈モード(作業対象セットの蓄積)。
/// モック(ViewPrismUI:資料/画像タブ/ViewPrism2 画像タブ作業ボタン.html)の toggleWork/addToWork 挙動を回帰固定:
/// 作業=タグ編集/整理と排他・選択クリア / 作業中はグリッド選択可(inSelect・選択機構の再利用) /
/// 「追加」は選択を workTargets へ和集合追加し選択クリア(重複吸収・選択なしは無操作) /
/// ツールバーは排他隠し統一(作業中は他モード入口を隠す)。3裁定(2026-06-29 maintainer)に対応。
/// </summary>
[Trait("cp", "CP-UI-G1")]
public sealed class CpUiG1WorkModeTests : IDisposable
{
    private readonly TempDb _db = new();
    private SyncFolder _col = null!;

    public void Dispose() => _db.Dispose();

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

    private async Task<ImageTabViewModel> NewWithImagesAsync(params string[] insertionOrderNames)
    {
        _col = new SyncFolder { Id = IdGenerator.NewId(), Name = "C", Path = @"C:\col" };
        await _db.Folders.AddAsync(_col);
        foreach (var name in insertionOrderNames)
        {
            await _db.Images.AddAsync(new ImageRecord
            {
                Id = IdGenerator.NewId(),
                SyncFolderId = _col.Id,
                RelativePath = name,
                FileName = name,
                FileSize = 10,
                Hash = new string('0', 64),
                Status = ImageStatus.Normal,
                CreatedDate = "2026-06-11T00:00:00.000Z",
                ModifiedDate = "2026-06-11T00:00:00.000Z",
            });
        }
        var vm = new ImageTabViewModel(
            _db.Folders, _db.Images, _db.Tags, new ImageSorter(),
            new ViewService(_db.Views, _db.Clock), new NodeGraphBuilder(),
            new PathConditionConverter(), new ConditionEvaluator(),
            new SimilaritySearchService(_db.Folders, _db.Images, _db.Features, _db.Similarities, new FakePHashImageReader(), _db.Clock),
            new MergeService(_db.Images, _db.Tags, _db.Merges),
            new TrashService(_db.Images, _db.Folders, new FilePresenceProbe()),
            new StubWindowService(), new AppSettings(), new WorkspaceService(_db.Workspaces, _db.Clock), TestLoc.Empty());
        await vm.InitializeAsync(_col.Id);
        return vm;
    }

    private static ImageItemVM Item(ImageTabViewModel vm, string name)
        => vm.Items.Single(i => !i.IsFolder && i.Name == name);

    [Fact]
    public async Task 作業モードはタグ編集と整理を解除し選択をクリアする()
    {
        var vm = await NewWithImagesAsync("a.jpg", "b.jpg");
        vm.ToggleEditCommand.Execute(null);
        vm.HandleItemClick(Item(vm, "a.jpg"), ctrl: false, shift: false); // 編集で選択
        Assert.True(vm.HasSelection);

        vm.ToggleWorkCommand.Execute(null);

        Assert.True(vm.WorkMode);
        Assert.False(vm.EditMode);          // 編集解除(排他)
        Assert.False(vm.OrganizeMode);
        Assert.False(vm.HasSelection);      // 選択クリア
        Assert.Equal("作業を終了", vm.WorkButtonLabel);
    }

    [Fact]
    public async Task 作業モードのクリックはグリッド選択を有効化する()
    {
        var vm = await NewWithImagesAsync("a.jpg", "b.jpg");
        // 閲覧(モードなし)はシングルクリック無操作
        vm.HandleItemClick(Item(vm, "a.jpg"), ctrl: false, shift: false);
        Assert.False(Item(vm, "a.jpg").IsSelected);

        vm.ToggleWorkCommand.Execute(null);
        Assert.True(Item(vm, "a.jpg").Selectable); // inSelect=作業

        vm.HandleItemClick(Item(vm, "a.jpg"), ctrl: false, shift: false);
        Assert.True(Item(vm, "a.jpg").IsSelected);
        Assert.True(vm.HasWorkSelection);
        Assert.True(vm.CanAddToWork);
        Assert.Equal(1, vm.WorkSelCount);
    }

    [Fact]
    public async Task 追加は選択を作業対象へ和集合追加し選択をクリアする()
    {
        var vm = await NewWithImagesAsync("a.jpg", "b.jpg", "c.jpg");
        vm.ToggleWorkCommand.Execute(null);

        vm.HandleItemClick(Item(vm, "a.jpg"), ctrl: false, shift: false);
        vm.HandleItemClick(Item(vm, "b.jpg"), ctrl: true, shift: false);
        vm.AddToWorkCommand.Execute(null);

        Assert.False(vm.HasWorkSelection);          // 追加後に選択クリア
        Assert.True(vm.HasWorkTargets);
        Assert.Equal("作業対象 2 枚", vm.WorkTargetLabel);

        // 同じ画像を再追加しても重複しない(Set 意味論)
        vm.HandleItemClick(Item(vm, "a.jpg"), ctrl: false, shift: false);
        vm.HandleItemClick(Item(vm, "c.jpg"), ctrl: true, shift: false);
        vm.AddToWorkCommand.Execute(null);
        Assert.Equal("作業対象 3 枚", vm.WorkTargetLabel); // a 重複は吸収 → b,a,c=3
    }

    [Fact]
    public async Task 選択がなければ追加は無効で無操作()
    {
        var vm = await NewWithImagesAsync("a.jpg");
        vm.ToggleWorkCommand.Execute(null);

        Assert.False(vm.CanAddToWork);
        Assert.False(vm.HasWorkSelection);

        vm.AddToWorkCommand.Execute(null); // no-op
        Assert.False(vm.HasWorkTargets);
        Assert.Equal("作業対象 0 枚", vm.WorkTargetLabel);
    }

    [Fact]
    public async Task 作業対象チップは作業モード中のみ表示される()
    {
        var vm = await NewWithImagesAsync("a.jpg");
        vm.ToggleWorkCommand.Execute(null);
        vm.HandleItemClick(Item(vm, "a.jpg"), ctrl: false, shift: false);
        vm.AddToWorkCommand.Execute(null);
        Assert.True(vm.HasWorkTargets);

        vm.ToggleWorkCommand.Execute(null); // 作業終了
        Assert.False(vm.WorkMode);
        Assert.False(vm.HasWorkTargets);    // チップは作業モード中のみ(蓄積は保持)

        vm.ToggleWorkCommand.Execute(null); // 再開でチップ復活(蓄積は保持されている)
        Assert.True(vm.HasWorkTargets);
        Assert.Equal("作業対象 1 枚", vm.WorkTargetLabel);
    }

    [Fact]
    public async Task ツールバーは作業モード中に他モード入口とその他を隠す()
    {
        var vm = await NewWithImagesAsync("a.jpg");

        // browse: 3モード入口とも表示・⋯ 表示
        Assert.True(vm.ShowEditEntry);
        Assert.True(vm.ShowOrganizeEntry);
        Assert.True(vm.ShowWorkEntry);
        Assert.False(vm.InAnyMode);

        vm.ToggleWorkCommand.Execute(null);

        Assert.True(vm.InAnyMode);          // ⋯ は !InAnyMode で隠れる
        Assert.False(vm.ShowEditEntry);     // タグ編集入口は隠れる
        Assert.False(vm.ShowOrganizeEntry); // 整理入口は隠れる
        Assert.True(vm.ShowWorkEntry);      // 作業自身は「作業を終了」として残る
    }
}
