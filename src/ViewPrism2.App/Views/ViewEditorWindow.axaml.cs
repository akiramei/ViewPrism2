using Avalonia.Controls;
using Avalonia.Interactivity;
using ViewPrism2.App.ViewModels;

namespace ViewPrism2.App.Views;

public partial class ViewEditorWindow : Window
{
    public ViewEditorWindow()
    {
        InitializeComponent();
    }

    private void OnNodeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is ViewEditorViewModel vm && sender is TreeView tree)
        {
            vm.SelectedNode = tree.SelectedItem as HierarchyNodeViewModel;
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
