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
/// CP-UI-G1(unit 部分・ECO-018): 画像タブ「削除」モード=⋯メニュー「削除」で入る排他文脈モード。
/// モック(ViewPrismUI:資料/画像タブ/ViewPrism2 画像タブ削除ボタン.html)の enterDelete/deleteSelected 挙動を
/// 回帰固定: 削除=他モードと排他・選択クリア / 削除中はグリッド選択可(inSelect) /「ゴミ箱へ移動」は
/// 選択を normal→deleted のソフト削除(Core 経由)し normal 母集合から外す(REQ-053) /
/// ⋯「ゴミ箱」バッジ件数の反映 / ツールバー排他隠し統一。修復/ゴミ箱は既存モーダル(ECO-015)のまま。
/// </summary>
[Trait("cp", "CP-UI-G1")]
public sealed class CpUiG1DeleteModeTests : IDisposable
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

    private async Task<ImageTabViewModel> NewWithImagesAsync(params string[] names)
    {
        _col = new SyncFolder { Id = IdGenerator.NewId(), Name = "C", Path = @"C:\col" };
        await _db.Folders.AddAsync(_col);
        foreach (var name in names)
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
            new StubWindowService(), new AppSettings());
        await vm.InitializeAsync(_col.Id);
        return vm;
    }

    private static ImageItemVM Item(ImageTabViewModel vm, string name)
        => vm.Items.Single(i => !i.IsFolder && i.Name == name);

    [Fact]
    public async Task 削除メニューはタグ編集整理作業を解除し削除モードへ入る()
    {
        var vm = await NewWithImagesAsync("a.jpg", "b.jpg");
        vm.ToggleEditCommand.Execute(null);
        vm.HandleItemClick(Item(vm, "a.jpg"), ctrl: false, shift: false);
        Assert.True(vm.HasSelection);

        vm.EnterDeleteCommand.Execute(null);

        Assert.True(vm.DeleteMode);
        Assert.False(vm.EditMode);
        Assert.False(vm.WorkMode);
        Assert.False(vm.OrganizeMode);
        Assert.False(vm.HasSelection);   // 選択クリア
        Assert.False(vm.MoreMenuOpen);   // メニューを閉じる
    }

    [Fact]
    public async Task 削除モードのクリックはグリッド選択を有効化する()
    {
        var vm = await NewWithImagesAsync("a.jpg", "b.jpg");
        vm.EnterDeleteCommand.Execute(null);
        Assert.True(Item(vm, "a.jpg").Selectable); // inSelect=削除

        vm.HandleItemClick(Item(vm, "a.jpg"), ctrl: false, shift: false);
        Assert.True(Item(vm, "a.jpg").IsSelected);
        Assert.True(vm.HasDeleteSelection);
        Assert.True(vm.CanDeleteToTrash);
        Assert.Equal(1, vm.DeleteSelCount);
    }

    [Fact]
    public async Task ゴミ箱へ移動は選択をソフト削除しnormal母集合から外す()
    {
        var vm = await NewWithImagesAsync("a.jpg", "b.jpg", "c.jpg");
        vm.EnterDeleteCommand.Execute(null);
        vm.HandleItemClick(Item(vm, "a.jpg"), ctrl: false, shift: false);
        vm.HandleItemClick(Item(vm, "b.jpg"), ctrl: true, shift: false);

        await vm.DeleteToTrashCommand.ExecuteAsync(null);

        // normal 母集合(REQ-053)から消える: c のみ残る
        Assert.False(vm.HasDeleteSelection);
        Assert.Equal(["c.jpg"], vm.Items.Where(i => !i.IsFolder).Select(i => i.Name));
        // DB 上は deleted(物理非破壊・復元可)
        var all = await _db.Images.GetByFolderAsync(_col.Id);
        Assert.Equal(2, all.Count(r => r.Status == ImageStatus.Deleted));
        // ⋯「ゴミ箱」バッジ件数に反映
        Assert.True(vm.HasTrash);
        Assert.Equal(2, vm.TrashCount);
    }

    [Fact]
    public async Task 選択がなければゴミ箱へ移動は無効で無操作()
    {
        var vm = await NewWithImagesAsync("a.jpg");
        vm.EnterDeleteCommand.Execute(null);

        Assert.False(vm.CanDeleteToTrash);
        await vm.DeleteToTrashCommand.ExecuteAsync(null); // no-op

        var all = await _db.Images.GetByFolderAsync(_col.Id);
        Assert.Equal(0, all.Count(r => r.Status == ImageStatus.Deleted));
        Assert.False(vm.HasTrash);
    }

    [Fact]
    public async Task ツールバーは削除モード中に全モード入口とその他を隠す()
    {
        var vm = await NewWithImagesAsync("a.jpg");

        Assert.False(vm.InAnyMode); // browse

        vm.EnterDeleteCommand.Execute(null);

        Assert.True(vm.InAnyMode);          // ⋯ は !InAnyMode で隠れる
        Assert.False(vm.ShowEditEntry);
        Assert.False(vm.ShowOrganizeEntry);
        Assert.False(vm.ShowWorkEntry);

        vm.ExitDeleteCommand.Execute(null); // 終了で browse へ戻る
        Assert.False(vm.DeleteMode);
        Assert.True(vm.ShowEditEntry);
        Assert.True(vm.ShowWorkEntry);
    }
}
