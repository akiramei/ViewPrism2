using Avalonia.Controls;
using Avalonia.Interactivity;
using ViewPrism2.App.ViewModels;

namespace ViewPrism2.App.Views;

/// <summary>
/// numeric タグの値入力ダイアログ(M-UI-016、REQ-046)。
/// 検証は VM(NumericValueDialogViewModel.TryBuildValues)。成功時は選択順の値列を返して閉じる。
/// 範囲外・step 不一致は閉じずにエラー表示(適用 0 件)。
/// </summary>
public partial class NumericValueDialog : Window
{
    public NumericValueDialog()
    {
        InitializeComponent();
    }

    private void OnFixedChecked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is NumericValueDialogViewModel vm)
        {
            vm.IsSequential = false;
        }
    }

    private void OnSequentialChecked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is NumericValueDialogViewModel vm)
        {
            vm.IsSequential = true;
        }
    }

    private void OnApplyClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is NumericValueDialogViewModel vm && vm.TryBuildValues() is { } values)
        {
            Close(values);
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);
}
