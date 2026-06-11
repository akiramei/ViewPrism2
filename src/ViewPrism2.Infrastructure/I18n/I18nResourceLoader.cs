using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ViewPrism2.Infrastructure.I18n;

/// <summary>
/// 翻訳資産ローダ(M-I18N-011 / ADR-0006 / K-I18N)。
/// Assets/i18n/{ja,en}.json(フラットな「namespace.key」→ 文言の辞書、UTF-8)を読み込み、
/// LocalizationService へ注入する形(ロケール → キー → 文言)に変換する。
/// 欠落・破損ファイルは空辞書として扱い、例外を外へ漏らさない(REQ-050: フォールバックで吸収)。
/// </summary>
public static class I18nResourceLoader
{
    public static readonly IReadOnlyList<string> SupportedLocales = ["ja", "en"];

    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Load(
        string directory, ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);

        var resources = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        foreach (var locale in SupportedLocales)
        {
            resources[locale] = LoadLocale(Path.Combine(directory, locale + ".json"), logger);
        }

        return resources;
    }

    private static IReadOnlyDictionary<string, string> LoadLocale(string path, ILogger? logger)
    {
        try
        {
            if (!File.Exists(path))
            {
                logger?.LogWarning("翻訳資産が見つかりません: {Path}", path);
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }

            var json = File.ReadAllText(path);
            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return map ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            logger?.LogWarning(ex, "翻訳資産の読込に失敗しました: {Path}", path);
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }
}
