using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Threading;
using ViewPrism2.App.Services;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Repositories;
using ViewPrism2.Core.Services;
using ViewPrism2.Core.Services.Repair;
using ViewPrism2.Core.Services.Similarity;

namespace ViewPrism2.App.ViewModels;

/// <summary>
/// 作業タブ surface(ECO-020/ECO-α + ECO-021/ECO-β)。
/// 左=作業スペースサイドバー、中央=現スペースの画像をグリッド/リストで閲覧(ソート・タグドット・タグ絞り込みチップ)。
/// ECO-β: 文脈モードを段階的に追加(β-1=作業モード=選択して別スペースへ移動)。
/// 画像タブ(golden 済み)には触れず、Core サービス・末端 VM(ImageItemVM/ChipVM)・DS を再利用して
/// 作業タブ側でオーケストレートする(maintainer 裁定 UQ-W06=B・隔離方式)。ドメイン操作は WorkspaceService 経由。
/// </summary>
public sealed partial class WorkTabViewModel : ObservableObject
{
    private readonly WorkspaceService _workspaces;
    private readonly ISyncFolderRepository _folders;
    private readonly ITagRepository _tags;
    private readonly TagService _tagService;
    private readonly SimilaritySearchService _similar;
    private readonly MergeService _merge;
    private readonly TrashService _trash;
    private readonly IWindowService _windows;
    private readonly ImageSorter _sorter;
    private readonly AppSettings _settings;

    private readonly Dictionary<string, string> _folderPath = new(StringComparer.Ordinal);
    private Dictionary<string, Tag> _tagById = new(StringComparer.Ordinal);
    private Dictionary<string, List<ImageTag>> _imageTags = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<string>> _textSettings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, NumericTagSettings?> _numSettings = new(StringComparer.Ordinal);

    private string? _currentWorkspaceId;
    private string? _renameId;
    private string? _wsDeleteId;   // 削除確認モーダルの対象スペース
    private SortField _sortField = SortField.Name;
    private SortDirection _sortDir = SortDirection.Asc;
    private string _layout = "grid";
    private bool _initialized;

    // ECO-β: 作業モード(選択して別スペースへ移動)
    private bool _workMode;
    private readonly List<string> _selected = new();   // 選択順を保持(連番バッジ)
    private string? _tagFilter;                          // 現スペース内の絞り込み(単一)
    private List<ImageRecord> _sourceImages = new();     // 現スペースの全 normal 画像(絞り込み前=選択母集合の素)

    // ECO-β-2: タグ編集モード(作業と排他)
    private bool _editMode;
    private string _panelTab = "current";
    private string? _expandTag;

    // ECO-β-3: 整理モード(類似+マージ整理トレイ・E-UI-SIMILARITY-035 + E-UI-MERGE-036 再利用)
    private bool _organizeMode;
    private string? _mergeTargetId;
    private readonly List<string> _organizeTargets = new();
    // ECO-044(IMG-011 裁定③): 直近マージの操作ログ id と取り消し可否・不可理由
    private string? _undoOperationId;
    private bool _canUndo;
    private string? _undoNote;
    private string _searchMethod = "similar";
    private int _similarThreshold = 70; // 既定は仕様値 70(REQ-064/065・ECO-050 — 90 は転写ドリフトの逸脱だった)
    // ECO-055: 条件検索= CAD 意味論(マージ先との属性一致トグル 5 種)。自由入力 2 欄は撤去(裁定②a)
    private bool _condHash;
    private bool _condExt;
    private bool _condSize;
    private bool _condName;
    private bool _condDate;
    private bool _searching;
    private bool _hasSearched;
    private bool _searchOpen; // ECO-056(v2 3 ゾーン): 下部ピンの「似た画像を探す」折りたたみ状態
    private List<(string ImageId, int Score, bool IsCriteria)> _searchResults = new();
    private bool _organizeDone;
    private int _doneSourceCount;

    // ECO-β-4: 削除モード(4つ目の排他文脈モード・⋯から入る)+ ⋯メニュー + ゴミ箱 popup
    private bool _deleteMode;
    private int _trashCount;
    private readonly List<string> _trashSel = new();

    public WorkTabViewModel(
        WorkspaceService workspaces, ISyncFolderRepository folders, ITagRepository tags,
        SimilaritySearchService similar, MergeService merge, TrashService trash,
        IWindowService windows, ImageSorter sorter, AppSettings settings)
    {
        _workspaces = workspaces;
        _folders = folders;
        _tags = tags;
        _tagService = new TagService(tags);
        _similar = similar;
        _merge = merge;
        _trash = trash;
        _windows = windows;
        _sorter = sorter;
        _settings = settings;
    }

    public ObservableCollection<WorkspaceRowVM> Workspaces { get; } = new();
    public ObservableCollection<ImageItemVM> Items { get; } = new();
    public ObservableCollection<ChipVM> Chips { get; } = new();
    public ObservableCollection<CurrentTagVM> CurrentTags { get; } = new();
    public ObservableCollection<AddGroupVM> AddGroups { get; } = new();
    public ObservableCollection<OrganizeSlotVM> OrganizeTargets { get; } = new();
    public ObservableCollection<OrganizeResultVM> SearchResults { get; } = new();
    public ObservableCollection<TrashPopupItemVM> TrashPopupItems { get; } = new();

    // ---- サイドバー ----
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Expanded))]
    [NotifyPropertyChangedFor(nameof(SidebarWidth))]
    private bool _collapsed;
    public bool Expanded => !Collapsed;
    public double SidebarWidth => Collapsed ? 64 : 276;

    [ObservableProperty] private string _renameValue = string.Empty;

    // ---- 作業スペース削除 確認モーダル(タブ内中央オーバーレイ) ----
    [ObservableProperty] private bool _wsDeleteOpen;
    [ObservableProperty] private string _wsDeleteMessage = string.Empty;

    // ---- ワークスペースヘッダ ----
    [ObservableProperty] private string _wsName = string.Empty;
    [ObservableProperty] private bool _wsIsDefault;
    [ObservableProperty] private string _countLabel = "0 項目";

    // ---- 本体表示合成 ----
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowGrid))]
    [NotifyPropertyChangedFor(nameof(ShowList))]
    private bool _wsEmpty = true;
    public bool ShowGrid => !WsEmpty && _layout == "grid";
    public bool ShowList => !WsEmpty && _layout == "list";
    public bool IsGrid => _layout == "grid";
    public bool IsList => _layout == "list";

    // ---- チップ(絞り込み) ----
    [ObservableProperty] private bool _showChips;
    [ObservableProperty] private bool _showChipHint;
    [ObservableProperty] private string _chipHintLabel = "タグで絞り込み";

    // ---- 作業モード(β-1) ----
    public bool WorkMode => _workMode;
    public string WorkButtonLabel => _workMode ? "作業を終了" : "作業";
    public bool HasMoveSelection => _workMode && _selected.Count > 0;
    public int MoveSelCount => _selected.Count;
    [ObservableProperty] private bool _moveMenuOpen;
    public ObservableCollection<MoveTargetVM> MoveTargets { get; } = new();
    public bool NoMoveTargets => MoveTargets.Count == 0;

    // ---- 文脈モードの排他表示(ECO-014§8 規律を作業タブへ拡張) ----
    public bool InAnyMode => _editMode || _workMode || _organizeMode || _deleteMode;
    public bool ShowEditEntry => !_workMode && !_organizeMode && !_deleteMode;
    public bool ShowOrganizeEntry => !_editMode && !_workMode && !_deleteMode;
    public bool ShowWorkEntry => !_editMode && !_organizeMode && !_deleteMode;
    public bool ShowRightPane => _editMode || _organizeMode;
    public bool IsTagEditContext => _editMode;
    public bool IsOrganizeContext => _organizeMode;

    // ---- 削除モード + ⋯メニュー + ゴミ箱 popup(β-4) ----
    public bool DeleteMode => _deleteMode;
    public bool HasDeleteSelection => _deleteMode && _selected.Count > 0;
    public int DeleteSelCount => _selected.Count;
    public bool CanDeleteToTrash => HasDeleteSelection;
    [ObservableProperty] private bool _moreMenuOpen;
    public bool CanOpenMaintenance => _currentWorkspaceId is not null;
    public bool HasTrash => _trashCount > 0;            // ⋯ゴミ箱バッジ(現スペースの deleted 件数)
    public int TrashCount => _trashCount;
    // ゴミ箱ポップアップ(ECO-019 再利用)
    [ObservableProperty] private bool _trashOpen;
    public int TrashPopupCount => TrashPopupItems.Count;
    public bool HasTrashItems => TrashPopupItems.Count > 0;
    public bool TrashPopupEmpty => TrashPopupItems.Count == 0;
    public bool HasTrashSel => _trashSel.Count > 0;
    public int TrashSelCount => _trashSel.Count;
    public string TrashSelCountLabel => HasTrashSel ? $"{_trashSel.Count} 枚選択中" : "画像を選択して操作";
    public string TrashSelectAllLabel => (TrashPopupItems.Count > 0 && _trashSel.Count == TrashPopupItems.Count) ? "選択を解除" : "すべて選択";
    public bool CanRestoreTrash => _trashSel.Count > 0;
    public bool CanPurgeTrash => _trashSel.Count > 0;

    // ---- 整理モード(β-3) ----
    public bool OrganizeMode => _organizeMode;
    public string OrganizeButtonLabel => _organizeMode ? "整理を終了" : "整理";
    public bool HasMergeTarget => _mergeTargetId is not null;
    public OrganizeSlotVM? MergeTarget { get; private set; }
    public bool ShowMergeTargetPrompt => _organizeMode && _mergeTargetId is null;
    public bool HasOrganizeTargets => _organizeTargets.Count > 0;
    public bool ShowOrganizeTargetsPrompt => _organizeMode && _mergeTargetId is not null && _organizeTargets.Count == 0;
    public string OrganizeTargetsCountLabel => $"{_organizeTargets.Count} 枚";
    // タグ統合トグルは ECO-044(IMG-011 裁定②)で撤去 — タグ union は常時 ON(選択肢ではない)。
    public bool IsSimilarMethod => _searchMethod == "similar";
    public bool IsCriteriaMethod => _searchMethod == "criteria";
    public int SimilarThreshold
    {
        get => _similarThreshold;
        set { _similarThreshold = Math.Clamp(value, 50, 100); OnPropertyChanged(); OnPropertyChanged(nameof(SimilarThresholdLabel)); }
    }
    public string SimilarThresholdLabel => $"{_similarThreshold}%";
    // ECO-055: マージ先との属性一致トグル(順序はモック condDefs: hash/ext/size/name/date)
    public bool CondHash { get => _condHash; set { _condHash = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanRunSearch)); } }
    public bool CondExt { get => _condExt; set { _condExt = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanRunSearch)); } }
    public bool CondSize { get => _condSize; set { _condSize = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanRunSearch)); } }
    public bool CondName { get => _condName; set { _condName = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanRunSearch)); } }
    public bool CondDate { get => _condDate; set { _condDate = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanRunSearch)); } }
    private bool HasAnyCond => _condHash || _condExt || _condSize || _condName || _condDate;
    public bool Searching => _searching;
    public bool ShowSearchResults => _organizeMode && _hasSearched && !_organizeDone;
    public bool NoSearchResults => ShowSearchResults && SearchResults.Count == 0;
    /// <summary>検索実行可否(ECO-055 裁定③): 条件検索もマージ先必須+条件 1 つ以上。類似は従来どおり。</summary>
    public bool CanRunSearch => _mergeTargetId is not null && (!IsCriteriaMethod || HasAnyCond);
    public bool CanExecuteMerge => _mergeTargetId is not null && _organizeTargets.Count > 0 && !_organizeDone;
    // ECO-056(v2 モック): 実行可= 総数(対象+マージ先)→1枚 を明示。不可= 素の文言+理由注記(画像タブと同型)
    public string MergeButtonLabel => CanExecuteMerge ? $"マージを実行（{_organizeTargets.Count + 1}枚 → 1枚）" : "マージを実行";
    public bool ShowMergeBlockedNote => _organizeMode && !_organizeDone && !CanExecuteMerge;
    public string MergeBlockedNote => _mergeTargetId is null ? "宛先を選んでください" : "整理対象を1枚以上追加してください";
    /// <summary>下部ピンの「似た画像を探す」折りたたみ(ECO-056/v2 3 ゾーン)。</summary>
    public bool SearchOpen => _searchOpen;
    /// <summary>検索結果ヘッダ右端の方式ラベル(ECO-056/v2 モック searchMethodLabel)。</summary>
    public string SearchMethodLabel => _searchMethod == "similar" ? $"類似画像検索 · {_similarThreshold}% 以上" : "条件検索";
    public string SearchResultsSubLabel => $"マージ先「{MergeTarget?.Name}」に似た画像 · {SearchResults.Count} 件";
    public bool OrganizeDone => _organizeDone;
    public string DoneSummary => $"{_doneSourceCount + 1} 枚を 1 枚へまとめ、{_doneSourceCount} 枚を削除しました。";
    // ECO-044(IMG-011 裁定③): ログに基づく補償 Undo(画像タブと同型)
    public bool CanUndo => _canUndo;
    public string? UndoNote => _undoNote;
    public bool HasUndoNote => _undoNote is not null;
    // 中央ブラウズグリッド/リストは検索結果表示中は譲る
    public bool ShowBrowseGrid => ShowGrid && !ShowSearchResults;
    public bool ShowBrowseList => ShowList && !ShowSearchResults;

    // ---- タグ編集モード(β-2) ----
    public bool EditMode => _editMode;
    public string EditButtonLabel => _editMode ? "タグ編集を終了" : "タグ編集";
    public bool HasSelection => _selected.Count > 0;
    public bool PanelEmpty => _editMode && _selected.Count == 0;
    public bool PanelActive => _editMode && _selected.Count > 0;
    public string SelectionLabel => $"{_selected.Count} 枚選択中";
    public bool OnCurrentTab => _panelTab == "current";
    public bool OnAddTab => _panelTab == "add";
    public bool HasCurrentTags => CurrentTags.Count > 0;
    public bool NoCurrentTags => CurrentTags.Count == 0;
    public string CurrentNote { get; private set; } = "";
    public string NoCurrentLabel { get; private set; } = "";

    private string _addQuery = "";
    /// <summary>
    /// ECO-041: タグ追加の検索(画像タブと同一意味論= E-UI-TAGASSIGN-029 β-2 再利用)。
    /// trim・大文字小文字無視の部分一致(判定は BuildAddGroups)・入力即時反映。
    /// </summary>
    public string AddQuery
    {
        get => _addQuery;
        set
        {
            if (_addQuery == value) return;
            _addQuery = value;
            OnPropertyChanged();
            RebuildAddPanel(); // タグ追加パネルのみ部分再構築(Items 不変)
        }
    }

    // ---- ソート ----
    [ObservableProperty] private bool _sortMenuOpen;
    public string SortLabel => _sortField switch
    {
        SortField.Name => "名前",
        SortField.FileSize => "サイズ",
        SortField.ModifiedDate => "更新日",
        _ => "名前",
    };
    public bool SortNoneActive => false;
    public bool SortNameActive => _sortField == SortField.Name;
    public bool SortDateActive => _sortField == SortField.ModifiedDate;
    public bool SortSizeActive => _sortField == SortField.FileSize;
    public bool SortEnabled => true;
    public double SortArrowAngle => _sortDir == SortDirection.Desc ? 180 : 0;

    public async Task InitializeAsync()
    {
        var folders = await _folders.GetAllAsync().ConfigureAwait(true);
        _folderPath.Clear();
        foreach (var f in folders) _folderPath[f.Id] = f.Path;

        _tagById = (await _tags.GetAllAsync().ConfigureAwait(true)).ToDictionary(t => t.Id, StringComparer.Ordinal);

        // (ECO-039/FL-004=D-b) 専用キー優先。未保存(null)は画像タブの共通キーを初期値に読む(初回挙動不変)。
        _layout = string.Equals(_settings.WorkTabDisplayMode ?? _settings.DisplayMode, "list", StringComparison.Ordinal) ? "list" : "grid";
        await _workspaces.EnsureDefaultExistsAsync().ConfigureAwait(true);
        await ReloadWorkspacesAsync(preferDefault: true).ConfigureAwait(true);
        _initialized = true;
    }

    /// <summary>他タブで受け渡し(追加)が起きた後などに呼ぶ再読込(現スペース維持)。</summary>
    public async Task RefreshAsync()
    {
        if (!_initialized) { await InitializeAsync().ConfigureAwait(true); return; }
        // 起動後に追加されたコレクション/フォルダの画像も AbsolutePath を解決できるようフォルダマップを再読込する。
        var folders = await _folders.GetAllAsync().ConfigureAwait(true);
        _folderPath.Clear();
        foreach (var f in folders) _folderPath[f.Id] = f.Path;
        _tagById = (await _tags.GetAllAsync().ConfigureAwait(true)).ToDictionary(t => t.Id, StringComparer.Ordinal);
        await ReloadWorkspacesAsync(preferDefault: false).ConfigureAwait(true);
    }

    private async Task ReloadWorkspacesAsync(bool preferDefault)
    {
        var list = await _workspaces.ListAsync().ConfigureAwait(true);

        if (preferDefault || _currentWorkspaceId is null || list.All(w => w.Workspace.Id != _currentWorkspaceId))
        {
            _currentWorkspaceId =
                list.FirstOrDefault(w => w.Workspace.IsDefault)?.Workspace.Id
                ?? list.FirstOrDefault()?.Workspace.Id;
        }

        Workspaces.Clear();
        foreach (var w in list)
        {
            Workspaces.Add(new WorkspaceRowVM(
                w.Workspace.Id, w.Workspace.Name, w.Workspace.IsDefault,
                w.Workspace.IsDefault ? "自動追加先" : "保存済みスペース",
                w.NormalImageCount.ToString(), w.Workspace.Id == _currentWorkspaceId,
                editing: w.Workspace.Id == _renameId));
        }

        await LoadCurrentImagesAsync(list).ConfigureAwait(true);
        RebuildMoveTargets(list);
        await RefreshTrashCountAsync().ConfigureAwait(true); // ⋯ゴミ箱バッジ(現スペースの deleted 件数)
    }

    private async Task LoadCurrentImagesAsync(IReadOnlyList<WorkspaceWithCount> list)
    {
        var current = list.FirstOrDefault(w => w.Workspace.Id == _currentWorkspaceId);
        if (current is null || _currentWorkspaceId is null)
        {
            WsName = string.Empty; WsIsDefault = false; CountLabel = "0 項目";
            _sourceImages = new(); _imageTags = new(StringComparer.Ordinal);
            Recompute();
            return;
        }

        WsName = current.Workspace.Name;
        WsIsDefault = current.Workspace.IsDefault;

        _sourceImages = (await _workspaces.GetImagesAsync(_currentWorkspaceId).ConfigureAwait(true)).ToList();
        var all = await _tags.GetAllImageTagsAsync().ConfigureAwait(true);
        _imageTags = all.GroupBy(it => it.ImageId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        Recompute();
    }

    /// <summary>現スペース画像 → 絞り込み → ソート → Items + チップを再構築(membership 変化時)。</summary>
    private void Recompute()
    {
        // ---- チップ(現スペース内のタグ) ----
        Chips.Clear();
        ShowChips = false; ShowChipHint = false;
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var im in _sourceImages)
            foreach (var tid in ImgTagIds(im))
                counts[tid] = counts.GetValueOrDefault(tid) + 1;
        if (counts.Count > 0)
        {
            ShowChips = true; ShowChipHint = true; ChipHintLabel = "タグで絞り込み";
            Chips.Add(ChipVM.Neutral("クリア", _tagFilter is null));
            foreach (var (tid, count) in counts.OrderBy(c => c.Key, StringComparer.Ordinal))
            {
                if (!_tagById.TryGetValue(tid, out var def)) continue;
                Chips.Add(ChipVM.Colored(tid, def.Name, TagColor(def), count, _tagFilter == tid, isNav: false));
            }
        }

        // ---- 絞り込み + ソート ----
        var filtered = _tagFilter is null
            ? _sourceImages
            : _sourceImages.Where(im => ImgTagIds(im).Contains(_tagFilter)).ToList();
        var sorted = _sorter.Sort(filtered, _sortField, _sortDir);

        // ---- Items ----
        bool inSelect = _editMode || _workMode || _deleteMode; // 整理は選択でなくマージ先/整理対象の割当
        var selSet = new HashSet<string>(_selected);
        var orgSet = new HashSet<string>(_organizeTargets, StringComparer.Ordinal);
        Items.Clear();
        foreach (var r in sorted)
        {
            bool selected = selSet.Contains(r.Id);
            int? order = selected ? _selected.IndexOf(r.Id) + 1 : null;
            var tagsOf = ImgTagIds(r);
            var dots = tagsOf.Take(3)
                .Select(tid => (IBrush)Solid(TagColor(_tagById.GetValueOrDefault(tid)))).ToList();
            Items.Add(new ImageItemVM(r.Id, r.FileName, isFolder: false, isPlaceholder: false, hasThumb: false,
                thumbBrush: null, selectable: inSelect, isSelected: selected,
                hasTagDots: !inSelect && tagsOf.Count > 0, tagDots: dots,
                sizeLabel: FmtSize(r.FileSize), dateLabel: FmtDate(r.ModifiedDate),
                target: null, absolutePath: AbsolutePath(r), selectionOrder: order,
                isMergeTarget: _organizeMode && string.Equals(r.Id, _mergeTargetId, StringComparison.Ordinal),
                isOrganizeTarget: _organizeMode && orgSet.Contains(r.Id)));
        }

        CountLabel = $"{Items.Count} 項目";
        WsEmpty = _sourceImages.Count == 0;
        BuildContextPanels(selSet);
        OnPropertyChanged(nameof(ShowGrid));
        OnPropertyChanged(nameof(ShowList));
        OnPropertyChanged(nameof(ShowBrowseGrid));
        OnPropertyChanged(nameof(ShowBrowseList));
        OnPropertyChanged(nameof(ShowSearchResults));
        OnPropertyChanged(string.Empty);
    }

    /// <summary>選択マーカーのみその場更新(Items を作り直さない=クリック応答性。ECO-020 perf 規律)。</summary>
    private void RefreshSelectionMarkers()
    {
        var selSet = new HashSet<string>(_selected);
        var orgSet = new HashSet<string>(_organizeTargets, StringComparer.Ordinal);
        foreach (var item in Items)
        {
            bool selected = selSet.Contains(item.Id);
            int? order = selected ? _selected.IndexOf(item.Id) + 1 : null;
            bool merge = _organizeMode && string.Equals(item.Id, _mergeTargetId, StringComparison.Ordinal);
            bool org = _organizeMode && orgSet.Contains(item.Id);
            item.SetSelectionMarkers(selected, order, merge, org);
        }
        BuildContextPanels(selSet); // タグ編集パネル/整理トレイ(選択・整理依存)をその場更新
        OnPropertyChanged(nameof(HasMoveSelection));
        OnPropertyChanged(nameof(MoveSelCount));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(PanelEmpty));
        OnPropertyChanged(nameof(PanelActive));
        OnPropertyChanged(nameof(SelectionLabel));
        OnPropertyChanged(nameof(HasDeleteSelection));
        OnPropertyChanged(nameof(DeleteSelCount));
        OnPropertyChanged(nameof(CanDeleteToTrash));
    }

    /// <summary>選択依存パネル(タグ編集=現在のタグ+タグ追加)を再構築(ECO-β-2)。</summary>
    private void BuildContextPanels(HashSet<string> selSet)
    {
        var selected = _sourceImages.Where(r => selSet.Contains(r.Id)).ToList();
        CurrentTags.Clear();
        if (selected.Count > 0)
        {
            var first = ImgTagIds(selected[0]);
            var common = first.Where(t => selected.All(r => ImgTagIds(r).Contains(t)));
            foreach (var tid in common)
            {
                if (!_tagById.TryGetValue(tid, out var d)) continue;
                var col = TagColor(d);
                CurrentTags.Add(new CurrentTagVM(tid, d.Name, HexA(col, 1),
                    HexA(col, 0.12), HexA(col, 0.28), HexA(col, 1)));
            }
        }
        CurrentNote = selected.Count > 1 ? "選択画像に共通するタグ" : "この画像に付いているタグ";
        NoCurrentLabel = selected.Count > 1 ? "共通のタグはありません。" : "まだタグがありません。";

        BuildAddGroups(selected);

        // ---- 整理トレイ(β-3) ----
        MergeTarget = _mergeTargetId is not null ? SlotFor(_mergeTargetId) : null;
        OrganizeTargets.Clear();
        foreach (var id in _organizeTargets)
        {
            var slot = SlotFor(id);
            if (slot is not null) OrganizeTargets.Add(slot);
        }
        SearchResults.Clear();
        var inTray = new HashSet<string>(_organizeTargets, StringComparer.Ordinal);
        foreach (var (id, score, isCrit) in _searchResults)
        {
            var r = _sourceImages.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.Ordinal));
            if (r is null) continue; // マージ後に deleted 化した候補等は除外
            bool added = inTray.Contains(id) || id == _mergeTargetId;
            SearchResults.Add(new OrganizeResultVM(id, r.FileName, AbsolutePath(r), FmtSize(r.FileSize), score, isCrit, added));
        }

        OnPropertyChanged(nameof(HasCurrentTags));
        OnPropertyChanged(nameof(NoCurrentTags));
        OnPropertyChanged(nameof(CurrentNote));
        OnPropertyChanged(nameof(NoCurrentLabel));
        OnPropertyChanged(nameof(HasMergeTarget));
        OnPropertyChanged(nameof(MergeTarget));
        OnPropertyChanged(nameof(ShowMergeTargetPrompt));
        OnPropertyChanged(nameof(HasOrganizeTargets));
        OnPropertyChanged(nameof(ShowOrganizeTargetsPrompt));
        OnPropertyChanged(nameof(OrganizeTargetsCountLabel));
        OnPropertyChanged(nameof(CanExecuteMerge));
        OnPropertyChanged(nameof(MergeButtonLabel));
        OnPropertyChanged(nameof(NoSearchResults));
        OnPropertyChanged(nameof(CanRunSearch));
    }

    private void BuildAddGroups(List<ImageRecord> selected)
    {
        AddGroups.Clear();
        if (!_editMode || selected.Count == 0) return;

        var groups = new (TagType Type, string Label, string Hint, string Fg, string Bg)[]
        {
            (TagType.Simple, "シンプル", "タグ名のみ", "#5b6473", "#f0f2f6"),
            (TagType.Textual, "テキスト", "候補値から選ぶ", "#2459cf", "#eaf1fe"),
            (TagType.Numeric, "数値", "値を選ぶ", "#0f8a5e", "#eafaf3"),
        };
        string q = _addQuery.Trim().ToLowerInvariant(); // ECO-041: 検索(画像タブと同一意味論)
        foreach (var g in groups)
        {
            var rows = new List<AddRowVM>();
            foreach (var tag in _tagById.Values.Where(t => t.Type == g.Type)
                         .Where(t => q.Length == 0 || t.Name.ToLowerInvariant().Contains(q))
                         .OrderBy(t => t.Name, StringComparer.Ordinal))
            {
                bool added = g.Type == TagType.Simple && selected.All(r => ImgTagIds(r).Contains(tag.Id));
                bool expandable = g.Type != TagType.Simple;
                bool expanded = _expandTag == tag.Id;
                var col = TagColor(tag);
                var row = new AddRowVM(tag.Id, tag.Name, added, expandable,
                    g.Type == TagType.Simple && !added, expanded)
                {
                    NameBrush = added ? Solid("#aab1bd") : Solid("#1f2937"),
                    DotBrush = HexA(col, 1),
                    DotOpacity = added ? 0.4 : 1.0,
                    RowBackground = expanded ? HexA(col, 0.06) : (added ? Solid("#f9fafb") : Solid("#ffffff")),
                    RowBorderBrush = expanded ? HexA(col, 0.4) : Solid("#e8ebf0"),
                };
                if (expanded && g.Type == TagType.Textual)
                {
                    var values = _textSettings.GetValueOrDefault(tag.Id) ?? Array.Empty<string>();
                    foreach (var v in values)
                    {
                        bool setNow = selected.All(r => TagsOf(r.Id).Any(t => t.TagId == tag.Id && t.Value == v));
                        row.ValueChips.Add(new ValueChipVM(tag.Id, v, setNow,
                            setNow ? Solid(col) : HexA(col, 0.1),
                            HexA(col, setNow ? 1 : 0.28),
                            setNow ? Brushes.White : HexA(col, 1)));
                    }
                }
                if (expanded && g.Type == TagType.Numeric)
                {
                    var ns = _numSettings.GetValueOrDefault(tag.Id);
                    var cur = CommonNumeric(selected, tag.Id);
                    if (ns?.Min is { } min && ns.Max is { } max && max >= min && (max - min) <= 50)
                    {
                        double step = ns.Step is { } s && s > 0 ? s : 1;
                        row.NumRange = $"{min:0.##}–{max:0.##}";
                        row.NumCurrent = cur is not null ? $"★ {cur}" : "未設定";
                        for (double v = min; v <= max + 1e-9; v += step)
                        {
                            string label = v.ToString("0.##", CultureInfo.InvariantCulture);
                            bool on = cur == label;
                            row.NumCells.Add(new NumCellVM(tag.Id, (int)Math.Round(v),
                                on ? Solid(col) : HexA(col, 0.12),
                                HexA(col, on ? 1 : 0.3),
                                on ? Brushes.White : Solid("#9a7b1a"))
                            { ValueText = label });
                        }
                    }
                    else
                    {
                        row.NumRange = ns?.Min is { } mn && ns.Max is { } mx ? $"{mn:0.##}–{mx:0.##}" : "";
                        row.NumCurrent = cur is not null ? $"★ {cur}" : "未設定";
                    }
                }
                rows.Add(row);
            }
            if (rows.Count > 0)
                AddGroups.Add(new AddGroupVM(g.Label, g.Hint, Solid(g.Bg), Solid(g.Fg), rows));
        }
    }

    private List<ImageTag> TagsOf(string imageId)
        => _imageTags.GetValueOrDefault(imageId) ?? new List<ImageTag>();

    private string? CommonNumeric(List<ImageRecord> selected, string tagId)
    {
        if (selected.Count == 0) return null;
        var first = TagsOf(selected[0].Id).FirstOrDefault(t => t.TagId == tagId)?.Value;
        if (first is null) return null;
        return selected.All(r => TagsOf(r.Id).FirstOrDefault(t => t.TagId == tagId)?.Value == first) ? first : null;
    }

    private void RebuildMoveTargets(IReadOnlyList<WorkspaceWithCount> list)
    {
        MoveTargets.Clear();
        foreach (var w in list.Where(w => w.Workspace.Id != _currentWorkspaceId))
            MoveTargets.Add(new MoveTargetVM(w.Workspace.Id, w.Workspace.Name, w.NormalImageCount.ToString()));
        OnPropertyChanged(nameof(NoMoveTargets));
    }

    private IReadOnlyList<string> ImgTagIds(ImageRecord r)
        => _imageTags.TryGetValue(r.Id, out var its)
            ? its.Select(t => t.TagId).Distinct().ToList()
            : Array.Empty<string>();

    private string? AbsolutePath(ImageRecord r)
        => _folderPath.TryGetValue(r.SyncFolderId, out var root)
            ? Path.Combine(root, r.RelativePath.Replace('/', Path.DirectorySeparatorChar))
            : null;

    // ---------------- サイドバー コマンド ----------------
    [RelayCommand]
    private void ToggleSidebar() => Collapsed = !Collapsed;

    [RelayCommand]
    private async Task SelectWorkspace(string id)
    {
        if (string.IsNullOrEmpty(id) || id == _currentWorkspaceId) return;
        _currentWorkspaceId = id;
        _renameId = null; _tagFilter = null; _selected.Clear(); MoveMenuOpen = false;
        await ReloadWorkspacesAsync(preferDefault: false).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task AddWorkspace()
    {
        var fresh = await _workspaces.CreateRotatingDefaultAsync().ConfigureAwait(true);
        _currentWorkspaceId = fresh.Id;
        _renameId = null; _tagFilter = null; _selected.Clear(); MoveMenuOpen = false;
        await ReloadWorkspacesAsync(preferDefault: false).ConfigureAwait(true);
    }

    [RelayCommand]
    private void StartRename(string id)
    {
        var row = Workspaces.FirstOrDefault(w => w.Id == id);
        if (row is null || row.IsDefault) return;
        _renameId = id;
        RenameValue = row.Name;
        foreach (var w in Workspaces) w.IsEditing = w.Id == id;
    }

    [RelayCommand]
    private async Task CommitRename()
    {
        if (_renameId is null) return;
        var id = _renameId;
        _renameId = null;
        await _workspaces.RenameAsync(id, RenameValue).ConfigureAwait(true);
        await ReloadWorkspacesAsync(preferDefault: false).ConfigureAwait(true);
    }

    [RelayCommand]
    private void CancelRename()
    {
        _renameId = null;
        foreach (var w in Workspaces) w.IsEditing = false;
    }

    /// <summary>削除要求(デフォルト不可)。件数つきの確認文言を作りモーダルを開く(モック requestDelete 準拠)。</summary>
    [RelayCommand]
    private void RequestDeleteWorkspace(string id)
    {
        var row = Workspaces.FirstOrDefault(w => w.Id == id);
        if (row is null || row.IsDefault) return;
        _wsDeleteId = id;
        var n = int.TryParse(row.CountText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var c) ? c : 0;
        WsDeleteMessage = n > 0
            ? $"この作業スペースを削除します。中の {n} 枚はスペースから外れますが、画像自体は削除されません。この操作は元に戻せません。"
            : "この作業スペースを削除します。この操作は元に戻せません。";
        WsDeleteOpen = true;
    }

    /// <summary>削除確認のキャンセル。</summary>
    [RelayCommand]
    private void CancelDeleteWorkspace()
    {
        _wsDeleteId = null;
        WsDeleteOpen = false;
    }

    /// <summary>削除確定。現スペースを消した場合はデフォルトへフォールバックして再読込する。</summary>
    [RelayCommand]
    private async Task ConfirmDeleteWorkspace()
    {
        var id = _wsDeleteId;
        _wsDeleteId = null;
        WsDeleteOpen = false;
        if (string.IsNullOrEmpty(id)) return;

        var result = await _workspaces.DeleteAsync(id).ConfigureAwait(true);
        if (!result.IsSuccess) return; // デフォルト/不存在(UI 上は起こらない想定の防御)

        var deletingCurrent = id == _currentWorkspaceId;
        if (deletingCurrent)
        {
            _currentWorkspaceId = null;
            _renameId = null; _tagFilter = null; _selected.Clear(); MoveMenuOpen = false;
        }
        await ReloadWorkspacesAsync(preferDefault: deletingCurrent).ConfigureAwait(true);
    }

    // ---------------- 作業モード(β-1) ----------------
    /// <summary>作業モード開始/終了。タグ編集・整理と排他・選択クリア(モック toggleWork 準拠)。</summary>
    [RelayCommand]
    private void ToggleWork()
    {
        _workMode = !_workMode;
        if (_workMode) { _editMode = false; _organizeMode = false; ResetOrganizeState(); _deleteMode = false; _expandTag = null; }
        _selected.Clear(); MoveMenuOpen = false;
        Recompute();
        NotifyModeChanged();
    }

    /// <summary>グリッド/リストのクリック処理。整理=マージ先/整理対象の割当、タグ編集・作業=選択。</summary>
    public void HandleItemClick(ImageItemVM item, bool ctrl, bool shift)
    {
        if (_organizeMode)
        {
            if (_mergeTargetId is null) SetMergeTarget(item.Id);
            else ToggleOrganizeTarget(item.Id);
            return;
        }
        if (_editMode || _workMode || _deleteMode) ToggleSelect(item.Id, ctrl, shift);
    }

    private void NotifyModeChanged()
    {
        OnPropertyChanged(nameof(WorkMode));
        OnPropertyChanged(nameof(WorkButtonLabel));
        OnPropertyChanged(nameof(HasMoveSelection));
        OnPropertyChanged(nameof(MoveSelCount));
        OnPropertyChanged(nameof(EditMode));
        OnPropertyChanged(nameof(EditButtonLabel));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(PanelEmpty));
        OnPropertyChanged(nameof(PanelActive));
        OnPropertyChanged(nameof(ShowEditEntry));
        OnPropertyChanged(nameof(ShowWorkEntry));
        OnPropertyChanged(nameof(ShowOrganizeEntry));
        OnPropertyChanged(nameof(OnCurrentTab));
        OnPropertyChanged(nameof(OnAddTab));
        OnPropertyChanged(nameof(OrganizeMode));
        OnPropertyChanged(nameof(OrganizeButtonLabel));
        OnPropertyChanged(nameof(ShowRightPane));
        OnPropertyChanged(nameof(IsTagEditContext));
        OnPropertyChanged(nameof(IsOrganizeContext));
        OnPropertyChanged(nameof(ShowBrowseGrid));
        OnPropertyChanged(nameof(ShowBrowseList));
        OnPropertyChanged(nameof(ShowSearchResults));
        OnPropertyChanged(nameof(DeleteMode));
        OnPropertyChanged(nameof(HasDeleteSelection));
        OnPropertyChanged(nameof(DeleteSelCount));
        OnPropertyChanged(nameof(CanDeleteToTrash));
        OnPropertyChanged(nameof(InAnyMode));
    }

    // ---------------- タグ編集モード(β-2) ----------------
    /// <summary>タグ編集モード開始/終了。作業・整理と排他・選択クリア・タブを「現在のタグ」へ。</summary>
    [RelayCommand]
    private void ToggleEdit()
    {
        _editMode = !_editMode;
        if (_editMode) { _workMode = false; _organizeMode = false; ResetOrganizeState(); _deleteMode = false; MoveMenuOpen = false; }
        _selected.Clear(); _expandTag = null; _panelTab = "current";
        Recompute();
        NotifyModeChanged();
    }

    [RelayCommand]
    private void TabCurrent() { _panelTab = "current"; OnPropertyChanged(nameof(OnCurrentTab)); OnPropertyChanged(nameof(OnAddTab)); }

    [RelayCommand]
    private void TabAdd()
    {
        _panelTab = "add"; _expandTag = null;
        var selSet = new HashSet<string>(_selected);
        BuildContextPanels(selSet);
        OnPropertyChanged(nameof(OnCurrentTab));
        OnPropertyChanged(nameof(OnAddTab));
    }

    /// <summary>タグ追加行クリック: シンプル=即付与 / テキスト・数値=展開(設定ロード)。</summary>
    [RelayCommand]
    private async Task ClickAddRow(AddRowVM row)
    {
        if (row.Added) return;
        if (!row.Expandable) { await ApplyTagAsync(row.Id, null).ConfigureAwait(true); return; }
        if (_expandTag == row.Id) { _expandTag = null; RebuildAddPanel(); return; }
        _expandTag = row.Id;
        await EnsureSettingsAsync(row.Id).ConfigureAwait(true);
        RebuildAddPanel();
    }

    [RelayCommand]
    private async Task ApplyTextValue(ValueChipVM chip) => await ApplyTagAsync(chip.TagId, chip.Value).ConfigureAwait(true);

    [RelayCommand]
    private async Task ApplyRating(NumCellVM cell) => await ApplyTagAsync(cell.TagId, cell.Label).ConfigureAwait(true);

    [RelayCommand]
    private async Task RemoveCurrentTag(CurrentTagVM tag)
    {
        if (_selected.Count == 0) return;
        var result = await _tagService.UntagImagesAsync(_selected.ToList(), tag.Id).ConfigureAwait(true);
        if (result.IsSuccess) await ReloadTagsAsync().ConfigureAwait(true);
    }

    private async Task ApplyTagAsync(string tagId, string? value)
    {
        if (_selected.Count == 0) return;
        var result = await _tagService.TagImagesAsync(_selected.ToList(), tagId, value).ConfigureAwait(true);
        if (result.IsSuccess) await ReloadTagsAsync().ConfigureAwait(true);
    }

    private async Task EnsureSettingsAsync(string tagId)
    {
        if (!_tagById.TryGetValue(tagId, out var tag)) return;
        if (tag.Type == TagType.Textual && !_textSettings.ContainsKey(tagId))
        {
            var s = await _tags.GetTextualSettingsAsync(tagId).ConfigureAwait(true);
            _textSettings[tagId] = s?.PredefinedValues ?? Array.Empty<string>();
        }
        else if (tag.Type == TagType.Numeric && !_numSettings.ContainsKey(tagId))
        {
            _numSettings[tagId] = await _tags.GetNumericSettingsAsync(tagId).ConfigureAwait(true);
        }
    }

    /// <summary>タグ変更後の再読込(image_tags 再取得 → Items + パネル再構築)。</summary>
    private async Task ReloadTagsAsync()
    {
        var all = await _tags.GetAllImageTagsAsync().ConfigureAwait(true);
        _imageTags = all.GroupBy(it => it.ImageId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        Recompute();
    }

    /// <summary>タグ追加パネルのみ再構築(展開トグル時=選択不変)。</summary>
    private void RebuildAddPanel() => BuildAddGroups(_sourceImages.Where(r => _selected.Contains(r.Id)).ToList());

    // ---------------- 整理モード(β-3: 類似+マージ整理トレイ) ----------------
    /// <summary>整理モード開始/終了。タグ編集・作業と排他・トレイをリセット。</summary>
    [RelayCommand]
    private void ToggleOrganize()
    {
        _organizeMode = !_organizeMode;
        if (_organizeMode) { _editMode = false; _workMode = false; _deleteMode = false; MoveMenuOpen = false; }
        ResetOrganizeState();
        Recompute();
        NotifyModeChanged();
    }

    private void ResetOrganizeState()
    {
        _mergeTargetId = null;
        _organizeTargets.Clear();
        _searchMethod = "similar";
        _condHash = false; _condExt = false; _condSize = false; _condName = false; _condDate = false; // ECO-055
        _searching = false; _hasSearched = false;
        _searchOpen = false; // ECO-056: 検索パネルは畳んだ状態で開始(v2 モック direct シナリオ)
        _searchResults = new();
        _organizeDone = false; _doneSourceCount = 0;
        _undoOperationId = null; _canUndo = false; _undoNote = null; // ECO-044
        _selected.Clear();
    }

    /// <summary>グリッドで残したい1枚をマージ先にする(整理対象に入っていれば外す)。</summary>
    private void SetMergeTarget(string imageId)
    {
        if (!_organizeMode) return;
        _organizeTargets.Remove(imageId);
        _mergeTargetId = imageId;
        RefreshSelectionMarkers();
    }

    private void ToggleOrganizeTarget(string imageId)
    {
        if (!_organizeMode || _mergeTargetId is null || imageId == _mergeTargetId) return;
        if (!_organizeTargets.Remove(imageId)) _organizeTargets.Add(imageId);
        RefreshSelectionMarkers();
    }

    [RelayCommand]
    private void RemoveOrganizeTarget(string imageId)
    {
        if (_organizeTargets.Remove(imageId)) RefreshSelectionMarkers();
    }

    /// <summary>マージ先の解除(ECO-056/CAD v2・A-2 裁定=REQ-067): 整理対象は保持し、マージ先のみ未設定へ。
    /// 通知は一括(GF-055-01 教訓: 派生 CanExecuteMerge/CanRunSearch/バッジの取りこぼしを避ける)。</summary>
    [RelayCommand]
    private void ClearMergeTarget()
    {
        if (!_organizeMode) return;
        _mergeTargetId = null;
        RefreshSelectionMarkers(); // タイルの宛先マーカー+トレイ(BuildContextPanels)
        OnPropertyChanged(string.Empty);
    }

    /// <summary>整理対象をすべて外す(ECO-056/v2 モック「すべて解除」)。マージ先は保持。</summary>
    [RelayCommand]
    private void ClearOrganizeTargets()
    {
        if (!_organizeMode) return;
        _organizeTargets.Clear();
        RefreshSelectionMarkers();
        OnPropertyChanged(string.Empty);
    }

    /// <summary>検索結果からグリッドへ戻る(ECO-056/CAD backToGrid — v1 モック定義・51ad8ee から欠落)。
    /// 結果は保持(再検索まで不変=モック実測)・整理モードは維持。</summary>
    [RelayCommand]
    private void BackToGrid()
    {
        if (!_organizeMode) return;
        _hasSearched = false;
        OnPropertyChanged(string.Empty); // ShowSearchResults/ShowBrowseGrid/ShowBrowseList の切替
    }

    /// <summary>「似た画像を探す」パネルの開閉(ECO-056/v2 3 ゾーン: 下部ピン内の折りたたみ)。</summary>
    [RelayCommand]
    private void ToggleSearchOpen()
    {
        _searchOpen = !_searchOpen;
        OnPropertyChanged(string.Empty);
    }

    /// <summary>整理対象をマージ先へ昇格し、元のマージ先を整理対象へ戻す。</summary>
    [RelayCommand]
    private void PromoteToMergeTarget(string imageId)
    {
        if (!_organizeTargets.Remove(imageId)) return;
        if (_mergeTargetId is not null) _organizeTargets.Add(_mergeTargetId);
        _mergeTargetId = imageId;
        RefreshSelectionMarkers();
    }

    [RelayCommand]
    private void SetSearchMethod(string method)
    {
        _searchMethod = method == "criteria" ? "criteria" : "similar";
        OnPropertyChanged(nameof(IsSimilarMethod));
        OnPropertyChanged(nameof(IsCriteriaMethod));
        OnPropertyChanged(nameof(CanRunSearch));
    }

    /// <summary>
    /// 似た画像を探す: 類似(E-SIMSEARCH-032・マージ先基準)または条件(CriteriaMatcher)。
    /// 候補は現スペース内に限定(集めて吟味してマージのシナリオ)。結果を中央ペインへ。
    /// </summary>
    [RelayCommand]
    private async Task RunSearch()
    {
        if (!_organizeMode) return;
        _searching = true; OnPropertyChanged(nameof(Searching));
        var results = new List<(string ImageId, int Score, bool IsCriteria)>();
        var inWorkspace = new HashSet<string>(_sourceImages.Select(i => i.Id), StringComparer.Ordinal);
        try
        {
            if (_searchMethod == "criteria")
            {
                // ECO-055: マージ先(dest)との属性一致検索(CAD 意味論)。dest 必須(裁定③)・空条件非実行
                var dest = _mergeTargetId is null
                    ? null
                    : _sourceImages.FirstOrDefault(i => string.Equals(i.Id, _mergeTargetId, StringComparison.Ordinal));
                if (dest is not null && HasAnyCond)
                {
                    var criteria = OrganizeCriteria.FromMergeTarget(
                        dest, _condHash, _condExt, _condSize, _condName, _condDate);
                    var ids = CriteriaMatcher.Match(_sourceImages, criteria,
                        new HashSet<ImageStatus> { ImageStatus.Normal });
                    foreach (var id in ids)
                    {
                        if (string.Equals(id, _mergeTargetId, StringComparison.Ordinal)) continue;
                        results.Add((id, 100, true));
                    }
                }
            }
            else if (_mergeTargetId is not null) // 類似は基準(マージ先)が必要
            {
                var found = await _similar.FindSimilarAsync(_mergeTargetId, _similarThreshold).ConfigureAwait(true);
                foreach (var s in found)
                    if (inWorkspace.Contains(s.ImageId)) results.Add((s.ImageId, s.Score, false)); // 現スペース内に限定
            }
        }
        finally { _searching = false; }
        _searchResults = results;
        _hasSearched = true;
        Recompute();
    }

    /// <summary>検索結果の候補を整理対象へ追加する(マージ先が前提)。</summary>
    [RelayCommand]
    private void AddCandidateToTargets(string imageId)
    {
        if (_mergeTargetId is null || string.Equals(imageId, _mergeTargetId, StringComparison.Ordinal)) return;
        if (!_organizeTargets.Contains(imageId)) _organizeTargets.Add(imageId);
        Recompute();
    }

    /// <summary>マージ実行: E-MERGE-034(原子・タグ union・source=deleted・物理非破壊 INV-009)。完了後に再読込。</summary>
    [RelayCommand]
    private async Task ExecuteMerge()
    {
        if (_mergeTargetId is null || _organizeTargets.Count == 0) return;
        var target = _mergeTargetId;
        var sources = _organizeTargets.ToList();
        var result = await _merge.MergeAsync(target, sources).ConfigureAwait(true);
        if (!result.IsSuccess) return;
        _doneSourceCount = sources.Count;
        _organizeDone = true;
        _mergeTargetId = null;
        _organizeTargets.Clear();
        _hasSearched = false;
        _searchResults = new();
        // ECO-044: 直近マージの操作ログを「取り消す」の対象として保持(実行可能条件も初期評価)
        var op = await _merge.GetLatestOperationAsync(target).ConfigureAwait(true);
        _undoOperationId = op?.Id;
        _canUndo = op is not null && (await _merge.EvaluateUndoAsync(op.Id).ConfigureAwait(true)).IsSuccess;
        _undoNote = null;
        await ReloadWorkspacesAsync(preferDefault: false).ConfigureAwait(true); // source は deleted=現スペースから外れる
        NotifyModeChanged();
        OnPropertyChanged(nameof(OrganizeDone));
        OnPropertyChanged(nameof(DoneSummary));
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(UndoNote));
        OnPropertyChanged(nameof(HasUndoNote));
    }

    /// <summary>取り消す(ECO-044/IMG-011 裁定③): ログに基づく補償 Undo(画像タブと同型)。
    /// 条件破れは失敗理由を UndoNote に出しボタンを不活性化。成功時はトレイを畳んで再読込(sources が一覧へ戻る)。</summary>
    [RelayCommand]
    private async Task UndoMerge()
    {
        if (!_organizeDone || _undoOperationId is null) return;
        var result = await _merge.UndoMergeAsync(_undoOperationId).ConfigureAwait(true);
        if (!result.IsSuccess)
        {
            _canUndo = false;
            _undoNote = result.Message ?? "取り消しできません。";
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(UndoNote));
            OnPropertyChanged(nameof(HasUndoNote));
            return;
        }
        ResetOrganizeState();
        await ReloadWorkspacesAsync(preferDefault: false).ConfigureAwait(true); // sources が normal へ戻り一覧復帰
        Recompute();
        NotifyModeChanged();
        OnPropertyChanged(nameof(OrganizeDone));
    }

    /// <summary>別の整理を続ける: 完了状態を畳んでトレイをリセット(整理モードは維持)。</summary>
    [RelayCommand]
    private void ContinueOrganize()
    {
        ResetOrganizeState();
        Recompute();
        NotifyModeChanged();
        OnPropertyChanged(nameof(OrganizeDone));
    }

    private OrganizeSlotVM? SlotFor(string id)
    {
        var r = _sourceImages.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.Ordinal));
        return r is null ? null : new OrganizeSlotVM(id, r.FileName, AbsolutePath(r), FmtSize(r.FileSize));
    }

    // ---------------- ⋯メニュー + 削除モード + ゴミ箱 popup(β-4) ----------------
    // 注: 修復(criteria/relink)はコレクションスコープで作業スペースに対応しないため、作業タブ ⋯ は 削除/ゴミ箱 のみ。
    [RelayCommand]
    private void ToggleMoreMenu() { MoreMenuOpen = !MoreMenuOpen; SortMenuOpen = false; MoveMenuOpen = false; }

    /// <summary>⋯「削除」: 削除モードに入る(4つ目の排他文脈モード)。グリッド選択可→ゴミ箱へ移動。</summary>
    [RelayCommand]
    private void EnterDelete()
    {
        MoreMenuOpen = false;
        _deleteMode = true;
        _editMode = false; _workMode = false; _organizeMode = false; ResetOrganizeState(); MoveMenuOpen = false;
        _selected.Clear();
        Recompute();
        NotifyModeChanged();
    }

    [RelayCommand]
    private void ExitDelete()
    {
        _deleteMode = false;
        _selected.Clear();
        Recompute();
        NotifyModeChanged();
    }

    /// <summary>選択中の normal 画像をゴミ箱へ移動(normal→deleted ソフト削除・物理非破壊 INV-009・復元可)。</summary>
    [RelayCommand]
    private async Task DeleteToTrash()
    {
        if (_selected.Count == 0) return;
        foreach (var id in _selected.ToList())
            await _trash.DeleteToTrashAsync(id).ConfigureAwait(true); // 状態遷移は TrashService が担う
        _selected.Clear();
        await ReloadWorkspacesAsync(preferDefault: false).ConfigureAwait(true); // deleted は normal 一覧から外れる
        NotifyModeChanged();
    }

    /// <summary>⋯「ゴミ箱」: 現スペースの deleted 一覧を画像タブ内ポップアップで開く(ECO-019 再利用)。</summary>
    [RelayCommand]
    private async Task OpenTrash()
    {
        MoreMenuOpen = false;
        if (_currentWorkspaceId is null) return;
        await LoadTrashItemsAsync().ConfigureAwait(true);
        TrashOpen = true;
        NotifyTrash();
    }

    [RelayCommand]
    private void CloseTrash()
    {
        TrashOpen = false;
        _trashSel.Clear();
        NotifyTrash();
    }

    private async Task LoadTrashItemsAsync()
    {
        TrashPopupItems.Clear();
        _trashSel.Clear();
        if (_currentWorkspaceId is null) return;
        var deleted = await _workspaces.GetDeletedImagesAsync(_currentWorkspaceId).ConfigureAwait(true);
        foreach (var r in deleted)
            TrashPopupItems.Add(new TrashPopupItemVM(r.Id, r.FileName, AbsolutePath(r) ?? string.Empty, FmtSize(r.FileSize)));
    }

    [RelayCommand]
    private void ToggleTrashItem(TrashPopupItemVM item)
    {
        if (!_trashSel.Remove(item.Id)) _trashSel.Add(item.Id);
        RefreshTrashSelection();
    }

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
        NotifyTrash();
    }

    /// <summary>選択を復元(deleted→normal/不在 missing・T6/T7)。復元 normal は現スペースへ戻る。</summary>
    [RelayCommand]
    private async Task RestoreSelectedTrash()
    {
        if (_trashSel.Count == 0) return;
        foreach (var id in _trashSel.ToList())
            await _trash.RestoreAsync(id).ConfigureAwait(true);
        await LoadTrashItemsAsync().ConfigureAwait(true);
        await ReloadWorkspacesAsync(preferDefault: false).ConfigureAwait(true);
        NotifyTrash();
    }

    /// <summary>選択を完全削除(T8・CASCADE で workspace_images も消滅)。確認+INV-009 非破壊明示。</summary>
    [RelayCommand]
    private async Task PurgeSelectedTrash()
    {
        if (_trashSel.Count == 0) return;
        int n = _trashSel.Count;
        if (!await _windows.ConfirmAsync("完全削除",
                $"{n} 枚を完全に削除します。画像ファイルは削除されません(DB から除去)。この操作は元に戻せません。").ConfigureAwait(true))
            return;
        foreach (var id in _trashSel.ToList())
            await _trash.PermanentDeleteAsync(id).ConfigureAwait(true);
        await LoadTrashItemsAsync().ConfigureAwait(true);
        await RefreshTrashCountAsync().ConfigureAwait(true);
        NotifyTrash();
    }

    /// <summary>ゴミ箱を空にする(全 deleted を完全削除)。確認+INV-009 非破壊明示。</summary>
    [RelayCommand]
    private async Task EmptyTrash()
    {
        if (TrashPopupItems.Count == 0) return;
        int n = TrashPopupItems.Count;
        if (!await _windows.ConfirmAsync("ゴミ箱を空にする",
                $"ゴミ箱内の {n} 枚を完全に削除します。画像ファイルは削除されません(DB から除去)。この操作は元に戻せません。").ConfigureAwait(true))
            return;
        foreach (var id in TrashPopupItems.Select(i => i.Id).ToList())
            await _trash.PermanentDeleteAsync(id).ConfigureAwait(true);
        await LoadTrashItemsAsync().ConfigureAwait(true);
        await RefreshTrashCountAsync().ConfigureAwait(true);
        NotifyTrash();
    }

    private async Task RefreshTrashCountAsync()
    {
        _trashCount = _currentWorkspaceId is null
            ? 0
            : (await _workspaces.GetDeletedImagesAsync(_currentWorkspaceId).ConfigureAwait(true)).Count;
        OnPropertyChanged(nameof(HasTrash));
        OnPropertyChanged(nameof(TrashCount));
    }

    private void NotifyTrash()
    {
        OnPropertyChanged(nameof(TrashOpen));
        OnPropertyChanged(nameof(TrashPopupCount));
        OnPropertyChanged(nameof(HasTrashItems));
        OnPropertyChanged(nameof(TrashPopupEmpty));
        OnPropertyChanged(nameof(HasTrashSel));
        OnPropertyChanged(nameof(TrashSelCount));
        OnPropertyChanged(nameof(TrashSelCountLabel));
        OnPropertyChanged(nameof(TrashSelectAllLabel));
        OnPropertyChanged(nameof(CanRestoreTrash));
        OnPropertyChanged(nameof(CanPurgeTrash));
    }

    private void ToggleSelect(string id, bool ctrl, bool shift)
    {
        var list = Items.Select(i => i.Id).ToList(); // 表示順(絞り込み+ソート後)が選択母集合
        if (shift && _selected.Count > 0)
        {
            var last = _selected[^1];
            int a = list.IndexOf(last), b = list.IndexOf(id);
            if (a >= 0 && b >= 0)
            {
                int lo = Math.Min(a, b), hi = Math.Max(a, b);
                foreach (var rid in list.GetRange(lo, hi - lo + 1))
                    if (!_selected.Contains(rid)) _selected.Add(rid);
                RefreshSelectionMarkers();
                return;
            }
        }
        if (ctrl) { if (!_selected.Remove(id)) _selected.Add(id); RefreshSelectionMarkers(); return; }
        if (_selected.Count == 1 && _selected[0] == id) _selected.Clear();
        else { _selected.Clear(); _selected.Add(id); }
        RefreshSelectionMarkers();
    }

    [RelayCommand]
    private void ToggleMoveMenu()
    {
        if (_selected.Count == 0) return;
        MoveMenuOpen = !MoveMenuOpen;
    }

    /// <summary>選択画像を別スペースへ移動(ACT-0077・E-WORKSPACE-042.MoveImages・INV-W5)。</summary>
    [RelayCommand]
    private async Task MoveSelectedTo(string targetWorkspaceId)
    {
        if (_currentWorkspaceId is null || _selected.Count == 0) return;
        var ids = _selected.ToList();
        var result = await _workspaces.MoveImagesAsync(_currentWorkspaceId, targetWorkspaceId, ids).ConfigureAwait(true);
        if (!result.IsSuccess) return;
        _selected.Clear(); MoveMenuOpen = false;
        await ReloadWorkspacesAsync(preferDefault: false).ConfigureAwait(true); // membership 変化=作り直し
    }

    // ---------------- チップ(絞り込み) ----------------
    public void ClickChip(ChipVM chip)
    {
        _tagFilter = chip.Id == "__clear" ? null : (_tagFilter == chip.Id ? null : chip.Id);
        // 絞り込みで非表示になった選択は落とす(見えない画像を別スペース移動/ゴミ箱へ移動しない=安全)。
        if (_selected.Count > 0 && _tagFilter is not null)
        {
            var visible = new HashSet<string>(
                _sourceImages.Where(im => ImgTagIds(im).Contains(_tagFilter)).Select(i => i.Id), StringComparer.Ordinal);
            _selected.RemoveAll(id => !visible.Contains(id));
        }
        Recompute(); // 末尾の OnPropertyChanged(string.Empty) で選択依存(HasMoveSelection/CanDeleteToTrash 等)も更新
    }

    // ---------------- ソート / レイアウト ----------------
    [RelayCommand]
    private void ToggleSortMenu() => SortMenuOpen = !SortMenuOpen;

    [RelayCommand]
    private void SelectSort(string key)
    {
        _sortField = key switch
        {
            "name" => SortField.Name,
            "date" => SortField.ModifiedDate,
            "size" => SortField.FileSize,
            _ => SortField.Name,
        };
        SortMenuOpen = false;
        NotifySort();
        Recompute();
    }

    [RelayCommand]
    private void ToggleSortDir()
    {
        _sortDir = _sortDir == SortDirection.Asc ? SortDirection.Desc : SortDirection.Asc;
        NotifySort();
        Recompute();
    }

    // (ECO-039/FL-004=D-b) 切替は作業タブ専用キーへ保存(画像タブの DisplayMode とは連動しない)。
    [RelayCommand]
    private void SetGrid() { _layout = "grid"; _settings.WorkTabDisplayMode = "grid"; NotifyLayout(); }

    [RelayCommand]
    private void SetList() { _layout = "list"; _settings.WorkTabDisplayMode = "list"; NotifyLayout(); }

    /// <summary>Popup の light-dismiss から呼ぶメニュー閉じ。</summary>
    public void CloseMenusFromDismiss()
    {
        SortMenuOpen = false;
        MoveMenuOpen = false;
        MoreMenuOpen = false; // ⋯メニューも light-dismiss で false に戻す(二度押し防止)
    }

    private void NotifySort()
    {
        OnPropertyChanged(nameof(SortLabel));
        OnPropertyChanged(nameof(SortNameActive));
        OnPropertyChanged(nameof(SortDateActive));
        OnPropertyChanged(nameof(SortSizeActive));
        OnPropertyChanged(nameof(SortArrowAngle));
    }

    private void NotifyLayout()
    {
        // (ECO-038) XAML 本体は派生の ShowBrowseGrid/List を見る — 手書き通知リストは派生の追加に
        // 追随できず切替が本体へ届かなかった。画像タブ SetGrid/SetList(CR-6)と同型の全通知へ寄せる。
        OnPropertyChanged(string.Empty);
    }

    // ---------------- 色ヘルパ(画像タブと同流儀) ----------------
    private static Color Hex(string hex)
    {
        var h = hex.TrimStart('#');
        if (h.Length == 3) h = string.Concat(h.Select(c => $"{c}{c}"));
        var n = Convert.ToInt32(h, 16);
        return Color.FromRgb((byte)((n >> 16) & 255), (byte)((n >> 8) & 255), (byte)(n & 255));
    }
    private static IBrush Solid(string hex) => new SolidColorBrush(Hex(hex));
    private static IBrush HexA(string hex, double a)
    {
        var c = Hex(hex);
        return new SolidColorBrush(Color.FromArgb((byte)Math.Round(a * 255), c.R, c.G, c.B));
    }
    private static string TagColor(Tag? t) => t?.Color ?? "#5b6473";

    private static string FmtSize(long bytes)
    {
        double mb = bytes / 1024.0 / 1024.0;
        return mb >= 1 ? $"{mb:0.0} MB" : $"{Math.Round(bytes / 1024.0)} KB";
    }

    private static string FmtDate(string iso)
        => iso.Length >= 10 ? $"{iso[0..4]}/{iso[5..7]}/{iso[8..10]}" : iso;
}

/// <summary>作業スペース行(サイドバー・ECO-020)。選択・リネーム状態はその場更新する。</summary>
public sealed partial class WorkspaceRowVM : ObservableObject
{
    public WorkspaceRowVM(string id, string name, bool isDefault, string sub, string countText, bool isActive, bool editing)
    {
        Id = id; Name = name; IsDefault = isDefault; Sub = sub; CountText = countText;
        _isActive = isActive; _isEditing = editing;
    }

    public string Id { get; }
    public string Name { get; }
    public bool IsDefault { get; }
    public string Sub { get; }
    public string CountText { get; }
    public bool CanRename => !IsDefault;

    [ObservableProperty] private bool _isActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowName))]
    private bool _isEditing;
    public bool ShowName => !IsEditing;
}

/// <summary>別スペースへ移動の移動先候補(ECO-β)。</summary>
public sealed class MoveTargetVM
{
    public MoveTargetVM(string id, string name, string countText)
    { Id = id; Name = name; CountText = countText; }
    public string Id { get; }
    public string Name { get; }
    public string CountText { get; }
}
