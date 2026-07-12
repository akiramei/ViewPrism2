using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using ViewPrism2.Infrastructure.Database;

namespace ViewPrism2.App.ViewModels;

/// <summary>
/// B-1 コレクションを書き出す(ECO-073・CAD snapshot_export_import B-1)。
/// 対象=入口の三点メニューを開いたコレクション(SS-001 裁定(b))。画像実体は含めない(M3)。
/// </summary>
public sealed partial class CollectionExportViewModel : ObservableObject
{
    private readonly CollectionPackageExporter _exporter;
    private readonly SyncFolder _collection;
    private readonly LocalizationService _localization;
    private readonly Func<string, string, Task<string?>> _pickSaveFile;
    private CancellationTokenSource? _cts;

    public CollectionExportViewModel(
        CollectionPackageExporter exporter,
        SyncFolder collection,
        LocalizationService localization,
        Func<string, string, Task<string?>> pickSaveFile,
        string? packageDirectory = null)
    {
        _exporter = exporter;
        _collection = collection;
        _localization = localization;
        _pickSaveFile = pickSaveFile;
        Loc = new LocalizationProxy(localization);
        localization.CultureChanged += (_, _) =>
        {
            Loc = new LocalizationProxy(localization);
            OnPropertyChanged(nameof(Loc));
        };
        // ECO-074 案A: 既定出力先=管理フォルダ(「最後に使ったフォルダ」等の無管理な起点を用いない)
        _outputPath = Path.Combine(
            packageDirectory ?? CollectionPackageFormat.DefaultDirectory,
            _collection.Name + CollectionPackageFormat.FileExtension);
    }

    public LocalizationProxy Loc { get; private set; }

    public string CollectionName => _collection.Name;

    [ObservableProperty]
    private string _collectionSummary = "";

    [ObservableProperty]
    private string _outputPath;

    [ObservableProperty]
    private bool _isExporting;

    [ObservableProperty]
    private double _progressRatio;

    [ObservableProperty]
    private string _progressText = "";

    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>書き出し完了(閉じる導線へ切替)。</summary>
    [ObservableProperty]
    private bool _done;

    /// <summary>B-1 の件数表示(N 項目 · タグ N)。</summary>
    public async Task LoadAsync()
    {
        var (images, tags) = await _exporter.CountAsync(_collection.Id);
        CollectionSummary = _localization.T("package.collectionSummary", new Dictionary<string, string>
        {
            ["images"] = images.ToString("N0", System.Globalization.CultureInfo.InvariantCulture),
            ["tags"] = tags.ToString(System.Globalization.CultureInfo.InvariantCulture),
        });
    }

    [RelayCommand]
    private async Task ChangeOutputAsync()
    {
        var picked = await _pickSaveFile(
            _localization.T("package.exportTitle"), Path.GetFileName(OutputPath));
        if (!string.IsNullOrEmpty(picked))
        {
            OutputPath = picked;
        }
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (IsExporting)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        IsExporting = true;
        StatusMessage = null;
        try
        {
            var progress = new Progress<(long Done, long Total)>(p =>
            {
                ProgressRatio = p.Total > 0 ? (double)p.Done / p.Total : 0;
                ProgressText = $"{p.Done:N0} / {p.Total:N0}";
            });
            var result = await _exporter.ExportAsync(_collection.Id, OutputPath, progress, _cts.Token);
            if (result.IsSuccess)
            {
                Done = true;
                StatusMessage = _localization.T("package.exported", new Dictionary<string, string>
                {
                    ["path"] = result.Value!.FilePath,
                });
            }
            else
            {
                StatusMessage = _localization.T("package.exportFailed", new Dictionary<string, string>
                {
                    ["message"] = result.Message ?? "",
                });
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = _localization.T("package.exportCancelled");
        }
        finally
        {
            IsExporting = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private void CancelExport() => _cts?.Cancel();
}
