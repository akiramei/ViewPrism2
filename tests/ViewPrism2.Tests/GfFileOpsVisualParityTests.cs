using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.Media;
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
/// ECO-112(画像タブ ファイル操作モード): CAD image_tab.md の視覚契約 VC-IMG-11(⋯メニュー)/
/// VC-IMG-12(モード中ツールバーの出し分け)/VC-IMG-13(選択視覚=チェック+青リング・番号バッジなし)から
/// 先行生成した視覚 probe(R7・GF 後追い禁止=GF-073 様式)。
/// 原器= captures/image_tab/{MENU-fileops,TB-fileops-none,TB-fileops-single,TB-fileops-multi,full-fileops}.png。
/// VC-IMG-11: ①ポップオーバー幅 208 ②項目順=ファイル操作→修復→削除→ゴミ箱 ③フォルダグリフ
/// (stroke #5b6473)+ラベル 13.5px/500・行高 42。
/// VC-IMG-12: ①0件=終了のみ(ラベル維持) ②1件=パスをコピー(青 #2F6BED 塗り・バッジ 1)+場所を開く
/// ③2件以上=コピーのみ ④バッジ N=選択枚数。
/// VC-IMG-13: 選択チェック(青塗り+白✓・番号バッジなし)+セル青リング。リスト行は行ハイライト
/// (IMG-026④ 裁定=タグ編集と同型)。
/// </summary>
[Trait("cp", "CP-UI-G1")]
public sealed class GfFileOpsVisualParityTests : IDisposable
{
    private static Avalonia.Headless.HeadlessUnitTestSession Session => HeadlessApp.Session;

    private readonly TempDb _db = new();
    private SyncFolder _col = null!;

    public void Dispose() => _db.Dispose();

    // ECO-122: 期待値は部品表写像(RegistryContract)参照へ移行(値不変=移行前後で同一判定)。
    // MockBlue= color.accent(VC-IMG-12② 主 CTA 塗り/VC-IMG-13 チェック= CMP-007 check)・
    // MockMenuGlyph= color.text.secondary(VC-IMG-11③ フォルダグリフ stroke)。
    private static readonly Color MockBlue = RegistryContract.ColorAccent;
    private static readonly Color MockMenuGlyph = RegistryContract.ColorTextSecondary;

    private static void RunJobs()
    {
        for (var i = 0; i < 8; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    private async Task<ImageTabViewModel> NewVmWithImagesAsync(params string[] names)
    {
        _col = new SyncFolder { Id = IdGenerator.NewId(), Name = "C", Path = @"C:\col" };
        await _db.Folders.AddAsync(_col);
        foreach (var name in names)
        {
            await _db.Images.AddAsync(new ImageRecord
            {
                Id = IdGenerator.NewId(),
                SyncFolderId = _col.Id,
                RelativePath = name,
                FileName = name,
                FileSize = 10,
                Hash = new string('0', 64),
                Status = ImageStatus.Normal,
                CreatedDate = "2026-06-11T00:00:00.000Z",
                ModifiedDate = "2026-06-11T00:00:00.000Z",
            });
        }
        var vm = TestImageTab.NewVm(_db);
        await vm.InitializeAsync(_col.Id);
        return vm;
    }

    private static ImageItemVM Item(ImageTabViewModel vm, string name)
        => vm.Items.Single(i => !i.IsFolder && i.Name == name);

    private static TextBlock? FindText(Visual root, string text)
        => root.GetLogicalDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Text == text);

    private static Button ButtonWithLabel(Window window, string label)
    {
        var tb = window.GetLogicalDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Text == label);
        Assert.True(tb is not null, $"ラベル「{label}」のボタンが見つからない");
        var btn = tb!.FindLogicalAncestorOfType<Button>();
        Assert.True(btn is not null, $"「{label}」の Button 祖先が無い");
        return btn!;
    }

    private static bool HasVisibleButton(Window window, string label)
    {
        var tb = window.GetLogicalDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Text == label);
        var btn = tb?.FindLogicalAncestorOfType<Button>();
        return btn is not null && btn.IsEffectivelyVisible;
    }

    [Fact]
    public async Task VCIMG11_メニューにファイル操作行があり幅208で修復より先に並ぶ()
    {
        var vm = await NewVmWithImagesAsync("a.jpg");
        await Session.Dispatch(() =>
        {
            var window = new Window { Content = new ImageTabView { DataContext = vm }, Width = 1200, Height = 800 };
            window.Show();
            RunJobs();
            try
            {
                vm.ToggleMoreMenuCommand.Execute(null);
                RunJobs();

                // ⋯ メニューの Popup(修復 menuRow を持つ popupMenu)を特定
                var menu = window.GetLogicalDescendants().OfType<Popup>()
                    .Select(p => p.Child).OfType<Border>()
                    .FirstOrDefault(b => FindText(b, "修復") is not null);
                Assert.True(menu is not null, "⋯ メニューの Popup が見つからない");

                // VC-IMG-11①: ポップオーバー幅= CMP-006 インスタンス契約値(ECO-122 で写像参照へ)
                Assert.True(menu!.Width == RegistryContract.MenuWidthMore,
                    $"VC-IMG-11①: メニュー幅が {menu.Width}(契約 {RegistryContract.MenuWidthMore})");

                // VC-IMG-11②③: 「ファイル操作」行が存在し、修復より先に並ぶ
                var fileOpsText = FindText(menu, "ファイル操作");
                Assert.True(fileOpsText is not null, "VC-IMG-11③: 「ファイル操作」行が無い");
                var rows = menu.GetLogicalDescendants().OfType<Button>()
                    .Where(b => b.Classes.Contains("menuRow")).ToList();
                int idxFileOps = rows.FindIndex(r => FindText(r, "ファイル操作") is not null);
                int idxRepair = rows.FindIndex(r => FindText(r, "修復") is not null);
                Assert.True(idxFileOps >= 0 && idxRepair >= 0 && idxFileOps < idxRepair,
                    $"VC-IMG-11②: 項目順が ファイル操作({idxFileOps})→修復({idxRepair}) でない");

                // VC-IMG-11③: フォルダグリフ(stroke #5b6473)+ラベル 13.5px/500・行高 42
                var row = rows[idxFileOps];
                var glyph = row.GetLogicalDescendants().OfType<PathIcon>().FirstOrDefault();
                Assert.True(glyph is not null, "VC-IMG-11③: フォルダグリフが無い");
                var glyphFg = Assert.IsAssignableFrom<ISolidColorBrush>(glyph!.Foreground).Color;
                Assert.True(glyphFg == MockMenuGlyph, $"VC-IMG-11③: グリフ色が契約 {RegistryContract.ColorTextSecondary} でない({glyphFg})");
                Assert.True(fileOpsText!.FontSize == 13.5, $"VC-IMG-11③: ラベルが 13.5px でない({fileOpsText.FontSize})");
                Assert.True(fileOpsText.FontWeight == FontWeight.Medium, $"VC-IMG-11③: ラベルが 500 でない({fileOpsText.FontWeight})");
                Assert.True(row.Bounds.Height == RegistryContract.MenuRowHeightMore,
                    $"VC-IMG-11③: 行高が {row.Bounds.Height}(契約 {RegistryContract.MenuRowHeightMore})");
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task VCIMG12_ツールバーは選択件数でボタンを出し分けコピーは青塗りバッジつき()
    {
        var vm = await NewVmWithImagesAsync("a.jpg", "b.jpg");
        await Session.Dispatch(() =>
        {
            var window = new Window { Content = new ImageTabView { DataContext = vm }, Width = 1200, Height = 800 };
            window.Show();
            RunJobs();
            try
            {
                vm.EnterFileOpsCommand.Execute(null);
                RunJobs();

                // VC-IMG-12①: 0 件=「ファイル操作を終了」のみ(✕グリフ+ラベル=狭幅でも畳まない)
                var exitBtn = ButtonWithLabel(window, "ファイル操作を終了");
                Assert.True(exitBtn.IsEffectivelyVisible, "VC-IMG-12①: 終了ボタンが可視でない");
                Assert.True(exitBtn.GetLogicalDescendants().OfType<PathIcon>().Any(), "VC-IMG-12①: 終了の✕グリフが無い");
                Assert.False(HasVisibleButton(window, "パスをコピー"), "VC-IMG-12①: 0件でコピーが見えている");
                Assert.False(HasVisibleButton(window, "ファイルの場所を開く"), "VC-IMG-12①: 0件で場所を開くが見えている");

                // VC-IMG-12②: 1 件=パスをコピー(青塗り・バッジ 1)+ファイルの場所を開く
                vm.HandleItemClick(Item(vm, "a.jpg"), ctrl: false, shift: false);
                RunJobs();
                var copyBtn = ButtonWithLabel(window, "パスをコピー");
                Assert.True(copyBtn.IsEffectivelyVisible, "VC-IMG-12②: 1件でコピーが見えない");
                Assert.True(HasVisibleButton(window, "ファイルの場所を開く"), "VC-IMG-12②: 1件で場所を開くが見えない");
                var presenter = copyBtn.GetVisualDescendants().OfType<ContentPresenter>().FirstOrDefault();
                var bg = presenter?.Background as ISolidColorBrush;
                Assert.True(bg is not null && bg.Color == MockBlue,
                    $"VC-IMG-12②: コピーが青 契約 accent 塗りでない({bg?.Color.ToString() ?? "null"})");
                var badge1 = copyBtn.GetLogicalDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Text == "1");
                Assert.True(badge1 is not null, "VC-IMG-12②/④: バッジ 1 が無い");

                // VC-IMG-12③④: 2 件=コピー(バッジ 2)のみ・場所を開くは非表示
                vm.HandleItemClick(Item(vm, "b.jpg"), ctrl: true, shift: false);
                RunJobs();
                Assert.True(HasVisibleButton(window, "パスをコピー"), "VC-IMG-12③: 2件でコピーが見えない");
                Assert.False(HasVisibleButton(window, "ファイルの場所を開く"), "VC-IMG-12③: 2件で場所を開くが見えている");
                var badge2 = copyBtn.GetLogicalDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Text == "2");
                Assert.True(badge2 is not null, "VC-IMG-12④: バッジが選択枚数 2 に追随しない");
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task VCIMG13_選択視覚はチェックと青リングで番号バッジを出さない()
    {
        var vm = await NewVmWithImagesAsync("a.jpg", "b.jpg");
        await Session.Dispatch(() =>
        {
            var window = new Window { Content = new ImageTabView { DataContext = vm }, Width = 1200, Height = 800 };
            window.Show();
            RunJobs();
            try
            {
                vm.EnterFileOpsCommand.Execute(null);
                vm.HandleItemClick(Item(vm, "a.jpg"), ctrl: false, shift: false);
                RunJobs();

                // グリッドセル(a.jpg)を特定
                var nameText = window.GetLogicalDescendants().OfType<TextBlock>()
                    .FirstOrDefault(t => t.Text == "a.jpg" && t.Classes.Contains("thumbName"));
                Assert.True(nameText is not null, "グリッドセル a.jpg が見つからない");
                var cellWrap = nameText!.FindLogicalAncestorOfType<Border>();
                while (cellWrap is not null && !cellWrap.Classes.Contains("thumbCellWrap"))
                    cellWrap = cellWrap.FindLogicalAncestorOfType<Border>();
                Assert.True(cellWrap is not null, "thumbCellWrap 祖先が無い");

                // VC-IMG-13: セル青リング(thumbCell.selected)
                var cell = cellWrap!.GetLogicalDescendants().OfType<Border>()
                    .FirstOrDefault(b => b.Classes.Contains("thumbCell"));
                Assert.True(cell is not null && cell.Classes.Contains("selected"),
                    "VC-IMG-13: 選択セルに青リング(thumbCell.selected)が無い");

                // VC-IMG-13: チェックは青塗り+白✓・番号バッジなし
                var check = cellWrap.GetLogicalDescendants().OfType<Border>()
                    .FirstOrDefault(b => b.Classes.Contains("thumbCheck"));
                Assert.True(check is not null && check.Classes.Contains("selected"), "VC-IMG-13: 選択チェックが無い");
                var checkBg = Assert.IsAssignableFrom<ISolidColorBrush>(check!.Background).Color;
                Assert.True(checkBg == MockBlue, $"VC-IMG-13: チェックが青 契約 accent 塗りでない({checkBg})");
                var plainCheck = check.GetLogicalDescendants().OfType<PathIcon>()
                    .FirstOrDefault(p => p.IsEffectivelyVisible);
                Assert.True(plainCheck is not null, "VC-IMG-13: 白✓(番号なしチェック)が出ていない");
                var orderText = check.GetLogicalDescendants().OfType<TextBlock>().FirstOrDefault();
                Assert.True(string.IsNullOrEmpty(orderText?.Text), $"VC-IMG-13: 番号バッジが出ている({orderText?.Text})");

                // IMG-026④ 裁定: リスト表示の選択視覚はタグ編集と同型(行ハイライト=imageListRow.selected)
                vm.SetListCommand.Execute(null);
                RunJobs();
                var row = window.GetLogicalDescendants().OfType<Border>()
                    .FirstOrDefault(b => b.Classes.Contains("imageListRow") && FindText(b, "a.jpg") is not null);
                Assert.True(row is not null && row.Classes.Contains("selected"),
                    "IMG-026④: リスト行の選択ハイライトが出ない");
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }
}
