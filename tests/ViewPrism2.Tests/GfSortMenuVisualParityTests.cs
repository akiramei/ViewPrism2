using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ViewPrism2.App.Services;
using ViewPrism2.App.ViewModels;
using ViewPrism2.App.Views;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using ViewPrism2.Core.Services.Repair;
using ViewPrism2.Core.Services.Similarity;
using ViewPrism2.Infrastructure.Imaging;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// ECO-109(ファイル一覧 並び替え UI の視覚追随=mock 精緻化改版 2026-07-17 への転写):
/// CAD file_list.md の視覚契約 VC-FL-1(並び替えメニュー)/VC-FL-2(ツールバー)から先行生成した
/// 視覚 probe(R7・GF 後追い禁止=GF-073 様式)。原器= captures/file_list/SORT-menu.png / TB-grid.png。
/// VC-FL-1: ①幅252 ③候補行=先頭20pxアイコン列(基本=列グリフ灰地/タグ=色ドット9px)+種別チップ配色
/// (基本灰 #F0F2F6・数値緑 #EAFAF3・テキスト青 #EAF1FE・シンプル紫 #F3EEFE) ④アクティブ行太字 #2459CF
/// +行末矢印 ⑤昇順/降順セグメント(アクティブ青地 #EAF1FE/非アクティブ灰地 #F4F6FA)。
/// VC-FL-2: ①チップ文言青 #2459CF+radius9 ③チップは isSorted のみ条件=リスト/アイコン共通(退化禁止)
/// ④バッジ太字・開時トリガー枠 #CFD6E1。
/// 並び替えメニューは ImageTab/WorkTab で同型複製(共通部品でない)のため両面を検査する(read-across)。
/// </summary>
[Trait("cp", "CP-UI-G2")]
public sealed class GfSortMenuVisualParityTests : IDisposable
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

    private static readonly Color MockBasicGlyphBg = Color.Parse("#F0F2F6");
    private static readonly Color MockNumChipBg = Color.Parse("#EAFAF3");
    private static readonly Color MockTextChipBg = Color.Parse("#EAF1FE");
    private static readonly Color MockSimpleChipBg = Color.Parse("#F3EEFE");
    private static readonly Color MockActiveFg = Color.Parse("#2459CF");
    private static readonly Color MockSegActiveBg = Color.Parse("#EAF1FE");
    private static readonly Color MockSegInactiveBg = Color.Parse("#F4F6FA");
    private static readonly Color MockOpenTriggerBorder = Color.Parse("#CFD6E1");

    /// <summary>メニュー候補 fixture(基本 active+数値/テキスト/シンプル タグ列)を VM へ直接投入する。</summary>
    private static void SeedSortColumns(System.Collections.ObjectModel.ObservableCollection<SortOptionVM> target)
    {
        target.Clear();
        target.Add(new SortOptionVM("name", "名前", "基本", null, true, 0, ListCellKind.BasicName));
        target.Add(new SortOptionVM("t-num", "評価", "数値", "#F0B429", false, 0, ListCellKind.Num));
        target.Add(new SortOptionVM("t-text", "職業", "テキスト", "#4C8BF5", false, 0, ListCellKind.Text));
        target.Add(new SortOptionVM("t-simple", "ガチャ", "シンプル", "#8B5CF6", false, 0, ListCellKind.Simple));
    }

    private static Border SortMenuPanel(Window window)
    {
        // 並び替え Popup を特定(候補行 sortOpt を持つ popupMenu)。開いた状態で呼ぶこと。
        var panel = window.GetLogicalDescendants().OfType<Popup>()
            .Select(p => p.Child).OfType<Border>()
            .FirstOrDefault(b => b.GetLogicalDescendants().OfType<Button>().Any(x => x.Classes.Contains("sortOpt")));
        Assert.True(panel is not null, "並び替えメニューの Popup(sortOpt 行を持つ popupMenu)が見つからない");
        return panel!;
    }

    private static Color BorderBg(Border b) =>
        Assert.IsAssignableFrom<ISolidColorBrush>(b.Background).Color;

    private static Border ChipOf(Border menu, string chipText)
    {
        var tb = menu.GetLogicalDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Text == chipText);
        Assert.True(tb is not null, $"種別チップ文言「{chipText}」が見つからない");
        var chip = tb!.FindLogicalAncestorOfType<Border>();
        Assert.True(chip is not null, $"種別チップ「{chipText}」の Border が無い");
        return chip!;
    }

    [Fact]
    public async Task VCFL1_並び替えメニューは幅252と列グリフと種別チップ配色とアクティブ強調とセグメント配色を備える()
    {
        var vm = TestImageTab.NewVm(_db);
        await Session.Dispatch(() =>
        {
            var window = new Window { Content = new ImageTabView { DataContext = vm }, Width = 1200, Height = 800 };
            window.Show();
            RunJobs();
            try
            {
                vm.SetGridCommand.Execute(null);
                SeedSortColumns(vm.SortColumns);
                if (!vm.SortMenuOpen) vm.ToggleSortMenuCommand.Execute(null);
                RunJobs();

                var menu = SortMenuPanel(window);

                // VC-FL-1①: ポップオーバー幅 252(mock 実測。現行 248 は乖離)
                Assert.True(menu.Width == 252, $"VC-FL-1①: メニュー幅が {menu.Width}(期待 252)");

                // VC-FL-1③: 基本情報行の先頭 20px 列グリフ(灰地 #F0F2F6 ボックス)
                var rows = menu.GetLogicalDescendants().OfType<Button>()
                    .Where(b => b.Classes.Contains("sortOpt")).ToList();
                Assert.True(rows.Count == 4, $"候補行が {rows.Count}(期待 4)");
                var basicRow = rows.First(r => r.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text == "名前"));
                var glyphBox = basicRow.GetLogicalDescendants().OfType<Border>()
                    .FirstOrDefault(b => b.Width == 20 && b.Background is ISolidColorBrush s && s.Color == MockBasicGlyphBg);
                Assert.True(glyphBox is not null, "VC-FL-1③: 基本情報行の列グリフ灰ボックス(20px・#F0F2F6)が無い");

                // VC-FL-1③: タグ行の色ドットは 9px(mock。現行 10px は乖離)
                var numRow = rows.First(r => r.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text == "評価"));
                var dot = numRow.GetLogicalDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>().FirstOrDefault();
                Assert.True(dot is not null && dot.Width == 9, $"VC-FL-1③: タグ色ドットが 9px でない({dot?.Width})");

                // VC-FL-1③: 種別チップの kind 別配色(基本灰/数値緑/テキスト青/シンプル紫)
                Assert.True(BorderBg(ChipOf(menu, "基本")) == MockBasicGlyphBg, "VC-FL-1③: 基本チップが灰 #F0F2F6 でない");
                Assert.True(BorderBg(ChipOf(menu, "数値")) == MockNumChipBg, "VC-FL-1③: 数値チップが緑 #EAFAF3 でない");
                Assert.True(BorderBg(ChipOf(menu, "テキスト")) == MockTextChipBg, "VC-FL-1③: テキストチップが青 #EAF1FE でない");
                Assert.True(BorderBg(ChipOf(menu, "シンプル")) == MockSimpleChipBg, "VC-FL-1③: シンプルチップが紫 #F3EEFE でない");

                // VC-FL-1③/後段検証: 候補行は固定高 38px(ECO-040 規約=固定高は VerticalContentAlignment 明示とセット)
                Assert.All(rows, r => Assert.True(r.Height == 38, $"VC-FL-1③: 候補行 Height が {r.Height}(期待 38)"));

                // VC-FL-1④: アクティブ行(名前)のラベルが太字+#2459CF、行末に方向矢印
                Assert.True(basicRow.Classes.Contains("active"), "VC-FL-1④: アクティブ行に active クラスが無い");
                var label = basicRow.GetLogicalDescendants().OfType<TextBlock>().First(t => t.Text == "名前");
                Assert.True(label.FontWeight >= FontWeight.Bold, $"VC-FL-1④: アクティブラベルが太字でない({label.FontWeight})");
                var labelFg = Assert.IsAssignableFrom<ISolidColorBrush>(label.Foreground).Color;
                Assert.True(labelFg == MockActiveFg, $"VC-FL-1④: アクティブラベルが #2459CF でない({labelFg})");
                var arrow = basicRow.GetLogicalDescendants().OfType<PathIcon>().FirstOrDefault(p => p.IsVisible);
                Assert.True(arrow is not null, "VC-FL-1④: アクティブ行の方向矢印が無い");

                // GF-109-01(maintainer 実機所見 2026-07-18): 矢印は行末(右端)固定=mock margin-left:auto。
                // 実レイアウトで矢印右端と行右端の距離を実測(存在検査は Left 寄せ縮退を素通りした)
                var arrowRight = arrow!.TranslatePoint(new Point(arrow.Bounds.Width, 0), basicRow);
                Assert.True(arrowRight is not null, "GF-109-01: 矢印の座標変換に失敗");
                var gap = basicRow.Bounds.Width - arrowRight!.Value.X;
                Assert.True(gap is >= 0 and <= 16,
                    $"GF-109-01: 方向矢印が行末に無い(行幅 {basicRow.Bounds.Width} − 矢印右端 {arrowRight.Value.X} = 右余白 {gap}。期待=右 Padding 10 前後)");

                // VC-FL-1⑤: 昇順/降順セグメント=アクティブ青地 #EAF1FE・非アクティブ灰地 #F4F6FA(既定=昇順)
                var asc = menu.GetLogicalDescendants().OfType<Button>()
                    .First(b => b.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text == "昇順"));
                var desc = menu.GetLogicalDescendants().OfType<Button>()
                    .First(b => b.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text == "降順"));
                Assert.True(SegBg(asc) == MockSegActiveBg, $"VC-FL-1⑤: アクティブ昇順が青地 #EAF1FE でない({SegBg(asc)})");
                Assert.True(SegBg(desc) == MockSegInactiveBg, $"VC-FL-1⑤: 非アクティブ降順が灰地 #F4F6FA でない({SegBg(desc)})");
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>セグメントボタンの実背景(テンプレート ContentPresenter 優先・未適用時は Button.Background)。</summary>
    private static Color SegBg(Button b)
    {
        var presenter = b.GetVisualDescendants().OfType<ContentPresenter>().FirstOrDefault();
        var brush = presenter?.Background ?? b.Background;
        return brush is ISolidColorBrush s ? s.Color : Colors.Transparent;
    }

    [Fact]
    public async Task VCFL2_ソートチップは青文字太字radius9でバッジ太字と開時トリガー枠を備えチップはリストでも出る()
    {
        var vm = TestImageTab.NewVm(_db);
        await Session.Dispatch(() =>
        {
            var window = new Window { Content = new ImageTabView { DataContext = vm }, Width = 1200, Height = 800 };
            window.Show();
            RunJobs();
            try
            {
                vm.SetGridCommand.Execute(null);
                vm.SelectColumnSortCommand.Execute("name"); // 名前で昇順ソート=isSorted
                RunJobs();
                Assert.True(vm.ShowSortChip, "前提: ソート中に ShowSortChip が立たない");

                // VC-FL-2①: チップ=青地 #EAF1FE/境界 #CFE0FC(既適合)+文言 #2459CF SemiBold+radius 9
                //(是正で mock 同型のチップ全体クリック解除=Button 化・x:Name=SortChip。probe 初版の Border 探索から強化)
                var chip = window.GetLogicalDescendants().OfType<Button>().FirstOrDefault(b => b.Name == "SortChip");
                Assert.True(chip is not null, "VC-FL-2①: ソートチップ(SortChip)が見つからない");
                // 文言内容は FL-003 の VM テストが担保済み。ここは視覚言語(色・太さ)のみ=fixture の
                // コレクション未選択では ColumnSortLabel が空のため、構造でラベル TextBlock を特定する。
                var chipText = chip!.GetLogicalDescendants().OfType<TextBlock>().FirstOrDefault();
                Assert.True(chipText is not null, "VC-FL-2①: ソートチップ文言 TextBlock が見つからない");
                var chipFg = Assert.IsAssignableFrom<ISolidColorBrush>(chipText!.Foreground).Color;
                Assert.True(chipFg == MockActiveFg, $"VC-FL-2①: チップ文言が #2459CF でない({chipFg})");
                Assert.True(chipText.FontWeight >= FontWeight.SemiBold, $"VC-FL-2①: チップ文言が太字でない({chipText.FontWeight})");
                Assert.True(chip.CornerRadius == new CornerRadius(9), $"VC-FL-2①: チップ角丸が {chip.CornerRadius}(期待 9)");
                var chipBg = chip.Background is ISolidColorBrush cb ? cb.Color : Colors.Transparent;
                Assert.True(chipBg == MockTextChipBg, $"VC-FL-2①: チップ地が #EAF1FE でない({chipBg})");

                // VC-FL-2④: ソート列名バッジ=太字(モック fontWeight 700)
                var badge = window.GetVisualDescendants().OfType<TextBlock>()
                    .First(t => t.Text == vm.SortButtonBadge && t.FindAncestorOfType<Button>()?.Name == "SortTrigger");
                Assert.True(badge.FontWeight >= FontWeight.Bold, $"VC-FL-2④: バッジが太字でない({badge.FontWeight})");

                // VC-FL-2④: メニュー開時はトリガー枠が濃色 #CFD6E1
                if (!vm.SortMenuOpen) vm.ToggleSortMenuCommand.Execute(null);
                RunJobs();
                var trigger = window.GetVisualDescendants().OfType<Button>().First(b => b.Name == "SortTrigger");
                var borderColor = trigger.BorderBrush is ISolidColorBrush s ? s.Color : Colors.Transparent;
                Assert.True(borderColor == MockOpenTriggerBorder, $"VC-FL-2④: 開時トリガー枠が #CFD6E1 でない({borderColor})");
                vm.ToggleSortMenuCommand.Execute(null);
                RunJobs();

                // VC-FL-2③(退化禁止): チップは isSorted のみが条件=リスト表示でも出る
                vm.SetListCommand.Execute(null);
                RunJobs();
                Assert.True(vm.ShowSortChip, "VC-FL-2③: リスト表示でソートチップが消えた(isSorted のみが条件)");
                Assert.True(!vm.SortMenuOpen, "FL-003: リスト切替で並び替えメニューが閉じない");
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    private sealed class StubWindowService : IWindowService
    {
        public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(true);
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

    [Fact]
    public async Task VCFL1_作業タブの同型メニューにも同じ視覚が乗る()
    {
        var vm = new WorkTabViewModel(
            new WorkspaceService(_db.Workspaces, _db.Clock), _db.Folders, _db.Tags,
            new SimilaritySearchService(_db.Folders, _db.Images, _db.Features, _db.Similarities, new FakePHashImageReader(), _db.Clock),
            new MergeService(_db.Images, _db.Tags, _db.Merges),
            new TrashService(_db.Images, _db.Folders, new FilePresenceProbe()),
            new StubWindowService(), new ImageSorter(), new AppSettings(),
            TestLoc.Ja());
        await Session.Dispatch(() =>
        {
            var window = new Window { Content = new WorkTabView { DataContext = vm }, Width = 1200, Height = 800 };
            window.Show();
            RunJobs();
            try
            {
                vm.SetGridCommand.Execute(null);
                // ワークスペース未選択では SortColumns が空のため、基本 3 列(FL-003 read-across の候補集合)を直接投入
                vm.SortColumns.Clear();
                vm.SortColumns.Add(new SortOptionVM("name", "名前", "基本", null, true, 0, ListCellKind.BasicName));
                vm.SortColumns.Add(new SortOptionVM("size", "サイズ", "基本", null, false, 0, ListCellKind.BasicSize));
                vm.SortColumns.Add(new SortOptionVM("modified_date", "更新日", "基本", null, false, 0, ListCellKind.BasicDate));
                if (!vm.SortMenuOpen) vm.ToggleSortMenuCommand.Execute(null);
                RunJobs();

                var menu = SortMenuPanel(window);
                Assert.True(menu.Width == 252, $"WorkTab: メニュー幅が {menu.Width}(期待 252)");

                // 基本 3 列限定=全行が列グリフ灰ボックスを持つ(FL-003 read-across)
                var rows = menu.GetLogicalDescendants().OfType<Button>()
                    .Where(b => b.Classes.Contains("sortOpt")).ToList();
                Assert.True(rows.Count == 3, $"WorkTab: 候補行が {rows.Count}(期待 基本3列)");
                Assert.All(rows, r =>
                {
                    var glyphBox = r.GetLogicalDescendants().OfType<Border>()
                        .FirstOrDefault(b => b.Width == 20 && b.Background is ISolidColorBrush s && s.Color == MockBasicGlyphBg);
                    Assert.True(glyphBox is not null, "WorkTab: 基本行の列グリフ灰ボックスが無い");
                });
                Assert.True(BorderBg(ChipOf(menu, "基本")) == MockBasicGlyphBg, "WorkTab: 基本チップが灰 #F0F2F6 でない");

                // セグメント配色(既定=昇順アクティブ)
                var asc = menu.GetLogicalDescendants().OfType<Button>()
                    .First(b => b.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text == "昇順"));
                Assert.True(SegBg(asc) == MockSegActiveBg, $"WorkTab: アクティブ昇順が青地 #EAF1FE でない({SegBg(asc)})");
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }
}
