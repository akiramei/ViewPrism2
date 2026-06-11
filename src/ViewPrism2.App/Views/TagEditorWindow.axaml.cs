using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ViewPrism2.App.ViewModels;

namespace ViewPrism2.App.Views;

public partial class TagEditorWindow : Window
{
    /// <summary>候補値の並べ替え D&D のアプリ内データ形式(ドラッグ元の値)。</summary>
    private static readonly DataFormat<string> ValueFormat =
        DataFormat.CreateStringApplicationFormat("viewprism2-predefined-value");

    public TagEditorWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is TagEditorViewModel vm)
            {
                Title = vm.IsCreate ? vm.Loc["tag.editor.createTitle"] : vm.Loc["tag.editor.editTitle"];
                TitleText.Text = Title;
            }
        };
        ValuesList.AddHandler(DragDrop.DragOverEvent, OnValuesDragOver);
        ValuesList.AddHandler(DragDrop.DropEvent, OnValuesDrop);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);

    /// <summary>候補値の D&D 並べ替え(REQ-024 順序保持、仕様 §2.6 v1.2)。</summary>
    private void OnValuePressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(ValuesList).Properties.IsLeftButtonPressed &&
            FindValue(e.Source) is { } value)
        {
            var data = new DataTransfer();
            data.Add(DataTransferItem.Create(ValueFormat, value));
            _ = DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move);
        }
    }

    private void OnValuesDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(ValueFormat) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnValuesDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not TagEditorViewModel vm ||
            e.DataTransfer.TryGetValue(ValueFormat) is not { } moved)
        {
            return;
        }

        var from = vm.PredefinedValues.IndexOf(moved);
        var target = FindValue(e.Source);
        var to = target is null ? vm.PredefinedValues.Count - 1 : vm.PredefinedValues.IndexOf(target);
        vm.MoveValue(from, to);
        vm.SelectedPredefinedValue = moved;
        e.Handled = true;
    }

    private static string? FindValue(object? source)
    {
        var current = source as StyledElement;
        while (current is not null)
        {
            if (current.DataContext is string value)
            {
                return value;
            }

            current = current.Parent;
        }

        return null;
    }
}
