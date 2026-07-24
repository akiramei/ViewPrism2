using ViewPrism2.App.Services;
using ViewPrism2.App.ViewModels;
using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Repositories;
using ViewPrism2.Core.Services;
using ViewPrism2.Core.Services.Repair;
using ViewPrism2.Core.Services.Similarity;
using ViewPrism2.Infrastructure.Imaging;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// CP-UI-G1(ECO-131・GF-128-01): クロスタブ状態鮮度。作業タブ等の外部経路で images.status が
/// 変わっても、画像タブの母集合(_allNormal/_allPending)は無効化されない先在欠陥(ECO-128 の
/// 復元→pending が顕在化)。RefreshContentAsync による母集合鮮度回復と、シェルのタブ切替配線
/// (作業タブ訪問→画像タブ復帰で回復)を固定する。
/// </summary>
[Trait("cp", "CP-UI-G1")]
public sealed class CpUiG1CrossTabRefreshTests : IDisposable
{
    private readonly TempDb _db = new();
    private SyncFolder _col = null!;

    public void Dispose() => _db.Dispose();

    private sealed class FakeProbe(bool exists) : IFilePresenceProbe
    {
        public bool Exists(string absoluteImagePath) => exists;
    }

    private async Task SeedAsync(params string[] normalNames)
    {
        _col = new SyncFolder { Id = IdGenerator.NewId(), Name = "C", Path = @"C:\col" };
        await _db.Folders.AddAsync(_col);
        foreach (var name in normalNames)
        {
            await _db.Images.AddAsync(new ImageRecord
            {
                Id = name, // 可読性: id=name
                SyncFolderId = _col.Id,
                RelativePath = name, FileName = name, FileSize = 10, Hash = new string('0', 64),
                Status = ImageStatus.Normal,
                CreatedDate = "2026-06-11T00:00:00.000Z", ModifiedDate = "2026-06-11T00:00:00.000Z",
            });
        }
    }

    private ImageTabViewModel NewImageTab() => new(
        _db.Folders, _db.Images, _db.Tags, new ImageSorter(),
        new ViewService(_db.Views, _db.Clock), new NodeGraphBuilder(),
        new PathConditionConverter(), new ConditionEvaluator(),
        new SimilaritySearchService(_db.Folders, _db.Images, _db.Features, _db.Similarities, new FakePHashImageReader(), _db.Clock),
        new MergeService(_db.Images, _db.Tags, _db.Merges),
        new TrashService(_db.Images, _db.Folders, new FilePresenceProbe()),
        new NullWindows(), new AppSettings(), new WorkspaceService(_db.Workspaces, _db.Clock), TestLoc.Ja());

    private static ImageItemVM Tile(ImageTabViewModel vm, string name)
        => vm.Items.Single(i => !i.IsFolder && i.Name == name);

    // ---- Probe A: 母集合鮮度回復の核(RefreshContentAsync) ----

    [Fact]
    public async Task 外部でのstatus変更後_RefreshContentで母集合鮮度が回復する()
    {
        await SeedAsync("a.jpg");
        var vm = NewImageTab();
        await vm.InitializeAsync(_col.Id);
        Assert.False(Tile(vm, "a.jpg").IsPending);            // 初期=normal(バッジなし)

        // 外部(作業タブ相当)で削除→復元 = a は DB で pending(origin=Restored・ECO-128 T6')
        var trash = new TrashService(_db.Images, _db.Folders, new FakeProbe(exists: true));
        Assert.True((await trash.DeleteToTrashAsync("a.jpg")).IsSuccess);
        Assert.Equal(ImageStatus.Pending, (await trash.RestoreAsync("a.jpg")).Value);

        // 母集合鮮度回復 → pending が反映(バッジ)。件数バッジも追随
        await vm.RefreshContentAsync();

        var tile = Tile(vm, "a.jpg");
        Assert.True(tile.IsPending);
        Assert.Equal(1, vm.IntegrityReviewCount);
        Assert.True(vm.HasIntegrityReview);
    }

    [Fact]
    public async Task 外部での削除後_RefreshContentでnormal母集合から外れる()
    {
        await SeedAsync("a.jpg", "b.jpg");
        var vm = NewImageTab();
        await vm.InitializeAsync(_col.Id);
        Assert.Equal(2, vm.Items.Count(i => !i.IsFolder));

        var trash = new TrashService(_db.Images, _db.Folders, new FakeProbe(exists: true));
        Assert.True((await trash.DeleteToTrashAsync("a.jpg")).IsSuccess); // normal→deleted

        await vm.RefreshContentAsync();

        // deleted は normal 一覧からも pending 並置からも外れる(ゴミ箱のみ)
        Assert.DoesNotContain(vm.Items, i => !i.IsFolder && i.Name == "a.jpg");
        Assert.Contains(vm.Items, i => !i.IsFolder && i.Name == "b.jpg");
    }

    // ---- Probe B: シェル配線(作業タブ訪問→画像タブ復帰で母集合鮮度回復) ----

    [Fact]
    public async Task シェル_作業タブ訪問後に画像タブへ戻ると母集合が鮮度回復する()
    {
        await SeedAsync("a.jpg");
        var localization = TestLoc.Ja();
        var viewService = new ViewService(_db.Views, _db.Clock);
        var tagService = new TagService(_db.Tags);
        var tagsTab = new TagsTabViewModel(viewService, tagService, _db.Tags, localization, new NullWindows());
        var shell = new MainWindowViewModel(
            _db.Folders, _db.Images, _db.Tags, viewService,
            new NodeGraphBuilder(), new PathConditionConverter(), new ConditionEvaluator(),
            new SimilaritySearchService(_db.Folders, _db.Images, _db.Features, _db.Similarities, new FakePHashImageReader(), _db.Clock),
            new MergeService(_db.Images, _db.Tags, _db.Merges),
            new TrashService(_db.Images, _db.Folders, new FilePresenceProbe()),
            new ImageSorter(), localization, new AppSettings(), new NullWindows(),
            tagsTab, new WorkspaceService(_db.Workspaces, _db.Clock));
        await shell.ImageTab.InitializeAsync(_col.Id);
        shell.ShowImagesTabCommand.Execute(null);
        Assert.Equal(1, shell.SelectedTabIndex);
        Assert.False(Tile(shell.ImageTab, "a.jpg").IsPending);

        // 作業タブへ(母集合 stale を立てる)→ 外部で削除→復元(pending)→ 画像タブへ戻る
        shell.ShowWorkTabCommand.Execute(null);
        Assert.Equal(2, shell.SelectedTabIndex);
        var trash = new TrashService(_db.Images, _db.Folders, new FakeProbe(exists: true));
        Assert.True((await trash.DeleteToTrashAsync("a.jpg")).IsSuccess);
        Assert.Equal(ImageStatus.Pending, (await trash.RestoreAsync("a.jpg")).Value);

        shell.ShowImagesTabCommand.Execute(null);
        Assert.Equal(1, shell.SelectedTabIndex);
        await shell.ImagesRefreshInFlight!;               // fire-and-forget の再読込を待つ

        var tile = Tile(shell.ImageTab, "a.jpg");
        Assert.True(tile.IsPending);                      // クロスタブでも pending が反映
        Assert.Equal(1, shell.ImageTab.IntegrityReviewCount);
    }

    private sealed class NullWindows : IWindowService
    {
        public Task<bool> ConfirmAsync(string title, string message, string confirmLabel,
            bool destructive = false, string? cancelLabel = null) => Task.FromResult(true);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
        public Task ShowFolderManagementAsync() => Task.CompletedTask;
        public Task ShowSettingsAsync() => Task.CompletedTask;
        public Task ShowSnapshotsAsync() => Task.CompletedTask;
        public Task<bool> ShowTagEditorAsync(Tag? existing) => Task.FromResult(false);
        public Task<bool> ShowViewEditDialogAsync(View? existing) => Task.FromResult(false);
        public Task<IReadOnlyList<string>?> ShowNumericValueDialogAsync(
            Tag tag, NumericTagSettings? settings, int imageCount)
            => Task.FromResult<IReadOnlyList<string>?>(null);
        public Task<NodeConditionResult?> ShowNodeConditionDialogAsync(
            Tag tag, HierarchyConditionType? conditionType, string? conditionValueJson)
            => Task.FromResult<NodeConditionResult?>(null);
        public Task ShowRelinkAsync(string folderId) => Task.CompletedTask;
        public void ShowViewer(IReadOnlyList<ImageEntry> ordered, int startIndex) { }
    }
}
