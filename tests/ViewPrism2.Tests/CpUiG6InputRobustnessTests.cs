using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ViewPrism2.App.Services;
using ViewPrism2.App.ViewModels;
using ViewPrism2.App.Views;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// CP-UI-G6(ECO-101): タグタブ操作系の入力堅牢性。
/// ①ボタン非押下のポインター移動でドラッグを開始しない(押下状態残留の自己回復)
/// ②挿入ポイント/「＋ 子にする」は左ボタン以外で実行しない ③無変更のホーム再設定はダーティにしない。
/// プローブ先行(R5): 是正前は ①②③ とも不合格 → 是正で緑転。
/// golden(ECO-099/100)の手順(左クリック正常系+Esc)の谷間で潜伏した欠陥の機械封止。
/// </summary>
[Trait("cp", "CP-UI-G6")]
public sealed class CpUiG6InputRobustnessTests : IDisposable
{
    private readonly TempDb _db = new();
    private readonly ViewService _views;
    private readonly TagService _tagService;

    public CpUiG6InputRobustnessTests()
    {
        _views = new ViewService(_db.Views, _db.Clock);
        _tagService = new TagService(_db.Tags);
    }

    public void Dispose() => _db.Dispose();

    private async Task<(TagsTabViewModel Tab, Tag TagA, Tag TagB)> SetupAsync()
    {
        var view = (await _views.CreateAsync("V")).Value!;
        var tagA = (await _tagService.CreateAsync("タグA", TagType.Simple)).Value!;
        var tagB = (await _tagService.CreateAsync("タグB", TagType.Simple)).Value!;
        var tab = new TagsTabViewModel(_views, _tagService, _db.Tags, TestLoc.Ja(), new StubWindows());
        await tab.EnsureLoadedAsync();
        await tab.Editor.LoadAsync(view, new Dictionary<string, Tag>(StringComparer.Ordinal)
        {
            [tagA.Id] = tagA,
            [tagB.Id] = tagB,
        });
        return (tab, tagA, tagB);
    }

    // ---- ③ 無変更のホーム再設定(VM 決定論・是正前赤) ----

    [Fact]
    public async Task 現ホーム行へのホームに設定は無変更でダーティにしない()
    {
        var (tab, tagA, tagB) = await SetupAsync();
        var a = tab.Editor.AddNode(tagA, null)!;
        var b = tab.Editor.AddNode(tagB, null)!;
        tab.Editor.SetHomeFromMenuCommand.Execute(a);
        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)tab.Editor.SaveCommand).ExecuteAsync(null);
        Assert.False(tab.Editor.IsDirty);

        // 現ホーム行への再設定= 実変更なし → ダーティ不変(是正前= SetDirty(true) で赤)
        tab.Editor.SetHomeFromMenuCommand.Execute(a);
        Assert.True(a.IsHome);
        Assert.False(tab.Editor.IsDirty);

        // pin: 他行への設定は排他移動してダーティ(従来契約の回帰なし)
        tab.Editor.SetHomeFromMenuCommand.Execute(b);
        Assert.False(a.IsHome);
        Assert.True(b.IsHome);
        Assert.True(tab.Editor.IsDirty);
    }

    // ---- ② 右クリックで配置が確定しない(headless 実入力・是正前赤) ----

    [Fact]
    public async Task 配置モード中の右クリックでは挿入が実行されない()
    {
        var (tab, tagA, _) = await SetupAsync();
        tab.Editor.AddNode(tagA, null);

        await HeadlessApp.Session.Dispatch(() =>
        {
            var window = new Window { Content = new TagsTabView { DataContext = tab }, Width = 1366, Height = 900 };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            tab.TogglePlacing(tab.Palette.Tags.First(r => r.Tag.Id == tagA.Id));
            Dispatcher.UIThread.RunJobs();
            Assert.True(tab.Editor.IsPlacing);
            var before = tab.Editor.Roots.Count;

            // ルート末尾の挿入ポイントを右クリック(是正前= InsertRootEnd が実行され挿入+配置解除で赤)
            var rootEnd = window.GetVisualDescendants().OfType<Border>()
                .First(b => b.Classes.Contains("ipRootEnd") && b.IsVisible);
            var point = CenterOf(rootEnd);
            window.MouseDown(point, MouseButton.Right);
            window.MouseUp(point, MouseButton.Right);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(before, tab.Editor.Roots.Count); // ツリー不変
            Assert.True(tab.Editor.IsPlacing);            // 配置モードも不変

            // pin: 左クリックでは従来どおり挿入される(回帰なし)
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(before + 1, tab.Editor.Roots.Count);
            Assert.False(tab.Editor.IsPlacing);

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    // ---- ① ボタン非押下のポインター移動でドラッグを開始しない(押下状態残留の自己回復・是正前赤) ----

    [Fact]
    public async Task ボタン非押下の移動は行ドラッグを開始せず既存の配置モードを壊さない()
    {
        var (tab, tagA, tagB) = await SetupAsync();
        var node = tab.Editor.AddNode(tagA, null)!;
        _ = node;

        await HeadlessApp.Session.Dispatch(() =>
        {
            var window = new Window { Content = new TagsTabView { DataContext = tab }, Width = 1366, Height = 900 };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // クリック配置(タグB)を開始しておく — 非押下移動が誤ドラッグへ到達すると
            // BeginMove/finally が配置モードを破壊する(持続観測できる副作用)
            tab.TogglePlacing(tab.Palette.Tags.First(r => r.Tag.Id == tagB.Id));
            Dispatcher.UIThread.RunJobs();
            Assert.True(tab.Editor.IsPlacing);

            var row = window.GetVisualDescendants().OfType<Border>()
                .First(b => b.Classes.Contains("nodeRow"));
            var start = CenterOf(row);
            // headless セッションはポインタ押下状態をテスト間で共有する — 先行テストの残留を掃除
            window.MouseUp(start, MouseButton.Left);
            // 押下(左)で press 状態を作る → ボタン情報の無い移動(キャプチャ喪失後のホバー相当)
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(new Point(start.X + 60, start.Y), RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.Null(tab.Editor.DraggingNode);                 // 誤ドラッグ非開始
            Assert.Equal(tagB.Id, tab.Editor.PlacingTag?.Id);     // 既存の配置モード無傷(是正前=破壊されて赤)
            Assert.Single(tab.Editor.Roots);                      // ツリー不変

            window.MouseUp(new Point(start.X + 60, start.Y), MouseButton.Left); // 対にして後続テストへ残留させない

            // pin: 通常の行クリック(Down+Up)は従来どおり動く(CaptureLost 掃除がクリックを壊さない)。
            // 配置解除後、葉行クリック=選択(CAD インタラクション表)
            tab.Editor.CancelPlacingCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            var leaf = tab.Editor.Roots[0];
            tab.Editor.SelectedNode = null;
            var leafRow = window.GetVisualDescendants().OfType<Border>()
                .First(b => b.Classes.Contains("nodeRow") && ReferenceEquals(b.DataContext, leaf));
            var p = CenterOf(leafRow);
            window.MouseDown(p, MouseButton.Left);
            window.MouseUp(p, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(leaf, tab.Editor.SelectedNode);

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ボタン非押下の移動はパレットのドラッグ配置を開始せず既存の配置モードを壊さない()
    {
        var (tab, tagA, tagB) = await SetupAsync();
        tab.Editor.AddNode(tagA, null);

        await HeadlessApp.Session.Dispatch(() =>
        {
            var window = new Window { Content = new TagsTabView { DataContext = tab }, Width = 1366, Height = 900 };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            tab.TogglePlacing(tab.Palette.Tags.First(r => r.Tag.Id == tagA.Id));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(tagA.Id, tab.Editor.PlacingTag?.Id);

            // 別カード(タグB)上で press → ボタン情報の無い移動(是正前= BeginDragPlacing→finally 解除で
            // 配置モードが null へ破壊されて赤)
            var cardB = window.GetVisualDescendants().OfType<Border>()
                .First(b => b.Classes.Contains("card") &&
                            b.DataContext is TagPaletteRowViewModel r && r.Tag.Id == tagB.Id);
            var start = CenterOf(cardB);
            window.MouseUp(start, MouseButton.Left); // 先行テストの押下残留を掃除(セッション共有)
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(new Point(start.X - 60, start.Y + 30), RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(tagA.Id, tab.Editor.PlacingTag?.Id);     // 既存の配置モード無傷
            Assert.Single(tab.Editor.Roots);                      // ツリー不変

            window.MouseUp(new Point(start.X - 60, start.Y + 30), MouseButton.Left); // 対にして残留させない

            // pin: 通常のカードクリック(Down+Up)は従来どおり配置トグル(CaptureLost 掃除がクリックを壊さない)
            window.MouseDown(start, MouseButton.Left);
            window.MouseUp(start, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(tagB.Id, tab.Editor.PlacingTag?.Id);     // クリックで B へ切替

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    private static Point CenterOf(Visual visual)
    {
        var tb = visual.GetTransformedBounds()!.Value;
        var rect = tb.Bounds.TransformToAABB(tb.Transform);
        return new Point(rect.X + (rect.Width / 2), rect.Y + (rect.Height / 2));
    }

    private sealed class StubWindows : IWindowService
    {
        public Task<bool> ConfirmAsync(string title, string message, string confirmLabel, bool destructive = false, string? cancelLabel = null) => Task.FromResult(true);

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
