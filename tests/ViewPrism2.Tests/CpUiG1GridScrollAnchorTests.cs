using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ViewPrism2.App.ViewModels;
using ViewPrism2.App.Views;
using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// ECO-110(gate① 裁定 2026-07-18: 案A=先頭可視アイテムアンカー・適用範囲a=右ペイン開閉のみ):
/// グリッド表示で文脈モード(タグ編集/整理)の右ペイン(Dock Right 344px)が開閉すると中央幅が変わり
/// UniformGridLayout が再流動する。CAD image_tab.md layoutInvariant「右ペインの開閉が中央を壊さない」の
/// 保存対象は**可視コンテンツ**(開閉直前の先頭完全可視アイテムを再レイアウト後も先頭付近に維持)であり、
/// ピクセルオフセットの保持だけでは充足しない(mock はブラウザ標準 overflow-anchor で自然充足)。
/// 本 probe は開閉前の先頭可視アイテムを実測し、開閉後もビューポート先頭(±許容 1.5px)に居ることを固定する。
/// </summary>
[Trait("cp", "CP-UI-G1")]
public sealed class CpUiG1GridScrollAnchorTests : IDisposable
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

    /// <summary>スクロールが成立する規模(80 枚)のコレクションで初期化した VM。表示順=名前昇順で決定的。</summary>
    private async Task<ImageTabViewModel> NewScrollableVmAsync()
    {
        var col = new SyncFolder { Id = IdGenerator.NewId(), Name = "C", Path = @"C:\col" };
        await _db.Folders.AddAsync(col);
        for (var i = 0; i < 80; i++)
        {
            var name = $"img{i:D3}.jpg";
            await _db.Images.AddAsync(new ImageRecord
            {
                Id = IdGenerator.NewId(),
                SyncFolderId = col.Id,
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
        await vm.InitializeAsync(col.Id);
        return vm;
    }

    /// <summary>ブラウズグリッド(可視な ItemsRepeater)とそのスクロール本体を実レイアウトから特定する。</summary>
    private static (ScrollViewer Scroll, ItemsRepeater Repeater) BrowseGrid(Window window)
    {
        var rep = window.GetVisualDescendants().OfType<ItemsRepeater>()
            .FirstOrDefault(r => r.IsEffectivelyVisible);
        Assert.True(rep is not null, "ブラウズグリッドの ItemsRepeater が見つからない(グリッド表示が出ていない)");
        var scroll = rep!.FindAncestorOfType<ScrollViewer>();
        Assert.True(scroll is not null, "グリッドの ScrollViewer が見つからない");
        return (scroll!, rep);
    }

    /// <summary>
    /// 先頭完全可視アイテム(ビューポート上端以下で最上段・最左の実体化セル)の VM を実測で求める。
    /// 契約の「先頭可視アイテム」の定義そのもの=是正実装と独立に、ビューポート相対座標から判定する。
    /// </summary>
    private static (ImageItemVM Item, double ViewportY) FirstFullyVisible(ScrollViewer scroll, ItemsRepeater rep)
    {
        Control? best = null;
        var bestPos = new Point(double.MaxValue, double.MaxValue);
        foreach (var child in rep.Children)
        {
            if (!child.IsVisible) continue;
            if (child.TranslatePoint(new Point(0, 0), scroll) is not { } pt || pt.Y < -0.5) continue;
            if (best is null || pt.Y < bestPos.Y - 0.5 ||
                (Math.Abs(pt.Y - bestPos.Y) <= 0.5 && pt.X < bestPos.X))
            {
                best = child;
                bestPos = pt;
            }
        }
        Assert.True(best?.DataContext is ImageItemVM, "先頭可視セルが実測できない(実体化セルなし)");
        return ((ImageItemVM)best!.DataContext!, bestPos.Y);
    }

    /// <summary>
    /// 開閉後のアンカーの現在位置。実体化から外れていたら null(=完全にスクロールアウト)。
    /// 同定は参照でなく Name(アイテム同一性)で行う(当初はモード切替=Recompute の再構築が理由。
    /// ECO-114 で再構築されなくなったが、Name 同定は再構築の有無に非依存で頑健なため維持)。
    /// </summary>
    private static double? ViewportYOf(ScrollViewer scroll, ItemsRepeater rep, string anchorName)
    {
        foreach (var child in rep.Children)
        {
            if (child.DataContext is ImageItemVM { IsFolder: false } item && item.Name == anchorName &&
                child.TranslatePoint(new Point(0, 0), scroll) is { } pt)
            {
                return pt.Y;
            }
        }
        return null;
    }

    [Theory]
    [InlineData("edit")]
    [InlineData("organize")]
    public async Task 右ペインが開いても開閉前の先頭可視アイテムが先頭に維持される(string mode)
    {
        var vm = await NewScrollableVmAsync();
        await Session.Dispatch(() =>
        {
            var window = new Window { Content = new ImageTabView { DataContext = vm }, Width = 1200, Height = 800 };
            window.Show();
            RunJobs();
            try
            {
                vm.SetGridCommand.Execute(null);
                RunJobs();
                var (scroll, rep) = BrowseGrid(window);

                // 中腹へスクロールして基準アンカーを実測(先頭行以外=補正の有無が観測できる位置)
                scroll.Offset = new Vector(0, 600);
                RunJobs();
                Assert.True(scroll.Offset.Y > 0, "前提: スクロールが成立していない(コンテンツ不足)");
                var (anchor, beforeY) = FirstFullyVisible(scroll, rep);

                // 右ペインを開く(タグ編集/整理)→ 中央幅 −344 で再流動
                (mode == "edit" ? vm.ToggleEditCommand : vm.ToggleOrganizeCommand).Execute(null);
                RunJobs();
                Assert.True(vm.ShowRightPane, "前提: 右ペインが開いていない");

                var afterY = ViewportYOf(scroll, rep, anchor.Name);
                Assert.True(afterY is not null,
                    $"ECO-110: 開閉前の先頭可視アイテム「{anchor.Name}」が右ペイン表示後に実体化から外れた(完全スクロールアウト)");
                Assert.True(Math.Abs(afterY!.Value) <= 1.5,
                    $"ECO-110: アンカー「{anchor.Name}」がビューポート先頭に維持されていない(開閉前 y={beforeY:F1} → 開閉後 y={afterY.Value:F1}・期待 0±1.5)");
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task 右ペインを閉じても先頭可視アイテムが先頭に維持される()
    {
        var vm = await NewScrollableVmAsync();
        await Session.Dispatch(() =>
        {
            var window = new Window { Content = new ImageTabView { DataContext = vm }, Width = 1200, Height = 800 };
            window.Show();
            RunJobs();
            try
            {
                vm.SetGridCommand.Execute(null);
                vm.ToggleEditCommand.Execute(null); // 開いた状態から始める
                RunJobs();
                var (scroll, rep) = BrowseGrid(window);

                scroll.Offset = new Vector(0, 600);
                RunJobs();
                Assert.True(scroll.Offset.Y > 0, "前提: スクロールが成立していない");
                var (anchor, beforeY) = FirstFullyVisible(scroll, rep);

                vm.ToggleEditCommand.Execute(null); // 閉じる → 中央幅 +344 で逆方向の再流動
                RunJobs();
                Assert.False(vm.ShowRightPane, "前提: 右ペインが閉じていない");

                var afterY = ViewportYOf(scroll, rep, anchor.Name);
                Assert.True(afterY is not null,
                    $"ECO-110: アンカー「{anchor.Name}」が右ペイン終了後に実体化から外れた");
                Assert.True(Math.Abs(afterY!.Value) <= 1.5,
                    $"ECO-110: 終了時にアンカー「{anchor.Name}」が先頭に維持されていない(開閉前 y={beforeY:F1} → 開閉後 y={afterY.Value:F1})");
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task 先頭表示中の開閉はオフセット0のまま何もしない()
    {
        var vm = await NewScrollableVmAsync();
        await Session.Dispatch(() =>
        {
            var window = new Window { Content = new ImageTabView { DataContext = vm }, Width = 1200, Height = 800 };
            window.Show();
            RunJobs();
            try
            {
                vm.SetGridCommand.Execute(null);
                RunJobs();
                var (scroll, _) = BrowseGrid(window);
                Assert.True(scroll.Offset.Y == 0, "前提: 初期表示が先頭でない");

                vm.ToggleEditCommand.Execute(null);
                RunJobs();
                Assert.True(scroll.Offset.Y == 0, $"ECO-110: 先頭表示中の開で余計な補正が入った(offset={scroll.Offset.Y})");

                vm.ToggleEditCommand.Execute(null);
                RunJobs();
                Assert.True(scroll.Offset.Y == 0, $"ECO-110: 先頭表示中の閉で余計な補正が入った(offset={scroll.Offset.Y})");
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }
}
