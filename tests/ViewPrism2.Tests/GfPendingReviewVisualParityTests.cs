using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using ViewPrism2.App.Services;
using ViewPrism2.App.ViewModels;
using ViewPrism2.App.Views;
using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services.Repair;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// ECO-129: pending 裁定ダイアログ(PendingReviewWindow)の視覚 probe。
/// CAD 正本= ../ViewPrismUI docs/screens/pending_review.md visualContract(PD-2〜4)+
/// E-UI-PENDING-049 invariants。CTA の出し分け(§2.11.7)= 別画像は changed/reappeared/restored のみ・
/// 再リンク導線は candidate つき新規のみ。フッター= CMP-011 dlgBtn 委譲+保留=左端分離。
/// </summary>
[Trait("cp", "CP-UI-G1")]
public sealed class GfPendingReviewVisualParityTests : IDisposable
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

    private async Task<(PendingReviewViewModel Vm, SyncFolder Folder)> BuildVmAsync(
        params (string Path, PendingOrigin? Origin, string? CandidateId)[] pendings)
    {
        var folder = new SyncFolder { Id = "col-1", Name = "fixture", Path = @"C:\Photos" };
        Assert.True((await _db.Folders.AddAsync(folder)).IsSuccess);
        foreach (var (path, origin, candidate) in pendings)
        {
            await _db.Images.AddAsync(new ImageRecord
            {
                Id = IdGenerator.NewId(),
                SyncFolderId = folder.Id,
                RelativePath = path,
                FileName = path[(path.LastIndexOf('/') + 1)..],
                FileSize = 1024,
                Hash = "h-" + path,
                Status = ImageStatus.Pending,
                PendingOrigin = origin,
                CandidateLinkId = candidate,
                CreatedDate = "2026-01-01T00:00:00.000Z",
                ModifiedDate = "2026-01-01T00:00:00.000Z",
            });
        }

        var vm = new PendingReviewViewModel(
            new PendingReviewService(_db.Images), _db.Images, _db.Tags,
            TestLoc.Ja(), new NullWindows(), folder);
        await vm.LoadAsync();
        return (vm, folder);
    }

    private static PendingReviewWindow Show(PendingReviewViewModel vm)
    {
        var window = new PendingReviewWindow { DataContext = vm };
        window.Show();
        RunJobs();
        return window;
    }

    [Fact]
    public async Task PD2_内容変更由来は4操作で受け入れるがprimary_削除がdestructive_保留が左端分離()
    {
        await Session.Dispatch(async () =>
        {
            var (vm, _) = await BuildVmAsync(("a.jpg", PendingOrigin.Changed, null));
            var window = Show(vm);
            try
            {
                Assert.True(vm.ShowTreatAsNew);
                Assert.False(vm.ShowRelink);

                var accept = window.FindControl<Button>("AcceptButton")!;
                Assert.True(accept.IsVisible);
                Assert.Contains("primary", accept.Classes);
                var bg = Assert.IsAssignableFrom<ISolidColorBrush>(accept.Background).Color;
                Assert.Equal(RegistryContract.ColorAccent, bg);

                var buttons = window.GetLogicalDescendants().OfType<Button>()
                    .Where(b => b.Classes.Contains("dlgBtn")).ToList();
                // destructive(削除する)が 1 つ・保留= フッター DockPanel 直下の左端分離
                Assert.Single(buttons, b => b.Classes.Contains("destructive") && b.IsVisible);
                var defer = buttons.Single(b =>
                    b.Parent is DockPanel && DockPanel.GetDock(b) == global::Avalonia.Controls.Dock.Left);
                Assert.True(defer.IsVisible);
                Assert.Contains("secondary", defer.Classes);
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task PD3_候補つき新規は再リンク導線が出て別画像は出ない()
    {
        await Session.Dispatch(async () =>
        {
            var (vm, _) = await BuildVmAsync(("scan.jpg", PendingOrigin.New, "missing-1"));
            var window = Show(vm);
            try
            {
                Assert.False(vm.ShowTreatAsNew); // 新規に「別画像として扱う」は出さない(§2.11.7)
                Assert.True(vm.ShowRelink);
                Assert.True(vm.WhyIsCandidate);  // 候補注記= 青 callout
                Assert.True(window.FindControl<Button>("RelinkButton")!.IsVisible);
                Assert.False(window.FindControl<Button>("TreatAsNewButton")!.IsVisible);
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task 候補なし新規は受け入れる_削除_保留のみ()
    {
        await Session.Dispatch(async () =>
        {
            var (vm, _) = await BuildVmAsync(("scan.jpg", PendingOrigin.New, null));
            var window = Show(vm);
            try
            {
                Assert.False(vm.ShowTreatAsNew);
                Assert.False(vm.ShowRelink);
                Assert.True(window.FindControl<Button>("AcceptButton")!.IsVisible);
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task 未裁定バッジは琥珀破線でプレビューに重畳される()
    {
        await Session.Dispatch(async () =>
        {
            var (vm, _) = await BuildVmAsync(("a.jpg", PendingOrigin.Changed, null));
            var window = Show(vm);
            try
            {
                // REG-C8 族: 破線 Rectangle(3,2)+琥珀ドット+「未裁定」
                var dashed = window.GetLogicalDescendants().OfType<Rectangle>()
                    .Where(r => r.StrokeDashArray is { Count: > 0 }).ToList();
                Assert.NotEmpty(dashed);
                Assert.Contains(window.GetLogicalDescendants().OfType<TextBlock>(),
                    t => t.Text == "未裁定");
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task 全件裁定するとPD4空状態になり閉じるだけが残る()
    {
        await Session.Dispatch(async () =>
        {
            var (vm, folder) = await BuildVmAsync(("a.jpg", PendingOrigin.Changed, null));
            var window = Show(vm);
            try
            {
                await vm.AcceptCommand.ExecuteAsync(null);
                RunJobs();
                Assert.True(vm.IsEmpty);
                Assert.True(vm.Adjudicated);
                Assert.Contains(window.GetLogicalDescendants().OfType<TextBlock>(),
                    t => t.IsVisible && t.Text == "未裁定の画像はありません");
                // 裁定は確定済み= DB は normal 化されている
                var rows = await _db.Images.GetByFolderAsync(folder.Id);
                Assert.Equal(ImageStatus.Normal, Assert.Single(rows).Status);
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    private sealed class NullWindows : IWindowService
    {
        public Task<bool> ConfirmAsync(string title, string message, string confirmLabel,
            bool destructive = false, string? cancelLabel = null) => Task.FromResult(false);

        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);

        public Task ShowFolderManagementAsync() => Task.CompletedTask;

        public Task ShowSettingsAsync() => Task.CompletedTask;

        public Task ShowSnapshotsAsync() => Task.CompletedTask;

        public Task<bool> ShowTagEditorAsync(Tag? existing) => Task.FromResult(false);

        public Task<bool> ShowViewEditDialogAsync(View? existing) => Task.FromResult(false);

        public Task<IReadOnlyList<string>?> ShowNumericValueDialogAsync(
            Tag tag, NumericTagSettings? settings, int imageCount)
            => Task.FromResult<IReadOnlyList<string>?>(null);

        public Task<NodeConditionResult?> ShowNodeConditionDialogAsync(
            Tag tag, HierarchyConditionType? conditionType, string? conditionValueJson)
            => Task.FromResult<NodeConditionResult?>(null);

        public Task ShowRelinkAsync(string folderId) => Task.CompletedTask;

        public void ShowViewer(IReadOnlyList<ImageEntry> ordered, int startIndex)
        {
        }
    }
}
