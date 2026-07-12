using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ViewPrism2.App.ViewModels;

/// <summary>
/// 検証状態バッジの配色/グリフ(ECO-072/GF-072-01・CAD snapshot_export_import A-1 mock 権威)。
/// 緑=検証済み(✓)/黄=検証待ち(時計)。CAD「突き合わせ結果の状態バッジ配色」パターンの実体。
/// </summary>
public static class SnapshotBadgeConverters
{
    private static readonly SolidColorBrush VerifiedBg = new(Color.Parse("#E7F6EC"));
    private static readonly SolidColorBrush VerifiedBorder = new(Color.Parse("#C4E7D2"));
    private static readonly SolidColorBrush VerifiedFg = new(Color.Parse("#1F7A3D"));
    private static readonly SolidColorBrush UnverifiedBg = new(Color.Parse("#FCF4DC"));
    private static readonly SolidColorBrush UnverifiedBorder = new(Color.Parse("#EDDFAF"));
    private static readonly SolidColorBrush UnverifiedFg = new(Color.Parse("#8A6D1A"));
    private static readonly Geometry CheckGlyph = Geometry.Parse("M1,5.5 L4.5,9 L11,1.5");
    private static readonly Geometry ClockGlyph = Geometry.Parse(
        "M8,1.5 A6.5,6.5 0 1 0 8,14.5 A6.5,6.5 0 1 0 8,1.5 M8,4.5 L8,8 L11,9.5");

    public static readonly IValueConverter Background =
        new FuncValueConverter<bool, IBrush>(v => v ? VerifiedBg : UnverifiedBg);

    public static readonly IValueConverter Border =
        new FuncValueConverter<bool, IBrush>(v => v ? VerifiedBorder : UnverifiedBorder);

    public static readonly IValueConverter Foreground =
        new FuncValueConverter<bool, IBrush>(v => v ? VerifiedFg : UnverifiedFg);

    public static readonly IValueConverter Glyph =
        new FuncValueConverter<bool, Geometry>(v => v ? CheckGlyph : ClockGlyph);
}
