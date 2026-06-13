using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// CP-GF-015: 条件サマリ整形(GF-05・REQ-060(e))が仕様 §2.6 の i18n テンプレートと一致する。
/// ja ロケールの整形文字列を exact 検査。\uXXXX や生 JSON('{')が露出しないことも検査する。
/// </summary>
[Trait("cp", "CP-GF-015")]
public sealed class CpGf015Tests
{
    // 仕様 §2.6 の ja 既定テンプレート(資産 ja.json と同一)
    private static LocalizationService JaLocalization()
    {
        var ja = new Dictionary<string, string>
        {
            [ConditionSummaryFormatter.KeyEquals] = "値が一致: {value}",
            [ConditionSummaryFormatter.KeyRange] = "範囲: {valueFrom}〜{valueTo}",
            [ConditionSummaryFormatter.KeyRangeFrom] = "{valueFrom} 以上",
            [ConditionSummaryFormatter.KeyRangeTo] = "{valueTo} 以下",
            [ConditionSummaryFormatter.KeyPattern] = "パターン: {pattern}",
            [ConditionSummaryFormatter.KeyValues] = "いずれか: {values}",
        };
        var resources = new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["ja"] = ja,
        };
        return new LocalizationService(resources, "ja");
    }

    private static string Format(HierarchyConditionType? type, string? json) =>
        ConditionSummaryFormatter.Format(type, json, JaLocalization());

    [Fact]
    public void Equals_値が一致_生JSONもエスケープも露出しない()
    {
        // {"value":"男"}(Unicode エスケープでも同義 — JSON パース後は '男')
        var result = Format(HierarchyConditionType.Equals, """{"value":"男"}""");
        Assert.Equal("値が一致: 男", result);
        Assert.DoesNotContain("\\u", result, StringComparison.Ordinal);
        Assert.DoesNotContain("{", result, StringComparison.Ordinal);

        // 男 を含む生 JSON でも復号され '男' になる
        var escaped = Format(HierarchyConditionType.Equals, """{"value":"男"}""");
        Assert.Equal("値が一致: 男", escaped);
        Assert.DoesNotContain("\\u", escaped, StringComparison.Ordinal);
        Assert.DoesNotContain("{", escaped, StringComparison.Ordinal);
    }

    [Fact]
    public void Range_両端()
    {
        Assert.Equal("範囲: 10〜20", Format(HierarchyConditionType.Range, """{"valueFrom":10,"valueTo":20}"""));
    }

    [Fact]
    public void Range_片側のみ()
    {
        Assert.Equal("10 以上", Format(HierarchyConditionType.Range, """{"valueFrom":10}"""));
        Assert.Equal("20 以下", Format(HierarchyConditionType.Range, """{"valueTo":20}"""));
    }

    [Fact]
    public void Pattern()
    {
        Assert.Equal("パターン: ^IMG", Format(HierarchyConditionType.Pattern, """{"pattern":"^IMG"}"""));
    }

    [Fact]
    public void Values_区切りはカンマ空白()
    {
        var result = Format(HierarchyConditionType.Values, """{"values":["男","女"]}""");
        Assert.Equal("いずれか: 男, 女", result);
        Assert.DoesNotContain("\\u", result, StringComparison.Ordinal);
        Assert.DoesNotContain("[", result, StringComparison.Ordinal);
        Assert.DoesNotContain("{", result, StringComparison.Ordinal);
    }

    [Fact]
    public void ConditionType_NULLは空文字列()
    {
        Assert.Equal(string.Empty, Format(null, """{"value":"x"}"""));
    }

    [Fact]
    public void 不正JSONは例外を投げずフォールバック空文字列()
    {
        Assert.Equal(string.Empty, Format(HierarchyConditionType.Equals, "{{{ broken"));
        Assert.Equal(string.Empty, Format(HierarchyConditionType.Range, "not json"));
        Assert.Equal(string.Empty, Format(HierarchyConditionType.Values, "[1,2,3"));
        Assert.Equal(string.Empty, Format(HierarchyConditionType.Pattern, null));
    }
}
