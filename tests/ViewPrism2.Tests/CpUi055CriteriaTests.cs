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
/// ECO-055: 整理トレイの条件検索= CAD 意味論(マージ先との属性一致トグル 5 種)。
/// 裁定(maintainer 2026-07-06): ①一致の定義=ハッシュ完全一致/拡張子完全一致/名前=dest 本体の部分一致/
/// サイズ=バイト完全一致/更新日=同一日(UTC) ②自由入力 2 欄は撤去し全面置換 ③条件検索もマージ先必須。
/// </summary>
[Trait("cp", "CP-UI-G1")]
public sealed class CpUi055CriteriaTests : IDisposable
{
    private readonly TempDb _db = new();
    private SyncFolder _col = null!;
    private readonly Dictionary<string, string> _idByName = new(StringComparer.Ordinal);

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task 条件検索はマージ先が必須()
    {
        // ECO-055 プローブ(裁定③): CAD 意味論=条件検索は「マージ先と比べる」ためマージ先が前提。
        // 是正前は CanRunSearch = IsCriteriaMethod || dest で、マージ先なしでも実行可になっていた。
        var vm = await NewVmAsync(("a.jpg", "h1", 10, "2026-06-11T00:00:00.000Z"));
        vm.ToggleOrganizeCommand.Execute(null);
        vm.SetSearchMethodCommand.Execute("criteria");

        Assert.True(vm.IsCriteriaMethod);
        Assert.False(vm.CanRunSearch); // マージ先未設定 → 実行不可(裁定③)
    }

    [Fact]
    public void 写像の全数pin_5条件すべてがSearchCriteriaへ写像される()
    {
        // ECO-055/ECO-050 教訓: 「全数がどの検査面にもない」様式の封止 — 5 トグル全 ON で全フィールドが埋まる
        var dest = new ImageRecord
        {
            Id = "d", SyncFolderId = "f", RelativePath = "IMG_9.jpg", FileName = "IMG_9.jpg",
            FileSize = 12345, Hash = "HASH-D", Status = ImageStatus.Normal,
            CreatedDate = "2026-06-11T00:00:00.000Z", ModifiedDate = "2026-06-11T05:06:07.000Z",
        };

        var all = OrganizeCriteria.FromMergeTarget(dest, hash: true, ext: true, size: true, name: true, date: true);
        Assert.Equal("HASH-D", all.Hash);
        Assert.Equal(".jpg", all.Extension);
        Assert.Equal(12345, all.SizeMin);
        Assert.Equal(12345, all.SizeMax);
        Assert.Equal("IMG_9", all.NameContains);
        Assert.Equal("2026-06-11T00:00:00.000Z", all.MtimeFrom);
        Assert.Equal("2026-06-11T23:59:59.999Z", all.MtimeTo);

        var none = OrganizeCriteria.FromMergeTarget(dest, false, false, false, false, false);
        Assert.Null(none.Hash); Assert.Null(none.Extension); Assert.Null(none.SizeMin);
        Assert.Null(none.SizeMax); Assert.Null(none.NameContains); Assert.Null(none.MtimeFrom); Assert.Null(none.MtimeTo);
    }

    [Fact]
    public async Task 属性別トグルはマージ先と一致する候補だけを返す()
    {
        // dest= base.jpg(H1・100 バイト・06-11)。各候補は 1 属性だけ dest と一致するよう設計
        var vm = await NewVmAsync(
            ("base.jpg", "H1", 100, "2026-06-11T05:00:00.000Z"),
            ("samehash.png", "H1", 200, "2026-06-12T00:00:00.000Z"),
            ("samesize.jpg", "H2", 100, "2026-06-12T00:00:00.000Z"),
            ("sameday.jpg", "H3", 300, "2026-06-11T20:00:00.000Z"),
            ("base (1).jpg", "H4", 400, "2026-06-12T00:00:00.000Z"));
        vm.ToggleOrganizeCommand.Execute(null);
        vm.HandleItemClick(Item(vm, "base.jpg"), false, false); // マージ先
        vm.SetSearchMethodCommand.Execute("criteria");

        async Task<List<string>> RunWith(Action set, Action reset)
        {
            set();
            Assert.True(vm.CanRunSearch);
            await vm.RunSearchCommand.ExecuteAsync(null);
            reset();
            return vm.SearchResults.Select(r => r.Name).OrderBy(n => n).ToList();
        }

        Assert.Equal(["samehash.png"], await RunWith(() => vm.CondHash = true, () => vm.CondHash = false));
        Assert.Equal(["samesize.jpg"], await RunWith(() => vm.CondSize = true, () => vm.CondSize = false));
        Assert.Equal(["sameday.jpg"], await RunWith(() => vm.CondDate = true, () => vm.CondDate = false));
        Assert.Equal(["base (1).jpg"], await RunWith(() => vm.CondName = true, () => vm.CondName = false));
        // 拡張子= dest と同じ .jpg の候補全部(.png の samehash は出ない)
        Assert.Equal(new[] { "base (1).jpg", "samesize.jpg", "sameday.jpg" }.OrderBy(n => n).ToList(),
            await RunWith(() => vm.CondExt = true, () => vm.CondExt = false));
    }

    [Fact]
    public async Task 複数トグルはAND結合で絞り込む()
    {
        var vm = await NewVmAsync(
            ("base.jpg", "H1", 100, "2026-06-11T05:00:00.000Z"),
            ("samehash.png", "H1", 200, "2026-06-12T00:00:00.000Z"));
        vm.ToggleOrganizeCommand.Execute(null);
        vm.HandleItemClick(Item(vm, "base.jpg"), false, false);
        vm.SetSearchMethodCommand.Execute("criteria");

        vm.CondHash = true;
        vm.CondExt = true; // samehash は .png のため AND で脱落(REQ-068: 指定条件のみ AND)
        await vm.RunSearchCommand.ExecuteAsync(null);
        Assert.Empty(vm.SearchResults);
    }

    [Fact]
    public async Task 作業タブでも条件検索はマージ先が必須()
    {
        // 転写ドリフト封止(ECO-050 教訓): CanRunSearch の意味論は両タブで同一
        await _db.Folders.AddAsync(new SyncFolder { Id = "f-w", Name = "F", Path = @"C:w" });
        await _db.Images.AddAsync(new ImageRecord
        {
            Id = "w1", SyncFolderId = "f-w", RelativePath = "w1.jpg", FileName = "w1.jpg",
            FileSize = 1, Hash = "hw", Status = ImageStatus.Normal,
            CreatedDate = "2026-06-11T00:00:00.000Z", ModifiedDate = "2026-06-11T00:00:00.000Z",
        });
        var workspaces = new WorkspaceService(_db.Workspaces, _db.Clock);
        await workspaces.AddImagesToDefaultAsync(new[] { "w1" });
        var vm = new WorkTabViewModel(
            workspaces, _db.Folders, _db.Tags,
            new SimilaritySearchService(_db.Folders, _db.Images, _db.Features, _db.Similarities, new FakePHashImageReader(), _db.Clock),
            new MergeService(_db.Images, _db.Tags, _db.Merges),
            new TrashService(_db.Images, _db.Folders, new FilePresenceProbe()),
            new StubWindowService(), new ImageSorter(), new AppSettings(),
            TestLoc.Ja());
        await vm.InitializeAsync();

        vm.ToggleOrganizeCommand.Execute(null);
        vm.SetSearchMethodCommand.Execute("criteria");
        vm.CondHash = true;
        Assert.False(vm.CanRunSearch); // マージ先なし → 不可(裁定③)
    }

    [Fact]
    public async Task トグル変更はホストの通知を発火しCanRunSearchが即時反映される()
    {
        // GF-055-01(golden 所見 2026-07-06): マージ先+条件 ON でもボタンが押せず、タブ往復で直る。
        // 真因= ホスト転送セッターが子 VM へ値を渡すだけでホスト自身の PropertyChanged を発火しない
        // (ECO-038「転送殻の通知漏れ」の同型 — XAML はホストにバインドしている)。
        var vm = await NewVmAsync(("a.jpg", "h1", 10, "2026-06-11T00:00:00.000Z"));
        vm.ToggleOrganizeCommand.Execute(null);
        vm.HandleItemClick(Item(vm, "a.jpg"), false, false); // マージ先あり
        vm.SetSearchMethodCommand.Execute("criteria");
        Assert.False(vm.CanRunSearch); // トグル全 OFF

        var raised = false;
        vm.PropertyChanged += (_, _) => raised = true;
        vm.CondHash = true; // XAML と同じ「ホスト経由」で設定

        Assert.True(raised, "ホストの PropertyChanged が発火していない(GF-055-01)");
        Assert.True(vm.CanRunSearch);
    }

    // ---- ヘルパ ----

    private async Task<ImageTabViewModel> NewVmAsync(
        params (string Name, string Hash, long Size, string Modified)[] images)
    {
        _col = new SyncFolder { Id = IdGenerator.NewId(), Name = "C", Path = @"C:\col-055" };
        await _db.Folders.AddAsync(_col);
        foreach (var (name, hash, size, modified) in images)
        {
            var id = IdGenerator.NewId();
            _idByName[name] = id;
            await _db.Images.AddAsync(new ImageRecord
            {
                Id = id,
                SyncFolderId = _col.Id,
                RelativePath = name,
                FileName = name,
                FileSize = size,
                Hash = hash,
                Status = ImageStatus.Normal,
                CreatedDate = "2026-06-11T00:00:00.000Z",
                ModifiedDate = modified,
            });
        }

        var vm = new ImageTabViewModel(
            _db.Folders, _db.Images, _db.Tags, new ImageSorter(),
            new ViewService(_db.Views, _db.Clock), new NodeGraphBuilder(),
            new PathConditionConverter(), new ConditionEvaluator(),
            new SimilaritySearchService(_db.Folders, _db.Images, _db.Features, _db.Similarities, new FakePHashImageReader(), _db.Clock),
            new MergeService(_db.Images, _db.Tags, _db.Merges),
            new TrashService(_db.Images, _db.Folders, new FilePresenceProbe()),
            new StubWindowService(), new AppSettings(), new WorkspaceService(_db.Workspaces, _db.Clock), TestLoc.Ja());
        await vm.InitializeAsync(_col.Id);
        return vm;
    }

    private ImageItemVM Item(ImageTabViewModel vm, string name)
        => vm.Items.Single(i => !i.IsFolder && i.Name == name);

    private string Id(string name) => _idByName[name];

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
        public Task ShowTrashAsync(string collectionId) => Task.CompletedTask;
    }
}
