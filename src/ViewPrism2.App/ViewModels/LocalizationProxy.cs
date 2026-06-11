using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ViewPrism2.Core.Services;

namespace ViewPrism2.App.ViewModels;

/// <summary>
/// XAML バインディング用の i18n プロキシ(K-AVALONIA: 文言は LocalizationService 経由のバインディング、
/// XAML 直書き文字列禁止)。CultureChanged でインデクサ変更を通知し、全文言を一斉再バインドする(REQ-051)。
/// </summary>
public sealed class LocalizationProxy : ObservableObject
{
    /// <summary>インデクサの変更通知名(バインディング規約)。</summary>
    private static readonly PropertyChangedEventArgs IndexerChanged = new("Item[]");

    private readonly LocalizationService _localization;

    public LocalizationProxy(LocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(localization);
        _localization = localization;
        _localization.CultureChanged += (_, _) =>
        {
            OnPropertyChanged(IndexerChanged);
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(CurrentLocale)));
        };
    }

    /// <summary>キー → 現在ロケールの文言(OC-8 のフォールバック適用)。</summary>
    public string this[string key] => _localization.T(key);

    public string CurrentLocale => _localization.CurrentLocale;

    public string T(string key, IReadOnlyDictionary<string, string>? args = null) => _localization.T(key, args);
}
