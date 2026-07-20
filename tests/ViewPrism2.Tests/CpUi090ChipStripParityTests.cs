using Avalonia;
using Avalonia.Controls;
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
/// CP-CHIPWRAP-088 共通ベクトル拡張(ECO-090): 画像タブ/作業タブのチップ行は
/// 同一意味論の同期実装(E-UI-AXIS-NAV-040⇔E-UI-WORKSPACE-043)— DRY 統合までの暫定統制として
/// **同一のテストベクトル**(1 件/5 件/折返し境界/47 件/長ラベル/狭幅)を両タブへ流し、
/// 受入検査を共通化する(コードは共通化しない=ECO-089 残置の尊重)。
/// ECO-088 教訓 2「fixture 規模は起票動機の規模で張る」の次元一般化(規模×ラベル長×幅)。
/// </summary>
[Trait("cp", "CP-CHIPWRAP-088")]
public sealed class CpUi090ChipStripParityTests : IDisposable
{
    private readonly TempDb _db = new();
    private SyncFolder _col = null!;
    private View _view = null!;

    public void Dispose() => _db.Dispose();

    // ---- 共通ベクトル(rowContract: 1=ちょうど1行 / 2=2行以上 / 0=行数不問・全数可視のみ) ----

    public static TheoryData<string, int, double, int> CommonVectors => new()
    {
        { "v01_単一", 1, 1366, 1 },
        { "v05_mockデモ規模", 5, 1366, 1 },
        { "v47_起票動機規模", 47, 1366, 2 },
        { "v47_狭幅900", 47, 900, 2 },
        { "v長ラベル込み8件", -1, 1366, 0 },
    };

    [Theory]
    [MemberData(nameof(CommonVectors))]
    public async Task 画像タブ_チップ行は共通ベクトルで容量と到達の契約を満たす(string vector, int count, double width, int rowContract)
    {
        var labels = ResolveLabels(count);
        await SeedImageTabAsync(labels, "共通" + vector);
        await HeadlessApp.Session.Dispatch(async () =>
        {
            var (rects, moreN) = await RenderImageTabChipsAsync("共通" + vector, width);
            AssertChipContract("画像タブ/" + vector, rects, moreN, labels.Count, width, rowContract);
            return true;
        }, CancellationToken.None);
    }

    [Theory]
    [MemberData(nameof(CommonVectors))]
    public async Task 作業タブ_チップ行は共通ベクトルで容量と到達の契約を満たす(string vector, int count, double width, int rowContract)
    {
        var labels = ResolveLabels(count);
        await HeadlessApp.Session.Dispatch(() =>
        {
            var (rects, moreN) = RenderWorkTabChips(labels, width);
            AssertChipContract("作業タブ/" + vector, rects, moreN, labels.Count, width, rowContract);
            return true;
        }, CancellationToken.None);
    }

    // ---- 折畳み境界(実行時較正・ECO-091 で容量契約へ改訂):
    //      47 件折畳みの可視件数 k を測り、k 件だけなら「ほか N 件」なしで 2 行以内に全数可視 ----

    [Fact]
    public async Task 画像タブ_折畳み境界の両側でほかN件の有無が変わる()
    {
        await SeedImageTabAsync(PrefectureNames47, "境界47");
        var visibleAtFold = 0;
        await HeadlessApp.Session.Dispatch(async () =>
        {
            var (rects, moreN) = await RenderImageTabChipsAsync("境界47", 1366);
            Assert.True(moreN > 0, "47 件で折畳みが発生していない(較正の前提)");
            visibleAtFold = rects.Count;
            return true;
        }, CancellationToken.None);
        Assert.InRange(visibleAtFold, 2, 46); // 較正値が退化していない

        await SeedImageTabAsync(PrefectureNames47.Take(visibleAtFold).ToArray(), "境界K");
        await HeadlessApp.Session.Dispatch(async () =>
        {
            var (rects, moreN) = await RenderImageTabChipsAsync("境界K", 1366);
            Assert.Equal(0, moreN); // 境界内=折畳みなし(全数可視)
            Assert.Equal(visibleAtFold, rects.Count);
            Assert.InRange(RowCount(rects), 1, 2);
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task 作業タブ_折畳み境界の両側でほかN件の有無が変わる()
    {
        await HeadlessApp.Session.Dispatch(() =>
        {
            var (all, moreN47) = RenderWorkTabChips(PrefectureNames47, 1366);
            Assert.True(moreN47 > 0, "47 件で折畳みが発生していない(較正の前提)");
            var k = all.Count;
            Assert.InRange(k, 2, 46);

            var (atBoundary, moreNk) = RenderWorkTabChips(PrefectureNames47.Take(k).ToArray(), 1366);
            Assert.Equal(0, moreNk); // 境界内=折畳みなし(全数可視)
            Assert.Equal(k, atBoundary.Count);
            Assert.InRange(RowCount(atBoundary), 1, 2);
            return true;
        }, CancellationToken.None);
    }

    // ---- 共通アサーション(ECO-091 容量契約: 可視+ほかN件=全数・最大 2 行) ----

    private static void AssertChipContract(string face, IReadOnlyList<Rect> rects, int moreN, int expectedCount, double width, int rowContract)
    {
        Assert.Equal(expectedCount, rects.Count + moreN); // 可視+非表示 N=全数(到達性の会計)
        foreach (var rect in rects)
        {
            Assert.True(rect.Right <= width + 0.5,
                $"{face}: チップが可視幅からはみ出す(right={rect.Right:0.0} > {width})— 折返し契約違反(CP-CHIPWRAP-088/ECO-090)");
        }
        var rows = RowCount(rects);
        if (rowContract == 1)
        {
            Assert.Equal(0, moreN); // 少数は畳まれない
            Assert.True(rows == 1, $"{face}: {expectedCount} チップが {rows} 行(1 行不変の pin)");
        }
        else if (rowContract == 2)
        {
            Assert.True(moreN > 0, $"{face}: {expectedCount} 件で「ほか N 件」が出ていない(容量契約=ECO-091)");
            Assert.True(rows <= 2, $"{face}: チップ領域が {rows} 行 — 最大 2 行(IMG-023A=A-b)を超過");
        }
        else
        {
            Assert.True(rows <= 2, $"{face}: チップ領域が {rows} 行 — 最大 2 行(IMG-023A=A-b)を超過");
        }
    }

    private static int RowCount(IReadOnlyList<Rect> rects) =>
        rects.Select(r => Math.Round(r.Y)).Distinct().Count();

    /// <summary>「ほか N 件」の N(非表示件数)。ボタンがなければ 0。</summary>
    private static int MoreCount(Window window)
    {
        var btn = window.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(b => b.Classes.Contains("chipMore") && b.IsVisible);
        if (btn is null) return 0;
        var text = btn.Content as string ?? "";
        var digits = new string(text.Where(char.IsDigit).ToArray());
        return digits.Length == 0 ? 0 : int.Parse(digits);
    }

    private static IReadOnlyList<string> ResolveLabels(int count) =>
        count == -1 ? LongLabels : PrefectureNames47.Take(count).ToArray();

    // ---- 画像タブ(実パイプライン: textual タグ defined 展開・CpUi088 と同系) ----

    private async Task SeedImageTabAsync(IReadOnlyList<string> values, string tagName)
    {
        await HeadlessApp.Session.Dispatch(() => true, CancellationToken.None); // 先行初期化(ECO-084 教訓)
        _col = new SyncFolder { Id = IdGenerator.NewId(), Name = "C" + tagName, Path = @"C:\col-090-" + IdGenerator.NewId() };
        await _db.Folders.AddAsync(_col);

        var tagService = new TagService(_db.Tags);
        var tag = (await tagService.CreateAsync(tagName, TagType.Textual)).Value!;
        Assert.True((await tagService.SetTextualSettingsAsync(tag.Id, values, TagValueDomain.Suggest)).IsSuccess);

        var viewService = new ViewService(_db.Views, _db.Clock);
        _view = (await viewService.CreateAsync("V090" + tagName)).Value!;
        var node = new HierarchyNode
        {
            Id = IdGenerator.NewId(), ViewId = _view.Id, TagId = tag.Id, Position = 0,
            ExpansionMode = HierarchyExpansionMode.Defined,
        };
        Assert.True((await viewService.SaveHierarchyAsync(_view.Id, [node], null)).IsSuccess);
    }

    private async Task<(List<Rect> Rects, int MoreN)> RenderImageTabChipsAsync(string tagName, double width)
    {
        var vm = TestImageTab.NewVm(_db);
        await vm.InitializeAsync(_col.Id);
        await vm.SelectAxisCommand.ExecuteAsync(_view.Id);
        Assert.True(vm.IsViewAxis);
        vm.ClickChipCommand.Execute(vm.Chips.Single(c => c.Label == tagName));

        var window = new Window { Content = new ImageTabView { DataContext = vm }, Width = width, Height = 900 };
        window.Show();
        RunJobs();

        var rects = window.GetVisualDescendants().OfType<Border>()
            .Where(b => b.Classes.Contains("tagChip") && b.IsVisible)
            .Select(GlobalRect)
            .ToList();
        var moreN = MoreCount(window);
        window.Close();
        return (rects, moreN);
    }

    // ---- 作業タブ(ChipVM 直積み・CpUi089 面 B と同系) ----

    private (List<Rect> Rects, int MoreN) RenderWorkTabChips(IReadOnlyList<string> labels, double width)
    {
        var vm = NewWorkVm(); // Brush 生成を伴うため UI スレッド内で構築(ECO-084 教訓)
        vm.ShowChips = true;
        vm.ShowChipHint = true;
        vm.ChipHintLabel = "タグで絞り込み";
        foreach (var name in labels)
        {
            vm.Chips.Add(ChipVM.Colored(name, name, "#2459cf", 0, active: false, isNav: false));
        }
        vm.WsEmpty = false;

        var window = new Window { Content = new WorkTabView { DataContext = vm }, Width = width, Height = 900 };
        window.Show();
        RunJobs();

        var rects = window.GetVisualDescendants().OfType<Border>()
            .Where(b => b.Classes.Contains("tagChip") && b.IsVisible)
            .Select(GlobalRect)
            .ToList();
        var moreN = MoreCount(window);
        window.Close();
        return (rects, moreN);
    }

    // ---- ヘルパ ----

    private static void RunJobs()
    {
        for (var i = 0; i < 8; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static Rect GlobalRect(Visual v)
    {
        var tb = v.GetTransformedBounds()!.Value;
        return tb.Bounds.TransformToAABB(tb.Transform);
    }

    private WorkTabViewModel NewWorkVm() =>
        new(new WorkspaceService(_db.Workspaces, _db.Clock), _db.Folders, _db.Tags,
            new SimilaritySearchService(_db.Folders, _db.Images, _db.Features, _db.Similarities, new FakePHashImageReader(), _db.Clock),
            new MergeService(_db.Images, _db.Tags, _db.Merges),
            new TrashService(_db.Images, _db.Folders, new TruePresenceProbe()),
            new StubWindows(), new ImageSorter(), new AppSettings(), TestLoc.Ja());

    private static readonly string[] LongLabels =
    [
        "スタジオ第2撮影ブース（窓際・自然光・レフ板あり）",
        "屋外・河川敷の午後逆光（ゴールデンアワー手前）",
        "市街地・夜景",
        "海岸",
        "山間部・霧",
        "室内・蛍光灯",
        "体育館",
        "地下街",
    ];

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
