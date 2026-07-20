using Avalonia.Controls;
using Avalonia.Interactivity;
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
        string confirmLabel, bool destructive, string? cancelLabel = null)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmLabel;
        ConfirmButton.Classes.Add(destructive ? "destructive" : "primary");
        CancelButton.Content = cancelLabel ?? loc["common.cancel"];
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
}
