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
/// ECO-117: ゴミ箱ポップアップ(画像タブ/作業タブ)の本体スクロールは末尾に到達する。
/// 欠陥クラス= ScrollViewer.Padding は Viewport から引かれず Extent とも一致しないため、
/// 内容末尾が Padding.Top のぶん到達不能になる(ECO-116 実測法則。本面は Padding="18" 一律
/// = 18px の到達不能を予測)。検査は ECO-116 と同じ到達性の関係式(内容末尾判定・固定閾値なし)。
/// </summary>
[Trait("cp", "CP-UI-G1")]
public sealed class CpUiG1TrashPopupScrollTests : IDisposable
{
    private const double WindowWidth = 1366;
    private const double WindowHeight = 700;
    private const int DeletedCount = 80; // WrapPanel カード(幅150+余白)が縦に溢れる検体数

    private readonly TempDb _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public Task 画像タブのゴミ箱は最下端まで送ると内容末尾が可視になる() =>
        HeadlessApp.Session.Dispatch(async () =>
        {
            var vm = await NewImageTabVmAsync();
            await vm.OpenTrashCommand.ExecuteAsync(null);
            Assert.True(vm.HasTrashItems && vm.TrashPopupCount == DeletedCount);

            var window = new Window { Content = new ImageTabView { DataContext = vm }, Width = WindowWidth, Height = WindowHeight };
            window.Show();
            RunJobs();
            try
            {
                AssertTrashEndReachable(window, "画像タブ・ゴミ箱");
            }
            finally
            {
                window.Close();
            }
            return true;
        }, CancellationToken.None);

    [Fact]
    public Task 作業タブのゴミ箱は最下端まで送ると内容末尾が可視になる() =>
        HeadlessApp.Session.Dispatch(async () =>
        {
            var vm = await NewWorkTabVmAsync();
            await vm.OpenTrashCommand.ExecuteAsync(null);
            Assert.True(vm.HasTrashItems && vm.TrashPopupCount == DeletedCount);

            var window = new Window { Content = new WorkTabView { DataContext = vm }, Width = WindowWidth, Height = WindowHeight };
            window.Show();
            RunJobs();
            try
            {
                AssertTrashEndReachable(window, "作業タブ・ゴミ箱");
            }
            finally
            {
                window.Close();
            }
            return true;
        }, CancellationToken.None);

    /// <summary>
    /// ゴミ箱ポップアップ本体の ScrollViewer(trashCard を子孫に持つ可視 SV)を特定し、
    /// (a) 有界 (b) 溢れ (c) 最大送り後に内容下端がビューポート内、を検査する(ECO-116 様式)。
    /// </summary>
    private static void AssertTrashEndReachable(Window window, string face)
    {
        var sv = window.GetVisualDescendants().OfType<ScrollViewer>()
            .FirstOrDefault(s => s.IsEffectivelyVisible
                && s.GetVisualDescendants().OfType<Button>().Any(b => b.Classes.Contains("trashCard")));
        Assert.NotNull(sv);

        Assert.True(double.IsFinite(sv!.Viewport.Height) && sv.Viewport.Height > 0,
            $"[{face}] Viewport.Height={sv.Viewport.Height}(有界化されていない)");

        // R7(視覚不変の pin): 是正は余白の持ち方のみ(18 は旧 ScrollViewer.Padding と同値)
        var svRect0 = GlobalRect(sv).Rect;
        var content0 = GlobalRect((Visual)sv.Content!).Rect;
        Assert.True(Math.Abs(content0.X - (svRect0.X + 18)) < 0.5 && Math.Abs(content0.Y - (svRect0.Y + 18)) < 0.5,
            $"[{face}] 内容の内寄せが 18 から変化(content=({content0.X:0.0},{content0.Y:0.0}) sv=({svRect0.X:0.0},{svRect0.Y:0.0}))");

        Assert.True(sv.Extent.Height > sv.Viewport.Height + 0.5,
            $"[{face}] Extent.Height={sv.Extent.Height} <= Viewport.Height={sv.Viewport.Height}"
            + "(溢れていない=検体不足かレイアウト破綻)");

        sv.Offset = new Vector(sv.Offset.X, sv.Extent.Height - sv.Viewport.Height);
        RunJobs();

        var viewport = GlobalRect(sv).Rect;
        var content = GlobalRect((Visual)sv.Content!).Rect;
        Assert.True(content.Bottom <= viewport.Bottom + 0.5,
            $"[{face}] 最大送り後も内容の下端がビューポート外(content.Bottom={content.Bottom:0.0} > "
            + $"viewport.Bottom={viewport.Bottom:0.0}・Extent={sv.Extent.Height:0.0}/Viewport={sv.Viewport.Height:0.0}/"
            + $"Offset={sv.Offset.Y:0.0})— 末尾が表示しきれない(ECO-117)");
    }

    // ---- 検体 ----

    private async Task<ImageTabViewModel> NewImageTabVmAsync()
    {
        var col = new SyncFolder { Id = IdGenerator.NewId(), Name = "C", Path = @"C:\col" };
        await _db.Folders.AddAsync(col);
        await _db.Images.AddAsync(NewImage("img-live", col.Id, ImageStatus.Normal));
        for (var i = 0; i < DeletedCount; i++)
        {
            await _db.Images.AddAsync(NewImage($"del-{i:D3}", col.Id, ImageStatus.Deleted));
        }

        var vm = TestImageTab.NewVm(_db);
        await vm.InitializeAsync(col.Id);
        return vm;
    }

    private async Task<WorkTabViewModel> NewWorkTabVmAsync()
    {
        await _db.Folders.AddAsync(new SyncFolder { Id = "f1", Name = "F", Path = @"C:\f" });
        var ids = new List<string>();
        for (var i = 0; i < DeletedCount; i++)
        {
            var id = $"del-{i:D3}";
            await _db.Images.AddAsync(NewImage(id, "f1", ImageStatus.Deleted));
            ids.Add(id);
        }
        var workspaces = new WorkspaceService(_db.Workspaces, _db.Clock);
        await workspaces.AddImagesToDefaultAsync(ids);

        var vm = new WorkTabViewModel(
            workspaces, _db.Folders, _db.Tags,
            new SimilaritySearchService(_db.Folders, _db.Images, _db.Features, _db.Similarities, new FakePHashImageReader(), _db.Clock),
            new MergeService(_db.Images, _db.Tags, _db.Merges),
            new TrashService(_db.Images, _db.Folders, new FilePresenceProbe()),
            new StubWindows(), new ImageSorter(), new AppSettings(), TestLoc.Ja());
        await vm.InitializeAsync();
        return vm;
    }

    private static ImageRecord NewImage(string id, string folderId, ImageStatus status) => new()
    {
        Id = id, SyncFolderId = folderId, RelativePath = $"{id}.jpg", FileName = $"{id}.jpg",
        FileSize = 10, Hash = new string('0', 64), Status = status,
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
        public Task<bool> ConfirmAsync(string title, string message, string confirmLabel, bool destructive = false, string? cancelLabel = null) => Task.FromResult(false);
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
