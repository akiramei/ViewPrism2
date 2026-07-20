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
            new TrashService(_db.Images, _db.Folders, new FilePresenceProbe()),
            win, new AppSettings(), new WorkspaceService(_db.Workspaces, _db.Clock), TestLoc.Ja());
        await vm.InitializeAsync(_col.Id);
        return (vm, win);
    }

    private static ImageItemVM Item(ImageTabViewModel vm, string name)
        => vm.Items.Single(i => !i.IsFolder && i.Name == name);

    private static string[] FileNames(ImageTabViewModel vm)
        => vm.Items.Where(i => !i.IsFolder).Select(i => i.Name).ToArray();

    private static string[] ItemOrder(ImageTabViewModel vm)
        => vm.Items.Select(i => $"{(i.IsFolder ? "F" : "I")}:{i.Name}").ToArray();

    /// <summary>ECO-097先行probe: FSタグfilter後の選択は可視画像との交差へ縮退し、解除で復活しない。</summary>
    [Theory]
    [InlineData("edit")]
    [InlineData("work")]
    [InlineData("delete")]
    public async Task FSタグ絞り込みは各選択モードの非表示選択を落とし解除しても復活させない(string mode)
    {
        var (vm, _) = await NewWithImagesAsync("a.jpg", "b.jpg");
        var visibleId = Item(vm, "a.jpg").Id;
        await _db.Tags.AddAsync(new Tag
        {
            Id = "t-visible",
            Name = "表示対象",
            Type = TagType.Simple,
            Color = "#2f6bed",
        });
        await _db.Tags.UpsertImageTagAsync(new ImageTag { ImageId = visibleId, TagId = "t-visible" });
        await vm.ReloadTagCatalogAsync();

        switch (mode)
        {
            case "edit":
                vm.ToggleEditCommand.Execute(null);
                break;
            case "work":
                vm.ToggleWorkCommand.Execute(null);
                break;
            case "delete":
                vm.EnterDeleteCommand.Execute(null);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode));
        }

        vm.HandleItemClick(Item(vm, "a.jpg"), ctrl: false, shift: false);
        vm.HandleItemClick(Item(vm, "b.jpg"), ctrl: true, shift: false);

        vm.ClickChip(vm.Chips.Single(c => c.Id == "t-visible"));
        Assert.Equal(["a.jpg"], FileNames(vm));
        Assert.True(Item(vm, "a.jpg").IsSelected);
        Assert.Equal(1, vm.WorkSelCount); // 3モードが共有して消費する _selected の公開件数

        vm.ClickChip(vm.Chips.Single(c => c.IsNeutral));
        Assert.True(Item(vm, "a.jpg").IsSelected);
        Assert.False(Item(vm, "b.jpg").IsSelected);
    }

    /// <summary>ECO-070先行probe: FS軸はfolder群→image群を保ち、両群へ現在方向を個別適用する。</summary>
    [Fact]
    public async Task FS表示はフォルダ先頭を保ちフォルダと画像を群別ソートする()
    {
        var (vm, _) = await NewWithImagesAsync("zeta/z.jpg", "alpha/a.jpg", "c.jpg", "a.jpg");

        Assert.Equal(["F:alpha", "F:zeta", "I:a.jpg", "I:c.jpg"], ItemOrder(vm));

        vm.SelectColumnSortCommand.Execute("name"); // 名前昇順を明示
        vm.SelectColumnSortCommand.Execute("name"); // 名前降順
        Assert.Equal(["F:zeta", "F:alpha", "I:c.jpg", "I:a.jpg"], ItemOrder(vm));

        vm.SelectColumnSortCommand.Execute("size"); // 別列=昇順
        vm.SetSortDescCommand.Execute(null);        // size降順でもfolderは名前降順
        Assert.Equal(["F:zeta", "F:alpha", "I:a.jpg", "I:c.jpg"], ItemOrder(vm));
    }

    /// <summary>ECO-025 β: リスト列ヘッダーソートの配線(SelectColumnSort→SortFiles→ViewColumnSorter)。</summary>
    [Fact]
    public async Task リスト列ヘッダーソートは列比較器で並べ替えトグルとクリアが効く()
    {
        var (vm, _) = await NewWithImagesAsync("c.jpg", "a.jpg", "b.jpg"); // 挿入順≠表示順
        vm.SetListCommand.Execute(null);

        // 既定は名前昇順(_sortField)
        Assert.Equal(["a.jpg", "b.jpg", "c.jpg"], FileNames(vm));

        // 名前列ヘッダー: 別列クリック=昇順開始(既定と同じ順)
        vm.SelectColumnSortCommand.Execute("name");
        Assert.True(vm.IsColumnSorted);
        Assert.Equal(["a.jpg", "b.jpg", "c.jpg"], FileNames(vm));

        // 同列再クリック=降順トグル(順が反転すれば列比較器が効いている)
        vm.SelectColumnSortCommand.Execute("name");
        Assert.Equal(["c.jpg", "b.jpg", "a.jpg"], FileNames(vm));

        // クリアで解除(元順=名前昇順)
        vm.ClearColumnSortCommand.Execute(null);
        Assert.False(vm.IsColumnSorted);
        Assert.Equal(["a.jpg", "b.jpg", "c.jpg"], FileNames(vm));
    }

    /// <summary>ECO-025 β/FL-003 v2: ソートは表示形式間で共有・アイコンの並び替え候補=表示列・未ソートは名前昇順の既定順。</summary>
    [Fact]
    public async Task アイコン表示でもソートは共有され並び替え候補は表示列になる()
    {
        var (vm, _) = await NewWithImagesAsync("c.jpg", "a.jpg", "b.jpg"); // 既定=アイコン(grid)

        // 未ソート: バッジ「なし」・既定順=名前昇順
        Assert.False(vm.IsColumnSorted);
        Assert.Equal("なし", vm.SortButtonBadge);
        Assert.Equal(["a.jpg", "b.jpg", "c.jpg"], FileNames(vm));

        // 並び替え候補=ビューの表示列(ビュー無し=既定3列 name/size/modified_date)
        Assert.Equal(["name", "size", "modified_date"], vm.SortColumns.Select(o => o.Key));

        // アイコン(grid)でも列ソートが効く(状態共有・旧 名前/更新日/サイズ固定メニューは廃止)
        vm.SelectColumnSortCommand.Execute("name");
        vm.SelectColumnSortCommand.Execute("name"); // 降順トグル
        Assert.Equal(["c.jpg", "b.jpg", "a.jpg"], FileNames(vm));
        Assert.Equal("名前", vm.SortButtonBadge);
        Assert.True(vm.SortColumns.Single(o => o.Key == "name").IsActive);

        // 昇順/降順セグメント
        vm.SetSortAscCommand.Execute(null);
        Assert.True(vm.SortAscActive);
        Assert.Equal(["a.jpg", "b.jpg", "c.jpg"], FileNames(vm));

        // リストへ切替えてもソート状態は保持(共有)
        vm.SetListCommand.Execute(null);
        Assert.True(vm.IsColumnSorted);
        Assert.Equal(["a.jpg", "b.jpg", "c.jpg"], FileNames(vm));
    }

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
    // ================================================================
    //  ECO-113: 選択トグル経路の母集合再評価(26万件で顕在化)の構造プローブ。
    //  計器= ImageTabViewModel.ContextEnumerationCount(母集合列挙=全件評価+ソートの累計回数)。
    //  ECO-058 方式=固定時間閾値を設けず「選択クリックが母集合列挙を呼ばない」構造で pin する。
    // ================================================================

    [Fact]
    public async Task 選択クリックは母集合列挙を走らせない()
    {
        var (vm, _) = await NewWithImagesAsync("a.jpg", "b.jpg", "c.jpg");
        vm.ToggleEditCommand.Execute(null);
        var baseline = vm.ContextEnumerationCount;

        vm.HandleItemClick(Item(vm, "a.jpg"), ctrl: false, shift: false); // plain
        vm.HandleItemClick(Item(vm, "b.jpg"), ctrl: true, shift: false);  // ctrl トグル
        vm.HandleItemClick(Item(vm, "b.jpg"), ctrl: true, shift: false);  // ctrl 解除

        Assert.True(vm.HasSelection);
        Assert.Equal(baseline, vm.ContextEnumerationCount); // 選択コストは母集合サイズ非依存(ECO-113)
    }

    [Fact]
    public async Task ファイル操作モードの選択クリックも母集合列挙を走らせない()
    {
        var (vm, _) = await NewWithImagesAsync("a.jpg", "b.jpg");
        vm.EnterFileOpsCommand.Execute(null);
        var baseline = vm.ContextEnumerationCount;

        vm.HandleItemClick(Item(vm, "a.jpg"), ctrl: false, shift: false);
        vm.HandleItemClick(Item(vm, "b.jpg"), ctrl: true, shift: false);

        Assert.Equal(2, vm.FileOpsSelCount);
        Assert.Equal(baseline, vm.ContextEnumerationCount); // 症状の観測面(ECO-112 のモード)でも非比例
    }

    [Fact]
    public async Task SHIFT範囲選択だけが母集合列挙を1回だけ使う()
    {
        var (vm, _) = await NewWithImagesAsync("c.jpg", "a.jpg", "b.jpg"); // 表示順 a,b,c
        vm.ToggleEditCommand.Execute(null);
        vm.HandleItemClick(Item(vm, "a.jpg"), ctrl: false, shift: false);
        var baseline = vm.ContextEnumerationCount;

        vm.HandleItemClick(Item(vm, "c.jpg"), ctrl: false, shift: true); // 範囲は表示順の列挙が必要=1回だけ許容

        Assert.Equal(3, vm.Items.Count(i => i.IsSelected)); // a,b,c の union(既存意味論の維持)
        Assert.Equal(baseline + 1, vm.ContextEnumerationCount);
    }
}
