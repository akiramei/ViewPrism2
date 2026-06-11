using System.Text.Json;
using ViewPrism2.Core.Models;

namespace ViewPrism2.Infrastructure.Database;

/// <summary>
/// 列挙値・JSON 列の文字列表現(M-DB-007)。カラム値は原典準拠の小文字トークン
/// (仕様 §2.0〜2.4 の表記: normal/simple/exists/name/asc/equals 等)。
/// </summary>
internal static class DbMapping
{
    public static string ToDb(this ImageStatus status) => status switch
    {
        ImageStatus.Normal => "normal",
        ImageStatus.Missing => "missing",
        ImageStatus.Deleted => "deleted",
        ImageStatus.Pending => "pending",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    public static ImageStatus ToImageStatus(string value) => value switch
    {
        "normal" => ImageStatus.Normal,
        "missing" => ImageStatus.Missing,
        "deleted" => ImageStatus.Deleted,
        "pending" => ImageStatus.Pending,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static string ToDb(this TagType type) => type switch
    {
        TagType.Simple => "simple",
        TagType.Textual => "textual",
        TagType.Numeric => "numeric",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    public static TagType ToTagType(string value) => value switch
    {
        "simple" => TagType.Simple,
        "textual" => TagType.Textual,
        "numeric" => TagType.Numeric,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static string ToDb(this ConditionOperator op) => op switch
    {
        ConditionOperator.Exists => "exists",
        ConditionOperator.Equals => "equals",
        ConditionOperator.Between => "between",
        ConditionOperator.Regexp => "regexp",
        ConditionOperator.In => "in",
        _ => throw new ArgumentOutOfRangeException(nameof(op), op, null),
    };

    public static ConditionOperator ToConditionOperator(string value) => value switch
    {
        "exists" => ConditionOperator.Exists,
        "equals" => ConditionOperator.Equals,
        "between" => ConditionOperator.Between,
        "regexp" => ConditionOperator.Regexp,
        "in" => ConditionOperator.In,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static string ToDb(this SortField field) => field switch
    {
        SortField.Name => "name",
        SortField.CreatedDate => "created_date",
        SortField.ModifiedDate => "modified_date",
        SortField.FileSize => "file_size",
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
    };

    public static SortField ToSortField(string value) => value switch
    {
        "name" => SortField.Name,
        "created_date" => SortField.CreatedDate,
        "modified_date" => SortField.ModifiedDate,
        "file_size" => SortField.FileSize,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static string ToDb(this SortDirection direction) => direction switch
    {
        SortDirection.Asc => "asc",
        SortDirection.Desc => "desc",
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null),
    };

    public static SortDirection ToSortDirection(string value) => value switch
    {
        "asc" => SortDirection.Asc,
        "desc" => SortDirection.Desc,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static string? ToDb(this HierarchyConditionType? type) => type switch
    {
        null => null,
        HierarchyConditionType.Equals => "equals",
        HierarchyConditionType.Range => "range",
        HierarchyConditionType.Pattern => "pattern",
        HierarchyConditionType.Values => "values",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    public static HierarchyConditionType? ToHierarchyConditionType(string? value) => value switch
    {
        null => null,
        "equals" => HierarchyConditionType.Equals,
        "range" => HierarchyConditionType.Range,
        "pattern" => HierarchyConditionType.Pattern,
        "values" => HierarchyConditionType.Values,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    /// <summary>文字列配列 ↔ JSON TEXT 列(exclude_patterns / predefined_values)。</summary>
    public static string ToJsonArray(IReadOnlyList<string> values) => JsonSerializer.Serialize(values);

    public static IReadOnlyList<string> FromJsonArray(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return []; // INV-008: 破損データで停止しない
        }
    }
}
