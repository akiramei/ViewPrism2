using System.Globalization;
using ViewPrism2.Core.Models;

namespace ViewPrism2.Core.Services;

/// <summary>
/// NodeGraph 構築の定義値供給契約(REQ-096/ECO-086)。観測値契約(<see cref="ITagValueSource"/>=
/// status:Normal の付与値・INV-010)とは別系統 — 定義値をこちらへ混ぜない(契約の混同禁止)。
/// </summary>
public interface ITagDefinedValueSource
{
    /// <summary>
    /// 当該タグの定義値を**定義順**で返す(textual=predefined_values / numeric=min..max を step 刻みの
    /// InvariantCulture 数値文字列)。定義不能(候補値 0 件・定義域欠落・step≤0・生成数が上限超)なら
    /// null(呼び出し側は警告+観測値へフォールバック=裁定 e)。
    /// </summary>
    IReadOnlyList<string>? GetDefinedValues(string tagId);

    /// <summary>閉じた値集合か(REQ-095 closed)。true なら定義外の付与値を未定義値として検出する。</summary>
    bool IsClosedDomain(string tagId);
}

/// <summary>
/// タグの型別設定スナップショットからの <see cref="ITagDefinedValueSource"/> 実装(REQ-096)。
/// numeric の生成は decimal 演算(浮動小数の刻み誤差で "0.30000000000000004" を作らない)。
/// </summary>
public sealed class TagDefinedValueIndex : ITagDefinedValueSource
{
    /// <summary>定義値ノード数の上限(REQ-096/裁定 e)。超過は定義不能=観測値フォールバック。</summary>
    public const int MaxDefinedValues = 256;

    private readonly Dictionary<string, IReadOnlyList<string>?> _definedByTag;
    private readonly HashSet<string> _closedTags;

    private TagDefinedValueIndex(
        Dictionary<string, IReadOnlyList<string>?> definedByTag, HashSet<string> closedTags)
    {
        _definedByTag = definedByTag;
        _closedTags = closedTags;
    }

    /// <summary>定義なし(全タグ定義不能=観測値フォールバック)。</summary>
    public static TagDefinedValueIndex Empty { get; } =
        new(new Dictionary<string, IReadOnlyList<string>?>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal));

    public static TagDefinedValueIndex Build(
        IReadOnlyDictionary<string, TextualTagSettings> textualSettings,
        IReadOnlyDictionary<string, NumericTagSettings> numericSettings)
    {
        ArgumentNullException.ThrowIfNull(textualSettings);
        ArgumentNullException.ThrowIfNull(numericSettings);

        var defined = new Dictionary<string, IReadOnlyList<string>?>(StringComparer.Ordinal);
        var closed = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (tagId, settings) in textualSettings)
        {
            defined[tagId] = TextualDefinedValues(settings);
            if (settings.ValueDomain == TagValueDomain.Closed)
            {
                closed.Add(tagId);
            }
        }

        foreach (var (tagId, settings) in numericSettings)
        {
            defined[tagId] = NumericDefinedValues(settings);
        }

        return new TagDefinedValueIndex(defined, closed);
    }

    public IReadOnlyList<string>? GetDefinedValues(string tagId)
    {
        ArgumentNullException.ThrowIfNull(tagId);
        return _definedByTag.TryGetValue(tagId, out var values) ? values : null;
    }

    public bool IsClosedDomain(string tagId)
    {
        ArgumentNullException.ThrowIfNull(tagId);
        return _closedTags.Contains(tagId);
    }

    /// <summary>定義順を保持して重複だけ除去。0 件・上限超は定義不能(null)。</summary>
    private static IReadOnlyList<string>? TextualDefinedValues(TextualTagSettings settings)
    {
        if (settings.PredefinedValues.Count is 0 or > MaxDefinedValues)
        {
            return null;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>(settings.PredefinedValues.Count);
        foreach (var value in settings.PredefinedValues)
        {
            if (value is not null && seen.Add(value))
            {
                result.Add(value);
            }
        }

        return result.Count > 0 ? result : null;
    }

    /// <summary>min..max を step 刻みで生成(両端含む)。欠落・step≤0・上限超は定義不能(null)。</summary>
    private static IReadOnlyList<string>? NumericDefinedValues(NumericTagSettings settings)
    {
        if (settings.Min is not { } min || settings.Max is not { } max || settings.Step is not { } step ||
            step <= 0 || max < min)
        {
            return null;
        }

        decimal dMin, dMax, dStep;
        try
        {
            dMin = (decimal)min;
            dMax = (decimal)max;
            dStep = (decimal)step;
        }
        catch (OverflowException)
        {
            return null; // decimal で表せない極端値は定義不能扱い(フォールバック)
        }

        if ((dMax - dMin) / dStep >= MaxDefinedValues)
        {
            return null;
        }

        var result = new List<string>();
        for (var v = dMin; v <= dMax; v += dStep)
        {
            result.Add(v.ToString(CultureInfo.InvariantCulture));
        }

        return result.Count > 0 ? result : null;
    }
}
