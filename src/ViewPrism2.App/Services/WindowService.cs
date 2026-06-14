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
    private readonly ScanService _scan;
    private readonly RelinkService _relink;
    private readonly ImageMemoryCache _imageCache;
    private readonly SimilaritySearchService _similaritySearch;
    private readonly MergeService _mergeService;
    private readonly CriteriaSearchService _criteriaSearch;
    private readonly TrashService _trashService;
    private readonly LocalizationService _localization;
    private readonly AppSettings _settings;
    private readonly SettingsStore _settingsStore;

    public WindowService(
        ISyncFolderRepository folders,
        IImageRepository images,
        ITagRepository tags,
        TagService tagService,
        ViewService viewService,
        ScanService scan,
        RelinkService relink,
        ImageMemoryCache imageCache,
        SimilaritySearchService similaritySearch,
        MergeService mergeService,
        CriteriaSearchService criteriaSearch,
        TrashService trashService,
        LocalizationService localization,
        AppSettings settings,
        SettingsStore settingsStore)
    {
        _folders = folders;
        _images = images;
        _tags = tags;
        _tagService = tagService;
        _viewService = viewService;
        _scan = scan;
        _relink = relink;
        _imageCache = imageCache;
        _similaritySearch = similaritySearch;
        _mergeService = mergeService;
        _criteriaSearch = criteriaSearch;
        _trashService = trashService;
        _localization = localization;
        _settings = settings;
        _settingsStore = settingsStore;
    }

    /// <summary>モーダルダイアログのオーナー(App 起動時に設定)。</summary>
    public Window? Owner { get; set; }

    public async Task<bool> ConfirmAsync(string title, string message)
    {
        if (Owner is null)
        {
            return false;
        }

        var dialog = new ConfirmDialog(new LocalizationProxy(_localization), title, message);
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

        var vm = new FolderManagementViewModel(_folders, _scan, _localization, this);
        var window = new FolderManagementWindow { DataContext = vm };
        await vm.LoadAsync();
        await window.ShowDialog(Owner);
    }

    public async Task ShowSettingsAsync()
    {
        if (Owner is null)
        {
            return;
        }

        var vm = new SettingsViewModel(_localization, _settings, _settingsStore);
        var window = new SettingsWindow { DataContext = vm };
        await window.ShowDialog(Owner);
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

        var vm = new ViewEditDialogViewModel(existing, _viewService, _localization);
        var window = new ViewEditDialog { DataContext = vm };
        vm.Saved += (_, _) => window.Close(true);
        return await window.ShowDialog<bool?>(Owner) == true;
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

    public async Task ShowRelinkAsync(string folderId)
    {
        if (Owner is null)
        {
            return;
        }

        var vm = new RelinkViewModel(folderId, _images, _relink, _localization, this);
        var window = new RelinkWindow { DataContext = vm };
        await vm.LoadAsync();
        await window.ShowDialog(Owner);
    }

    public void ShowViewer(IReadOnlyList<ImageEntry> ordered, int startIndex)
    {
        // ビューア設定を復元(REQ-059)し、変更は即時 settings.json へ保存する
        var settings = ViewerSettingsModel.FromSettings(_settings);
        var vm = new ViewerViewModel(ordered, startIndex, settings, Persist)
        {
            Loc = new LocalizationProxy(_localization),
        };
        var window = new ViewerWindow(_imageCache) { DataContext = vm };
        window.Show(Owner!);

        void Persist(ViewerSettingsModel model)
        {
            model.ApplyTo(_settings);
            _settingsStore.Save(_settings);
        }
    }

    public async Task ShowSimilarSearchAsync(ImageEntry baseImage, IReadOnlyList<ImageEntry> collectionEntries)
    {
        if (Owner is null)
        {
            return;
        }

        var vm = new SimilarSearchViewModel(baseImage, collectionEntries, _similaritySearch, _localization, this);
        var window = new SimilarSearchWindow { DataContext = vm };
        await window.ShowDialog(Owner);
    }

    public async Task<bool> ShowMergeAsync(ImageEntry target, IReadOnlyList<ImageEntry> sources)
    {
        if (Owner is null)
        {
            return false;
        }

        // 統合後タグプレビューのためタグ名を解決する(マージ計算は MergeCalculator が純粋に行う)
        var tagById = (await _tags.GetAllAsync()).ToDictionary(t => t.Id, StringComparer.Ordinal);
        var vm = new MergeViewModel(target, sources, tagById, _mergeService, _localization);
        var window = new MergeDialog { DataContext = vm };
        vm.MergeCompleted += (_, _) => window.Close(true);
        return await window.ShowDialog<bool?>(Owner) == true;
    }

    public async Task ShowTrashAsync(string collectionId)
    {
        if (Owner is null)
        {
            return;
        }

        // V4: 復元/完全削除を有効化(TrashService 注入)。完全削除の確認は this(IWindowService)経由
        var vm = new TrashViewModel(collectionId, _images, _folders, _localization, _trashService, this);
        var window = new TrashView { DataContext = vm };
        await vm.LoadAsync();
        await window.ShowDialog(Owner);
    }

    public async Task ShowRepairAsync(string collectionId)
    {
        if (Owner is null)
        {
            return;
        }

        // 修復ライフサイクル UI(M-UI-REPAIR-027 / §2.11.5): criteria 検索+relink フロー
        var vm = new RepairViewModel(collectionId, _images, _criteriaSearch, _relink, _localization, this);
        var window = new RepairWindow { DataContext = vm };
        await vm.LoadAsync();
        await window.ShowDialog(Owner);
    }
}
