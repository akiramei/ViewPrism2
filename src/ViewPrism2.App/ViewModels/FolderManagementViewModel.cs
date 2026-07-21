using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ViewPrism2.App.Services;
using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Repositories;
using ViewPrism2.Core.Services;
using ViewPrism2.Infrastructure.Scanning;

namespace ViewPrism2.App.ViewModels;

/// <summary>同期フォルダ管理の 1 行(REQ-010)。is_active / include_subfolders は即時保存。</summary>
public sealed partial class FolderRowViewModel : ObservableObject
{
    private readonly FolderManagementViewModel _owner;
    private bool _loading;

    public FolderRowViewModel(FolderManagementViewModel owner, SyncFolder folder, string lastScanText)
    {
        _owner = owner;
        Folder = folder;
        _loading = true;
        _isActive = folder.IsActive;
        _includeSubfolders = folder.IncludeSubfolders;
        _excludePatternsText = string.Join(", ", folder.ExcludePatterns);
        _lastScanText = lastScanText;
        _loading = false;
    }

    public SyncFolder Folder { get; private set; }

    public string Name => Folder.Name;

    public string Path => Folder.Path;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _includeSubfolders;

    [ObservableProperty]
    private string _excludePatternsText;

    [ObservableProperty]
    private string _lastScanText;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private int _scanProgress;

    [ObservableProperty]
    private string? _rowMessage;

    /// <summary>コレクションとして選択中(REQ-053 v1.3/CR-2: 選択スコープの表示)。</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>normal 画像数(REQ-053 v1.3/CR-8: コレクション項目に画像数を表示)。</summary>
    [ObservableProperty]
    private int _imageCount;

    /// <summary>画像数の表示文言(i18n: collection.sidebar.imageCount)。</summary>
    public string ImageCountText => _owner.FormatImageCount(ImageCount);

    partial void OnImageCountChanged(int value) => OnPropertyChanged(nameof(ImageCountText));

    /// <summary>言語切替時の表示文言更新(DF-3)。</summary>
    public void RefreshTexts() => OnPropertyChanged(nameof(ImageCountText));

    [RelayCommand]
    private Task ScanAsync() => _owner.ScanAsync(this);

    [RelayCommand]
    private Task DeleteAsync() => _owner.DeleteAsync(this);

    [RelayCommand]
    private Task RelinkAsync() => _owner.OpenRelinkAsync(this);

    [RelayCommand]
    private Task SavePatternsAsync() => _owner.SavePatternsAsync(this);

    partial void OnIsActiveChanged(bool value)
    {
        if (!_loading)
        {
            _ = _owner.UpdateFolderAsync(this);
        }
    }

    partial void OnIncludeSubfoldersChanged(bool value)
    {
        if (!_loading)
        {
            _ = _owner.UpdateFolderAsync(this);
        }
    }

    public void Apply(SyncFolder updated) => Folder = updated;
}

/// <summary>
/// 同期フォルダ管理(M-UI-013、REQ-010/015、E-UI-SHELL-021)。
/// 登録(パス一意・重複は明示エラー)・削除(確認ダイアログ必須 — タグ関連も消える)・
/// スキャン実行+サマリ表示・再リンク入口。
/// </summary>
public sealed partial class FolderManagementViewModel : ObservableObject
{
    private readonly ISyncFolderRepository _folders;
    private readonly ScanCoordinator _scans;
    private readonly LocalizationService _localization;
    private readonly IWindowService _windows;

    public FolderManagementViewModel(
        ISyncFolderRepository folders,
        ScanCoordinator scans,
        LocalizationService localization,
        IWindowService windows)
    {
        _folders = folders;
        _scans = scans;
        _localization = localization;
        _windows = windows;
        Loc = new LocalizationProxy(localization);
        localization.CultureChanged += (_, _) =>
        {
            // DF-3: Loc 差し替えで全文言バインディングを再評価させる(K-AVALONIA の罠対策)
            Loc = new LocalizationProxy(localization);
            OnPropertyChanged(nameof(Loc));
            foreach (var row in Folders)
            {
                row.RefreshTexts();
            }
        };
    }

    public LocalizationProxy Loc { get; private set; }

    /// <summary>画像数の表示文言(REQ-053/CR-8)。</summary>
    public string FormatImageCount(int count)
        => _localization.T("collection.sidebar.imageCount", new Dictionary<string, string>
        {
            ["count"] = count.ToString(System.Globalization.CultureInfo.InvariantCulture),
        });

    public ObservableCollection<FolderRowViewModel> Folders { get; } = [];

    [ObservableProperty]
    private string? _statusMessage;

    public bool IsEmpty => Folders.Count == 0;

    /// <summary>フォルダ・スキャン状態の永続変更があった(画像タブの再読込用、v1.2 左ペイン埋め込み)。</summary>
    public event EventHandler? DataChanged;

    public async Task LoadAsync()
    {
        Folders.Clear();
        foreach (var folder in await _folders.GetAllAsync())
        {
            Folders.Add(new FolderRowViewModel(this, folder, FormatLastScan(folder.LastScan)));
        }

        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand]
    private async Task AddFolderAsync()
    {
        var path = await _windows.PickFolderAsync(_localization.T("folder.selectFolder"));
        if (path is null)
        {
            return;
        }

        var folder = new SyncFolder
        {
            Id = IdGenerator.NewId(),
            Name = System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(path)),
            Path = path,
        };

        var result = await _folders.AddAsync(folder);
        if (!result.IsSuccess)
        {
            StatusMessage = ErrorMessages.Resolve(_localization, result.Error);
            return;
        }

        StatusMessage = null;
        await LoadAsync();
        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task ScanAsync(FolderRowViewModel row)
    {
        // ECO-130/REQ-100: 再スキャン(last_scan あり)は二段階(差分計算→サマリー→適用/破棄)。
        // 初回スキャン(last_scan=NULL)は従来の一段階+段階的公開(REQ-086・登録行為=適用の同意)
        if (row.Folder.LastScan is not null)
        {
            await StagedRescanAsync(row);
            return;
        }

        row.IsScanning = true;
        row.ScanProgress = 0;
        row.RowMessage = _localization.T("folder.scanning");
        try
        {
            var progress = new Progress<int>(p => row.ScanProgress = p);
            var result = await _scans.ScanAsync(row.Folder.Id, progress, CancellationToken.None);
            if (result.IsSuccess && result.Value is { } summary)
            {
                // スキャン結果サマリ(REQ-015)
                row.RowMessage = _localization.T("folder.scanSummary", new Dictionary<string, string>
                {
                    ["added"] = summary.Added.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["updated"] = summary.Updated.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["missing"] = summary.Missing.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["pending"] = summary.Pending.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["skipped"] = summary.Skipped.ToString(System.Globalization.CultureInfo.InvariantCulture),
                });
            }
            else
            {
                row.RowMessage = ErrorMessages.Resolve(_localization, result.Error);
            }
        }
        finally
        {
            row.IsScanning = false;
            var updated = await _folders.GetByIdAsync(row.Folder.Id);
            if (updated is not null)
            {
                row.Apply(updated);
                row.LastScanText = FormatLastScan(updated.LastScan);
            }

            DataChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task StagedRescanAsync(FolderRowViewModel row)
    {
        row.IsScanning = true;
        try
        {
            var outcome = await _windows.ShowScanStagingAsync(row.Folder);
            if (outcome.Error is not null)
            {
                row.RowMessage = outcome.Error;
                return;
            }

            if (outcome.Applied && outcome.Summary is { } summary)
            {
                row.RowMessage = _localization.T("folder.scanSummary", new Dictionary<string, string>
                {
                    ["added"] = summary.Added.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["updated"] = summary.Updated.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["missing"] = summary.Missing.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["pending"] = summary.Pending.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["skipped"] = summary.Skipped.ToString(System.Globalization.CultureInfo.InvariantCulture),
                });
            }
            else
            {
                // 破棄・キャンセル= DB 完全無変更(REQ-100)
                row.RowMessage = _localization.T("folder.scanDiscarded");
            }

            if (outcome.Applied)
            {
                var updated = await _folders.GetByIdAsync(row.Folder.Id);
                if (updated is not null)
                {
                    row.Apply(updated);
                    row.LastScanText = FormatLastScan(updated.LastScan);
                }

                DataChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        finally
        {
            row.IsScanning = false;
        }
    }

    public async Task DeleteAsync(FolderRowViewModel row)
    {
        // 削除時は配下 images が連鎖削除されるため確認ダイアログ必須(REQ-010)
        var message = _localization.T("folder.deleteConfirm", new Dictionary<string, string> { ["name"] = row.Name });
        if (!await _windows.ConfirmAsync(_localization.T("collection.deleteConfirmTitle"), message,
                _localization.T("common.ctaDelete"), destructive: true))
        {
            return;
        }

        await _folders.DeleteAsync(row.Folder.Id);
        await LoadAsync();
        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task OpenRelinkAsync(FolderRowViewModel row) => _windows.ShowRelinkAsync(row.Folder.Id);

    public async Task UpdateFolderAsync(FolderRowViewModel row)
    {
        var updated = row.Folder with
        {
            IsActive = row.IsActive,
            IncludeSubfolders = row.IncludeSubfolders,
        };
        await _folders.UpdateAsync(updated);
        row.Apply(updated);
        DataChanged?.Invoke(this, EventArgs.Empty); // is_active は表示対象に影響(REQ-010)
    }

    public async Task SavePatternsAsync(FolderRowViewModel row)
    {
        // 除外パターンはファイル名の完全一致(大文字小文字無視)。glob/regex は対象外(REQ-010)
        var patterns = row.ExcludePatternsText
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        var updated = row.Folder with { ExcludePatterns = patterns };
        await _folders.UpdateAsync(updated);
        row.Apply(updated);
        StatusMessage = _localization.T("success.saved");
    }

    private string FormatLastScan(string? lastScan)
    {
        return lastScan is null
            ? _localization.T("folder.neverScanned")
            : LocaleFormats.FormatTimestamp(lastScan, _localization.CurrentLocale);
    }
}
