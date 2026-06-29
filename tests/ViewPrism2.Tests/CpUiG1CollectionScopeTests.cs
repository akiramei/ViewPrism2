using CommunityToolkit.Mvvm.Input;
using ViewPrism2.App.Services;
using ViewPrism2.App.ViewModels;
using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using ViewPrism2.Core.Services.Repair;
using ViewPrism2.Core.Services.Similarity;
using ViewPrism2.Infrastructure.Imaging;
using ViewPrism2.Infrastructure.Scanning;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// CP-UI-G1(unit 部分・v1.3 + ECO-013): コレクション=選択スコープ(REQ-053、ECO-002 CR-2/5/8)。
/// ECO-013 で画像タブ surface を新 <see cref="ImageTabViewModel"/> 一本へ統合したのに伴い、本カバレッジを
/// 原典 MainWindowViewModel 契約から新 VM 契約へ移行する(挙動は等価維持)。
/// 一覧・表示軸評価の母集合が選択中コレクションの normal 画像に限られること、未選択時の選択プロンプト、
/// 画像数表示(モック準拠=数値のみ)、settings への永続化・復元(CR-5/6)・消失フォールバックを実 DB+実 VM で検査する。
/// 描画(セル整列・省略表示)は golden(承認者 maintainer)。
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

    /// <summary>実 ImageTabViewModel(ECO-013 後の画像タブ surface の唯一の VM)を実 Core サービスで組む。</summary>
    private ImageTabViewModel NewImageTab(AppSettings settings)
    {
        var views = new ViewService(_db.Views, _db.Clock);
        return new ImageTabViewModel(
            _db.Folders, _db.Images, _db.Tags, new ImageSorter(), views,
            new NodeGraphBuilder(), new PathConditionConverter(), new ConditionEvaluator(),
            new SimilaritySearchService(_db.Folders, _db.Images, _db.Features, _db.Similarities, new FakePHashImageReader(), _db.Clock),
            new MergeService(_db.Images, _db.Tags, _db.Merges),
            new TrashService(_db.Images, _db.Folders, new FilePresenceProbe()),
            new StubWindowService(), settings, new WorkspaceService(_db.Workspaces, _db.Clock));
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

    private static CollectionRowVM RowOf(ImageTabViewModel vm, SyncFolder folder)
        => vm.Collections.Single(r => r.Id == folder.Id);

    /// <summary>FS 軸で現在表示中の画像(フォルダ以外)のファイル名列。</summary>
    private static IEnumerable<string> ImageNames(ImageTabViewModel vm)
        => vm.Items.Where(i => !i.IsFolder).Select(i => i.Name);

    [Fact]
    public async Task 未選択時は母集合が空で選択を促す空状態になる()
    {
        await SeedTwoCollectionsAsync();
        var vm = NewImageTab(new AppSettings()); // LastCollectionId 未設定

        await vm.InitializeAsync();

        Assert.False(vm.IsCollectionSelected);
        Assert.True(vm.ShowCollectionPrompt);
        Assert.False(vm.ShowGridPane);
        Assert.False(vm.ShowListPane);
        Assert.False(vm.ShowEmptyMessage); // 「画像がありません」ではなく選択プロンプトを出す
        Assert.Empty(vm.Items);            // 横断表示は行わない(REQ-053)
    }

    [Fact]
    public async Task コレクション選択で当該コレクションのnormal画像のみが母集合になる()
    {
        var (a, b) = await SeedTwoCollectionsAsync();
        var vm = NewImageTab(new AppSettings());
        await vm.InitializeAsync();

        await vm.SelectCollectionCommand.ExecuteAsync(a.Id);

        Assert.True(vm.IsCollectionSelected);
        Assert.True(vm.ShowGridPane);
        Assert.False(vm.ShowCollectionPrompt);
        Assert.Equal(["a1.jpg", "a2.jpg"], ImageNames(vm)); // missing は出ない
        Assert.True(RowOf(vm, a).IsSelected);
        Assert.False(RowOf(vm, b).IsSelected);

        // 切替で母集合が切り替わる(横断しない)
        await vm.SelectCollectionCommand.ExecuteAsync(b.Id);
        Assert.Equal(["b1.jpg"], ImageNames(vm));
        Assert.False(RowOf(vm, a).IsSelected);
        Assert.True(RowOf(vm, b).IsSelected);
    }

    [Fact]
    public async Task コレクション項目に画像数が表示される()
    {
        var (a, b) = await SeedTwoCollectionsAsync();
        var vm = NewImageTab(new AppSettings());
        await vm.InitializeAsync();
        await vm.SelectCollectionCommand.ExecuteAsync(a.Id);

        // normal のみ計上。モック準拠でバッジは数値のみ(原典の「N枚」表記は CAD 採用で廃止)
        Assert.Equal("2", RowOf(vm, a).CountText);
        Assert.Equal("1", RowOf(vm, b).CountText);
    }

    [Fact]
    public async Task 表示軸評価の母集合も選択中コレクションに限られる()
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

        var vm = NewImageTab(new AppSettings());
        await vm.InitializeAsync();

        // A 選択中にタグビュー軸へ: 値「赤」は A の normal 画像に存在しない → 値ノードなし(タグ名ノードのみ)
        await vm.SelectCollectionCommand.ExecuteAsync(a.Id);
        await vm.SelectAxisCommand.ExecuteAsync(view.Value.Id);
        Assert.True(vm.IsViewAxis);
        Assert.Equal("色", vm.Chips.Single().Label);

        // B 選択中: 母集合が B へ切り替わり distinct 値 1 件 → 一体型「色: 赤」(REQ-035)
        await vm.SelectCollectionCommand.ExecuteAsync(b.Id);
        Assert.Equal("色: 赤", vm.Chips.Single().Label);
    }

    [Fact]
    public async Task 選択コレクションと表示モードはsettingsへ書き戻され復元される()
    {
        var (a, _) = await SeedTwoCollectionsAsync();
        var settings = new AppSettings();
        var vm = NewImageTab(settings);
        await vm.InitializeAsync();
        await vm.SelectCollectionCommand.ExecuteAsync(a.Id);
        vm.SetListCommand.Execute(null);

        vm.CaptureSettings();
        Assert.Equal(a.Id, settings.LastCollectionId); // CR-5
        Assert.Equal("list", settings.DisplayMode);    // CR-6

        // 「再起動」: 同じ settings で新しい VM を作ると選択コレクションと表示モードが復元される
        var restored = NewImageTab(settings);
        await restored.InitializeAsync();
        Assert.Equal(a.Id, restored.SelectedCollectionId);
        Assert.True(restored.IsList);
        Assert.True(restored.ShowListPane);
        Assert.Equal(["a1.jpg", "a2.jpg"], ImageNames(restored));
    }

    [Fact]
    public async Task 保存済みコレクションが消えていれば未選択へフォールバックする()
    {
        await SeedTwoCollectionsAsync();
        var settings = new AppSettings { LastCollectionId = "missing-folder-id" };
        var vm = NewImageTab(settings);

        await vm.InitializeAsync();

        Assert.False(vm.IsCollectionSelected);
        Assert.True(vm.ShowCollectionPrompt);
        Assert.Null(settings.LastCollectionId); // 無効 id は設定からも除去される
    }
}
