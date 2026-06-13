using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Repositories;
using ViewPrism2.Core.Services;

namespace ViewPrism2.App.ViewModels;

/// <summary>トラッシュ(deleted 画像)の表示行。閲覧のみ。</summary>
public sealed record TrashItemViewModel(ImageRecord Record, string AbsolutePath)
{
    public string FileName => Record.FileName;
}

/// <summary>
/// トラッシュ表示の ViewModel(M-UI-SIMILARITY-023 / E-UI-MERGE-036、仕様 §2.10.5)。
/// 選択中コレクション(REQ-053)の status=Deleted 画像の一覧+件数のみ。
/// 閲覧のみ(復元・完全削除は後続ループ)。マージで deleted になった画像が「見えないまま消える」のを避ける。
/// </summary>
public sealed partial class TrashViewModel : ObservableObject
{
    private readonly string _collectionId;
    private readonly IImageRepository _images;
    private readonly ISyncFolderRepository _folders;
    private readonly LocalizationService _localization;

    public TrashViewModel(
        string collectionId,
        IImageRepository images,
        ISyncFolderRepository folders,
        LocalizationService localization)
    {
        _collectionId = collectionId;
        _images = images;
        _folders = folders;
        _localization = localization;
        Loc = new LocalizationProxy(localization);
        localization.CultureChanged += (_, _) =>
        {
            Loc = new LocalizationProxy(localization);
            OnPropertyChanged(nameof(Loc));
        };
    }

    public LocalizationProxy Loc { get; private set; }

    /// <summary>選択中コレクションの deleted 画像一覧。</summary>
    public ObservableCollection<TrashItemViewModel> Items { get; } = [];

    /// <summary>deleted 件数。</summary>
    public int Count => Items.Count;

    public bool IsEmpty => Items.Count == 0;

    public async Task LoadAsync()
    {
        Items.Clear();

        var folder = await _folders.GetByIdAsync(_collectionId);
        if (folder is null)
        {
            OnPropertyChanged(nameof(Count));
            OnPropertyChanged(nameof(IsEmpty));
            return;
        }

        var records = await _images.GetByFolderAsync(_collectionId);
        foreach (var record in records
                     .Where(r => r.Status == ImageStatus.Deleted)
                     .OrderBy(r => r.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            var absolute = Path.Combine(folder.Path, record.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            Items.Add(new TrashItemViewModel(record, absolute));
        }

        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(IsEmpty));
    }
}
