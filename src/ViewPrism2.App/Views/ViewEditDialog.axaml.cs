using Avalonia.Controls;
using Avalonia.Interactivity;
using ViewPrism2.App.ViewModels;

namespace ViewPrism2.App.Views;

/// <summary>ビュー作成/編集ダイアログ(v1.2: 名前+説明)。保存成功時に true で閉じる。</summary>
public partial class ViewEditDialog : Window
{
    public ViewEditDialog()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is ViewEditDialogViewModel vm)
            {
                TitleText.Text = vm.Loc[vm.IsCreate ? "view.createView" : "view.editView"];
                Title = TitleText.Text;
            }
        };
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
}
