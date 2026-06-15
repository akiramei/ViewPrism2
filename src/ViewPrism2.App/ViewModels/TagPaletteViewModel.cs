using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ViewPrism2.App.Services;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;

namespace ViewPrism2.App.ViewModels;

/// <summary>タグパレットの 1 行(色スウォッチ+名前+種類チップ+編集/削除、仕様 §2.6 v1.2)。</summary>
public sealed partial class TagPaletteRowViewModel : ObservableObject
{
    public TagPaletteRowViewModel(Tag tag, string typeText)
    {
        Tag = tag;
        TypeText = typeText;
    }

    public Tag Tag { get; }

    public string TypeText { get; }

    public string Name => Tag.Name;

    // ECO-007/E2(DC-TAGPALETTE-001/DE-4 撤回): タグパレット行に説明を出さない。
    // Tag.Description はデータとして残し、作成/編集ダイアログでのみ参照する(行 VM では公開しない)。

    public string? Color => Tag.Color;

    /// <summary>color=NULL のタグは境界線色のリング表示(K-DESIGN)。</summary>
    public bool HasColor => Tag.Color is not null;

    [ObservableProperty]
    private bool _isSelected;
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
    private List<Tag> _all = [];

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

    /// <summary>タグの作成・編集・削除があった(シェル・エディタの再読込用)。</summary>
    public event EventHandler? TagsChanged;

    public async Task LoadAsync()
    {
        // 一覧は name 昇順(REQ-029。GetAllWithUsageAsync と同じ整列)
        var all = await _tagService.GetAllWithUsageAsync();
        _all = all.Select(t => t.Tag).ToList();
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
        foreach (var tag in _all)
        {
            // 検索: 名前の部分一致・大文字小文字無視(仕様 §2.6)
            if (SearchText.Length > 0 && !tag.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Tags.Add(new TagPaletteRowViewModel(tag, _localization.T(tag.Type switch
            {
                TagType.Simple => "tag.type.simple",
                TagType.Textual => "tag.type.textual",
                _ => "tag.type.numeric",
            })));
        }

        SelectedTag = selectedId is null
            ? null
            : Tags.FirstOrDefault(t => string.Equals(t.Tag.Id, selectedId, StringComparison.Ordinal));
        OnPropertyChanged(nameof(IsEmpty));
    }
}
