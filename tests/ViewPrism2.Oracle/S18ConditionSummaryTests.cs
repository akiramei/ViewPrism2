using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using Xunit;

namespace ViewPrism2.Oracle;

/// <summary>
/// S-18: 条件サマリ整形(GF-05 再発防止。spec §2.6 REQ-060(e)、EQ-001)。
/// textual equals 値='男'・values=['男','女']・range(10,20)・range(10,なし)・不正 JSON の 5 入力を ja で整形。
/// 『値が一致: 男』『いずれか: 男, 女』『範囲: 10〜20』『10 以上』、不正 JSON=空文字列(例外なし)。
/// 全出力に '\u' と '{' が含まれない(エスケープ・生 JSON 非露出)。
/// 期待値は spec §2.6 の条件型別 i18n テンプレートから導出(テンプレートを設計者がオラクルに与える)。
/// </summary>
[Trait("oracle", "S-18")]
public sealed class S18ConditionSummaryTests
{
    private static LocalizationService NewService()
    {
        // spec §2.6 が定めた ja テンプレート(formatter の公開キー定数で結線)
        var ja = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ConditionSummaryFormatter.KeyEquals] = "値が一致: {value}",
            [ConditionSummaryFormatter.KeyRange] = "範囲: {valueFrom}〜{valueTo}",
            [ConditionSummaryFormatter.KeyRangeFrom] = "{valueFrom} 以上",
            [ConditionSummaryFormatter.KeyRangeTo] = "{valueTo} 以下",
            [ConditionSummaryFormatter.KeyPattern] = "パターン: {pattern}",
            [ConditionSummaryFormatter.KeyValues] = "いずれか: {values}",
        };
        var resources = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
        {
            ["ja"] = ja,
        };
        return new LocalizationService(resources, initialLocale: "ja");
    }

    private static void AssertNoLeak(string output)
    {
        Assert.DoesNotContain("\\u", output); // Unicode エスケープ非露出
        Assert.DoesNotContain("{", output);   // 生テンプレート/JSON 非露出
    }

    [Fact]
    public void 各条件型がja整形されエスケープと生JSONを露出しない()
    {
        var loc = NewService();

        var equals = ConditionSummaryFormatter.Format(
            HierarchyConditionType.Equals, "{\"value\":\"男\"}", loc);
        Assert.Equal("値が一致: 男", equals);
        AssertNoLeak(equals);

        var values = ConditionSummaryFormatter.Format(
            HierarchyConditionType.Values, "{\"values\":[\"男\",\"女\"]}", loc);
        Assert.Equal("いずれか: 男, 女", values);
        AssertNoLeak(values);

        var range = ConditionSummaryFormatter.Format(
            HierarchyConditionType.Range, "{\"valueFrom\":10,\"valueTo\":20}", loc);
        Assert.Equal("範囲: 10〜20", range);
        AssertNoLeak(range);

        var rangeFrom = ConditionSummaryFormatter.Format(
            HierarchyConditionType.Range, "{\"valueFrom\":10}", loc);
        Assert.Equal("10 以上", rangeFrom);
        AssertNoLeak(rangeFrom);
    }

    [Fact]
    public void 不正JSONとnull型は空文字列で例外を投げない()
    {
        var loc = NewService();

        var broken = ConditionSummaryFormatter.Format(
            HierarchyConditionType.Equals, "{ not json", loc);
        Assert.Equal(string.Empty, broken);

        var noType = ConditionSummaryFormatter.Format(
            conditionType: null, conditionValueJson: "{\"value\":\"男\"}", loc);
        Assert.Equal(string.Empty, noType);
    }
}
