using Avalonia.Controls;

namespace ViewPrism2.App.Views;

/// <summary>
/// シェルの View(M-UI-013)。ECO-024 で原典画像タブ Grid・legacy code-behind を撤去。
/// 各タブ surface(TagsTabView / ImageTabView / WorkTabView)が自前の View ロジックを持つため、
/// シェルはタブのホストに徹する(K-MVVM)。
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }
}
