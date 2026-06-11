using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using ViewPrism2.Core.Models;

namespace ViewPrism2.Core.Services;

/// <summary>条件評価の入力 1 件分: タグ付与状態(タグ種別+値)。</summary>
public sealed record EvalTagValue(string TagId, TagType TagType, string? Value);

/// <summary>条件評価の入力: 画像 1 件(タグ付け状態付き)(OC-1)。</summary>
public sealed record ImageWithTags(string ImageId, ImageStatus Status, IReadOnlyList<EvalTagValue> Tags);

/// <summary>評価中に発生した警告の種別(REQ-031 エッジケース規則)。</summary>
public enum EvalWarningKind
{
    /// <summary>必須入力の欠落(value=null 等)により条件を評価から除外した。</summary>
    ConditionIgnored,

    /// <summary>不正な正規表現パターン(条件不成立)。</summary>
    InvalidRegex,

    /// <summary>正規表現マッチのタイムアウト(1 秒、条件不成立)。</summary>
    RegexTimeout,

    /// <summary>in 演算子の JSON 配列が不正(条件不成立)。</summary>
    InvalidValueList,
}

/// <summary>評価警告(UI 通知用、M-EVAL-002 interface_contract.warnings)。</summary>
public sealed record EvalWarning(string? ConditionId, EvalWarningKind Kind, string Message);

/// <summary>条件評価の出力: 条件を満たす画像 id 集合+警告列。</summary>
public sealed record EvaluationResult(IReadOnlySet<string> MatchedImageIds, IReadOnlyList<EvalWarning> Warnings);

/// <summary>
/// 条件評価器(OC-1、REQ-031)。全条件を AND 結合し status=Normal の画像集合を絞り込む。
/// 例外を外へ漏らさない。数値比較は常に InvariantCulture の数値として行う(INV-007)。
/// </summary>
public sealed class ConditionEvaluator
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    /// <summary>パターン長上限(K-REGEX)。超過は不正パターン扱い。</summary>
    private const int MaxPatternLength = 1024;

    public EvaluationResult Evaluate(IEnumerable<ImageWithTags> images, IReadOnlyList<ViewCondition> conditions)
    {
        ArgumentNullException.ThrowIfNull(images);
        ArgumentNullException.ThrowIfNull(conditions);

        var warnings = new List<EvalWarning>();
        var compiled = new List<CompiledCondition>(conditions.Count);
        foreach (var condition in conditions)
        {
            var cc = CompiledCondition.Create(condition, warnings);
            if (cc is not null)
            {
                compiled.Add(cc);
            }
        }

        var matched = new HashSet<string>(StringComparer.Ordinal);
        foreach (var image in images)
        {
            // INV-010: 評価対象は Normal のみ(入力前に除外済みでも防御的に再除外)
            if (image.Status != ImageStatus.Normal)
            {
                continue;
            }

            if (SatisfiesAll(image, compiled, warnings))
            {
                matched.Add(image.ImageId);
            }
        }

        return new EvaluationResult(matched, warnings);
    }

    private static bool SatisfiesAll(ImageWithTags image, List<CompiledCondition> compiled, List<EvalWarning> warnings)
    {
        if (compiled.Count == 0)
        {
            return true; // 条件なし=無条件
        }

        Dictionary<string, EvalTagValue>? lookup = null;
        foreach (var tag in image.Tags)
        {
            lookup ??= new Dictionary<string, EvalTagValue>(StringComparer.Ordinal);
            lookup[tag.TagId] = tag; // INV-003 により高々 1 行(重複入力は後勝ちで防御)
        }

        foreach (var condition in compiled)
        {
            if (!condition.Matches(lookup, warnings))
            {
                return false; // AND 結合
            }
        }

        return true;
    }

    /// <summary>条件 1 件の前処理済み表現。不正入力はここで warning に変換する。</summary>
    private sealed class CompiledCondition
    {
        private readonly ViewCondition _condition;
        private readonly Regex? _regex;
        private readonly IReadOnlyList<string>? _inValues;
        private readonly double? _equalsNumber;
        private readonly double _betweenLow;
        private readonly double _betweenHigh;
        private readonly bool _alwaysFalse;
        private bool _timeoutWarned;

        private CompiledCondition(
            ViewCondition condition,
            Regex? regex,
            IReadOnlyList<string>? inValues,
            double? equalsNumber,
            double betweenLow,
            double betweenHigh,
            bool alwaysFalse)
        {
            _condition = condition;
            _regex = regex;
            _inValues = inValues;
            _equalsNumber = equalsNumber;
            _betweenLow = betweenLow;
            _betweenHigh = betweenHigh;
            _alwaysFalse = alwaysFalse;
        }

        /// <summary>評価可能な形に前処理する。条件を評価から除外する場合は null を返す(警告計上)。</summary>
        public static CompiledCondition? Create(ViewCondition condition, List<EvalWarning> warnings)
        {
            // INV-008: 参照切れ(タグ削除で SET NULL)の条件は無視しフォールバック
            if (condition.TagId is null)
            {
                warnings.Add(new EvalWarning(
                    condition.Id, EvalWarningKind.ConditionIgnored, "条件の対象タグが存在しないため無視しました。"));
                return null;
            }

            // REQ-031: operator≠exists で value が NULL の条件は評価から除外+警告
            if (condition.Operator != ConditionOperator.Exists && condition.Value is null)
            {
                warnings.Add(new EvalWarning(
                    condition.Id, EvalWarningKind.ConditionIgnored, "value が未設定のため条件を無視しました。"));
                return null;
            }

            // between の上限欠落も必須入力の欠落として同様に除外(value=NULL 規則の準用)
            if (condition.Operator == ConditionOperator.Between && condition.Value2 is null)
            {
                warnings.Add(new EvalWarning(
                    condition.Id, EvalWarningKind.ConditionIgnored, "value2 が未設定のため between 条件を無視しました。"));
                return null;
            }

            switch (condition.Operator)
            {
                case ConditionOperator.Regexp:
                {
                    var pattern = condition.Value!;
                    if (pattern.Length > MaxPatternLength)
                    {
                        warnings.Add(new EvalWarning(
                            condition.Id, EvalWarningKind.InvalidRegex, "正規表現パターンが長さ上限(1024 文字)を超えています。"));
                        return new CompiledCondition(condition, null, null, null, 0, 0, alwaysFalse: true);
                    }

                    try
                    {
                        var regex = new Regex(pattern, RegexOptions.None, MatchTimeout);
                        return new CompiledCondition(condition, regex, null, null, 0, 0, alwaysFalse: false);
                    }
                    catch (ArgumentException)
                    {
                        // 不正パターン → 条件不成立+警告(REQ-031)
                        warnings.Add(new EvalWarning(
                            condition.Id, EvalWarningKind.InvalidRegex, "不正な正規表現パターンのため条件は不成立です。"));
                        return new CompiledCondition(condition, null, null, null, 0, 0, alwaysFalse: true);
                    }
                }

                case ConditionOperator.In:
                {
                    var values = TryParseStringArray(condition.Value!);
                    if (values is null)
                    {
                        // 不正 JSON → 条件不成立+警告(不正 regex 規則の準用)
                        warnings.Add(new EvalWarning(
                            condition.Id, EvalWarningKind.InvalidValueList, "in 条件の値リスト(JSON 配列)が不正のため条件は不成立です。"));
                        return new CompiledCondition(condition, null, null, null, 0, 0, alwaysFalse: true);
                    }

                    return new CompiledCondition(condition, null, values, null, 0, 0, alwaysFalse: false);
                }

                case ConditionOperator.Between:
                {
                    if (!TryParseInvariant(condition.Value!, out var low) ||
                        !TryParseInvariant(condition.Value2!, out var high))
                    {
                        // 数値変換できない境界値 → 条件不成立+警告
                        warnings.Add(new EvalWarning(
                            condition.Id, EvalWarningKind.ConditionIgnored, "between の境界値が数値でないため条件は不成立です。"));
                        return new CompiledCondition(condition, null, null, null, 0, 0, alwaysFalse: true);
                    }

                    return new CompiledCondition(condition, null, null, null, low, high, alwaysFalse: false);
                }

                case ConditionOperator.Equals:
                {
                    double? equalsNumber =
                        TryParseInvariant(condition.Value!, out var n) ? n : null;
                    return new CompiledCondition(condition, null, null, equalsNumber, 0, 0, alwaysFalse: false);
                }

                case ConditionOperator.Exists:
                default:
                    return new CompiledCondition(condition, null, null, null, 0, 0, alwaysFalse: false);
            }
        }

        public bool Matches(IReadOnlyDictionary<string, EvalTagValue>? tags, List<EvalWarning> warnings)
        {
            if (_alwaysFalse)
            {
                return false;
            }

            // 対象タグが付与されていない画像 → いずれの演算子でも不成立(REQ-031)
            if (tags is null || !tags.TryGetValue(_condition.TagId!, out var tag))
            {
                return false;
            }

            if (_condition.Operator == ConditionOperator.Exists)
            {
                return true;
            }

            // simple タグ(value=NULL)への equals/between/regexp/in → 不成立(REQ-031)
            if (tag.Value is null)
            {
                return false;
            }

            switch (_condition.Operator)
            {
                case ConditionOperator.Equals:
                    if (tag.TagType == TagType.Numeric)
                    {
                        // INV-007: 数値比較(辞書順比較禁止)。変換不能値(空文字含む)は不成立
                        return _equalsNumber is { } expected &&
                               TryParseInvariant(tag.Value, out var actual) &&
                               actual == expected;
                    }

                    return string.Equals(tag.Value, _condition.Value, StringComparison.Ordinal);

                case ConditionOperator.Between:
                    return TryParseInvariant(tag.Value, out var v) && v >= _betweenLow && v <= _betweenHigh;

                case ConditionOperator.Regexp:
                    try
                    {
                        return _regex!.IsMatch(tag.Value);
                    }
                    catch (RegexMatchTimeoutException)
                    {
                        // タイムアウト(1 秒) → 条件不成立+警告(条件ごとに 1 回)
                        if (!_timeoutWarned)
                        {
                            _timeoutWarned = true;
                            warnings.Add(new EvalWarning(
                                _condition.Id, EvalWarningKind.RegexTimeout, "正規表現マッチがタイムアウトしたため条件は不成立です。"));
                        }

                        return false;
                    }

                case ConditionOperator.In:
                    return _inValues!.Contains(tag.Value, StringComparer.Ordinal);

                default:
                    return false;
            }
        }

        private static bool TryParseInvariant(string text, out double value)
        {
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static IReadOnlyList<string>? TryParseStringArray(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                {
                    return null;
                }

                var values = new List<string>();
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    if (element.ValueKind == JsonValueKind.String)
                    {
                        values.Add(element.GetString()!);
                    }
                }

                return values;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
