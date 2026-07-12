using Avalonia.Data.Converters;

namespace ViewPrism2.App.ViewModels;

/// <summary>B-3 競合行の状態ラベル(ECO-073・CAD 文言=解決済み/要対応)。</summary>
public static class PackageConverters
{
    public static readonly IValueConverter ResolvedLabel =
        new FuncValueConverter<bool, string>(v => v ? "解決済み" : "要対応");
}
