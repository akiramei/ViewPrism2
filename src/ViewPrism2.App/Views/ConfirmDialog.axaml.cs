using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using ViewPrism2.App.Services;
using ViewPrism2.App.ViewModels;

namespace ViewPrism2.App.Views;

/// <summary>
/// 汎用確認ダイアログ。ShowDialog&lt;bool?&gt; で結果を返す。
/// ECO-126: CTA ラベルは呼び出し側が動詞で指定する(REG-C5「はい/いいえ」禁止を型で強制)。
/// キャンセル側は既定 common.cancel(必要な面のみ上書き=例: 未保存終了確認の「戻る」= ECO-103 裁定文言)。
/// </summary>
public partial class ConfirmDialog : Window
{
    // ランタイムローダ用
    public ConfirmDialog()
    {
        InitializeComponent();
    }

    public ConfirmDialog(LocalizationProxy loc, string title, string message,
        string confirmLabel, bool destructive, string? cancelLabel = null,
        IReadOnlyList<ConfirmationListItem>? items = null, string? supportingMessage = null)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmLabel;
        ConfirmButton.Classes.Add(destructive ? "destructive" : "primary");
        CancelButton.Content = cancelLabel ?? loc["common.cancel"];
        if (items is { Count: > 0 })
        {
            // ECO-139/PD-6 対象一覧版のみ mock 準拠の余白/文字太さへ。既存の汎用確認面は XAML 既定
            // (Margin 16・Spacing 16・通常太さ)のまま=既存 golden 不変(F-1 スコープ限定)。
            // ECO-140/IR-6: 対象一覧版だけを幅 500 にする。共有部品の基底スタイルは変更しない。
            Width = 500;
            RootPanel.Margin = new Thickness(20);
            RootPanel.Spacing = 12;
            MessageText.FontWeight = FontWeight.SemiBold;
            ConfirmationItems.ItemsSource = items;
            ConfirmationItemsBorder.IsVisible = true;
        }

        if (!string.IsNullOrWhiteSpace(supportingMessage))
        {
            SupportingMessageText.Text = supportingMessage;
            SupportingMessageText.IsVisible = true;
        }
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
}
