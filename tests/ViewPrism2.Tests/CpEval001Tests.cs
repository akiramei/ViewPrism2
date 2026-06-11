using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// CP-EVAL-001: 条件評価器が演算子 5 種・AND 結合・全エッジケース規則(仕様 §2.3)を満たす。
/// 固定フィクスチャ(画像 8 枚 × タグ 4 種)に対する出力 id 集合の完全一致。
/// </summary>
[Trait("cp", "CP-EVAL-001")]
public sealed class CpEval001Tests
{
    private const string SimpleTag = "tag-simple";
    private const string ColorTag = "tag-color";
    private const string RatingTag = "tag-rating";
    private const string NameTag = "tag-name";

    private static readonly ConditionEvaluator Evaluator = new();

    private static EvalTagValue Simple() => new(SimpleTag, TagType.Simple, null);
    private static EvalTagValue Color(string value) => new(ColorTag, TagType.Textual, value);
    private static EvalTagValue Rating(string value) => new(RatingTag, TagType.Numeric, value);
    private static EvalTagValue Name(string value) => new(NameTag, TagType.Textual, value);

    private static ImageWithTags Img(string id, params EvalTagValue[] tags)
        => new(id, ImageStatus.Normal, tags);

    /// <summary>固定フィクスチャ: 画像 8 枚 × タグ 4 種(simple / textual / numeric / textual)。</summary>
    private static readonly IReadOnlyList<ImageWithTags> Fixture =
    [
        Img("img1", Simple(), Color("red"), Rating("5"), Name("IMG_001")),
        Img("img2", Color("Red"), Rating("5.0"), Name("IMG_002")),
        Img("img3", Color("blue"), Rating("1"), Name("DSC_100")),
        Img("img4", Color("a"), Rating("0"), Name("IMG_abc")),
        Img("img5", Color("b"), Rating("6")),
        Img("img6", Color(""), Rating("abc")),
        Img("img7"),
        Img("img8", Simple(), Rating("10")),
    ];

    private static ViewCondition Cond(
        string? tagId, ConditionOperator op, string? value = null, string? value2 = null, string id = "cond-1")
        => new() { Id = id, ViewId = "view-1", TagId = tagId, Operator = op, Value = value, Value2 = value2 };

    private static void AssertMatched(EvaluationResult result, params string[] expectedIds)
    {
        Assert.Equal(
            expectedIds.Order(StringComparer.Ordinal),
            result.MatchedImageIds.Order(StringComparer.Ordinal));
    }

    // ---- exists ----

    [Fact]
    public void Exists_付与ありは含み_なしは含まない()
    {
        var result = Evaluator.Evaluate(Fixture, [Cond(SimpleTag, ConditionOperator.Exists)]);
        AssertMatched(result, "img1", "img8");
        Assert.Empty(result.Warnings);
    }

    // ---- equals(textual) ----

    [Fact]
    public void EqualsTextual_完全一致のみ()
    {
        var result = Evaluator.Evaluate(Fixture, [Cond(ColorTag, ConditionOperator.Equals, "red")]);
        AssertMatched(result, "img1");
    }

    [Fact]
    public void EqualsTextual_は大文字小文字を区別する()
    {
        var result = Evaluator.Evaluate(Fixture, [Cond(ColorTag, ConditionOperator.Equals, "Red")]);
        AssertMatched(result, "img2");
    }

    // ---- equals(numeric) ----

    [Fact]
    public void EqualsNumeric_5と5_0は数値一致()
    {
        var result = Evaluator.Evaluate(Fixture, [Cond(RatingTag, ConditionOperator.Equals, "5")]);
        AssertMatched(result, "img1", "img2");

        var result2 = Evaluator.Evaluate(Fixture, [Cond(RatingTag, ConditionOperator.Equals, "5.0")]);
        AssertMatched(result2, "img1", "img2");
    }

    [Fact]
    public void EqualsNumeric_変換不能なタグ値は不成立()
    {
        // 'abc' と ''(空文字)は数値変換できないため不成立(仕様 §2.3)
        var images = new[] { Img("e1", Rating("abc")), Img("e2", Rating("")) };
        var result = Evaluator.Evaluate(images, [Cond(RatingTag, ConditionOperator.Equals, "0")]);
        AssertMatched(result);
    }

    // ---- between ----

    [Fact]
    public void Between_両端を含み_範囲外を含まない()
    {
        var result = Evaluator.Evaluate(Fixture, [Cond(RatingTag, ConditionOperator.Between, "1", "5")]);
        // 1 含む(img3)・5 含む(img1, img2=5.0)・0(img4)/6(img5) 含まない
        AssertMatched(result, "img1", "img2", "img3");
    }

    [Fact]
    public void Between_9と10は数値順で比較する_FMEA001()
    {
        // 辞書順では '10' < '9' となり img8 が漏れる。数値比較なら 10 ∈ [9,10]
        var result = Evaluator.Evaluate(Fixture, [Cond(RatingTag, ConditionOperator.Between, "9", "10")]);
        AssertMatched(result, "img8");
    }

    // ---- regexp ----

    [Fact]
    public void Regexp_部分一致でマッチする()
    {
        var result = Evaluator.Evaluate(Fixture, [Cond(NameTag, ConditionOperator.Regexp, @"^IMG_\d+")]);
        // IMG_001 / IMG_002 はマッチ、IMG_abc / DSC_100 は不一致
        AssertMatched(result, "img1", "img2");
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Regexp_不正パターンは不成立かつ警告()
    {
        var result = Evaluator.Evaluate(Fixture, [Cond(NameTag, ConditionOperator.Regexp, "(", id: "c-bad")]);
        AssertMatched(result);
        var warning = Assert.Single(result.Warnings);
        Assert.Equal(EvalWarningKind.InvalidRegex, warning.Kind);
        Assert.Equal("c-bad", warning.ConditionId);
    }

    [Fact]
    public void Regexp_タイムアウトは不成立かつ警告()
    {
        // 災害的バックトラッキングを起こすパターン(マッチタイムアウト 1 秒 — REQ-031)
        var images = new[] { Img("t1", Name(new string('a', 60) + "!")) };
        var result = Evaluator.Evaluate(images, [Cond(NameTag, ConditionOperator.Regexp, "^(a|aa)+$", id: "c-slow")]);
        AssertMatched(result);
        var warning = Assert.Single(result.Warnings);
        Assert.Equal(EvalWarningKind.RegexTimeout, warning.Kind);
        Assert.Equal("c-slow", warning.ConditionId);
    }

    // ---- in ----

    [Fact]
    public void In_JSON配列のいずれかと一致()
    {
        var result = Evaluator.Evaluate(Fixture, [Cond(ColorTag, ConditionOperator.In, """["a","b"]""")]);
        AssertMatched(result, "img4", "img5");
    }

    [Fact]
    public void In_リスト外の値は不一致()
    {
        var result = Evaluator.Evaluate(Fixture, [Cond(ColorTag, ConditionOperator.In, """["c"]""")]);
        AssertMatched(result);
    }

    [Fact]
    public void In_不正なJSONは不成立かつ警告()
    {
        var result = Evaluator.Evaluate(Fixture, [Cond(ColorTag, ConditionOperator.In, "not-json", id: "c-json")]);
        AssertMatched(result);
        var warning = Assert.Single(result.Warnings);
        Assert.Equal(EvalWarningKind.InvalidValueList, warning.Kind);
        Assert.Equal("c-json", warning.ConditionId);
    }

    // ---- エッジケース規則 ----

    [Theory]
    [InlineData(ConditionOperator.Exists, null, null)]
    [InlineData(ConditionOperator.Equals, "red", null)]
    [InlineData(ConditionOperator.Between, "0", "10")]
    [InlineData(ConditionOperator.Regexp, ".*", null)]
    [InlineData(ConditionOperator.In, """["red"]""", null)]
    public void タグ未付与画像は全演算子で不成立(ConditionOperator op, string? value, string? value2)
    {
        var result = Evaluator.Evaluate(Fixture, [Cond(ColorTag, op, value, value2)]);
        Assert.DoesNotContain("img7", result.MatchedImageIds);
    }

    [Fact]
    public void SimpleタグへのEqualsは不成立()
    {
        var result = Evaluator.Evaluate(Fixture, [Cond(SimpleTag, ConditionOperator.Equals, "true")]);
        AssertMatched(result);
    }

    [Theory]
    [InlineData(ConditionOperator.Between, "0", "10")]
    [InlineData(ConditionOperator.Regexp, ".*")]
    [InlineData(ConditionOperator.In, """["x"]""")]
    public void Simpleタグへの値演算子は不成立(ConditionOperator op, string value, string? value2 = null)
    {
        var result = Evaluator.Evaluate(Fixture, [Cond(SimpleTag, op, value, value2)]);
        AssertMatched(result);
    }

    [Fact]
    public void ValueがNULLのEquals条件は無視され_他条件は評価継続()
    {
        var result = Evaluator.Evaluate(
            Fixture,
            [
                Cond(ColorTag, ConditionOperator.Equals, value: null, id: "c-null"),
                Cond(SimpleTag, ConditionOperator.Exists, id: "c-exists"),
            ]);

        // 無視された条件は絞り込みに影響せず、exists 条件のみが効く
        AssertMatched(result, "img1", "img8");
        var warning = Assert.Single(result.Warnings);
        Assert.Equal(EvalWarningKind.ConditionIgnored, warning.Kind);
        Assert.Equal("c-null", warning.ConditionId);
    }

    [Fact]
    public void AND結合_片方のみ成立の画像は含まない()
    {
        // img1: simple✓ rating=5✓ / img8: simple✓ rating=10✗ / img2: simple✗ rating=5✓
        var result = Evaluator.Evaluate(
            Fixture,
            [
                Cond(SimpleTag, ConditionOperator.Exists, id: "c-1"),
                Cond(RatingTag, ConditionOperator.Equals, "5", id: "c-2"),
            ]);
        AssertMatched(result, "img1");
    }

    [Fact]
    public void AND結合_互いに素な2条件は空集合()
    {
        var result = Evaluator.Evaluate(
            Fixture,
            [
                Cond(ColorTag, ConditionOperator.Equals, "red", id: "c-1"),
                Cond(RatingTag, ConditionOperator.Between, "9", "10", id: "c-2"),
            ]);
        AssertMatched(result);
    }

    [Fact]
    public void 評価対象はNormalのみ_防御的に再除外する()
    {
        // INV-010: missing/deleted/pending のタグ付け状態は表示系に反映されない
        var images = new[]
        {
            Img("n1", Color("red")),
            new ImageWithTags("m1", ImageStatus.Missing, [Color("red")]),
            new ImageWithTags("p1", ImageStatus.Pending, [Color("red")]),
            new ImageWithTags("d1", ImageStatus.Deleted, [Color("red")]),
        };
        var result = Evaluator.Evaluate(images, [Cond(ColorTag, ConditionOperator.Exists)]);
        AssertMatched(result, "n1");
    }

    [Fact]
    public void 条件なしはNormal全件を返す()
    {
        var result = Evaluator.Evaluate(Fixture, []);
        AssertMatched(result, "img1", "img2", "img3", "img4", "img5", "img6", "img7", "img8");
    }
}
