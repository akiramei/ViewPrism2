using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ViewPrism2.App.ViewModels;
using ViewPrism2.App.Views;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Repositories;
using ViewPrism2.Core.Services;
using ViewPrism2.Core.Services.Repair;
using ViewPrism2.Core.Services.Similarity;
using ViewPrism2.Core.Services.Viewer;
using ViewPrism2.Infrastructure.Database;
using ViewPrism2.Infrastructure.Scanning;
using ViewPrism2.Infrastructure.Settings;

namespace ViewPrism2.App.Services;

/// <summary>
/// IWindowService の View 層実装(K-MVVM: ダイアログ表示の実体)。
/// Window.ShowDialog / StorageProvider.OpenFolderPickerAsync(K-AVALONIA)。
/// </summary>
public sealed class WindowService : IWindowService
{
    private readonly ISyncFolderRepository _folders;
    private readonly IImageRepository _images;
    private readonly ITagRepository _tags;
    private readonly TagService _tagService;
    private readonly ViewService _viewService;
    private readonly ScanCoordinator _scans;
    private readonly RelinkService _relink;
    private readonly IntegrityReviewService _integrityReview;
    private readonly ImageMemoryCache _imageCache;
    private readonly TrashService _trashService;
    private readonly LocalizationService _localization;
    private readonly AppSettings _settings;
    private readonly SettingsStore _settingsStore;
    private readonly SnapshotService _snapshots;
    private readonly CollectionPackageExporter _packageExporter;
    private readonly CollectionPackageImporter _packageImporter;

    public WindowService(
        ISyncFolderRepository folders,
        IImageRepository images,
        ITagRepository tags,
        TagService tagService,
        ViewService viewService,
        ScanCoordinator scans,
        RelinkService relink,
        IntegrityReviewService integrityReview,
        ImageMemoryCache imageCache,
        TrashService trashService,
        LocalizationService localization,
        AppSettings settings,
        SettingsStore settingsStore,
        SnapshotService snapshots,
        CollectionPackageExporter packageExporter,
        CollectionPackageImporter packageImporter)
    {
        _folders = folders;
        _images = images;
        _tags = tags;
        _tagService = tagService;
        _viewService = viewService;
        _scans = scans;
        _relink = relink;
        _integrityReview = integrityReview;
        _imageCache = imageCache;
        _trashService = trashService;
        _localization = localization;
        _settings = settings;
        _settingsStore = settingsStore;
        _snapshots = snapshots;
        _packageExporter = packageExporter;
        _packageImporter = packageImporter;
    }

    /// <summary>モーダルダイアログのオーナー(App 起動時に設定)。</summary>
    public Window? Owner { get; set; }

    public async Task<bool> ConfirmAsync(string title, string message, string confirmLabel,
        bool destructive = false, string? cancelLabel = null)
    {
        if (Owner is null)
        {
            return false;
        }

        var dialog = new ConfirmDialog(new LocalizationProxy(_localization), title, message,
            confirmLabel, destructive, cancelLabel);
        return await dialog.ShowDialog<bool?>(Owner) == true;
    }

    public async Task<bool> ConfirmListAsync(
        string title,
        string lead,
        string supportingMessage,
        string confirmLabel,
        IReadOnlyList<ConfirmationListItem> items,
        string? cancelLabel = null)
    {
        if (Owner is null)
        {
            return false;
        }

        var dialog = new ConfirmDialog(
            new LocalizationProxy(_localization),
            title,
            lead,
            confirmLabel,
            destructive: false,
            cancelLabel,
            items,
            supportingMessage);
        return await dialog.ShowDialog<bool?>(Owner) == true;
    }

    public async Task<string?> PickFolderAsync(string title)
    {
        if (Owner is null)
        {
            return null;
        }

        var results = await Owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });
        return results.Count > 0 ? results[0].TryGetLocalPath() : null;
    }

    public async Task ShowFolderManagementAsync()
    {
        if (Owner is null)
        {
            return;
        }

        var vm = new FolderManagementViewModel(_folders, _scans, _localization, this);
        var window = new FolderManagementWindow { DataContext = vm };
        await vm.LoadAsync();
        _activeFolderManagement = window;
        try
        {
            await window.ShowDialog(Owner);
        }
        finally
        {
            _activeFolderManagement = null;
        }
    }

    /// <summary>スキャン結果確認の親付け先(R8 所見3: sibling 活性の穴を塞ぐ)。</summary>
    private Window? _activeFolderManagement;

    public async Task<bool> ShowIntegrityReviewAsync(string collectionId)
    {
        if (Owner is null)
        {
            return false;
        }

        var folder = await _folders.GetByIdAsync(collectionId);
        if (folder is null)
        {
            return false;
        }

        var vm = new IntegrityReviewViewModel(
            _integrityReview,
            new PendingReviewService(_images),
            _images,
            _tags,
            _relink,
            _trashService,
            _localization,
            this,
            folder);
        var window = new IntegrityReviewWindow { DataContext = vm };
        await window.ShowDialog(Owner);
        return vm.Adjudicated;
    }

    public async Task<ScanStagingOutcome> ShowScanStagingAsync(SyncFolder folder)
    {
        // R8 所見3: Owner(メイン窓)親だとフォルダ管理ウィンドウが sibling として活性のまま=
        // stage→apply 間の「アプリ内並行変更なし」(REQ-100)が破れる。フォルダ管理へ親付けして塞ぐ
        var owner = _activeFolderManagement ?? Owner;
        if (owner is null)
        {
            return ScanStagingOutcome.Discarded;
        }

        // ECO-130/REQ-100: モーダル(差分計算は Window.Opened で開始)
        var vm = new ScanSummaryViewModel(_scans, _localization, this, folder);
        var window = new ScanSummaryWindow { DataContext = vm };
        await window.ShowDialog(owner);
        return vm.Outcome;
    }

    public Task ShowSettingsAsync() => ShowSettingsAsync(SettingsSection.General);

    public async Task ShowSettingsAsync(SettingsSection section)
    {
        if (Owner is null)
        {
            return;
        }

        // ECO-077/E-1: スナップショット行サマリ(最終作成・件数)のため SnapshotService を渡す
        var vm = new SettingsViewModel(_localization, _settings, _settingsStore, this, _snapshots, section);
        var window = new SettingsWindow { DataContext = vm };
        await window.ShowDialog(Owner);
    }

    public async Task ShowSnapshotsAsync()
    {
        if (Owner is null)
        {
            return;
        }

        SnapshotWindow? window = null;
        var vm = new SnapshotViewModel(
            _snapshots,
            _settings,
            _settingsStore,
            _localization,
            PickFolderAsync,
            async item =>
            {
                var dialog = new SnapshotRestoreConfirmWindow(new LocalizationProxy(_localization), item);
                return await dialog.ShowDialog<bool?>(window!) == true;
            },
            RestartApplication);
        window = new SnapshotWindow { DataContext = vm };
        vm.Load();
        await window.ShowDialog(Owner);
    }

    public async Task ShowCollectionExportAsync()
    {
        // ECO-077(SS-001 再裁定/M5): 入口=設定 ▸ データとバックアップ。コレクション文脈が無いため
        // 対象は B-1 内で選択する(CAD interaction 表)。コレクション 0 のライブラリでは開かない。
        if (Owner is null || await _folders.GetAllAsync() is not { Count: > 0 } collections)
        {
            return;
        }

        var vm = new CollectionExportViewModel(
            _packageExporter, collections, _localization, PickSaveFileAsync, PackageDirectory);
        var window = new CollectionExportWindow { DataContext = vm };
        await vm.LoadAsync();
        await window.ShowDialog(Owner);
    }

    public async Task ShowCollectionImportAsync()
    {
        // ECO-077 gate①裁定=案A: 取り込み先は B-2 内で選択(既定=未選択・選択まで「次へ」不活性)
        if (Owner is null || await _folders.GetAllAsync() is not { Count: > 0 } collections)
        {
            return;
        }

        var vm = new CollectionImportViewModel(
            _packageImporter, collections, _localization, PickPackageFileAsync, () => _tags.GetAllAsync());
        var window = new CollectionImportWindow { DataContext = vm };
        await window.ShowDialog(Owner);
    }

    /// <summary>パッケージ管理フォルダ(ECO-074/案A: settings 上書き可・null=既定)。</summary>
    private string PackageDirectory =>
        _settings.CollectionPackageDirectory ?? CollectionPackageFormat.DefaultDirectory;

    /// <summary>
    /// picker の起点=管理フォルダ(ECO-074/案A: 常に管理フォルダ起点。逸脱先は永続しない=
    /// 「最後に使ったフォルダ」の再発防止)。無ければ作る(初回でも起点が成立する)。
    /// </summary>
    private async Task<Avalonia.Platform.Storage.IStorageFolder?> GetPackageStartFolderAsync()
    {
        try
        {
            Directory.CreateDirectory(PackageDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null; // 起点なしで picker を開く(OS 既定)よりよい代替がないため続行
        }

        return await Owner!.StorageProvider.TryGetFolderFromPathAsync(PackageDirectory);
    }

    /// <summary>保存ファイル選択(B-1 出力先)。キャンセルは null。</summary>
    private async Task<string?> PickSaveFileAsync(string title, string suggestedName)
    {
        if (Owner is null)
        {
            return null;
        }

        var file = await Owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = "json",
            SuggestedStartLocation = await GetPackageStartFolderAsync(),
        });
        return file?.TryGetLocalPath();
    }

    /// <summary>パッケージファイル選択(B-2)。キャンセルは null。</summary>
    private async Task<string?> PickPackageFileAsync(string title)
    {
        if (Owner is null)
        {
            return null;
        }

        var files = await Owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("ViewPrism2 collection") { Patterns = ["*.viewprism2-collection.json", "*.json"] },
            ],
            SuggestedStartLocation = await GetPackageStartFolderAsync(),
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    /// <summary>
    /// 復元予約後の自動再起動(ECO-072 案A・CAD A-2「実行後、自動的に再起動します」)。
    /// Exit で DB 接続が破棄された後に新プロセスを起動する(差し替えとのファイル競合を避ける)。
    /// </summary>
    private static void RestartApplication()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime
            is not Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            return;
        }

        if (Environment.ProcessPath is { Length: > 0 } exe)
        {
            desktop.Exit += (_, _) => System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true });
        }

        desktop.Shutdown();
    }

    public async Task<bool> ShowTagEditorAsync(Tag? existing)
    {
        if (Owner is null)
        {
            return false;
        }

        var vm = new TagEditorViewModel(existing, _tagService, _tags, _localization);
        var window = new TagEditorWindow { DataContext = vm };
        vm.Saved += (_, _) => window.Close(true);
        await vm.LoadAsync();
        return await window.ShowDialog<bool?>(Owner) == true;
    }

    public async Task<bool> ShowViewEditDialogAsync(View? existing)
    {
        if (Owner is null)
        {
            return false;
        }

        var viewTags = await LoadViewTagsAsync(existing);
        var vm = new ViewEditDialogViewModel(existing, viewTags, _viewService, _localization);
        var window = new ViewEditDialog { DataContext = vm };
        vm.Saved += (_, _) => window.Close(true);
        return await window.ShowDialog<bool?>(Owner) == true;
    }

    /// <summary>
    /// ビュー編集モーダルの表示列タグ母集合(ECO-025 α・REQ-079): 当該ビューのタグ階層メンバーを
    /// 出現順で distinct 取得する。新規ビュー(existing=null)・階層なしは空(基本情報のみ選択可)。
    /// </summary>
    private async Task<IReadOnlyList<Tag>> LoadViewTagsAsync(View? view)
    {
        if (view is null)
        {
            return [];
        }

        var nodes = await _viewService.GetHierarchyAsync(view.Id);
        if (nodes.Count == 0)
        {
            return [];
        }

        var all = await _tags.GetAllAsync();
        var byId = all.ToDictionary(t => t.Id, StringComparer.Ordinal);
        var result = new List<Tag>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            if (seen.Add(node.TagId) && byId.TryGetValue(node.TagId, out var tag))
            {
                result.Add(tag);
            }
        }

        return result;
    }

    public async Task<IReadOnlyList<string>?> ShowNumericValueDialogAsync(
        Tag tag, NumericTagSettings? settings, int selectionCount)
    {
        if (Owner is null)
        {
            return null;
        }

        var vm = new NumericValueDialogViewModel(tag, settings, selectionCount, _localization);
        var window = new NumericValueDialog { DataContext = vm };
        return await window.ShowDialog<IReadOnlyList<string>?>(Owner);
    }

    public async Task<NodeConditionResult?> ShowNodeConditionDialogAsync(
        Tag tag, HierarchyConditionType? currentType, string? currentValueJson)
    {
        if (Owner is null)
        {
            return null;
        }

        var vm = new NodeConditionDialogViewModel(tag, currentType, currentValueJson, _localization);
        var window = new NodeConditionDialog { DataContext = vm };
        return await window.ShowDialog<NodeConditionResult?>(Owner);
    }

    public async Task<NodeConditionResult?> ShowNodeSettingsDialogAsync(Tag tag, NodeSettingsRequest request)
    {
        if (Owner is null)
        {
            return null;
        }

        // ECO-086: 配置タグの設定(展開モード+条件)。同一ダイアログを拡張入力で開く
        var vm = new NodeConditionDialogViewModel(
            tag, request.ConditionType, request.ConditionValueJson, _localization,
            request.ExpansionMode, request.HideEmptyValues, request.DefinedValuesAvailable);
        var window = new NodeConditionDialog { DataContext = vm };
        return await window.ShowDialog<NodeConditionResult?>(Owner);
    }

    public async Task ShowRelinkAsync(string folderId)
    {
        if (Owner is null)
        {
            return;
        }

        // _folders は候補/missing 行のサムネイル絶対パス解決用(DC-RELINK-001/ECO-004。IntegrityReviewViewModel と同型)。
        var vm = new RelinkViewModel(folderId, _images, _folders, _relink, _localization, this);
        var window = new RelinkWindow { DataContext = vm };
        await vm.LoadAsync();
        await window.ShowDialog(Owner);
    }

    public void ShowViewer(IReadOnlyList<ImageEntry> ordered, int startIndex)
    {
        // ビューア設定を復元(REQ-059/REQ-077)し、変更は即時 settings.json へ保存する
        var settings = ViewerSettingsModel.FromSettings(_settings);
        var vm = new ViewerViewModel(ordered, startIndex, settings, Persist)
        {
            Loc = new LocalizationProxy(_localization),
        };

        // タグ制御マッピング picker 用に現存タグのみを供給(major-1 補正・ECO-022)。
        // 削除済みタグは出さない。Resolve(Core)はタグ存在台帳を取らず自然無視で結果整合する。
        _ = LoadTagControlOptionsAsync(vm);

        var window = new ViewerWindow(_imageCache) { DataContext = vm };
        window.Show(Owner!);

        void Persist(ViewerSettingsModel model)
        {
            model.ApplyTo(_settings);
            _settingsStore.Save(_settings);
        }
    }

    private async Task LoadTagControlOptionsAsync(ViewerViewModel vm)
    {
        try
        {
            var tags = await _tags.GetAllAsync();
            var options = tags
                .Select(t => new TagPickerOption(t.Id, t.Name, t.Color))
                .ToList();
            vm.SetAvailableTags(options);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Data.Common.DbException)
        {
            // タグ取得失敗時はマッピング picker を空にして続行(ビューア本体は機能する)
            vm.SetAvailableTags([]);
        }
    }

    // ECO-051: ShowSimilarSearchAsync / ShowMergeAsync / ShowTrashAsync(V3 旧モーダル一式)は撤去(残骸)。

}
