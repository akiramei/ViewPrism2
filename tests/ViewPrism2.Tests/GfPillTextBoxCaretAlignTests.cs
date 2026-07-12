using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
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
/// ECO-040(maintainer 報告 2026-07-05)の恒久回帰: ピル Border に埋め込んだ TextBox
/// (BorderThickness=0 / Padding=0)のカレット・テキスト行がピルの垂直中央に描画されること。
/// CAD(mock 画像タブ.dc.html L385-387)は flex/align-items:center= 垂直中央が原器確定。
/// 欠陥様式= Padding="0" でテーマ既定の内側余白を潰しつつ VerticalContentAlignment 未指定
/// → テキスト行が上寄り。同型全数(タグ追加検索+整理トレイ条件入力×画像/作業タブ)を
/// Avalonia.Headless の実レイアウトパスで ground-truth 実測する。
/// </summary>
[Trait("cp", "CP-UI-G1")]
public sealed class GfPillTextBoxCaretAlignTests : IDisposable
{
    private readonly TempDb _db = new();

    public void Dispose() => _db.Dispose();

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
        public Task ShowSimilarSearchAsync(ImageEntry baseImage, IReadOnlyList<ImageEntry> collectionEntries) => Task.CompletedTask;
        public Task<bool> ShowMergeAsync(ImageEntry target, IReadOnlyList<ImageEntry> sources) => Task.FromResult(false);
        public Task ShowTrashAsync(string collectionId) => Task.CompletedTask;
    }

    private async Task<ImageTabViewModel> NewImageTabVmAsync()
    {
        var col = new SyncFolder { Id = IdGenerator.NewId(), Name = "C", Path = @"C:\col" };
        await _db.Folders.AddAsync(col);
        await _db.Images.AddAsync(new ImageRecord
        {
            Id = IdGenerator.NewId(),
            SyncFolderId = col.Id,
            RelativePath = "a.jpg",
            FileName = "a.jpg",
            FileSize = 10,
            Hash = new string('0', 64),
            Status = ImageStatus.Normal,
            CreatedDate = "2026-06-11T00:00:00.000Z",
            ModifiedDate = "2026-06-11T00:00:00.000Z",
        });
        var vm = new ImageTabViewModel(
            _db.Folders, _db.Images, _db.Tags, new ImageSorter(),
            new ViewService(_db.Views, _db.Clock), new NodeGraphBuilder(),
            new PathConditionConverter(), new ConditionEvaluator(),
            new SimilaritySearchService(_db.Folders, _db.Images, _db.Features, _db.Similarities, new FakePHashImageReader(), _db.Clock),
            new MergeService(_db.Images, _db.Tags, _db.Merges),
            new TrashService(_db.Images, _db.Folders, new FilePresenceProbe()),
            new StubWindowService(), new AppSettings(), new WorkspaceService(_db.Workspaces, _db.Clock), TestLoc.Empty());
        await vm.InitializeAsync(col.Id);
        return vm;
    }

    /// <summary>
    /// pill(最近傍 Border 祖先)内の TextBox について、描画テキスト行(カレット位置 0 の
    /// ヒットテスト矩形)の垂直中心と pill 垂直中心のズレ(px)を実測する。
    /// </summary>
    private static double CaretCenterOffsetFromPillCenter(TextBox tb)
    {
        var pill = tb.FindAncestorOfType<Border>();
        Assert.NotNull(pill);
        var presenter = tb.GetVisualDescendants().OfType<TextPresenter>().FirstOrDefault();
        Assert.NotNull(presenter);
        var origin = presenter!.TranslatePoint(new Point(0, 0), pill!);
        Assert.NotNull(origin);
        var caret = presenter.TextLayout.HitTestTextPosition(0);
        double caretCenterY = origin!.Value.Y + caret.Y + caret.Height / 2;
        return caretCenterY - pill.Bounds.Height / 2;
    }

    private static void RunJobs()
    {
        for (var i = 0; i < 8; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>
    /// GF-040-01(golden 所見 2026-07-05): テキスト開始位置(カレット位置 0)が TextBox
    /// (=フォーカス枠)左端から離れていること。Padding=0 の水平面= 枠にテキストが密着し窮屈。
    /// </summary>
    private static double TextLeftInsetFromTextBox(TextBox tb)
    {
        var presenter = tb.GetVisualDescendants().OfType<TextPresenter>().First();
        var caret = presenter.TextLayout.HitTestTextPosition(0);
        var left = presenter.TranslatePoint(new Point(caret.X, 0), tb);
        Assert.NotNull(left);
        return left!.Value.X;
    }

    private static void AssertCentered(TextBox tb, string label)
    {
        double off = CaretCenterOffsetFromPillCenter(tb);
        Assert.True(Math.Abs(off) <= 2.0,
            $"{label}: テキスト行中心がピル中心から {off:+0.0;-0.0}px ズレ(許容 ±2.0)");
        double inset = TextLeftInsetFromTextBox(tb);
        Assert.True(inset is >= 3.0 and <= 8.0,
            $"{label}: テキスト左端がフォーカス枠から {inset:0.0}px(窮屈でない余白= 3.0〜8.0 を要求)");
    }

    [Fact]
    public async Task タグ追加検索ボックスのテキスト行はピル垂直中央()
    {
        var vm = await NewImageTabVmAsync();
        vm.ToggleEditCommand.Execute(null);                                   // タグ編集モード
        vm.HandleItemClick(vm.Items.Single(i => !i.IsFolder), false, false);  // 1 枚選択= PanelActive
        vm.TabAddCommand.Execute(null);                                       // タグ追加タブ
        Assert.True(vm.PanelActive && vm.OnAddTab);

        await HeadlessApp.Session.Dispatch(() =>
        {
            var window = new Window { Content = new ImageTabView { DataContext = vm }, Width = 1366, Height = 768 };
            window.Show();
            RunJobs();

            // タグ追加タブの検索 TextBox(Width=240 は当該ボックス固有)
            var tb = window.GetVisualDescendants().OfType<TextBox>()
                .FirstOrDefault(t => Math.Abs(t.Width - 240) < 0.5);
            Assert.NotNull(tb);
            AssertCentered(tb!, "タグ追加 検索ボックス");

            window.Close();
        }, CancellationToken.None);
    }

    // ECO-055(裁定②a): 「整理トレイ条件入力のテキスト行はピル垂直中央」×2(画像/作業タブ)は退役 —
    // 検査対象の自由入力 TextBox 2 本が CAD 意味論(マージ先との属性一致トグル)への置換で消滅した。
    // ECO-040 の観点(ピル埋込 TextBox の垂直中央)はタグ追加検索ボックスの fact が引き続き担う。
    // トグルチップの視覚は golden(G-9/G-1)+次の v2 追随 ECO のレイアウト検査で担保する。
}
