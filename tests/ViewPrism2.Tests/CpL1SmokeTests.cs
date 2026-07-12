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

    [Fact]
    public async Task フォルダ登録からグリッド表示_タグ台帳反映_タグ編集までの正常系1本()
    {
        // ECO-024: 原典画像タブ(Browser/Detail)撤去後の CP-L1-SMOKE 統合スモークを
        // 新 surface(シェル→ImageTabViewModel)経路へ移行。詳細パネル(REQ-043)は ECO-023 で撤回済のため
        // 代わりにタグ台帳反映(タグビュー軸)+タグ編集モードの現在タグ表示を通す。

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

        // --- 実サービス合成(App と同じ構成。i18n は実資産を読む。ECO-024 後の薄いシェル ctor) ---
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
            new ImageSorter(), localization, new AppSettings(), windows,
            new TagsTabViewModel(viewService, tagService, _db.Tags, localization, windows),
            new WorkspaceService(_db.Workspaces, _db.Clock));

        var img = vm.ImageTab;

        // --- 起動初期化: コレクション未選択 → 選択を促す空状態(REQ-053 v1.3/CR-2) ---
        await vm.InitializeAsync();
        Assert.True(vm.IsImagesTabSelected); // 初期は画像タブ
        Assert.False(img.IsCollectionSelected);
        Assert.True(img.ShowCollectionPrompt);

        // --- コレクション選択 → 当該コレクションの normal 画像が母集合になる ---
        await ((IAsyncRelayCommand)img.SelectCollectionCommand).ExecuteAsync(folder.Id);
        Assert.True(img.IsCollectionSelected);
        var fileItems = img.Items.Where(i => !i.IsFolder).ToList();
        Assert.Equal(3, fileItems.Count);
        Assert.Equal("photo1.jpg", fileItems[0].Name); // name asc

        // --- サムネイル生成(「サムネイルが並ぶ」の in-process 確認) ---
        foreach (var item in fileItems)
        {
            Assert.NotNull(item.AbsolutePath);
            var thumb = await thumbnails.GetOrCreateAsync(item.AbsolutePath!);
            Assert.NotNull(thumb);
            Assert.True(File.Exists(thumb));
        }

        // --- タグ付け → ビュー+階層 → タグ台帳反映 → タグビュー軸 → ノードで絞り込み ---
        var tag = await tagService.CreateAsync("色", TagType.Textual);
        Assert.True(tag.IsSuccess);
        var imageId = fileItems[0].Id;
        Assert.True((await tagService.TagImageAsync(imageId, tag.Value!.Id, "赤")).IsSuccess);

        var view = await viewService.CreateAsync("スモークビュー");
        Assert.True(view.IsSuccess);
        Assert.True((await viewService.AddNodeAsync(view.Value!.Id, tag.Value.Id, null, 0)).IsSuccess);

        // タグ/ビューの永続変更を画像タブへ反映(タブ切替 stale 経路と同じ ReloadTagCatalogAsync)
        await img.ReloadTagCatalogAsync();

        // タグビュー軸へ切替 → ノードチップが台帳から構築される(REQ-035: distinct 値 1 件 → 一体型「色: 赤」)
        await ((IAsyncRelayCommand)img.SelectAxisCommand).ExecuteAsync(view.Value.Id);
        Assert.True(img.IsViewAxis);
        var navChip = img.Chips.Single(c => c.IsNav);
        Assert.Equal("色: 赤", navChip.Label);

        // ノードへ潜る → equals(赤) で 1 枚に絞られる
        img.ClickChipCommand.Execute(navChip);
        var filtered = img.Items.Where(i => !i.IsFolder).ToList();
        Assert.Single(filtered);
        Assert.Equal(imageId, filtered[0].Id);

        // --- タブ切替: タグタブの遅延読込 ---
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

        // --- 画像タブへ戻りタグ編集モード: 選択画像の現在タグに付与済み「色」が出る(REQ-046) ---
        vm.SelectedTabIndex = 1;
        await ((IAsyncRelayCommand)img.SelectAxisCommand).ExecuteAsync("fs"); // FS 軸の素の一覧で編集
        img.ToggleEditCommand.Execute(null);
        Assert.True(img.EditMode);
        var editItem = img.Items.First(i => !i.IsFolder && i.Id == imageId);
        img.HandleItemClick(editItem, ctrl: false, shift: false);
        Assert.True(img.HasSelection);
        Assert.Contains(img.CurrentTags, t => t.Id == tag.Value.Id); // 付与済み「色」が現在タグに出る
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
