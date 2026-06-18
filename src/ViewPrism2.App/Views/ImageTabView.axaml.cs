using System;
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
    private readonly DoubleClickDetector _doubleClick = new();

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
        if (sender is Control { DataContext: ImageItemVM item } control && Vm is { } vm &&
            e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
        {
            var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
            var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            // ダブルクリック判定は ClickCount に加え自前検出で補完する(DF-4 堅牢化・原典と同流儀)
            var detected = _doubleClick.ObserveClick(item, (long)e.Timestamp, SystemDoubleClickTimeMs, ctrl || shift);
            vm.HandleItemClick(item, ctrl, shift, e.ClickCount >= 2 || detected);
        }
    }

    /// <summary>OS のダブルクリック時間(ms)。本アプリは Windows 専用(仕様 §1)。</summary>
    private static double SystemDoubleClickTimeMs
    {
        get
        {
            try { return GetDoubleClickTime(); }
            catch (EntryPointNotFoundException) { return 500; }
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();

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
