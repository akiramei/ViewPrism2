using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using ViewPrism2.App.Services;
using ViewPrism2.App.ViewModels;
using ViewPrism2.App.Views;
using ViewPrism2.Core.Models;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// ECO-130 R8 所見の是正プローブ(UI ライフサイクル境界)。
/// 所見1= 適用中の ✕ クローズは中断もキャンセルもできないため塞ぐ(REQ-100「クローズ=適用しない」は
/// 適用開始前の契約・開始後は完走させる=部分適用を作らない)。
/// 所見2= SC-6 確認ダイアログ表示中の破棄/詳細/✕ の再入を塞ぐ(破棄報告後に適用が走る事故防止)。
/// </summary>
[Trait("cp", "CP-UI-G1")]
public sealed class CpScanSummaryLifecycleTests
{
    private static HeadlessUnitTestSession Session => HeadlessApp.Session;

    private static void RunJobs()
    {
        for (var i = 0; i < 8; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static ScanStaging Staging(int missing, int managed)
        => new()
        {
            FolderId = "folder-1",
            ManagedTotal = managed,
            ScannedFiles = managed - missing,
            Unchanged = managed - missing,
            MetaUpdated = 0,
            AddedNormal = 0,
            AddedPending = 0,
            MissingFromNormal = missing,
            PendingRemoved = 0,
            DeletedUnchanged = 0,
            ReadFailures = 0,
            Adds = [],
            MetaUpdates = [],
            StatusUpdates = [],
            Deletes = [],
            Examples = [],
        };

    private static (ScanSummaryWindow Window, ScanSummaryViewModel Vm) Create(
        ScanStaging staging, IWindowService windows)
    {
        var folder = new SyncFolder { Id = "folder-1", Name = "fixture", Path = @"C:\Photos" };
        var vm = new ScanSummaryViewModel(new ScanCoordinator(null!), TestLoc.Ja(), windows, folder);
        vm.PresentSummary(staging);
        var window = new ScanSummaryWindow { DataContext = vm };
        window.Show();
        RunJobs();
        return (window, vm);
    }

    [Fact]
    public async Task 適用中のウィンドウクローズはブロックされる()
    {
        await Session.Dispatch(() =>
        {
            var (window, vm) = Create(Staging(9, 100), new PendingConfirmWindows());
            try
            {
                vm.Phase = ScanStagePhase.Applying;
                RunJobs();
                window.Close(); // ✕ 相当
                RunJobs();
                // 適用中は閉じない(閉じられると Outcome=Discarded なのに DB 適用が完走する=所見1)
                Assert.True(window.IsVisible);

                // 適用が終わってサマリーへ戻れば通常どおり閉じられる
                vm.Phase = ScanStagePhase.Summary;
                window.Close();
                RunJobs();
                Assert.False(window.IsVisible);
            }
            finally
            {
                if (window.IsVisible)
                {
                    window.Close();
                }
            }
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task 確認ダイアログ表示中は破棄もクローズも再入できない()
    {
        await Session.Dispatch(() =>
        {
            var windows = new PendingConfirmWindows();
            // 変更合計 ≥1,000= SC-6 確認ダイアログ経路
            var (window, vm) = Create(Staging(1000, 10000), windows);
            try
            {
                var closeRequests = 0;
                vm.RequestClose += (_, _) => closeRequests++;

                var applyTask = ((IAsyncRelayCommand)vm.ApplyCommand).ExecuteAsync(null);
                RunJobs();
                Assert.True(vm.IsConfirmOpen); // 確認ダイアログ待ち中

                vm.DiscardCommand.Execute(null); // 破棄は無効(再入防止=所見2)
                RunJobs();
                Assert.Equal(0, closeRequests);

                window.Close(); // ✕ も無効
                RunJobs();
                Assert.True(window.IsVisible);

                windows.CompleteConfirm(false); // キャンセルで確認を閉じる
                RunJobs();
                Assert.True(applyTask.IsCompleted);
                Assert.False(vm.IsConfirmOpen);

                vm.DiscardCommand.Execute(null); // 確認が閉じれば破棄できる
                RunJobs();
                Assert.Equal(1, closeRequests);
            }
            finally
            {
                if (window.IsVisible)
                {
                    window.Close();
                }
            }
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>ConfirmAsync を保留できるスタブ(SC-6 表示中の再入検査用)。</summary>
    private sealed class PendingConfirmWindows : IWindowService
    {
        private TaskCompletionSource<bool>? _pending;

        public void CompleteConfirm(bool result) => _pending?.TrySetResult(result);

        public Task<bool> ConfirmAsync(string title, string message, string confirmLabel,
            bool destructive = false, string? cancelLabel = null)
        {
            _pending = new TaskCompletionSource<bool>();
            return _pending.Task;
        }

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
