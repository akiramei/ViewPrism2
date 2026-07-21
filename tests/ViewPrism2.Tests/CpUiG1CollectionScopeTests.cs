using System.Collections.Concurrent;
using System.Reflection;
using CommunityToolkit.Mvvm.Input;
using ViewPrism2.App.Services;
using ViewPrism2.App.ViewModels;
using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Repositories;
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
        public Task<bool> ConfirmAsync(string title, string message, string confirmLabel, bool destructive = false, string? cancelLabel = null) => Task.FromResult(true);

        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);

        public Task ShowFolderManagementAsync() => Task.CompletedTask;

        public Task ShowSettingsAsync() => Task.CompletedTask;

        public Task ShowSnapshotsAsync() => Task.CompletedTask;

        public Task ShowCollectionExportAsync(string collectionId) => Task.CompletedTask;

        public Task ShowCollectionImportAsync(string collectionId) => Task.CompletedTask;

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
    private ImageTabViewModel NewImageTab(
        AppSettings settings,
        IImageRepository? images = null,
        ITagRepository? tags = null)
    {
        images ??= _db.Images;
        tags ??= _db.Tags;
        var views = new ViewService(_db.Views, _db.Clock);
        return new ImageTabViewModel(
            _db.Folders, images, tags, new ImageSorter(), views,
            new NodeGraphBuilder(), new PathConditionConverter(), new ConditionEvaluator(),
            new SimilaritySearchService(_db.Folders, images, _db.Features, _db.Similarities, new FakePHashImageReader(), _db.Clock),
            new MergeService(images, tags, _db.Merges),
            new TrashService(images, _db.Folders, new FilePresenceProbe()),
            new StubWindowService(), settings, new WorkspaceService(_db.Workspaces, _db.Clock), TestLoc.Ja());
    }

    private class RepositorySpy<T> : DispatchProxy where T : class
    {
        public T Inner { get; set; } = null!;
        public ConcurrentQueue<(string Name, object?[] Arguments)> Calls { get; } = new();
        public Func<MethodInfo, object?[], (bool Handled, object? Result)>? Interceptor { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Assert.NotNull(targetMethod);
            var arguments = args ?? [];
            Calls.Enqueue((targetMethod.Name, arguments.ToArray()));
            if (Interceptor?.Invoke(targetMethod, arguments) is { Handled: true } intercepted)
                return intercepted.Result;
            return targetMethod.Invoke(Inner, arguments);
        }

        public int Count(string methodName) => Calls.Count(call => call.Name == methodName);

        public IEnumerable<object?[]> Arguments(string methodName)
            => Calls.Where(call => call.Name == methodName).Select(call => call.Arguments);
    }

    private static T Spy<T>(T inner, out RepositorySpy<T> spy) where T : class
    {
        var proxy = DispatchProxy.Create<T, RepositorySpy<T>>();
        spy = (RepositorySpy<T>)(object)proxy;
        spy.Inner = inner;
        return proxy;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 500 && !condition(); attempt++)
            await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.True(condition(), "condition did not become true within 5 seconds");
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
    public async Task ECO063_ビュー選択時に保存済みホームノードまで初期遷移する()
    {
        var (a, b) = await SeedTwoCollectionsAsync();
        var images = (await _db.Images.GetAllNormalAsync())
            .Where(image => image.SyncFolderId == a.Id)
            .OrderBy(image => image.FileName, StringComparer.Ordinal)
            .ToList();

        var tagService = new TagService(_db.Tags);
        var parentTag = (await tagService.CreateAsync("親", TagType.Simple)).Value!;
        var homeTag = (await tagService.CreateAsync("ホーム", TagType.Simple)).Value!;
        Assert.True((await tagService.TagImageAsync(images[0].Id, parentTag.Id, null)).IsSuccess);
        Assert.True((await tagService.TagImageAsync(images[0].Id, homeTag.Id, null)).IsSuccess);
        Assert.True((await tagService.TagImageAsync(images[1].Id, parentTag.Id, null)).IsSuccess);

        var viewService = new ViewService(_db.Views, _db.Clock);
        var view = (await viewService.CreateAsync("ホーム付きビュー")).Value!;
        var parentNode = new HierarchyNode
        {
            Id = IdGenerator.NewId(), ViewId = view.Id, TagId = parentTag.Id, Position = 0,
        };
        var homeNode = new HierarchyNode
        {
            Id = IdGenerator.NewId(), ViewId = view.Id, TagId = homeTag.Id,
            ParentId = parentNode.Id, Position = 0,
        };
        Assert.True((await viewService.SaveHierarchyAsync(
            view.Id, [parentNode, homeNode], homeNode.Id)).IsSuccess);

        var vm = NewImageTab(new AppSettings());
        await vm.InitializeAsync();
        await vm.SelectCollectionCommand.ExecuteAsync(a.Id);
        await vm.SelectAxisCommand.ExecuteAsync(view.Id);

        Assert.True(vm.IsViewAxis);
        Assert.False(vm.HomeActive);
        Assert.Equal(["親", "ホーム"], vm.Crumbs.Select(crumb => crumb.Name));
        Assert.Equal(["a1.jpg"], ImageNames(vm));

        // view軸のままcollectionを切替える再loadでも、rootへ退行せずhome pathを再適用する。
        await vm.SelectCollectionCommand.ExecuteAsync(b.Id);
        Assert.Equal(["親", "ホーム"], vm.Crumbs.Select(crumb => crumb.Name));
        Assert.Empty(ImageNames(vm));
        await vm.SelectCollectionCommand.ExecuteAsync(a.Id);
        Assert.Equal(["親", "ホーム"], vm.Crumbs.Select(crumb => crumb.Name));
        Assert.Equal(["a1.jpg"], ImageNames(vm));
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

    [Fact]
    public async Task ECO064_起動はcatalogと選択contentのloading状態を別々に公開する()
    {
        var (a, _) = await SeedTwoCollectionsAsync();
        var vm = NewImageTab(new AppSettings { LastCollectionId = a.Id });

        var initializing = vm.InitializeAsync();

        // shell-first CAD(IMG-019): 未ロードを空状態へ化けさせず、catalog/content を別状態で公開する。
        Assert.True(vm.IsCatalogLoading || vm.IsContentLoading);
        Assert.False(vm.ShowCollectionPrompt);
        Assert.False(vm.ShowEmptyMessage);

        await initializing;

        Assert.False(vm.IsCatalogLoading);
        Assert.False(vm.IsContentLoading);
        Assert.False(vm.HasCatalogError);
        Assert.False(vm.HasContentError);
        Assert.Equal(a.Id, vm.SelectedCollectionId);
        Assert.Equal(["a1.jpg", "a2.jpg"], ImageNames(vm));
    }

    [Fact]
    public async Task ECO064_起動と切替は全件APIを使わず選択collectionだけを読む()
    {
        var (a, b) = await SeedTwoCollectionsAsync();
        var imageRepo = Spy<IImageRepository>(_db.Images, out var imageSpy);
        var tagRepo = Spy<ITagRepository>(_db.Tags, out var tagSpy);
        var vm = NewImageTab(new AppSettings { LastCollectionId = a.Id }, imageRepo, tagRepo);

        await vm.InitializeAsync();

        Assert.Equal(0, imageSpy.Count(nameof(IImageRepository.GetAllNormalAsync)));
        Assert.Equal(0, tagSpy.Count(nameof(ITagRepository.GetAllImageTagsAsync)));
        Assert.Equal(1, imageSpy.Count(nameof(IImageRepository.GetNormalCountsByFolderAsync)));
        Assert.Contains(imageSpy.Arguments(nameof(IImageRepository.GetNormalByFolderAsync)),
            args => Equals(args[0], a.Id));
        Assert.Contains(tagSpy.Arguments(nameof(ITagRepository.GetImageTagsByFolderAsync)),
            args => Equals(args[0], a.Id));

        await vm.SelectCollectionCommand.ExecuteAsync(b.Id);

        Assert.Equal(1, imageSpy.Count(nameof(IImageRepository.GetNormalCountsByFolderAsync)));
        Assert.Contains(imageSpy.Arguments(nameof(IImageRepository.GetNormalByFolderAsync)),
            args => Equals(args[0], b.Id));
        Assert.Contains(tagSpy.Arguments(nameof(ITagRepository.GetImageTagsByFolderAsync)),
            args => Equals(args[0], b.Id));
        Assert.Equal(["b1.jpg"], ImageNames(vm));
    }

    [Fact]
    public async Task ECO064_catalog失敗は空状態へ化けず再試行できる()
    {
        await SeedTwoCollectionsAsync();
        var imageRepo = Spy<IImageRepository>(_db.Images, out var imageSpy);
        var failOnce = true;
        imageSpy.Interceptor = (method, _) =>
        {
            if (method.Name == nameof(IImageRepository.GetNormalCountsByFolderAsync) && failOnce)
            {
                failOnce = false;
                return (true, Task.FromException<IReadOnlyDictionary<string, int>>(
                    new InvalidOperationException("probe catalog failure")));
            }
            return (false, null);
        };
        var vm = NewImageTab(new AppSettings(), imageRepo);

        await vm.InitializeAsync();

        Assert.True(vm.HasCatalogError);
        Assert.False(vm.ShowCollectionPrompt);
        Assert.Empty(vm.Collections);

        await vm.RetryCatalogCommand.ExecuteAsync(null);

        Assert.False(vm.HasCatalogError);
        Assert.True(vm.ShowCollectionPrompt);
        Assert.Equal(2, vm.Collections.Count);
    }

    [Fact]
    public async Task ECO064_content失敗は0件へ化けず同じcollectionを再試行できる()
    {
        var (a, _) = await SeedTwoCollectionsAsync();
        var imageRepo = Spy<IImageRepository>(_db.Images, out var imageSpy);
        var failOnce = true;
        imageSpy.Interceptor = (method, _) =>
        {
            if (method.Name == nameof(IImageRepository.GetNormalByFolderAsync) && failOnce)
            {
                failOnce = false;
                return (true, Task.FromException<IReadOnlyList<ImageRecord>>(
                    new InvalidOperationException("probe content failure")));
            }
            return (false, null);
        };
        var vm = NewImageTab(new AppSettings { LastCollectionId = a.Id }, imageRepo);

        await vm.InitializeAsync();

        Assert.True(vm.HasContentError);
        Assert.False(vm.ShowEmptyMessage);
        Assert.Equal(a.Id, vm.SelectedCollectionId);

        await vm.RetryContentCommand.ExecuteAsync(null);

        Assert.False(vm.HasContentError);
        Assert.Equal(["a1.jpg", "a2.jpg"], ImageNames(vm));
    }

    [Fact]
    public async Task ECO131_RefreshContentのawait中コレクション切替は別母集合で上書きしない()
    {
        // ECO-131 R8 所見1: RefreshContentAsync は対話中シェルから fire-and-forget されるため、
        // ReloadImagesAsync の await 中にコレクション切替が起きると別コレクション母集合で上書きし得る。
        // 世代ガード(LoadContentAsync と同規律)でこの上書きを防ぐことを固定する。
        var (a, b) = await SeedTwoCollectionsAsync();
        var aImages = await _db.Images.GetNormalByFolderAsync(a.Id, TestContext.Current.CancellationToken);
        var imageRepo = Spy<IImageRepository>(_db.Images, out var imageSpy);
        var vm = NewImageTab(new AppSettings(), imageRepo);
        await vm.InitializeAsync();
        await vm.SelectCollectionCommand.ExecuteAsync(a.Id);
        Assert.Equal(a.Id, vm.SelectedCollectionId);
        Assert.Equal(["a1.jpg", "a2.jpg"], ImageNames(vm));

        // RefreshContentAsync の GetNormalByFolderAsync(A) をゲートして await 中に固定する
        var delayedA = new TaskCompletionSource<IReadOnlyList<ImageRecord>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        imageSpy.Interceptor = (method, args) =>
            method.Name == nameof(IImageRepository.GetNormalByFolderAsync) && Equals(args[0], a.Id)
                ? (true, delayedA.Task) : (false, null);
        var refresh = vm.RefreshContentAsync();
        await WaitUntilAsync(() => imageSpy.Arguments(nameof(IImageRepository.GetNormalByFolderAsync))
            .Any(args => Equals(args[0], a.Id)));

        // await 中に B へ切替(LoadContentAsync(B) 完了=世代 ++・_collectionId=B)
        imageSpy.Interceptor = null;
        await vm.SelectCollectionCommand.ExecuteAsync(b.Id);
        Assert.Equal(b.Id, vm.SelectedCollectionId);
        Assert.Equal(["b1.jpg"], ImageNames(vm));

        // 古い RefreshContent が resume しても B を A の母集合で上書きしない(世代ガード)
        delayedA.SetResult(aImages);
        await refresh;

        Assert.Equal(b.Id, vm.SelectedCollectionId);
        Assert.Equal(["b1.jpg"], ImageNames(vm)); // A の母集合で上書きされない
    }

    [Fact]
    public async Task ECO064_遅れて完了した旧collection結果は現在選択を上書きしない()
    {
        var (a, b) = await SeedTwoCollectionsAsync();
        var aImages = await _db.Images.GetNormalByFolderAsync(a.Id, TestContext.Current.CancellationToken);
        var imageRepo = Spy<IImageRepository>(_db.Images, out var imageSpy);
        var delayedA = new TaskCompletionSource<IReadOnlyList<ImageRecord>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        imageSpy.Interceptor = (method, args) =>
        {
            if (method.Name == nameof(IImageRepository.GetNormalByFolderAsync) && Equals(args[0], a.Id))
                return (true, delayedA.Task);
            return (false, null);
        };
        var vm = NewImageTab(new AppSettings(), imageRepo);
        await vm.InitializeAsync();

        var selectA = vm.SelectCollectionCommand.ExecuteAsync(a.Id);
        await WaitUntilAsync(() => imageSpy.Arguments(nameof(IImageRepository.GetNormalByFolderAsync))
            .Any(args => Equals(args[0], a.Id)));

        await vm.SelectCollectionCommand.ExecuteAsync(b.Id);
        Assert.Equal(b.Id, vm.SelectedCollectionId);
        Assert.Equal(["b1.jpg"], ImageNames(vm));

        delayedA.SetResult(aImages);
        await selectA;

        Assert.Equal(b.Id, vm.SelectedCollectionId);
        Assert.Equal(["b1.jpg"], ImageNames(vm));
    }
}
