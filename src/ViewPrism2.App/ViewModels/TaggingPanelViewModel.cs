using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ViewPrism2.App.Services;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Repositories;
using ViewPrism2.Core.Services;

namespace ViewPrism2.App.ViewModels;

/// <summary>「現在のタグ」の 1 行(選択画像全件に共通のタグ。各行に解除×、REQ-046)。</summary>
public sealed record CurrentTagRowViewModel(Tag Tag, string? ValueText, string TypeText)
{
    public string Name => Tag.Name;

    public string? Color => Tag.Color;

    public bool HasColor => Tag.Color is not null;

    public bool HasValue => ValueText is not null;
}

/// <summary>「タグを追加」候補の 1 行(全タグ+検索、REQ-046)。</summary>
public sealed record AvailableTagRowViewModel(Tag Tag, string TypeText)
{
    public string Name => Tag.Name;

    public string? Color => Tag.Color;

    public bool HasColor => Tag.Color is not null;
}

/// <summary>
/// タグ付与パネル(M-UI-016、E-UI-TAGASSIGN-029、REQ-046、G-7)。
/// 画像タブの「タグ編集」モードで右パネルに表示し、現在タグ(共通タグ+個別解除)と
/// 追加(全タグ+検索→適用)を提供する。適用は TagService の原子バッチ API のみ経由(REQ-027)。
/// 選択 0 件はプレースホルダ(HasSelection=false)。共通タグ・適用対象の計算は本 VM で unit 検査可能。
/// </summary>
public sealed partial class TaggingPanelViewModel : ObservableObject
{
    private readonly TagService _tagService;
    private readonly ITagRepository _tags;
    private readonly LocalizationService _localization;
    private readonly IWindowService _windows;

    private List<ImageEntry> _selection = [];
    private IReadOnlyDictionary<string, Tag> _tagById = new Dictionary<string, Tag>(StringComparer.Ordinal);
    private List<Tag> _allTags = [];

    public TaggingPanelViewModel(
        TagService tagService,
        ITagRepository tags,
        LocalizationService localization,
        IWindowService windows)
    {
        _tagService = tagService;
        _tags = tags;
        _localization = localization;
        _windows = windows;
        Loc = new LocalizationProxy(localization);
        localization.CultureChanged += (_, _) =>
        {
            // DF-3: Loc 差し替えで全文言バインディングを再評価させる(K-AVALONIA の罠対策)
            Loc = new LocalizationProxy(localization);
            OnPropertyChanged(nameof(Loc));
            RebuildAvailable();
            RebuildCurrentTags();
            OnPropertyChanged(nameof(SelectionCountText));
        };
    }

    public LocalizationProxy Loc { get; private set; }

    /// <summary>選択画像全件に付与済みの共通タグ(複数選択時は積集合、REQ-046)。</summary>
    public ObservableCollection<CurrentTagRowViewModel> CurrentTags { get; } = [];

    /// <summary>追加候補(全タグ。検索=名前の部分一致・大文字小文字無視)。</summary>
    public ObservableCollection<AvailableTagRowViewModel> AvailableTags { get; } = [];

    /// <summary>textual タグの候補値(predefined_values 順、REQ-024/046)。</summary>
    public ObservableCollection<string> CandidateValues { get; } = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private AvailableTagRowViewModel? _selectedTag;

    /// <summary>textual の適用値(候補値ドロップダウン+自由入力)。</summary>
    [ObservableProperty]
    private string _valueText = string.Empty;

    [ObservableProperty]
    private string? _selectedCandidate;

    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>選択 0 件 → false(プレースホルダ表示、仕様 §2.6)。</summary>
    public bool HasSelection => _selection.Count > 0;

    public bool HasNoCurrentTags => HasSelection && CurrentTags.Count == 0;

    public string SelectionCountText => _localization.T("common.selectedCount", new Dictionary<string, string>
    {
        ["count"] = _selection.Count.ToString(CultureInfo.InvariantCulture),
    });

    public bool IsTextualSelected => SelectedTag?.Tag.Type == TagType.Textual;

    public bool IsNumericSelected => SelectedTag?.Tag.Type == TagType.Numeric;

    public bool CanApply => HasSelection && SelectedTag is not null;

    /// <summary>選択順の画像 id(連番適用の整列基準、FMEA-014)。</summary>
    public IReadOnlyList<string> SelectionOrderIds => _selection.Select(e => e.Record.Id).ToList();

    /// <summary>付与・解除を適用した(シェルが再読込する)。</summary>
    public event EventHandler? Applied;

    /// <summary>全タグの差し替え(シェルの基礎データ読込と同期)。</summary>
    public void UpdateTags(IReadOnlyDictionary<string, Tag> tagById)
    {
        ArgumentNullException.ThrowIfNull(tagById);
        _tagById = tagById;
        _allTags = tagById.Values
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Id, StringComparer.Ordinal)
            .ToList();
        RebuildAvailable();
        RebuildCurrentTags();
    }

    /// <summary>選択画像の差し替え(選択順を保持)。共通タグを再計算する。</summary>
    public void SetSelection(IReadOnlyList<ImageEntry> selectionInOrder)
    {
        ArgumentNullException.ThrowIfNull(selectionInOrder);
        _selection = selectionInOrder.ToList();
        RebuildCurrentTags();
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectionCountText));
        OnPropertyChanged(nameof(CanApply));
    }

    /// <summary>解除: 当該タグのみ選択画像全件から外す(冪等・単一トランザクション、REQ-026/027)。</summary>
    [RelayCommand]
    private async Task RemoveTagAsync(CurrentTagRowViewModel row)
    {
        if (!HasSelection)
        {
            return;
        }

        var result = await _tagService.UntagImagesAsync(SelectionOrderIds, row.Tag.Id);
        StatusMessage = result.IsSuccess
            ? _localization.T("success.updated")
            : ErrorMessages.Resolve(_localization, result.Error);
        if (result.IsSuccess)
        {
            Applied?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// 適用: 選択画像全件へ一括付与(REQ-046)。simple=値なし / textual=候補値+自由入力 /
    /// numeric=値入力ダイアログ(固定値|連番)。いずれも REQ-027 の原子バッチ。
    /// </summary>
    [RelayCommand]
    private async Task ApplyAsync()
    {
        if (!HasSelection || SelectedTag is not { } row)
        {
            return;
        }

        StatusMessage = null;
        var ids = SelectionOrderIds;
        Core.Common.Result result;
        switch (row.Tag.Type)
        {
            case TagType.Simple:
                result = await _tagService.TagImagesAsync(ids, row.Tag.Id, null);
                break;

            case TagType.Textual:
            {
                if (ValueText.Length == 0)
                {
                    StatusMessage = _localization.T("tagging.valueRequired");
                    return;
                }

                result = await _tagService.TagImagesAsync(ids, row.Tag.Id, ValueText);
                break;
            }

            case TagType.Numeric:
            default:
            {
                var settings = await _tags.GetNumericSettingsAsync(row.Tag.Id);
                var values = await _windows.ShowNumericValueDialogAsync(row.Tag, settings, ids.Count);
                if (values is null)
                {
                    return; // キャンセル(適用 0 件)
                }

                result = await ApplyNumericAsync(row.Tag.Id, values);
                break;
            }
        }

        StatusMessage = result.IsSuccess
            ? _localization.T("success.updated")
            : ErrorMessages.Resolve(_localization, result.Error);
        if (result.IsSuccess)
        {
            Applied?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>numeric の値列(選択順に整列済み)を原子バッチで適用する(unit 検査対象)。</summary>
    public async Task<Core.Common.Result> ApplyNumericAsync(string tagId, IReadOnlyList<string> valuesInSelectionOrder)
    {
        ArgumentNullException.ThrowIfNull(valuesInSelectionOrder);
        var ids = SelectionOrderIds;
        if (valuesInSelectionOrder.Count != ids.Count)
        {
            return Core.Common.Result.Fail(Core.Common.ErrorCode.ValidationError, "値の数が選択数と一致しません。");
        }

        var assignments = new List<(string ImageId, string? Value)>(ids.Count);
        for (var i = 0; i < ids.Count; i++)
        {
            assignments.Add((ids[i], valuesInSelectionOrder[i]));
        }

        return await _tagService.TagImagesWithValuesAsync(tagId, assignments);
    }

    partial void OnSearchTextChanged(string value) => RebuildAvailable();

    partial void OnSelectedTagChanged(AvailableTagRowViewModel? value)
    {
        ValueText = string.Empty;
        SelectedCandidate = null;
        OnPropertyChanged(nameof(IsTextualSelected));
        OnPropertyChanged(nameof(IsNumericSelected));
        OnPropertyChanged(nameof(CanApply));
        _ = LoadCandidatesAsync(value?.Tag);
    }

    partial void OnSelectedCandidateChanged(string? value)
    {
        if (value is not null)
        {
            ValueText = value;
        }
    }

    private async Task LoadCandidatesAsync(Tag? tag)
    {
        CandidateValues.Clear();
        if (tag?.Type != TagType.Textual)
        {
            return;
        }

        var settings = await _tags.GetTextualSettingsAsync(tag.Id);
        if (SelectedTag?.Tag.Id == tag.Id)
        {
            foreach (var candidate in settings?.PredefinedValues ?? [])
            {
                CandidateValues.Add(candidate); // 順序保持(REQ-024)
            }
        }
    }

    private void RebuildAvailable()
    {
        var filter = SearchText;
        AvailableTags.Clear();
        foreach (var tag in _allTags)
        {
            // 検索: 名前の部分一致・大文字小文字無視(仕様 §2.6)
            if (filter.Length > 0 && !tag.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            AvailableTags.Add(new AvailableTagRowViewModel(tag, TypeTextOf(tag.Type)));
        }
    }

    /// <summary>共通タグ算出(REQ-046): 選択画像全件が持つタグのみ。値は全件同値のときのみ表示。</summary>
    private void RebuildCurrentTags()
    {
        CurrentTags.Clear();
        if (_selection.Count > 0)
        {
            var common = _selection[0].Tags
                .Select(t => t.TagId)
                .Where(tagId => _selection.All(e => e.Tags.Any(t => string.Equals(t.TagId, tagId, StringComparison.Ordinal))))
                .ToList();

            foreach (var tagId in common
                .Select(id => _tagById.TryGetValue(id, out var tag) ? tag : null)
                .Where(t => t is not null)
                .OrderBy(t => t!.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(t => t!.Id, StringComparer.Ordinal))
            {
                var values = _selection
                    .Select(e => e.Tags.First(t => string.Equals(t.TagId, tagId!.Id, StringComparison.Ordinal)).Value)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                var uniform = values.Count == 1 ? values[0] : null;
                CurrentTags.Add(new CurrentTagRowViewModel(tagId!, uniform, TypeTextOf(tagId!.Type)));
            }
        }

        OnPropertyChanged(nameof(HasNoCurrentTags));
    }

    private string TypeTextOf(TagType type) => _localization.T(type switch
    {
        TagType.Simple => "tag.type.simple",
        TagType.Textual => "tag.type.textual",
        _ => "tag.type.numeric",
    });
}
