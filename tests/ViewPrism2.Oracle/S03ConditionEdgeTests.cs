using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using Xunit;

namespace ViewPrism2.Oracle;

/// <summary>
/// S-03: 条件評価複合(spec §2.3 演算子表+エッジケース規則、EQ-001)。
/// 画像 6 枚(タグ未付与/simple のみ/numeric '5'/'5.0'/'abc'/textual 'Red')に
/// [exists(simple), between(1,5), equals('red')] を個別評価する。
/// </summary>
[Trait("oracle", "S-03")]
public sealed class S03ConditionEdgeTests
{
    private const string SimpleTagId = "2e0a8e6a-3333-4a6a-8a6a-000000000001";
    private const string NumericTagId = "2e0a8e6a-3333-4a6a-8a6a-000000000002";
    private const string TextualTagId = "2e0a8e6a-3333-4a6a-8a6a-000000000003";

    private static readonly IReadOnlyList<ImageWithTags> Images =
    [
        new ImageWithTags("img-1", ImageStatus.Normal, []),                                                       // タグ未付与
        new ImageWithTags("img-2", ImageStatus.Normal, [new EvalTagValue(SimpleTagId, TagType.Simple, null)]),    // simple のみ
        new ImageWithTags("img-3", ImageStatus.Normal, [new EvalTagValue(NumericTagId, TagType.Numeric, "5")]),   // numeric '5'
        new ImageWithTags("img-4", ImageStatus.Normal, [new EvalTagValue(NumericTagId, TagType.Numeric, "5.0")]), // numeric '5.0'
        new ImageWithTags("img-5", ImageStatus.Normal, [new EvalTagValue(NumericTagId, TagType.Numeric, "abc")]), // 数値変換不能
        new ImageWithTags("img-6", ImageStatus.Normal, [new EvalTagValue(TextualTagId, TagType.Textual, "Red")]), // textual 'Red'
    ];

    private static IReadOnlyList<string> EvaluateSingle(ViewCondition condition)
    {
        var result = new ConditionEvaluator().Evaluate(Images, [condition]);
        return result.MatchedImageIds.Order(StringComparer.Ordinal).ToList();
    }

    [Fact]
    public void exists_simple_はsimple付与画像のみ成立()
    {
        var matched = EvaluateSingle(new ViewCondition
        {
            Id = "c-exists", ViewId = "v", TagId = SimpleTagId, Operator = ConditionOperator.Exists,
        });

        Assert.Equal(["img-2"], matched);
    }

    [Fact]
    public void between_1_5_は数値5と5_0が成立しabcと未付与は不成立()
    {
        var matched = EvaluateSingle(new ViewCondition
        {
            Id = "c-between", ViewId = "v", TagId = NumericTagId,
            Operator = ConditionOperator.Between, Value = "1", Value2 = "5",
        });

        // '5' と '5.0' は数値比較で成立(INV-007)。'abc' は変換不能で不成立。未付与は不成立
        Assert.Equal(["img-3", "img-4"], matched);
    }

    [Fact]
    public void equals_red_はcase_sensitiveでRedに不成立()
    {
        var matched = EvaluateSingle(new ViewCondition
        {
            Id = "c-equals", ViewId = "v", TagId = TextualTagId,
            Operator = ConditionOperator.Equals, Value = "red",
        });

        Assert.Empty(matched);
    }
}
