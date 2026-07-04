using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ViewPrism2.App.ViewModels;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services.Viewer;
using ViewPrism2.Core.Services;
using ViewPrism2.Infrastructure.I18n;
using ViewPrism2.App.Views;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// GF-TAGCTRL-01(ECO-022 golden G-11)の恒久回帰: ビューア設定ドロワーの ScrollViewer が
/// 有界高さ(Viewport 有限)で内容が溢れるとき(Extent &gt; Viewport)スクロール可能であること。
/// 製造時からの潜在レイアウトバグ(ドロワーが非有界コンテナ下で ScrollViewer に無限高さが渡り
/// スクロールしない)を再発させないための ground-truth 実測(Avalonia.Headless の実レイアウトパス)。
/// </summary>
[Trait("cp", "CP-UI-G11")]
public sealed class GfViewerDrawerScrollTests
{
    // ヘッドレスセッションは App リソース(スタイル/ブラシ/アイコン)を読み込むため App を起動する。
    // ECO-040: AppBuilder.Setup のプロセス 1 回制約のため HeadlessApp.Session へ共有化(挙動不変)。
    private static HeadlessUnitTestSession Session => HeadlessApp.Session;

    [Fact]
    public Task 設定ドロワーのScrollViewerは有界でありスクロール可能() =>
        Session.Dispatch(() =>
        {
            var items = Enumerable.Range(0, 6).Select(Entry).ToList();
            var vm = new ViewerViewModel(items, startIndex: 2, new ViewerSettingsModel(), persist: null)
            {
                Loc = new LocalizationProxy(new LocalizationService(
                    I18nResourceLoader.Load(Path.Combine(AppContext.BaseDirectory, "Assets", "i18n")))),
            };
            vm.Mode = ViewerMode.SpreadRight; // ドロワーが見開き設定(最長)を表示
            vm.EnableTagControl = true;       // タグ制御カードまで含む
            vm.SettingsOpen = true;

            var window = new ViewerWindow { DataContext = vm, Width = 1000, Height = 760 };
            window.Show();
            for (var i = 0; i < 8; i++)
            {
                Dispatcher.UIThread.RunJobs();
            }

            var drawer = window.GetVisualDescendants().OfType<Border>()
                .FirstOrDefault(b => Math.Abs(b.Width - 376) < 0.5);
            Assert.NotNull(drawer); // 幅 376 の設定ドロワー Border(GF-TAGCTRL-05 V1: 360→376)

            var sv = drawer!.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            Assert.NotNull(sv); // ドロワー内 ScrollViewer

            // 有界: Viewport 高さが有限かつ 0 超(= 行高から無限高さを受けていない)
            Assert.True(double.IsFinite(sv!.Viewport.Height) && sv.Viewport.Height > 0,
                $"Viewport.Height={sv.Viewport.Height} (有界化されていない)");
            // スクロール可能: 内容(Extent)が Viewport を超える
            Assert.True(sv.Extent.Height > sv.Viewport.Height + 0.5,
                $"Extent.Height={sv.Extent.Height} <= Viewport.Height={sv.Viewport.Height} (スクロール域ゼロ)");

            window.Close();
        }, CancellationToken.None);

    /// <summary>
    /// GF-TAGCTRL-05 D1: タグ制御マッピングモーダルはモック権威の幅 820 で描画され、
    /// 760 窓に縦方向で収まる(header/列見出し/6 行/常時フッターが MaxHeight 720 内)。
    /// D1 幅 820 の headless 再計測(change-order §9 の残作業)を恒久ガード化する。
    /// </summary>
    [Fact]
    public Task タグ制御マッピングモーダルは幅820で760窓に収まる() =>
        Session.Dispatch(() =>
        {
            var items = Enumerable.Range(0, 6).Select(Entry).ToList();
            var vm = new ViewerViewModel(items, startIndex: 2, new ViewerSettingsModel(), persist: null)
            {
                Loc = new LocalizationProxy(new LocalizationService(
                    I18nResourceLoader.Load(Path.Combine(AppContext.BaseDirectory, "Assets", "i18n")))),
            };
            vm.Mode = ViewerMode.SpreadRight;
            vm.EnableTagControl = true;
            // picker の使用中(D4)/選択✓(D5)経路も VM 構築で通す(1 タグを 2 アクションへ割当)。
            vm.SetAvailableTags(new[]
            {
                new TagPickerOption("t1", "タグA", "#2F6BED"),
                new TagPickerOption("t2", "タグB", "#12A594"),
            });
            vm.SetTagActionMapping(ViewerTagAction.ForceLeftPage, "t1");
            vm.SetTagActionMapping(ViewerTagAction.Skip, "t1"); // t1 は複数アクション=使用中
            vm.TagControlMappingOpen = true;

            var window = new ViewerWindow { DataContext = vm, Width = 1200, Height = 760 };
            window.Show();
            for (var i = 0; i < 8; i++)
            {
                Dispatcher.UIThread.RunJobs();
            }

            var modal = window.GetVisualDescendants().OfType<Border>()
                .FirstOrDefault(b => Math.Abs(b.Width - 820) < 0.5);
            Assert.NotNull(modal); // 幅 820 のモーダル Border(D1)

            // 760 窓に縦で収まる(スクロール無しで全行+フッター到達可能)
            Assert.True(modal!.Bounds.Height > 0 && modal.Bounds.Height <= 760.5,
                $"modal.Bounds.Height={modal.Bounds.Height} (760 窓に収まっていない)");

            window.Close();
        }, CancellationToken.None);

    private static ImageEntry Entry(int i)
    {
        var name = $"img{i}.jpg";
        var record = new ImageRecord
        {
            Id = name,
            SyncFolderId = "f",
            RelativePath = name,
            FileName = name,
            FileSize = 1,
            Hash = new string('0', 64),
            CreatedDate = "2026-07-01T00:00:00.000Z",
            ModifiedDate = "2026-07-01T00:00:00.000Z",
        };
        return new ImageEntry(record, @"C:\img\" + name, []);
    }

}
