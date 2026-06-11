using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Repositories;
using ViewPrism2.Core.Services;
using ViewPrism2.Infrastructure.Imaging;

namespace ViewPrism2.App.ViewModels;

/// <summary>詳細パネルのタグ表示行(名前+値。numeric は unit 併記、REQ-043)。</summary>
public sealed record DetailTagViewModel(string Name, string? ValueText, string? Color)
{
    public bool HasValue => ValueText is not null;

    public bool HasColor => Color is not null;
}

/// <summary>
/// 画像詳細パネル(M-UI-013、REQ-043、E-UI-DETAIL-023、G-3)。
/// ファイル名/サイズ(1024 進・小数 1 桁)/解像度/作成・更新日時(ロケール書式)/
/// ノート編集(images.notes へ保存)/タグ一覧。選択 0 件はプレースホルダ(仕様 §2.6)。
/// </summary>
public sealed partial class DetailPanelViewModel : ObservableObject
{
    private readonly IImageRepository _images;
    private readonly ITagRepository _tags;
    private readonly ThumbnailService _thumbnails;
    private readonly LocalizationService _localization;
    private ImageEntry? _entry;
    private IReadOnlyDictionary<string, Tag> _tagById = new Dictionary<string, Tag>(StringComparer.Ordinal);

    public DetailPanelViewModel(
        IImageRepository images,
        ITagRepository tags,
        ThumbnailService thumbnails,
        LocalizationService localization)
    {
        _images = images;
        _tags = tags;
        _thumbnails = thumbnails;
        _localization = localization;
        localization.CultureChanged += (_, _) => RenderTimestamps();
    }

    /// <summary>選択 0 件 → false(プレースホルダ表示、仕様 §2.6)。</summary>
    [ObservableProperty]
    private bool _hasImage;

    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private string _sizeText = string.Empty;

    [ObservableProperty]
    private string _resolutionText = string.Empty;

    [ObservableProperty]
    private string _createdText = string.Empty;

    [ObservableProperty]
    private string _modifiedText = string.Empty;

    /// <summary>ノート(編集 → SaveNotes で images.notes へ保存)。</summary>
    [ObservableProperty]
    private string _notes = string.Empty;

    [ObservableProperty]
    private string? _absolutePath;

    public ObservableCollection<DetailTagViewModel> Tags { get; } = [];

    /// <summary>ノート保存完了の通知(ステータス表示用)。</summary>
    public event EventHandler? NotesSaved;

    /// <summary>表示対象を差し替える。null は選択 0 件(プレースホルダ)。</summary>
    public async Task SetEntryAsync(ImageEntry? entry, IReadOnlyDictionary<string, Tag> tagById)
    {
        ArgumentNullException.ThrowIfNull(tagById);
        _entry = entry;
        _tagById = tagById;

        if (entry is null)
        {
            HasImage = false;
            AbsolutePath = null;
            Tags.Clear();
            return;
        }

        HasImage = true;
        FileName = entry.Record.FileName;
        SizeText = ByteSizeFormatter.Format(entry.Record.FileSize);
        AbsolutePath = entry.AbsolutePath;
        Notes = entry.Record.Notes ?? string.Empty;
        RenderTimestamps();

        // 解像度はデコード時取得(SKCodec ヘッダ読み、REQ-043 / K-SKIA)。失敗時は空欄
        ResolutionText = string.Empty;
        var dimensions = await _thumbnails.GetDimensionsAsync(entry.AbsolutePath);
        if (_entry == entry && dimensions is { } d)
        {
            ResolutionText = $"{d.Width} × {d.Height} px";
        }

        await LoadTagsAsync(entry);
    }

    [RelayCommand]
    private async Task SaveNotesAsync()
    {
        if (_entry is null)
        {
            return;
        }

        var value = string.IsNullOrEmpty(Notes) ? null : Notes;
        await _images.UpdateNotesAsync(_entry.Record.Id, value);
        NotesSaved?.Invoke(this, EventArgs.Empty);
    }

    private async Task LoadTagsAsync(ImageEntry entry)
    {
        Tags.Clear();
        foreach (var assigned in entry.Tags)
        {
            if (!_tagById.TryGetValue(assigned.TagId, out var tag))
            {
                continue; // INV-008: 参照切れタグは無視
            }

            string? valueText = null;
            if (tag.Type == TagType.Numeric && assigned.Value is not null)
            {
                var settings = await _tags.GetNumericSettingsAsync(tag.Id);
                valueText = settings?.Unit is { } unit ? $"{assigned.Value} {unit}" : assigned.Value;
            }
            else if (assigned.Value is not null)
            {
                valueText = assigned.Value;
            }

            if (_entry == entry)
            {
                Tags.Add(new DetailTagViewModel(tag.Name, valueText, tag.Color));
            }
        }
    }

    private void RenderTimestamps()
    {
        if (_entry is null)
        {
            return;
        }

        // 作成・更新日時はロケール書式(REQ-043)
        CreatedText = LocaleFormats.FormatTimestamp(_entry.Record.CreatedDate, _localization.CurrentLocale);
        ModifiedText = LocaleFormats.FormatTimestamp(_entry.Record.ModifiedDate, _localization.CurrentLocale);
    }
}
