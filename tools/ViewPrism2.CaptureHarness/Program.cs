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
        var dbDir = Path.Combine(outDir, "db-work");
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
}
