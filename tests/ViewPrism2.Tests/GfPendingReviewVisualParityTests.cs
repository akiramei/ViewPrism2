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
/// ECO-129/139: pending 裁定ダイアログ(PendingReviewWindow)の視覚 probe。
/// CAD 正本= ../ViewPrismUI docs/screens/pending_review.md visualContract(PD-2〜6)+
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

    [Fact]
    public async Task 裁定後の一覧は該当行を残さない_複数件で1件裁定()
    {
        // ECO-132(Codex P1① の R5 反証ガード・won't-fix): Codex は「Items が plain List だと
        // Remove+同一インスタンス通知で ItemsControl が削除行を保持し得る」(WPF 挙動)と指摘したが、
        // Avalonia 12.0.4 は本経路でも表示を正しく更新する=症状は再現しない(headless 実 UI で実測)。
        // 本テストは正しい挙動(裁定後に該当行が UI から消える)の回帰ガードとして残す。
        await Session.Dispatch(async () =>
        {
            var (vm, _) = await BuildVmAsync(
                ("a.jpg", PendingOrigin.Changed, null),
                ("b.jpg", PendingOrigin.New, null));
            var window = Show(vm);
            try
            {
                var list = window.GetLogicalDescendants().OfType<ItemsControl>()
                    .First(ic => ReferenceEquals(ic.ItemsSource, vm.Items));

                // 実 UI に表示されている行= DataContext が PendingItemVM の実現コンテナのファイル名で数える
                IReadOnlyList<string> DisplayedNames() => list.GetRealizedContainers()
                    .Select(c => (c.DataContext as PendingItemVM)?.FileName)
                    .Where(n => n is not null).Select(n => n!).OrderBy(n => n, StringComparer.Ordinal).ToList();

                Assert.Equal(["a.jpg", "b.jpg"], DisplayedNames()); // 初期 2 行

                var accepted = vm.Items[0]; // a.jpg
                vm.SelectCommand.Execute(accepted);
                RunJobs();
                await vm.AcceptCommand.ExecuteAsync(null); // 受け入れる= Items から 1 件 Remove
                RunJobs();

                Assert.Single(vm.Items);                        // VM 側は 1 件
                Assert.Equal(["b.jpg"], DisplayedNames());      // UI も 1 行(裁定済み a.jpg の stale 行を残さない)
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    [Trait("cp", "CP-PENDING-AUTO-035")]
    public async Task PD5_高信頼ありはfullWidthCalloutと先頭グループと自動チップを表示()
    {
        await Session.Dispatch(async () =>
        {
            var (vm, _) = await BuildVmAsync(
                ("z-auto.jpg", PendingOrigin.New, "missing-1"),
                ("a-changed.jpg", PendingOrigin.Changed, null));
            var window = Show(vm);
            try
            {
                var callout = window.FindControl<Border>("AutoAdjudicateCallout")!;
                Assert.True(callout.IsVisible);
                Assert.Equal(2, Grid.GetColumnSpan(callout));
                Assert.Equal(window.ClientSize.Width, callout.Bounds.Width, precision: 1);
                Assert.Equal(1, vm.HighConfidenceCount);

                var autoButton = window.FindControl<Button>("AutoAdjudicateButton")!;
                Assert.True(autoButton.IsVisible);
                Assert.Contains("primary", autoButton.Classes);
                Assert.Contains("1 件を受け入れる", autoButton.Content as string);

                Assert.Contains(window.GetLogicalDescendants().OfType<TextBlock>(),
                    t => t.IsVisible && t.Text == "自動裁定できる 1 件(ハッシュ一致)");
                Assert.Contains(window.GetLogicalDescendants().OfType<TextBlock>(),
                    t => t.IsVisible && t.Text == "個別に確認 1 件");
                Assert.Contains(window.GetLogicalDescendants().OfType<TextBlock>(),
                    t => t.IsVisible && t.Text == "自動");
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    [Trait("cp", "CP-PENDING-AUTO-035")]
    public async Task PD5_高信頼0件はcalloutと対象グループを表示しない()
    {
        await Session.Dispatch(async () =>
        {
            var (vm, _) = await BuildVmAsync(("a-new.jpg", PendingOrigin.New, null));
            var window = Show(vm);
            try
            {
                Assert.False(vm.HasHighConfidence);
                Assert.False(window.FindControl<Border>("AutoAdjudicateCallout")!.IsVisible);
                Assert.False(window.FindControl<StackPanel>("AutoAdjudicateGroupHeader")!.IsVisible);
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
