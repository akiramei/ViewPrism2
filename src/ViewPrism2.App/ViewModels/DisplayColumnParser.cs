using System.Text.Json;
using ViewPrism2.Core.Models;

namespace ViewPrism2.App.ViewModels;

public enum DisplayColumnKind
{
    Basic,
    Tag,
}

/// <summary>リスト表示列の定義 1 件(REQ-042)。Star は列幅の比率(M-BOM: name=2*、他=1*)。</summary>
public sealed record DisplayColumn(DisplayColumnKind Kind, string Key, string? Label, double Star);

/// <summary>
/// display_columns(JSON 配列)の解釈(REQ-042)。列 = {type: basic|tag, key, label, width}。
/// basic.key ∈ {name, size, modified_date}。tag.key=タグ id。
/// 削除済みタグの列は無視して描画し、残り列の star で全幅を按分する(AUDIT-102 / INV-008)。
/// 不正 JSON・列 0 件は既定 3 列(name 2*・size 1*・modified_date 1*)。
/// </summary>
public static class DisplayColumnParser
{
    private static readonly HashSet<string> BasicKeys = new(StringComparer.Ordinal)
    {
        "name", "size", "modified_date",
    };

    public static IReadOnlyList<DisplayColumn> Parse(string? json, IReadOnlyDictionary<string, Tag> tagById)
    {
        ArgumentNullException.ThrowIfNull(tagById);

        if (string.IsNullOrWhiteSpace(json))
        {
            return Defaults();
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return Defaults();
            }

            var columns = new List<DisplayColumn>();
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                if (TryParseColumn(element, tagById) is { } column)
                {
                    columns.Add(column);
                }
            }

            return columns.Count > 0 ? columns : Defaults();
        }
        catch (JsonException)
        {
            return Defaults(); // INV-008: 破損データで停止しない
        }
    }

    private static DisplayColumn? TryParseColumn(JsonElement element, IReadOnlyDictionary<string, Tag> tagById)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !TryGetString(element, "type", out var type) ||
            !TryGetString(element, "key", out var key))
        {
            return null;
        }

        string? label = TryGetString(element, "label", out var l) ? l : null;
        var star = GetStar(element, key);

        switch (type)
        {
            case "basic":
                return BasicKeys.Contains(key) ? new DisplayColumn(DisplayColumnKind.Basic, key, label, star) : null;

            case "tag":
                // 削除済みタグの列は無視(REQ-042)。残り列の star がそのまま全幅按分になる
                return tagById.TryGetValue(key, out var tag)
                    ? new DisplayColumn(DisplayColumnKind.Tag, key, label ?? tag.Name, star)
                    : null;

            default:
                return null;
        }
    }

    private static double GetStar(JsonElement element, string key)
    {
        if (element.TryGetProperty("width", out var width) &&
            width.ValueKind == JsonValueKind.Number &&
            width.TryGetDouble(out var value) && value > 0)
        {
            return value;
        }

        return DefaultStar(key);
    }

    /// <summary>M-BOM silence_sweep: width は star 値(name=2*、他=1*)。</summary>
    private static double DefaultStar(string key) => key == "name" ? 2 : 1;

    private static IReadOnlyList<DisplayColumn> Defaults() =>
    [
        new(DisplayColumnKind.Basic, "name", null, 2),
        new(DisplayColumnKind.Basic, "size", null, 1),
        new(DisplayColumnKind.Basic, "modified_date", null, 1),
    ];

    private static bool TryGetString(JsonElement element, string name, out string value)
    {
        value = string.Empty;
        if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            value = prop.GetString()!;
            return true;
        }

        return false;
    }
}
