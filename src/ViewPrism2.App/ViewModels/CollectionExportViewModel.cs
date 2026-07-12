using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using ViewPrism2.Infrastructure.Database;

namespace ViewPrism2.App.ViewModels;

/// <summary>
/// B-1 コレクションを書き出す(ECO-073・CAD snapshot_export_import B-1)。
/// ECO-077(SS-001 再裁定=入口は設定 E-1・コレクション文脈なし): 対象コレクションは
/// B-1 内で選択する(CAD interaction 表)。既定=先頭(mock は常に選択済み状態を描く)。
/// 画像実体は含めない(M3)。
/// </summary>
public sealed partial class CollectionExportViewModel : ObservableObject
{
    private readonly CollectionPackageExporter _exporter;
    private readonly LocalizationService _localization;
    private readonly Func<string, string, Task<string?>> _pickSaveFile;
    private readonly string _packageDirectory;
    private CancellationTokenSource? _cts;

    public CollectionExportViewModel(
        CollectionPackageExporter exporter,
        IReadOnlyList<SyncFolder> collections,
        LocalizationService localization,
        Func<string, string, Task<string?>> pickSaveFile,
        string? packageDirectory = null)
    {
        _exporter = exporter;
        Collections = collections;
        _localization = localization;
        _pickSaveFile = pickSaveFile;
        // ECO-074 案A: 既定出力先=管理フォルダ(「最後に使ったフォルダ」等の無管理な起点を用いない)
        _packageDirectory = packageDirectory ?? CollectionPackageFormat.DefaultDirectory;
        Loc = new LocalizationProxy(localization);
        localization.CultureChanged += (_, _) =>
        {
            Loc = new LocalizationProxy(localization);
            OnPropertyChanged(nameof(Loc));
        };
        _selectedCollection = collections.Count > 0 ? collections[0] : null;
        _outputPath = DefaultOutputPath();
    }

    public LocalizationProxy Loc { get; private set; }

    /// <summary>B-1 コレクション選択の母集合(ECO-077: 入口にコレクション文脈が無いため全コレクション)。</summary>
    public IReadOnlyList<SyncFolder> Collections { get; }

    /// <summary>書き出し対象。変更で既定出力先(<名前>.viewprism2-collection.json)と件数表示を追随させる。</summary>
    [ObservableProperty]
    private SyncFolder? _selectedCollection;

    private string DefaultOutputPath() => Path.Combine(
        _packageDirectory, (SelectedCollection?.Name ?? "collection") + CollectionPackageFormat.FileExtension);

    partial void OnSelectedCollectionChanged(SyncFolder? value)
    {
        OutputPath = DefaultOutputPath();
        _ = LoadAsync();
    }

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

    /// <summary>B-1 の件数表示(N 項目 · タグ N)。選択コレクションに追随(CAD: 項目数・タグ数を表示)。</summary>
    public async Task LoadAsync()
    {
        if (SelectedCollection is not { } collection)
        {
            CollectionSummary = "";
            return;
        }

        var (images, tags) = await _exporter.CountAsync(collection.Id);
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
        if (IsExporting || SelectedCollection is not { } collection)
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
            var result = await _exporter.ExportAsync(collection.Id, OutputPath, progress, _cts.Token);
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
