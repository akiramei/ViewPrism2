using CommunityToolkit.Mvvm.Input;
using SkiaSharp;
using ViewPrism2.App.Services;
using ViewPrism2.App.ViewModels;
using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using ViewPrism2.Core.Services.Repair;
using ViewPrism2.Core.Services.Similarity;
using ViewPrism2.Core.Services.Viewer;
using ViewPrism2.Infrastructure.I18n;
using ViewPrism2.Infrastructure.Imaging;
using ViewPrism2.Infrastructure.Scanning;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// CP-L1-SMOKE(in-process 部分): フォルダ登録 → スキャン → シェル VM のグリッド表示 →
/// サムネイル生成 → NodeGraph 選択 → 詳細パネル、の正常系 1 本を実サービス+実 DB で通す。
/// プロセス起動・ウィンドウ生成の確認は Release ビルド後の手動起動で実施し報告書に記録する。
/// </summary>
[Trait("cp", "CP-L1-SMOKE")]
public sealed class CpL1SmokeTests : IDisposable
{
    private readonly TempDb _db = new();
    private readonly string _root;
    private readonly string _thumbDir;

    public CpL1SmokeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ViewPrism2.Tests", Guid.NewGuid().ToString("D"));
        _thumbDir = Path.Combine(_root, "thumbs");
        Directory.CreateDirectory(Path.Combine(_root, "files"));
    }

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

    [Fact]
    public async Task フォルダ登録からグリッド表示までの正常系1本()
    {
        // --- フィクスチャフォルダ(実画像 3 枚) ---
        var files = Path.Combine(_root, "files");
        ImageFixtures.WriteEncoded(Path.Combine(files, "photo1.jpg"), 800, 600, SKEncodedImageFormat.Jpeg);
        ImageFixtures.WriteEncoded(Path.Combine(files, "photo2.png"), 320, 240, SKEncodedImageFormat.Png);
        ImageFixtures.WriteBmp(Path.Combine(files, "photo3.bmp"), 64, 64);

        // --- フォルダ登録 → スキャン ---
        var folder = new SyncFolder { Id = IdGenerator.NewId(), Name = "smoke", Path = files };
        Assert.True((await _db.Folders.AddAsync(folder)).IsSuccess);

        var scan = new ScanService(_db.Folders, _db.Images, _db.Clock);
        var scanResult = await scan.ScanAsync(folder.Id, null, TestContext.Current.CancellationToken);
        Assert.True(scanResult.IsSuccess, scanResult.Message);
        Assert.Equal(3, scanResult.Value!.Added);

        // --- 実サービス合成(App と同じ構成。i18n は実資産を読む) ---
        var localization = new LocalizationService(
            I18nResourceLoader.Load(Path.Combine(AppContext.BaseDirectory, "Assets", "i18n")));
        var thumbnails = new ThumbnailService(_thumbDir);
        var viewService = new ViewService(_db.Views, _db.Clock);
        var tagService = new TagService(_db.Tags);
        var windows = new StubWindowService();
        var vm = new MainWindowViewModel(
            _db.Folders, _db.Images, _db.Tags, viewService,
            new NodeGraphBuilder(), new PathConditionConverter(), new ConditionEvaluator(),
            new SimilaritySearchService(_db.Folders, _db.Images, _db.Features, _db.Similarities, new FakePHashImageReader(), _db.Clock),
            new MergeService(_db.Images, _db.Tags, _db.Merges),
            new TrashService(_db.Images, _db.Folders, new FilePresenceProbe()),
            new ImageSorter(), thumbnails, localization, new AppSettings(), windows,
            new FolderManagementViewModel(_db.Folders, scan, localization, windows),
            new TagsTabViewModel(viewService, tagService, _db.Tags, localization, windows),
            new TaggingPanelViewModel(tagService, _db.Tags, localization, windows),
            new WorkspaceService(_db.Workspaces, _db.Clock));

        // --- 起動初期化: コレクション未選択 → 選択を促す空状態(REQ-053 v1.3/CR-2) ---
        await vm.InitializeAsync();
        Assert.False(vm.IsCollectionSelected);
        Assert.True(vm.ShowCollectionPrompt);
        Assert.True(vm.Browser.IsEmpty); // 母集合は空(横断表示なし)

        // --- コレクション選択 → 当該コレクションの normal 画像が母集合になる ---
        var collectionRow = vm.FolderPane.Folders.Single(r => r.Folder.Id == folder.Id);
        await ((IAsyncRelayCommand)vm.SelectCollectionCommand).ExecuteAsync(collectionRow);
        Assert.True(vm.IsCollectionSelected);
        Assert.True(collectionRow.IsSelected);
        Assert.Equal(3, collectionRow.ImageCount); // 項目に画像数(CR-8)
        Assert.False(vm.Browser.IsEmpty);
        Assert.Equal(3, vm.Browser.SortedItems.Count);
        Assert.Single(vm.Browser.Rows); // 幅未供給時の暫定 4 列 × 3 枚 = 1 行
        Assert.Equal("photo1.jpg", vm.Browser.SortedItems[0].FileName); // name asc

        // --- サムネイル生成(「サムネイルが並ぶ」の in-process 確認) ---
        foreach (var item in vm.Browser.SortedItems)
        {
            var thumb = await thumbnails.GetOrCreateAsync(item.AbsolutePath);
            Assert.NotNull(thumb);
            Assert.True(File.Exists(thumb));
        }

        // --- 選択 → 詳細パネル(REQ-043) ---
        vm.Browser.HandleItemPointer(vm.Browser.SortedItems[0], ctrl: false, shift: false, isDoubleClick: false);
        var waited = 0;
        while (!vm.Detail.HasImage && waited < 100)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
            waited++;
        }

        Assert.True(vm.Detail.HasImage);
        Assert.Equal("photo1.jpg", vm.Detail.FileName);
        Assert.NotEqual(string.Empty, vm.Detail.SizeText);

        // --- タグ付け → ビュー+階層 → NodeGraph 選択 → 絞り込み ---
        var tag = await tagService.CreateAsync("色", TagType.Textual);
        Assert.True(tag.IsSuccess);
        var imageId = vm.Browser.SortedItems[0].Record.Id;
        Assert.True((await tagService.TagImageAsync(imageId, tag.Value!.Id, "赤")).IsSuccess);

        var view = await viewService.CreateAsync("スモークビュー");
        Assert.True(view.IsSuccess);
        Assert.True((await viewService.AddNodeAsync(view.Value!.Id, tag.Value.Id, null, 0)).IsSuccess);

        await vm.ReloadAsync();
        var viewItem = vm.Recents.First(i => i.View?.Id == view.Value.Id);
        await ((IAsyncRelayCommand)vm.SelectViewListItemCommand).ExecuteAsync(viewItem);

        Assert.Single(vm.TreeRoots);
        var root = vm.TreeRoots[0];
        Assert.Single(root.Children); // distinct 値 1 件 → 一体型「色: 赤」(REQ-035)
        Assert.Equal("色: 赤", root.Children[0].DisplayName);

        vm.SelectedTreeNode = root.Children[0];
        var filtered = Assert.Single(vm.Browser.SortedItems); // equals(赤) で 1 枚に絞られる
        Assert.Equal(imageId, filtered.Record.Id);

        vm.SelectedTreeNode = root; // ルート=ビュー条件のみ(無条件)→ 全 3 枚
        Assert.Equal(3, vm.Browser.SortedItems.Count);

        // --- v1.2 シェル: タブ切替(タグタブの遅延読込)+タグ編集モード(右パネル切替+ビューア無効) ---
        Assert.True(vm.IsImagesTabSelected); // 初期は画像タブ
        vm.SelectedTabIndex = 0;
        Assert.True(vm.IsTagsTabSelected);
        var waitedTab = 0;
        while (vm.TagsTab.Views.Count == 0 && waitedTab < 100)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
            waitedTab++;
        }

        Assert.Contains(vm.TagsTab.Views, r => r.View.Id == view.Value.Id);
        Assert.Contains(vm.TagsTab.Palette.Tags, r => r.Tag.Id == tag.Value.Id);

        vm.SelectedTabIndex = 1;
        vm.IsTagEditMode = true; // タグ付与パネルへ切替(REQ-046)
        Assert.True(vm.Browser.SuppressOpenItem); // タグ編集モード中はダブルクリック無効(REQ-041 v1.2)
        vm.Browser.HandleItemPointer(vm.Browser.SortedItems[0], ctrl: false, shift: false, isDoubleClick: false);
        var waitedTagging = 0;
        while (!vm.Tagging.HasSelection && waitedTagging < 100)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
            waitedTagging++;
        }

        Assert.True(vm.Tagging.HasSelection);
        Assert.Contains(vm.Tagging.CurrentTags, r => r.Tag.Id == tag.Value.Id); // 付与済み「色」が現在タグに出る
        vm.IsTagEditMode = false;
        Assert.False(vm.Browser.SuppressOpenItem);
    }

    [Fact]
    public void V2_ビューア3モード切替とページ送りと位置記憶の経路()
    {
        // CP-L1-SMOKE v2.0 経路(in-process): 3 モード切替+各モードで送り 1 回+モード別位置記憶
        // +設定の即時永続化ラウンドトリップ。描画/ウィンドウ生成は手動起動で確認(報告書記録)。
        var items = Enumerable.Range(0, 6)
            .Select(i => Entry($"img{i}.jpg"))
            .ToList();

        // 永続化先(WindowService と同等: モデル → AppSettings → SettingsStore)
        var settings = new AppSettings();
        var saved = new List<ViewerSettingsModel>();
        var vm = new ViewerViewModel(items, startIndex: 2, new ViewerSettingsModel(), model =>
        {
            model.ApplyTo(settings);
            saved.Add(model);
        });

        // 起動 index=2 → normal で現在 3/6
        Assert.True(vm.IsNormal);
        Assert.Equal("3 / 6", vm.CurrentPositionText);

        // scroll へ → 位置記憶の初期値=起動 index(2)。送り 1 回(→ 3)
        vm.SetScrollModeCommand.Execute(null);
        Assert.True(vm.IsScroll);
        Assert.Equal(2, vm.CurrentIndex);
        vm.NextCommand.Execute(null);
        Assert.Equal(3, vm.CurrentIndex);

        // spread-right へ → spread の記憶は起動 index(2)のまま。送り(2 ページ)→ 4
        vm.SetSpreadRightModeCommand.Execute(null);
        Assert.True(vm.IsSpread);
        Assert.Equal(2, vm.CurrentIndex);
        vm.NextCommand.Execute(null);
        Assert.Equal(4, vm.CurrentIndex); // doublePage 既定 step=2

        // scroll へ戻す → scroll の記憶 3 が復元(共通 index 引き継ぎではない — FMEA-020)
        vm.SetScrollModeCommand.Execute(null);
        Assert.Equal(3, vm.CurrentIndex);

        // 設定変更の即時永続化(REQ-059): mode/customGapPx が settings へ反映
        Assert.Equal("scroll", settings.ViewerMode);
        vm.GapMode = GapMode.Loose;
        vm.CustomGapPx = 16;
        Assert.Equal("loose", settings.ViewerGapMode);
        Assert.Equal(16, settings.ViewerCustomGapPx);
        Assert.NotEmpty(saved);
    }

    private static ImageEntry Entry(string name)
    {
        var record = new ImageRecord
        {
            Id = name,
            SyncFolderId = "f",
            RelativePath = name,
            FileName = name,
            FileSize = 1,
            Hash = new string('0', 64),
            CreatedDate = "2026-06-13T00:00:00.000Z",
            ModifiedDate = "2026-06-13T00:00:00.000Z",
        };
        return new ImageEntry(record, @"C:\img\" + name, []);
    }
}
