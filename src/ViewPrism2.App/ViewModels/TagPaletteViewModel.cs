using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ViewPrism2.App.Services;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;

namespace ViewPrism2.App.ViewModels;

/// <summary>
/// タグパレットの 1 行(色+名前+型チップ+候補値/範囲+編集/削除、ECO-009/E-UI-TAGS-026)。
/// ECO-009: テキスト型は候補値チップ、数値型は範囲ピル+刻みを提示する(モック CAD 準拠)。
/// </summary>
public sealed partial class TagPaletteRowViewModel : ObservableObject
{
    public TagPaletteRowViewModel(
        Tag tag, string typeText, IReadOnlyList<string> predefinedValues, NumericTagSettings? numeric)
    {
        Tag = tag;
        TypeText = typeText;
        CandidateValues = predefinedValues;
        RangeText = BuildRangeText(numeric);
        StepValue = numeric?.Step is { } step ? FormatNum(step) : null;
    }

    public Tag Tag { get; }

    public string TypeText { get; }

    public string Name => Tag.Name;

    // ECO-007/E2(DC-TAGPALETTE-001/DE-4 撤回): タグパレット行に説明を出さない。
    // Tag.Description はデータとして残し、作成/編集ダイアログでのみ参照する(行 VM では公開しない)。

    public string? Color => Tag.Color;

    /// <summary>color=NULL のタグは境界線色のリング表示(K-DESIGN)。</summary>
    public bool HasColor => Tag.Color is not null;

    public bool IsSimple => Tag.Type == TagType.Simple;

    public bool IsTextual => Tag.Type == TagType.Textual;

    public bool IsNumeric => Tag.Type == TagType.Numeric;

    /// <summary>テキスト型の候補値(順序保持)。ECO-009: パレット行に候補値チップで提示。</summary>
    public IReadOnlyList<string> CandidateValues { get; }

    public bool HasCandidateValues => IsTextual && CandidateValues.Count > 0;

    /// <summary>数値型の範囲表示(例: "1–5 ★")。null は範囲表示なし。</summary>
    public string? RangeText { get; }

    public bool HasRange => IsNumeric && RangeText is not null;

    /// <summary>数値型の刻み値(例: "1")。null は刻み表示なし。</summary>
    public string? StepValue { get; }

    public bool HasStep => IsNumeric && StepValue is not null;

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>範囲ラベル: "{min}–{max}"(+単位)。min/max・単位とも無ければ null。INV-007 不変表現。</summary>
    private static string? BuildRangeText(NumericTagSettings? n)
    {
        if (n is null)
        {
            return null;
        }

        var min = FormatNum(n.Min);
        var max = FormatNum(n.Max);
        if (min is null && max is null && string.IsNullOrEmpty(n.Unit))
        {
            return null;
        }

        var range = $"{min ?? "—"}–{max ?? "—"}";
        return string.IsNullOrEmpty(n.Unit) ? range : $"{range} {n.Unit}";
    }

    private static string? FormatNum(double? v)
    {
        if (v is not { } d)
        {
            return null;
        }

        return d == Math.Floor(d)
            ? ((long)d).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : d.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// タグタブ右「タグパレット」(M-UI-013 v1.2、E-UI-TAGS-026、G-6)。
/// 検索(名前の部分一致・大文字小文字無視)・「追加」→タグ作成ダイアログ・一覧(編集/削除)。
/// 階層エディタへの D&D/ボタン追加のドラッグ元。
/// </summary>
public sealed partial class TagPaletteViewModel : ObservableObject
{
    private readonly TagService _tagService;
    private readonly LocalizationService _localization;
    private readonly IWindowService _windows;
    private List<PaletteTagItem> _all = [];

    public TagPaletteViewModel(TagService tagService, LocalizationService localization, IWindowService windows)
    {
        _tagService = tagService;
        _localization = localization;
        _windows = windows;
        Loc = new LocalizationProxy(localization);
        localization.CultureChanged += (_, _) =>
        {
            // DF-3: Loc 差し替えで全文言バインディングを再評価させる(K-AVALONIA の罠対策)
            Loc = new LocalizationProxy(localization);
            OnPropertyChanged(nameof(Loc));
            ApplyFilter();
        };
    }

    public LocalizationProxy Loc { get; private set; }

    public ObservableCollection<TagPaletteRowViewModel> Tags { get; } = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private TagPaletteRowViewModel? _selectedTag;

    [ObservableProperty]
    private string? _statusMessage;

    public bool IsEmpty => Tags.Count == 0;

    /// <summary>絞り込み後の件数(ECO-009: 件数/凡例行)。</summary>
    public int ItemCount => Tags.Count;

    /// <summary>全件数(検索前)。</summary>
    public int TotalCount => _all.Count;

    /// <summary>"{count}/{total} アイテム"(ECO-009)。i18n キー経由。</summary>
    public string ItemCountText => _localization.T("tag.palette.itemCount", new Dictionary<string, string>
    {
        ["count"] = ItemCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["total"] = TotalCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
    });

    /// <summary>タグの作成・編集・削除があった(シェル・エディタの再読込用)。</summary>
    public event EventHandler? TagsChanged;

    /// <summary>
    /// 未保存の階層編集に載っているタグか(REQ-083/ECO-046 U-a)。ホスト(TagsTabViewModel)が
    /// エディタ状態への判定を配線する。DB 参照ガード(TagService=ECO-045)は未コミットの
    /// 編集状態を関知できないため、この UI 層判定が谷間を塞ぐ。
    /// </summary>
    public Func<string, bool>? IsTagInUnsavedEdit { get; set; }

    public async Task LoadAsync()
    {
        // 一覧は name 昇順(REQ-029)。ECO-009: 候補値/数値範囲を含めて取得しパレット行に提示
        _all = (await _tagService.GetPaletteItemsAsync()).ToList();
        ApplyFilter();
    }

    [RelayCommand]
    private async Task NewTagAsync()
    {
        if (await _windows.ShowTagEditorAsync(null))
        {
            await LoadAsync();
            TagsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand]
    private async Task EditAsync(TagPaletteRowViewModel row)
    {
        if (await _windows.ShowTagEditorAsync(row.Tag))
        {
            await LoadAsync();
            TagsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(TagPaletteRowViewModel row)
    {
        // ECO-046(U-a 裁定): 未保存の階層編集に載っているタグは確認ダイアログの前に拒否(TAG-008 の外延)
        if (IsTagInUnsavedEdit?.Invoke(row.Tag.Id) == true)
        {
            StatusMessage = _localization.T("error.tagInUnsavedEdit");
            return;
        }

        var message = _localization.T("tag.deleteTagConfirmation", new Dictionary<string, string>
        {
            ["tagName"] = row.Tag.Name,
        });
        if (!await _windows.ConfirmAsync(_localization.T("tag.deleteTag"), message))
        {
            return;
        }

        var result = await _tagService.DeleteAsync(row.Tag.Id);
        StatusMessage = result.IsSuccess ? null : ErrorMessages.Resolve(_localization, result.Error);
        await LoadAsync();
        TagsChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnSelectedTagChanged(TagPaletteRowViewModel? value)
    {
        foreach (var row in Tags)
        {
            row.IsSelected = ReferenceEquals(row, value);
        }
    }

    private void ApplyFilter()
    {
        var selectedId = SelectedTag?.Tag.Id;
        Tags.Clear();
        foreach (var item in _all)
        {
            // 検索: 名前の部分一致・大文字小文字無視(仕様 §2.6)
            if (SearchText.Length > 0 && !item.Tag.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Tags.Add(new TagPaletteRowViewModel(
                item.Tag,
                _localization.T(item.Tag.Type switch
                {
                    TagType.Simple => "tag.type.simple",
                    TagType.Textual => "tag.type.textual",
                    _ => "tag.type.numeric",
                }),
                item.PredefinedValues,
                item.Numeric));
        }

        SelectedTag = selectedId is null
            ? null
            : Tags.FirstOrDefault(t => string.Equals(t.Tag.Id, selectedId, StringComparison.Ordinal));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(ItemCount));
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(ItemCountText));
    }
}
