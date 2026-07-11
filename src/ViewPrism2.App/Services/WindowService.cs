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
    private readonly ScanCoordinator _scans;
    private readonly RelinkService _relink;
    private readonly ImageMemoryCache _imageCache;
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
        ScanCoordinator scans,
        RelinkService relink,
        ImageMemoryCache imageCache,
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
        _scans = scans;
        _relink = relink;
        _imageCache = imageCache;
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

        var vm = new FolderManagementViewModel(_folders, _scans, _localization, this);
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

    public async Task ShowRelinkAsync(string folderId)
    {
        if (Owner is null)
        {
            return;
        }

        // _folders は候補/missing 行のサムネイル絶対パス解決用(DC-RELINK-001/ECO-004。RepairViewModel と同型)。
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

    public async Task ShowRepairAsync(string collectionId)
    {
        if (Owner is null)
        {
            return;
        }

        // GF-V4-02(原典 view-prism 準拠): 修復を開く前にコレクションをスキャンしてリンク切れを検出する。
        // 記録パスのファイル消失→missing 化 / リネーム後の新ファイル→pending 登録(=自動修復候補)。
        // これにより「再起動/コレクション展開で検出される」原典挙動に相当(明示再スキャン不要)。
        // ルート消失時の一括 missing 化は ScanService 側が抑止(INV-009/V1 F-3 保護)。
        await _scans.ScanAsync(collectionId, null, System.Threading.CancellationToken.None);

        // 修復ライフサイクル UI(M-UI-REPAIR-027 / §2.11.5): relink フロー(検索=候補絞り込みに統一)。
        // _folders は候補/missing のサムネイル絶対パス解決用(GF-V4-04)。
        var vm = new RepairViewModel(collectionId, _images, _folders, _relink, _trashService, _localization, this);
        var window = new RepairWindow { DataContext = vm };
        await vm.LoadAsync();
        await window.ShowDialog(Owner);
    }
}
