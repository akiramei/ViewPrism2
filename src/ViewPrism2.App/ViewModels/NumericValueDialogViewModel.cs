using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;

namespace ViewPrism2.App.ViewModels;

/// <summary>
/// numeric タグの値入力ダイアログの VM(M-UI-016、REQ-046)。
/// モード=固定値(全画像に同じ値)|連番(開始値+選択順 i)。min/max/step をダイアログ内で検証し、
/// 範囲外は適用前に拒否する(適用 0 件)。値の生成は選択順整列(FMEA-014)。unit 検査対象。
/// </summary>
public sealed partial class NumericValueDialogViewModel : ObservableObject
{
    private readonly NumericTagSettings? _settings;
    private readonly int _selectionCount;
    private readonly LocalizationService _localization;

    public NumericValueDialogViewModel(
        Tag tag, NumericTagSettings? settings, int selectionCount, LocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(tag);
        Tag = tag;
        _settings = settings;
        _selectionCount = Math.Max(0, selectionCount);
        _localization = localization;
        Loc = new LocalizationProxy(localization);
    }

    public LocalizationProxy Loc { get; }

    public Tag Tag { get; }

    public string TagName => Tag.Name;

    /// <summary>固定値 or 連番(REQ-046)。既定は固定値。</summary>
    [ObservableProperty]
    private bool _isSequential;

    [ObservableProperty]
    private string _fixedValueText = string.Empty;

    [ObservableProperty]
    private string _startValueText = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    public bool IsFixed => !IsSequential;

    public string SelectionCountText => _localization.T("common.selectedCount", new Dictionary<string, string>
    {
        ["count"] = _selectionCount.ToString(CultureInfo.InvariantCulture),
    });

    /// <summary>制約の表示(min/max/step/unit。null 項目は省く)。</summary>
    public string ConstraintText
    {
        get
        {
            var parts = new List<string>();
            if (_settings?.Min is { } min)
            {
                parts.Add($"{_localization.T("tag.editor.minValue")}: {min.ToString(CultureInfo.InvariantCulture)}");
            }

            if (_settings?.Max is { } max)
            {
                parts.Add($"{_localization.T("tag.editor.maxValue")}: {max.ToString(CultureInfo.InvariantCulture)}");
            }

            if (_settings?.Step is { } step)
            {
                parts.Add($"{_localization.T("tag.editor.step")}: {step.ToString(CultureInfo.InvariantCulture)}");
            }

            if (_settings?.Unit is { } unit)
            {
                parts.Add($"{_localization.T("tag.editor.unit")}: {unit}");
            }

            return string.Join(" / ", parts);
        }
    }

    public bool HasConstraint => ConstraintText.Length > 0;

    /// <summary>
    /// 適用値列の生成+検証(選択順整列)。固定値=同値×選択数 / 連番=開始値+選択順 i。
    /// min/max(両端含む)・step 刻みをダイアログ内で検証し、不成立は null(適用 0 件)+ErrorMessage。
    /// </summary>
    public IReadOnlyList<string>? TryBuildValues()
    {
        ErrorMessage = null;
        var text = IsSequential ? StartValueText : FixedValueText;
        if (string.IsNullOrWhiteSpace(text))
        {
            ErrorMessage = _localization.T("tagging.valueRequired");
            return null;
        }

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var baseValue))
        {
            ErrorMessage = _localization.T("error.validationError");
            return null;
        }

        var values = new List<string>(_selectionCount);
        for (var i = 0; i < _selectionCount; i++)
        {
            // 連番=開始値+選択順 i(増分 1、REQ-046)
            var value = IsSequential ? baseValue + i : baseValue;
            if (!Validate(value))
            {
                return null;
            }

            values.Add(value.ToString(CultureInfo.InvariantCulture)); // INV-007
        }

        return values;
    }

    partial void OnIsSequentialChanged(bool value) => OnPropertyChanged(nameof(IsFixed));

    private bool Validate(double value)
    {
        // min/max は両端含む(REQ-025)
        if ((_settings?.Min is { } min && value < min) || (_settings?.Max is { } max && value > max))
        {
            ErrorMessage = _localization.T("tagging.numericDialog.outOfRange");
            return false;
        }

        // step 刻み: min(無ければ 0)を基準に step の整数倍であること(許容誤差 1e-9)
        if (_settings?.Step is { } step && step > 0)
        {
            var basis = _settings.Min ?? 0;
            var ratio = (value - basis) / step;
            if (Math.Abs(ratio - Math.Round(ratio)) > 1e-9)
            {
                ErrorMessage = _localization.T("tagging.numericDialog.stepMismatch");
                return false;
            }
        }

        return true;
    }
}
