using System.Text.Json;
using ViewPrism2.Core.Models;

namespace ViewPrism2.Infrastructure.Settings;

/// <summary>
/// 設定ストア(M-SET-010、REQ-052)。%APPDATA%/ViewPrism2/settings.json。
/// 破損・欠落時は既定値(例外を外へ漏らさない)。破損時は .bak へ退避して再生成する(FMEA-009)。
/// </summary>
public sealed class SettingsStore
{
    public const string FileName = "settings.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly string _directory;

    /// <param name="directory">保存先ディレクトリ。null なら %APPDATA%/ViewPrism2(受入は一時ディレクトリを注入)。</param>
    public SettingsStore(string? directory = null)
    {
        _directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ViewPrism2");
    }

    public string SettingsFilePath => Path.Combine(_directory, FileName);

    /// <summary>破損ファイルの退避先。</summary>
    public string BackupFilePath => SettingsFilePath + ".bak";

    /// <summary>設定を読み込む。欠落は既定値、破損は退避+再生成のうえ既定値(例外なし)。</summary>
    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(SettingsFilePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            return settings ?? RecoverFromCorruption();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return RecoverFromCorruption();
        }
    }

    /// <summary>設定を保存する(インデント付き JSON・UTF-8)。</summary>
    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsFilePath, JsonSerializer.Serialize(settings, JsonOptions));
    }

    private AppSettings RecoverFromCorruption()
    {
        var defaults = new AppSettings();
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                File.Move(SettingsFilePath, BackupFilePath, overwrite: true);
            }

            Save(defaults); // ファイルを再生成(REQ-052)
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 退避・再生成に失敗しても既定値で続行する(例外を外へ漏らさない)
        }

        return defaults;
    }
}
