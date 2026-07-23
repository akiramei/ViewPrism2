using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using ViewPrism2.App.ViewModels;
using ViewPrism2.App.Services;
using ViewPrism2.App.Views;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// ECO-126: 確認ダイアログ(ConfirmDialog)の CMP-011/L2 準拠 probe。
/// CAD 正本= ../ViewPrismUI docs/03_dialog_language.md L2(ボタンテキスト中央揃え=Avalonia 既定 Left の
/// 取り漏れ禁止・二次操作=白 outline #D6E0EE=テーマ既定グレー禁止)+docs/04_component_registry.md
/// CMP-011(destructive= #d83a3f 塗り+枠 #c4282d+白文字・ラベルは動詞= REG-C5「はい/いいえ」禁止)。
/// ボタンは XAML 順(キャンセル=先頭・CTA=末尾)で特定= 名前非依存。
/// 期待値は RegistryContract(ECO-122 写像)参照。
/// </summary>
[Trait("cp", "CP-UI-G1")]
public sealed class GfConfirmDialogVisualParityTests
{
    private static HeadlessUnitTestSession Session => HeadlessApp.Session;

    private static void RunJobs()
    {
        for (var i = 0; i < 8; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    // 是正前赤の採取(2026-07-21)は旧 ctor(loc,title,message)+同一アサーションで実施済み
    // (中央揃え/secondary/destructive/はい・いいえ の 3 本全赤)。是正で ctor が動詞ラベル必須へ
    // 変わったため(REG-C5 の型強制)、以後は新署名で構築する。アサーション本体は不変。
    private static (Window Dialog, Button Cancel, Button Confirm) Create(bool destructive = true)
    {
        var dialog = new ConfirmDialog(new LocalizationProxy(TestLoc.Ja()), "ビューの削除", "削除しますか?",
            confirmLabel: "削除する", destructive: destructive);
        dialog.Show();
        RunJobs();
        var buttons = dialog.GetLogicalDescendants().OfType<Button>().ToList();
        Assert.Equal(2, buttons.Count);
        return (dialog, buttons[0], buttons[1]);
    }

    [Fact]
    public async Task L2_ボタンテキストは中央揃えである()
    {
        await Session.Dispatch(() =>
        {
            var (dialog, cancel, confirm) = Create();
            try
            {
                // L2: Avalonia 既定= Left の取り漏れ禁止(明記事項)
                Assert.Equal(HorizontalAlignment.Center, cancel.HorizontalContentAlignment);
                Assert.Equal(HorizontalAlignment.Center, confirm.HorizontalContentAlignment);
            }
            finally
            {
                dialog.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CMP011_キャンセルはsecondary白outlineでCTAはテーマ既定グレーでない()
    {
        await Session.Dispatch(() =>
        {
            var (dialog, cancel, confirm) = Create();
            try
            {
                // CMP-011 secondary: 白地+枠 #D6E0EE(テーマ既定グレー禁止)
                var cancelBg = Assert.IsAssignableFrom<ISolidColorBrush>(cancel.Background).Color;
                Assert.Equal(Colors.White, cancelBg);
                var cancelBorder = Assert.IsAssignableFrom<ISolidColorBrush>(cancel.BorderBrush).Color;
                Assert.Equal(RegistryContract.DlgSecondaryBorder, cancelBorder);
                // CMP-011 destructive(削除確認の CTA): #d83a3f 塗り+白文字
                var confirmBg = Assert.IsAssignableFrom<ISolidColorBrush>(confirm.Background).Color;
                Assert.Equal(RegistryContract.DlgDestructiveFill, confirmBg);
                var confirmFg = Assert.IsAssignableFrom<ISolidColorBrush>(confirm.Foreground).Color;
                Assert.Equal(Colors.White, confirmFg);
            }
            finally
            {
                dialog.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CMP011_primaryはAccent塗り白文字である()
    {
        // R8 所見3: 非破壊系(再リンク 2 サイト)が通る primary variant の被覆
        await Session.Dispatch(() =>
        {
            var dialog = new ConfirmDialog(new LocalizationProxy(TestLoc.Ja()), "再リンク", "確定しますか?",
                confirmLabel: "再リンクする", destructive: false);
            dialog.Show();
            RunJobs();
            try
            {
                var confirm = dialog.GetLogicalDescendants().OfType<Button>().Last();
                Assert.Contains("primary", confirm.Classes);
                var bg = Assert.IsAssignableFrom<ISolidColorBrush>(confirm.Background).Color;
                Assert.Equal(RegistryContract.ColorAccent, bg); // CMP-011 primary= 青塗り(Accent)
                var fg = Assert.IsAssignableFrom<ISolidColorBrush>(confirm.Foreground).Color;
                Assert.Equal(Colors.White, fg);
            }
            finally
            {
                dialog.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ECO103_終了確認はキャンセル側ラベルを戻るへ上書きできる()
    {
        // R8 所見3: cancelLabel 上書き経路(ECO-103 裁定文言「破棄して終了/戻る」)の被覆
        await Session.Dispatch(() =>
        {
            var dialog = new ConfirmDialog(new LocalizationProxy(TestLoc.Ja()), "未保存の変更", "破棄しますか?",
                confirmLabel: "破棄して終了", destructive: true, cancelLabel: "戻る");
            dialog.Show();
            RunJobs();
            try
            {
                var buttons = dialog.GetLogicalDescendants().OfType<Button>().ToList();
                Assert.Equal("戻る", buttons[0].Content as string);
                Assert.Equal("破棄して終了", buttons[1].Content as string);
            }
            finally
            {
                dialog.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task REGC5_ラベルは動詞でありはいいいえでない()
    {
        await Session.Dispatch(() =>
        {
            var (dialog, cancel, confirm) = Create();
            try
            {
                // REG-C5: 応答が行為を名指す動詞ラベル(呼び出し側指定)+キャンセル既定は common.cancel
                Assert.Equal("削除する", confirm.Content as string);
                Assert.Equal("キャンセル", cancel.Content as string);
            }
            finally
            {
                dialog.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    [Trait("cp", "CP-PENDING-AUTO-035")]
    public async Task PD6_対象一覧つき確認は480幅_176px以下のscroll_非破壊primary()
    {
        await Session.Dispatch(() =>
        {
            var items = Enumerable.Range(1, 8)
                .Select(i => new ConfirmationListItem(
                    $"scan_{i:0000}.jpg", $"album_{i:0000}.png へ再リンク", $@"C:\Photos\scan_{i:0000}.jpg"))
                .ToList();
            var dialog = new ConfirmDialog(
                new LocalizationProxy(TestLoc.Ja()),
                "自動裁定の確認",
                "この 8 件をまとめて再リンクします",
                "再リンク",
                destructive: false,
                items: items,
                supportingMessage: "これらは移動元の「見つからない画像」とハッシュが一致します"
                    + "（スクロールで全 8 件を確認できます）。元の画像に付け替え、タグと ID を保持したまま"
                    + "通常表示に戻します。見つからない状態（リンク切れ）も解消されます。"
                    + "あとで各画像の通常運用から取り消せます。");
            dialog.Show();
            RunJobs();
            try
            {
                Assert.Equal(480, dialog.Width);
                var listBorder = dialog.FindControl<Border>("ConfirmationItemsBorder")!;
                Assert.True(listBorder.IsVisible);
                var list = dialog.FindControl<ListBox>("ConfirmationItems")!;
                Assert.Equal(176, list.MaxHeight);
                Assert.Equal(8, list.ItemCount);
                var buttons = dialog.GetLogicalDescendants().OfType<Button>().ToList();
                Assert.Contains("secondary", buttons[0].Classes);
                Assert.Contains("primary", buttons[1].Classes);
                Assert.Equal("再リンク", buttons[1].Content as string);
                Assert.Contains(dialog.GetLogicalDescendants().OfType<TextBlock>(),
                    text => text.Text == "この 8 件をまとめて再リンクします");
                Assert.Contains(dialog.GetLogicalDescendants().OfType<TextBlock>(),
                    text => text.Text == "album_0001.png へ再リンク");
            }
            finally
            {
                dialog.Close();
            }
        }, TestContext.Current.CancellationToken);
    }
}
