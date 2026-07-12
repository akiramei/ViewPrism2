using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ViewPrism2.App.Views;

/// <summary>スナップショット管理(ECO-072 A-1)。DataContext=SnapshotViewModel。</summary>
public partial class SnapshotWindow : Window
{
    public SnapshotWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
