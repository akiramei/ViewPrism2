using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ViewPrism2.App.Services;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Repositories;
using ViewPrism2.Core.Services;

namespace ViewPrism2.App.ViewModels;

/// <summary>
/// シェルのルート ViewModel(M-UI-013 v1.2、E-UI-SHELL-021)。
/// 上部タブナビゲーション「タグ」「画像」+右端「設定」(仕様 §2.6 v1.2)。
/// 画像タブ: 左=同期フォルダ+ビュー(お気に入り/最近)+NodeGraph+「全画像」固定入口 /
/// ツールバー(表示切替・列数・ソート・タグ編集モード)/ 中央=グリッド⇔リスト /
/// 右=詳細パネル⇔タグ付与パネル切替。
/// ノード選択 → パス→条件変換(OC-3)→ 条件評価(OC-1)→ 整列(OC-4)は Core を呼ぶだけにする(K-MVVM)。
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly ISyncFolderRepository _folders;
    private readonly IImageRepository _images;
    private readonly ITagRepository _tags;
    private readonly ViewService _views;
    private readonly NodeGraphBuilder _graphBuilder;
    private readonly PathConditionConverter _pathConverter;
    private readonly ConditionEvaluator _evaluator;
    private readonly LocalizationService _localization;
    private readonly AppSettings _settings;
    private readonly IWindowService _windows;
    private readonly ILogger<MainWindowViewModel>? _logger;

    private List<ImageEntry> _entries = [];
    private Dictionary<string, Tag> _tagById = new(StringComparer.Ordinal);
    private IReadOnlyList<ViewCondition> _viewConditions = [];
    private bool _suppressEvaluation;
    private bool _imagesTabStale;

    public MainWindowViewModel(
        ISyncFolderRepository folders,
        IImageRepository images,
        ITagRepository tags,
        ViewService views,
        NodeGraphBuilder graphBuilder,
        PathConditionConverter pathConverter,
        ConditionEvaluator evaluator,
        ImageSorter sorter,
        Infrastructure.Imaging.ThumbnailService thumbnails,
        LocalizationService localization,
        AppSettings settings,
        IWindowService windows,
        FolderManagementViewModel folderPane,
        TagsTabViewModel tagsTab,
        TaggingPanelViewModel tagging,
        ILogger<MainWindowViewModel>? logger = null)
    {
        _folders = folders;
        _images = images;
        _tags = tags;
        _views = views;
        _graphBuilder = graphBuilder;
        _pathConverter = pathConverter;
        _evaluator = evaluator;
        _localization = localization;
        _settings = settings;
        _windows = windows;
        _logger = logger;

        Loc = new LocalizationProxy(localization);
        Browser = new ImageBrowserViewModel(localization, sorter)
        {
            GridColumns = Math.Clamp(settings.GridColumns, 3, 6),
        };
        Detail = new DetailPanelViewModel(images, tags, thumbnails, localization);
        FolderPane = folderPane;
        TagsTab = tagsTab;
        Tagging = tagging;

        AllImagesItem = new ViewListItemViewModel(null, localization.T("view.allImages"));
        Browser.SelectionChanged += async (_, _) => await OnSelectionChangedAsync();
        Browser.OpenItemRequested += (_, item) => OpenViewer(item);
        Detail.NotesSaved += (_, _) => StatusMessage = localization.T("success.saved");
        FolderPane.DataChanged += async (_, _) => await ReloadAsync();
        TagsTab.DataChanged += (_, _) => _imagesTabStale = true;
        Tagging.Applied += async (_, _) => await OnTaggingAppliedAsync();
        localization.CultureChanged += (_, _) => AllImagesItem.DisplayName = localization.T("view.allImages");
    }

    public LocalizationProxy Loc { get; }

    public ImageBrowserViewModel Browser { get; }

    public DetailPanelViewModel Detail { get; }

    /// <summary>画像タブ左の同期フォルダ(コレクション)ペイン(仕様 §2.6 v1.2)。</summary>
    public FolderManagementViewModel FolderPane { get; }

    /// <summary>タグタブ(3 ペイン)。</summary>
    public TagsTabViewModel TagsTab { get; }

    /// <summary>タグ付与パネル(M-UI-016。タグ編集モード時に右パネルへ表示)。</summary>
    public TaggingPanelViewModel Tagging { get; }

    public ViewListItemViewModel AllImagesItem { get; }

    /// <summary>お気に入り: is_favorite=true を name 昇順(REQ-033)。</summary>
    public ObservableCollection<ViewListItemViewModel> Favorites { get; } = [];

    /// <summary>最近: modified_at 降順 limit 10(REQ-033)。</summary>
    public ObservableCollection<ViewListItemViewModel> Recents { get; } = [];

    public ObservableCollection<GraphNodeViewModel> TreeRoots { get; } = [];

    /// <summary>選択中タブ(0=タグ / 1=画像)。初期は画像タブ。</summary>
    [ObservableProperty]
    private int _selectedTabIndex = 1;

    [ObservableProperty]
    private GraphNodeViewModel? _selectedTreeNode;

    [ObservableProperty]
    private ViewListItemViewModel? _selectedViewItem;

    /// <summary>非モーダル通知(REQ-031 警告等)。ステータスバーに表示する。</summary>
    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>選択中ビューに階層定義が無い(nodeGraph.empty 表示用)。全画像選択時は false。</summary>
    [ObservableProperty]
    private bool _isTreeEmpty;

    /// <summary>「タグ編集」モード(REQ-046): 右パネルがタグ付与へ切替、ダブルクリックのビューア起動は無効。</summary>
    [ObservableProperty]
    private bool _isTagEditMode;

    public bool IsTagsTabSelected => SelectedTabIndex == 0;

    public bool IsImagesTabSelected => SelectedTabIndex == 1;

    public View? CurrentView => SelectedViewItem?.View;

    /// <summary>起動時初期化: フォルダ・ビュー一覧の読込+最後に開いたビュー(REQ-052)または全画像を選択。</summary>
    public async Task InitializeAsync()
    {
        await FolderPane.LoadAsync();
        await ReloadViewListsAsync();

        var last = _settings.LastViewId is { } lastId
            ? FindViewItem(lastId)
            : null;
        await SelectViewItemAsync(last ?? AllImagesItem);
    }

    /// <summary>全データ再読込(スキャン・タグ変更後)。選択中のビュー/ノードを可能なら復元する。</summary>
    public async Task ReloadAsync()
    {
        var currentViewId = CurrentView?.Id;
        var nodeId = SelectedTreeNode?.Node.HierarchyNodeId;
        var nodeValue = SelectedTreeNode?.Node.Value;

        await ReloadViewListsAsync();
        var item = currentViewId is null ? AllImagesItem : FindViewItem(currentViewId) ?? AllImagesItem;
        await SelectViewItemAsync(item, nodeId, nodeValue);
    }

    [RelayCommand]
    private Task Refresh() => ReloadAsync();

    [RelayCommand]
    private void ShowTagsTab() => SelectedTabIndex = 0;

    [RelayCommand]
    private void ShowImagesTab() => SelectedTabIndex = 1;

    [RelayCommand]
    private async Task SelectViewListItem(ViewListItemViewModel item) => await SelectViewItemAsync(item);

    [RelayCommand]
    private async Task OpenFolderManagement()
    {
        await _windows.ShowFolderManagementAsync();
        await FolderPane.LoadAsync();
        await ReloadAsync();
    }

    [RelayCommand]
    private async Task OpenSettings()
    {
        await _windows.ShowSettingsAsync();
    }

    /// <summary>終了時の永続化(REQ-052): グリッド列数・最後に開いたビュー id を設定へ書き戻す。</summary>
    public void CaptureSettings()
    {
        _settings.GridColumns = Browser.GridColumns;
        _settings.LastViewId = CurrentView?.Id;
        _settings.Locale = _localization.CurrentLocale;
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsTagsTabSelected));
        OnPropertyChanged(nameof(IsImagesTabSelected));
        if (value == 0)
        {
            _ = TagsTab.EnsureLoadedAsync();
        }
        else if (_imagesTabStale)
        {
            // タグタブでの永続変更(タグ・ビュー・階層)を画像タブへ反映
            _imagesTabStale = false;
            _ = ReloadAsync();
        }
    }

    partial void OnIsTagEditModeChanged(bool value)
    {
        // タグ編集モード中はダブルクリックのビューア起動無効(REQ-041 v1.2)
        Browser.SuppressOpenItem = value;
        if (value)
        {
            SyncTaggingSelection();
        }
    }

    partial void OnSelectedTreeNodeChanged(GraphNodeViewModel? value)
    {
        if (!_suppressEvaluation)
        {
            EvaluateAndShow();
        }
    }

    private async Task OnSelectionChangedAsync()
    {
        await Detail.SetEntryAsync(Browser.LastSelected?.Entry, _tagById);
        if (IsTagEditMode)
        {
            SyncTaggingSelection();
        }
    }

    /// <summary>タグ付与パネルへ選択(選択順)を供給する(REQ-046 / FMEA-014: 連番は選択順)。</summary>
    private void SyncTaggingSelection()
    {
        Tagging.SetSelection(Browser.Selection.Select(i => i.Entry).ToList());
    }

    /// <summary>タグ付与適用後: 基礎データを読み直し、選択を選択順のまま復元する。</summary>
    private async Task OnTaggingAppliedAsync()
    {
        var selectedIds = Browser.Selection.Select(i => i.Record.Id).ToList();
        var nodeId = SelectedTreeNode?.Node.HierarchyNodeId;
        var nodeValue = SelectedTreeNode?.Node.Value;

        var item = CurrentView is null ? AllImagesItem : FindViewItem(CurrentView.Id) ?? AllImagesItem;
        await SelectViewItemAsync(item, nodeId, nodeValue);
        Browser.RestoreSelection(selectedIds);
    }

    private void OpenViewer(ImageItemViewModel item)
    {
        // 並びは呼び出し元一覧の整列結果(REQ-044)
        var ordered = Browser.SortedItems.Select(i => i.Entry).ToList();
        var index = Browser.SortedItems.ToList().FindIndex(i => ReferenceEquals(i, item));
        _windows.ShowViewer(ordered, Math.Max(0, index));
    }

    private async Task ReloadViewListsAsync()
    {
        var favorites = await _views.GetFavoritesAsync();
        var recents = await _views.GetRecentAsync();

        Favorites.Clear();
        foreach (var view in favorites)
        {
            Favorites.Add(new ViewListItemViewModel(view, view.Name));
        }

        Recents.Clear();
        foreach (var view in recents)
        {
            Recents.Add(new ViewListItemViewModel(view, view.Name));
        }
    }

    private ViewListItemViewModel? FindViewItem(string viewId)
    {
        return Favorites.Concat(Recents).FirstOrDefault(
            i => string.Equals(i.View?.Id, viewId, StringComparison.Ordinal));
    }

    private async Task SelectViewItemAsync(
        ViewListItemViewModel item, string? restoreNodeId = null, string? restoreNodeValue = null)
    {
        foreach (var other in Favorites.Concat(Recents).Append(AllImagesItem))
        {
            other.IsSelected = false;
        }

        // 同名ビューが両リストに現れるため、id 一致は全て選択表示にする
        foreach (var same in Favorites.Concat(Recents).Append(AllImagesItem).Where(
            i => ReferenceEquals(i, item) ||
                 (item.View is not null && string.Equals(i.View?.Id, item.View.Id, StringComparison.Ordinal))))
        {
            same.IsSelected = true;
        }

        SelectedViewItem = item;

        await LoadBaseDataAsync();

        var view = item.View;
        if (view is null)
        {
            // 固定入口「全画像」: NodeGraph なし・無条件
            _viewConditions = [];
            _suppressEvaluation = true;
            TreeRoots.Clear();
            SelectedTreeNode = null;
            _suppressEvaluation = false;
            IsTreeEmpty = false;
            Browser.SetColumns(null, _tagById);
            EvaluateAndShow();
            return;
        }

        _viewConditions = await _views.GetConditionsAsync(view.Id);
        Browser.SetColumns(view.DisplayColumns, _tagById);
        Browser.SetSort(view.SortField, view.SortDirection);

        // NodeGraph は表示のたびに再構築(REQ-035)
        var hierarchy = await _views.GetHierarchyAsync(view.Id);
        var values = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var tagId in hierarchy.Select(n => n.TagId).Distinct(StringComparer.Ordinal))
        {
            values[tagId] = await _images.GetDistinctNormalTagValuesAsync(tagId);
        }

        var result = _graphBuilder.BuildGraph(hierarchy, _tagById, TagValueIndex.FromValues(values));
        if (result.Warnings.Count > 0)
        {
            StatusMessage = result.Warnings[0].Message;
            _logger?.LogWarning("NodeGraph 警告: {Warnings}", string.Join(" / ", result.Warnings.Select(w => w.Message)));
        }

        _suppressEvaluation = true;
        TreeRoots.Clear();
        var rootVm = new GraphNodeViewModel(result.Root, null, view.Name);
        TreeRoots.Add(rootVm);
        IsTreeEmpty = rootVm.Children.Count == 0;

        // 選択復元: 旧選択ノードが解決できなければルートへ(M-BOM silence_sweep / REQ-037 と同じフォールバック)
        GraphNodeViewModel? target = null;
        if (restoreNodeId is not null)
        {
            target = rootVm.Find(restoreNodeId, restoreNodeValue);
        }

        if (target is null && view.HomeTagId is not null)
        {
            // ホームタグ(REQ-037): 解決できれば初期選択、不能ならルート(エラーにしない)
            var home = _graphBuilder.ResolveHome(result.Root, view.HomeTagId);
            if (home is not null)
            {
                target = rootVm.Find(home.HierarchyNodeId, home.Value);
            }
        }

        _suppressEvaluation = false;
        SelectedTreeNode = target ?? rootVm;
    }

    /// <summary>選択ノードまでのパス＋ビュー条件で評価し、グリッドへ反映する(REQ-031/036)。</summary>
    private void EvaluateAndShow()
    {
        var conditions = new List<ViewCondition>(_viewConditions);
        if (SelectedTreeNode is { } node)
        {
            conditions.AddRange(_pathConverter.BuildConditions(node.PathFromRoot));
        }

        IReadOnlyList<ImageEntry> matched;
        if (conditions.Count == 0)
        {
            matched = _entries;
        }
        else
        {
            var result = _evaluator.Evaluate(_entries.Select(e => e.ToImageWithTags()), conditions);
            if (result.Warnings.Count > 0)
            {
                // 警告は非モーダル通知(REQ-031): ステータスバー+ログ
                StatusMessage = result.Warnings[0].Message;
                _logger?.LogWarning("条件評価警告: {Warnings}", string.Join(" / ", result.Warnings.Select(w => w.Message)));
            }

            matched = _entries.Where(e => result.MatchedImageIds.Contains(e.Record.Id)).ToList();
        }

        Browser.SetImages(matched);
    }

    /// <summary>status=normal の全画像+タグ付け状態を読み込む(INV-010。is_active=false のフォルダは対象外)。</summary>
    private async Task LoadBaseDataAsync()
    {
        var folders = await _folders.GetAllAsync();
        var activeById = folders.Where(f => f.IsActive).ToDictionary(f => f.Id, StringComparer.Ordinal);

        var allTags = await _tags.GetAllAsync();
        _tagById = allTags.ToDictionary(t => t.Id, StringComparer.Ordinal);

        var imageTags = (await _tags.GetAllImageTagsAsync())
            .GroupBy(t => t.ImageId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var normals = await _images.GetAllNormalAsync();
        var entries = new List<ImageEntry>(normals.Count);
        foreach (var record in normals)
        {
            if (!activeById.TryGetValue(record.SyncFolderId, out var folder))
            {
                continue; // REQ-010: is_active=false は表示対象外
            }

            var absolute = Path.Combine(folder.Path, record.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            var assigned = imageTags.TryGetValue(record.Id, out var list) ? list : [];
            var evalTags = new List<EvalTagValue>(assigned.Count);
            foreach (var imageTag in assigned)
            {
                if (_tagById.TryGetValue(imageTag.TagId, out var tag))
                {
                    evalTags.Add(new EvalTagValue(tag.Id, tag.Type, imageTag.Value));
                }
            }

            entries.Add(new ImageEntry(record, absolute, evalTags));
        }

        _entries = entries;
        Tagging.UpdateTags(_tagById);
    }
}
