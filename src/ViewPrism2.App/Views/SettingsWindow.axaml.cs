using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ViewPrism2.App.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
