using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using ViewPrism2.App.Services;
using ViewPrism2.App.ViewModels;
using ViewPrism2.App.Views;
using ViewPrism2.Core.Models;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// ECO-130: スキャン結果確認(ScanSummaryWindow)の視覚 probe。
/// CAD 正本= ../ViewPrismUI docs/screens/scan_summary.md visualContract(SC-2〜4)+
/// E-UI-SCANSTAGE-048 invariants。CMP-011(dlgBtn 委譲)の色は RegistryContract 参照・
/// 面固有の callout/行強調色は CAD の面契約値を pin する。
/// ステージングは PresentSummary へ直接注入(差分計算は走らせない=表示契約のみ検査)。
/// </summary>
[Trait("cp", "CP-UI-G1")]
public sealed class GfScanSummaryVisualParityTests
{
    private static HeadlessUnitTestSession Session => HeadlessApp.Session;

    // 面契約値(scan_summary.md visualContract。Standard 部品でない面固有値= CAD が正)
    private static readonly Color CalloutGreenBg = Color.Parse("#EAFAF0");
    private static readonly Color CalloutYellowBg = Color.Parse("#FDF8EF");
    private static readonly Color CalloutRedBg = Color.Parse("#FBECED");
    private static readonly Color TotalRowBg = Color.Parse("#F7F9FC");

    private static void RunJobs()
    {
        for (var i = 0; i < 8; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static ScanStaging Staging(
        int missing, int managed, int contentChanged = 0,
        int addedPending = 0, int missingFromPending = 0, int readFailures = 0)
        => new()
        {
            FolderId = "folder-1",
            ManagedTotal = managed,
            ScannedFiles = managed - missing,
            Unchanged = managed - missing - contentChanged,
            ContentChanged = contentChanged,
            AddedPending = addedPending,
            Reappeared = 0,
            MissingFromNormal = missing,
            MissingFromPending = missingFromPending,
            PreexistingMissing = 0,
            DeletedUnchanged = 0,
            DeletedMetaRefreshed = 0,
            PendedWithoutMeta = 0,
            ReadFailures = readFailures,
            Adds = [],
            MetaUpdates = [],
            StatusUpdates = [],
            Deletes = [],
            Examples = [],
        };

    private static (ScanSummaryWindow Window, ScanSummaryViewModel Vm) Create(ScanStaging staging)
    {
        var folder = new SyncFolder { Id = "folder-1", Name = "メイン写真庫", Path = @"C:\Photos" };
        var vm = new ScanSummaryViewModel(new ScanCoordinator(null!), TestLoc.Ja(), new StubWindows(), folder);
        vm.PresentSummary(staging); // Opened の差分計算開始をスキップさせる(probe 契約)
        var window = new ScanSummaryWindow { DataContext = vm };
        window.Show();
        RunJobs();
        return (window, vm);
    }

    [Fact]
    public async Task REQ100_missing率レッドでも適用ボタンは有効のまま()
    {
        await Session.Dispatch(() =>
        {
            // SC-4: 257,400/260,000(99.0%)=レッド。色は情報表示のみ=操作を制限しない
            var (window, vm) = Create(Staging(missing: 257400, managed: 260000));
            try
            {
                Assert.True(vm.IsRateRed);
                var apply = window.FindControl<Button>("ApplyButton")!;
                Assert.True(apply.IsVisible);
                Assert.True(apply.IsEnabled);
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task REQ100_適用CTAのラベルは変更合計件数を桁区切りで含む()
    {
        await Session.Dispatch(() =>
        {
            // SC-3 相当: 変更合計 10,000(9,842+124+18+16)
            var (window, vm) = Create(Staging(
                missing: 9842, managed: 259984, contentChanged: 124, addedPending: 16, missingFromPending: 18));
            try
            {
                var apply = window.FindControl<Button>("ApplyButton")!;
                var label = Assert.IsType<string>(apply.Content);
                Assert.Contains("10,000", label, StringComparison.Ordinal);
                Assert.Equal(vm.ApplyLabel, label);
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SCAN002_小規模は詳細ボタン非表示_中規模以上は表示()
    {
        await Session.Dispatch(() =>
        {
            // SC-2: 変更 28 件 → [変更内容を確認] なし(通常の 2 ボタン)
            var (small, _) = Create(Staging(missing: 9, managed: 12400, contentChanged: 3, addedPending: 16));
            try
            {
                Assert.False(small.FindControl<Button>("DetailButton")!.IsVisible);
            }
            finally
            {
                small.Close();
            }

            // SC-3: 変更 10,000 件 → 表示
            var (large, _) = Create(Staging(
                missing: 9842, managed: 259984, contentChanged: 124, addedPending: 16, missingFromPending: 18));
            try
            {
                Assert.True(large.FindControl<Button>("DetailButton")!.IsVisible);
            }
            finally
            {
                large.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CMP011_フッターはdlgBtn委譲で破棄ボタンは左端分離()
    {
        await Session.Dispatch(() =>
        {
            var (window, _) = Create(Staging(missing: 9, managed: 12400));
            try
            {
                var buttons = window.GetLogicalDescendants().OfType<Button>().ToList();
                Assert.NotEmpty(buttons);
                // CMP-011: 生 Button 禁止= 全ボタンが dlgBtn 委譲(CP-REGISTRY-LINT-122 検査C と対)
                Assert.All(buttons, b => Assert.Contains("dlgBtn", b.Classes));
                // 「結果を破棄」= secondary+DockPanel 左端分離(誤操作防止= CAD フッター契約)。
                // 右クラスタは StackPanel 内= フッター DockPanel 直下の Button は破棄のみ
                var discard = buttons.Single(b =>
                    b.Parent is DockPanel && DockPanel.GetDock(b) == global::Avalonia.Controls.Dock.Left);
                Assert.Contains("secondary", discard.Classes);
                Assert.True(discard.IsVisible);
                // 適用= primary(Accent 塗り= RegistryContract)
                var apply = window.FindControl<Button>("ApplyButton")!;
                Assert.Contains("primary", apply.Classes);
                var bg = Assert.IsAssignableFrom<ISolidColorBrush>(apply.Background).Color;
                Assert.Equal(RegistryContract.ColorAccent, bg);
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task L4_missing率カードの色は段に一致する()
    {
        await Session.Dispatch(() =>
        {
            foreach (var (staging, cls, expected) in new[]
            {
                (Staging(missing: 9, managed: 12400), "green", CalloutGreenBg),
                (Staging(missing: 9860, managed: 259984), "yellow", CalloutYellowBg),
                (Staging(missing: 257400, managed: 260000), "red", CalloutRedBg),
            })
            {
                var (window, _) = Create(staging);
                try
                {
                    var callout = window.GetLogicalDescendants().OfType<Border>()
                        .Single(b => b.Classes.Contains("callout") && b.Classes.Contains(cls));
                    Assert.True(callout.IsVisible, cls);
                    var bg = Assert.IsAssignableFrom<ISolidColorBrush>(callout.Background).Color;
                    Assert.Equal(expected, bg);
                }
                finally
                {
                    window.Close();
                }
            }
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task L6_変更合計行は強調地で末尾に置かれる()
    {
        await Session.Dispatch(() =>
        {
            var (window, vm) = Create(Staging(missing: 9, managed: 12400, contentChanged: 3, addedPending: 16));
            try
            {
                Assert.True(vm.SummaryRows[^1].IsTotal);
                RunJobs();
                var totalRow = window.GetLogicalDescendants().OfType<Border>()
                    .Single(b => b.Classes.Contains("sumRow") && b.Classes.Contains("total"));
                var bg = Assert.IsAssignableFrom<ISolidColorBrush>(totalRow.Background).Color;
                Assert.Equal(TotalRowBg, bg);
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task 変更0件は適用が無効で件数ラベルを出さない()
    {
        await Session.Dispatch(() =>
        {
            var (window, vm) = Create(Staging(missing: 0, managed: 12400));
            try
            {
                Assert.False(vm.CanApply);
                var apply = window.FindControl<Button>("ApplyButton")!;
                Assert.False(apply.IsEnabled);
                Assert.DoesNotContain("0件", Assert.IsType<string>(apply.Content), StringComparison.Ordinal);
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    private sealed class StubWindows : IWindowService
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
