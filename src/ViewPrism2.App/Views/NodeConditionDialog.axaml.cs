using Avalonia.Controls;
using Avalonia.Interactivity;
using ViewPrism2.App.ViewModels;

namespace ViewPrism2.App.Views;

/// <summary>
/// 階層ノードの条件設定ダイアログ(仕様 §2.6 v1.2)。検証・JSON 生成は VM。
/// OK で NodeConditionResult を返して閉じる(キャンセルは null)。
/// </summary>
public partial class NodeConditionDialog : Window
{
    public NodeConditionDialog()
    {
        InitializeComponent();
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is NodeConditionDialogViewModel vm && vm.TryBuildResult() is { } result)
        {
            Close(result);
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);
}
