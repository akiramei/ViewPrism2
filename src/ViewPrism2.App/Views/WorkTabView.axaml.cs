using System;
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
    private readonly DoubleClickDetector _doubleClick = new();

    private ItemsControl? _chipDisplay;
    private Avalonia.Controls.Primitives.Popup? _chipPopover;
    private TextBox? _chipSearchBox;
    private double _lastChipPanelWidth;

    public WorkTabView()
    {
        InitializeComponent();

        // ECO-091: チップ行の容量(最大2行+ほかN件)。画像タブと同一契約(ECO-090 同期宣言)・
        // 実測供給は View 責務(ECO-027 と同じ流儀・計算は ChipStripViewModel/ChipRowOverflow)
        _chipDisplay = this.FindControl<ItemsControl>("ChipDisplay");
        _chipPopover = this.FindControl<Avalonia.Controls.Primitives.Popup>("ChipPopover");
        _chipSearchBox = this.FindControl<TextBox>("ChipSearchBox");
        if (_chipPopover is { } pop)
        {
            // 開いたら検索欄へフォーカス(VC-WORK-3 キーボード契約: 検索欄→一覧の順)
            pop.Opened += (_, _) => _chipSearchBox?.Focus();
        }
        LayoutUpdated += (_, _) => EvaluateChipRow();
    }

    private WorkTabViewModel? Vm => DataContext as WorkTabViewModel;

    /// <summary>ECO-091(IMG-023A=A-b): チップ行の実描画からの折畳み評価(ImageTabView と同一契約)。</summary>
    private void EvaluateChipRow()
    {
        if (Vm is not { } vm || !vm.ShowChips) return;
        var panel = _chipDisplay?.ItemsPanelRoot;
        if (panel is null || panel.Bounds.Width <= 0) return;

        var width = panel.Bounds.Width;
        if (Math.Abs(width - _lastChipPanelWidth) > 0.5)
        {
            _lastChipPanelWidth = width;
            // 折畳み中だった場合のみ次パスへ(全表示へ戻した=矩形が古い)。未折畳みなら同一パスで計測可
            if (vm.ChipStrip.ResetFold()) return;
        }

        var chipRects = new System.Collections.Generic.List<Avalonia.Rect>();
        Avalonia.Rect? moreRect = null;
        foreach (var child in panel.Children)
        {
            if (!child.IsVisible) continue;
            if (child.DataContext is ChipVM) chipRects.Add(child.Bounds);
            else if (child.DataContext is ChipMoreVM) moreRect = child.Bounds;
        }
        vm.ChipStrip.ReportLayout(chipRects, moreRect, width);
    }

    /// <summary>Escape でポップオーバーを閉じ「ほか N 件」へフォーカスを戻す(VC-WORK-3)。</summary>
    private void OnChipPopoverKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || Vm is not { } vm) return;
        vm.ChipStrip.ClosePopoverCommand.Execute(null);
        FocusChipMoreButton();
        e.Handled = true;
    }

    private void FocusChipMoreButton()
    {
        if (_chipDisplay is null) return;
        foreach (var btn in Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(_chipDisplay))
        {
            if (btn is Button b && b.Classes.Contains("chipMore")) { b.Focus(); return; }
        }
    }

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
            // ECO-068: 作業タブも画像タブと同じ閲覧契約。ClickCountだけに依存せず、
            // OSのダブルクリック時間内の同一項目連続押下を共有検出器で補完する。
            var detected = _doubleClick.ObserveClick(item, (long)e.Timestamp, SystemDoubleClickTimeMs, ctrl || shift);
            vm.HandleItemClick(item, ctrl, shift, e.ClickCount >= 2 || detected);
        }
    }

    /// <summary>OSのダブルクリック時間(ms)。本アプリはWindows専用(仕様§1)。</summary>
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
