using System.Globalization;
using System.Text.Json;

namespace ViewPrism2.Core.Services;

/// <summary>
/// 階層ノードの condition_value JSON の読み取り(仕様 §2.4 のスキーマ):
/// equals={"value":…} / range={"valueFrom":…,"valueTo":…} / pattern={"pattern":…} / values={"values":[…]}。
/// 数値は InvariantCulture の不変文字列表現として取り出す(INV-007)。不正入力で例外を漏らさない。
/// </summary>
internal static class HierarchyConditionValue
{
    /// <summary>JSON オブジェクトの文字列(または数値)プロパティを取り出す。</summary>
    public static bool TryGetString(string? json, string propertyName, out string value)
    {
        value = string.Empty;
        if (TryGetProperty(json, propertyName, out var element) && TryToString(element, out var text))
        {
            value = text;
            return true;
        }

        return false;
    }

    /// <summary>JSON オブジェクトの文字列配列プロパティを取り出す。</summary>
    public static bool TryGetStringArray(string? json, string propertyName, out IReadOnlyList<string> values)
    {
        values = [];
        if (!TryGetProperty(json, propertyName, out var element) || element.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var list = new List<string>();
        foreach (var item in element.EnumerateArray())
        {
            if (TryToString(item, out var text))
            {
                list.Add(text);
            }
        }

        values = list;
        return true;
    }

    /// <summary>range スキーマ {"valueFrom":…,"valueTo":…} を数値ペアとして取り出す。</summary>
    public static bool TryGetRange(string? json, out double from, out double to)
    {
        from = 0;
        to = 0;
        return TryGetString(json, "valueFrom", out var fromText) &&
               TryGetString(json, "valueTo", out var toText) &&
               TryParseNumber(fromText, out from) &&
               TryParseNumber(toText, out to);
    }

    /// <summary>INV-007: InvariantCulture の数値解釈。</summary>
    public static bool TryParseNumber(string text, out double value)
    {
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryGetProperty(string? json, string propertyName, out JsonElement element)
    {
        element = default;
        if (json is null)
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                !doc.RootElement.TryGetProperty(propertyName, out var found))
            {
                return false;
            }

            element = found.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryToString(JsonElement element, out string text)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                text = element.GetString()!;
                return true;
            case JsonValueKind.Number:
                // 数値リテラルは JSON 原文(InvariantCulture 互換の不変表現)をそのまま使う
                text = element.GetRawText();
                return true;
            default:
                text = string.Empty;
                return false;
        }
    }
}
