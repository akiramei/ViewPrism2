using Avalonia.Controls;
using Avalonia.Interactivity;
using ViewPrism2.App.ViewModels;

namespace ViewPrism2.App.Views;

/// <summary>コレクションを取り込む(ECO-073 B-2〜B-4 ウィザード)。DataContext=CollectionImportViewModel。</summary>
public partial class CollectionImportWindow : Window
{
    public CollectionImportWindow()
    {
        InitializeComponent();

        // ECO-074 案イ: CAD は B-2 を「互換OK/NG」の 2 状態で定義し未選択を定常状態としない —
        // 表示直後に picker を開く(キャンセル残留時のみプレースホルダ表示)
        Opened += (_, _) =>
        {
            if (DataContext is CollectionImportViewModel { PackagePath: null } vm)
            {
                vm.PickFileCommand.Execute(null);
            }
        };
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
