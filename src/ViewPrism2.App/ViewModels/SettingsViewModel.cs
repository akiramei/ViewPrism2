using CommunityToolkit.Mvvm.ComponentModel;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using ViewPrism2.Infrastructure.Settings;

namespace ViewPrism2.App.ViewModels;

/// <summary>言語の選択肢。表示名は固定文言キー(common.languageJapanese / common.languageEnglish)。</summary>
public sealed class LanguageOption : ObservableObject
{
    private readonly LocalizationService _localization;
    private readonly string _labelKey;

    public LanguageOption(LocalizationService localization, string locale, string labelKey)
    {
        _localization = localization;
        Locale = locale;
        _labelKey = labelKey;
        localization.CultureChanged += (_, _) => OnPropertyChanged(nameof(Label));
    }

    public string Locale { get; }

    public string Label => _localization.T(_labelKey);
}

/// <summary>
/// 設定(言語)ダイアログ(M-UI-013、REQ-050〜052、G-5)。
/// 言語切替は即時反映(CultureChanged で全 UI 再バインド)+ settings.json へ永続化(REQ-051)。
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly LocalizationService _localization;
    private readonly AppSettings _settings;
    private readonly SettingsStore _store;

    public SettingsViewModel(LocalizationService localization, AppSettings settings, SettingsStore store)
    {
        _localization = localization;
        _settings = settings;
        _store = store;
        Loc = new LocalizationProxy(localization);

        Languages =
        [
            new(localization, "ja", "common.languageJapanese"),
            new(localization, "en", "common.languageEnglish"),
        ];
        _selectedLanguage = Languages.FirstOrDefault(
            l => string.Equals(l.Locale, localization.CurrentLocale, StringComparison.Ordinal)) ?? Languages[0];
    }

    public LocalizationProxy Loc { get; }

    public IReadOnlyList<LanguageOption> Languages { get; }

    [ObservableProperty]
    private LanguageOption _selectedLanguage;

    partial void OnSelectedLanguageChanged(LanguageOption value)
    {
        // 即時反映(REQ-051: 再起動不要)+ 永続化
        _localization.SetLocale(value.Locale);
        _settings.Locale = value.Locale;
        _store.Save(_settings);
    }
}
