using CommunityToolkit.Mvvm.Input;
using ViewPrism2.App.Services;
using ViewPrism2.App.ViewModels;
using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using ViewPrism2.Infrastructure.Imaging;
using ViewPrism2.Infrastructure.Scanning;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// CP-UI-G1(unit 部分・v1.3): コレクション=選択スコープ(REQ-053、ECO-002 CR-2/5/8)。
/// 一覧・「全画像」入口・NodeGraph 評価の母集合が選択中コレクションの normal 画像に限られること、
/// 未選択時の空状態、画像数表示、settings への永続化・復元(CR-5/6)を実 DB+実 VM で検査する。
/// </summary>
[Trait("cp", "CP-UI-G1")]
public sealed class CpUiG1CollectionScopeTests : IDisposable
{
    private readonly TempDb _db = new();
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "ViewPrism2.Tests", Guid.NewGuid().ToString("D"));

    public void Dispose()
    {
        _db.Dispose();
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // 一時ディレクトリの後始末失敗はテスト結果に影響させない
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class StubWindowService : IWindowService
    {
        public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(true);

        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);

        public Task ShowFolderManagementAsync() => Task.CompletedTask;

        public Task ShowSettingsAsync() => Task.CompletedTask;

        public Task<bool> ShowTagEditorAsync(Tag? existing) => Task.FromResult(false);

        public Task<bool> ShowViewEditDialogAsync(View? existing) => Task.FromResult(false);

        public Task<IReadOnlyList<string>?> ShowNumericValueDialogAsync(
            Tag tag, NumericTagSettings? settings, int selectionCount)
            => Task.FromResult<IReadOnlyList<string>?>(null);

        public Task<NodeConditionResult?> ShowNodeConditionDialogAsync(
            Tag tag, HierarchyConditionType? currentType, string? currentValueJson)
            => Task.FromResult<NodeConditionResult?>(null);

        public Task ShowRelinkAsync(string folderId) => Task.CompletedTask;

        public void ShowViewer(IReadOnlyList<ImageEntry> ordered, int startIndex)
        {
        }

        public Task ShowSimilarSearchAsync(ImageEntry baseImage, IReadOnlyList<ImageEntry> collectionEntries)
            => Task.CompletedTask;

        public Task<bool> ShowMergeAsync(ImageEntry target, IReadOnlyList<ImageEntry> sources)
            => Task.FromResult(false);

        public Task ShowTrashAsync(string collectionId) => Task.CompletedTask;
    }

    private MainWindowViewModel NewShell(AppSettings settings)
    {
        var localization = new LocalizationService(
            new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
            {
                ["ja"] = new Dictionary<string, string>
                {
                    ["view.allImages"] = "全画像",
                    ["collection.sidebar.imageCount"] = "{count}枚",
                },
            });
        var windows = new StubWindowService();
        var scan = new ScanService(_db.Folders, _db.Images, _db.Clock);
        var viewService = new ViewService(_db.Views, _db.Clock);
        var tagService = new TagService(_db.Tags);
        return new MainWindowViewModel(
            _db.Folders, _db.Images, _db.Tags, viewService,
            new NodeGraphBuilder(), new PathConditionConverter(), new ConditionEvaluator(),
            new ImageSorter(), new ThumbnailService(Path.Combine(_root, "thumbs")),
            localization, settings, windows,
            new FolderManagementViewModel(_db.Folders, scan, localization, windows),
            new TagsTabViewModel(viewService, tagService, _db.Tags, localization, windows),
            new TaggingPanelViewModel(tagService, _db.Tags, localization, windows));
    }

    private async Task<(SyncFolder A, SyncFolder B)> SeedTwoCollectionsAsync()
    {
        var a = new SyncFolder { Id = IdGenerator.NewId(), Name = "A", Path = Path.Combine(_root, "A") };
        var b = new SyncFolder { Id = IdGenerator.NewId(), Name = "B", Path = Path.Combine(_root, "B") };
        Assert.True((await _db.Folders.AddAsync(a)).IsSuccess);
        Assert.True((await _db.Folders.AddAsync(b)).IsSuccess);

        await AddImageAsync(a.Id, "a1.jpg", ImageStatus.Normal);
        await AddImageAsync(a.Id, "a2.jpg", ImageStatus.Normal);
        await AddImageAsync(a.Id, "a3.jpg", ImageStatus.Missing); // normal 以外は母集合外(INV-010)
        await AddImageAsync(b.Id, "b1.jpg", ImageStatus.Normal);
        return (a, b);
    }

    private async Task<ImageRecord> AddImageAsync(string folderId, string name, ImageStatus status)
    {
        var record = new ImageRecord
        {
            Id = IdGenerator.NewId(),
            SyncFolderId = folderId,
            RelativePath = name,
            FileName = name,
            FileSize = 10,
            Hash = new string('0', 64),
            Status = status,
            CreatedDate = "2026-06-11T00:00:00.000Z",
            ModifiedDate = "2026-06-11T00:00:00.000Z",
        };
        await _db.Images.AddAsync(record);
        return record;
    }

    private static FolderRowViewModel RowOf(MainWindowViewModel vm, SyncFolder folder)
        => vm.FolderPane.Folders.Single(r => r.Folder.Id == folder.Id);

    [Fact]
    public async Task 未選択時は母集合が空で選択を促す空状態になる()
    {
        await SeedTwoCollectionsAsync();
        var vm = NewShell(new AppSettings());

        await vm.InitializeAsync();

        Assert.False(vm.IsCollectionSelected);
        Assert.True(vm.ShowCollectionPrompt);
        Assert.False(vm.ShowGridPane);
        Assert.False(vm.ShowListPane);
        Assert.False(vm.ShowEmptyMessage); // 「画像がありません」ではなく選択プロンプトを出す
        Assert.True(vm.Browser.IsEmpty);   // 横断表示は行わない(REQ-053)
    }

    [Fact]
    public async Task コレクション選択で当該コレクションのnormal画像のみが母集合になる()
    {
        var (a, b) = await SeedTwoCollectionsAsync();
        var vm = NewShell(new AppSettings());
        await vm.InitializeAsync();

        await ((IAsyncRelayCommand)vm.SelectCollectionCommand).ExecuteAsync(RowOf(vm, a));

        Assert.True(vm.IsCollectionSelected);
        Assert.True(vm.ShowGridPane);
        Assert.False(vm.ShowCollectionPrompt);
        Assert.Equal(["a1.jpg", "a2.jpg"], vm.Browser.SortedItems.Select(i => i.FileName)); // missing は出ない
        Assert.True(RowOf(vm, a).IsSelected);
        Assert.False(RowOf(vm, b).IsSelected);

        // 切替で母集合が切り替わる(横断しない)
        await ((IAsyncRelayCommand)vm.SelectCollectionCommand).ExecuteAsync(RowOf(vm, b));
        Assert.Equal(["b1.jpg"], vm.Browser.SortedItems.Select(i => i.FileName));
        Assert.False(RowOf(vm, a).IsSelected);
        Assert.True(RowOf(vm, b).IsSelected);
    }

    [Fact]
    public async Task コレクション項目に画像数が表示される()
    {
        var (a, b) = await SeedTwoCollectionsAsync();
        var vm = NewShell(new AppSettings());
        await vm.InitializeAsync();
        await ((IAsyncRelayCommand)vm.SelectCollectionCommand).ExecuteAsync(RowOf(vm, a));

        Assert.Equal(2, RowOf(vm, a).ImageCount); // normal のみ計上
        Assert.Equal(1, RowOf(vm, b).ImageCount);
        Assert.Equal("2枚", RowOf(vm, a).ImageCountText);
    }

    [Fact]
    public async Task NodeGraph評価の母集合も選択中コレクションに限られる()
    {
        var (a, b) = await SeedTwoCollectionsAsync();

        // タグ値は B の画像にのみ付与する
        var tagService = new TagService(_db.Tags);
        var tag = await tagService.CreateAsync("色", TagType.Textual);
        Assert.True(tag.IsSuccess);
        var bImage = (await _db.Images.GetAllNormalAsync()).Single(r => r.SyncFolderId == b.Id);
        Assert.True((await tagService.TagImageAsync(bImage.Id, tag.Value!.Id, "赤")).IsSuccess);

        var viewService = new ViewService(_db.Views, _db.Clock);
        var view = await viewService.CreateAsync("V");
        Assert.True(view.IsSuccess);
        Assert.True((await viewService.AddNodeAsync(view.Value!.Id, tag.Value.Id, null, 0)).IsSuccess);

        var vm = NewShell(new AppSettings());
        await vm.InitializeAsync();

        // A 選択中: 値「赤」は A の normal 画像に存在しない → 値ノードなし(タグ名ノードのみ)
        await ((IAsyncRelayCommand)vm.SelectCollectionCommand).ExecuteAsync(RowOf(vm, a));
        var viewItem = vm.Recents.First(i => i.View?.Id == view.Value.Id);
        await ((IAsyncRelayCommand)vm.SelectViewListItemCommand).ExecuteAsync(viewItem);
        Assert.Single(vm.TreeRoots);
        Assert.Equal("色", vm.TreeRoots[0].Children.Single().DisplayName);

        // B 選択中: distinct 値 1 件 → 一体型「色: 赤」(REQ-035)
        await ((IAsyncRelayCommand)vm.SelectCollectionCommand).ExecuteAsync(RowOf(vm, b));
        Assert.Equal("色: 赤", vm.TreeRoots[0].Children.Single().DisplayName);
    }

    [Fact]
    public async Task 選択コレクションと表示モードはsettingsへ書き戻され復元される()
    {
        var (a, _) = await SeedTwoCollectionsAsync();
        var settings = new AppSettings();
        var vm = NewShell(settings);
        await vm.InitializeAsync();
        await ((IAsyncRelayCommand)vm.SelectCollectionCommand).ExecuteAsync(RowOf(vm, a));
        vm.Browser.IsListMode = true;

        vm.CaptureSettings();
        Assert.Equal(a.Id, settings.LastCollectionId); // CR-5
        Assert.Equal("list", settings.DisplayMode);    // CR-6

        // 「再起動」: 同じ settings で新しいシェルを作ると選択コレクションと表示モードが復元される
        var restored = NewShell(settings);
        await restored.InitializeAsync();
        Assert.Equal(a.Id, restored.SelectedCollectionId);
        Assert.True(restored.Browser.IsListMode);
        Assert.True(restored.ShowListPane);
        Assert.Equal(["a1.jpg", "a2.jpg"], restored.Browser.SortedItems.Select(i => i.FileName));
    }

    [Fact]
    public async Task 保存済みコレクションが消えていれば未選択へフォールバックする()
    {
        await SeedTwoCollectionsAsync();
        var settings = new AppSettings { LastCollectionId = "missing-folder-id" };
        var vm = NewShell(settings);

        await vm.InitializeAsync();

        Assert.False(vm.IsCollectionSelected);
        Assert.True(vm.ShowCollectionPrompt);
        Assert.Null(settings.LastCollectionId); // 無効 id は設定からも除去される
    }
}
