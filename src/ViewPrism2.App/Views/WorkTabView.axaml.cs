using Avalonia.Controls;
using Avalonia.Input;
using ViewPrism2.App.ViewModels;

namespace ViewPrism2.App.Views;

/// <summary>
/// 作業タブ surface(ECO-020 / ECO-α)。左=作業スペースサイドバー / 中央=現スペース画像のグリッド/リスト。
/// 行選択は PointerPressed を code-behind で処理(ImageTabView と同じ流儀)。
/// </summary>
public partial class WorkTabView : UserControl
{
    public WorkTabView() => InitializeComponent();

    private WorkTabViewModel? Vm => DataContext as WorkTabViewModel;

    private void OnWorkspacePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: WorkspaceRowVM row } && Vm is { } vm)
            vm.SelectWorkspaceCommand.Execute(row.Id);
    }

    private void OnRenameKeyDown(object? sender, KeyEventArgs e)
    {
        if (Vm is not { } vm) return;
        if (e.Key == Key.Enter) { vm.CommitRenameCommand.Execute(null); e.Handled = true; }
        else if (e.Key == Key.Escape) { vm.CancelRenameCommand.Execute(null); e.Handled = true; }
    }

    private void OnRenameLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => Vm?.CommitRenameCommand.Execute(null);

    private void OnSortMenuClosed(object? sender, System.EventArgs e)
    {
        if (Vm is { } vm) vm.SortMenuOpen = false;
    }

    private void OnMenuClosed(object? sender, System.EventArgs e) => Vm?.CloseMenusFromDismiss();

    private void OnItemPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: ImageItemVM item } control && Vm is { } vm &&
            e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
        {
            var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
            var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            vm.HandleItemClick(item, ctrl, shift);
        }
    }

    private void OnChipPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: ChipVM chip } && Vm is { } vm)
            vm.ClickChip(chip);
    }

    private void OnAddRowPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: AddRowVM row } && Vm is { } vm)
            vm.ClickAddRowCommand.Execute(row);
    }

    private void OnValueChipPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: ValueChipVM chip } && Vm is { } vm)
            vm.ApplyTextValueCommand.Execute(chip);
    }

    private void OnNumCellPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: NumCellVM cell } && Vm is { } vm)
            vm.ApplyRatingCommand.Execute(cell);
    }
}
