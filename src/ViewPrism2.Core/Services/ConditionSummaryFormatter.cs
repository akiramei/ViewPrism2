using ViewPrism2.Core.Models;

namespace ViewPrism2.Core.Services;

/// <summary>
/// 階層ノードの条件サマリを人間可読に整形する(GF-05・REQ-060(e)、仕様 §2.6)。
/// 書式は i18n リソースの条件型別テンプレートキーで定義する(ja 既定の文面は K-I18N 資産)。
/// 格納値の JSON 形式(REQ-034)は不変 — 本整形は表示のみ。
/// JSON 文字列や Unicode エスケープ(\uXXXX)・生 JSON を画面に露出しない。
/// 不正 JSON は例外を投げずフォールバック(空文字列)。純粋計算(M-UI-019。CP-GF-015 unit 検査対象)。
/// </summary>
public static class ConditionSummaryFormatter
{
    // i18n テンプレートキー(条件型別。仕様 §2.6 — ja 既定の文面は資産側)
    public const string KeyEquals = "hierarchy.conditionSummary.equals";       // 値が一致: {value}
    public const string KeyRange = "hierarchy.conditionSummary.range";         // 範囲: {valueFrom}〜{valueTo}
    public const string KeyRangeFrom = "hierarchy.conditionSummary.rangeFrom"; // {valueFrom} 以上
    public const string KeyRangeTo = "hierarchy.conditionSummary.rangeTo";     // {valueTo} 以下
    public const string KeyPattern = "hierarchy.conditionSummary.pattern";     // パターン: {pattern}
    public const string KeyValues = "hierarchy.conditionSummary.values";       // いずれか: {values}

    /// <summary>区切り(values の連結。仕様 §2.6: ", ")。</summary>
    public const string ValuesSeparator = ", ";

    /// <summary>区切り(range の上下限連結。仕様 §2.6: "〜")。</summary>
    public const string RangeSeparator = "〜";

    /// <summary>
    /// 条件型と格納 JSON を、ロケールの条件型別テンプレートで整形した表示文字列にする。
    /// conditionType=null は空文字列(サマリなし)。不正 JSON もフォールバックで空文字列。
    /// </summary>
    public static string Format(HierarchyConditionType? conditionType, string? conditionValueJson, LocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(localization);
        if (conditionType is null)
        {
            return string.Empty;
        }

        switch (conditionType.Value)
        {
            case HierarchyConditionType.Equals:
                if (HierarchyConditionValue.TryGetString(conditionValueJson, "value", out var value))
                {
                    return localization.T(KeyEquals, new Dictionary<string, string> { ["value"] = value });
                }

                return string.Empty;

            case HierarchyConditionType.Pattern:
                if (HierarchyConditionValue.TryGetString(conditionValueJson, "pattern", out var pattern))
                {
                    return localization.T(KeyPattern, new Dictionary<string, string> { ["pattern"] = pattern });
                }

                return string.Empty;

            case HierarchyConditionType.Range:
                return FormatRange(conditionValueJson, localization);

            case HierarchyConditionType.Values:
                if (HierarchyConditionValue.TryGetStringArray(conditionValueJson, "values", out var values))
                {
                    var joined = string.Join(ValuesSeparator, values);
                    return localization.T(KeyValues, new Dictionary<string, string> { ["values"] = joined });
                }

                return string.Empty;

            default:
                return string.Empty;
        }
    }

    private static string FormatRange(string? json, LocalizationService localization)
    {
        var hasFrom = HierarchyConditionValue.TryGetString(json, "valueFrom", out var from) && from.Length > 0;
        var hasTo = HierarchyConditionValue.TryGetString(json, "valueTo", out var to) && to.Length > 0;

        if (hasFrom && hasTo)
        {
            return localization.T(KeyRange, new Dictionary<string, string>
            {
                ["valueFrom"] = from,
                ["valueTo"] = to,
            });
        }

        if (hasFrom)
        {
            return localization.T(KeyRangeFrom, new Dictionary<string, string> { ["valueFrom"] = from });
        }

        if (hasTo)
        {
            return localization.T(KeyRangeTo, new Dictionary<string, string> { ["valueTo"] = to });
        }

        return string.Empty;
    }
}
