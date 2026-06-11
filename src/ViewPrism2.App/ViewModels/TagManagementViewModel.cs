using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ViewPrism2.App.Services;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;

namespace ViewPrism2.App.ViewModels;

/// <summary>タグ一覧の 1 行(name 昇順+使用数、REQ-029。色見本は 16px 円形スウォッチ — K-DESIGN)。</summary>
public sealed record TagRowViewModel(Tag Tag, string TypeText, string UsageText)
{
    public string Name => Tag.Name;

    public string? Color => Tag.Color;

    /// <summary>color=NULL のタグは境界線色のリング表示(K-DESIGN)。</summary>
    public bool HasColor => Tag.Color is not null;
}

/// <summary>
/// タグ管理 UI(M-UI-013、E-UI-TAGS-026、REQ-021〜025/029)。
/// 一覧(name 昇順 OrdinalIgnoreCase+使用数)・作成/編集ダイアログ・削除(確認付き)。
/// </summary>
public sealed partial class TagManagementViewModel : ObservableObject
{
    private readonly TagService _tagService;
    private readonly LocalizationService _localization;
    private readonly IWindowService _windows;

    public TagManagementViewModel(TagService tagService, LocalizationService localization, IWindowService windows)
    {
        _tagService = tagService;
        _localization = localization;
        _windows = windows;
        Loc = new LocalizationProxy(localization);
    }

    public LocalizationProxy Loc { get; }

    public ObservableCollection<TagRowViewModel> Tags { get; } = [];

    [ObservableProperty]
    private string? _statusMessage;

    public bool IsEmpty => Tags.Count == 0;

    public async Task LoadAsync()
    {
        Tags.Clear();
        foreach (var entry in await _tagService.GetAllWithUsageAsync())
        {
            Tags.Add(new TagRowViewModel(
                entry.Tag,
                _localization.T(entry.Tag.Type switch
                {
                    TagType.Simple => "tag.type.simple",
                    TagType.Textual => "tag.type.textual",
                    _ => "tag.type.numeric",
                }),
                _localization.T("tag.usageCount", new Dictionary<string, string>
                {
                    ["count"] = entry.UsageCount.ToString(CultureInfo.InvariantCulture),
                })));
        }

        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand]
    private async Task NewTagAsync()
    {
        if (await _windows.ShowTagEditorAsync(null))
        {
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task EditAsync(TagRowViewModel row)
    {
        if (await _windows.ShowTagEditorAsync(row.Tag))
        {
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(TagRowViewModel row)
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
    }
}
