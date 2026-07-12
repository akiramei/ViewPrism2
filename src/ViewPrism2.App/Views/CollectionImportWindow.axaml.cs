using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ViewPrism2.App.Views;

/// <summary>コレクションを取り込む(ECO-073 B-2〜B-4 ウィザード)。DataContext=CollectionImportViewModel。</summary>
public partial class CollectionImportWindow : Window
{
    public CollectionImportWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
