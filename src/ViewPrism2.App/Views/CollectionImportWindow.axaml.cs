using Avalonia.Controls;
using Avalonia.Interactivity;
using ViewPrism2.App.ViewModels;

namespace ViewPrism2.App.Views;

/// <summary>コレクションを取り込む(ECO-073 B-2〜B-4 ウィザード)。DataContext=CollectionImportViewModel。</summary>
public partial class CollectionImportWindow : Window
{
    public CollectionImportWindow()
    {
        InitializeComponent();

        // ECO-074 案イ: CAD は B-2 を「互換OK/NG」の 2 状態で定義し未選択を定常状態としない —
        // 表示直後に picker を開く(キャンセル残留時のみプレースホルダ表示)
        Opened += (_, _) =>
        {
            if (DataContext is CollectionImportViewModel { PackagePath: null } vm)
            {
                vm.PickFileCommand.Execute(null);
            }
        };

        // GF-073-07: B-3 は広幅=タグ/画像 2 カラム・狭幅=縦積み(CAD layoutInvariant。
        // Avalonia に ResizeObserver は無いため実測幅で切替=ECO-027 と同じ手法)
        SizeChanged += (_, e) => UpdatePreviewColumns(e.NewSize.Width);
    }

    private void UpdatePreviewColumns(double width)
    {
        var image = this.FindControl<Avalonia.Controls.StackPanel>("ImageColumn");
        if (image is null)
        {
            return;
        }

        var narrow = width < 860; // タグ min440+画像 min360+余白(CAD の回り込みしきい値相当)
        Avalonia.Controls.Grid.SetColumn(image, narrow ? 0 : 2);
        Avalonia.Controls.Grid.SetRow(image, narrow ? 2 : 0);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
