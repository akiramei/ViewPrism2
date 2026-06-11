using Avalonia.Controls;
using Avalonia.Interactivity;
using ViewPrism2.App.ViewModels;

namespace ViewPrism2.App.Views;

public partial class TagEditorWindow : Window
{
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
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
}
