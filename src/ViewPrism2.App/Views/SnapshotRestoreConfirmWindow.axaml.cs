using Avalonia.Controls;
using Avalonia.Interactivity;
using ViewPrism2.App.ViewModels;

namespace ViewPrism2.App.Views;

/// <summary>時点復元の確認(ECO-072 A-2)。ShowDialog&lt;bool?&gt; で結果を返す。</summary>
public partial class SnapshotRestoreConfirmWindow : Window
{
    // ランタイムローダ用
    public SnapshotRestoreConfirmWindow()
    {
        InitializeComponent();
    }

    public SnapshotRestoreConfirmWindow(LocalizationProxy loc, SnapshotItemViewModel item)
    {
        InitializeComponent();
        Title = loc["snapshot.restoreTitle"];
        HeadingText.Text = loc["snapshot.restoreHeading"];
        WarningText.Text = loc["snapshot.restoreWarning"];
        TargetLabel.Text = loc["snapshot.restoreTarget"];
        CreatedAtLabel.Text = loc["snapshot.colCreatedAt"];
        CreatedAtValue.Text = item.CreatedAtText;
        AppVersionValue.Text = item.Info.AppVersion ?? "?";
        SizeLabel.Text = loc["snapshot.colSize"];
        SizeValue.Text = item.SizeText;
        VerifyLabel.Text = loc["snapshot.verifyLabel"];
        VerifyValue.Text = item.StatusText;
        RestartNote.Text = loc["snapshot.restoreRestartNote"];
        RollbackNote.Text = loc["snapshot.restoreRollbackNote"];
        NoImagesNote.Text = loc["snapshot.restoreNoImagesNote"];
        CancelButton.Content = loc["common.cancel"];
        RestoreButton.Content = loc["snapshot.restoreExecute"];
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);

    private void OnRestoreClick(object? sender, RoutedEventArgs e) => Close(true);
}
