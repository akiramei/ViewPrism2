using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ViewPrism2.App.Services;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Repositories;
using ViewPrism2.Core.Services;
using ViewPrism2.Core.Services.Repair;
using ViewPrism2.Core.Services.Similarity;

namespace ViewPrism2.App.ViewModels;

/// <summary>
/// 画像タブ実 VM(M3)。ImageTabView はシード VM(<see cref="ImageTabSeedViewModel"/>)と
/// 同一の公開契約(Collections/Crumbs/Chips/Items/AddGroups/CurrentTags + コマンド)を期待するため、
/// 本 VM も同じ面を実装し、データは Core リポジトリ/サービスから供給する。
///
/// M3a(本実装): FS フォルダ軸ブラウズ(relative_path 派生・新スキーマ不要)+ 実コレクション +
///   実サムネ(ImageItemVM.AbsolutePath→ThumbnailImage)+ ソート(ImageSorter)+ 選択 +
///   インライン付与(TagService: simple/textual/numeric)+ 連番は別アクション存置(UQ-I02b)。
/// M3b(次): タグビュー軸(ViewService→NodeGraphBuilder→ConditionEvaluator 消費)。
/// 固定オラクル S-01〜S-31・REQ-053 を退行させない(母集合=選択コレクションの status=normal)。
/// </summary>
public sealed partial class ImageTabViewModel : ObservableObject
{
    private readonly ISyncFolderRepository _folders;
    private readonly IImageRepository _images;
    private readonly ITagRepository _tags;
    private readonly ImageSorter _sorter;
    private readonly TagService _tagService;
    private readonly ViewService _views;
    private readonly NodeGraphBuilder _graphBuilder;
    private readonly PathConditionConverter _pathConverter;
    private readonly ConditionEvaluator _evaluator;
    private readonly SimilaritySearchService _similar;
    private readonly MergeService _merge;
    private readonly TrashService _trash;
    private readonly CriteriaSearchService _criteriaSearch;
    private readonly IWindowService _windows;
    private readonly AppSettings _settings;

    // ---- ロード済みデータ ----
    private List<SyncFolder> _collections = new();
    private readonly Dictionary<string, int> _collectionCounts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _collectionPath = new(StringComparer.Ordinal);
    private List<ImageRecord> _allNormal = new();
    private Dictionary<string, List<ImageTag>> _imageTags = new(StringComparer.Ordinal);
    private Dictionary<string, Tag> _tagById = new(StringComparer.Ordinal);
    private List<ImageEntry> _entries = new();
    private readonly Dictionary<string, IReadOnlyList<string>> _textSettings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, NumericTagSettings?> _numSettings = new(StringComparer.Ordinal);

    // ---- 状態 ----
    private string? _collectionId;
    private string _axis = "fs"; // fs | view
    private readonly List<string> _fsPath = new();
    // ---- view 軸(M3b): 保存ビューを閲覧軸として消費 ----
    private List<View> _allViews = new();
    private string? _viewId;
    private string _viewLabel = "タグビュー";
    private IReadOnlyList<ViewCondition> _viewConditions = Array.Empty<ViewCondition>();
    private GraphNode? _viewRoot;
    private readonly List<GraphNode> _viewPath = new();
    private readonly Dictionary<string, GraphNode> _currentChildren = new(StringComparer.Ordinal);
    private string _layout = "grid";
    private SortField _sortField = SortField.Name;
    private SortDirection _sortDir = SortDirection.Asc;
    private string? _tagFilter;
    private bool _editMode;
    private readonly List<string> _selected = new();
    private bool _collapsed;
    private string _panelTab = "current";
    private string? _expandTag;
    private bool _loaded;

    // ---- 整理モード(ECO-014: 類似+マージ統合「整理トレイ」)----
    // タグ編集モードと排他の文脈モード。マージ先(残す1枚)と整理対象(統合し削除対象)を選び、
    // 「似た画像を探す」(類似 E-SIMSEARCH-032 / 条件 E-CRITERIA-037)で候補を中央ペインに出し、
    // マージ実行(E-MERGE-034 原子・タグ union・source=deleted・物理非破壊 INV-009)で 1 枚へまとめる。
    private bool _organizeMode;
    private string? _mergeTargetId;
    private readonly List<string> _organizeTargets = new();
    private bool _includeTags = true;            // タグ統合(E-MERGE-034 は常に union=INV-011。OFF の no-union は IMG-011)
    private string _searchMethod = "similar";    // "similar" | "criteria"
    private int _similarThreshold = 80;
    private string _criteriaName = "";
    private string _criteriaExt = "";
    private bool _searching;
    private bool _hasSearched;
    private List<(string ImageId, int Score, bool IsCriteria)> _searchResults = new();
    private bool _organizeDone;
    private int _doneSourceCount;

    // ---- 作業モード(ECO-017: 作業対象セットの蓄積)----
    // タグ編集/整理に並ぶ3つ目の排他文脈モード。作業中はグリッドが選択可能になり(inSelect=編集 or 作業・
    // 既存の選択機構を再利用)、「追加」で選択を _workTargets へ和集合蓄積し選択をクリアする。
    // 右ペインは開かない(追加ボタン+作業対象チップはツールバー内)。workTargets はセッション内蓄積のみ
    // (消費先=作業タブ本体・永続化はスコープ外。モック明記)。
    private bool _workMode;
    private readonly List<string> _workTargets = new();

    // ---- 削除モード(ECO-018: ⋯メニュー「削除」=ゴミ箱へ移動)----
    // タグ編集/整理/作業に並ぶ排他文脈モード。⋯メニューの「削除」で入る(トグル入口は持たない)。
    // 削除中はグリッドが選択可能になり(inSelect)、「ゴミ箱へ移動」で選択を normal→deleted の
    // ソフト削除(物理非破壊 INV-009・復元可)へ。修復/ゴミ箱は既存モーダル(ECO-015)のまま。
    private bool _deleteMode;
    private int _trashCount; // 選択コレクションの deleted 件数(⋯「ゴミ箱」バッジ)

    // ---- ゴミ箱ポップアップ(ECO-019: トラッシュモーダルを画像タブ内ポップアップへ作り直す)----
    // ⋯「ゴミ箱」で開く中央オーバーレイ。deleted 画像を複数選択し、復元 / 完全削除 / ゴミ箱を空 を行う。
    // 完全削除は確認+INV-009 非破壊明示(画像ファイルは削除されない=DB 行のみ除去)。状態遷移は TrashService 経由。
    private readonly List<string> _trashSel = new();

    public ImageTabViewModel(
        ISyncFolderRepository folders,
        IImageRepository images,
        ITagRepository tags,
        ImageSorter sorter,
        ViewService views,
        NodeGraphBuilder graphBuilder,
        PathConditionConverter pathConverter,
        ConditionEvaluator evaluator,
        SimilaritySearchService similar,
        MergeService merge,
        TrashService trash,
        IWindowService windows,
        AppSettings settings)
    {
        _folders = folders;
        _images = images;
        _tags = tags;
        _sorter = sorter;
        _tagService = new TagService(tags);
        _views = views;
        _graphBuilder = graphBuilder;
        _pathConverter = pathConverter;
        _evaluator = evaluator;
        _similar = similar;
        _merge = merge;
        _trash = trash;
        _criteriaSearch = new CriteriaSearchService(images); // 整理トレイの条件検索(E-CRITERIA-037)。images のみ依存
        _windows = windows;
        _settings = settings;
    }

    // ---------------- 色ヘルパ ----------------
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

    // =====================================================================
    //  ロード
    // =====================================================================
    public async Task InitializeAsync(string? preferredCollectionId = null)
    {
        _collections = (await _folders.GetAllAsync().ConfigureAwait(true)).ToList();
        _collectionPath.Clear();
        foreach (var c in _collections) _collectionPath[c.Id] = c.Path;

        _allNormal = (await _images.GetAllNormalAsync().ConfigureAwait(true)).ToList();
        _collectionCounts.Clear();
        foreach (var g in _allNormal.GroupBy(r => r.SyncFolderId, StringComparer.Ordinal))
            _collectionCounts[g.Key] = g.Count();

        _tagById = (await _tags.GetAllAsync().ConfigureAwait(true)).ToDictionary(t => t.Id, StringComparer.Ordinal);
        await RefreshImageTagsAsync().ConfigureAwait(true);
        _allViews = (await _views.GetAllAsync().ConfigureAwait(true)).ToList();

        // 表示モード復元(REQ-052 v1.3/CR-6)
        _layout = string.Equals(_settings.DisplayMode, "list", StringComparison.Ordinal) ? "list" : "grid";

        // 選択コレクション復元(REQ-053/REQ-052 v1.3/CR-5)。引数優先(harness 併走中はシェルが
        // 検証済 id を渡す)、無ければ settings から復元する。解決不能なら未選択(REQ-053 の選択を
        // 促す空状態)へフォールバックし、無効 id は settings からも除去する。先頭への自動選択はしない。
        var restoreId = preferredCollectionId ?? _settings.LastCollectionId;
        if (restoreId is not null && _collections.Any(c => string.Equals(c.Id, restoreId, StringComparison.Ordinal)))
        {
            _collectionId = restoreId;
            _settings.LastCollectionId = restoreId;
        }
        else
        {
            _collectionId = null;
            _settings.LastCollectionId = null;
        }

        BuildEntries();
        await RefreshTrashCountAsync().ConfigureAwait(true); // ⋯「ゴミ箱」バッジ(ECO-018)
        _loaded = true;
        Recompute();
    }

    /// <summary>
    /// 終了時の永続化スナップショット(REQ-052 v1.3/CR-5・CR-6)。選択・表示モードの変更時にも
    /// 逐次 settings へ書くが(堅牢化)、最終確定もここで行う。Locale/LastViewId はシェルが担う。
    /// </summary>
    public void CaptureSettings()
    {
        _settings.LastCollectionId = _collectionId;
        _settings.DisplayMode = _layout == "list" ? "list" : "grid";
    }

    private async Task RefreshImageTagsAsync()
    {
        var all = await _tags.GetAllImageTagsAsync().ConfigureAwait(true);
        _imageTags = all.GroupBy(it => it.ImageId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
    }

    private void BuildEntries()
    {
        _entries = _allNormal
            .Where(r => _collectionId is not null && string.Equals(r.SyncFolderId, _collectionId, StringComparison.Ordinal))
            .Select(BuildEntry)
            .ToList();
    }

    private ImageEntry BuildEntry(ImageRecord r)
    {
        var tags = _imageTags.TryGetValue(r.Id, out var its)
            ? its.Where(it => _tagById.ContainsKey(it.TagId))
                 .Select(it => new EvalTagValue(it.TagId, _tagById[it.TagId].Type, it.Value)).ToList()
            : new List<EvalTagValue>();
        var root = _collectionPath.TryGetValue(r.SyncFolderId, out var p) ? p : "";
        var abs = Path.Combine(root, r.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        return new ImageEntry(r, abs, tags);
    }

    private IReadOnlyList<string> ImgTagIds(ImageEntry e) => e.Tags.Select(t => t.TagId).Distinct().ToList();

    private static string FmtSize(long bytes)
    {
        double mb = bytes / 1024.0 / 1024.0;
        return mb >= 1 ? $"{mb:0.0} MB" : $"{Math.Round(bytes / 1024.0)} KB";
    }

    private static string FmtDate(string iso)
    {
        return DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)
            ? dt.ToLocalTime().ToString("yyyy/MM/dd", CultureInfo.InvariantCulture)
            : iso;
    }

    // =====================================================================
    //  FS フォルダ軸 context
    // =====================================================================
    private sealed record FsContext(
        List<(string Name, int Count)> Folders,
        List<ImageEntry> Files,
        List<(string TagId, int Count)> Chips,
        bool AnyTagged);

    private FsContext ResolveFs()
    {
        string prefix = _fsPath.Count == 0 ? "" : string.Join("/", _fsPath) + "/";
        var folderCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var files = new List<ImageEntry>();
        foreach (var e in _entries)
        {
            var rel = e.Record.RelativePath;
            if (prefix.Length > 0 && !rel.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var remainder = prefix.Length > 0 ? rel[prefix.Length..] : rel;
            int slash = remainder.IndexOf('/');
            if (slash >= 0)
                folderCounts[remainder[..slash]] = folderCounts.GetValueOrDefault(remainder[..slash]) + 1;
            else
                files.Add(e);
        }
        var folders = folderCounts.Select(kv => (kv.Key, kv.Value)).ToList();
        bool anyTagged = files.Any(f => ImgTagIds(f).Count > 0);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var f in files)
            foreach (var tid in ImgTagIds(f))
                counts[tid] = counts.GetValueOrDefault(tid) + 1;
        var chips = counts.Select(kv => (kv.Key, kv.Value)).ToList();
        if (_tagFilter is not null)
            files = files.Where(f => ImgTagIds(f).Contains(_tagFilter)).ToList();
        return new FsContext(folders, files, chips, anyTagged);
    }

    private List<ImageEntry> SortFiles(List<ImageEntry> files)
    {
        var byId = files.ToDictionary(e => e.Record.Id, StringComparer.Ordinal);
        var sorted = _sorter.Sort(files.Select(e => e.Record), _sortField, _sortDir);
        return sorted.Select(r => byId[r.Id]).ToList();
    }

    private List<ImageEntry> AllLoadedImagesInContext()
    {
        // 編集モードの選択母集合・ビューアー順=現在の文脈で表示中の画像を**表示と同じソート順**で返す。
        // (SHIFT 範囲選択が表示順と一致する=歯抜け防止。連番/ビューアーの順序も表示順に一致する)
        if (_axis == "view" && _viewRoot is not null)
        {
            var fullPath = new List<GraphNode> { _viewRoot };
            fullPath.AddRange(_viewPath);
            return SortFiles(ViewMatched(fullPath));
        }
        return SortFiles(ResolveFs().Files); // FS: 現在のフォルダに直接ある画像(サブフォルダは含めない)
    }

    private static List<GraphNode> Append(List<GraphNode> path, GraphNode child)
    {
        var list = new List<GraphNode>(path) { child };
        return list;
    }

    /// <summary>ビュー条件 + ノードパス条件で _entries(選択コレクション scope)を絞り込む(OC-1/OC-3 再利用)。</summary>
    private List<ImageEntry> ViewMatched(IReadOnlyList<GraphNode> fullPath)
    {
        var conds = new List<ViewCondition>(_viewConditions);
        conds.AddRange(_pathConverter.BuildConditions(fullPath));
        if (conds.Count == 0) return _entries.ToList();
        var res = _evaluator.Evaluate(_entries.Select(e => e.ToImageWithTags()), conds);
        return _entries.Where(e => res.MatchedImageIds.Contains(e.Record.Id)).ToList();
    }

    // =====================================================================
    //  公開: 派生コレクション + スカラー(ImageTabView がバインド)
    // =====================================================================
    public ObservableCollection<CollectionRowVM> Collections { get; } = new();
    public ObservableCollection<AxisOptionVM> AxisOptions { get; } = new();
    public ObservableCollection<CrumbVM> Crumbs { get; } = new();
    public ObservableCollection<ChipVM> Chips { get; } = new();
    public ObservableCollection<ImageItemVM> Items { get; } = new();
    public ObservableCollection<AddGroupVM> AddGroups { get; } = new();
    public ObservableCollection<CurrentTagVM> CurrentTags { get; } = new();
    // 整理トレイ(ECO-014): 整理対象の一覧 + 検索結果候補(中央ペイン)
    public ObservableCollection<OrganizeSlotVM> OrganizeTargets { get; } = new();
    public ObservableCollection<OrganizeResultVM> SearchResults { get; } = new();

    public bool Collapsed => _collapsed;
    public bool Expanded => !_collapsed;
    public double SidebarWidth => _collapsed ? 64 : 276;
    public bool IsGrid => _layout == "grid";
    public bool IsList => _layout == "list";

    // ---- REQ-053: コレクション=選択スコープ(ECO-013 で原典 MainWindowViewModel から移管・等価維持) ----
    public string? SelectedCollectionId => _collectionId;
    public bool IsCollectionSelected => _collectionId is not null;
    /// <summary>未選択時に中央へ「コレクションを選択」プロンプトを出す(REQ-053)。</summary>
    public bool ShowCollectionPrompt => _loaded && _collectionId is null;
    /// <summary>グリッドペイン表示(コレクション選択済み かつ グリッドモード)。</summary>
    public bool ShowGridPane => IsCollectionSelected && IsGrid;
    /// <summary>リストペイン表示(コレクション選択済み かつ リストモード)。</summary>
    public bool ShowListPane => IsCollectionSelected && IsList;
    /// <summary>画像 0 件の空状態(コレクション選択済みのときのみ・未選択はプロンプトを優先)。</summary>
    public bool ShowEmptyMessage => IsCollectionSelected && Items.Count == 0;

    public bool IsViewAxis => _axis == "view";
    public bool IsFsActive => _axis == "fs";
    public string AxisLabel => _axis == "fs" ? "ファイルシステム" : _viewLabel;
    public bool AxisMenuOpen { get; private set; }
    public bool SortMenuOpen { get; private set; }
    public bool MoreMenuOpen { get; private set; }
    /// <summary>⋯ メンテナンス(トラッシュ/修復)はコレクションスコープ。未選択時は無効(REQ-053)。</summary>
    public bool CanOpenMaintenance => _collectionId is not null;
    public string SortLabel => _sortField switch
    {
        SortField.Name => "名前",
        SortField.FileSize => "サイズ",
        SortField.ModifiedDate => "更新日",
        SortField.CreatedDate => "作成日",
        _ => "名前",
    };
    public bool SortNoneActive => false;
    public bool SortNameActive => _sortField == SortField.Name;
    public bool SortDateActive => _sortField == SortField.ModifiedDate;
    public bool SortSizeActive => _sortField == SortField.FileSize;
    public bool SortEnabled => true;
    public double SortArrowAngle => _sortDir == SortDirection.Desc ? 180 : 0;
    public bool EditMode => _editMode;
    public string EditButtonLabel => _editMode ? "タグ編集を終了" : "タグ編集";
    public bool HomeActive { get; private set; }
    public string CountLabel { get; private set; } = "";
    public bool ShowChips { get; private set; }
    public bool ShowChipHint { get; private set; }
    public string ChipHintLabel { get; private set; } = "";
    public bool ShowEmptyTagNote { get; private set; }
    public bool PanelEmpty => _editMode && _selected.Count == 0;
    public bool PanelActive => _editMode && _selected.Count > 0;
    public bool HasSelection => _selected.Count > 0;
    public string SelectionLabel => $"{_selected.Count} 枚選択中";
    public bool OnCurrentTab => _panelTab == "current";
    public bool OnAddTab => _panelTab == "add";
    public bool HasCurrentTags => CurrentTags.Count > 0;
    public bool NoCurrentTags => CurrentTags.Count == 0;
    public string CurrentNote { get; private set; } = "";
    public string NoCurrentLabel { get; private set; } = "";

    // ---- 整理モード(ECO-014)公開契約 ----
    public bool OrganizeMode => _organizeMode;
    public string OrganizeButtonLabel => _organizeMode ? "整理を終了" : "整理";
    /// <summary>いずれかの文脈モード中(タグ編集 or 整理 or 作業 or 削除)。モード中は他モード入口・⋯ を隠す(集中・排他可視化・幅)。ECO-017/018 で作業・削除へ拡張。</summary>
    public bool InAnyMode => _editMode || _organizeMode || _workMode || _deleteMode;

    // ---- 作業モード(ECO-017)公開契約 ----
    /// <summary>選択を有効化するモード(タグ編集 or 作業 or 削除)。グリッドの選択視覚=チェック/選択順バッジを出す。</summary>
    public bool InSelectMode => _editMode || _workMode || _deleteMode;
    public bool WorkMode => _workMode;
    public string WorkButtonLabel => _workMode ? "作業を終了" : "作業";
    /// <summary>作業モード中に選択がある=「追加」が活性。</summary>
    public bool HasWorkSelection => _workMode && _selected.Count > 0;
    public int WorkSelCount => _selected.Count;
    /// <summary>「追加」ボタンの活性(=選択あり)。</summary>
    public bool CanAddToWork => HasWorkSelection;
    /// <summary>作業対象が1件以上ある=「作業対象 N 枚」チップを出す。</summary>
    public bool HasWorkTargets => _workMode && _workTargets.Count > 0;
    public string WorkTargetLabel => $"作業対象 {_workTargets.Count} 枚";

    // ---- ツールバー モード入口の出し分け(ECO-017/018: 排他隠し統一) ----
    // 各モード入口は他モードの最中は隠れる(自モード中は「終了」として残る)。削除は ⋯ メニューから入る
    // ためツールバー入口を持たず、削除中は全入口・⋯ が隠れて「削除を終了」+「ゴミ箱へ移動」のみ残る。
    public bool ShowEditEntry => !_organizeMode && !_workMode && !_deleteMode;
    public bool ShowOrganizeEntry => !_editMode && !_workMode && !_deleteMode;
    public bool ShowWorkEntry => !_editMode && !_organizeMode && !_deleteMode;

    // ---- 削除モード(ECO-018)公開契約 ----
    public bool DeleteMode => _deleteMode;
    /// <summary>削除モード中に選択がある=「ゴミ箱へ移動」が活性。</summary>
    public bool HasDeleteSelection => _deleteMode && _selected.Count > 0;
    public int DeleteSelCount => _selected.Count;
    public bool CanDeleteToTrash => HasDeleteSelection;
    /// <summary>⋯「ゴミ箱」のバッジ: 選択コレクションの deleted 件数(0 なら出さない)。</summary>
    public bool HasTrash => _trashCount > 0;
    public int TrashCount => _trashCount;

    // ---- ゴミ箱ポップアップ(ECO-019)公開契約 ----
    public bool TrashOpen { get; private set; }
    public ObservableCollection<TrashPopupItemVM> TrashPopupItems { get; } = new();
    public int TrashPopupCount => TrashPopupItems.Count;
    public bool HasTrashItems => TrashPopupItems.Count > 0;
    public bool TrashPopupEmpty => TrashPopupItems.Count == 0;
    public bool HasTrashSel => _trashSel.Count > 0;
    public int TrashSelCount => _trashSel.Count;
    public string TrashSelCountLabel => HasTrashSel ? $"{_trashSel.Count} 枚選択中" : "画像を選択して操作";
    public string TrashSelectAllLabel => (TrashPopupItems.Count > 0 && _trashSel.Count == TrashPopupItems.Count) ? "選択を解除" : "すべて選択";
    /// <summary>復元・完全削除は選択がある時のみ活性。</summary>
    public bool CanRestoreTrash => _trashSel.Count > 0;
    public bool CanPurgeTrash => _trashSel.Count > 0;
    /// <summary>右ペインの文脈モード(タグ編集 / 整理)は排他。どちらかなら右ペインを出す。</summary>
    public bool ShowRightPane => _editMode || _organizeMode;
    public bool IsTagEditContext => _editMode;
    public bool IsOrganizeContext => _organizeMode;

    // マージ先(残す1枚)
    public bool HasMergeTarget => _mergeTargetId is not null;
    public OrganizeSlotVM? MergeTarget { get; private set; }
    /// <summary>マージ先未設定: グリッドで残したい1枚を選ぶよう促す。</summary>
    public bool ShowMergeTargetPrompt => _organizeMode && _mergeTargetId is null;

    // 整理対象(統合し削除対象)
    public bool HasOrganizeTargets => _organizeTargets.Count > 0;
    /// <summary>マージ先はあるが整理対象が空: まとめる相手がない。</summary>
    public bool ShowOrganizeTargetsPrompt => _organizeMode && _mergeTargetId is not null && _organizeTargets.Count == 0;
    public string OrganizeTargetsCountLabel => $"{_organizeTargets.Count} 枚";

    // タグ統合(「マージ時にタグを含める」)。E-MERGE-034 は常にタグ union(INV-011)。OFF の no-union は IMG-011(別 ECO)。
    public bool IncludeTags { get => _includeTags; set { _includeTags = value; OnPropertyChanged(); } }

    // 似た画像を探す
    public bool IsSimilarMethod => _searchMethod == "similar";
    public bool IsCriteriaMethod => _searchMethod == "criteria";
    /// <summary>類似度しきい値(%)。1枚から探す=マージ先起点。</summary>
    public int SimilarThreshold
    {
        get => _similarThreshold;
        set { _similarThreshold = Math.Clamp(value, 50, 100); OnPropertyChanged(); OnPropertyChanged(nameof(SimilarThresholdLabel)); }
    }
    public string SimilarThresholdLabel => $"{_similarThreshold}%";
    /// <summary>類似検索はマージ先(基準画像)が必要。</summary>
    public bool CanRunSimilar => _mergeTargetId is not null;
    public string CriteriaName { get => _criteriaName; set => _criteriaName = value ?? ""; }
    public string CriteriaExt { get => _criteriaExt; set => _criteriaExt = value ?? ""; }
    public bool Searching => _searching;
    /// <summary>検索結果表示(中央ペインを候補一覧へ切替)。完了状態では出さない。</summary>
    public bool ShowSearchResults => _organizeMode && _hasSearched && !_organizeDone;
    public bool NoSearchResults => ShowSearchResults && SearchResults.Count == 0;
    /// <summary>検索実行可否: 条件検索は常に / 類似はマージ先(基準)が要る。</summary>
    public bool CanRunSearch => IsCriteriaMethod || _mergeTargetId is not null;
    /// <summary>中央ブラウズグリッド: 検索結果表示中は譲る(整理モードでもグリッドで対象を選ぶため出す)。</summary>
    public bool ShowBrowseGrid => ShowGridPane && !ShowSearchResults;
    public bool ShowBrowseList => ShowListPane && !ShowSearchResults;

    // 実行・完了
    public bool CanExecuteMerge => _mergeTargetId is not null && _organizeTargets.Count > 0 && !_organizeDone;
    public string MergeButtonLabel => $"マージを実行（{_organizeTargets.Count} 枚）";
    public bool OrganizeDone => _organizeDone;
    public string DoneSummary => $"{_doneSourceCount + 1} 枚を 1 枚へまとめ、{_doneSourceCount} 枚を削除しました。";
    /// <summary>取り消し: Undo 保持範囲は IMG-011(別 ECO)。本 ECO では affordance のみで未実装(無効)。</summary>
    public bool CanUndo => false;

    // =====================================================================
    //  Recompute
    // =====================================================================
    private void Recompute()
    {
        if (!_loaded) return;

        // ---- collections ----
        Collections.Clear();
        foreach (var c in _collections)
            Collections.Add(new CollectionRowVM(c.Id, c.Name, c.Path,
                _collectionCounts.GetValueOrDefault(c.Id), c.Id == _collectionId));

        // ---- AxisOptions(FS + 保存ビュー) ----
        AxisOptions.Clear();
        AxisOptions.Add(new AxisOptionVM("fs", "ファイルシステム", "OS のフォルダ階層", isView: false, _axis == "fs"));
        foreach (var v in _allViews)
            AxisOptions.Add(new AxisOptionVM(v.Id, v.Name, "タグで組んだビュー", isView: true, _axis == "view" && _viewId == v.Id));

        var folders = new List<(string Name, int Count)>();
        List<ImageEntry> files;
        List<string> crumbNames;
        Chips.Clear();
        ShowChips = false; ShowChipHint = false; ShowEmptyTagNote = false;
        _currentChildren.Clear();

        if (_axis == "view" && _viewRoot is not null)
        {
            // ---- view 軸(M3b): ノード階層をチップで潜り・パンくずで戻る ----
            var fullPath = new List<GraphNode> { _viewRoot };
            fullPath.AddRange(_viewPath);
            var current = fullPath[^1];
            files = SortFiles(ViewMatched(fullPath));
            crumbNames = _viewPath.Select(n => n.DisplayName).ToList();
            HomeActive = _viewPath.Count == 0;

            int ci = 0;
            foreach (var child in current.Children)
            {
                var key = "vc" + ci++;
                _currentChildren[key] = child;
                int count = ViewMatched(Append(fullPath, child)).Count;
                var color = TagColor(child.TagId is { } tid ? _tagById.GetValueOrDefault(tid) : null);
                Chips.Add(ChipVM.Colored(key, child.DisplayName, color, count, active: false, isNav: true));
            }
            if (Chips.Count > 0) { ShowChips = true; ShowChipHint = true; ChipHintLabel = "階層を掘る"; }
        }
        else
        {
            // ---- FS 軸 ----
            var ctx = ResolveFs();
            files = SortFiles(ctx.Files);
            folders = ctx.Folders.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
            if (_sortDir == SortDirection.Desc) folders.Reverse();
            crumbNames = _fsPath.ToList();
            HomeActive = _fsPath.Count == 0;
            if (ctx.AnyTagged)
            {
                ShowChips = true; ShowChipHint = true; ChipHintLabel = "タグで絞り込み";
                Chips.Add(ChipVM.Neutral("クリア", _tagFilter is null));
                foreach (var (tid, count) in ctx.Chips.OrderBy(c => c.TagId, StringComparer.Ordinal))
                {
                    if (!_tagById.TryGetValue(tid, out var def)) continue;
                    Chips.Add(ChipVM.Colored(tid, def.Name, TagColor(def), count, _tagFilter == tid, isNav: false));
                }
            }
            else if (_fsPath.Count > 0)
            {
                ShowEmptyTagNote = true;
            }
        }

        // ---- breadcrumb ----
        Crumbs.Clear();
        for (int i = 0; i < crumbNames.Count; i++)
            Crumbs.Add(new CrumbVM(crumbNames[i], i == crumbNames.Count - 1, i));
        CountLabel = $"{folders.Count + files.Count} 項目";

        // ---- items ----
        var selSet = new HashSet<string>(_selected);
        Items.Clear();
        foreach (var (name, _) in folders)
            Items.Add(new ImageItemVM(name, name, isFolder: true, isPlaceholder: false, hasThumb: false,
                thumbBrush: null, selectable: false, isSelected: false, hasTagDots: false,
                tagDots: new List<IBrush>(), sizeLabel: "—", dateLabel: "—", target: name));
        foreach (var e in files)
        {
            bool selected = selSet.Contains(e.Record.Id);
            int? order = selected ? _selected.IndexOf(e.Record.Id) + 1 : null; // 選択順バッジ(1 起点・REQ-041 CR-3)
            var tagsOf = ImgTagIds(e);
            bool inSelect = _editMode || _workMode || _deleteMode; // ECO-017/018: 作業・削除モードでも選択可(選択機構の再利用)
            var dots = (!inSelect && tagsOf.Count > 0)
                ? tagsOf.Take(3).Select(t => HexA(TagColor(_tagById.GetValueOrDefault(t)), 1)).ToList()
                : new List<IBrush>();
            Items.Add(new ImageItemVM(e.Record.Id, e.Record.FileName, isFolder: false, isPlaceholder: false,
                hasThumb: true, thumbBrush: null, selectable: inSelect, isSelected: selected,
                hasTagDots: !inSelect && tagsOf.Count > 0, tagDots: dots,
                sizeLabel: FmtSize(e.Record.FileSize), dateLabel: FmtDate(e.Record.ModifiedDate),
                target: null, absolutePath: e.AbsolutePath, selectionOrder: order,
                isMergeTarget: _organizeMode && string.Equals(e.Record.Id, _mergeTargetId, StringComparison.Ordinal),
                isOrganizeTarget: _organizeMode && _organizeTargets.Contains(e.Record.Id)));
        }

        // ---- 編集パネル / 整理トレイ(選択依存・小コレクション)----
        BuildContextPanels(selSet);

        OnPropertyChanged(string.Empty);
    }

    /// <summary>
    /// 選択/マーカーのみが変化したときの軽量更新。Items(グリッド)を作り直さず、
    /// 既存の各 ImageItemVM をその場更新する(大量画像でのクリック応答性=Items 全再構築と
    /// CollectionChanged Reset・スクロールリセットを避ける)。membership が変わる遷移(フォルダ移動・
    /// チップ・軸/ソート・モード切替=selectable/タグドットが変わる)は従来どおり Recompute を使う。
    /// </summary>
    private void RefreshSelectionMarkers()
    {
        if (!_loaded) return;
        var selSet = new HashSet<string>(_selected);
        foreach (var item in Items)
        {
            if (item.IsFolder) continue;
            bool selected = selSet.Contains(item.Id);
            int? order = selected ? _selected.IndexOf(item.Id) + 1 : null;
            bool merge = _organizeMode && string.Equals(item.Id, _mergeTargetId, StringComparison.Ordinal);
            bool org = _organizeMode && _organizeTargets.Contains(item.Id);
            item.SetSelectionMarkers(selected, order, merge, org);
        }
        BuildContextPanels(selSet);
        OnPropertyChanged(string.Empty);
    }

    /// <summary>選択依存パネル(タグ編集パネル+整理トレイ)の再構築。Items とは独立した小コレクション。</summary>
    private void BuildContextPanels(HashSet<string> selSet)
    {
        // ---- edit panel ----
        var selectedEntries = AllLoadedImagesInContext().Where(e => selSet.Contains(e.Record.Id)).ToList();
        CurrentTags.Clear();
        if (selectedEntries.Count > 0)
        {
            var first = ImgTagIds(selectedEntries[0]);
            var common = first.Where(t => selectedEntries.All(e => ImgTagIds(e).Contains(t)));
            foreach (var tid in common)
            {
                if (!_tagById.TryGetValue(tid, out var d)) continue;
                var col = TagColor(d);
                CurrentTags.Add(new CurrentTagVM(tid, d.Name, HexA(col, 1),
                    HexA(col, 0.12), HexA(col, 0.28), HexA(col, 1)));
            }
        }
        CurrentNote = selectedEntries.Count > 1 ? "選択画像に共通するタグ" : "この画像に付いているタグ";
        NoCurrentLabel = selectedEntries.Count > 1 ? "共通のタグはありません。" : "まだタグがありません。";

        BuildAddGroups(selectedEntries);

        // ---- 整理トレイ(ECO-014) ----
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
            var e = EntryById(id);
            if (e is null) continue; // マージ後に deleted 化した候補等は除外
            bool added = inTray.Contains(id) || id == _mergeTargetId;
            SearchResults.Add(new OrganizeResultVM(id, e.Record.FileName, e.AbsolutePath,
                FmtSize(e.Record.FileSize), score, isCrit, added));
        }
    }

    private ImageEntry? EntryById(string id)
        => _entries.FirstOrDefault(e => string.Equals(e.Record.Id, id, StringComparison.Ordinal));

    private OrganizeSlotVM? SlotFor(string id)
    {
        var e = EntryById(id);
        return e is null ? null : new OrganizeSlotVM(id, e.Record.FileName, e.AbsolutePath, FmtSize(e.Record.FileSize));
    }

    private void BuildAddGroups(List<ImageEntry> selectedEntries)
    {
        AddGroups.Clear();
        if (!_editMode || selectedEntries.Count == 0) return;

        var groups = new (TagType Type, string Label, string Hint, string Fg, string Bg)[]
        {
            (TagType.Simple, "シンプル", "タグ名のみ", "#5b6473", "#f0f2f6"),
            (TagType.Textual, "テキスト", "候補値から選ぶ", "#2459cf", "#eaf1fe"),
            (TagType.Numeric, "数値", "値を選ぶ", "#0f8a5e", "#eafaf3"),
        };
        foreach (var g in groups)
        {
            var rows = new List<AddRowVM>();
            foreach (var tag in _tagById.Values.Where(t => t.Type == g.Type)
                         .OrderBy(t => t.Name, StringComparer.Ordinal))
            {
                bool added = g.Type == TagType.Simple &&
                    selectedEntries.All(e => ImgTagIds(e).Contains(tag.Id));
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
                        bool setNow = selectedEntries.All(e =>
                            e.Tags.Any(t => t.TagId == tag.Id && t.Value == v));
                        row.ValueChips.Add(new ValueChipVM(tag.Id, v, setNow,
                            setNow ? Solid(col) : HexA(col, 0.1),
                            HexA(col, setNow ? 1 : 0.28),
                            setNow ? Brushes.White : HexA(col, 1)));
                    }
                }
                if (expanded && g.Type == TagType.Numeric)
                {
                    var ns = _numSettings.GetValueOrDefault(tag.Id);
                    var cur = CommonNumeric(selectedEntries, tag.Id);
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

    private string? CommonNumeric(List<ImageEntry> entries, string tagId)
    {
        if (entries.Count == 0) return null;
        var first = entries[0].Tags.FirstOrDefault(t => t.TagId == tagId)?.Value;
        if (first is null) return null;
        return entries.All(e => e.Tags.FirstOrDefault(t => t.TagId == tagId)?.Value == first) ? first : null;
    }

    private IReadOnlyList<string> SelectedIds => _selected.ToList();

    // =====================================================================
    //  コマンド
    // =====================================================================
    [RelayCommand]
    private async Task SelectCollection(string id)
    {
        if (_collectionId == id) return;
        _collectionId = id;
        _settings.LastCollectionId = id; // CR-5 書き戻し(永続化は SettingsStore / CaptureSettings)
        _fsPath.Clear(); _tagFilter = null; _selected.Clear(); _expandTag = null;
        BuildEntries();
        // view 軸なら新コレクション scope で NodeGraph(値ノード)を再構築する。
        if (_axis == "view" && _viewId is not null) await LoadViewAsync(_viewId);
        else Recompute();
    }

    [RelayCommand]
    private void ToggleSidebar() { _collapsed = !_collapsed; Recompute(); }

    /// <summary>
    /// コレクション「追加(+)」(ECO-013/IMG-009): コレクション管理(追加・スキャン・削除)ビューを開く
    /// 単一入口。閉じた後はベースデータを読み直し、FS 軸ルートへ戻す(選択コレクションは存続していれば維持・
    /// 削除されていれば未選択へフォールバック=REQ-053)。
    /// </summary>
    [RelayCommand]
    private async Task OpenFolderManagement()
    {
        var keep = _collectionId;
        await _windows.ShowFolderManagementAsync().ConfigureAwait(true);
        _axis = "fs"; _viewId = null; _viewRoot = null; _viewPath.Clear();
        _fsPath.Clear(); _tagFilter = null; _selected.Clear(); _expandTag = null;
        await InitializeAsync(keep).ConfigureAwait(true);
    }

    [RelayCommand]
    private void ToggleAxisMenu() { AxisMenuOpen = !AxisMenuOpen; SortMenuOpen = false; MoreMenuOpen = false; OnPropertyChanged(string.Empty); }

    public void CloseMenusFromDismiss()
    {
        if (!AxisMenuOpen && !SortMenuOpen && !MoreMenuOpen) return;
        AxisMenuOpen = false; SortMenuOpen = false; MoreMenuOpen = false;
        OnPropertyChanged(string.Empty);
    }

    [RelayCommand]
    private async Task SelectAxis(string id)
    {
        AxisMenuOpen = false;
        _tagFilter = null; _selected.Clear(); _expandTag = null;
        if (id == "fs")
        {
            _axis = "fs"; _viewId = null; _viewRoot = null; _viewPath.Clear(); _fsPath.Clear();
            Recompute();
            return;
        }
        await LoadViewAsync(id);
    }

    private async Task LoadViewAsync(string viewId)
    {
        var view = _allViews.FirstOrDefault(v => v.Id == viewId);
        if (view is null) { _axis = "fs"; _viewId = null; _viewRoot = null; Recompute(); return; }
        _axis = "view"; _viewId = viewId; _viewLabel = view.Name;
        _viewConditions = await _views.GetConditionsAsync(viewId).ConfigureAwait(true);
        var hierarchy = await _views.GetHierarchyAsync(viewId).ConfigureAwait(true);
        var valueIndex = TagValueIndex.Build(_entries.Select(e => e.ToImageWithTags()));
        var result = _graphBuilder.BuildGraph(hierarchy, _tagById, valueIndex);
        _viewRoot = result.Root;
        _viewPath.Clear();
        Recompute();
    }

    [RelayCommand]
    private void ToggleSortMenu() { SortMenuOpen = !SortMenuOpen; AxisMenuOpen = false; MoreMenuOpen = false; OnPropertyChanged(string.Empty); }

    [RelayCommand]
    private void ToggleMoreMenu() { MoreMenuOpen = !MoreMenuOpen; AxisMenuOpen = false; SortMenuOpen = false; OnPropertyChanged(string.Empty); }

    /// <summary>⋯ メニュー「ゴミ箱」: トラッシュを画像タブ内ポップアップで開く(ECO-019)。deleted 一覧を読み込み overlay を表示。</summary>
    [RelayCommand]
    private async Task OpenTrash()
    {
        MoreMenuOpen = false;
        if (_collectionId is null) { OnPropertyChanged(string.Empty); return; }
        await LoadTrashItemsAsync().ConfigureAwait(true);
        TrashOpen = true;
        OnPropertyChanged(string.Empty);
    }

    /// <summary>ゴミ箱ポップアップを閉じる。</summary>
    [RelayCommand]
    private void CloseTrash()
    {
        TrashOpen = false;
        _trashSel.Clear();
        OnPropertyChanged(string.Empty);
    }

    /// <summary>選択コレクションの deleted 画像を読み込みポップアップ一覧を作る(ファイル名昇順)。</summary>
    private async Task LoadTrashItemsAsync()
    {
        TrashPopupItems.Clear();
        _trashSel.Clear();
        if (_collectionId is null) return;
        var all = await _images.GetByFolderAsync(_collectionId).ConfigureAwait(true);
        var root = _collectionPath.GetValueOrDefault(_collectionId, "");
        foreach (var r in all.Where(r => r.Status == ImageStatus.Deleted)
                             .OrderBy(r => r.FileName, StringComparer.OrdinalIgnoreCase))
        {
            var abs = Path.Combine(root, r.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            TrashPopupItems.Add(new TrashPopupItemVM(r.Id, r.FileName, abs, FmtSize(r.FileSize)));
        }
    }

    /// <summary>ポップアップ項目の選択トグル(青選択・複数可)。</summary>
    [RelayCommand]
    private void ToggleTrashItem(TrashPopupItemVM item)
    {
        if (!_trashSel.Remove(item.Id)) _trashSel.Add(item.Id);
        RefreshTrashSelection();
    }

    /// <summary>すべて選択 / 選択を解除。</summary>
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
        OnPropertyChanged(string.Empty);
    }

    /// <summary>選択を復元(T6/T7・物理存在→normal/不在→missing)。復元分は一覧と母集合へ反映。</summary>
    [RelayCommand]
    private async Task RestoreSelectedTrash()
    {
        if (_trashSel.Count == 0) return;
        foreach (var id in _trashSel.ToList())
            await _trash.RestoreAsync(id).ConfigureAwait(true);
        await ReloadImagesAsync().ConfigureAwait(true); // 復元 normal はグリッドへ戻る
        await LoadTrashItemsAsync().ConfigureAwait(true);
        await RefreshTrashCountAsync().ConfigureAwait(true);
        Recompute();
    }

    /// <summary>選択を完全削除(T8・CASCADE)。確認+INV-009 非破壊明示(画像ファイルは削除されない=DB 行のみ除去)。</summary>
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
        Recompute();
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
        Recompute();
    }

    /// <summary>⋯ メニュー: 修復ライフサイクル(criteria/relink/復元)を既存モーダルで開く(ECO-015)。閉じ後にデータ再読込。</summary>
    [RelayCommand]
    private async Task OpenRepair()
    {
        MoreMenuOpen = false;
        if (_collectionId is null) { OnPropertyChanged(string.Empty); return; }
        await _windows.ShowRepairAsync(_collectionId).ConfigureAwait(true);
        await ReloadImagesAsync().ConfigureAwait(true);
        await RefreshTrashCountAsync().ConfigureAwait(true); // 修復の除外(missing→deleted)で件数が変わる
        Recompute();
    }

    [RelayCommand]
    private void SelectSort(string? col)
    {
        _sortField = col switch
        {
            "name" => SortField.Name,
            "date" => SortField.ModifiedDate,
            "size" => SortField.FileSize,
            _ => SortField.Name,
        };
        SortMenuOpen = false;
        Recompute();
    }

    [RelayCommand]
    private void ToggleSortDir() { _sortDir = _sortDir == SortDirection.Asc ? SortDirection.Desc : SortDirection.Asc; Recompute(); }

    [RelayCommand]
    private void SetGrid() { _layout = "grid"; _settings.DisplayMode = "grid"; Recompute(); } // CR-6

    [RelayCommand]
    private void SetList() { _layout = "list"; _settings.DisplayMode = "list"; Recompute(); } // CR-6

    [RelayCommand]
    private void ToggleEdit()
    {
        _editMode = !_editMode;
        if (_editMode) { _organizeMode = false; ResetOrganizeState(); _workMode = false; _deleteMode = false; } // 整理・作業・削除と排他(ECO-014/017/018)
        _selected.Clear(); _expandTag = null; _panelTab = "current";
        Recompute();
    }

    [RelayCommand]
    private void GoHome()
    {
        if (_axis == "view") _viewPath.Clear();
        else { _fsPath.Clear(); _tagFilter = null; }
        _selected.Clear();
        Recompute();
    }

    [RelayCommand]
    private void GoCrumb(int depth)
    {
        if (_axis == "view")
        {
            while (_viewPath.Count > depth + 1) _viewPath.RemoveAt(_viewPath.Count - 1);
        }
        else
        {
            while (_fsPath.Count > depth + 1) _fsPath.RemoveAt(_fsPath.Count - 1);
            _tagFilter = null;
        }
        _selected.Clear();
        Recompute();
    }

    [RelayCommand]
    private void ClickChip(ChipVM chip)
    {
        if (chip.IsNav)
        {
            // view 軸: 子ノードへ潜る
            if (_currentChildren.TryGetValue(chip.Id, out var node)) { _viewPath.Add(node); _selected.Clear(); Recompute(); }
            return;
        }
        if (chip.IsNeutral) _tagFilter = null;
        else _tagFilter = _tagFilter == chip.Id ? null : chip.Id;
        Recompute();
    }

    public void HandleItemClick(ImageItemVM item, bool ctrl, bool shift, bool isDoubleClick = false)
    {
        if (item.IsFolder)
        {
            if (item.Target is not null) { _fsPath.Add(item.Target); _tagFilter = null; _selected.Clear(); Recompute(); }
            return;
        }
        if (_organizeMode)
        {
            // 整理モード(ECO-014・モック準拠): マージ先未設定→マージ先に / 設定後→整理対象をトグル(選択ではない)
            if (_mergeTargetId is null) SetMergeTarget(item.Id);
            else if (item.Id != _mergeTargetId) ToggleOrganizeTarget(item.Id);
            return;
        }
        if (!_editMode && !_workMode && !_deleteMode)
        {
            // 閲覧モード(モック準拠): シングルクリックは無操作・ダブルクリックでビューアー起動(REQ-041)
            if (isDoubleClick) OpenViewer(item.Id);
            return;
        }
        ToggleSelect(item.Id, ctrl, shift); // 編集 or 作業 or 削除モード: 選択(inSelect・選択機構を再利用)
    }

    /// <summary>閲覧モードのダブルクリック=ビューアー起動(REQ-041)。表示順(SortFiles)で開く。</summary>
    private void OpenViewer(string id)
    {
        var ordered = AllLoadedImagesInContext();
        int idx = ordered.FindIndex(e => string.Equals(e.Record.Id, id, StringComparison.Ordinal));
        if (idx < 0) return;
        _windows.ShowViewer(ordered, idx);
    }

    private void ToggleSelect(string id, bool ctrl, bool shift)
    {
        var list = AllLoadedImagesInContext().Select(e => e.Record.Id).ToList();
        if (shift && _selected.Count > 0)
        {
            var last = _selected[^1];
            int a = list.IndexOf(last), b = list.IndexOf(id);
            if (a >= 0 && b >= 0)
            {
                int lo = Math.Min(a, b), hi = Math.Max(a, b);
                foreach (var rid in list.GetRange(lo, hi - lo + 1))
                    if (!_selected.Contains(rid)) _selected.Add(rid);
                RefreshSelectionMarkers(); // 選択のみ変化=Items を作り直さない
                return;
            }
        }
        if (ctrl) { if (!_selected.Remove(id)) _selected.Add(id); RefreshSelectionMarkers(); return; }
        if (_selected.Count == 1 && _selected[0] == id) _selected.Clear();
        else { _selected.Clear(); _selected.Add(id); }
        RefreshSelectionMarkers();
    }

    [RelayCommand]
    private void TabCurrent() { _panelTab = "current"; Recompute(); }

    [RelayCommand]
    private async Task TabAdd()
    {
        _panelTab = "add"; _expandTag = null;
        Recompute();
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task ClickAddRow(AddRowVM row)
    {
        if (row.Added) return;
        if (!row.Expandable)
        {
            await ApplyTagAsync(row.Id, null);
            return;
        }
        // 展開: 設定をロードしてから再描画
        if (_expandTag == row.Id) { _expandTag = null; Recompute(); return; }
        _expandTag = row.Id;
        await EnsureSettingsAsync(row.Id);
        Recompute();
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

    [RelayCommand]
    private async Task ApplyTextValue(ValueChipVM chip) => await ApplyTagAsync(chip.TagId, chip.Value);

    [RelayCommand]
    private async Task ApplyRating(NumCellVM cell) => await ApplyTagAsync(cell.TagId, cell.Label);

    /// <summary>連番別アクション(UQ-I02b): NumericValueDialog で固定値/連番を生成し選択画像へ原子バッチ付与。</summary>
    [RelayCommand]
    private async Task ApplySequential(string tagId)
    {
        if (_selected.Count == 0) return;
        if (!_tagById.TryGetValue(tagId, out var tag) || tag.Type != TagType.Numeric) return;
        await EnsureSettingsAsync(tagId);
        var settings = _numSettings.GetValueOrDefault(tagId);
        var values = await _windows.ShowNumericValueDialogAsync(tag, settings, _selected.Count).ConfigureAwait(true);
        if (values is null || values.Count != _selected.Count) return; // キャンセル or 数不一致
        var assignments = new List<(string ImageId, string? Value)>(_selected.Count);
        for (int i = 0; i < _selected.Count; i++)
            assignments.Add((_selected[i], values[i]));
        var result = await _tagService.TagImagesWithValuesAsync(tagId, assignments).ConfigureAwait(true);
        if (result.IsSuccess) await ReloadTagsAsync();
    }

    private async Task ApplyTagAsync(string tagId, string? value)
    {
        if (_selected.Count == 0) return;
        var result = await _tagService.TagImagesAsync(SelectedIds, tagId, value).ConfigureAwait(true);
        if (result.IsSuccess) await ReloadTagsAsync();
    }

    [RelayCommand]
    private async Task RemoveCurrentTag(CurrentTagVM tag)
    {
        if (_selected.Count == 0) return;
        var result = await _tagService.UntagImagesAsync(SelectedIds, tag.Id).ConfigureAwait(true);
        if (result.IsSuccess) await ReloadTagsAsync();
    }

    private async Task ReloadTagsAsync()
    {
        await RefreshImageTagsAsync().ConfigureAwait(true);
        BuildEntries();
        Recompute();
    }

    // =====================================================================
    //  整理モード コマンド(ECO-014: 類似+マージ統合「整理トレイ」)
    // =====================================================================
    [RelayCommand]
    private void ToggleOrganize()
    {
        _organizeMode = !_organizeMode;
        if (_organizeMode) { _editMode = false; _workMode = false; _deleteMode = false; } // タグ編集・作業・削除と排他(ECO-014/017/018)
        ResetOrganizeState();
        Recompute();
    }

    // =====================================================================
    //  作業モード コマンド(ECO-017: 作業対象セットの蓄積)
    // =====================================================================
    /// <summary>作業モード開始/終了。タグ編集/整理と排他・選択クリア(モック toggleWork 準拠)。</summary>
    [RelayCommand]
    private void ToggleWork()
    {
        _workMode = !_workMode;
        if (_workMode) { _editMode = false; _organizeMode = false; ResetOrganizeState(); _deleteMode = false; } // 他文脈モードと排他(ECO-018)
        _selected.Clear(); _expandTag = null;
        Recompute();
    }

    /// <summary>選択中の画像を作業対象セットへ和集合追加し、選択をクリア(モック addToWork 準拠)。選択なしは無操作。</summary>
    [RelayCommand]
    private void AddToWork()
    {
        if (_selected.Count == 0) return;
        foreach (var id in _selected)
            if (!_workTargets.Contains(id)) _workTargets.Add(id); // Set 意味論(重複なし)
        _selected.Clear();
        RefreshSelectionMarkers(); // 選択クリア+チップ更新のみ=Items を作り直さない
    }

    // =====================================================================
    //  削除モード コマンド(ECO-018: ⋯メニュー「削除」=ゴミ箱へ移動)
    // =====================================================================
    /// <summary>⋯メニュー「削除」: 削除モードに入る。他文脈モードと排他・選択クリア・メニューを閉じる。</summary>
    [RelayCommand]
    private void EnterDelete()
    {
        MoreMenuOpen = false;
        _deleteMode = true;
        _editMode = false; _organizeMode = false; ResetOrganizeState(); _workMode = false; // 排他
        _selected.Clear(); _expandTag = null;
        Recompute();
    }

    /// <summary>削除モードを終了(選択クリア)。</summary>
    [RelayCommand]
    private void ExitDelete()
    {
        _deleteMode = false;
        _selected.Clear();
        Recompute();
    }

    /// <summary>選択中の normal 画像をゴミ箱へ移動(normal→deleted のソフト削除・物理非破壊 INV-009・復元可)。選択なしは無操作。</summary>
    [RelayCommand]
    private async Task DeleteToTrash()
    {
        if (_selected.Count == 0) return;
        var ids = _selected.ToList();
        foreach (var id in ids)
            await _trash.DeleteToTrashAsync(id).ConfigureAwait(true); // Core 経由(状態遷移は TrashService が担う)
        _selected.Clear();
        await ReloadImagesAsync().ConfigureAwait(true); // deleted は normal 母集合から外れる(REQ-053)
        await RefreshTrashCountAsync().ConfigureAwait(true);
        Recompute();
    }

    /// <summary>⋯「ゴミ箱」バッジ用に選択コレクションの deleted 件数を取り直す。</summary>
    private async Task RefreshTrashCountAsync()
    {
        if (_collectionId is null) { _trashCount = 0; return; }
        var all = await _images.GetByFolderAsync(_collectionId).ConfigureAwait(true);
        _trashCount = all.Count(r => r.Status == ImageStatus.Deleted);
    }

    private void ResetOrganizeState()
    {
        _mergeTargetId = null;
        _organizeTargets.Clear();
        _includeTags = true;
        _searchMethod = "similar";
        _criteriaName = ""; _criteriaExt = "";
        _searching = false; _hasSearched = false;
        _searchResults = new();
        _organizeDone = false; _doneSourceCount = 0;
        _selected.Clear();
    }

    /// <summary>グリッドで残したい1枚をマージ先にする(整理対象に入っていれば外す)。</summary>
    [RelayCommand]
    private void SetMergeTarget(string imageId)
    {
        if (!_organizeMode) return;
        _organizeTargets.Remove(imageId);
        _mergeTargetId = imageId;
        RefreshSelectionMarkers(); // マージ先マーカー+トレイのみ=Items を作り直さない
    }

    private void ToggleOrganizeTarget(string imageId)
    {
        if (!_organizeMode || _mergeTargetId is null || imageId == _mergeTargetId) return;
        if (!_organizeTargets.Remove(imageId)) _organizeTargets.Add(imageId);
        RefreshSelectionMarkers(); // 整理対象マーカー+トレイのみ=Items を作り直さない
    }

    [RelayCommand]
    private void RemoveOrganizeTarget(string imageId)
    {
        if (_organizeTargets.Remove(imageId)) RefreshSelectionMarkers();
    }

    /// <summary>整理対象をマージ先へ昇格し、元のマージ先を整理対象へ戻す(モック「マージ先にする」)。</summary>
    [RelayCommand]
    private void PromoteToMergeTarget(string imageId)
    {
        if (!_organizeTargets.Remove(imageId)) return;
        if (_mergeTargetId is not null) _organizeTargets.Add(_mergeTargetId);
        _mergeTargetId = imageId;
        RefreshSelectionMarkers();
    }

    [RelayCommand]
    private void ToggleIncludeTags() { _includeTags = !_includeTags; Recompute(); }

    [RelayCommand]
    private void SetSearchMethod(string method)
    {
        _searchMethod = method == "criteria" ? "criteria" : "similar";
        Recompute();
    }

    /// <summary>似た画像を探す: 類似(E-SIMSEARCH-032)または条件(E-CRITERIA-037)。結果を中央ペインへ。</summary>
    [RelayCommand]
    private async Task RunSearch()
    {
        if (!_organizeMode || _collectionId is null) return;
        _searching = true; Recompute();
        var results = new List<(string ImageId, int Score, bool IsCriteria)>();
        try
        {
            if (_searchMethod == "criteria")
            {
                var criteria = BuildCriteria();
                if (CriteriaHasAny(criteria))
                {
                    var recs = await _criteriaSearch.SearchAsync(_collectionId, criteria,
                        new HashSet<ImageStatus> { ImageStatus.Normal }, CancellationToken.None).ConfigureAwait(true);
                    foreach (var r in recs)
                    {
                        if (string.Equals(r.Id, _mergeTargetId, StringComparison.Ordinal)) continue; // マージ先自身は候補に出さない
                        results.Add((r.Id, 100, true));
                    }
                }
            }
            else if (_mergeTargetId is not null) // 類似は基準(マージ先)が必要
            {
                var found = await _similar.FindSimilarAsync(_mergeTargetId, _similarThreshold).ConfigureAwait(true);
                foreach (var s in found) results.Add((s.ImageId, s.Score, false));
            }
        }
        finally
        {
            _searching = false;
        }
        _searchResults = results;
        _hasSearched = true;
        Recompute();
    }

    private SearchCriteria BuildCriteria() => new()
    {
        NameContains = string.IsNullOrWhiteSpace(_criteriaName) ? null : _criteriaName.Trim(),
        Extension = string.IsNullOrWhiteSpace(_criteriaExt) ? null : _criteriaExt.Trim(),
        // hash / サイズ範囲 / 更新日範囲の入力は surface 増分(M2)で結線する。
    };

    private static bool CriteriaHasAny(SearchCriteria c) =>
        c.Hash is not null || c.NameContains is not null || c.Extension is not null ||
        c.MtimeFrom is not null || c.MtimeTo is not null || c.SizeMin is not null || c.SizeMax is not null;

    /// <summary>検索結果の候補を整理対象へ追加する(マージ先が前提・モック「整理対象に追加」)。</summary>
    [RelayCommand]
    private void AddCandidateToTargets(string imageId)
    {
        if (_mergeTargetId is null || string.Equals(imageId, _mergeTargetId, StringComparison.Ordinal)) return;
        if (!_organizeTargets.Contains(imageId)) _organizeTargets.Add(imageId);
        Recompute();
    }

    /// <summary>マージ実行: E-MERGE-034(原子・タグ union・source=deleted・物理非破壊 INV-009)。完了後にデータ再読込。</summary>
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
        await ReloadImagesAsync().ConfigureAwait(true);
        Recompute();
    }

    /// <summary>別の整理を続ける: 完了状態を畳んでトレイをリセット(整理モードは維持)。</summary>
    [RelayCommand]
    private void ContinueOrganize()
    {
        ResetOrganizeState();
        Recompute();
    }

    /// <summary>マージ後のデータ再読込(source は deleted 化=母集合から外れる)。</summary>
    private async Task ReloadImagesAsync()
    {
        _allNormal = (await _images.GetAllNormalAsync().ConfigureAwait(true)).ToList();
        _collectionCounts.Clear();
        foreach (var g in _allNormal.GroupBy(r => r.SyncFolderId, StringComparer.Ordinal))
            _collectionCounts[g.Key] = g.Count();
        await RefreshImageTagsAsync().ConfigureAwait(true);
        BuildEntries();
    }
}
