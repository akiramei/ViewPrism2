using Avalonia.Controls;
using Avalonia.Input;
using ViewPrism2.App.ViewModels;

namespace ViewPrism2.App.Views;

/// <summary>
/// 画像タブ製造(M2)の golden ハーネス View。シード VM(<see cref="ImageTabSeedViewModel"/>)を駆動し、
/// Components.axaml の画像タブ部品(M1)をモック準拠で描画する。選択は修飾キーを読むため
/// PointerPressed を code-behind で処理(タグタブ TagsTabView と同じ流儀)。
/// </summary>
public partial class ImageTabView : UserControl
{
    public ImageTabView() => InitializeComponent();

    private ImageTabViewModel? Vm => DataContext as ImageTabViewModel;

    private void OnCollectionPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: CollectionRowVM row } && Vm is { } vm)
            vm.SelectCollectionCommand.Execute(row.Id);
    }

    private void OnChipPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: ChipVM chip } && Vm is { } vm)
            vm.ClickChipCommand.Execute(chip);
    }

    private void OnItemPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: ImageItemVM item } && Vm is { } vm)
        {
            var m = e.KeyModifiers;
            vm.HandleItemClick(item, m.HasFlag(KeyModifiers.Control), m.HasFlag(KeyModifiers.Shift));
        }
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

    private void OnMenuClosed(object? sender, System.EventArgs e) => Vm?.CloseMenusFromDismiss();
}
