using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using ViewPrism2.App.Services;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;

namespace ViewPrism2.App.ViewModels;

/// <summary>条件種別の選択肢(null=条件なし)。</summary>
public sealed record ConditionTypeOption(HierarchyConditionType? Value, string Label);

/// <summary>
/// 階層ノードの条件設定ダイアログ VM(仕様 §2.6 v1.2 / §2.4 condition_value スキーマ)。
/// textual → equals / pattern / values、numeric → equals / range(+いずれも「条件なし」)。
/// condition_value JSON の生成と入力検証(InvariantCulture 数値・正規表現の事前検証)を行う。unit 検査対象。
/// </summary>
public sealed partial class NodeConditionDialogViewModel : ObservableObject
{
    private readonly Tag _tag;
    private readonly LocalizationService _localization;

    public NodeConditionDialogViewModel(
        Tag tag, HierarchyConditionType? currentType, string? currentValueJson, LocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(tag);
        _tag = tag;
        _localization = localization;
        Loc = new LocalizationProxy(localization);

        var options = new List<ConditionTypeOption>
        {
            new(null, localization.T("hierarchy.conditionType.none")),
            new(HierarchyConditionType.Equals, localization.T("hierarchy.conditionType.equals")),
        };
        if (tag.Type == TagType.Numeric)
        {
            options.Add(new(HierarchyConditionType.Range, localization.T("hierarchy.conditionType.range")));
        }
        else
        {
            options.Add(new(HierarchyConditionType.Pattern, localization.T("hierarchy.conditionType.pattern")));
            options.Add(new(HierarchyConditionType.Values, localization.T("hierarchy.conditionType.values")));
        }

        TypeOptions = options;
        _selectedType = TypeOptions.FirstOrDefault(o => o.Value == currentType) ?? TypeOptions[0];
        LoadCurrent(currentType, currentValueJson);
    }

    public LocalizationProxy Loc { get; }

    public string TagName => _tag.Name;

    public IReadOnlyList<ConditionTypeOption> TypeOptions { get; }

    [ObservableProperty]
    private ConditionTypeOption _selectedType;

    [ObservableProperty]
    private string _equalsValueText = string.Empty;

    [ObservableProperty]
    private string _rangeFromText = string.Empty;

    [ObservableProperty]
    private string _rangeToText = string.Empty;

    [ObservableProperty]
    private string _patternText = string.Empty;

    /// <summary>values 用(1 行に 1 値)。</summary>
    [ObservableProperty]
    private string _valuesText = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    public bool IsEquals => SelectedType.Value == HierarchyConditionType.Equals;

    public bool IsRange => SelectedType.Value == HierarchyConditionType.Range;

    public bool IsPattern => SelectedType.Value == HierarchyConditionType.Pattern;

    public bool IsValues => SelectedType.Value == HierarchyConditionType.Values;

    /// <summary>検証+condition_value JSON の生成(仕様 §2.4 のスキーマ)。不正は null+ErrorMessage。</summary>
    public NodeConditionResult? TryBuildResult()
    {
        ErrorMessage = null;
        switch (SelectedType.Value)
        {
            case null:
                return new NodeConditionResult(null, null);

            case HierarchyConditionType.Equals:
            {
                if (EqualsValueText.Length == 0 || (_tag.Type == TagType.Numeric && !IsNumber(EqualsValueText)))
                {
                    ErrorMessage = _localization.T("error.validationError");
                    return null;
                }

                return new NodeConditionResult(
                    HierarchyConditionType.Equals,
                    JsonSerializer.Serialize(new { value = EqualsValueText }));
            }

            case HierarchyConditionType.Range:
            {
                if (!IsNumber(RangeFromText) || !IsNumber(RangeToText) ||
                    Parse(RangeFromText) > Parse(RangeToText))
                {
                    ErrorMessage = _localization.T("error.validationError");
                    return null;
                }

                return new NodeConditionResult(
                    HierarchyConditionType.Range,
                    JsonSerializer.Serialize(new { valueFrom = RangeFromText, valueTo = RangeToText }));
            }

            case HierarchyConditionType.Pattern:
            {
                // K-REGEX: パターン長上限 1024。不正パターンは入力時に拒否
                if (PatternText.Length is 0 or > 1024)
                {
                    ErrorMessage = _localization.T("error.invalidRegex");
                    return null;
                }

                try
                {
                    _ = new Regex(PatternText, RegexOptions.None, TimeSpan.FromSeconds(1));
                }
                catch (ArgumentException)
                {
                    ErrorMessage = _localization.T("error.invalidRegex");
                    return null;
                }

                return new NodeConditionResult(
                    HierarchyConditionType.Pattern,
                    JsonSerializer.Serialize(new { pattern = PatternText }));
            }

            case HierarchyConditionType.Values:
            default:
            {
                var values = ValuesText
                    .Split('\n')
                    .Select(v => v.TrimEnd('\r'))
                    .Where(v => v.Length > 0)
                    .ToArray();
                if (values.Length == 0)
                {
                    ErrorMessage = _localization.T("error.validationError");
                    return null;
                }

                return new NodeConditionResult(
                    HierarchyConditionType.Values,
                    JsonSerializer.Serialize(new { values }));
            }
        }
    }

    partial void OnSelectedTypeChanged(ConditionTypeOption value)
    {
        OnPropertyChanged(nameof(IsEquals));
        OnPropertyChanged(nameof(IsRange));
        OnPropertyChanged(nameof(IsPattern));
        OnPropertyChanged(nameof(IsValues));
    }

    private void LoadCurrent(HierarchyConditionType? type, string? json)
    {
        if (json is null)
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            switch (type)
            {
                case HierarchyConditionType.Equals when doc.RootElement.TryGetProperty("value", out var v):
                    EqualsValueText = AsText(v);
                    break;
                case HierarchyConditionType.Range:
                    if (doc.RootElement.TryGetProperty("valueFrom", out var from))
                    {
                        RangeFromText = AsText(from);
                    }

                    if (doc.RootElement.TryGetProperty("valueTo", out var to))
                    {
                        RangeToText = AsText(to);
                    }

                    break;
                case HierarchyConditionType.Pattern when doc.RootElement.TryGetProperty("pattern", out var p):
                    PatternText = AsText(p);
                    break;
                case HierarchyConditionType.Values when doc.RootElement.TryGetProperty("values", out var list) &&
                                                        list.ValueKind == JsonValueKind.Array:
                    ValuesText = string.Join('\n', list.EnumerateArray().Select(AsText));
                    break;
                default:
                    break;
            }
        }
        catch (JsonException)
        {
            // 不正 JSON は空のまま(INV-008: 例外で停止しない)
        }
    }

    private static string AsText(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString() ?? string.Empty,
        JsonValueKind.Number => element.GetRawText(),
        _ => string.Empty,
    };

    private static bool IsNumber(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _);

    private static double Parse(string text) =>
        double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
}
