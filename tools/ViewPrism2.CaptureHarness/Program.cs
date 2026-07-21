// ViewPrism2 撮影ハーネス(R7 セルフゴールデン用・ECO-103 で正式資産化)。
// 実 App スタイル+Skia のヘッドレス実描画でタグタブ各状態を PNG 化し、CAD
// (ViewPrismUI docs/screens/captures/)との並置突合に使う。
//
// 来歴: ECO-099 R7 で開発(scratch)→ ECO-099(配置モデル統一)/ECO-100(D&D)/ECO-103(保存モデル)の
//       セルフゴールデンに実運用。様式= ECO-100 教訓3「headless+Skia self-golden」(BomDD 昇格候補)。
// 公開安全(ECO-057): ユーザー絶対パスをリテラルで持たない(出力先= 第1引数 or 実行ディレクトリ/captures)。
// 使い方: dotnet run --project tools/ViewPrism2.CaptureHarness -- <出力ディレクトリ>
// 注意: Date/乱数なし・シードは mock デモデータ(タグ管理.dc.html tagDefs)の転記=決定論。
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ViewPrism2.App.Services;
using ViewPrism2.App.ViewModels;
using ViewPrism2.App.Views;
using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using ViewPrism2.Core.Services.Repair;
using ViewPrism2.Core.Services.Similarity;
using ViewPrism2.Infrastructure.Database;
using ViewPrism2.Infrastructure.I18n;
using ViewPrism2.Infrastructure.Imaging;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var outDir = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "captures");
        Directory.CreateDirectory(outDir);

        AppBuilder.Configure<ViewPrism2.App.App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .WithInterFont()
            .SetupWithoutStarting();

        var task = RunAsync(outDir);
        while (!task.IsCompleted)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }

        if (task.Exception is { } ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }

        Console.WriteLine("done: " + outDir);
        return 0;
    }

    private static async Task RunAsync(string outDir)
    {
        // ECO-105: 前回 run の DB を引き継がず毎回新規に作る(シードが正本=決定論)。
        // 既存 DB の再オープンは同一出力先の再実行で DuplicateTagName→NRE になっていた。
        var dbDir = Path.Combine(outDir, "db-work");
        if (Directory.Exists(dbDir))
        {
            Directory.Delete(dbDir, recursive: true);
        }

        Directory.CreateDirectory(dbDir);
        using var manager = DatabaseManager.Open(Path.Combine(dbDir, "vp2.db"), new SystemClock());
        var tagRepo = new TagRepository(manager);
        var viewRepo = new ViewRepository(manager);
        var tagService = new TagService(tagRepo);
        var viewService = new ViewService(viewRepo, new SystemClock(), tagRepo);

        // ---- mock デモデータ(タグ管理.dc.html tagDefs/views を転記) ----
        async Task<Tag> T(string name, TagType type, string color)
            => (await tagService.CreateAsync(name, type, color: color)).Value!;
        var region = await T("地域", TagType.Textual, "#12a594");
        var season = await T("季節", TagType.Textual, "#30a46c");
        var gender = await T("性別", TagType.Textual, "#e93d82");
        var compo = await T("構図", TagType.Textual, "#2f6bed");
        var rating = await T("評価", TagType.Numeric, "#e8b931");
        var priority = await T("重要度", TagType.Numeric, "#f2912b");
        var scenery = await T("風景", TagType.Simple, "#30a46c");
        var person = await T("人物", TagType.Simple, "#e93d82");
        var nature = await T("自然", TagType.Simple, "#12a594");
        var evt = await T("イベント", TagType.Simple, "#8b5cf6");
        await T("お気に入り", TagType.Simple, "#e5484d");
        await T("作業中", TagType.Simple, "#f2912b");
        var pref = await T("都道府県", TagType.Textual, "#7c53d6");
        var shootloc = await T("撮影ロケーション", TagType.Textual, "#f2912b");

        await tagService.SetTextualSettingsAsync(region.Id, ["国内", "海外"]);
        await tagService.SetTextualSettingsAsync(season.Id, ["春", "夏", "秋", "冬"]);
        await tagService.SetTextualSettingsAsync(gender.Id, ["男", "女", "その他"]);
        await tagService.SetTextualSettingsAsync(compo.Id, ["俯瞰", "煽り", "正面", "横顔"]);
        await tagService.SetNumericSettingsAsync(rating.Id, 1, 5, 1, "★");
        await tagService.SetNumericSettingsAsync(priority.Id, 1, 3, 1, "");
        await tagService.SetTextualSettingsAsync(pref.Id,
        [
            "北海道", "青森県", "岩手県", "宮城県", "秋田県", "山形県", "福島県", "茨城県", "栃木県", "群馬県",
            "埼玉県", "千葉県", "東京都", "神奈川県", "新潟県", "富山県", "石川県", "福井県", "山梨県", "長野県",
            "岐阜県", "静岡県", "愛知県", "三重県", "滋賀県", "京都府", "大阪府", "兵庫県", "奈良県", "和歌山県",
            "鳥取県", "島根県", "岡山県", "広島県", "山口県", "徳島県", "香川県", "愛媛県", "高知県", "福岡県",
            "佐賀県", "長崎県", "熊本県", "大分県", "宮崎県", "鹿児島県", "沖縄県",
        ]);
        await tagService.SetTextualSettingsAsync(shootloc.Id,
        [
            "スタジオ第2ブース（窓際・自然光・レフ板あり）", "屋外・河川敷の午後逆光", "市街地・夜景",
            "海岸（日没前後のマジックアワー）",
        ]);

        var viewLoc = (await viewService.CreateAsync("ロケーション")).Value!;
        await viewService.CreateAsync("人物カタログ");
        await viewService.CreateAsync("制作ステータス");
        await viewService.CreateAsync("季節アーカイブ");

        // ---- VM+View 構築(ja) ----
        var locJa = new LocalizationService(
            I18nResourceLoader.Load(Path.Combine(AppContext.BaseDirectory, "Assets", "i18n")), "ja");
        var tab = new TagsTabViewModel(viewService, tagService, tagRepo, locJa, new StubWindows());
        await tab.EnsureLoadedAsync();
        var locRow = tab.Views.First(r => r.Name == "ロケーション");
        tab.SelectViewCommand.Execute(locRow);
        await PumpAsync();
        if (!tab.Editor.HasView)
        {
            throw new InvalidOperationException("view not loaded");
        }

        // mock ツリー: 地域{風景, 人物(HOME)} / 季節{自然, イベント} / 評価。地域=条件「値が一致: 国内」
        var regionNode = tab.Editor.AddNode(region, null)!;
        var sceneryNode = tab.Editor.AddNode(scenery, regionNode)!;
        var personNode = tab.Editor.AddNode(person, regionNode)!;
        var seasonNode = tab.Editor.AddNode(season, null)!;
        tab.Editor.AddNode(nature, seasonNode);
        tab.Editor.AddNode(evt, seasonNode);
        tab.Editor.AddNode(rating, null);
        tab.Editor.SetCondition(regionNode, HierarchyConditionType.Equals, """{"value":"国内"}""");
        tab.Editor.ToggleHomeCommand.Execute(personNode);
        tab.Editor.SelectedNode = sceneryNode;

        // ECO-103: 保存してクリーン基準を作る(v4= クリーン時は 3 表示なし)
        tab.Editor.SavedToastDuration = TimeSpan.FromMilliseconds(1); // 基準撮影にトーストを残さない
        await SaveAsync(tab);
        await PumpAsync();
        tab.Editor.SavedToastDuration = TimeSpan.FromHours(1);        // 以降のトースト撮影用に固定表示
        tab.Editor.GuardAttentionRevertDelay = TimeSpan.FromHours(1); // attention 撮影用に固定表示

        var window = new Window
        {
            Content = new TagsTabView { DataContext = tab },
            Width = 1240,
            Height = 860,
        };
        window.Show();
        await PumpAsync();

        Capture(window, Path.Combine(outDir, "impl-full.png")); // クリーン(バー/チップなし=VC-TAG-16⑤)

        // ---- P2-dirty: 視覚的に同一のツリーのまま dirty にする(ホーム 2 回トグル=構造不変) ----
        tab.Editor.ToggleHomeCommand.Execute(personNode);
        tab.Editor.ToggleHomeCommand.Execute(personNode);
        await PumpAsync();
        Capture(window, Path.Combine(outDir, "impl-dirty.png"));

        // ---- P2-dirty-attention: 遷移ガード発火(復帰タイマは撮影用に停止) ----
        tab.Editor.GuardNavigation();
        await PumpAsync();
        Capture(window, Path.Combine(outDir, "impl-dirty-attention.png"));
        tab.Editor.CancelPlacingCommand.Execute(null); // no-op(状態には触れない)

        // ---- P2-saved-toast: 保存(トーストは撮影用に固定表示) ----
        await SaveAsync(tab);
        await PumpAsync();
        Capture(window, Path.Combine(outDir, "impl-saved-toast.png"));

        // ---- 配置/移動状態(ECO-099/100 の回帰確認用に維持) ----
        var genderRow = tab.Palette.Tags.First(r => r.Tag.Id == gender.Id);
        tab.TogglePlacing(genderRow);
        await PumpAsync();
        Capture(window, Path.Combine(outDir, "impl-placing.png"));
        tab.Editor.CancelPlacingCommand.Execute(null);
        await PumpAsync();

        window.Close();
        await PumpAsync();

        // ---- NAV-dirty: シェル(MainWindow)のタグタブ琥珀ドット。
        //      前段の撮影用固定タイマ(トースト/attention)を持ち越さないため新規 TagsTab VM で構築 ----
        var navTab = new TagsTabViewModel(viewService, tagService, tagRepo, locJa, new StubWindows());
        await navTab.EnsureLoadedAsync();
        navTab.SelectViewCommand.Execute(navTab.Views.First(r => r.Name == "ロケーション"));
        await PumpAsync();
        var featureRepo = new ImageFeatureRepository(manager);
        var simRepo = new ImageSimilarityRepository(manager);
        var shell = new MainWindowViewModel(
            new SyncFolderRepository(manager), new ImageRepository(manager), tagRepo, viewService,
            new NodeGraphBuilder(), new PathConditionConverter(), new ConditionEvaluator(),
            new SimilaritySearchService(new SyncFolderRepository(manager), new ImageRepository(manager),
                featureRepo, simRepo, new PHashImageReader(), new SystemClock()),
            new MergeService(new ImageRepository(manager), tagRepo, new MergeRepository(manager)),
            new TrashService(new ImageRepository(manager), new SyncFolderRepository(manager), new FilePresenceProbe()),
            new ImageSorter(), locJa, new AppSettings(), new StubWindows(),
            navTab, new WorkspaceService(new WorkspaceRepository(manager), new SystemClock()));
        shell.ShowTagsTabCommand.Execute(null);
        var navHome = navTab.Editor.Roots.First(n => n.Children.Count > 0).Children[^1];
        navTab.Editor.ToggleHomeCommand.Execute(navHome);
        navTab.Editor.ToggleHomeCommand.Execute(navHome); // 視覚不変の dirty(HOME は元位置へ戻る)
        var mainWindow = new MainWindow { DataContext = shell, Width = 1240, Height = 860 };
        mainWindow.Show();
        await PumpAsync();
        await PumpAsync();
        Capture(mainWindow, Path.Combine(outDir, "impl-nav-dirty.png"));
        mainWindow.Close();
        await PumpAsync();

        await CaptureFileListSortAsync(outDir, manager, tagRepo, tagService, viewService, locJa);
        await CaptureFileOpsAsync(outDir, manager, tagRepo, viewService, locJa);
        await CaptureScanSummaryAsync(outDir, locJa);
        await CapturePendingReviewAsync(outDir, manager, tagRepo, viewService, locJa);
    }

    /// <summary>
    /// ECO-129(pending_review PD-1〜4): 未裁定バッジ+裁定ダイアログの R7 並置用撮影。
    /// 原器= pending_review/PD-*.png。pending 行を直接シード(実ファイルなし=クローム比較・ECO-109 同様式)。
    /// </summary>
    private static async Task CapturePendingReviewAsync(
        string outDir, DatabaseManager manager, TagRepository tagRepo,
        ViewService viewService, LocalizationService locJa)
    {
        var folders = new SyncFolderRepository(manager);
        var images = new ImageRepository(manager);
        var col = (await folders.GetAllAsync()).First(f => f.Id == "col-eco109");

        // pending 3 種をシード(PD-1 バッジ+PD-2/3 一覧の由来チップ)
        async Task<ImageRecord> SeedPendingAsync(string path, PendingOrigin origin, string? candidate = null)
        {
            var record = new ImageRecord
            {
                Id = IdGenerator.NewId(),
                SyncFolderId = col.Id,
                RelativePath = path,
                FileName = path[(path.LastIndexOf('/') + 1)..],
                FileSize = 2_516_582, // 2.4 MB(PD-2 の比較行)
                Hash = "pending-" + path,
                Status = ImageStatus.Pending,
                PendingOrigin = origin,
                CandidateLinkId = candidate,
                CreatedDate = "2026-07-19T09:02:00.000Z",
                ModifiedDate = "2026-07-20T13:41:00.000Z",
            };
            await images.AddAsync(record);
            return record;
        }

        var changed = await SeedPendingAsync("hakone_0142.jpg", PendingOrigin.Changed);
        await SeedPendingAsync("hakone_0198.jpg", PendingOrigin.Changed);
        var missing = new ImageRecord
        {
            Id = IdGenerator.NewId(), SyncFolderId = col.Id, RelativePath = "album_0004.png",
            FileName = "album_0004.png", FileSize = 1, Hash = "same-content",
            Status = ImageStatus.Missing,
            CreatedDate = "2026-01-01T00:00:00.000Z", ModifiedDate = "2026-01-01T00:00:00.000Z",
        };
        await images.AddAsync(missing);
        await SeedPendingAsync("scan_0001.jpg", PendingOrigin.New, missing.Id);
        await SeedPendingAsync("dsc_8820.jpg", PendingOrigin.Restored);

        // ---- PD-1: 画像タブのバッジ+⋯メニュー入口 ----
        var featureRepo = new ImageFeatureRepository(manager);
        var simRepo = new ImageSimilarityRepository(manager);
        var tab = new ImageTabViewModel(
            folders, images, tagRepo, new ImageSorter(),
            viewService, new NodeGraphBuilder(), new PathConditionConverter(), new ConditionEvaluator(),
            new SimilaritySearchService(folders, images, featureRepo, simRepo, new PHashImageReader(), new SystemClock()),
            new MergeService(images, tagRepo, new MergeRepository(manager)),
            new TrashService(images, folders, new FilePresenceProbe()),
            new StubWindows(), new AppSettings(), new WorkspaceService(new WorkspaceRepository(manager), new SystemClock()),
            locJa);
        await tab.InitializeAsync(col.Id);
        tab.SetGridCommand.Execute(null);
        var tabWindow = new Window { Content = new ImageTabView { DataContext = tab }, Width = 1240, Height = 860 };
        tabWindow.Show();
        await PumpAsync();
        tab.ToggleMoreMenuCommand.Execute(null);
        await PumpAsync();
        Capture(tabWindow, Path.Combine(outDir, "impl-pending-PD-1.png"));   // 原器 PD-1.png(バッジ+メニュー)
        tabWindow.Close();
        await PumpAsync();

        // ---- PD-2/3/4: 裁定ダイアログ ----
        var review = new ViewPrism2.Core.Services.Repair.PendingReviewService(images);
        async Task<PendingReviewViewModel> NewVmAsync()
        {
            var vm = new PendingReviewViewModel(review, images, tagRepo, locJa, new StubWindows(), col);
            await vm.LoadAsync();
            return vm;
        }

        async Task ShotAsync(PendingReviewViewModel vm, string file)
        {
            var window = new PendingReviewWindow { DataContext = vm };
            window.Show();
            await PumpAsync();
            await PumpAsync();
            Capture(window, Path.Combine(outDir, file));
            window.Close();
            await PumpAsync();
        }

        var pd2 = await NewVmAsync();
        pd2.Selected = pd2.Items.First(i => string.Equals(i.Record.Id, changed.Id, StringComparison.Ordinal));
        await ShotAsync(pd2, "impl-pending-PD-2.png");                        // 内容変更由来

        var pd3 = await NewVmAsync();
        pd3.Selected = pd3.Items.First(i => i.Record.CandidateLinkId is not null);
        await ShotAsync(pd3, "impl-pending-PD-3.png");                        // 新規・候補あり

        // PD-4 空状態: 全件裁定して空へ(受け入れ=T13)
        var pd4 = await NewVmAsync();
        while (pd4.Items.Count > 0)
        {
            pd4.Selected = pd4.Items[0];
            await pd4.AcceptCommand.ExecuteAsync(null);
        }

        await ShotAsync(pd4, "impl-pending-PD-4.png");
    }

    /// <summary>
    /// ECO-130(scan_summary SC-1〜6): 二段階スキャンの R7 並置用撮影。原器= scan_summary/SC-*.png。
    /// ステージングは PresentSummary へ直接注入(実スキャンなし=決定論)。数値は原器 mock と同値。
    /// SC-6 は ConfirmDialog(CMP-011 既存部品)委譲のため scan 文言で撮影する。
    /// </summary>
    private static async Task CaptureScanSummaryAsync(string outDir, LocalizationService locJa)
    {
        ScanSummaryViewModel NewVm(string name, string path) => new(
            new ScanCoordinator(null!), locJa, new StubWindows(),
            new SyncFolder { Id = "scan-demo", Name = name, Path = path });

        async Task ShotAsync(ScanSummaryViewModel vm, string file, bool detail = false)
        {
            if (detail)
            {
                vm.ShowDetailCommand.Execute(null);
            }

            var window = new ScanSummaryWindow { DataContext = vm };
            window.Show();
            await PumpAsync();
            await PumpAsync();
            Capture(window, Path.Combine(outDir, file));
            window.Close();
            await PumpAsync();
        }

        // SC-1 スキャン中(AutoStart=false で表示のみ再現。数値は原器と同値)
        var scanning = NewVm("メイン写真庫", @"D:\Photos\Main");
        scanning.AutoStart = false;
        scanning.ProcessedText = locJa.T("scan.processed", new Dictionary<string, string> { ["count"] = "132,480" });
        scanning.ElapsedText = locJa.T("scan.elapsed", new Dictionary<string, string> { ["time"] = "01:12" });
        await ShotAsync(scanning, "impl-scan-SC-1.png");

        // SC-2 小規模(グリーン 0.07%・変更 28)
        var small = NewVm("スクリーンショット", @"D:\Photos\Shots");
        small.PresentSummary(new ScanStaging
        {
            FolderId = "scan-demo", ManagedTotal = 12400, ScannedFiles = 12391,
            Unchanged = 12372, ContentChanged = 3, AddedPending = 16, Reappeared = 0,
            MissingFromNormal = 9, MissingFromPending = 0,
            DeletedUnchanged = 0, DeletedMetaRefreshed = 0, PendedWithoutMeta = 0, ReadFailures = 0,
            Adds = [], MetaUpdates = [], StatusUpdates = [], Deletes = [], Examples = [],
        });
        await ShotAsync(small, "impl-scan-SC-2.png");

        // SC-3 中規模(イエロー 3.8%・変更 10,000)+SC-5 詳細(同一ステージング)
        ScanSummaryViewModel Medium()
        {
            var vm = NewVm("メイン写真庫", @"D:\Photos\Main");
            vm.PresentSummary(new ScanStaging
            {
                FolderId = "scan-demo", ManagedTotal = 259984, ScannedFiles = 250142,
                Unchanged = 249963, ContentChanged = 124, AddedPending = 16, Reappeared = 0,
                MissingFromNormal = 9842, MissingFromPending = 18,
                DeletedUnchanged = 0, DeletedMetaRefreshed = 37, PendedWithoutMeta = 0, ReadFailures = 2,
                Deletes = [],
                // 候補件数(裁定対象の内数)は変更案リストから導出されるため件数を実データと同型に埋める
                MetaUpdates = Enumerable.Range(0, 124)
                    .Select(i => new ScanFileMetaUpdate($"img-{i:D6}", "hash", 1, "2026-01-01T00:00:00.000Z"))
                    .ToList(),
                StatusUpdates = Enumerable.Range(0, 9842)
                    .Select(i => new ScanStatusUpdate($"mis-{i:D6}", ImageStatus.Missing))
                    .ToList(),
                Adds = Enumerable.Range(0, 16)
                    .Select(i => new ImageRecord
                    {
                        Id = $"new-{i:D4}",
                        SyncFolderId = "scan-demo",
                        RelativePath = $"2026/スキャン/scan_{i:D4}.jpg",
                        FileName = $"scan_{i:D4}.jpg",
                        FileSize = 1,
                        Hash = "hash",
                        Status = ImageStatus.Pending,
                        CandidateLinkId = $"mis-{i:D6}",
                        CreatedDate = "2026-01-01T00:00:00.000Z",
                        ModifiedDate = "2026-01-01T00:00:00.000Z",
                    })
                    .ToList(),
                Examples =
                [
                    new ScanTransitionExample(ScanTransitionKind.ContentChanged, "2024/旅行/hakone_0142.jpg"),
                    new ScanTransitionExample(ScanTransitionKind.ContentChanged, "2024/旅行/hakone_0198.jpg"),
                    new ScanTransitionExample(ScanTransitionKind.MissingFromNormal, "2023/家族/album_0004.png"),
                    new ScanTransitionExample(ScanTransitionKind.MissingFromNormal, "2023/家族/album_0005.png"),
                    new ScanTransitionExample(ScanTransitionKind.MissingFromPending, "取り込み/dsc_8811.jpg"),
                    new ScanTransitionExample(ScanTransitionKind.AddedPending, "2026/スキャン/scan_0001.jpg"),
                ],
            });
            return vm;
        }

        await ShotAsync(Medium(), "impl-scan-SC-3.png");
        await ShotAsync(Medium(), "impl-scan-SC-5.png", detail: true);

        // SC-4 大規模(レッド 99.0%)— 適用ボタンは有効のまま(REQ-100)
        var large = NewVm("メイン写真庫", @"D:\Photos\Main");
        large.PresentSummary(new ScanStaging
        {
            FolderId = "scan-demo", ManagedTotal = 260000, ScannedFiles = 2600,
            Unchanged = 2600, ContentChanged = 0, AddedPending = 0, Reappeared = 0,
            MissingFromNormal = 257400, MissingFromPending = 0,
            DeletedUnchanged = 0, DeletedMetaRefreshed = 0, PendedWithoutMeta = 0, ReadFailures = 0,
            Adds = [], MetaUpdates = [], StatusUpdates = [], Deletes = [], Examples = [],
        });
        await ShotAsync(large, "impl-scan-SC-4.png");

        // SC-6 適用の確認(CMP-011 ConfirmDialog・primary=物理非破壊)
        var confirm = new ConfirmDialog(
            new LocalizationProxy(locJa),
            locJa.T("scan.applyConfirmTitle"),
            locJa.T("scan.applyConfirmMessage", new Dictionary<string, string>
            {
                ["count"] = "10,000",
                ["missing"] = "9,842",
            }),
            locJa.T("scan.applyConfirmCta", new Dictionary<string, string> { ["count"] = "10,000" }),
            destructive: false);
        confirm.Show();
        await PumpAsync();
        Capture(confirm, Path.Combine(outDir, "impl-scan-SC-6.png"));
        confirm.Close();
        await PumpAsync();
    }

    /// <summary>
    /// ECO-112(VC-IMG-11〜13): ファイル操作モードの R7 並置用撮影。
    /// 原器= image_tab の MENU-fileops / TB-fileops-{none,single,multi} / full-fileops。
    /// FS 軸で ⋯メニュー→モード 0/1/2 件選択を再現する。画像はファイル実体なし
    /// (サムネはプレースホルダー描画=クローム比較が目的・CP-UI-G6 許容=ECO-109 と同じ)。
    /// </summary>
    private static async Task CaptureFileOpsAsync(
        string outDir, DatabaseManager manager, TagRepository tagRepo,
        ViewService viewService, LocalizationService locJa)
    {
        var folders = new SyncFolderRepository(manager);
        var images = new ImageRepository(manager);
        var featureRepo = new ImageFeatureRepository(manager);
        var simRepo = new ImageSimilarityRepository(manager);
        var tab = new ImageTabViewModel(
            folders, images, tagRepo, new ImageSorter(),
            viewService, new NodeGraphBuilder(), new PathConditionConverter(), new ConditionEvaluator(),
            new SimilaritySearchService(folders, images, featureRepo, simRepo, new PHashImageReader(), new SystemClock()),
            new MergeService(images, tagRepo, new MergeRepository(manager)),
            new TrashService(images, folders, new FilePresenceProbe()),
            new StubWindows(), new AppSettings(), new WorkspaceService(new WorkspaceRepository(manager), new SystemClock()),
            locJa);
        await tab.InitializeAsync("col-eco109"); // ECO-109 シードのコレクション(FS 軸既定)を再利用
        tab.SetGridCommand.Execute(null);

        var window = new Window
        {
            Content = new ImageTabView { DataContext = tab },
            Width = 1240,
            Height = 860,
        };
        window.Show();
        await PumpAsync();

        tab.ToggleMoreMenuCommand.Execute(null);
        await PumpAsync();
        Capture(window, Path.Combine(outDir, "impl-fileops-menu.png"));      // 原器 MENU-fileops.png

        tab.EnterFileOpsCommand.Execute(null); // メニューは開始時に閉じる(CAD)
        await PumpAsync();
        Capture(window, Path.Combine(outDir, "impl-fileops-tb-none.png"));   // 原器 TB-fileops-none.png

        var first = tab.Items.First(i => !i.IsFolder);
        tab.HandleItemClick(first, ctrl: false, shift: false);
        await PumpAsync();
        Capture(window, Path.Combine(outDir, "impl-fileops-tb-single.png")); // 原器 TB-fileops-single.png
        Capture(window, Path.Combine(outDir, "impl-fileops-full.png"));      // 原器 full-fileops.png(選択視覚込み全面)

        // コピー完了フィードバック(IMG-026② 裁定=ボタン内一時表示)。mock は挙動未配線で原器なし=
        // golden 判断用の実装面。撮影用に固定表示(SavedToastDuration と同じ様式)
        tab.CopyFeedbackDuration = TimeSpan.FromHours(1);
        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)tab.CopyPathsCommand).ExecuteAsync(null);
        await PumpAsync();
        Capture(window, Path.Combine(outDir, "impl-fileops-tb-copied.png"));

        var second = tab.Items.Where(i => !i.IsFolder).Skip(1).First();
        tab.HandleItemClick(second, ctrl: true, shift: false); // 選択変化=フィードバック解除遷移(multi 面はラベル復帰済み)
        await PumpAsync();
        Capture(window, Path.Combine(outDir, "impl-fileops-tb-multi.png"));  // 原器 TB-fileops-multi.png

        tab.ExitFileOpsCommand.Execute(null);
        window.Close();
        await PumpAsync();
    }

    /// <summary>
    /// ECO-109(VC-FL-1〜4): ファイル一覧 並び替え UI の R7 並置用撮影。
    /// mock 原器(file_list SORT-menu/TB-grid/LIST-sorted/GRID-sorted)の初期状態=
    /// cols[name,date,評価,ガチャ]・評価 降順・grid を再現する。画像はファイル実体なし
    /// (サムネはプレースホルダー描画=クローム比較が目的・CP-UI-G6 許容)。
    /// </summary>
    private static async Task CaptureFileListSortAsync(
        string outDir, DatabaseManager manager, TagRepository tagRepo, TagService tagService,
        ViewService viewService, LocalizationService locJa)
    {
        var rating = (await tagRepo.GetAllAsync()).First(t => t.Name == "評価");
        var gacha = (await tagService.CreateAsync("ガチャ", TagType.Simple, color: "#8b5cf6")).Value!;

        var cols = $$"""
            [{"type":"basic","key":"name","label":"名前","width":2},
             {"type":"basic","key":"modified_date","label":"更新日","width":1},
             {"type":"tag","key":"{{rating.Id}}","label":"評価","width":1},
             {"type":"tag","key":"{{gacha.Id}}","label":"ガチャ","width":1}]
            """;
        var fieldView = (await viewService.CreateAsync("フィールド", displayColumns: cols)).Value!;

        var folders = new SyncFolderRepository(manager);
        var images = new ImageRepository(manager);
        var col = new SyncFolder { Id = "col-eco109", Name = "画像", Path = @"X:\demo" };
        await folders.AddAsync(col);
        // mock 18 件から代表 8 件(評価値の分布+ガチャ有無+未設定行=空値末尾の視覚)
        var seed = new (string Name, string? Rating, bool Gacha)[]
        {
            ("IMG_0847.jpg", "5", true), ("IMG_0851.jpg", "5", false), ("IMG_012.jpg", "4", true),
            ("IMG_033.jpg", "4", false), ("IMG_204.jpg", "3", true), ("IMG_310.jpg", "3", false),
            ("IMG_415.jpg", null, true), ("IMG_502.jpg", null, false),
        };
        var day = 10;
        foreach (var (name, ratingValue, hasGacha) in seed)
        {
            var img = new ImageRecord
            {
                Id = "img-eco109-" + name,
                SyncFolderId = col.Id,
                RelativePath = name,
                FileName = name,
                FileSize = 4_204_019,
                Hash = new string('0', 64),
                Status = ImageStatus.Normal,
                CreatedDate = $"2026-07-{day:00}T00:00:00.000Z",
                ModifiedDate = $"2026-07-{day:00}T00:00:00.000Z",
            };
            day++;
            await images.AddAsync(img);
            if (ratingValue is not null) await tagService.TagImageAsync(img.Id, rating.Id, ratingValue);
            if (hasGacha) await tagService.TagImageAsync(img.Id, gacha.Id, null);
        }

        var featureRepo = new ImageFeatureRepository(manager);
        var simRepo = new ImageSimilarityRepository(manager);
        var tab = new ImageTabViewModel(
            folders, images, tagRepo, new ImageSorter(),
            viewService, new NodeGraphBuilder(), new PathConditionConverter(), new ConditionEvaluator(),
            new SimilaritySearchService(folders, images, featureRepo, simRepo, new PHashImageReader(), new SystemClock()),
            new MergeService(images, tagRepo, new MergeRepository(manager)),
            new TrashService(images, folders, new FilePresenceProbe()),
            new StubWindows(), new AppSettings(), new WorkspaceService(new WorkspaceRepository(manager), new SystemClock()),
            locJa);
        await tab.InitializeAsync(col.Id);
        await tab.SelectAxisCommand.ExecuteAsync(fieldView.Id);
        tab.SetGridCommand.Execute(null);
        tab.SelectColumnSortCommand.Execute(rating.Id); // 昇順
        tab.SelectColumnSortCommand.Execute(rating.Id); // → 降順(mock 初期状態)

        var window = new Window
        {
            Content = new ImageTabView { DataContext = tab },
            Width = 1240,
            Height = 860,
        };
        window.Show();
        await PumpAsync();

        Capture(window, Path.Combine(outDir, "impl-fl-grid-sorted.png"));   // 原器 TB-grid.png / GRID-sorted.png

        tab.ToggleSortMenuCommand.Execute(null);
        await PumpAsync();
        Capture(window, Path.Combine(outDir, "impl-fl-sort-menu.png"));     // 原器 SORT-menu.png
        tab.ToggleSortMenuCommand.Execute(null);
        await PumpAsync();

        tab.SetListCommand.Execute(null);
        await PumpAsync();
        Capture(window, Path.Combine(outDir, "impl-fl-list-sorted.png"));   // 原器 LIST-sorted.png
        window.Close();
        await PumpAsync();
    }

    private static async Task SaveAsync(TagsTabViewModel tab)
    {
        var save = (CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)tab.Editor.SaveCommand;
        await save.ExecuteAsync(null);
    }

    private static async Task PumpAsync()
    {
        for (var i = 0; i < 6; i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(15);
        }
    }

    private static void Capture(TopLevel top, string path)
    {
        using var frame = top.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("no frame: " + path);
        frame.Save(path);
        Console.WriteLine("saved: " + path);
    }

    private sealed class StubWindows : IWindowService
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
}
