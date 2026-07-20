using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ViewPrism2.App.Services;
using ViewPrism2.App.ViewModels;
using ViewPrism2.App.Views;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using ViewPrism2.Core.Services.Repair;
using ViewPrism2.Core.Services.Similarity;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// CP-CHIPWRAP-088 拡張(ECO-089・CAD VC-TAG-9 / VC-WORK-1): WrapPanel×横 StackPanel の
/// 同型残存 2 面の折返し回復。面 A=タグパレット候補値行(候補値多数でカードが押し広がり
/// 編集/削除アイコン・チップ末尾が見切れる=maintainer 実機 2026-07-15)。
/// 面 B=作業タブチップ行(ECO-088 是正前の ImageTabView と同一構造の残存)。
/// </summary>
[Trait("cp", "CP-CHIPWRAP-088")]
public sealed class CpUi089ChipWrapTests : IDisposable
{
    private const double WindowWidth = 1366;

    private readonly TempDb _db = new();

    public void Dispose() => _db.Dispose();

    // ---- 面 A: タグパレット候補値行 ----

    [Fact]
    public async Task タグパレットの候補値多数カードは編集削除アイコンとチップ全数が可視のまま折り返す()
    {
        // VC-TAG-9: mock 候補値行= flex-wrap:wrap。是正前=候補値行の無限幅測定がカードを
        // 押し広げ、右端要素(iconBtn・valueChip 末尾)がペイン可視幅の外へ出る(赤)。
        var tagService = new TagService(_db.Tags);
        var tag = (await tagService.CreateAsync("職種", TagType.Textual)).Value!;
        Assert.True((await tagService.SetTextualSettingsAsync(tag.Id,
            ["先鋒", "前衛", "重装", "術師", "狙撃", "医療", "補助", "特殊"], TagValueDomain.Suggest)).IsSuccess);

        await HeadlessApp.Session.Dispatch(async () =>
        {
            var vm = NewTagsVm();
            await vm.Palette.LoadAsync();
            var window = new Window { Content = new TagsTabView { DataContext = vm }, Width = WindowWidth, Height = 900 };
            window.Show();
            RunJobs();

            // 編集/削除アイコン(iconBtn)は完全可視(クリップされない)
            var iconButtons = window.GetVisualDescendants().OfType<Button>()
                .Where(b => b.Classes.Contains("iconBtn"))
                .ToList();
            Assert.Equal(2, iconButtons.Count); // カード 1 枚=編集+削除
            foreach (var btn in iconButtons)
            {
                var (rect, clip) = GlobalRect(btn);
                var visible = rect.Intersect(clip);
                Assert.True(visible.Width >= rect.Width - 0.5 && rect.Right <= WindowWidth + 0.5,
                    $"編集/削除アイコンが見切れる(可視 {visible.Width:0.0}/{rect.Width:0.0}px・right={rect.Right:0.0})— ECO-089 面A");
            }

            // 候補値チップは全数が可視幅内(折返し)
            var chips = window.GetVisualDescendants().OfType<Border>()
                .Where(b => b.Classes.Contains("valueChip") && b.IsVisible)
                .ToList();
            Assert.Equal(8, chips.Count);
            foreach (var chip in chips)
            {
                var (rect, clip) = GlobalRect(chip);
                var visible = rect.Intersect(clip);
                Assert.True(visible.Width >= rect.Width - 0.5 && rect.Right <= WindowWidth + 0.5,
                    $"候補値チップが見切れる(可視 {visible.Width:0.0}/{rect.Width:0.0}px・right={rect.Right:0.0})— ECO-089 面A");
            }
            var rowCount = chips.Select(c => Math.Round(GlobalRect(c).Rect.Y)).Distinct().Count();
            Assert.True(rowCount >= 2, $"候補値 8 件が {rowCount} 行 — 折返しが機能していない(ペイン幅 300)");

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task タグパレットの候補値少数カードは一行のまま変わらない()
    {
        // VC-TAG-9 後段: 少数件(2 件=性別級)の配置は不変(既存 golden の視覚)。
        var tagService = new TagService(_db.Tags);
        var tag = (await tagService.CreateAsync("性別", TagType.Textual)).Value!;
        Assert.True((await tagService.SetTextualSettingsAsync(tag.Id, ["男", "女"], TagValueDomain.Suggest)).IsSuccess);

        await HeadlessApp.Session.Dispatch(async () =>
        {
            var vm = NewTagsVm();
            await vm.Palette.LoadAsync();
            var window = new Window { Content = new TagsTabView { DataContext = vm }, Width = WindowWidth, Height = 900 };
            window.Show();
            RunJobs();

            var chips = window.GetVisualDescendants().OfType<Border>()
                .Where(b => b.Classes.Contains("valueChip") && b.IsVisible)
                .ToList();
            Assert.Equal(2, chips.Count);
            var rowCount = chips.Select(c => Math.Round(GlobalRect(c).Rect.Y)).Distinct().Count();
            Assert.True(rowCount == 1, $"候補値 2 件が {rowCount} 行に割れている(1 行不変の pin)");

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    // ---- 面 B: 作業タブ チップ行 ----

    [Fact]
    public async Task 作業タブの47件チップは折り返しつつ最大2行とほかN件に収まる()
    {
        // VC-WORK-1(=VC-IMG-8 と同文): ECO-088 是正前の ImageTabView と同一構造の残存の回復。
        // since ECO-091(IMG-023A=A-b): 全数直接表示は「最大 2 行+ほか N 件」へ進化(CpUi091 が容量契約を検査)。
        await HeadlessApp.Session.Dispatch(() =>
        {
            var vm = NewWorkVm(); // Brush 生成を伴うため UI スレッド内で構築(ECO-084 教訓)
            vm.ShowChips = true;
            vm.ShowChipHint = true;
            vm.ChipHintLabel = "タグで絞り込み";
            foreach (var name in PrefectureNames47)
            {
                vm.Chips.Add(ChipVM.Colored(name, name, "#2459cf", 0, active: false, isNav: false));
            }
            vm.WsEmpty = false;

            var window = new Window { Content = new WorkTabView { DataContext = vm }, Width = WindowWidth, Height = 900 };
            window.Show();
            RunJobs();

            var rects = window.GetVisualDescendants().OfType<Border>()
                .Where(b => b.Classes.Contains("tagChip") && b.IsVisible)
                .Select(b => GlobalRect(b).Rect)
                .ToList();
            Assert.InRange(rects.Count, 2, 46); // 一部が直接表示・残りは「ほか N 件」(ECO-091)
            foreach (var rect in rects)
            {
                Assert.True(rect.Right <= WindowWidth + 0.5,
                    $"作業タブのチップが可視幅からはみ出す(right={rect.Right:0.0} > {WindowWidth})— ECO-089 面B");
            }
            var rowCount = rects.Select(r => Math.Round(r.Y)).Distinct().Count();
            Assert.Equal(2, rowCount);

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task 作業タブの少数チップは一行のまま変わらない()
    {
        await HeadlessApp.Session.Dispatch(() =>
        {
            var vm = NewWorkVm();
            vm.ShowChips = true;
            foreach (var name in new[] { "北海道", "青森県", "岩手県" })
            {
                vm.Chips.Add(ChipVM.Colored(name, name, "#2459cf", 0, active: false, isNav: false));
            }
            vm.WsEmpty = false;

            var window = new Window { Content = new WorkTabView { DataContext = vm }, Width = WindowWidth, Height = 900 };
            window.Show();
            RunJobs();

            var rects = window.GetVisualDescendants().OfType<Border>()
                .Where(b => b.Classes.Contains("tagChip") && b.IsVisible)
                .Select(b => GlobalRect(b).Rect)
                .ToList();
            Assert.Equal(3, rects.Count);
            Assert.All(rects, r => Assert.True(r.Right <= WindowWidth + 0.5));
            var rowCount = rects.Select(r => Math.Round(r.Y)).Distinct().Count();
            Assert.True(rowCount == 1, $"3 チップが {rowCount} 行に割れている(1 行不変の pin)");

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    // ---- ヘルパ ----

    private static void RunJobs()
    {
        for (var i = 0; i < 8; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>実描画矩形(global)とクリップ矩形。</summary>
    private static (Rect Rect, Rect Clip) GlobalRect(Visual v)
    {
        var tb = v.GetTransformedBounds()!.Value;
        return (tb.Bounds.TransformToAABB(tb.Transform), tb.Clip);
    }

    private TagsTabViewModel NewTagsVm() =>
        new(new ViewService(_db.Views, _db.Clock), new TagService(_db.Tags), _db.Tags,
            TestLoc.Ja(), new StubWindows());

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
