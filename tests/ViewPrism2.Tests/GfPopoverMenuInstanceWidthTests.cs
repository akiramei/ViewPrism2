using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using ViewPrism2.App.Services;
using ViewPrism2.App.ViewModels;
using ViewPrism2.App.Views;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using ViewPrism2.Core.Services.Repair;
using ViewPrism2.Core.Services.Similarity;
using ViewPrism2.Infrastructure.Imaging;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// ECO-123: CMP-006 PopoverMenu(部品表 04_component_registry・REG-C3 裁定 2026-07-20 で Standard 昇格)の
/// インスタンス契約幅の pin。契約値=表示軸 240/⋯ 208/並び替え 252/移動先 240・「同型インスタンスの
/// 面間複製は同値必須」。是正前赤の 2 サイト(転写ドリフト)を対象:
/// ①作業タブ ⋯メニュー 200(混入 f211fa9=複製時・契約 208=画像タブと同値必須)
/// ②画像タブ 表示軸メニュー 260(混入 45a6c77=M3b 製造時・mock 実測 240・260 は mock に不在)。
/// 並び替え 252 は GfSortMenuVisualParityTests・⋯画像タブ 208 は GfFileOpsVisualParityTests が既 pin。
/// 移動先 240(契約適合・是正なし)も本クラスで pin し契約 4 値の全数を閉じる(R8 所見5)。
/// 期待値定数は ECO-122(部品表 適合性検査の配線)で RegistryContract 写像参照へ置換予定。
/// </summary>
[Trait("cp", "CP-UI-G1")]
public sealed class GfPopoverMenuInstanceWidthTests : IDisposable
{
    private static HeadlessUnitTestSession Session => HeadlessApp.Session;

    private readonly TempDb _db = new();

    public void Dispose() => _db.Dispose();

    private static void RunJobs()
    {
        for (var i = 0; i < 8; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>開いている Popup の本体 Border を返す(単一メニューのみ開いた状態で呼ぶこと)。</summary>
    private static Border OpenMenuPanel(Window window)
    {
        var open = window.GetLogicalDescendants().OfType<Popup>().Where(p => p.IsOpen).ToList();
        Assert.True(open.Count == 1, $"開いている Popup が {open.Count} 個(期待 1)");
        var panel = open[0].Child as Border;
        Assert.True(panel is not null && panel.Classes.Contains("popupMenu"), "開いた Popup の本体が popupMenu Border でない");
        return panel!;
    }

    [Fact]
    public async Task CMP006_画像タブの表示軸メニューは契約幅240である()
    {
        var vm = TestImageTab.NewVm(_db);
        await Session.Dispatch(() =>
        {
            var window = new Window { Content = new ImageTabView { DataContext = vm }, Width = 1200, Height = 800 };
            window.Show();
            RunJobs();
            try
            {
                vm.ToggleAxisMenuCommand.Execute(null);
                RunJobs();
                var menu = OpenMenuPanel(window);
                // CMP-006 インスタンス契約: 表示軸メニュー=幅 240(mock 実測。旧 260 は mock に不在の転写ドリフト)
                Assert.True(menu.Width == 240, $"CMP-006: 表示軸メニュー幅が {menu.Width}(契約 240)");
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CMP006_作業タブの三点リーダーメニューは契約幅208である()
    {
        var vm = new WorkTabViewModel(
            new WorkspaceService(_db.Workspaces, _db.Clock), _db.Folders, _db.Tags,
            new SimilaritySearchService(_db.Folders, _db.Images, _db.Features, _db.Similarities, new FakePHashImageReader(), _db.Clock),
            new MergeService(_db.Images, _db.Tags, _db.Merges),
            new TrashService(_db.Images, _db.Folders, new FilePresenceProbe()),
            new StubWindowService(), new ImageSorter(), new AppSettings(),
            TestLoc.Ja());
        await Session.Dispatch(() =>
        {
            var window = new Window { Content = new WorkTabView { DataContext = vm }, Width = 1200, Height = 800 };
            window.Show();
            RunJobs();
            try
            {
                vm.ToggleMoreMenuCommand.Execute(null);
                RunJobs();
                var menu = OpenMenuPanel(window);
                // CMP-006 インスタンス契約: ⋯メニュー=幅 208(同型インスタンスの面間複製は同値必須=画像タブと同値)
                Assert.True(menu.Width == 208, $"CMP-006: 作業タブ ⋯メニュー幅が {menu.Width}(契約 208)");
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CMP006_作業タブの移動先メニューは契約幅240である()
    {
        var vm = new WorkTabViewModel(
            new WorkspaceService(_db.Workspaces, _db.Clock), _db.Folders, _db.Tags,
            new SimilaritySearchService(_db.Folders, _db.Images, _db.Features, _db.Similarities, new FakePHashImageReader(), _db.Clock),
            new MergeService(_db.Images, _db.Tags, _db.Merges),
            new TrashService(_db.Images, _db.Folders, new FilePresenceProbe()),
            new StubWindowService(), new ImageSorter(), new AppSettings(),
            TestLoc.Ja());
        await Session.Dispatch(() =>
        {
            var window = new Window { Content = new WorkTabView { DataContext = vm }, Width = 1200, Height = 800 };
            window.Show();
            RunJobs();
            try
            {
                vm.MoveMenuOpen = true;
                RunJobs();
                var menu = OpenMenuPanel(window);
                // CMP-006 インスタンス契約: 移動先メニュー=幅 240(契約適合の pin=是正なし。契約 4 値の全数 pin を閉じる)
                Assert.True(menu.Width == 240, $"CMP-006: 移動先メニュー幅が {menu.Width}(契約 240)");
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    // 既存流儀(各テストクラス私有の最小スタブ)どおり。ECO-122 で共有化候補になり得るが本 ECO では複製維持
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
}
