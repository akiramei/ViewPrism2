using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ViewPrism2.App.Services;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using ViewPrism2.Infrastructure.Database;
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
/// 設定ダイアログ(M-UI-013、REQ-050〜052、G-5)。
/// 言語切替は即時反映(CultureChanged で全 UI 再バインド)+ settings.json へ永続化(REQ-051)。
/// ECO-077(SS-001 再裁定/E-1): 左ナビ(一般/データとバックアップ)+節構成。データとバックアップ節は
/// スナップショット行カード(サマリ=最終作成+件数・L8)とコレクション移送 2 行カードの入口を持つ。
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly LocalizationService _localization;
    private readonly AppSettings _settings;
    private readonly SettingsStore _store;
    private readonly IWindowService? _windows;
    private readonly SnapshotService? _snapshots;

    public SettingsViewModel(
        LocalizationService localization, AppSettings settings, SettingsStore store,
        IWindowService? windows = null, SnapshotService? snapshots = null,
        SettingsSection initialSection = SettingsSection.General)
    {
        _localization = localization;
        _settings = settings;
        _store = store;
        _windows = windows;
        _snapshots = snapshots;
        _selectedSection = initialSection;
        Loc = new LocalizationProxy(localization);
        localization.CultureChanged += (_, _) =>
        {
            // DF-3: Loc 差し替えで全文言バインディングを再評価させる(K-AVALONIA の罠対策)
            Loc = new LocalizationProxy(localization);
            OnPropertyChanged(nameof(Loc));
            RefreshSnapshotSummary();
        };

        Languages =
        [
            new(localization, "ja", "common.languageJapanese"),
            new(localization, "en", "common.languageEnglish"),
        ];
        _selectedLanguage = Languages.FirstOrDefault(
            l => string.Equals(l.Locale, localization.CurrentLocale, StringComparison.Ordinal)) ?? Languages[0];
        RefreshSnapshotSummary();
    }

    public LocalizationProxy Loc { get; private set; }

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

    // ---- E-1 節ナビ(ECO-077/VC-6: 選択中=淡青背景+青文字) ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGeneralSelected), nameof(IsDataBackupSelected))]
    private SettingsSection _selectedSection;

    public bool IsGeneralSelected => SelectedSection == SettingsSection.General;

    public bool IsDataBackupSelected => SelectedSection == SettingsSection.DataBackup;

    [RelayCommand]
    private void SelectGeneral() => SelectedSection = SettingsSection.General;

    [RelayCommand]
    private void SelectDataBackup() => SelectedSection = SettingsSection.DataBackup;

    // ---- E-1 データとバックアップ節(ECO-077) ----

    /// <summary>スナップショット行の副情報(VC-7/L8: 「最終作成 yyyy/MM/dd HH:mm ・ N 件」。0 件=placeholder 最小=SS-004 暫定)。</summary>
    [ObservableProperty]
    private string _snapshotSummary = "";

    private void RefreshSnapshotSummary()
    {
        if (_snapshots is null)
        {
            SnapshotSummary = "";
            return;
        }

        try
        {
            var items = _snapshots.List(_settings.SnapshotDirectory ?? _snapshots.DefaultDirectory);
            SnapshotSummary = items.Count == 0
                ? _localization.T("settings.dataBackup.snapshotSummaryEmpty")
                : _localization.T("settings.dataBackup.snapshotSummary", new Dictionary<string, string>
                {
                    ["date"] = items[0].CreatedAtUtc.ToLocalTime()
                        .ToString("yyyy/MM/dd HH:mm", System.Globalization.CultureInfo.InvariantCulture),
                    ["count"] = items.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SnapshotSummary = ""; // 保存先が読めない場合はサマリなしで入口自体は生かす
        }
    }

    /// <summary>A層の入口(SS-001 再裁定=ECO-077: 設定 ▸ データとバックアップ →[開く]→ A-1)。</summary>
    [RelayCommand]
    private async Task OpenSnapshotsAsync()
    {
        await (_windows?.ShowSnapshotsAsync() ?? Task.CompletedTask);
        RefreshSnapshotSummary(); // A-1 での作成/削除をサマリへ反映
    }

    /// <summary>B層書き出しの入口([選ぶ…]→ B-1。対象コレクションは B-1 内で選択)。</summary>
    [RelayCommand]
    private Task OpenCollectionExportAsync() => _windows?.ShowCollectionExportAsync() ?? Task.CompletedTask;

    /// <summary>B層取り込みの入口([ファイルを選ぶ…]→ B-2。picker 自動起動=ECO-074・取り込み先は B-2 内で選択=案A)。</summary>
    [RelayCommand]
    private Task OpenCollectionImportAsync() => _windows?.ShowCollectionImportAsync() ?? Task.CompletedTask;
}
