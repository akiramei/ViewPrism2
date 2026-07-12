using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using ViewPrism2.Infrastructure.Database;
using ViewPrism2.Infrastructure.Settings;

namespace ViewPrism2.App.ViewModels;

/// <summary>A-1 一覧の 1 行(CAD snapshot_export_import A-1)。検証待ちは復元不可。</summary>
public sealed class SnapshotItemViewModel(SnapshotInfo info, LocalizationService localization)
{
    public SnapshotInfo Info { get; } = info;

    public string CreatedAtText => Info.CreatedAtUtc.ToLocalTime()
        .ToString("yyyy/MM/dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);

    public string AppVersionText => $"app_version {Info.AppVersion ?? "?"}";

    public string SizeText => (Info.SizeBytes / 1024.0 / 1024.0)
        .ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " MB";

    public bool IsVerified => Info.IsVerified;

    public string StatusText => localization.T(Info.IsVerified ? "snapshot.verified" : "snapshot.unverified");
}

/// <summary>
/// スナップショット管理(ECO-072 A-1/A-2、CAD snapshot_export_import)。
/// 作成=SnapshotService(VACUUM INTO→検証→アトミック確定)。復元=A-2 確認→復元予約→再起動(案A)。
/// ダイアログ表示(A-2/フォルダ選択)と再起動は WindowService がデリゲートで注入する(K-MVVM)。
/// </summary>
public sealed partial class SnapshotViewModel : ObservableObject
{
    private readonly SnapshotService _service;
    private readonly AppSettings _settings;
    private readonly SettingsStore _store;
    private readonly LocalizationService _localization;
    private readonly Func<string, Task<string?>> _pickFolder;
    private readonly Func<SnapshotItemViewModel, Task<bool>> _confirmRestore;
    private readonly Action _requestRestart;
    private CancellationTokenSource? _createCts;

    public SnapshotViewModel(
        SnapshotService service,
        AppSettings settings,
        SettingsStore store,
        LocalizationService localization,
        Func<string, Task<string?>> pickFolder,
        Func<SnapshotItemViewModel, Task<bool>> confirmRestore,
        Action requestRestart)
    {
        _service = service;
        _settings = settings;
        _store = store;
        _localization = localization;
        _pickFolder = pickFolder;
        _confirmRestore = confirmRestore;
        _requestRestart = requestRestart;
        Loc = new LocalizationProxy(localization);
        localization.CultureChanged += (_, _) =>
        {
            Loc = new LocalizationProxy(localization);
            OnPropertyChanged(nameof(Loc));
        };
    }

    public LocalizationProxy Loc { get; private set; }

    public ObservableCollection<SnapshotItemViewModel> Items { get; } = [];

    /// <summary>保存先(SS-002: アプリ共通・settings.json 永続)。</summary>
    public string Directory => _settings.SnapshotDirectory ?? _service.DefaultDirectory;

    [ObservableProperty]
    private bool _isCreating;

    /// <summary>作成中の件数表示(CAD A-1「タグ N / ビュー N / メタデータ N 件」)。</summary>
    [ObservableProperty]
    private string _creatingCountsText = "";

    [ObservableProperty]
    private string? _statusMessage;

    public bool HasItems => Items.Count > 0;

    /// <summary>一覧を読み直す(SnapshotWindow 表示時と作成/復元操作後)。</summary>
    public void Load()
    {
        Items.Clear();
        foreach (var info in _service.List(Directory))
        {
            Items.Add(new SnapshotItemViewModel(info, _localization));
        }

        OnPropertyChanged(nameof(HasItems));
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        if (IsCreating)
        {
            return;
        }

        _createCts = new CancellationTokenSource();
        IsCreating = true;
        StatusMessage = null;
        try
        {
            var counts = await _service.CountAsync(_createCts.Token);
            CreatingCountsText = _localization.T("snapshot.creatingCounts", new Dictionary<string, string>
            {
                ["tags"] = counts.Tags.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["views"] = counts.Views.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["images"] = counts.Images.ToString("N0", System.Globalization.CultureInfo.InvariantCulture),
            });
            var result = await _service.CreateAsync(Directory, _createCts.Token);
            StatusMessage = result.IsSuccess
                ? _localization.T("snapshot.created")
                : _localization.T("snapshot.createFailed", new Dictionary<string, string> { ["message"] = result.Message ?? "" });
        }
        catch (OperationCanceledException)
        {
            StatusMessage = _localization.T("snapshot.createCancelled");
        }
        finally
        {
            IsCreating = false;
            _createCts.Dispose();
            _createCts = null;
            Load();
        }
    }

    [RelayCommand]
    private void CancelCreate() => _createCts?.Cancel();

    [RelayCommand]
    private async Task ChangeDirectoryAsync()
    {
        var picked = await _pickFolder(_localization.T("snapshot.saveDir"));
        if (string.IsNullOrEmpty(picked))
        {
            return;
        }

        _settings.SnapshotDirectory = picked;
        _store.Save(_settings);
        OnPropertyChanged(nameof(Directory));
        Load();
    }

    /// <summary>
    /// A-2 確認→復元直前検証(integrity_check・未知 migration 拒否)→復元予約→再起動(案A)。
    /// 検証待ち(未検証)は入口で拒否する(CAD: 復元ボタン無効の裏面ガード)。
    /// </summary>
    [RelayCommand]
    private async Task RestoreAsync(SnapshotItemViewModel? item)
    {
        if (item is null || !item.IsVerified || IsCreating)
        {
            return;
        }

        if (!await _confirmRestore(item))
        {
            return;
        }

        var validation = _service.ValidateForRestore(item.Info.FilePath);
        if (!validation.IsSuccess)
        {
            StatusMessage = _localization.T("snapshot.validateFailed", new Dictionary<string, string>
            {
                ["message"] = validation.Message ?? "",
            });
            return;
        }

        _service.RequestRestore(item.Info.FilePath, Directory);
        _requestRestart();
    }
}
