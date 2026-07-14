using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Repositories;
using ViewPrism2.Core.Services;

namespace ViewPrism2.App.ViewModels;

/// <summary>タグ種別の選択肢(作成時のみ変更可。ECO-007/E6 セグメントタブの 1 トグル)。</summary>
public sealed partial class TagTypeOption : ObservableObject
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

    /// <summary>このタブが選択中か(ECO-007/E6 セグメントの強調・IsChecked バインド)。</summary>
    [ObservableProperty]
    private bool _isActive;
}

/// <summary>カラープリセットの 1 色(v1.2 タグ作成ダイアログ: プリセット+hex 表示)。</summary>
public sealed record ColorPresetViewModel(string Hex);

/// <summary>付与プレビューの候補値チップ(ECO-007/E5 テキスト型・axaml バインド用)。</summary>
public sealed record TagPreviewChipViewModel(string Label, bool IsSelected);

/// <summary>付与プレビューの★ 1 個(ECO-007/E5 数値★モード・点灯/非点灯)。</summary>
public sealed record TagPreviewStarViewModel(bool IsFilled);

/// <summary>
/// タグ編集ダイアログ(E-UI-TAGS-026、REQ-021〜025、v1.2 §2.6)。
/// 名前・種別(既存は変更不可)・カラー(プリセット+hex 表示)・説明・
/// numeric 設定(min/max/step/unit)・textual 定義済み値(追加・削除・並べ替え、REQ-024)。
/// 検証は TagService(core)に委譲する。
/// </summary>
public sealed partial class TagEditorViewModel : ObservableObject
{
    /// <summary>
    /// プリセットカラー(ECO-007/E7・E-DESIGN-028: モックの 9 色(Radix 系)。
    /// 従来の Material-500 18 色を置換。色は自由 hex 継続(プリセットは入力ショートカット)。
    /// </summary>
    public static readonly IReadOnlyList<ColorPresetViewModel> ColorPresets =
    [
        new("#e5484d"), new("#f2912b"), new("#e8b931"), new("#30a46c"), new("#12a594"),
        new("#2f6bed"), new("#8b5cf6"), new("#e93d82"), new("#5b6473"),
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
        localization.CultureChanged += (_, _) =>
        {
            // DF-3: Loc 差し替えで全文言バインディングを再評価させる(K-AVALONIA の罠対策)
            Loc = new LocalizationProxy(localization);
            OnPropertyChanged(nameof(Loc));
            OnPropertyChanged(nameof(DomainNote)); // VM 算出文言も追随(ECO-079 GF-079-01 教訓)
            OnPropertyChanged(nameof(Subtitle));
            OnPropertyChanged(nameof(OptionCountText));
        };

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

        // ECO-007/E6: セグメントタブの選択強調(IsActive)を初期同期する
        SyncTypeActive();

        // 候補値の追加/削除/並べ替えでプレビュー(テキストチップ)と件数ヘッダを再評価する
        PredefinedValues.CollectionChanged += (_, _) =>
        {
            RaisePreviewChanged();
            OnPropertyChanged(nameof(OptionCountText)); // ECO-087/VC-TAG-6
            OnPropertyChanged(nameof(NoOptions));
            OnPropertyChanged(nameof(HasOptions));
        };

        // Loc 差し替え(CultureChanged)でプレビューのプレースホルダも再評価
        localization.CultureChanged += (_, _) => RaisePreviewChanged();
    }

    public LocalizationProxy Loc { get; private set; }

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

    /// <summary>値の扱い(REQ-095/ECO-086 裁定 b)。false=入力補助(既定)/true=閉じた値集合。</summary>
    [ObservableProperty]
    private bool _isClosedDomain;

    public bool IsSuggestDomain => !IsClosedDomain;

    /// <summary>値の扱いの説明カード(選択に追随・CAD VC-TAG-1)。</summary>
    public string DomainNote => _localization.T(
        IsClosedDomain ? "tag.editor.valueDomainClosedNote" : "tag.editor.valueDomainSuggestNote");

    [ObservableProperty]
    private string? _selectedPredefinedValue;

    [ObservableProperty]
    private string _newValueText = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    public bool IsTextual => SelectedType.Value == TagType.Textual;

    public bool IsNumeric => SelectedType.Value == TagType.Numeric;

    public bool IsSimple => SelectedType.Value == TagType.Simple;

    /// <summary>ヘッダの種別サブタイトル(ECO-087/VC-TAG-5: 種別セグメント切替で追随)。</summary>
    public string Subtitle => _localization.T(SelectedType.Value switch
    {
        TagType.Textual => "tag.editor.subtitle.textual",
        TagType.Numeric => "tag.editor.subtitle.numeric",
        _ => "tag.editor.subtitle.simple",
    });

    /// <summary>「登録済みの選択肢 (N)」(ECO-087/VC-TAG-6)。</summary>
    public string OptionCountText => _localization.T("tag.editor.registeredOptions",
        new Dictionary<string, string>
        {
            ["count"] = PredefinedValues.Count.ToString(CultureInfo.InvariantCulture),
        });

    /// <summary>候補値 0 件(破線プレースホルダの表示制御・ECO-087/VC-TAG-6)。</summary>
    public bool NoOptions => PredefinedValues.Count == 0;

    public bool HasOptions => PredefinedValues.Count > 0;

    /// <summary>
    /// 付与プレビュー(DC-TAGPREVIEW-001・E5)。「画像に付けたときの見え方」を種別別に整形する。
    /// 整形は核側ヘルパ TagAssignmentPreviewBuilder(unit 検査可)。名前・色・種別・候補値・数値設定の
    /// 変化で再評価する。★モード(単位=★・span 0..9 整数)/ テキスト候補チップ / 数値ラベルを供給。
    /// </summary>
    public TagAssignmentPreview Preview => TagAssignmentPreviewBuilder.Build(
        SelectedType.Value,
        Name,
        Color,
        PredefinedValues,
        BuildNumericForPreview(),
        _localization.T(TagAssignmentPreviewBuilder.NamePlaceholderKey));

    /// <summary>テキスト型プレビューの候補値チップ(axaml バインド用・先頭が選択強調)。</summary>
    public IReadOnlyList<TagPreviewChipViewModel> PreviewChips =>
        Preview.Chips.Select(c => new TagPreviewChipViewModel(c.Label, c.IsSelected)).ToList();

    /// <summary>★モードのプレビュー(NumericStar 時)で ★ 並びを描画するための列挙。</summary>
    public IReadOnlyList<TagPreviewStarViewModel> PreviewStars
    {
        get
        {
            var preview = Preview;
            if (preview.Kind != TagPreviewKind.NumericStar)
            {
                return [];
            }

            var stars = new List<TagPreviewStarViewModel>(preview.TotalStars);
            for (var i = 0; i < preview.TotalStars; i++)
            {
                stars.Add(new TagPreviewStarViewModel(i < preview.FilledStars)); // 先頭から FilledStars 個が点灯
            }

            return stars;
        }
    }

    public bool IsPreviewTextual => Preview.Kind == TagPreviewKind.Textual;

    public bool IsPreviewStar => Preview.Kind == TagPreviewKind.NumericStar;

    public bool IsPreviewNumericPlain => Preview.Kind == TagPreviewKind.NumericPlain;

    /// <summary>数値型のプレビュー(±ステッパ・★)を表示するか。</summary>
    public bool IsPreviewNumeric => IsPreviewStar || IsPreviewNumericPlain;

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

            IsClosedDomain = settings?.ValueDomain == TagValueDomain.Closed; // REQ-095
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

    /// <summary>入力が空の間は非活性(ECO-087/mock DD02 の addReady)。</summary>
    public bool CanAddValue => NewValueText.Trim().Length > 0;

    [RelayCommand(CanExecute = nameof(CanAddValue))]
    private void AddPredefinedValue()
    {
        var value = NewValueText.Trim();
        if (value.Length > 0 && !PredefinedValues.Contains(value, StringComparer.Ordinal))
        {
            PredefinedValues.Add(value);
            NewValueText = string.Empty;
        }
    }

    partial void OnNewValueTextChanged(string value)
    {
        OnPropertyChanged(nameof(CanAddValue));
        AddPredefinedValueCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void RemovePredefinedValue()
    {
        if (SelectedPredefinedValue is { } value)
        {
            PredefinedValues.Remove(value);
        }
    }

    /// <summary>ピル行の × 削除(ECO-087/VC-TAG-6: 行ごとの削除ボタン)。</summary>
    [RelayCommand]
    private void RemovePredefinedValueItem(string value) => PredefinedValues.Remove(value);

    /// <summary>数値プリセット(ECO-087/mock DD02: ★評価 1–5 / % 0–100 / pt 0–10)。</summary>
    [RelayCommand]
    private void ApplyNumericPreset(string preset)
    {
        (MinText, MaxText, StepText, Unit) = preset switch
        {
            "percent" => ("0", "100", "5", "%"),
            "point" => ("0", "10", "1", "pt"),
            _ => ("1", "5", "1", "★"),
        };
    }

    [RelayCommand]
    private void MoveValueUp() => MoveSelected(-1);

    [RelayCommand]
    private void MoveValueDown() => MoveSelected(+1);

    /// <summary>プリセットカラーの選択(hex 表示欄へ反映)。</summary>
    [RelayCommand]
    private void PickColor(ColorPresetViewModel preset) => Color = preset.Hex;

    /// <summary>
    /// 種別セグメントタブの選択(ECO-007/E6)。TypeEditable=false(既存タグ)では非活性のため呼ばれない
    /// (axaml の IsEnabled で抑止。ECO-008: 種別は作成後変更不可)。
    /// </summary>
    [RelayCommand]
    private void SelectType(TagTypeOption option)
    {
        if (!TypeEditable)
        {
            return;
        }

        SelectedType = option;
    }

    /// <summary>値の扱いセグメント(REQ-095)。入力補助を選択。</summary>
    [RelayCommand]
    private void SelectDomainSuggest() => IsClosedDomain = false;

    /// <summary>値の扱いセグメント(REQ-095)。閉じた値集合を選択。</summary>
    [RelayCommand]
    private void SelectDomainClosed() => IsClosedDomain = true;

    partial void OnIsClosedDomainChanged(bool value)
    {
        OnPropertyChanged(nameof(IsSuggestDomain));
        OnPropertyChanged(nameof(DomainNote));
    }

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
            var result = await _tagService.SetTextualSettingsAsync(
                tag.Id, PredefinedValues.ToList(),
                IsClosedDomain ? TagValueDomain.Closed : TagValueDomain.Suggest);
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
        SyncTypeActive();
        OnPropertyChanged(nameof(IsTextual));
        OnPropertyChanged(nameof(IsNumeric));
        OnPropertyChanged(nameof(IsSimple));
        OnPropertyChanged(nameof(Subtitle)); // ECO-087/VC-TAG-5: 種別切替でヘッダ追随
        RaisePreviewChanged();
    }

    /// <summary>ECO-007/E6: 現在の SelectedType に合わせて各タブの IsActive を更新する。</summary>
    private void SyncTypeActive()
    {
        foreach (var option in TypeOptions)
        {
            option.IsActive = ReferenceEquals(option, SelectedType);
        }
    }

    partial void OnNameChanged(string value) => RaisePreviewChanged();

    partial void OnColorChanged(string value) => RaisePreviewChanged();

    partial void OnMinTextChanged(string value) => RaisePreviewChanged();

    partial void OnMaxTextChanged(string value) => RaisePreviewChanged();

    partial void OnUnitChanged(string value) => RaisePreviewChanged();

    /// <summary>付与プレビュー(DC-TAGPREVIEW-001)の全派生プロパティを再評価する。</summary>
    private void RaisePreviewChanged()
    {
        OnPropertyChanged(nameof(Preview));
        OnPropertyChanged(nameof(PreviewChips));
        OnPropertyChanged(nameof(PreviewStars));
        OnPropertyChanged(nameof(IsPreviewTextual));
        OnPropertyChanged(nameof(IsPreviewStar));
        OnPropertyChanged(nameof(IsPreviewNumericPlain));
        OnPropertyChanged(nameof(IsPreviewNumeric));
    }

    /// <summary>
    /// プレビュー用の numeric 設定(min/max/unit)。入力欄(MinText/MaxText/Unit)から組み立てる。
    /// 不正数値は null(=既定 0/100 をビルダが用いる)。プレビューは表示のみで保存値とは独立。
    /// </summary>
    private NumericTagSettings? BuildNumericForPreview()
    {
        if (!IsNumeric)
        {
            return null;
        }

        TryParseOptional(MinText, out var min);
        TryParseOptional(MaxText, out var max);
        var unit = string.IsNullOrWhiteSpace(Unit) ? null : Unit.Trim();
        return new NumericTagSettings { TagId = string.Empty, Min = min, Max = max, Step = null, Unit = unit };
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
