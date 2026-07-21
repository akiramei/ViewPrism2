using Avalonia.Controls;
using ViewPrism2.App.ViewModels;

namespace ViewPrism2.App.Views;

/// <summary>
/// スキャン結果確認ウィンドウ(ECO-130・E-UI-SCANSTAGE-048)。
/// Opened で差分計算を開始し、VM の RequestClose で閉じる。✕クローズは破棄/キャンセルと同義
/// (VM.OnWindowClosing がスキャン中ならキャンセル。適用は明示ボタンのみ=安全側)。
/// </summary>
public partial class ScanSummaryWindow : Window
{
    public ScanSummaryWindow()
    {
        InitializeComponent();
        Opened += (_, _) =>
        {
            if (DataContext is ScanSummaryViewModel vm)
            {
                vm.RequestClose += (_, _) => Close();
                // probe/撮影ハーネスは PresentSummary 済み or AutoStart=false で開く(差分計算を走らせない)
                if (vm.AutoStart && vm.Phase == ScanStagePhase.Scanning)
                {
                    _ = vm.StartAsync();
                }
            }
        };
        Closing += (_, e) =>
        {
            if (DataContext is not ScanSummaryViewModel vm)
            {
                return;
            }

            // R8 所見1/2: 適用中・SC-6 確認中の ✕ はブロック(適用開始後の中断=部分適用と
            // 「破棄と報告しながら DB 適用が完走する」誤報を作らない)。VM 起点の Close は素通し
            if ((vm.IsApplyingPhase || vm.IsConfirmOpen) && !vm.CloseRequested)
            {
                e.Cancel = true;
                return;
            }

            vm.OnWindowClosing();
        };
    }
}
