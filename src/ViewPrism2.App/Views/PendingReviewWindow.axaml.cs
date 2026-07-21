using Avalonia.Controls;
using ViewPrism2.App.ViewModels;

namespace ViewPrism2.App.Views;

/// <summary>
/// pending 裁定ウィンドウ(ECO-129・E-UI-PENDING-049)。✕クローズはいつでも可
/// (裁定は 1 件ずつ確定済み=破棄の概念なし・§2.11.7)。
/// </summary>
public partial class PendingReviewWindow : Window
{
    public PendingReviewWindow()
    {
        InitializeComponent();
        Opened += (_, _) =>
        {
            if (DataContext is PendingReviewViewModel vm)
            {
                vm.RequestClose += (_, _) => Close();
            }
        };
    }
}
