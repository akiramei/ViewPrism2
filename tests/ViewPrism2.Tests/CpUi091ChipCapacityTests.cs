using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using ViewPrism2.Core.Services.Repair;
using ViewPrism2.Core.Services.Similarity;
using ViewPrism2.App.Services;
using ViewPrism2.App.ViewModels;
using ViewPrism2.App.Views;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// CP-CHIPWRAP-088 容量・到達性拡張(ECO-091・CAD VC-IMG-9/10=image_tab.md/VC-WORK-2/3=work_tab.md):
/// チップ行は最大 2 行+「ほか N 件」→ポップオーバー(検索+全項目一覧)。N=非表示項目数。
/// FS 軸/作業タブは折畳み時も active+「クリア」を通常領域に残す。裁定= IMG-023A=A-b/B=B-a(2026-07-15)。
/// 是正前赤の真因= 折返しのみ(ECO-088/089)で容量上限・「ほか N 件」・ポップオーバーが未実装。
/// </summary>
[Trait("cp", "CP-CHIPWRAP-088")]
public sealed class CpUi091ChipCapacityTests : IDisposable
{
    private const double WindowWidth = 1366;
    /// <summary>行判定の許容差。「ほか N 件」ボタンはチップと高さ僅差=同一行でも Y が数 px ずれる(CAD mock 実証の罠)。</summary>
    private const double RowTolerance = 6;

    private readonly TempDb _db = new();
    private SyncFolder _col = null!;
    private View _view = null!;

    public void Dispose() => _db.Dispose();

    // ---- 容量(VC-IMG-9 / VC-WORK-2): 最大 2 行+N 一致 ----

    [Fact]
    public async Task 画像タブ_47件でチップ領域は最大2行に収まる()
    {
        await SeedImageTabAsync(PrefectureNames47, "都道府県");
        await HeadlessApp.Session.Dispatch(async () =>
        {
            var (window, _) = await RenderImageTabAsync("都道府県");
            var rects = ChipRects(window).Concat(MoreButtonRect(window) is { } m ? [m] : Array.Empty<Rect>()).ToList();
            Assert.True(RowsOf(rects) <= 2,
                $"チップ領域が {RowsOf(rects)} 行 — 最大 2 行(VC-IMG-9/IMG-023A=A-b)を超過(ECO-091)");
            Assert.All(rects, r => Assert.True(r.Right <= WindowWidth + 0.5));
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task 画像タブ_47件で非表示数がほかN件のNと一致する()
    {
        await SeedImageTabAsync(PrefectureNames47, "都道府県");
        await HeadlessApp.Session.Dispatch(async () =>
        {
            var (window, _) = await RenderImageTabAsync("都道府県");
            var moreBtn = MoreButton(window);
            Assert.True(moreBtn is not null, "「ほか N 件」ボタンが存在しない(VC-IMG-9・ECO-091)");
            var visible = ChipRects(window).Count;
            var n = ParseMoreCount(moreBtn!);
            Assert.Equal(47, visible + n);
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task 作業タブ_47件でチップ領域は最大2行に収まる()
    {
        await HeadlessApp.Session.Dispatch(() =>
        {
            var (window, _) = RenderWorkTab(PrefectureNames47.Select(p => (Label: p, Active: false)).ToList());
            var rects = ChipRects(window).Concat(MoreButtonRect(window) is { } m ? [m] : Array.Empty<Rect>()).ToList();
            Assert.True(RowsOf(rects) <= 2,
                $"チップ領域が {RowsOf(rects)} 行 — 最大 2 行(VC-WORK-2/IMG-023A=A-b)を超過(ECO-091)");
            Assert.All(rects, r => Assert.True(r.Right <= WindowWidth + 0.5));
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task 作業タブ_47件で非表示数がほかN件のNと一致する()
    {
        await HeadlessApp.Session.Dispatch(() =>
        {
            var (window, _) = RenderWorkTab(PrefectureNames47.Select(p => (Label: p, Active: false)).ToList());
            var moreBtn = MoreButton(window);
            Assert.True(moreBtn is not null, "「ほか N 件」ボタンが存在しない(VC-WORK-2・ECO-091)");
            var visible = ChipRects(window).Count;
            var n = ParseMoreCount(moreBtn!);
            Assert.Equal(47, visible + n);
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    // ---- 優先配置(VC-IMG-9 後段 / VC-WORK-2): active+クリアは通常領域から消えない ----

    [Fact]
    public async Task 作業タブ_折畳み時もクリアとactiveチップは表示領域に残る()
    {
        await HeadlessApp.Session.Dispatch(() =>
        {
            // クリア+46 件+定義順の深い位置(40 番目)に active 1 件
            var labels = PrefectureNames47.Take(46).Select((p, i) => (Label: p, Active: i == 40)).ToList();
            var (window, _) = RenderWorkTab(labels, withClear: true);
            var visibleLabels = ChipLabels(window);
            Assert.True(RowsOf(ChipRects(window)) <= 2, "折畳みが機能していない(前段の容量契約)");
            Assert.Contains("クリア", visibleLabels);
            Assert.Contains(PrefectureNames47[40], visibleLabels); // active は overflow へ送らない
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    // ---- ポップオーバー(VC-IMG-10 / VC-WORK-3): 検索+全項目一覧+到達 ----

    [Fact]
    public async Task 画像タブ_ポップオーバーは全項目を列挙し選択でナビゲーションする()
    {
        await SeedImageTabAsync(PrefectureNames47, "都道府県");
        await HeadlessApp.Session.Dispatch(async () =>
        {
            var (window, vm) = await RenderImageTabAsync("都道府県");
            var moreBtn = MoreButton(window);
            Assert.True(moreBtn is not null, "「ほか N 件」ボタンが存在しない(VC-IMG-10・ECO-091)");
            moreBtn!.Command!.Execute(moreBtn.CommandParameter);
            RunJobs();

            var popup = window.GetVisualDescendants().OfType<Popup>().FirstOrDefault(p => p.IsOpen);
            Assert.True(popup is not null, "ポップオーバーが開かない(VC-IMG-10)");
            var rows = PopoverRows(popup!);
            Assert.Equal(47, rows.Count); // 全項目一覧=全ノード到達可能

            var okinawa = rows.Single(r => RowLabel(r).Contains("沖縄県"));
            okinawa.Command!.Execute(okinawa.CommandParameter);
            RunJobs();
            Assert.False(popup!.IsOpen, "選択後にポップオーバーが閉じない");
            Assert.False(vm.ShowChips, "値リーフへナビゲーションされていない(リーフはチップ行なし)");
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task 画像タブ_ポップオーバーの検索で項目を絞り込める()
    {
        await SeedImageTabAsync(PrefectureNames47, "都道府県");
        await HeadlessApp.Session.Dispatch(async () =>
        {
            var (window, _) = await RenderImageTabAsync("都道府県");
            var moreBtn = MoreButton(window);
            Assert.True(moreBtn is not null, "「ほか N 件」ボタンが存在しない(VC-IMG-10・ECO-091)");
            moreBtn!.Command!.Execute(moreBtn.CommandParameter);
            RunJobs();
            var popup = window.GetVisualDescendants().OfType<Popup>().First(p => p.IsOpen);
            var search = popup.Child!.GetVisualDescendants().OfType<TextBox>().First();
            search.Text = "沖";
            RunJobs();
            var rows = PopoverRows(popup);
            Assert.Single(rows);
            Assert.Contains("沖縄県", RowLabel(rows[0]));
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task 画像タブ_Escapeでポップオーバーが閉じフォーカスがほかN件へ戻る()
    {
        await SeedImageTabAsync(PrefectureNames47, "都道府県");
        await HeadlessApp.Session.Dispatch(async () =>
        {
            var (window, _) = await RenderImageTabAsync("都道府県");
            var moreBtn = MoreButton(window);
            Assert.True(moreBtn is not null, "「ほか N 件」ボタンが存在しない(VC-IMG-10・ECO-091)");
            moreBtn!.Command!.Execute(moreBtn.CommandParameter);
            RunJobs();
            var popup = window.GetVisualDescendants().OfType<Popup>().First(p => p.IsOpen);
            var search = popup.Child!.GetVisualDescendants().OfType<TextBox>().First();
            search.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape, Source = search });
            RunJobs();
            Assert.False(popup.IsOpen, "Escape でポップオーバーが閉じない(VC-IMG-10)");
            var focused = TopLevel.GetTopLevel(window)?.FocusManager?.GetFocusedElement();
            Assert.Same(MoreButton(window), focused); // フォーカス復帰
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    // ---- GF-091-01: 「ほか N 件」の垂直整列(mock=flex align-items:center が正典) ----

    [Fact]
    public async Task 画像タブ_ほかN件ボタンはチップと垂直中央が揃い高さも一致する()
    {
        await SeedImageTabAsync(PrefectureNames47, "都道府県");
        await HeadlessApp.Session.Dispatch(async () =>
        {
            var (window, _) = await RenderImageTabAsync("都道府県");
            var moreBtn = MoreButton(window);
            Assert.True(moreBtn is not null, "「ほか N 件」ボタンが存在しない(前段の容量契約)");
            var btnRect = GlobalRect(moreBtn!);
            // 同じ行(2 行目)のチップと比較
            var rowChips = ChipRects(window)
                .Where(r => Math.Abs(r.Y + r.Height / 2 - (btnRect.Y + btnRect.Height / 2)) < r.Height)
                .ToList();
            Assert.NotEmpty(rowChips);
            var chip = rowChips[^1];
            Assert.True(Math.Abs(chip.Height - btnRect.Height) <= 1.5,
                $"高さ不一致: チップ {chip.Height:0.0}px / ほかN件 {btnRect.Height:0.0}px(GF-091-01)");
            var chipCenter = chip.Y + chip.Height / 2;
            var btnCenter = btnRect.Y + btnRect.Height / 2;
            Assert.True(Math.Abs(chipCenter - btnCenter) <= 1.5,
                $"垂直中心ずれ: チップ {chipCenter:0.0} / ほかN件 {btnCenter:0.0}(mock=align-items:center・GF-091-01)");
            // ボタン内ラベルも垂直中央(GF-091-01 の実体=コンテンツ上寄り)
            var label = moreBtn!.GetVisualDescendants().OfType<TextBlock>().First();
            var labelRect = GlobalRect(label);
            var labelCenter = labelRect.Y + labelRect.Height / 2;
            Assert.True(Math.Abs(labelCenter - btnCenter) <= 2,
                $"ラベルがボタン内で上寄り: label 中心 {labelCenter:0.0} / ボタン中心 {btnCenter:0.0}(GF-091-01)");
            // ラベルボックスが文字サイズであること(Stretch だと bounds 中心は合うがグリフは上端描画=実機の上寄り)
            Assert.True(labelRect.Height <= 24,
                $"ラベルボックスが縦 Stretch({labelRect.Height:0.0}px)— VerticalContentAlignment 未指定でグリフ上寄り(GF-091-01)");
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task 作業タブ_ほかN件ボタンはチップと垂直中央が揃い高さも一致する()
    {
        await HeadlessApp.Session.Dispatch(() =>
        {
            var (window, _) = RenderWorkTab(PrefectureNames47.Select(p => (Label: p, Active: false)).ToList());
            var moreBtn = MoreButton(window);
            Assert.True(moreBtn is not null, "「ほか N 件」ボタンが存在しない(前段の容量契約)");
            var btnRect = GlobalRect(moreBtn!);
            var rowChips = ChipRects(window)
                .Where(r => Math.Abs(r.Y + r.Height / 2 - (btnRect.Y + btnRect.Height / 2)) < r.Height)
                .ToList();
            Assert.NotEmpty(rowChips);
            var chip = rowChips[^1];
            Assert.True(Math.Abs(chip.Height - btnRect.Height) <= 1.5,
                $"高さ不一致: チップ {chip.Height:0.0}px / ほかN件 {btnRect.Height:0.0}px(GF-091-01)");
            var chipCenter = chip.Y + chip.Height / 2;
            var btnCenter = btnRect.Y + btnRect.Height / 2;
            Assert.True(Math.Abs(chipCenter - btnCenter) <= 1.5,
                $"垂直中心ずれ: チップ {chipCenter:0.0} / ほかN件 {btnCenter:0.0}(mock=align-items:center・GF-091-01)");
            // ボタン内ラベルも垂直中央(GF-091-01 の実体=コンテンツ上寄り)
            var label = moreBtn!.GetVisualDescendants().OfType<TextBlock>().First();
            var labelRect = GlobalRect(label);
            var labelCenter = labelRect.Y + labelRect.Height / 2;
            Assert.True(Math.Abs(labelCenter - btnCenter) <= 2,
                $"ラベルがボタン内で上寄り: label 中心 {labelCenter:0.0} / ボタン中心 {btnCenter:0.0}(GF-091-01)");
            // ラベルボックスが文字サイズであること(Stretch だと bounds 中心は合うがグリフは上端描画=実機の上寄り)
            Assert.True(labelRect.Height <= 24,
                $"ラベルボックスが縦 Stretch({labelRect.Height:0.0}px)— VerticalContentAlignment 未指定でグリフ上寄り(GF-091-01)");
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    // ---- 少数件の不変 pin(折畳みなし・ボタンなし) ----

    [Fact]
    public async Task 画像タブ_少数チップは折畳まれずほかN件も出ない()
    {
        await SeedImageTabAsync(PrefectureNames47.Take(5).ToArray(), "都道府県");
        await HeadlessApp.Session.Dispatch(async () =>
        {
            var (window, _) = await RenderImageTabAsync("都道府県");
            Assert.Equal(5, ChipRects(window).Count);
            Assert.Null(MoreButton(window));
            Assert.Equal(1, RowsOf(ChipRects(window)));
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    // ---- ヘルパ ----

    private static int RowsOf(IReadOnlyList<Rect> rects)
    {
        var rows = new List<double>();
        foreach (var y in rects.Select(r => r.Y).OrderBy(y => y))
        {
            if (rows.Count == 0 || y - rows[^1] > RowTolerance) rows.Add(y);
        }
        return rows.Count;
    }

    private static List<Rect> ChipRects(Window window) =>
        window.GetVisualDescendants().OfType<Border>()
            .Where(b => b.Classes.Contains("tagChip") && b.IsVisible)
            .Select(GlobalRect)
            .ToList();

    private static List<string> ChipLabels(Window window) =>
        window.GetVisualDescendants().OfType<Border>()
            .Where(b => b.Classes.Contains("tagChip") && b.IsVisible)
            .Select(b => b.GetVisualDescendants().OfType<TextBlock>()
                .Select(t => t.Text).FirstOrDefault(t => !string.IsNullOrEmpty(t)) ?? "")
            .ToList();

    private static Button? MoreButton(Window window) =>
        window.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(b => b.Classes.Contains("chipMore") && b.IsVisible);

    private static Rect? MoreButtonRect(Window window) =>
        MoreButton(window) is { } b ? GlobalRect(b) : null;

    private static int ParseMoreCount(Button moreBtn)
    {
        var text = moreBtn.Content as string
                   ?? moreBtn.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).FirstOrDefault(t => !string.IsNullOrEmpty(t))
                   ?? "";
        var digits = new string(text.Where(char.IsDigit).ToArray());
        Assert.False(digits.Length == 0, $"「ほか N 件」ラベルから N を読めない: '{text}'");
        return int.Parse(digits);
    }

    private static List<Button> PopoverRows(Popup popup) =>
        popup.Child!.GetVisualDescendants().OfType<Button>()
            .Where(b => b.Classes.Contains("chipPopRow") && b.IsVisible)
            .ToList();

    private static string RowLabel(Button row) =>
        string.Join("", row.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));

    private static Rect GlobalRect(Visual v)
    {
        var tb = v.GetTransformedBounds()!.Value;
        return tb.Bounds.TransformToAABB(tb.Transform);
    }

    private static void RunJobs()
    {
        for (var i = 0; i < 10; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    private async Task SeedImageTabAsync(IReadOnlyList<string> values, string tagName)
    {
        await HeadlessApp.Session.Dispatch(() => true, CancellationToken.None); // 先行初期化(ECO-084 教訓)
        _col = new SyncFolder { Id = IdGenerator.NewId(), Name = "C091", Path = @"C:\col-091-" + IdGenerator.NewId() };
        await _db.Folders.AddAsync(_col);

        var tagService = new TagService(_db.Tags);
        var tag = (await tagService.CreateAsync(tagName, TagType.Textual)).Value!;
        Assert.True((await tagService.SetTextualSettingsAsync(tag.Id, values, TagValueDomain.Suggest)).IsSuccess);

        var viewService = new ViewService(_db.Views, _db.Clock);
        _view = (await viewService.CreateAsync("V091")).Value!;
        var node = new HierarchyNode
        {
            Id = IdGenerator.NewId(), ViewId = _view.Id, TagId = tag.Id, Position = 0,
            ExpansionMode = HierarchyExpansionMode.Defined,
        };
        Assert.True((await viewService.SaveHierarchyAsync(_view.Id, [node], null)).IsSuccess);
    }

    private async Task<(Window Window, ImageTabViewModel Vm)> RenderImageTabAsync(string tagName)
    {
        var vm = TestImageTab.NewVm(_db);
        await vm.InitializeAsync(_col.Id);
        await vm.SelectAxisCommand.ExecuteAsync(_view.Id);
        Assert.True(vm.IsViewAxis);
        vm.ClickChipCommand.Execute(vm.Chips.Single(c => c.Label == tagName));

        var window = new Window { Content = new ImageTabView { DataContext = vm }, Width = WindowWidth, Height = 900 };
        window.Show();
        RunJobs();
        return (window, vm);
    }

    private (Window Window, WorkTabViewModel Vm) RenderWorkTab(IReadOnlyList<(string Label, bool Active)> chips, bool withClear = false)
    {
        var vm = NewWorkVm(); // Brush 生成を伴うため UI スレッド内で構築(ECO-084 教訓)
        vm.ShowChips = true;
        vm.ShowChipHint = true;
        vm.ChipHintLabel = "タグで絞り込み";
        if (withClear) vm.Chips.Add(ChipVM.Neutral("クリア", active: false));
        foreach (var (label, active) in chips)
        {
            vm.Chips.Add(ChipVM.Colored(label, label, "#2459cf", 0, active: active, isNav: false));
        }
        vm.WsEmpty = false;

        var window = new Window { Content = new WorkTabView { DataContext = vm }, Width = WindowWidth, Height = 900 };
        window.Show();
        RunJobs();
        return (window, vm);
    }

    private WorkTabViewModel NewWorkVm() =>
        new(new WorkspaceService(_db.Workspaces, _db.Clock), _db.Folders, _db.Tags,
            new SimilaritySearchService(_db.Folders, _db.Images, _db.Features, _db.Similarities, new FakePHashImageReader(), _db.Clock),
            new MergeService(_db.Images, _db.Tags, _db.Merges),
            new TrashService(_db.Images, _db.Folders, new TruePresenceProbe()),
            new StubWindows(), new ImageSorter(), new AppSettings(), TestLoc.Ja());

    private static readonly string[] PrefectureNames47 =
    [
        "北海道", "青森県", "岩手県", "宮城県", "秋田県", "山形県", "福島県",
        "茨城県", "栃木県", "群馬県", "埼玉県", "千葉県", "東京都", "神奈川県",
        "新潟県", "富山県", "石川県", "福井県", "山梨県", "長野県",
        "岐阜県", "静岡県", "愛知県", "三重県",
        "滋賀県", "京都府", "大阪府", "兵庫県", "奈良県", "和歌山県",
        "鳥取県", "島根県", "岡山県", "広島県", "山口県",
        "徳島県", "香川県", "愛媛県", "高知県",
        "福岡県", "佐賀県", "長崎県", "熊本県", "大分県", "宮崎県", "鹿児島県", "沖縄県",
    ];

    private sealed class TruePresenceProbe : IFilePresenceProbe
    {
        public bool Exists(string absoluteImagePath) => true;
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
        public Task<IReadOnlyList<string>?> ShowNumericValueDialogAsync(Tag tag, NumericTagSettings? settings, int selectionCount)
            => Task.FromResult<IReadOnlyList<string>?>(null);
        public Task<NodeConditionResult?> ShowNodeConditionDialogAsync(Tag tag, HierarchyConditionType? currentType, string? currentValueJson)
            => Task.FromResult<NodeConditionResult?>(null);
        public Task ShowRelinkAsync(string folderId) => Task.CompletedTask;
        public void ShowViewer(IReadOnlyList<ImageEntry> ordered, int startIndex) { }
        public Task ShowSimilarSearchAsync(ImageEntry baseImage, IReadOnlyList<ImageEntry> collectionEntries) => Task.CompletedTask;
    }
}
