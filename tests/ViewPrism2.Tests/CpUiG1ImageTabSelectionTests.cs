using CommunityToolkit.Mvvm.Input;
using ViewPrism2.App.Services;
using ViewPrism2.App.ViewModels;
using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using ViewPrism2.Core.Services.Similarity;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// CP-UI-G1(unit 部分・ECO-013): 新 ImageTabViewModel のグリッド選択・閲覧操作(REQ-041)。
/// golden(2026-06-18)で判明した原典との挙動差/バグを回帰固定する:
/// 選択順バッジ(1 起点・連番付与の順序)/ SHIFT 範囲は表示順で連続(歯抜けしない)/
/// 閲覧モードはシングルクリック無操作・ダブルクリックでビューアー(表示順で起動)。
/// 旧 surface の同カバレッジ(CpUiG1SelectionTests=ImageBrowserViewModel)を新 VM へ引き継ぐ。
/// </summary>
[Trait("cp", "CP-UI-G1")]
public sealed class CpUiG1ImageTabSelectionTests : IDisposable
{
    private readonly TempDb _db = new();
    private SyncFolder _col = null!;

    public void Dispose() => _db.Dispose();

    private sealed class CapturingWindowService : IWindowService
    {
        public List<(IReadOnlyList<ImageEntry> Ordered, int Index)> ViewerCalls { get; } = new();
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
        public void ShowViewer(IReadOnlyList<ImageEntry> ordered, int startIndex) => ViewerCalls.Add((ordered, startIndex));
        public Task ShowSimilarSearchAsync(ImageEntry baseImage, IReadOnlyList<ImageEntry> collectionEntries) => Task.CompletedTask;
        public Task<bool> ShowMergeAsync(ImageEntry target, IReadOnlyList<ImageEntry> sources) => Task.FromResult(false);
        public Task ShowTrashAsync(string collectionId) => Task.CompletedTask;
    }

    /// <summary>挿入順(DB 順)= 引数順で画像を作る。表示順は名前ソートなので両者をズラして検査できる。</summary>
    private async Task<(ImageTabViewModel Vm, CapturingWindowService Win)> NewWithImagesAsync(params string[] insertionOrderNames)
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
        var win = new CapturingWindowService();
        var vm = new ImageTabViewModel(
            _db.Folders, _db.Images, _db.Tags, new ImageSorter(),
            new ViewService(_db.Views, _db.Clock), new NodeGraphBuilder(),
            new PathConditionConverter(), new ConditionEvaluator(),
            new SimilaritySearchService(_db.Folders, _db.Images, _db.Features, _db.Similarities, new FakePHashImageReader(), _db.Clock),
            new MergeService(_db.Images, _db.Tags, _db.Merges),
            win, new AppSettings());
        await vm.InitializeAsync(_col.Id);
        return (vm, win);
    }

    private static ImageItemVM Item(ImageTabViewModel vm, string name)
        => vm.Items.Single(i => !i.IsFolder && i.Name == name);

    [Fact]
    public async Task 編集モードのクリックは選択順番号を1起点で付与する()
    {
        var (vm, _) = await NewWithImagesAsync("c.jpg", "a.jpg", "b.jpg"); // 挿入順≠表示順
        vm.ToggleEditCommand.Execute(null);

        vm.HandleItemClick(Item(vm, "b.jpg"), ctrl: false, shift: false);
        vm.HandleItemClick(Item(vm, "a.jpg"), ctrl: true, shift: false); // Ctrl 追加

        Assert.Equal(1, Item(vm, "b.jpg").SelectionOrder);
        Assert.Equal("1", Item(vm, "b.jpg").SelectionOrderText);
        Assert.Equal(2, Item(vm, "a.jpg").SelectionOrder);
        Assert.Null(Item(vm, "c.jpg").SelectionOrder);
    }

    [Fact]
    public async Task SHIFT範囲選択は表示順で連続選択し歯抜けしない()
    {
        // 挿入順(DB 順)と表示順(名前ソート)を意図的にズラす。
        // 旧バグ: 範囲母集合が未ソートで、表示順の中間が選択されない歯抜けが起きた。
        var (vm, _) = await NewWithImagesAsync("e.jpg", "c.jpg", "a.jpg", "d.jpg", "b.jpg");
        vm.ToggleEditCommand.Execute(null);

        vm.HandleItemClick(Item(vm, "a.jpg"), ctrl: false, shift: false); // 表示順先頭
        vm.HandleItemClick(Item(vm, "c.jpg"), ctrl: false, shift: true);  // a..c を範囲選択

        Assert.True(Item(vm, "a.jpg").IsSelected);
        Assert.True(Item(vm, "b.jpg").IsSelected); // 表示順の中間が歯抜けしない
        Assert.True(Item(vm, "c.jpg").IsSelected);
        Assert.False(Item(vm, "d.jpg").IsSelected);
        Assert.False(Item(vm, "e.jpg").IsSelected);
        // 選択順は表示順(index 昇順)で 1..3
        Assert.Equal(1, Item(vm, "a.jpg").SelectionOrder);
        Assert.Equal(2, Item(vm, "b.jpg").SelectionOrder);
        Assert.Equal(3, Item(vm, "c.jpg").SelectionOrder);
    }

    [Fact]
    public async Task 閲覧モードはシングルクリックで選択しない()
    {
        var (vm, win) = await NewWithImagesAsync("a.jpg", "b.jpg");

        vm.HandleItemClick(Item(vm, "a.jpg"), ctrl: false, shift: false, isDoubleClick: false);

        Assert.Null(Item(vm, "a.jpg").SelectionOrder);
        Assert.False(Item(vm, "a.jpg").IsSelected);
        Assert.Empty(win.ViewerCalls);
    }

    [Fact]
    public async Task 閲覧モードのダブルクリックはビューアーを表示順で起動する()
    {
        var (vm, win) = await NewWithImagesAsync("c.jpg", "a.jpg", "b.jpg"); // 表示順 a,b,c

        vm.HandleItemClick(Item(vm, "b.jpg"), ctrl: false, shift: false, isDoubleClick: true);

        Assert.Single(win.ViewerCalls);
        var (ordered, idx) = win.ViewerCalls[0];
        Assert.Equal(["a.jpg", "b.jpg", "c.jpg"], ordered.Select(e => e.Record.FileName)); // 表示順
        Assert.Equal(1, idx); // b.jpg は表示順 index 1
    }
}
