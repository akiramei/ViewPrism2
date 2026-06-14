using Avalonia.Controls;

namespace ViewPrism2.App.Views;

/// <summary>修復ライフサイクル UI(M-UI-REPAIR-027 / 仕様 §2.11.5)。</summary>
public partial class RepairWindow : Window
{
    public RepairWindow()
    {
        InitializeComponent();
    }
}
