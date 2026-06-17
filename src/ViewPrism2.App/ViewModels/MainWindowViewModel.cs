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
            // 表示モードの復元(REQ-052 v1.3/CR-6)
            IsListMode = string.Equals(settings.DisplayMode, "list", StringComparison.Ordinal),
        };
        Detail = new DetailPanelViewModel(images, tags, thumbnails, localization);
        FolderPane = folderPane;
        TagsTab = tagsTab;
        Tagging = tagging;

        // M3: 画像タブ実 VM(モック準拠 surface)。注入済みリポジトリ/サービスを共有(ctor 不変)。
        ImageTab = new ImageTabViewModel(folders, images, tags, sorter, views, graphBuilder, pathConverter, evaluator, windows, settings);

        AllImagesItem = new ViewListItemViewModel(null, localization.T("view.allImages"));
        Browser.SelectionChanged += async (_, _) => await OnSelectionChangedAsync();
        Browser.OpenItemRequested += (_, item) => OpenViewer(item);
        Browser.PropertyChanged += (_, e) =>
        {
            // 中央ペインの表示合成(コレクション未選択の空状態 — REQ-053)を再評価する
            if (e.PropertyName is nameof(ImageBrowserViewModel.IsListMode) or nameof(ImageBrowserViewModel.IsEmpty))
            {
                NotifyContentPaneChanged();
            }
        };
        Detail.NotesSaved += (_, _) => StatusMessage = localization.T("success.saved");
        FolderPane.DataChanged += async (_, _) => await ReloadAsync();
        TagsTab.DataChanged += (_, _) => _imagesTabStale = true;
        Tagging.Applied += async (_, _) => await OnTaggingAppliedAsync();
        localization.CultureChanged += (_, _) =>
        {
            AllImagesItem.DisplayName = localization.T("view.allImages");

            // DF-3(K-AVALONIA の罠): コンパイル済みバインディングはインデクサ('Item[]')の
            // PropertyChanged では再評価されない。Loc 自体(名前付きプロパティ)を差し替えて
            // 全文言バインディングを確実に再評価させる
            Loc = new LocalizationProxy(localization);
            OnPropertyChanged(nameof(Loc));
        };
    }

    public LocalizationProxy Loc { get; private set; }

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

    /// <summary>
    /// 選択中コレクション(同期フォルダ)id(REQ-053 v1.3/CR-2: コレクション=選択スコープ)。
    /// null=未選択(中央に選択を促す空状態)。settings へ永続化・復元する(CR-5)。
    /// </summary>
    [ObservableProperty]
    private string? _selectedCollectionId;

    [ObservableProperty]
    private GraphNodeViewModel? _selectedTreeNode;

    [ObservableProperty]
    private ViewListItemViewModel? _selectedViewItem;

    /// <summary>非モーダル通知(REQ-031 警告等)。ステータスバーに表示する。</summary>
    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>「タグ編集」モード(REQ-046): 右パネルがタグ付与へ切替、ダブルクリックのビューア起動は無効。</summary>
    [ObservableProperty]
    private bool _isTagEditMode;

    // M3 dev scaffold(画像タブ製造): モック準拠の新 surface(ImageTabView)を実データ VM で重畳する
    // dev トグル。既定 ON。M3 完了(view 軸+原典撤去)時に本 scaffold(VM 側 3 メンバ+
    // MainWindow.axaml の重畳+トグル+原典 Grid)を撤去し、画像タブを ImageTabView 一本化する。
    public ImageTabViewModel ImageTab { get; }

    [ObservableProperty]
    private bool _showImageTabPreview = true;

    partial void OnShowImageTabPreviewChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowImageTabHarness));
        OnPropertyChanged(nameof(ShowImageTabLegacy));
    }

    public bool IsTagsTabSelected => SelectedTabIndex == 0;

    public bool IsImagesTabSelected => SelectedTabIndex == 1;

    /// <summary>画像タブ harness(M2 scaffold)を表示中か。</summary>
    public bool ShowImageTabHarness => IsImagesTabSelected && ShowImageTabPreview;

    /// <summary>従来(原典)画像タブ surface を表示中か(harness OFF 時のみ)。</summary>
    public bool ShowImageTabLegacy => IsImagesTabSelected && !ShowImageTabPreview;

    /// <summary>コレクション選択済みか(REQ-053: 未選択時は中央に選択を促す空状態)。</summary>
    public bool IsCollectionSelected => SelectedCollectionId is not null;

    /// <summary>グリッドペインの表示(コレクション選択済み かつ グリッドモード)。</summary>
    public bool ShowGridPane => IsCollectionSelected && !Browser.IsListMode;

    /// <summary>リストペインの表示(コレクション選択済み かつ リストモード)。</summary>
    public bool ShowListPane => IsCollectionSelected && Browser.IsListMode;

    /// <summary>画像 0 件の空状態メッセージ(コレクション選択済みのときのみ)。</summary>
    public bool ShowEmptyMessage => IsCollectionSelected && Browser.IsEmpty;

    /// <summary>コレクション未選択の空状態(選択を促す、REQ-053)。</summary>
    public bool ShowCollectionPrompt => !IsCollectionSelected;

    public View? CurrentView => SelectedViewItem?.View;

    /// <summary>
    /// 起動時初期化: フォルダ・ビュー一覧の読込+最後に選択したコレクション(REQ-052 v1.3/CR-5)と
    /// 最後に開いたビュー(REQ-052)を復元する(解決不能なら未選択/全画像)。
    /// </summary>
    public async Task InitializeAsync()
    {
        await FolderPane.LoadAsync();
        SelectedCollectionId = _settings.LastCollectionId;
        ValidateSelectedCollection();
        await ReloadViewListsAsync();

        var last = _settings.LastViewId is { } lastId
            ? FindViewItem(lastId)
            : null;
        await SelectViewItemAsync(last ?? AllImagesItem);

        // M3: 画像タブ実 VM(モック準拠 surface)の初期ロード(コレクション+FS 軸)。
        await ImageTab.InitializeAsync(SelectedCollectionId);
    }

    /// <summary>全データ再読込(スキャン・タグ変更後)。選択中のビュー/ノードを可能なら復元する。</summary>
    public async Task ReloadAsync()
    {
        var currentViewId = CurrentView?.Id;
        var nodeId = SelectedTreeNode?.Node.HierarchyNodeId;
        var nodeValue = SelectedTreeNode?.Node.Value;

        ValidateSelectedCollection();
        await ReloadViewListsAsync();
        var item = currentViewId is null ? AllImagesItem : FindViewItem(currentViewId) ?? AllImagesItem;
        await SelectViewItemAsync(item, nodeId, nodeValue);
    }

    /// <summary>
    /// コレクション選択(REQ-053 v1.3/CR-2): 一覧・「全画像」入口・NodeGraph 評価の母集合を
    /// 当該コレクションの normal 画像へ切り替える。選択中のビュー/ノードは可能なら維持する。
    /// </summary>
    [RelayCommand]
    private async Task SelectCollectionAsync(FolderRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (string.Equals(row.Folder.Id, SelectedCollectionId, StringComparison.Ordinal))
        {
            return;
        }

        SelectedCollectionId = row.Folder.Id;

        var nodeId = SelectedTreeNode?.Node.HierarchyNodeId;
        var nodeValue = SelectedTreeNode?.Node.Value;
        var item = CurrentView is null ? AllImagesItem : FindViewItem(CurrentView.Id) ?? AllImagesItem;
        await SelectViewItemAsync(item, nodeId, nodeValue);
    }

    partial void OnSelectedCollectionIdChanged(string? value)
    {
        _settings.LastCollectionId = value; // 永続化対象(CR-5。書き出しは CaptureSettings)
        SyncCollectionRowSelection();
        OnPropertyChanged(nameof(IsCollectionSelected));
        NotifyContentPaneChanged();
    }

    /// <summary>選択中コレクションが一覧から消えた(削除等)場合は未選択へ戻す。</summary>
    private void ValidateSelectedCollection()
    {
        if (SelectedCollectionId is { } id &&
            FolderPane.Folders.All(r => !string.Equals(r.Folder.Id, id, StringComparison.Ordinal)))
        {
            SelectedCollectionId = null;
        }
        else
        {
            SyncCollectionRowSelection(); // 行再生成後の選択表示の同期
        }
    }

    private void SyncCollectionRowSelection()
    {
        foreach (var row in FolderPane.Folders)
        {
            row.IsSelected = string.Equals(row.Folder.Id, SelectedCollectionId, StringComparison.Ordinal);
        }
    }

    private void NotifyContentPaneChanged()
    {
        OnPropertyChanged(nameof(ShowGridPane));
        OnPropertyChanged(nameof(ShowListPane));
        OnPropertyChanged(nameof(ShowEmptyMessage));
        OnPropertyChanged(nameof(ShowCollectionPrompt));
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

    /// <summary>
    /// 終了時の永続化(REQ-052 v1.3): 表示モード(CR-6)・最後に開いたビュー id・
    /// 最後に選択したコレクション id(CR-5)を設定へ書き戻す(グリッド列数キーは CR-1 で廃止)。
    /// </summary>
    public void CaptureSettings()
    {
        _settings.DisplayMode = Browser.IsListMode ? "list" : "grid";
        _settings.LastViewId = CurrentView?.Id;
        _settings.LastCollectionId = SelectedCollectionId;
        _settings.Locale = _localization.CurrentLocale;
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsTagsTabSelected));
        OnPropertyChanged(nameof(IsImagesTabSelected));
        OnPropertyChanged(nameof(ShowImageTabHarness));
        OnPropertyChanged(nameof(ShowImageTabLegacy));
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
        OnPropertyChanged(nameof(CanSearchSimilar));
        OnPropertyChanged(nameof(CanMerge));
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

    /// <summary>選択中の画像 1 枚以上選択でき、最後に選択した 1 枚を基準に類似検索する(REQ-065)。</summary>
    public bool CanSearchSimilar => Browser.LastSelected is not null;

    /// <summary>マージは 2 枚以上選択(マージ先 1+マージ元 1 以上)で活性(REQ-067)。</summary>
    public bool CanMerge => Browser.Selection.Count >= 2;

    /// <summary>
    /// 類似画像検索(REQ-065、仕様 §2.10.4): 最後に選択した 1 枚を基準に、選択中コレクションの
    /// normal 画像を候補として類似検索 UI を開く。検索後はマージで構成が変わり得るため再読込する。
    /// </summary>
    [RelayCommand]
    private async Task SearchSimilarAsync()
    {
        if (Browser.LastSelected is not { } baseItem)
        {
            StatusMessage = _localization.T("similar.selectOne");
            return;
        }

        await _windows.ShowSimilarSearchAsync(baseItem.Entry, _entries);
        await ReloadAsync(); // マージで deleted 化した可能性 → グリッドを更新
    }

    /// <summary>
    /// 複数選択からマージ(REQ-067、仕様 §2.10.5): 最後に選択した 1 枚をマージ先、残りをマージ元とする。
    /// 2 枚未満では何もしない。マージ後はグリッドを再読込する。
    /// </summary>
    [RelayCommand]
    private async Task MergeSelectedAsync()
    {
        var selection = Browser.Selection.ToList();
        if (selection.Count < 2)
        {
            StatusMessage = _localization.T("merge.selectAtLeastTwo");
            return;
        }

        // マージ先=最後に選択した 1 枚、マージ元=それ以外(K-DESIGN v3.0: 役割を視覚区別)
        var target = selection[^1].Entry;
        var sources = selection.Take(selection.Count - 1).Select(i => i.Entry).ToList();

        var merged = await _windows.ShowMergeAsync(target, sources);
        if (merged)
        {
            StatusMessage = _localization.T("merge.completed");
            await ReloadAsync();
        }
    }

    /// <summary>トラッシュ表示(REQ-067): 選択中コレクションの deleted 一覧(閲覧のみ)。</summary>
    [RelayCommand]
    private async Task OpenTrashAsync()
    {
        if (SelectedCollectionId is not { } collectionId)
        {
            StatusMessage = _localization.T("collection.pleaseSelectCollection");
            return;
        }

        await _windows.ShowTrashAsync(collectionId);
        await ReloadAsync();
    }

    /// <summary>修復ライフサイクル UI(REQ-072): criteria 検索+relink フロー。終了後に一覧を更新する。</summary>
    [RelayCommand]
    private async Task OpenRepairAsync()
    {
        if (SelectedCollectionId is not { } collectionId)
        {
            StatusMessage = _localization.T("collection.pleaseSelectCollection");
            return;
        }

        await _windows.ShowRepairAsync(collectionId);
        await ReloadAsync();
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
            Browser.SetColumns(null, _tagById);
            EvaluateAndShow();
            return;
        }

        _viewConditions = await _views.GetConditionsAsync(view.Id);
        Browser.SetColumns(view.DisplayColumns, _tagById);
        Browser.SetSort(view.SortField, view.SortDirection);

        // NodeGraph は表示のたびに再構築(REQ-035)。値抽出の母集合は選択中コレクションの
        // normal 画像に限る(REQ-053 v1.3/CR-2: _entries はコレクションスコープ適用済み)
        var hierarchy = await _views.GetHierarchyAsync(view.Id);
        var valueIndex = TagValueIndex.Build(_entries.Select(e => e.ToImageWithTags()));

        var result = _graphBuilder.BuildGraph(hierarchy, _tagById, valueIndex);
        if (result.Warnings.Count > 0)
        {
            StatusMessage = result.Warnings[0].Message;
            _logger?.LogWarning("NodeGraph 警告: {Warnings}", string.Join(" / ", result.Warnings.Select(w => w.Message)));
        }

        _suppressEvaluation = true;
        TreeRoots.Clear();
        var rootVm = new GraphNodeViewModel(result.Root, null, view.Name);
        TreeRoots.Add(rootVm);

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

    /// <summary>
    /// 選択中コレクションの status=normal 画像+タグ付け状態を読み込む(INV-010、REQ-053 v1.3/CR-2)。
    /// コレクション未選択時は母集合を空にする。is_active=false のフォルダは対象外(REQ-010)。
    /// あわせてコレクション一覧の画像数表示(CR-8)を更新する。
    /// </summary>
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

        // コレクション項目の画像数(REQ-053/CR-8: normal 画像数 = 当該コレクションの表示母集合)
        var countByFolder = normals
            .GroupBy(r => r.SyncFolderId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        foreach (var row in FolderPane.Folders)
        {
            row.ImageCount = countByFolder.TryGetValue(row.Folder.Id, out var count) ? count : 0;
        }

        var entries = new List<ImageEntry>(normals.Count);
        foreach (var record in normals)
        {
            if (SelectedCollectionId is null ||
                !string.Equals(record.SyncFolderId, SelectedCollectionId, StringComparison.Ordinal))
            {
                continue; // REQ-053: 母集合は選択中コレクションのみ(横断表示なし)
            }

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
