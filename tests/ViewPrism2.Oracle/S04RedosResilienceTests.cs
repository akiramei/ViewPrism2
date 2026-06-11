using System.Diagnostics;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using Xunit;

namespace ViewPrism2.Oracle;

/// <summary>
/// S-04: ReDoS 耐性(spec §2.3 K-REGEX、EQ-001)。
/// regexp 条件にカタストロフィックパターン '(a+)+$' を与えて実測でタイムアウト挙動を検証する。
/// 期待: 1 秒タイムアウト → 条件不成立+警告 1 件。プロセスはハング・クラッシュしない。
/// </summary>
[Trait("oracle", "S-04")]
public sealed class S04RedosResilienceTests
{
    private const string TextualTagId = "3e0a8e6a-4444-4a6a-8a6a-000000000001";

    [Fact]
    public void カタストロフィックパターンは1秒タイムアウトで不成立かつ警告1件()
    {
        // オラクル記載の入力(連続 'a' + '!'。バックトラック爆発でマッチは 1 秒では終わらない)
        var tagValue = new string('a', 30) + "!";
        var images = new[]
        {
            new ImageWithTags("img-redos", ImageStatus.Normal,
                [new EvalTagValue(TextualTagId, TagType.Textual, tagValue)]),
        };
        var condition = new ViewCondition
        {
            Id = "c-redos", ViewId = "v", TagId = TextualTagId,
            Operator = ConditionOperator.Regexp, Value = "(a+)+$",
        };

        var stopwatch = Stopwatch.StartNew();
        var result = new ConditionEvaluator().Evaluate(images, [condition]);
        stopwatch.Stop();

        // ハングしない: タイムアウト 1 秒+余裕で 2 秒以内に制御が戻る
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2),
            $"評価が {stopwatch.Elapsed.TotalSeconds:F2} 秒かかりました(2 秒上限)。");

        // 条件不成立
        Assert.Empty(result.MatchedImageIds);

        // 警告 1 件(タイムアウト)
        var warning = Assert.Single(result.Warnings);
        Assert.Equal(EvalWarningKind.RegexTimeout, warning.Kind);
    }
}
