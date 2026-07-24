using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ViewPrism2.App.Services;
using ViewPrism2.App.ViewModels;
using ViewPrism2.App.Views;
using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services.Repair;
using ViewPrism2.Infrastructure.Imaging;
using ViewPrism2.Infrastructure.Scanning;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// ECO-140: CAD integrity_review.md IR-1〜8 visualContract の headless 実レイアウト probe。
/// </summary>
[Trait("cp", "CP-INTEGRITY-036")]
public sealed class GfIntegrityReviewVisualParityTests : IDisposable
{
    private readonly TempDb _db = new();

    private static HeadlessUnitTestSession Session => HeadlessApp.Session;

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task IR2_calloutは青系fullWidth_3グループ見出し色と自動チップを表示する()
    {
        await Session.Dispatch(async () =>
        {
            var (vm, _) = await BuildVmAsync(includeMoved: true, includeChanged: true, includeMissing: true);
            var window = Show(vm);
            try
            {
                var callout = window.FindControl<Border>("AutoAdjudicateCallout")!;
                Assert.True(callout.IsVisible);
                Assert.Equal(window.ClientSize.Width, callout.Bounds.Width, precision: 1);
                Assert.Equal(Color.Parse("#EAF1FE"),
                    Assert.IsAssignableFrom<ISolidColorBrush>(callout.Background).Color);
                Assert.Contains(window.GetLogicalDescendants().OfType<TextBlock>(),
                    t => t.IsVisible && t.Text == "自動");
                Assert.True(window.FindControl<Border>("PendingPreviewBadge")!.IsVisible);
                Assert.Equal(Color.Parse("#EAF1FE"),
                    Assert.IsAssignableFrom<ISolidColorBrush>(
                        window.FindControl<Border>("WhyCard")!.Background).Color);
                var whyLead = window.FindControl<Border>("WhyCard")!
                    .GetLogicalDescendants().OfType<TextBlock>().First();
                Assert.Equal(Color.Parse("#2459CF"),
                    Assert.IsAssignableFrom<ISolidColorBrush>(whyLead.Foreground).Color);

                var automatic = window.FindControl<StackPanel>("AutomaticGroupHeader")!;
                var automaticText = automatic.GetLogicalDescendants().OfType<TextBlock>().Single();
                Assert.Equal(Color.Parse("#2459CF"),
                    Assert.IsAssignableFrom<ISolidColorBrush>(automaticText.Foreground).Color);
                var individual = window.FindControl<Border>("IndividualGroupHeader")!
                    .GetLogicalDescendants().OfType<TextBlock>().Single();
                Assert.Equal(Color.Parse("#8A93A2"),
                    Assert.IsAssignableFrom<ISolidColorBrush>(individual.Foreground).Color);
                var missing = window.FindControl<Border>("MissingGroupHeader")!
                    .GetLogicalDescendants().OfType<TextBlock>().Single();
                Assert.Equal(Color.Parse("#C4282D"),
                    Assert.IsAssignableFrom<ISolidColorBrush>(missing.Foreground).Color);
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CMP011_統合面footerはdlgBtn委譲で保留分離_primary_secondary_destructiveを使う()
    {
        await Session.Dispatch(async () =>
        {
            var (vm, _) = await BuildVmAsync(includeMoved: false, includeChanged: true, includeMissing: false);
            var window = Show(vm);
            try
            {
                var buttons = window.GetLogicalDescendants().OfType<Button>()
                    .Where(b => b.IsVisible && b.Classes.Contains("dlgBtn")).ToList();
                Assert.Contains(buttons, b => b.Classes.Contains("primary"));
                Assert.Contains(buttons, b => b.Classes.Contains("secondary"));
                Assert.Contains(buttons, b => b.Classes.Contains("destructive"));
                var defer = buttons.Single(b =>
                    b.Parent is DockPanel && DockPanel.GetDock(b) == global::Avalonia.Controls.Dock.Left);
                Assert.Equal("保留して次へ", defer.Content);
                Assert.Equal(Avalonia.Layout.HorizontalAlignment.Center,
                    window.FindControl<Button>("AcceptButton")!.HorizontalContentAlignment);
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task IR7_hash確認中は中立calloutで自動ボタンを隠す()
    {
        await Session.Dispatch(async () =>
        {
            var (vm, _) = await BuildVmAsync(includeMoved: true, includeChanged: true, includeMissing: false);
            vm.IsHashChecking = true;
            vm.HashCompleted = 1;
            vm.HashTotal = 3;
            var window = Show(vm);
            try
            {
                var neutral = window.FindControl<Border>("HashCheckingCallout")!;
                Assert.True(neutral.IsVisible);
                Assert.Equal(Color.Parse("#F4F6FA"),
                    Assert.IsAssignableFrom<ISolidColorBrush>(neutral.Background).Color);
                Assert.False(window.FindControl<Border>("AutoAdjudicateCallout")!.IsVisible);
                Assert.False(window.FindControl<Button>("AutoAdjudicateButton")!.IsVisible);
                Assert.True(window.FindControl<StackPanel>("AutomaticGroupHeader")!.IsVisible);
                var spinner = window.FindControl<Border>("HashSpinner")!;
                Assert.True(spinner.IsVisible);
                Assert.InRange(spinner.Bounds.Width, 10, 16);
                Assert.Empty(neutral.GetLogicalDescendants().OfType<ProgressBar>());
                Assert.Contains(window.GetLogicalDescendants().OfType<TextBlock>(),
                    t => t.IsVisible && t.Text?.Contains("（1/3）", StringComparison.Ordinal) == true);
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task IR5_条件検索4項目と候補カード5要素を表示し候補一覧だけがscrollする()
    {
        await Session.Dispatch(async () =>
        {
            var (vm, folder) = await BuildVmAsync(
                includeMoved: false, includeChanged: false, includeMissing: true);
            var candidate = Row(folder.Id, "found/candidate.jpg", ImageStatus.Normal);
            await _db.Images.AddAsync(candidate);
            vm.NameContainsInput = null;
            await vm.SearchCandidatesCommand.ExecuteAsync(null);
            vm.SelectedCandidate = Assert.Single(vm.Candidates);
            var window = Show(vm);
            try
            {
                Assert.True(window.FindControl<Border>("CriteriaSearchCard")!.IsVisible);
                Assert.Equal(4, window.FindControl<Border>("CriteriaSearchCard")!
                    .GetLogicalDescendants().OfType<TextBox>().Count());
                var list = window.FindControl<ListBox>("CandidateList")!;
                Assert.Equal(118, list.MaxHeight);
                Assert.Single(vm.Candidates);
                var card = vm.Candidates[0];
                Assert.NotNull(card.AbsolutePath);
                Assert.NotEmpty(card.FileName);
                Assert.NotEmpty(card.RelativePath);
                Assert.NotEmpty(card.SizeText);
                Assert.NotEmpty(card.ModifiedText);
                var selectedCard = window.GetLogicalDescendants().OfType<Border>()
                    .Single(border => border.Classes.Contains("integrityCandidate"));
                Assert.Equal(Color.Parse("#2F6BED"),
                    Assert.IsAssignableFrom<ISolidColorBrush>(selectedCard.BorderBrush).Color);
                Assert.False(selectedCard.BoxShadow.Equals(default(BoxShadows)));
                var selectedContainer = Assert.IsType<ListBoxItem>(
                    list.ContainerFromItem(vm.SelectedCandidate));
                var presenter = selectedContainer.GetVisualDescendants()
                    .OfType<ContentPresenter>()
                    .Single(p => p.Name == "PART_ContentPresenter");
                var presenterBackground = Assert.IsAssignableFrom<ISolidColorBrush>(
                    presenter.Background);
                Assert.Equal(0, presenterBackground.Opacity);
                var candidateBottom = list.TranslatePoint(
                    new Point(0, list.Bounds.Height), window)!.Value.Y;
                var footerTop = window.FindControl<Border>("IntegrityFooter")!
                    .TranslatePoint(new Point(0, 0), window)!.Value.Y;
                Assert.True(candidateBottom <= footerTop,
                    $"IR-5 candidate list overlaps footer: {candidateBottom} > {footerTop}");
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task IR8_0件は緑チェック空状態とsecondary閉じるだけを表示する()
    {
        await Session.Dispatch(async () =>
        {
            var (vm, _) = await BuildVmAsync(
                includeMoved: false, includeChanged: false, includeMissing: false);
            var window = Show(vm);
            try
            {
                Assert.True(vm.IsEmpty);
                Assert.Equal(480, vm.WindowWidth);
                Assert.Equal(480, window.Width);
                Assert.True(window.FindControl<StackPanel>("EmptyState")!.IsVisible);
                Assert.Contains(window.GetLogicalDescendants().OfType<TextBlock>(),
                    t => t.IsVisible && t.Text == "要確認の画像はありません");
                var visibleFooter = window.GetLogicalDescendants().OfType<Button>()
                    .Where(b => b.IsVisible && b.Classes.Contains("dlgBtn")).ToList();
                var close = Assert.Single(visibleFooter);
                Assert.Contains("secondary", close.Classes);
                Assert.Equal("閉じる", close.Content);
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task IR5_候補0件は検索後だけ淡色メッセージを表示する()
    {
        await Session.Dispatch(async () =>
        {
            var (vm, _) = await BuildVmAsync(
                includeMoved: false, includeChanged: false, includeMissing: true);
            vm.NameContainsInput = "not-found";
            vm.ExtensionInput = null;
            vm.MtimeFromInput = null;
            vm.SizeToleranceInput = null;
            Assert.False(vm.ShowNoCandidates);

            await vm.SearchCandidatesCommand.ExecuteAsync(null);
            var window = Show(vm);
            try
            {
                Assert.True(vm.ShowNoCandidates);
                var message = window.FindControl<TextBlock>("NoCandidatesMessage")!;
                Assert.True(message.IsVisible);
                Assert.Equal("候補はありません", message.Text);
                Assert.Equal(Color.Parse("#8A93A2"),
                    Assert.IsAssignableFrom<ISolidColorBrush>(message.Foreground).Color);
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task IR6_確認一覧は500幅_maxHeight176_非破壊primary()
    {
        await Session.Dispatch(() =>
        {
            var items = Enumerable.Range(1, 8)
                .Select(i => new ConfirmationListItem(
                    $"scan_{i:0000}.jpg",
                    i % 2 == 0 ? "同じ場所に戻りました — 受け入れ" : $"old_{i:0000}.jpg へ再リンク",
                    $@"C:\Photos\scan_{i:0000}.jpg"))
                .ToList();
            var dialog = new ConfirmDialog(
                new LocalizationProxy(TestLoc.Ja()),
                "自動裁定の確認",
                "この 8 件をまとめて裁定します",
                "適用する",
                destructive: false,
                items: items,
                supportingMessage: "ハッシュが一致するものだけを対象にします。");
            dialog.Show();
            RunJobs();
            try
            {
                Assert.Equal(500, dialog.Width);
                Assert.Equal(176, dialog.FindControl<ListBox>("ConfirmationItems")!.MaxHeight);
                var buttons = dialog.GetLogicalDescendants().OfType<Button>().ToList();
                Assert.Contains("secondary", buttons[0].Classes);
                Assert.Contains("primary", buttons[1].Classes);
            }
            finally
            {
                dialog.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    private async Task<(IntegrityReviewViewModel Vm, SyncFolder Folder)> BuildVmAsync(
        bool includeMoved,
        bool includeChanged,
        bool includeMissing)
    {
        var root = Path.Combine(Path.GetTempPath(), "ViewPrism2.Tests", Guid.NewGuid().ToString("D"));
        Directory.CreateDirectory(root);
        var folder = new SyncFolder
        {
            Id = IdGenerator.NewId(),
            Name = "fixture",
            Path = root,
        };
        Assert.True((await _db.Folders.AddAsync(folder)).IsSuccess);
        if (includeMoved)
        {
            var missing = Row(folder.Id, "old/moved.jpg", ImageStatus.Missing);
            await _db.Images.AddAsync(missing);
            await _db.Images.AddAsync(Row(
                folder.Id,
                "new/moved.jpg",
                ImageStatus.Pending,
                PendingOrigin.New,
                missing.Id));
        }

        if (includeChanged)
        {
            await _db.Images.AddAsync(Row(
                folder.Id, "changed.jpg", ImageStatus.Pending, PendingOrigin.Changed));
        }

        if (includeMissing)
        {
            await _db.Images.AddAsync(Row(folder.Id, "missing.jpg", ImageStatus.Missing));
        }

        var relink = new RelinkService(_db.Images, _db.Tags);
        var vm = new IntegrityReviewViewModel(
            new IntegrityReviewService(_db.Images, relink, new NoopHashProvider()),
            new PendingReviewService(_db.Images),
            _db.Images,
            _db.Tags,
            relink,
            new TrashService(_db.Images, _db.Folders, new FilePresenceProbe()),
            TestLoc.Ja(),
            new NullWindows(),
            folder);
        await vm.LoadAsync();
        return (vm, folder);
    }

    private static IntegrityReviewWindow Show(IntegrityReviewViewModel vm)
    {
        var window = new IntegrityReviewWindow { DataContext = vm };
        window.Show();
        RunJobs();
        return window;
    }

    private static void RunJobs()
    {
        for (var i = 0; i < 8; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static ImageRecord Row(
        string folderId,
        string name,
        ImageStatus status,
        PendingOrigin? origin = null,
        string? candidateId = null) => new()
        {
            Id = IdGenerator.NewId(),
            SyncFolderId = folderId,
            RelativePath = name,
            FileName = Path.GetFileName(name),
            FileSize = 10,
            Hash = new string('a', 64),
            Status = status,
            PendingOrigin = origin,
            CandidateLinkId = candidateId,
            CreatedDate = "2026-07-24T00:00:00.000Z",
            ModifiedDate = "2026-07-24T00:00:00.000Z",
        };

    private sealed class NoopHashProvider : IIntegrityReviewHashProvider
    {
        public Task<string> ComputeSha256Async(string absolutePath, CancellationToken ct)
            => Task.FromResult(new string('a', 64));
    }

    private sealed class NullWindows : IWindowService
    {
        public Task<bool> ConfirmAsync(
            string title,
            string message,
            string confirmLabel,
            bool destructive = false,
            string? cancelLabel = null) => Task.FromResult(false);

        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);

        public Task ShowFolderManagementAsync() => Task.CompletedTask;

        public Task ShowSettingsAsync() => Task.CompletedTask;

        public Task ShowSnapshotsAsync() => Task.CompletedTask;

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
    }
}
