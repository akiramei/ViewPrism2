using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ViewPrism2.App.Services;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Repositories;
using ViewPrism2.Core.Services;
using ViewPrism2.Core.Services.Repair;

namespace ViewPrism2.App.ViewModels;

/// <summary>
/// ゴミ箱フィーチャ(ECO-019 ポップアップ + ECO-018 バッジ)を ImageTabViewModel(god-VM)から
/// 切り出した子 VM(ECO-036 第1段)。挙動不変のリファクタで、ホスト型を参照しない(コンストラクタ関数注入)。
/// 削除モードのフラグ・排他制御・DeleteToTrash の呼び出し殻はホスト(ImageTabViewModel)側に残る
/// (60-change-order-eco-036.md §8.2/§8.4)。
/// </summary>
public sealed partial class ImageTabTrashViewModel : ObservableObject
{
    private readonly IImageRepository _images;
    private readonly TrashService _trash;
    private readonly IWindowService _windows;
    private readonly Func<string?> _getCollectionId;
    private readonly Func<Task> _reloadImagesAsync;
    private readonly Action _recompute;
    private readonly Func<long, string> _fmtSize;
    private readonly Action _closeMoreMenu;
    private readonly Func<string, string> _resolveAbsolutePath;

    private int _trashCount; // 選択コレクションの deleted 件数(⋯「ゴミ箱」バッジ)
    private readonly List<string> _trashSel = new();

    private readonly LocalizationService _localization;

    public ImageTabTrashViewModel(
        IImageRepository images,
        TrashService trash,
        IWindowService windows,
        Func<string?> getCollectionId,
        Func<Task> reloadImagesAsync,
        Action recompute,
        Func<long, string> fmtSize,
        Action closeMoreMenu,
        Func<string, string> resolveAbsolutePath,
        LocalizationService localization)
    {
        _images = images;
        _trash = trash;
        _windows = windows;
        _getCollectionId = getCollectionId;
        _reloadImagesAsync = reloadImagesAsync;
        _recompute = recompute;
        _fmtSize = fmtSize;
        _closeMoreMenu = closeMoreMenu;
        _resolveAbsolutePath = resolveAbsolutePath;
        _localization = localization;
        // ECO-079/GF-079-01: 言語切替で算出文言(選択中ラベル等)を一斉再評価
        _localization.CultureChanged += (_, _) => OnPropertyChanged(string.Empty);
    }

    // ---- ⋯「ゴミ箱」バッジ(ECO-018) ----
    public bool HasTrash => _trashCount > 0;
    public int TrashCount => _trashCount;

    // ---- ゴミ箱ポップアップ(ECO-019)公開契約 ----
    public bool TrashOpen { get; private set; }
    public ObservableCollection<TrashPopupItemVM> TrashPopupItems { get; } = new();
    public int TrashPopupCount => TrashPopupItems.Count;
    public bool HasTrashItems => TrashPopupItems.Count > 0;
    public bool TrashPopupEmpty => TrashPopupItems.Count == 0;
    public bool HasTrashSel => _trashSel.Count > 0;
    public int TrashSelCount => _trashSel.Count;
    public string TrashSelCountLabel => HasTrashSel ? _localization.T("view.selectedCount", new Dictionary<string, string> { ["count"] = _trashSel.Count.ToString() }) : _localization.T("view.selectImagesToAct");
    public string TrashSelectAllLabel => (TrashPopupItems.Count > 0 && _trashSel.Count == TrashPopupItems.Count) ? _localization.T("view.deselect") : _localization.T("view.selectAll");
    /// <summary>復元・完全削除は選択がある時のみ活性。</summary>
    public bool CanRestoreTrash => _trashSel.Count > 0;
    public bool CanPurgeTrash => _trashSel.Count > 0;

    /// <summary>⋯ メニュー「ゴミ箱」: トラッシュを画像タブ内ポップアップで開く(ECO-019)。deleted 一覧を読み込み overlay を表示。</summary>
    [RelayCommand]
    private async Task OpenTrash()
    {
        _closeMoreMenu();
        if (_getCollectionId() is null) { OnPropertyChanged(string.Empty); return; }
        await LoadTrashItemsAsync().ConfigureAwait(true);
        TrashOpen = true;
        OnPropertyChanged(string.Empty);
    }

    /// <summary>ゴミ箱ポップアップを閉じる。</summary>
    [RelayCommand]
    private void CloseTrash()
    {
        TrashOpen = false;
        _trashSel.Clear();
        OnPropertyChanged(string.Empty);
    }

    /// <summary>選択コレクションの deleted 画像を読み込みポップアップ一覧を作る(ファイル名昇順)。</summary>
    private async Task LoadTrashItemsAsync()
    {
        TrashPopupItems.Clear();
        _trashSel.Clear();
        var collectionId = _getCollectionId();
        if (collectionId is null) return;
        var deleted = await _images.GetDeletedByFolderAsync(collectionId).ConfigureAwait(true);
        foreach (var r in deleted)
        {
            var abs = _resolveAbsolutePath(r.RelativePath);
            TrashPopupItems.Add(new TrashPopupItemVM(r.Id, r.FileName, abs, _fmtSize(r.FileSize)));
        }
    }

    /// <summary>ポップアップ項目の選択トグル(青選択・複数可)。</summary>
    [RelayCommand]
    private void ToggleTrashItem(TrashPopupItemVM item)
    {
        if (!_trashSel.Remove(item.Id)) _trashSel.Add(item.Id);
        RefreshTrashSelection();
    }

    /// <summary>すべて選択 / 選択を解除。</summary>
    [RelayCommand]
    private void ToggleTrashSelectAll()
    {
        if (_trashSel.Count == TrashPopupItems.Count) _trashSel.Clear();
        else { _trashSel.Clear(); _trashSel.AddRange(TrashPopupItems.Select(i => i.Id)); }
        RefreshTrashSelection();
    }

    private void RefreshTrashSelection()
    {
        var sel = new HashSet<string>(_trashSel, StringComparer.Ordinal);
        foreach (var it in TrashPopupItems) it.IsSelected = sel.Contains(it.Id); // その場更新
        OnPropertyChanged(string.Empty);
    }

    /// <summary>選択を復元(ECO-128 T6'/T7・物理存在→pending〔origin=Restored〕/不在→missing)。復元分は一覧と母集合へ反映。</summary>
    [RelayCommand]
    private async Task RestoreSelectedTrash()
    {
        if (_trashSel.Count == 0) return;
        foreach (var id in _trashSel.ToList())
            await _trash.RestoreAsync(id).ConfigureAwait(true);
        // 復元 pending は無絞り込み FS ブラウズにバッジ付きで並置(INV-010 v5.0)=一覧へ戻る。
        // ReloadImagesAsync は _allPending も再取得する(GF-129-01)ため件数バッジ・一覧バッジが追随する
        await _reloadImagesAsync().ConfigureAwait(true);
        await LoadTrashItemsAsync().ConfigureAwait(true);
        await RefreshCountAsync().ConfigureAwait(true);
        _recompute();
    }

    /// <summary>選択を完全削除(T8・CASCADE)。確認+INV-009 非破壊明示(画像ファイルは削除されない=DB 行のみ除去)。</summary>
    [RelayCommand]
    private async Task PurgeSelectedTrash()
    {
        if (_trashSel.Count == 0) return;
        int n = _trashSel.Count;
        if (!await _windows.ConfirmAsync(_localization.T("trash.purge"),
                _localization.T("trash.purgeConfirm", new Dictionary<string, string> { ["count"] = n.ToString() }),
                _localization.T("trash.purge"), destructive: true).ConfigureAwait(true))
            return;
        foreach (var id in _trashSel.ToList())
            await _trash.PermanentDeleteAsync(id).ConfigureAwait(true);
        await LoadTrashItemsAsync().ConfigureAwait(true);
        await RefreshCountAsync().ConfigureAwait(true);
        _recompute();
    }

    /// <summary>ゴミ箱を空にする(全 deleted を完全削除)。確認+INV-009 非破壊明示。</summary>
    [RelayCommand]
    private async Task EmptyTrash()
    {
        if (TrashPopupItems.Count == 0) return;
        int n = TrashPopupItems.Count;
        if (!await _windows.ConfirmAsync(_localization.T("modals.trash.emptyTrash"),
                _localization.T("trash.emptyConfirm", new Dictionary<string, string> { ["count"] = n.ToString() }),
                _localization.T("modals.trash.emptyTrash"), destructive: true).ConfigureAwait(true))
            return;
        foreach (var id in TrashPopupItems.Select(i => i.Id).ToList())
            await _trash.PermanentDeleteAsync(id).ConfigureAwait(true);
        await LoadTrashItemsAsync().ConfigureAwait(true);
        await RefreshCountAsync().ConfigureAwait(true);
        _recompute();
    }

    /// <summary>⋯「ゴミ箱」バッジ用に選択コレクションの deleted 件数を取り直す。ホスト(InitializeAsync/OpenIntegrityReview)からも呼ばれる。</summary>
    public async Task RefreshCountAsync()
    {
        var collectionId = _getCollectionId();
        if (collectionId is null) { _trashCount = 0; OnPropertyChanged(string.Empty); return; }
        _trashCount = await _images.CountByFolderAndStatusAsync(collectionId, ImageStatus.Deleted).ConfigureAwait(true);
        // 通知は本メソッドが自前で発行する(golden 所見 G-E36S1-1 の是正): 旧 god-VM ではホストの
        // OnPropertyChanged(string.Empty) 一括通知が肩代わりしていたが、分割後はホスト通知は子に届かない。
        OnPropertyChanged(string.Empty);
    }

    /// <summary>ECO-064: background content snapshotで取得済みのbadge件数をUIへ原子的に公開する。</summary>
    public void SetCount(int count)
    {
        _trashCount = count;
        OnPropertyChanged(string.Empty);
    }

    /// <summary>DeleteToTrash 殻(ホスト側)から呼ばれる実行部: 選択 ids をゴミ箱へ移動する(Core 経由)。</summary>
    public async Task MoveToTrashAsync(IReadOnlyList<string> ids)
    {
        foreach (var id in ids)
            await _trash.DeleteToTrashAsync(id).ConfigureAwait(true); // Core 経由(状態遷移は TrashService が担う)
    }
}
