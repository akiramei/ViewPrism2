using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using ViewPrism2.App.ViewModels;

namespace ViewPrism2.App.Views;

/// <summary>
/// タグタブの View(M-UI-013・ECO-099 配置モデル統一)。ロジックは TagsTabViewModel/
/// HierarchyEditorViewModel に置き、ここはポインタ/キーイベント→VM 呼び出し・
/// タグパレット→階層の D&D・「⋯」メニューのクローズのみ(K-MVVM)。
/// </summary>
public partial class TagsTabView : UserControl
{
    /// <summary>パレット→階層 D&D のアプリ内データ形式(タグ id)。</summary>
    private static readonly DataFormat<string> TagIdFormat =
        DataFormat.CreateStringApplicationFormat("viewprism2-tag-id");

    /// <summary>クリック(=配置モードのトグル)とドラッグ開始を分ける移動閾値(px)。</summary>
    private const double DragThreshold = 4;

    private TagPaletteRowViewModel? _palettePressRow;
    private PointerPressedEventArgs? _palettePressArgs;
    private Point _palettePressPoint;

    public TagsTabView()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, OnTreeDragOver);
        AddHandler(DragDrop.DropEvent, OnTreeDrop);
        // ECO-099(VC-TAG-12): Esc で配置モード解除。tunnel で先取りする(配置中は行操作が
        // 一時停止=別名編集は開始できないため、別名 TextBox の Esc とは競合しない)
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        // ECO-092(TAG-013=T-a): 候補値プレビューの容量(最大2行+非対話ほかN件)。
        // 実測供給は View 責務(ECO-091/ECO-027 と同流儀・計算は行 VM+ChipRowOverflow)
        LayoutUpdated += (_, _) => EvaluateCandidateRows();
    }

    private TagsTabViewModel? ViewModel => DataContext as TagsTabViewModel;

    /// <summary>各パレットカードの候補値行の実描画矩形を行 VM へ供給する(ECO-092)。</summary>
    private void EvaluateCandidateRows()
    {
        foreach (var strip in Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(this).OfType<ItemsControl>())
        {
            if (!strip.Classes.Contains("candidateStrip")) continue;
            if (strip.DataContext is not TagPaletteRowViewModel row) continue;
            var panel = strip.ItemsPanelRoot;
            if (panel is null || panel.Bounds.Width <= 0) continue;

            var chipRects = new System.Collections.Generic.List<Avalonia.Rect>();
            Avalonia.Rect? moreRect = null;
            foreach (var child in panel.Children)
            {
                if (!child.IsVisible) continue;
                if (child.DataContext is string) chipRects.Add(child.Bounds);
                else if (child.DataContext is ChipMoreVM) moreRect = child.Bounds;
            }
            row.ReportCandidateLayout(chipRects, moreRect, panel.Bounds.Width);
        }
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && ViewModel is { Editor.IsPlacing: true } vm)
        {
            vm.Editor.CancelPlacingCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnViewRowPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel is { } vm &&
            sender is Control { DataContext: ViewRowViewModel row } control &&
            e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
        {
            vm.SelectViewCommand.Execute(row);
        }
    }

    // ---- パレットカード: クリック=配置モードのトグル / ドラッグ=D&D(ECO-099。移動閾値で判別) ----

    private void OnPaletteItemPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: TagPaletteRowViewModel row } control &&
            e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
        {
            _palettePressRow = row;
            _palettePressArgs = e;
            _palettePressPoint = e.GetPosition(this);
        }
    }

    private void OnPaletteItemMoved(object? sender, PointerEventArgs e)
    {
        if (ViewModel is not { } vm || _palettePressRow is not { } row || _palettePressArgs is not { } press)
        {
            return;
        }

        var delta = e.GetPosition(this) - _palettePressPoint;
        if (Math.Abs(delta.X) < DragThreshold && Math.Abs(delta.Y) < DragThreshold)
        {
            return;
        }

        // ドラッグ確定: 従来 D&D(配置モードには入らない=mock は両経路を独立に提供)
        _palettePressRow = null;
        _palettePressArgs = null;
        vm.Palette.SelectedTag = row;
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(TagIdFormat, row.Tag.Id));
        _ = DragDrop.DoDragDropAsync(press, data, DragDropEffects.Copy);
    }

    private void OnPaletteItemReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (ViewModel is { } vm && _palettePressRow is { } row)
        {
            // クリック確定: 配置モードのトグル(VC-TAG-12①。mock select= selectedTag も更新)
            vm.Palette.SelectedTag = row;
            vm.TogglePlacing(row);
        }

        _palettePressRow = null;
        _palettePressArgs = null;
    }

    private void OnTreeDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = ViewModel?.Editor.HasView == true && e.DataTransfer.Contains(TagIdFormat)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>ドロップ: ノード上=その子として追加 / 空白=ルートへ追加。</summary>
    private void OnTreeDrop(object? sender, DragEventArgs e)
    {
        if (ViewModel is not { } vm || vm.Editor.HasView != true)
        {
            return;
        }

        if (e.DataTransfer.TryGetValue(TagIdFormat) is not { } tagId)
        {
            return;
        }

        var target = FindDataContext<EditNodeViewModel>(e.Source);
        vm.AddTagById(tagId, target);
        e.Handled = true;
    }

    // ---- 階層行(ECO-099): 親行クリック=展開/折畳・葉行クリック=選択(CAD インタラクション表) ----

    private void OnNodeRowPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel is not { } vm ||
            sender is not Control { DataContext: EditNodeViewModel node } control ||
            !e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (node.HasChildren)
        {
            node.IsExpanded = !node.IsExpanded;
        }
        else
        {
            vm.Editor.SelectedNode = node;
        }
    }

    // ---- 挿入ポイント/「＋ 子にする」(VC-TAG-12③④) ----

    private void OnInsertBeforePressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel is { } vm && sender is Control { DataContext: EditNodeViewModel node })
        {
            vm.Editor.InsertBeforeCommand.Execute(node);
            e.Handled = true;
        }
    }

    private void OnInsertChildEndPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel is { } vm && sender is Control { DataContext: EditNodeViewModel node })
        {
            vm.Editor.InsertChildEndCommand.Execute(node);
            e.Handled = true;
        }
    }

    private void OnInsertRootEndPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel is { } vm)
        {
            vm.Editor.InsertRootEndCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnMakeChildPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel is { } vm && sender is Control { DataContext: EditNodeViewModel node })
        {
            vm.Editor.PlaceAsChildCommand.Execute(node);
            e.Handled = true;
        }
    }

    // ---- 「⋯」メニュー(VC-TAG-13②): 項目実行=VM コマンド+Flyout クローズ ----

    private void OnMenuSetHomeClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm && sender is Control { DataContext: EditNodeViewModel node })
        {
            vm.Editor.SetHomeFromMenuCommand.Execute(node);
        }

        CloseRowMenu(sender);
    }

    private void OnMenuRenameClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm && sender is Control { DataContext: EditNodeViewModel node })
        {
            // TAG-015①裁定: インライン編集の従来契約へ接続(Enter 確定・空→タグ名復帰)
            vm.Editor.BeginAliasEditCommand.Execute(node);
        }

        CloseRowMenu(sender);
    }

    private void OnMenuSetConditionClick(object? sender, RoutedEventArgs e)
    {
        CloseRowMenu(sender);
        if (ViewModel is { } vm && sender is Control { DataContext: EditNodeViewModel node })
        {
            // 「配置タグの設定」ダイアログ(展開モード+条件・ECO-086。入口だけ ⋯メニューへ移動)
            vm.Editor.EditConditionCommand.Execute(node);
        }
    }

    private void OnMenuDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm && sender is Control { DataContext: EditNodeViewModel node })
        {
            // TAG-015④裁定: 確認なし即時(タグ定義は残る+バッチ編集=保存前はキャンセルで全戻し可能)
            vm.Editor.DeleteNodeCommand.Execute(node);
        }

        CloseRowMenu(sender);
    }

    /// <summary>メニュー項目クリック後に Flyout(Popup)を閉じる。</summary>
    private static void CloseRowMenu(object? sender)
    {
        if ((sender as ILogical)?.FindLogicalAncestorOfType<Popup>() is { } popup)
        {
            popup.IsOpen = false;
        }
    }

    /// <summary>別名インライン編集: Enter=確定 / Escape=取消(仕様 §2.6・TAG-015①)。</summary>
    private void OnAliasKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is not { } vm || sender is not Control { DataContext: EditNodeViewModel node })
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            vm.Editor.CommitAliasCommand.Execute(node);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            vm.Editor.CancelAliasEditCommand.Execute(node);
            e.Handled = true;
        }
    }

    private void OnAliasLostFocus(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm &&
            sender is Control { DataContext: EditNodeViewModel { IsEditingAlias: true } node })
        {
            vm.Editor.CommitAliasCommand.Execute(node);
        }
    }

    private static T? FindDataContext<T>(object? source)
        where T : class
    {
        var current = source as StyledElement;
        while (current is not null)
        {
            if (current.DataContext is T match)
            {
                return match;
            }

            current = current.Parent;
        }

        return null;
    }
}
