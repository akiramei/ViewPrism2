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

    /// <summary>行移動 D&D のアプリ内データ形式(階層ノード id・ECO-100)。</summary>
    private static readonly DataFormat<string> NodeIdFormat =
        DataFormat.CreateStringApplicationFormat("viewprism2-node-id");

    /// <summary>クリック(=配置トグル/展開/選択)とドラッグ開始を分ける移動閾値(px・裁定 a)。</summary>
    private const double DragThreshold = 4;

    private TagPaletteRowViewModel? _palettePressRow;
    private PointerPressedEventArgs? _palettePressArgs;
    private Point _palettePressPoint;

    private EditNodeViewModel? _rowPressNode;
    private PointerPressedEventArgs? _rowPressArgs;
    private Point _rowPressPoint;

    /// <summary>ドラッグオーバー中の受け面(dropHover 強調の対象)。</summary>
    private Border? _dropHoverElement;

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

    private async void OnPaletteItemMoved(object? sender, PointerEventArgs e)
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

        // ドラッグ確定(ECO-100 スコープ2): クリック配置と同一の挿入表示で受ける(mock 契約)。
        // ドラッグ開始で配置モードへ入り、終了で必ず解除(ドロップ成功時は既に解除済み=冪等)
        _palettePressRow = null;
        _palettePressArgs = null;
        vm.Palette.SelectedTag = row;
        vm.Editor.BeginDragPlacing(row.Tag);
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(TagIdFormat, row.Tag.Id));
        try
        {
            await DragDrop.DoDragDropAsync(press, data, DragDropEffects.Copy);
        }
        finally
        {
            SetDropHover(null);
            vm.Editor.CancelPlacingCommand.Execute(null); // 有効位置外/Esc=キャンセル(裁定 c 同型)
        }
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

    /// <summary>ドロップ受け面の種別(ECO-100: クリック配置と同一の挿入表示のみが受ける)。</summary>
    private enum DropTargetKind
    {
        None,
        Before,
        ChildEnd,
        RootEnd,
    }

    /// <summary>
    /// e.Source から受け面(挿入ポイント/「＋ 子にする」)を解決する。挿入表示以外は受けない
    /// (旧・行上=子/空白=ルートの暗黙ドロップは ECO-100 で撤去=挿入表示方式へ一本化)。
    /// </summary>
    private static (DropTargetKind Kind, EditNodeViewModel? Node, Border? Element) ResolveDropTarget(object? source)
    {
        for (var current = source as StyledElement; current is not null; current = current.Parent)
        {
            if (current is not Border border || !border.IsVisible)
            {
                continue;
            }

            if (border.Classes.Contains("makeChild") || border.Classes.Contains("ipChildEnd"))
            {
                return border.DataContext is EditNodeViewModel parent
                    ? (DropTargetKind.ChildEnd, parent, border)
                    : (DropTargetKind.None, null, null);
            }

            if (border.Classes.Contains("ipRootEnd"))
            {
                return (DropTargetKind.RootEnd, null, border);
            }

            if (border.Classes.Contains("ipBefore"))
            {
                return border.DataContext is EditNodeViewModel node
                    ? (DropTargetKind.Before, node, border)
                    : (DropTargetKind.None, null, null);
            }
        }

        return (DropTargetKind.None, null, null);
    }

    private void SetDropHover(Border? element)
    {
        if (ReferenceEquals(_dropHoverElement, element))
        {
            return;
        }

        _dropHoverElement?.Classes.Remove("dropHover");
        _dropHoverElement = element;
        _dropHoverElement?.Classes.Add("dropHover");
    }

    private void OnTreeDragOver(object? sender, DragEventArgs e)
    {
        var payloadOk = e.DataTransfer.Contains(NodeIdFormat) || e.DataTransfer.Contains(TagIdFormat);
        var (kind, _, element) = ResolveDropTarget(e.Source);
        if (ViewModel?.Editor.HasView == true && payloadOk && kind != DropTargetKind.None)
        {
            SetDropHover(element);
            e.DragEffects = e.DataTransfer.Contains(NodeIdFormat) ? DragDropEffects.Move : DragDropEffects.Copy;
        }
        else
        {
            SetDropHover(null);
            e.DragEffects = DragDropEffects.None;
        }

        e.Handled = true;
    }

    /// <summary>ドロップ: 挿入表示の面のみが受ける。行移動(NodeId)/ドラッグ配置(TagId)とも同一の意味論。</summary>
    private void OnTreeDrop(object? sender, DragEventArgs e)
    {
        SetDropHover(null);
        if (ViewModel is not { } vm || vm.Editor.HasView != true)
        {
            return;
        }

        var (kind, node, _) = ResolveDropTarget(e.Source);
        if (kind == DropTargetKind.None)
        {
            return; // 有効位置外=キャンセル(裁定 c)
        }

        if (e.DataTransfer.TryGetValue(NodeIdFormat) is { } nodeId)
        {
            // 行移動(ECO-100)。ドラッグ中ノードと id を突合(別ウィンドウ由来等の異物は無視)
            if (vm.Editor.DraggingNode is { } dragging && string.Equals(dragging.Id, nodeId, StringComparison.Ordinal))
            {
                switch (kind)
                {
                    case DropTargetKind.Before:
                        vm.Editor.MoveBefore(node!);
                        break;
                    case DropTargetKind.ChildEnd:
                        vm.Editor.MoveToChildEnd(node!);
                        break;
                    case DropTargetKind.RootEnd:
                        vm.Editor.MoveToRootEnd();
                        break;
                }
            }

            e.Handled = true;
            return;
        }

        if (e.DataTransfer.TryGetValue(TagIdFormat) is not null)
        {
            // ドラッグ配置(スコープ2): BeginDragPlacing 済みの PlacingTag をクリック配置と同一経路で挿入
            switch (kind)
            {
                case DropTargetKind.Before:
                    vm.Editor.InsertBeforeCommand.Execute(node!);
                    break;
                case DropTargetKind.ChildEnd:
                    vm.Editor.InsertChildEndCommand.Execute(node!);
                    break;
                case DropTargetKind.RootEnd:
                    vm.Editor.InsertRootEndCommand.Execute(null);
                    break;
            }

            e.Handled = true;
        }
    }

    // ---- 階層行(ECO-099/100): クリック(閾値未満リリース)=展開/選択・ドラッグ=行移動(裁定 a) ----

    private void OnNodeRowPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: EditNodeViewModel node } control &&
            e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
        {
            _rowPressNode = node;
            _rowPressArgs = e;
            _rowPressPoint = e.GetPosition(this);
        }
    }

    private async void OnNodeRowMoved(object? sender, PointerEventArgs e)
    {
        if (ViewModel is not { } vm || _rowPressNode is not { } node || _rowPressArgs is not { } press)
        {
            return;
        }

        var delta = e.GetPosition(this) - _rowPressPoint;
        if (Math.Abs(delta.X) < DragThreshold && Math.Abs(delta.Y) < DragThreshold)
        {
            return;
        }

        // 移動ドラッグ確定(ECO-100): クリック配置と同一の挿入表示で受ける。配置モードとは排他
        _rowPressNode = null;
        _rowPressArgs = null;
        vm.Editor.BeginMove(node);
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(NodeIdFormat, node.Id));
        try
        {
            await DragDrop.DoDragDropAsync(press, data, DragDropEffects.Move);
        }
        finally
        {
            SetDropHover(null);
            vm.Editor.EndMove(); // ドロップ成功時は既に解除済み(冪等)。Esc/有効位置外=キャンセル(裁定 c)
        }
    }

    private void OnNodeRowReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (ViewModel is { } vm && _rowPressNode is { } node)
        {
            // クリック確定: 親行=展開/折畳・葉行=選択(CAD インタラクション表)
            if (node.HasChildren)
            {
                node.IsExpanded = !node.IsExpanded;
            }
            else
            {
                vm.Editor.SelectedNode = node;
            }
        }

        _rowPressNode = null;
        _rowPressArgs = null;
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
