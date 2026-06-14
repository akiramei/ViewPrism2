using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ViewPrism2.App.Services;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Repositories;
using ViewPrism2.Core.Services;
using ViewPrism2.Core.Services.Repair;

namespace ViewPrism2.App.ViewModels;

/// <summary>トラッシュ(deleted 画像)の表示行。閲覧+復元/完全削除(V4)。</summary>
public sealed record TrashItemViewModel(ImageRecord Record, string AbsolutePath)
{
    public string FileName => Record.FileName;
}

/// <summary>
/// トラッシュ表示の ViewModel(M-UI-SIMILARITY-023 + M-UI-REPAIR-027 拡張、仕様 §2.10.5 / §2.11.3-4)。
/// 選択中コレクション(REQ-053)の status=Deleted 画像の一覧+件数。
/// V3 は閲覧のみ。V4 で『復元』『完全削除』を追加(TrashService の API のみ経由 — UI で状態遷移を再実装しない)。
/// 完全削除は確認+非破壊明示文言『画像ファイルは削除されません(DB から除去)』を伴う(裁定 6)。
/// 復元で物理不在のため missing 化した場合は結果を表示する(TrashService.RestoreAsync の戻り status を反映)。
/// </summary>
public sealed partial class TrashViewModel : ObservableObject
{
    private readonly string _collectionId;
    private readonly IImageRepository _images;
    private readonly ISyncFolderRepository _folders;
    private readonly LocalizationService _localization;
    private readonly TrashService? _trash;
    private readonly IWindowService? _windows;

    public TrashViewModel(
        string collectionId,
        IImageRepository images,
        ISyncFolderRepository folders,
        LocalizationService localization,
        TrashService? trash = null,
        IWindowService? windows = null)
    {
        _collectionId = collectionId;
        _images = images;
        _folders = folders;
        _localization = localization;
        _trash = trash;
        _windows = windows;
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

    [ObservableProperty]
    private TrashItemViewModel? _selectedItem;

    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>復元・完全削除を実行できるか(対象選択+サービス注入済み)。</summary>
    public bool CanOperate => SelectedItem is not null && _trash is not null;

    public async Task LoadAsync()
    {
        Items.Clear();
        SelectedItem = null;
        StatusMessage = null;

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

    partial void OnSelectedItemChanged(TrashItemViewModel? value) => OnPropertyChanged(nameof(CanOperate));

    /// <summary>復元(T6/T7): 物理存在→Normal・不在→Missing。missing 化した場合は通知文言を出す(INV-013)。</summary>
    [RelayCommand]
    public async Task RestoreAsync()
    {
        if (SelectedItem is not { } item || _trash is null)
        {
            return;
        }

        var result = await _trash.RestoreAsync(item.Record.Id);
        if (!result.IsSuccess)
        {
            StatusMessage = _localization.T("trash.restore.failed") + ": "
                + ErrorMessages.Resolve(_localization, result.Error);
            return;
        }

        // 一覧を更新してから結果文言を反映する(LoadAsync が StatusMessage をクリアするため順序が重要)。
        // 復元後 status を反映: 物理不在で missing 化したら明示する(幽霊 normal 防止の結果表示)
        await LoadAsync();
        StatusMessage = result.Value == ImageStatus.Missing
            ? _localization.T("trash.restore.missing")
            : _localization.T("trash.restore.success");
    }

    /// <summary>完全削除(T8): 確認+非破壊明示文言を経て images 行削除(CASCADE)。物理ファイルは不変(INV-014)。</summary>
    [RelayCommand]
    public async Task PurgeAsync()
    {
        if (SelectedItem is not { } item || _trash is null)
        {
            return;
        }

        // 破壊的に見える操作は実行前確認+非破壊明示文言を必須とする(裁定 6 / §2.11.5)
        if (_windows is not null && !await _windows.ConfirmAsync(
                _localization.T("trash.purge.title"), _localization.T("trash.purge.confirmMessage")))
        {
            return;
        }

        var result = await _trash.PermanentDeleteAsync(item.Record.Id);
        if (result.IsSuccess)
        {
            // 一覧更新後に文言を反映する(LoadAsync が StatusMessage をクリアするため)
            await LoadAsync();
            StatusMessage = _localization.T("trash.purge.success");
        }
        else
        {
            StatusMessage = _localization.T("trash.purge.failed") + ": "
                + ErrorMessages.Resolve(_localization, result.Error);
        }
    }
}
