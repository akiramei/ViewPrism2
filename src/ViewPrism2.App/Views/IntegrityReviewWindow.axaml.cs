using Avalonia.Controls;
using ViewPrism2.App.ViewModels;

namespace ViewPrism2.App.Views;

/// <summary>
/// ECO-140 統合裁定ウィンドウ。Opened 後に hash 確認を開始して IR-7 を実描画し、
/// close 時は確認を中断する(次回開扉で再確認)。
/// </summary>
public partial class IntegrityReviewWindow : Window
{
    public IntegrityReviewWindow()
    {
        InitializeComponent();
        Opened += async (_, _) =>
        {
            if (DataContext is not IntegrityReviewViewModel vm)
            {
                return;
            }

            vm.RequestClose += OnRequestClose;
            if (!vm.HasLoaded)
            {
                await vm.LoadAsync();
            }
        };
        Closed += (_, _) =>
        {
            if (DataContext is IntegrityReviewViewModel vm)
            {
                vm.CancelLoading();
                vm.RequestClose -= OnRequestClose;
            }
        };
    }

    private void OnRequestClose(object? sender, EventArgs e) => Close();
}
