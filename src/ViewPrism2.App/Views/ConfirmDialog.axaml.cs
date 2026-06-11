using Avalonia.Controls;
using Avalonia.Interactivity;
using ViewPrism2.App.ViewModels;

namespace ViewPrism2.App.Views;

/// <summary>汎用確認ダイアログ。ShowDialog&lt;bool?&gt; で結果を返す。</summary>
public partial class ConfirmDialog : Window
{
    // ランタイムローダ用
    public ConfirmDialog()
    {
        InitializeComponent();
    }

    public ConfirmDialog(LocalizationProxy loc, string title, string message)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
        YesButton.Content = loc["common.yes"];
        NoButton.Content = loc["common.no"];
    }

    private void OnYesClick(object? sender, RoutedEventArgs e) => Close(true);

    private void OnNoClick(object? sender, RoutedEventArgs e) => Close(false);
}
