using System.Diagnostics;
using System.Globalization;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// CP-NFR-001(L3): 条件評価 1,000 画像(各 5 タグ付与)× 3 条件 ≦ 200ms
/// (3 回計測の中央値、ウォームアップ 1 回後 — NFR-001)。
/// </summary>
[Trait("cp", "CP-NFR-001")]
[Trait("category", "perf")]
public sealed class CpNfr001Tests
{
    private const string SimpleTag = "tag-simple";
    private const string ColorTag = "tag-color";
    private const string RatingTag = "tag-rating";
    private const string NameTag = "tag-name";
    private const string GroupTag = "tag-group";

    [Fact]
    public void 評価レイテンシ_中央値200ms以内()
    {
        var images = BuildImages(count: 1000);
        IReadOnlyList<ViewCondition> conditions =
        [
            new() { Id = "c1", ViewId = "v", TagId = SimpleTag, Operator = ConditionOperator.Exists },
            new() { Id = "c2", ViewId = "v", TagId = RatingTag, Operator = ConditionOperator.Between, Value = "10", Value2 = "50" },
            new() { Id = "c3", ViewId = "v", TagId = NameTag, Operator = ConditionOperator.Regexp, Value = @"^IMG_\d+" },
        ];
        var evaluator = new ConditionEvaluator();

        // ウォームアップ 1 回
        var warmup = evaluator.Evaluate(images, conditions);
        Assert.NotEmpty(warmup.MatchedImageIds);

        var elapsed = new List<long>(3);
        for (var run = 0; run < 3; run++)
        {
            var watch = Stopwatch.StartNew();
            evaluator.Evaluate(images, conditions);
            watch.Stop();
            elapsed.Add(watch.ElapsedMilliseconds);
        }

        elapsed.Sort();
        var median = elapsed[1];
        Assert.True(median <= 200, $"中央値 {median}ms(計測値: {string.Join(", ", elapsed)}ms)が 200ms を超過");
    }

    private static IReadOnlyList<ImageWithTags> BuildImages(int count)
    {
        var images = new List<ImageWithTags>(count);
        for (var i = 0; i < count; i++)
        {
            // 各画像 5 タグ付与(NFR-001)
            images.Add(new ImageWithTags(
                $"img-{i:D4}",
                ImageStatus.Normal,
                [
                    new EvalTagValue(SimpleTag, TagType.Simple, null),
                    new EvalTagValue(ColorTag, TagType.Textual, "color-" + (i % 10).ToString(CultureInfo.InvariantCulture)),
                    new EvalTagValue(RatingTag, TagType.Numeric, (i % 100).ToString(CultureInfo.InvariantCulture)),
                    new EvalTagValue(NameTag, TagType.Textual, $"IMG_{i:D4}"),
                    new EvalTagValue(GroupTag, TagType.Textual, "group-" + (i % 5).ToString(CultureInfo.InvariantCulture)),
                ]));
        }

        return images;
    }
}
