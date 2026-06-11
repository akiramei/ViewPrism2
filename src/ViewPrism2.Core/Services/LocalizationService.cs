namespace ViewPrism2.Core.Services;

/// <summary>
/// i18n 解決器(OC-8、REQ-050/051、E-I18N-014)。
/// フォールバック順: 要求ロケール → ja → キー文字列(例外を投げない)。
/// リソースは辞書注入(ロケール → キー → 文言)。資産ファイルの読込は Infrastructure 側(後続 Run)。
/// </summary>
public sealed class LocalizationService
{
    /// <summary>既定ロケール(REQ-050: ja 既定)。フォールバック先。</summary>
    public const string DefaultLocale = "ja";

    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> _resources;

    public LocalizationService(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> resources,
        string initialLocale = DefaultLocale)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentException.ThrowIfNullOrWhiteSpace(initialLocale);
        _resources = resources;
        CurrentLocale = initialLocale;
    }

    public string CurrentLocale { get; private set; }

    /// <summary>言語切替の即時反映通知(REQ-051)。UI が一斉再バインドする。</summary>
    public event EventHandler? CultureChanged;

    /// <summary>ロケールを切り替え、変更があれば CultureChanged を発火する。</summary>
    public void SetLocale(string locale)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locale);
        if (string.Equals(locale, CurrentLocale, StringComparison.Ordinal))
        {
            return;
        }

        CurrentLocale = locale;
        CultureChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// キーを文言に解決する(OC-8)。欠落キーは ja へフォールバック、それも無ければキー文字列を返す。
    /// プレースホルダ {name} は args から置換し、未指定プレースホルダはそのまま残す(K-I18N)。
    /// </summary>
    public string T(string key, IReadOnlyDictionary<string, string>? args = null)
    {
        if (key is null)
        {
            return string.Empty; // 例外を投げない(REQ-050)
        }

        var text = key;
        if (_resources.TryGetValue(CurrentLocale, out var current) && current.TryGetValue(key, out var value))
        {
            text = value;
        }
        else if (_resources.TryGetValue(DefaultLocale, out var fallback) && fallback.TryGetValue(key, out var jaValue))
        {
            text = jaValue;
        }

        if (args is { Count: > 0 })
        {
            foreach (var (name, replacement) in args)
            {
                text = text.Replace("{" + name + "}", replacement, StringComparison.Ordinal);
            }
        }

        return text;
    }
}
