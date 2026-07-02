using Avalonia;
using Avalonia.Controls;

namespace ViewPrism2.App.Converters;

/// <summary>
/// Grid 列テンプレート文字列(例 "1.7*,120,150,140")を <see cref="Grid.ColumnDefinitions"/> に流し込む添付プロパティ
/// (ECO-025 β)。<c>Grid.ColumnDefinitions</c> はコンパイル XAML では実行時バインド不可のため、添付プロパティ経由で
/// 文字列を束ね、変更時に <see cref="ColumnDefinitions.Parse"/> で設定する。ファイル一覧のヘッダー行と各画像行が
/// 同一文字列を束ね、列位置を一致させる。
/// </summary>
public static class GridColumnsBinder
{
    public static readonly AttachedProperty<string?> TemplateProperty =
        AvaloniaProperty.RegisterAttached<Grid, string?>("Template", typeof(GridColumnsBinder));

    static GridColumnsBinder()
    {
        TemplateProperty.Changed.AddClassHandler<Grid>((grid, e) =>
        {
            var value = e.GetNewValue<string?>();
            grid.ColumnDefinitions = string.IsNullOrWhiteSpace(value)
                ? new ColumnDefinitions()
                : ColumnDefinitions.Parse(value);
        });
    }

    public static void SetTemplate(Grid grid, string? value) => grid.SetValue(TemplateProperty, value);

    public static string? GetTemplate(Grid grid) => grid.GetValue(TemplateProperty);
}
