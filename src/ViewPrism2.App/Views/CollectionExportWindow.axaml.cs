using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ViewPrism2.App.Views;

/// <summary>コレクションを書き出す(ECO-073 B-1)。DataContext=CollectionExportViewModel。</summary>
public partial class CollectionExportWindow : Window
{
    public CollectionExportWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
