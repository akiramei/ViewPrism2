using System.Collections.Specialized;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
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
using ViewPrism2.Infrastructure.Imaging;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// ECO-056: 整理トレイの v2 CAD 追随(3 ゾーン+マージ先解除)+v1 以来の導線欠落の是正。
/// プローブ(R5): ①マージ先を解除する導線が存在しない(CAD v2=clearDest・A-2 裁定済み)
/// ②検索結果からグリッドへ戻る導線が存在しない(CAD v1 から backToGrid「グリッドへ」— 混入 51ad8ee)。
/// コマンドはリフレクションで解決する= 是正前は「導線の不在」そのものが実行時不合格になる
/// (最終形 API で直接書くと是正前はコンパイル不能となり R5 の実測が撮れないため)。
/// </summary>
[Trait("cp", "CP-UI-G1")]
public sealed class CpUi056OrganizeV2Tests : IDisposable
{
    private readonly TempDb _db = new();
    private SyncFolder _col = null!;
    private readonly Dictionary<string, string> _idByName = new(StringComparer.Ordinal);

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task マージ先を解除でき整理対象と実行不活性はCADどおり()
    {
        // CAD v2(image_tab.md L353): 解除=「整理対象は保持し、マージ先のみ未設定へ戻る(実行は不活性化)」
        var vm = await NewVmAsync(
            ("keep.jpg", "H1", 100, "2026-06-11T00:00:00.000Z"),
            ("dup1.jpg", "H2", 200, "2026-06-11T00:00:00.000Z"),
            ("dup2.jpg", "H3", 300, "2026-06-11T00:00:00.000Z"));
        vm.ToggleOrganizeCommand.Execute(null);
        vm.HandleItemClick(Item(vm, "keep.jpg"), false, false);  // マージ先
        vm.HandleItemClick(Item(vm, "dup1.jpg"), false, false);  // 整理対象
        vm.HandleItemClick(Item(vm, "dup2.jpg"), false, false);
        Assert.True(vm.HasMergeTarget);
        Assert.True(vm.CanExecuteMerge);

        var cmd = ResolveCommand(vm, "ClearMergeTargetCommand"); // 是正前: 不在で不合格(解除導線なし)

        var raised = false;
        vm.PropertyChanged += (_, _) => raised = true;
        cmd.Execute(null);

        Assert.False(vm.HasMergeTarget);           // マージ先のみ未設定へ
        Assert.True(vm.HasOrganizeTargets);        // 整理対象は保持
        Assert.Equal("2 枚", vm.OrganizeTargetsCountLabel);
        Assert.False(vm.CanExecuteMerge);          // 実行は不活性化
        Assert.False(vm.CanRunSearch);             // 検索もマージ先必須(ECO-055 裁定③)
        Assert.True(raised, "解除がホストの PropertyChanged を発火していない(GF-055-01 様式の先回り封止)");
    }

    [Fact]
    public async Task 検索結果からグリッドへ戻れて結果は保持される()
    {
        // CAD(v1 から): 検索結果ヘッダ「グリッドへ」(backToGrid)。モック実測= view のみ切替・results 保持。
        // 是正前: ShowSearchResults を戻す経路が存在せず、グリッドへ戻る手段は整理終了のみ。
        var vm = await NewVmAsync(
            ("keep.jpg", "H1", 100, "2026-06-11T00:00:00.000Z"),
            ("samehash.jpg", "H1", 200, "2026-06-12T00:00:00.000Z"));
        vm.ToggleOrganizeCommand.Execute(null);
        vm.HandleItemClick(Item(vm, "keep.jpg"), false, false);
        vm.SetSearchMethodCommand.Execute("criteria");
        vm.CondHash = true;
        await vm.RunSearchCommand.ExecuteAsync(null);
        Assert.True(vm.ShowSearchResults);
        Assert.Single(vm.SearchResults);

        var cmd = ResolveCommand(vm, "BackToGridCommand"); // 是正前: 不在で不合格(戻る導線なし)
        cmd.Execute(null);

        Assert.False(vm.ShowSearchResults);  // グリッドへ戻る(整理モードは維持)
        Assert.True(vm.OrganizeMode);
        Assert.Single(vm.SearchResults);     // 結果は保持(モック: view 切替のみ・再検索まで不変)
    }

    [Fact]
    public async Task 作業タブでもマージ先を解除できる()
    {
        // 転写ドリフト封止(ECO-050/055 教訓): トレイの構造・操作は両タブ同一部品(work_tab.md L95)
        var vm = await NewWorkVmAsync(("w-keep.jpg", "H1"), ("w-dup.jpg", "H2"));
        vm.ToggleOrganizeCommand.Execute(null);
        vm.HandleItemClick(vm.Items.Single(i => i.Name == "w-keep.jpg"), ctrl: false, shift: false);
        vm.HandleItemClick(vm.Items.Single(i => i.Name == "w-dup.jpg"), ctrl: false, shift: false);
        Assert.True(vm.HasMergeTarget);

        var cmd = ResolveCommand(vm, "ClearMergeTargetCommand"); // 是正前: 不在で不合格
        cmd.Execute(null);

        Assert.False(vm.HasMergeTarget);
        Assert.True(vm.HasOrganizeTargets);
        Assert.False(vm.CanExecuteMerge);
    }

    [Fact]
    public async Task 検索パネルはタブ切替で高さが変わらない()
    {
        // GF-056-01(golden 所見 2026-07-07): 「条件検索」「類似画像検索」の切替でパネル高さが変わる。
        // CAD(v1/v2 モック)は切替コンテンツを height:150px の固定コンテナに収める(転写漏れ)。
        // Avalonia.Headless の実レイアウトパスで searchPanel の Bounds.Height を両タブで実測する。
        var vm = await NewVmAsync(("a.jpg", "H1", 10, "2026-06-11T00:00:00.000Z"));
        vm.ToggleOrganizeCommand.Execute(null);
        vm.ToggleSearchOpenCommand.Execute(null); // 検索パネルを開く
        Assert.True(vm.SearchOpen);

        await HeadlessApp.Session.Dispatch(() =>
        {
            var window = new Window { Content = new ImageTabView { DataContext = vm }, Width = 1366, Height = 900 };
            window.Show();
            RunJobs();

            var panel = window.GetVisualDescendants().OfType<Border>()
                .FirstOrDefault(b => b.Classes.Contains("searchPanel"));
            Assert.NotNull(panel);

            vm.SetSearchMethodCommand.Execute("criteria");
            RunJobs();
            double condHeight = panel!.Bounds.Height;

            vm.SetSearchMethodCommand.Execute("similar");
            RunJobs();
            double similarHeight = panel.Bounds.Height;

            Assert.True(Math.Abs(condHeight - similarHeight) <= 0.5,
                $"タブ切替で検索パネルの高さが変わる(GF-056-01): 条件={condHeight:0.0} / 類似={similarHeight:0.0}");

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task 検索方式のタブ切替はグリッドItemsを再構築しない()
    {
        // GF-056-02 ②(golden 所見 2026-07-07): タブ切替で画像一覧がちらつく。
        // 真因= ホスト SetSearchMethod が Recompute()= Items 全再構築(Clear+Add)を呼ぶ(51ad8ee 以来)。
        // 検索方式はグリッド内容と無関係= CollectionChanged が一切発火しないことを pin する。
        var vm = await NewVmAsync(("a.jpg", "H1", 10, "2026-06-11T00:00:00.000Z"));
        vm.ToggleOrganizeCommand.Execute(null);

        int collectionChanges = 0;
        ((INotifyCollectionChanged)vm.Items).CollectionChanged += (_, _) => collectionChanges++;
        vm.SetSearchMethodCommand.Execute("criteria");
        vm.SetSearchMethodCommand.Execute("similar");

        Assert.True(collectionChanges == 0,
            $"タブ切替で Items が {collectionChanges} 回変更された= グリッド再構築のちらつき(GF-056-02)");
    }

    [Fact]
    public async Task 条件トグルON時もラベルが白文字にならない()
    {
        // GF-056-02 ①(golden 所見 2026-07-07): Fluent の ToggleButton:checked がテンプレート内
        // Foreground を白にし、淡青ハイライト上で白ラベル= 視認性が極めて悪い。
        // headless 実レイアウトで checked 行のラベル実効 Foreground を実測する。
        var vm = await NewVmAsync(("a.jpg", "H1", 10, "2026-06-11T00:00:00.000Z"));
        vm.ToggleOrganizeCommand.Execute(null);
        vm.ToggleSearchOpenCommand.Execute(null);
        vm.SetSearchMethodCommand.Execute("criteria");
        vm.CondHash = true;

        await HeadlessApp.Session.Dispatch(() =>
        {
            var window = new Window { Content = new ImageTabView { DataContext = vm }, Width = 1366, Height = 900 };
            window.Show();
            RunJobs();

            var row = window.GetVisualDescendants().OfType<ToggleButton>()
                .FirstOrDefault(t => t.Classes.Contains("condRow") && t.IsChecked == true);
            Assert.NotNull(row);
            var label = row!.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Text == "ハッシュ値");
            Assert.NotNull(label);
            var brush = Assert.IsAssignableFrom<ISolidColorBrush>(label!.Foreground);
            bool whitish = brush.Color.R > 200 && brush.Color.G > 200 && brush.Color.B > 200;
            Assert.True(!whitish,
                $"checked 条件行のラベルが白系({brush.Color})= 淡青ハイライト上で視認不能(GF-056-02)");

            window.Close();
        }, CancellationToken.None);
    }

    // ---- ヘルパ ----

    private static void RunJobs()
    {
        for (var i = 0; i < 8; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static ICommand ResolveCommand(object vm, string name)
    {
        var prop = vm.GetType().GetProperty(name);
        Assert.True(prop is not null, $"{name} が存在しない(導線の不在= ECO-056 プローブ)");
        return (ICommand)prop!.GetValue(vm)!;
    }

    private async Task<ImageTabViewModel> NewVmAsync(
        params (string Name, string Hash, long Size, string Modified)[] images)
    {
        _col = new SyncFolder { Id = IdGenerator.NewId(), Name = "C", Path = @"C:\col-056" };
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
            new StubWindowService(), new AppSettings(), new WorkspaceService(_db.Workspaces, _db.Clock), TestLoc.Empty());
        await vm.InitializeAsync(_col.Id);
        return vm;
    }

    private async Task<WorkTabViewModel> NewWorkVmAsync(params (string Name, string Hash)[] images)
    {
        await _db.Folders.AddAsync(new SyncFolder { Id = "f-w", Name = "F", Path = @"C:\w-056" });
        var workspaces = new WorkspaceService(_db.Workspaces, _db.Clock);
        foreach (var (name, hash) in images)
        {
            var id = IdGenerator.NewId();
            _idByName[name] = id;
            await _db.Images.AddAsync(new ImageRecord
            {
                Id = id, SyncFolderId = "f-w", RelativePath = name, FileName = name,
                FileSize = 1, Hash = hash, Status = ImageStatus.Normal,
                CreatedDate = "2026-06-11T00:00:00.000Z", ModifiedDate = "2026-06-11T00:00:00.000Z",
            });
            await workspaces.AddImagesToDefaultAsync(new[] { id });
        }
        var vm = new WorkTabViewModel(
            workspaces, _db.Folders, _db.Tags,
            new SimilaritySearchService(_db.Folders, _db.Images, _db.Features, _db.Similarities, new FakePHashImageReader(), _db.Clock),
            new MergeService(_db.Images, _db.Tags, _db.Merges),
            new TrashService(_db.Images, _db.Folders, new FilePresenceProbe()),
            new StubWindowService(), new ImageSorter(), new AppSettings());
        await vm.InitializeAsync();
        return vm;
    }

    private ImageItemVM Item(ImageTabViewModel vm, string name)
        => vm.Items.Single(i => !i.IsFolder && i.Name == name);

    private sealed class StubWindowService : IWindowService
    {
        public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(true);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
        public Task ShowFolderManagementAsync() => Task.CompletedTask;
        public Task ShowSettingsAsync() => Task.CompletedTask;

        public Task ShowSnapshotsAsync() => Task.CompletedTask;
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
