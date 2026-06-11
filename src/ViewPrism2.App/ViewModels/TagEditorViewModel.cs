using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Repositories;
using ViewPrism2.Core.Services;

namespace ViewPrism2.App.ViewModels;

/// <summary>タグ種別の選択肢(作成時のみ変更可)。</summary>
public sealed class TagTypeOption : ObservableObject
{
    private readonly LocalizationService _localization;
    private readonly string _labelKey;

    public TagTypeOption(LocalizationService localization, TagType value, string labelKey)
    {
        _localization = localization;
        Value = value;
        _labelKey = labelKey;
        localization.CultureChanged += (_, _) => OnPropertyChanged(nameof(Label));
    }

    public TagType Value { get; }

    public string Label => _localization.T(_labelKey);
}

/// <summary>カラープリセットの 1 色(v1.2 タグ作成ダイアログ: プリセット+hex 表示)。</summary>
public sealed record ColorPresetViewModel(string Hex);

/// <summary>
/// タグ編集ダイアログ(E-UI-TAGS-026、REQ-021〜025、v1.2 §2.6)。
/// 名前・種別(既存は変更不可)・カラー(プリセット+hex 表示)・説明・
/// numeric 設定(min/max/step/unit)・textual 定義済み値(追加・削除・並べ替え、REQ-024)。
/// 検証は TagService(core)に委譲する。
/// </summary>
public sealed partial class TagEditorViewModel : ObservableObject
{
    /// <summary>
    /// プリセットカラー(原典 colorPicker の 18 カテゴリに対応する Material 500 値。
    /// K-DESIGN に定義が無いため工場判断 — cheat-log 参照)。
    /// </summary>
    public static readonly IReadOnlyList<ColorPresetViewModel> ColorPresets =
    [
        new("#F44336"), new("#E91E63"), new("#9C27B0"), new("#673AB7"), new("#3F51B5"), new("#2196F3"),
        new("#03A9F4"), new("#00BCD4"), new("#009688"), new("#4CAF50"), new("#8BC34A"), new("#CDDC39"),
        new("#FFEB3B"), new("#FFC107"), new("#FF9800"), new("#795548"), new("#9E9E9E"), new("#607D8B"),
    ];

    private readonly TagService _tagService;
    private readonly ITagRepository _tags;
    private readonly LocalizationService _localization;
    private readonly Tag? _existing;

    public TagEditorViewModel(
        Tag? existing, TagService tagService, ITagRepository tags, LocalizationService localization)
    {
        _existing = existing;
        _tagService = tagService;
        _tags = tags;
        _localization = localization;
        Loc = new LocalizationProxy(localization);

        TypeOptions =
        [
            new(localization, TagType.Simple, "tag.type.simple"),
            new(localization, TagType.Textual, "tag.type.textual"),
            new(localization, TagType.Numeric, "tag.type.numeric"),
        ];
        _selectedType = TypeOptions[0];

        if (existing is not null)
        {
            _name = existing.Name;
            _color = existing.Color ?? string.Empty;
            _description = existing.Description ?? string.Empty;
            _selectedType = TypeOptions.First(o => o.Value == existing.Type);
        }
    }

    public LocalizationProxy Loc { get; }

    public IReadOnlyList<TagTypeOption> TypeOptions { get; }

    public bool IsCreate => _existing is null;

    /// <summary>既存タグの種類は変更できない(原典 UI と同じ制約)。</summary>
    public bool TypeEditable => IsCreate;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private TagTypeOption _selectedType;

    [ObservableProperty]
    private string _color = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _minText = string.Empty;

    [ObservableProperty]
    private string _maxText = string.Empty;

    [ObservableProperty]
    private string _stepText = string.Empty;

    [ObservableProperty]
    private string _unit = string.Empty;

    public ObservableCollection<string> PredefinedValues { get; } = [];

    [ObservableProperty]
    private string? _selectedPredefinedValue;

    [ObservableProperty]
    private string _newValueText = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    public bool IsTextual => SelectedType.Value == TagType.Textual;

    public bool IsNumeric => SelectedType.Value == TagType.Numeric;

    /// <summary>保存成功(ウィンドウが閉じる)。</summary>
    public event EventHandler? Saved;

    public async Task LoadAsync()
    {
        if (_existing is null)
        {
            return;
        }

        if (_existing.Type == TagType.Textual)
        {
            var settings = await _tags.GetTextualSettingsAsync(_existing.Id);
            foreach (var value in settings?.PredefinedValues ?? [])
            {
                PredefinedValues.Add(value);
            }
        }
        else if (_existing.Type == TagType.Numeric)
        {
            var settings = await _tags.GetNumericSettingsAsync(_existing.Id);
            MinText = settings?.Min?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            MaxText = settings?.Max?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            StepText = settings?.Step?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            Unit = settings?.Unit ?? string.Empty;
        }
    }

    [RelayCommand]
    private void AddPredefinedValue()
    {
        var value = NewValueText.Trim();
        if (value.Length > 0 && !PredefinedValues.Contains(value, StringComparer.Ordinal))
        {
            PredefinedValues.Add(value);
            NewValueText = string.Empty;
        }
    }

    [RelayCommand]
    private void RemovePredefinedValue()
    {
        if (SelectedPredefinedValue is { } value)
        {
            PredefinedValues.Remove(value);
        }
    }

    [RelayCommand]
    private void MoveValueUp() => MoveSelected(-1);

    [RelayCommand]
    private void MoveValueDown() => MoveSelected(+1);

    /// <summary>プリセットカラーの選択(hex 表示欄へ反映)。</summary>
    [RelayCommand]
    private void PickColor(ColorPresetViewModel preset) => Color = preset.Hex;

    /// <summary>候補値の D&D 並べ替え(順序保持、REQ-024。View 層の Drop ハンドラから呼ぶ)。</summary>
    public void MoveValue(int fromIndex, int toIndex)
    {
        if (fromIndex >= 0 && fromIndex < PredefinedValues.Count &&
            toIndex >= 0 && toIndex < PredefinedValues.Count &&
            fromIndex != toIndex)
        {
            PredefinedValues.Move(fromIndex, toIndex);
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorMessage = null;

        double? min = null, max = null, step = null;
        if (IsNumeric &&
            (!TryParseOptional(MinText, out min) ||
             !TryParseOptional(MaxText, out max) ||
             !TryParseOptional(StepText, out step)))
        {
            ErrorMessage = _localization.T("error.validationError");
            return;
        }

        var color = string.IsNullOrWhiteSpace(Color) ? null : Color.Trim();
        var description = string.IsNullOrWhiteSpace(Description) ? null : Description;

        Tag tag;
        if (_existing is null)
        {
            var created = await _tagService.CreateAsync(Name, SelectedType.Value, null, color, description);
            if (!created.IsSuccess)
            {
                ErrorMessage = ErrorMessages.Resolve(_localization, created.Error);
                return;
            }

            tag = created.Value!;
        }
        else
        {
            var updated = await _tagService.UpdateAsync(_existing with
            {
                Name = Name,
                Color = color,
                Description = description,
            });
            if (!updated.IsSuccess)
            {
                ErrorMessage = ErrorMessages.Resolve(_localization, updated.Error);
                return;
            }

            tag = updated.Value!;
        }

        if (tag.Type == TagType.Textual)
        {
            var result = await _tagService.SetTextualSettingsAsync(tag.Id, PredefinedValues.ToList());
            if (!result.IsSuccess)
            {
                ErrorMessage = ErrorMessages.Resolve(_localization, result.Error);
                return;
            }
        }
        else if (tag.Type == TagType.Numeric)
        {
            var unit = string.IsNullOrWhiteSpace(Unit) ? null : Unit.Trim();
            var result = await _tagService.SetNumericSettingsAsync(tag.Id, min, max, step, unit);
            if (!result.IsSuccess)
            {
                ErrorMessage = ErrorMessages.Resolve(_localization, result.Error);
                return;
            }
        }

        Saved?.Invoke(this, EventArgs.Empty);
    }

    partial void OnSelectedTypeChanged(TagTypeOption value)
    {
        OnPropertyChanged(nameof(IsTextual));
        OnPropertyChanged(nameof(IsNumeric));
    }

    private void MoveSelected(int delta)
    {
        if (SelectedPredefinedValue is not { } value)
        {
            return;
        }

        var index = PredefinedValues.IndexOf(value);
        var target = index + delta;
        if (index >= 0 && target >= 0 && target < PredefinedValues.Count)
        {
            PredefinedValues.Move(index, target);
            SelectedPredefinedValue = value;
        }
    }

    /// <summary>空文字は null、その他は InvariantCulture の数値(INV-007)。不正は false。</summary>
    private static bool TryParseOptional(string text, out double? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            value = parsed;
            return true;
        }

        return false;
    }
}
