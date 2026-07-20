using Avalonia;
using Avalonia.Controls;
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
/// ECO-116: タグ編集パネルの本体スクロールは末尾に到達する
/// (CAD image_tab.md 行 101/103/107 = ペインごと単一スクロール本体 `overflow:auto`・
/// 右ペインはヘッダ固定/本体スクロール。末尾到達性は overflow:auto の定義に含む)。
///
/// 検査は固定ピクセル閾値ではなく<b>到達性の関係式</b>で書く(ECO-058 様式):
/// Offset を最大まで送ったとき、最終要素の下端がビューポート矩形の内側に入ること。
/// 先例= GfViewerDrawerScrollTests(GF-TAGCTRL-01・非有界コンテナで ScrollViewer が
/// スクロールしない同クラスの潜在レイアウトバグ)。
/// </summary>
[Trait("cp", "CP-UI-G1")]
public sealed class CpUiG1TagPanelScrollTests : IDisposable
{
    private const double WindowWidth = 1366;
    private const double WindowHeight = 700; // 症状観測時と同程度の縦幅(パネル本体が溢れる)

    private readonly TempDb _db = new();

    public void Dispose() => _db.Dispose();

    // 是正した 4 サーフェス(2 面 × 2 タブ)をそれぞれ張る。タブ別に検体の溢れ方が違うため
    // (タグ追加=行リスト / 現在のタグ=ピルの WrapPanel)、片方だけでは他方の再発を捕まえない。

    [Fact]
    public Task 画像タブのタグ追加タブは最下端まで送ると内容末尾が可視になる() =>
        AssertFaceAsync("画像タブ・タグ追加", topInset: 14, onAddTab: true);

    [Fact]
    public Task 画像タブの現在のタグタブは最下端まで送ると内容末尾が可視になる() =>
        AssertFaceAsync("画像タブ・現在のタグ", topInset: 16, onAddTab: false);

    [Fact]
    public Task 作業タブのタグ追加タブは最下端まで送ると内容末尾が可視になる() =>
        AssertFaceAsync("作業タブ・タグ追加", topInset: 14, onAddTab: true, workTab: true);

    [Fact]
    public Task 作業タブの現在のタグタブは最下端まで送ると内容末尾が可視になる() =>
        AssertFaceAsync("作業タブ・現在のタグ", topInset: 16, onAddTab: false, workTab: true);

    private Task AssertFaceAsync(string face, double topInset, bool onAddTab, bool workTab = false) =>
        HeadlessApp.Session.Dispatch(async () =>
        {
            Control view = workTab
                ? new WorkTabView { DataContext = await NewWorkTabVmAsync(onAddTab) }
                : new ImageTabView { DataContext = await NewImageTabVmAsync(onAddTab) };
            var window = new Window { Content = view, Width = WindowWidth, Height = WindowHeight };
            window.Show();
            RunJobs();
            try
            {
                AssertContentEndReachable(window, face, topInset);
            }
            finally
            {
                window.Close(); // 失敗時も窓を残さない(セッションはプロセス共有=PerAssembly)
            }
            return true;
        }, CancellationToken.None);

    /// <summary>
    /// 到達性の関係式: 可視パネル本体の ScrollViewer が (a) 有界 (b) スクロール域を持ち
    /// (c) 最大送り後に<b>内容そのものの下端</b>がビューポート内へ入る。
    /// (c) はタブ種別に依存しない(行/ピルのクラス名を使わない)ため 4 サーフェス共通で張れる。
    /// </summary>
    private static void AssertContentEndReachable(Window window, string face, double topInset)
    {
        var sv = VisiblePanelScrollViewer(window);
        Assert.True(double.IsFinite(sv.Viewport.Height) && sv.Viewport.Height > 0,
            $"[{face}] Viewport.Height={sv.Viewport.Height}(有界化されていない)");

        // R7(視覚不変の pin): 是正は「余白の持ち方」だけを変える。内容の内寄せ量は不変
        // (左 16 / 上 = 旧 ScrollViewer.Padding と同値)。ここが動くと golden の見た目が変わる。
        var svRect0 = GlobalRect(sv).Rect;
        var content = GlobalRect((Visual)sv.Content!).Rect;
        Assert.True(Math.Abs(content.X - (svRect0.X + 16)) < 0.5,
            $"[{face}] 内容の左インセットが 16 から変化(content.X={content.X:0.0} sv.X={svRect0.X:0.0})");
        Assert.True(Math.Abs(content.Y - (svRect0.Y + topInset)) < 0.5,
            $"[{face}] 内容の上インセットが {topInset} から変化(content.Y={content.Y:0.0} sv.Y={svRect0.Y:0.0})");
        Assert.True(sv.Extent.Height > sv.Viewport.Height + 0.5,
            $"[{face}] Extent.Height={sv.Extent.Height} <= Viewport.Height={sv.Viewport.Height}"
            + "(溢れていない=検体不足かレイアウト破綻)");

        // 最下端まで送る(ユーザーがスクロールバーを最後まで動かした状態)
        sv.Offset = new Vector(sv.Offset.X, sv.Extent.Height - sv.Viewport.Height);
        RunJobs();

        var viewport = GlobalRect(sv).Rect;
        var last = GlobalRect((Visual)sv.Content!).Rect;
        Assert.True(last.Bottom <= viewport.Bottom + 0.5,
            $"[{face}] 最大送り後も内容の下端がビューポート外(content.Bottom={last.Bottom:0.0} > "
            + $"viewport.Bottom={viewport.Bottom:0.0}・Extent={sv.Extent.Height:0.0}/Viewport={sv.Viewport.Height:0.0}/"
            + $"Offset={sv.Offset.Y:0.0})— 末尾が表示しきれない(ECO-116)");
    }

    /// <summary>編集パネル(幅 344)内で実際に見えているパネル本体の ScrollViewer。</summary>
    private static ScrollViewer VisiblePanelScrollViewer(Window window)
    {
        var panel = window.GetVisualDescendants().OfType<Border>()
            .FirstOrDefault(b => b.Classes.Contains("editPanel") && b.IsEffectivelyVisible);
        Assert.NotNull(panel);

        var sv = panel!.GetVisualDescendants().OfType<ScrollViewer>()
            .FirstOrDefault(s => s.IsEffectivelyVisible);
        Assert.NotNull(sv);
        return sv!;
    }

    // ---- 検体(溢れるだけのタグ) ----

    // 現在のタグ(ピルの WrapPanel・1 行に複数個)側も溢れる必要があるため多めに張る。
    private const int SimpleTagCount = 60;

    private async Task SeedManyTagsAsync()
    {
        for (var i = 0; i < SimpleTagCount; i++)
        {
            await _db.Tags.AddAsync(new Tag
            {
                Id = $"t-s{i:00}", Name = $"シンプル{i:00}", Type = TagType.Simple, Color = "#8b5cf6",
            });
        }
        await _db.Tags.AddAsync(new Tag { Id = "t-text", Name = "地域", Type = TagType.Textual, Color = "#e5484d" });
        await _db.Tags.AddAsync(new Tag { Id = "t-num", Name = "評価", Type = TagType.Numeric, Color = "#12a594" });
    }

    private async Task<ImageTabViewModel> NewImageTabVmAsync(bool onAddTab)
    {
        await SeedManyTagsAsync();
        var col = new SyncFolder { Id = IdGenerator.NewId(), Name = "C", Path = @"C:\col" };
        await _db.Folders.AddAsync(col);
        await _db.Images.AddAsync(NewImage("img-a", col.Id));
        await AttachAllSimpleTagsAsync("img-a"); // 現在のタグ側もピルで溢れさせる

        var vm = TestImageTab.NewVm(_db);
        await vm.InitializeAsync(col.Id);
        vm.ToggleEditCommand.Execute(null);
        vm.HandleItemClick(vm.Items.Single(i => !i.IsFolder), false, false);
        if (onAddTab)
        {
            vm.TabAddCommand.Execute(null);
        }
        Assert.True(vm.PanelActive && vm.OnAddTab == onAddTab);
        return vm;
    }

    private async Task<WorkTabViewModel> NewWorkTabVmAsync(bool onAddTab)
    {
        await SeedManyTagsAsync();
        await _db.Folders.AddAsync(new SyncFolder { Id = "f1", Name = "F", Path = @"C:\f" });
        await _db.Images.AddAsync(NewImage("img-a", "f1"));
        await AttachAllSimpleTagsAsync("img-a");

        var workspaces = new WorkspaceService(_db.Workspaces, _db.Clock);
        await workspaces.AddImagesToDefaultAsync(new[] { "img-a" });

        var vm = new WorkTabViewModel(
            workspaces, _db.Folders, _db.Tags,
            new SimilaritySearchService(_db.Folders, _db.Images, _db.Features, _db.Similarities, new FakePHashImageReader(), _db.Clock),
            new MergeService(_db.Images, _db.Tags, _db.Merges),
            new TrashService(_db.Images, _db.Folders, new FilePresenceProbe()),
            new StubWindows(), new ImageSorter(), new AppSettings(), TestLoc.Ja());
        await vm.InitializeAsync();
        vm.ToggleEditCommand.Execute(null);
        vm.HandleItemClick(vm.Items[0], false, false);
        if (onAddTab)
        {
            vm.TabAddCommand.Execute(null);
        }
        Assert.True(vm.PanelActive && vm.OnAddTab == onAddTab);
        return vm;
    }

    /// <summary>シンプルタグ全数を 1 枚へ付与(現在のタグ=ピル行を溢れさせる検体)。</summary>
    private async Task AttachAllSimpleTagsAsync(string imageId)
    {
        for (var i = 0; i < SimpleTagCount; i++)
        {
            await _db.Tags.TagImagesAsync([imageId], $"t-s{i:00}", null);
        }
    }

    private static ImageRecord NewImage(string id, string folderId) => new()
    {
        Id = id, SyncFolderId = folderId, RelativePath = $"{id}.jpg", FileName = $"{id}.jpg",
        FileSize = 10, Hash = new string('0', 64), Status = ImageStatus.Normal,
        CreatedDate = "2026-06-11T00:00:00.000Z", ModifiedDate = "2026-06-11T00:00:00.000Z",
    };

    // ---- ヘルパ ----

    private static void RunJobs()
    {
        for (var i = 0; i < 8; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static (Rect Rect, Rect Clip) GlobalRect(Visual v)
    {
        var tb = v.GetTransformedBounds()!.Value;
        return (tb.Bounds.TransformToAABB(tb.Transform), tb.Clip);
    }

    private sealed class StubWindows : IWindowService
    {
        public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(false);
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
        public Task ShowSimilarSearchAsync(ImageEntry baseImage, IReadOnlyList<ImageEntry> collectionEntries) => Task.CompletedTask;
        public Task<bool> ShowMergeAsync(ImageEntry target, IReadOnlyList<ImageEntry> sources) => Task.FromResult(false);
        public Task ShowTrashAsync(string collectionId) => Task.CompletedTask;
    }
}
