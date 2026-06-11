using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ViewPrism2.App.ViewModels;

namespace ViewPrism2.App.Views;

/// <summary>
/// タグタブの View(M-UI-013 v1.2)。ロジックは TagsTabViewModel/HierarchyEditorViewModel に置き、
/// ここはポインタイベント→VM 呼び出し・TreeView 選択同期・タグパレット→階層の D&D のみ(K-MVVM)。
/// </summary>
public partial class TagsTabView : UserControl
{
    /// <summary>パレット→階層 D&D のアプリ内データ形式(タグ id)。</summary>
    private static readonly DataFormat<string> TagIdFormat =
        DataFormat.CreateStringApplicationFormat("viewprism2-tag-id");

    public TagsTabView()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, OnTreeDragOver);
        AddHandler(DragDrop.DropEvent, OnTreeDrop);
    }

    private TagsTabViewModel? ViewModel => DataContext as TagsTabViewModel;

    private void OnViewRowPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel is { } vm &&
            sender is Control { DataContext: ViewRowViewModel row } control &&
            e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
        {
            vm.SelectViewCommand.Execute(row);
        }
    }

    /// <summary>パレット項目: クリック=選択+ドラッグ開始(D&D でノード追加、仕様 §2.6)。</summary>
    private void OnPaletteItemPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel is not { } vm ||
            sender is not Control { DataContext: TagPaletteRowViewModel row } control ||
            !e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
        {
            return;
        }

        vm.Palette.SelectedTag = row;

        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(TagIdFormat, row.Tag.Id));
        _ = DragDrop.DoDragDropAsync(e, data, DragDropEffects.Copy);
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

    private void OnHierarchySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ViewModel is { } vm)
        {
            vm.Editor.SelectedNode = HierarchyTree.SelectedItem as EditNodeViewModel;
        }
    }

    /// <summary>別名インライン編集: Enter=確定 / Escape=取消(仕様 §2.6)。</summary>
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
