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
public sealed partial class ImageTabViewModel : ObservableObject, IChipStripHost
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
    // ECO-036 第3段: SimilaritySearchService/MergeService/CriteriaSearchService はホストで保持しない
    // (整理の実行部は Organize 子 VM の所有 — E36S1-004/E36S2 と同型の後始末)
    private readonly IWindowService _windows;
    private readonly AppSettings _settings;
    // ECO-036 第2段: WorkspaceService はホストで保持しない(受け渡しは Work 子 VM の所有 — E36S1-004 と同型の後始末)
    private readonly LocalizationService _localization; // ECO-025 β-2: 表示列ポップオーバーの列ピッカー生成に使用

    // ---- ロード済みデータ ----
    private List<SyncFolder> _collections = new();
    private readonly Dictionary<string, int> _collectionCounts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _collectionPath = new(StringComparer.Ordinal);
    private List<ImageRecord> _allNormal = new();
    private HashSet<string> _normalIds = new(StringComparer.Ordinal);
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
    private string _viewLabel = ""; // 既定ラベル(view.tagView)は _localization 準備後に ctor で設定(GF-079-01)
    private IReadOnlyList<ViewCondition> _viewConditions = Array.Empty<ViewCondition>();
    private GraphNode? _viewRoot;
    private readonly List<GraphNode> _viewPath = new();
    /// <summary>ビュー軸の表示モード(ECO-084/REQ-094): false=すべて(累積フィルタ) / true=未分類(最深配置)。</summary>
    private bool _viewUnclassified;
    private readonly Dictionary<string, GraphNode> _currentChildren = new(StringComparer.Ordinal);
    private string _layout = "grid";
    // ECO-025 β/FL-003 v2: ソートはアクティブビューの表示列を軸に grid/list で共有(旧 名前/更新日/サイズ 固定ソートは廃止)。
    // null=未ソート=名前昇順の既定順。列 key = name/size/modified_date/タグ id。
    private string? _sortColKey;
    private SortDirection _sortColDir = SortDirection.Asc;
    private IReadOnlyList<ListColumnDef> _listColumnDefs = [];
    // (ECO-026/#5) 現在表示中の matched をキャッシュ=表示列ライブ編集で母集合を再評価せず Items を作り直すため。
    private List<(string Name, int Count)> _matchedFolders = new();
    // ECO-118: タグ付与/剥奪の差分更新用キャッシュ(Recompute の母集合評価時に確保し、
    // タグ変更時は選択分の旧/新評価の差分だけでチップ件数を追随させる)
    private readonly List<(GraphNode Child, int Matched, int Unclassified)> _viewChipCounts = new();
    private readonly Dictionary<string, int> _fsChipCounts = new(StringComparer.Ordinal);
    /// <summary>ECO-118(R8 F4): 差分経路の選択規模上限。超過時は全面再計算のほうが安い(逐次 DB 再読の定数回避)。</summary>
    private const int DeltaSelectionLimit = 256;
    private List<ImageEntry> _matchedFiles = new();
    // ECO-113: 選択クリック経路から母集合規模の処理を撤去するためのインデックス群。
    // 維持サイト= _entryById: BuildEntries/ClearContentData/スキャン append(OnScanUpdated)の 3 変異サイト全部 /
    // _itemById・_matchedIndexById: BuildItemsFromMatched+スキャン append+未ロード時 Items.Clear。
    // _markedItemIds= 何らかのマーカー(選択/宛先/整理対象)を保持中の item id(差分更新の解除対象)。
    private Dictionary<string, ImageEntry> _entryById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ImageItemVM> _itemById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _matchedIndexById = new(StringComparer.Ordinal);
    private readonly HashSet<string> _markedItemIds = new(StringComparer.Ordinal);
    private string? _tagFilter;
    private bool _editMode;
    private readonly List<string> _selected = new();
    private bool _collapsed;
    private string _panelTab = "current";
    private string? _expandTag;
    private bool _loaded;
    // ECO-064/IMG-019: shell-first startup。catalog と選択 collection content は独立状態を持つ。
    private bool _catalogLoaded;
    private bool _isCatalogLoading;
    private bool _isContentLoading;
    // ECO-108: エラー/通知は i18n キーで保持し表示時に解決(解決済み文字列の保持は言語非追随)
    private string? _catalogErrorKey;
    private string? _contentErrorKey;
    private long _catalogLoadGeneration;
    private long _contentLoadGeneration;
    private CancellationTokenSource? _catalogLoadCts;
    private CancellationTokenSource? _contentLoadCts;
    private bool _loadingStopped;
    private readonly ScanCoordinator? _scans;
    private readonly HashSet<string> _scanningCollections = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _scanOrderByCollection = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _completedScanOrderByCollection = new(StringComparer.Ordinal);
    private string? _scanNoticeKey;

    // ---- 整理モード(ECO-014: 類似+マージ統合「整理トレイ」)----
    // タグ編集モードと排他の文脈モード。マージ先(残す1枚)と整理対象(統合し削除対象)を選び、
    // 「似た画像を探す」(類似 E-SIMSEARCH-032 / 条件 E-CRITERIA-037)で候補を中央ペインに出し、
    // マージ実行(E-MERGE-034 原子・タグ union・source=deleted・物理非破壊 INV-009)で 1 枚へまとめる。
    // ECO-036 第3段: 状態12+実行部+操作は Organize 子 VM(M-UI-ORGANIZE-034)へ移送。モードフラグ・排他・
    // 全公開契約(転送プロパティ+コマンド殻)・Recompute のトレイ構築はホスト残置(order §12.2)。
    private bool _organizeMode;
    public ImageTabOrganizeViewModel Organize { get; }

    // ---- 作業モード(ECO-017: 作業対象セットの蓄積)----
    // タグ編集/整理に並ぶ3つ目の排他文脈モード。作業中はグリッドが選択可能になり(inSelect=編集 or 作業・
    // 既存の選択機構を再利用)、「追加」で選択を作業対象へ和集合蓄積し選択をクリアする。
    // 右ペインは開かない(追加ボタン+作業対象チップはツールバー内)。
    // ECO-036 第2段: 蓄積ストア+受け渡しは Work 子 VM(M-UI-WORK-033)へ移送。モードフラグ・排他・
    // 合成 UI プロパティはホスト残置(order §10.2 — XAML/tests の消費者は全てホスト公開契約)。
    private bool _workMode;
    public ImageTabWorkViewModel Work { get; }

    // ---- 削除モード(ECO-018: ⋯メニュー「削除」=ゴミ箱へ移動)----
    // タグ編集/整理/作業に並ぶ排他文脈モード。⋯メニューの「削除」で入る(トグル入口は持たない)。
    // 削除中はグリッドが選択可能になり(inSelect)、「ゴミ箱へ移動」で選択を normal→deleted の
    // ソフト削除(物理非破壊 INV-009・復元可)へ。修復/ゴミ箱は既存モーダル(ECO-015)のまま。
    private bool _deleteMode;

    // ---- ファイル操作モード(ECO-112: ⋯メニュー「ファイル操作」=参照系・右ペインなし)----
    // タグ編集/整理/作業/削除に並ぶ第5の排他文脈モード。選択機構(inSelect)を再利用するが、
    // 選択順の番号バッジは出さない(VC-IMG-13=白✓)。パスをコピー/場所を開くは IFileOperationsService 経由。
    private bool _fileOpsMode;
    private readonly IFileOperationsService _fileOps;
    // コピー完了フィードバック(IMG-026② 裁定: ボタン内一時表示・約2秒)。解除遷移の全列挙(ECO-104 教訓)=
    // タイマ / モード離脱 / 選択・母集合変化(RefreshSelectionMarkers・Recompute 経由)。ラベルは表示時解決(ECO-106)。
    private bool _copyFeedback;
    private CancellationTokenSource? _copyFeedbackCts;

    /// <summary>コピー完了フィードバックの表示時間(既定 約2秒=IMG-026② 裁定)。テスト/撮影ハーネスが
    /// 短縮・固定表示に差し替える(TagsTab の SavedToastDuration と同じ様式)。</summary>
    public TimeSpan CopyFeedbackDuration { get; set; } = TimeSpan.FromSeconds(2);

    // ---- ゴミ箱フィーチャ(ECO-018/ECO-019)は子 VM へ移送(ECO-036 第1段) ----
    public ImageTabTrashViewModel Trash { get; }

    /// <summary>XAML 文言バインディング用の i18n プロキシ(ECO-079: 画像タブの直書き文言を Loc[key] 経由へ)。
    /// CultureChanged で差し替えて全文言を一斉再バインドする(K-AVALONIA の罠対策=TagsTabViewModel の DF-3 に同じ)。</summary>
    public LocalizationProxy Loc { get; private set; } = null!;

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
        AppSettings settings,
        WorkspaceService workspaces,
        LocalizationService localization,
        ScanCoordinator? scanCoordinator = null,
        IFileOperationsService? fileOps = null)
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
        _windows = windows;
        _settings = settings;
        _fileOps = fileOps ?? new FileOperationsService();
        Work = new ImageTabWorkViewModel(workspaces);
        _localization = localization;
        _viewLabel = localization.T("view.tagView"); // 既定ビュー軸ラベル(GF-079-01)
        Loc = new LocalizationProxy(localization);
        // ECO-091: チップ行の容量・overflow(最大2行+ほかN件→ポップオーバー)。選択の意味論は ClickChip へ委譲
        ChipStrip = new ChipStripViewModel(localization, Chips, ClickChip);
        localization.CultureChanged += (_, _) =>
        {
            // ECO-079/DF-3: Loc 差し替えで画像タブ全文言バインディングを再評価させる
            Loc = new LocalizationProxy(localization);
            OnPropertyChanged(nameof(Loc));
            // GF-079-01: VM 算出ラベル(ボタン/軸/列/件数)も言語切替へ追随させる
            OnPropertyChanged(string.Empty);
            ChipStrip.OnCultureChanged(); // 「ほか N 件」ラベルの追随(ECO-091)
            // ECO-108: 焼き込みラベル(列見出し/ソート候補/件数/チップヒント/タグ面ノート)は
            // 再通知では再解決されない — WorkTab の Rebuild 対(GF-079-01)と対称化して再計算する
            Recompute();
        };
        _scans = scanCoordinator;
        if (_scans is not null)
        {
            _scans.Updated += OnScanUpdated;
        }
        Trash = new ImageTabTrashViewModel(
            images,
            trash,
            windows,
            getCollectionId: () => _collectionId,
            reloadImagesAsync: ReloadImagesAsync,
            recompute: Recompute,
            fmtSize: FmtSize,
            // MoreMenuOpen は通知なし自動プロパティ — ホスト内の全変更箇所と同じく通知を伴う
            // (golden 所見 G-E36S1-2 の是正: 通知なしラムダではメニューが視覚的に閉じない)
            closeMoreMenu: () => { MoreMenuOpen = false; OnPropertyChanged(string.Empty); },
            resolveAbsolutePath: ResolveAbsolutePath,
            localization: localization);
        Organize = new ImageTabOrganizeViewModel(
            images,
            similar,
            merge,
            getCollectionId: () => _collectionId,
            getSimilarityScopeCandidates: ResolveSimilarityScopeCandidates,
            recompute: Recompute,
            refreshSelectionMarkers: RefreshSelectionMarkers,
            reloadImagesAsync: ReloadImagesAsync,
            localization: localization,
            notifySearchState: () => OnPropertyChanged(string.Empty));
    }

    /// <summary>
    /// ECO-062/IMG-018: 検索ボタン押下時の FS/view 文脈を候補 snapshot にする。
    /// FS は _entries から再解決してタグ chip の一時フィルタを候補へ波及させない。
    /// </summary>
    private IReadOnlyList<ImageRecord> ResolveSimilarityScopeCandidates()
    {
        if (Organize.MergeTargetId is not { } baseId)
        {
            return [];
        }

        var baseImage = _entries.FirstOrDefault(entry =>
            string.Equals(entry.Record.Id, baseId, StringComparison.Ordinal))?.Record;
        if (baseImage is null)
        {
            return [];
        }

        if (_axis == "view" && _viewRoot is not null)
        {
            var fullPath = new List<GraphNode> { _viewRoot };
            fullPath.AddRange(_viewPath);
            var currentNodeImages = ViewMatched(fullPath).Select(entry => entry.Record).ToList();
            return SimilarityScopeResolver.ForView(currentNodeImages, baseImage);
        }

        return SimilarityScopeResolver.ForFileSystem(
            _entries.Select(entry => entry.Record).ToList(), baseImage, _fsPath);
    }

    /// <summary>コレクションルート+相対パスから絶対パスを組み立てる(BuildEntry と同型・ゴミ箱子 VM へも供給)。</summary>
    private string ResolveAbsolutePath(string relativePath)
    {
        var root = _collectionId is not null && _collectionPath.TryGetValue(_collectionId, out var p) ? p : "";
        return Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
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
    // ECO-114: タグドットのブラシキャッシュ(色数は高々タグ数=母集合件数×3 個の再生成を排除)
    private readonly Dictionary<string, IBrush> _dotBrushCache = new(StringComparer.OrdinalIgnoreCase);
    private IBrush DotBrush(string hex)
    {
        if (!_dotBrushCache.TryGetValue(hex, out var brush))
        {
            brush = Solid(hex);
            _dotBrushCache[hex] = brush;
        }
        return brush;
    }
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
        // 表示モード復元(REQ-052 v1.3/CR-6)
        _layout = string.Equals(_settings.DisplayMode, "list", StringComparison.Ordinal) ? "list" : "grid";
        // Opened event の同じ dispatcher turn でDB処理へ入らず、shellの初回描画を先に許可する。
        _isCatalogLoading = true;
        _catalogErrorKey = null;
        OnPropertyChanged(string.Empty);
        await Task.Yield();
        await LoadCatalogAsync(preferredCollectionId ?? _settings.LastCollectionId).ConfigureAwait(true);
    }

    private async Task LoadCatalogAsync(string? restoreId)
    {
        _loadingStopped = false;
        var generation = Interlocked.Increment(ref _catalogLoadGeneration);
        _catalogLoadCts?.Cancel();
        _catalogLoadCts?.Dispose();
        _catalogLoadCts = new CancellationTokenSource();
        var ct = _catalogLoadCts.Token;

        // catalog再読込は進行中contentを無効化する。旧結果を新catalogへ適用しない。
        CancelContentLoad(clearState: true);
        _catalogLoaded = false;
        _loaded = false;
        _isCatalogLoading = true;
        _catalogErrorKey = null;
        _contentErrorKey = null;
        _collectionId = null;
        ClearContentData();
        _tagById = new Dictionary<string, Tag>(StringComparer.Ordinal);
        _allViews = [];
        OnPropertyChanged(string.Empty);

        try
        {
            // Microsoft.Data.Sqlite/Dapper の await が同期完了してもUI threadを占有しない明示境界。
            var catalog = await Task.Run(async () =>
            {
                var folders = await _folders.GetAllAsync().ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                var counts = await _images.GetNormalCountsByFolderAsync(ct).ConfigureAwait(false);
                return (Folders: folders.ToList(), Counts: counts);
            }, ct).ConfigureAwait(true);

            if (_loadingStopped || generation != _catalogLoadGeneration || ct.IsCancellationRequested)
                return;

            _collections = catalog.Folders;
            _collectionPath.Clear();
            foreach (var collection in _collections) _collectionPath[collection.Id] = collection.Path;
            _collectionCounts.Clear();
            foreach (var pair in catalog.Counts) _collectionCounts[pair.Key] = pair.Value;

            _collectionId = restoreId is not null && _collections.Any(c =>
                string.Equals(c.Id, restoreId, StringComparison.Ordinal))
                ? restoreId
                : null;
            _settings.LastCollectionId = _collectionId;

            if (_scans is not null)
            {
                _scanningCollections.RemoveWhere(id => !_scans.IsScanning(id));
                foreach (var collection in _collections.Where(c => _scans.IsScanning(c.Id)))
                {
                    _scanningCollections.Add(collection.Id);
                    EnsureScanOrder(collection.Id);
                }
            }

            _catalogLoaded = true;
            _isCatalogLoading = false;
            Recompute();

            if (_collectionId is { } collectionId)
                await LoadContentAsync(collectionId).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // 新しいcatalog loadまたは終了が旧結果を無効化した正常系。
        }
        catch (Exception)
        {
            if (_loadingStopped || generation != _catalogLoadGeneration) return;
            _isCatalogLoading = false;
            _catalogErrorKey = "view.collectionLoadFailed";
            _catalogLoaded = false;
            OnPropertyChanged(string.Empty);
        }
    }

    private async Task LoadContentAsync(string collectionId)
    {
        var generation = Interlocked.Increment(ref _contentLoadGeneration);
        _contentLoadCts?.Cancel();
        _contentLoadCts?.Dispose();
        _contentLoadCts = new CancellationTokenSource();
        var ct = _contentLoadCts.Token;

        _loaded = false;
        _isContentLoading = true;
        _contentErrorKey = null;
        ClearContentData();
        Recompute();

        try
        {
            var content = await Task.Run(async () =>
            {
                var images = await _images.GetNormalByFolderAsync(collectionId, ct).ConfigureAwait(false);
                var imageTags = await _tags.GetImageTagsByFolderAsync(collectionId, ct).ConfigureAwait(false);
                var tags = await _tags.GetAllAsync().ConfigureAwait(false);
                var views = await _views.GetAllAsync().ConfigureAwait(false);
                var trashCount = await _images.CountByFolderAndStatusAsync(
                    collectionId, ImageStatus.Deleted, ct).ConfigureAwait(false);
                return (Images: images.ToList(), ImageTags: imageTags.ToList(), Tags: tags.ToList(),
                    Views: views.ToList(), TrashCount: trashCount);
            }, ct).ConfigureAwait(true);

            if (_loadingStopped || generation != _contentLoadGeneration || ct.IsCancellationRequested ||
                !string.Equals(_collectionId, collectionId, StringComparison.Ordinal))
                return;

            _allNormal = content.Images;
            _normalIds = _allNormal.Select(image => image.Id).ToHashSet(StringComparer.Ordinal);
            _imageTags = content.ImageTags.GroupBy(it => it.ImageId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
            _tagById = content.Tags.ToDictionary(tag => tag.Id, StringComparer.Ordinal);
            _allViews = content.Views;
            _collectionCounts[collectionId] = _allNormal.Count;
            BuildEntries();
            Trash.SetCount(content.TrashCount);

            _loaded = true;
            _isContentLoading = false;
            if (_axis == "view" && _viewId is not null)
                await LoadViewAsync(_viewId).ConfigureAwait(true);
            else
                Recompute();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // collection切替または終了による正常な破棄。
        }
        catch (Exception)
        {
            if (_loadingStopped || generation != _contentLoadGeneration ||
                !string.Equals(_collectionId, collectionId, StringComparison.Ordinal)) return;
            _isContentLoading = false;
            _contentErrorKey = "view.imagesLoadFailed";
            _loaded = false;
            OnPropertyChanged(string.Empty);
        }
    }

    private void ClearContentData()
    {
        _allNormal = [];
        _normalIds = new HashSet<string>(StringComparer.Ordinal);
        _imageTags = new Dictionary<string, List<ImageTag>>(StringComparer.Ordinal);
        _entries = [];
        _entryById = new Dictionary<string, ImageEntry>(StringComparer.Ordinal); // ECO-113 R8 所見2: 旧コレクションの滞留防止
        _selected.Clear();
        _fsPath.Clear();
        _tagFilter = null;
        _expandTag = null;
    }

    private void CancelContentLoad(bool clearState)
    {
        Interlocked.Increment(ref _contentLoadGeneration);
        _contentLoadCts?.Cancel();
        _contentLoadCts?.Dispose();
        _contentLoadCts = null;
        if (clearState)
        {
            _isContentLoading = false;
            _contentErrorKey = null;
        }
    }

    /// <summary>window終了後に遅延load結果をUIへ反映しない。</summary>
    public void CancelLoading()
    {
        _loadingStopped = true;
        Interlocked.Increment(ref _catalogLoadGeneration);
        _catalogLoadCts?.Cancel();
        _catalogLoadCts?.Dispose();
        _catalogLoadCts = null;
        CancelContentLoad(clearState: true);
        Organize.InvalidateSearchContext();
        _isCatalogLoading = false;
    }

    /// <summary>
    /// タグ タブでのタグ/ビュー永続変更(作成・編集・削除)を画像タブへ反映する軽量再読込。
    /// InitializeAsync は重く状態復元(コレクション/表示モード)も伴うため、タブ切替毎の反映には
    /// こちらを使う。タグ台帳(_tagById)・ビュー(_allViews)・画像タグ紐付けを入れ替え、コレクション
    /// 選択・ナビ・表示モード・画像選択は保持する。view 軸の選択中ビューは保存済み階層から graph も
    /// 再構築する(ECO-096)。これが無いと _tagById が起動時のまま固定され、新規タグがタグ編集の候補に
    /// 出ず、階層設定は旧 _viewRoot のまま残る(private ReloadTagsAsync は画像↔タグ紐付けのみで
    /// 台帳を再取得しないため別物)。
    /// </summary>
    public async Task ReloadTagCatalogAsync()
    {
        if (!_loaded)
        {
            return;
        }

        _tagById = (await _tags.GetAllAsync().ConfigureAwait(true)).ToDictionary(t => t.Id, StringComparer.Ordinal);
        _allViews = (await _views.GetAllAsync().ConfigureAwait(true)).ToList();
        await RefreshImageTagsAsync().ConfigureAwait(true);
        BuildEntries();

        if (_axis == "view" && _viewId is { } activeViewId)
        {
            var activeView = _allViews.FirstOrDefault(v =>
                string.Equals(v.Id, activeViewId, StringComparison.Ordinal));
            if (activeView is null)
            {
                // 保存中に選択中 view 自体が削除された場合は、旧 graph を残さず FS root へ安全退避。
                _axis = "fs";
                _viewId = null;
                _viewRoot = null;
                _viewPath.Clear();
                Recompute();
                return;
            }

            await ReloadViewGraphAsync(activeView, preserveNavigation: true).ConfigureAwait(true);
            return;
        }

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
        if (_collectionId is null)
        {
            _imageTags = new Dictionary<string, List<ImageTag>>(StringComparer.Ordinal);
            return;
        }
        var all = await _tags.GetImageTagsByFolderAsync(_collectionId).ConfigureAwait(true);
        _imageTags = all.GroupBy(it => it.ImageId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
    }

    private void BuildEntries()
    {
        _entries = _allNormal
            .Where(r => _collectionId is not null && string.Equals(r.SyncFolderId, _collectionId, StringComparison.Ordinal))
            .Select(BuildEntry)
            .ToList();
        _entryById = _entries.ToDictionary(e => e.Record.Id, StringComparer.Ordinal); // ECO-113: O(1) 解決
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
        // ECO-060: スキャン中はsort条件を保持するだけで、取込順を固定する。
        // sort未設定で完了した場合も、完了遷移に伴う追加sortを行わず最終取込順を保つ。
        if (TryGetPreservedScanOrder(out var scanOrder))
        {
            var positions = scanOrder
                .Select((id, index) => (id, index))
                .ToDictionary(x => x.id, x => x.index, StringComparer.Ordinal);
            return files
                .Select((entry, index) => (entry, index))
                .OrderBy(x => positions.GetValueOrDefault(x.entry.Record.Id, int.MaxValue))
                .ThenBy(x => x.index)
                .Select(x => x.entry)
                .ToList();
        }

        // ECO-025 β/FL-003 v2: ソート対象=ビュー表示列・状態(sortCol/dir)は grid/list で共有。
        // 列ソート中は列比較器(空値末尾・型別・安定タイブレーク)。未ソートは既定順(名前昇順・決定的)。
        if (_sortColKey is { } key)
        {
            return ViewColumnSorter.Sort(files, ResolveSortColumn(key), _sortColDir).ToList();
        }

        var byId = files.ToDictionary(e => e.Record.Id, StringComparer.Ordinal);
        var sorted = _sorter.Sort(files.Select(e => e.Record), SortField.Name, SortDirection.Asc);
        return sorted.Select(r => byId[r.Id]).ToList();
    }

    /// <summary>
    /// ECO-070案A: FS軸はfolder群→image群を保ち、両群を個別sortする。
    /// folderには列値を新設せず名前だけを現在方向で比較する。scan中/完了後未sortはIMG-015の取込順を優先。
    /// </summary>
    private List<(string Name, int Count)> SortFolders(List<(string Name, int Count)> folders)
    {
        if (TryGetPreservedScanOrder(out _)) return folders;

        return _sortColKey is not null && _sortColDir == SortDirection.Desc
            ? folders.OrderByDescending(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList()
            : folders.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private bool TryGetPreservedScanOrder(out IReadOnlyList<string> scanOrder)
    {
        scanOrder = [];
        if (_collectionId is not { } collectionId) return false;
        if (_scanningCollections.Contains(collectionId))
        {
            if (!_scanOrderByCollection.TryGetValue(collectionId, out var activeOrder)) return false;
            scanOrder = activeOrder;
            return true;
        }
        if (_sortColKey is not null ||
            !_completedScanOrderByCollection.TryGetValue(collectionId, out var completedOrder)) return false;
        scanOrder = completedOrder;
        return true;
    }

    /// <summary>列 key(basic or タグ id)から比較列を解決(ECO-025 β)。タグ型は _tagById から。</summary>
    private ViewSortColumn ResolveSortColumn(string key) =>
        _tagById.TryGetValue(key, out var tag)
            ? ViewSortColumn.ForTag(key, tag.Type)
            : ViewSortColumn.ForBasic(key);

    /// <summary>
    /// リスト表示のヘッダー列と Grid 列テンプレートを構築(ECO-025 β)。
    /// 列 = アクティブビュー(view 軸のとき)の display_columns。FS 軸・未選択は既定 3 列。
    /// ソート中の列が現構成から消えたらソート解除(file_list「除去列がソート中ならソート解除」)。
    /// </summary>
    private void BuildListColumns()
    {
        var view = _axis == "view" && _viewId is not null
            ? _allViews.FirstOrDefault(v => string.Equals(v.Id, _viewId, StringComparison.Ordinal))
            : null;
        _listColumnDefs = ListColumnBuilder.Build(view?.DisplayColumns, _tagById, BasicColLabel);
        ColumnTemplate = ListColumnBuilder.ColumnTemplate(_listColumnDefs);

        // 除去列がソート中ならソート解除
        if (_sortColKey is { } sk && !_listColumnDefs.Any(c => string.Equals(c.Key, sk, StringComparison.Ordinal)))
        {
            _sortColKey = null;
        }

        var arrow = _sortColDir == SortDirection.Desc ? 180.0 : 0.0;

        // リスト: ヘッダー列(全列がソート入口)
        ListColumns.Clear();
        for (int i = 0; i < _listColumnDefs.Count; i++)
        {
            var d = _listColumnDefs[i];
            bool active = string.Equals(_sortColKey, d.Key, StringComparison.Ordinal);
            ListColumns.Add(new ListColumnHeaderVM(i, d.Key, d.Label, active, active ? arrow : 0));
        }

        // アイコン: 「並び替え」メニュー候補=同じ表示列(名前含む全列・種別チップ+色ドット・アクティブ強調+方向矢印)
        SortColumns.Clear();
        foreach (var d in _listColumnDefs)
        {
            bool active = string.Equals(_sortColKey, d.Key, StringComparison.Ordinal);
            SortColumns.Add(new SortOptionVM(
                d.Key, d.Label, _localization.T(ListColumnBuilder.KindChipKey(d.Kind)), d.Color, active, active ? arrow : 0, d.Kind));
        }

        if (_sortColKey is { } key)
        {
            var def = _listColumnDefs.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.Ordinal));
            ColumnSortLabel = _localization.T("view.columnSortLabel", new Dictionary<string, string> { ["column"] = def?.Label ?? key, ["direction"] = _localization.T(_sortColDir == SortDirection.Desc ? "view.descending" : "view.ascending") });
        }
        else
        {
            ColumnSortLabel = "";
        }
    }

    private string BasicColLabel(string key) => key switch
    {
        ViewColumnModel.NameKey => _localization.T("common.name"),
        ViewColumnModel.SizeKey => _localization.T("common.size"),
        ViewColumnModel.ModifiedDateKey => _localization.T("common.modifiedDate"),
        _ => key,
    };

    /// <summary>母集合列挙(全件評価+ソート)の累計実行回数 — ECO-113 の構造プローブ計器
    /// (ECO-058 方式=固定時間閾値でなく構造で pin)。選択クリック経路はこれを増やしてはならない
    /// (増える=選択コストが母集合サイズに比例する退行)。</summary>
    public int ContextEnumerationCount { get; private set; }

    private List<ImageEntry> AllLoadedImagesInContext()
    {
        ContextEnumerationCount++;
        // 編集モードの選択母集合・ビューアー順=現在の文脈で表示中の画像を**表示と同じソート順**で返す。
        // (SHIFT 範囲選択が表示順と一致する=歯抜け防止。連番/ビューアーの順序も表示順に一致する)
        if (_axis == "view" && _viewRoot is not null)
        {
            var fullPath = new List<GraphNode> { _viewRoot };
            fullPath.AddRange(_viewPath);
            var matched = ViewMatched(fullPath);
            // (ECO-084/REQ-094) 未分類モードでは母集合も表示中の集合に一致させる(子 matched を減算)
            if (_viewUnclassified)
                matched = Unclassified(matched, fullPath[^1].Children.Select(c => ViewMatched(Append(fullPath, c), matched)));
            return SortFiles(matched);
        }
        return SortFiles(ResolveFs().Files); // FS: 現在のフォルダに直接ある画像(サブフォルダは含めない)
    }

    /// <summary>
    /// (ECO-084/REQ-094) 最深配置の減算: matched から「直下の子のいずれかにマッチする画像」を引く。
    /// 直下の子だけで十分(子孫の条件列は子の条件列の上位集合 — 仕様 §2.4 REQ-094)。
    /// </summary>
    private static List<ImageEntry> Unclassified(List<ImageEntry> matched, IEnumerable<List<ImageEntry>> childMatched)
    {
        var classified = new HashSet<string>(
            childMatched.SelectMany(m => m).Select(e => e.Record.Id), StringComparer.Ordinal);
        return classified.Count == 0 ? matched : matched.Where(e => !classified.Contains(e.Record.Id)).ToList();
    }

    private static List<GraphNode> Append(List<GraphNode> path, GraphNode child)
    {
        var list = new List<GraphNode>(path) { child };
        return list;
    }

    /// <summary>
    /// ビュー条件 + ノードパス条件で対象集合を絞り込む(OC-1/OC-3 再利用)。
    /// (ECO-026/#3) <paramref name="within"/> を渡すとその集合内だけで評価する。子ノードの matched は
    /// 親ノードの matched の部分集合(子条件は親条件へ AND 追加)のため、子件数は親 matched(files)内で
    /// 評価しても同一で、入力が全 _entries から現ノード集合へ縮む(子数×全件評価を回避)。
    /// </summary>
    private List<ImageEntry> ViewMatched(IReadOnlyList<GraphNode> fullPath, IReadOnlyList<ImageEntry>? within = null)
    {
        var source = within ?? _entries;
        var conds = new List<ViewCondition>(_viewConditions);
        conds.AddRange(_pathConverter.BuildConditions(fullPath));
        if (conds.Count == 0) return source.ToList();
        var res = _evaluator.Evaluate(source.Select(e => e.ToImageWithTags()), conds);
        return source.Where(e => res.MatchedImageIds.Contains(e.Record.Id)).ToList();
    }

    // =====================================================================
    //  公開: 派生コレクション + スカラー(ImageTabView がバインド)
    // =====================================================================
    public ObservableCollection<CollectionRowVM> Collections { get; } = new();
    public ObservableCollection<AxisOptionVM> AxisOptions { get; } = new();
    public ObservableCollection<CrumbVM> Crumbs { get; } = new();
    public ObservableCollection<ChipVM> Chips { get; } = new();

    /// <summary>チップ行の容量・overflow 状態(ECO-091・VC-IMG-9/10。作業タブと同一意味論=ECO-090 同期宣言)。</summary>
    public ChipStripViewModel ChipStrip { get; }
    public ObservableCollection<ImageItemVM> Items { get; } = new();
    // ECO-025 β: リスト表示のヘッダー列(アクティブビューの display_columns 由来)+ Grid 列テンプレート(ヘッダーと各行が同一値で整列)
    public ObservableCollection<ListColumnHeaderVM> ListColumns { get; } = new();
    /// <summary>アイコン(グリッド)の「並び替え」メニュー候補=アクティブビューの表示列(ECO-025 β/FL-003 v2)。</summary>
    public ObservableCollection<SortOptionVM> SortColumns { get; } = new();
    public string ColumnTemplate { get; private set; } = "1.7*,120,150";
    /// <summary>列ヘッダーソート中か(ソート概要+クリアの表示・ECO-025 β)。</summary>
    public bool IsColumnSorted => _sortColKey is not null;
    /// <summary>ソート概要ラベル「<列名>（昇順/降順）」(ECO-025 β)。</summary>
    public string ColumnSortLabel { get; private set; } = "";
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
    public bool IsCatalogLoading => _isCatalogLoading;
    public bool IsContentLoading => _isContentLoading;
    public bool HasCatalogError => _catalogErrorKey is not null;
    public bool HasContentError => _contentErrorKey is not null;
    public string CatalogErrorMessage => _catalogErrorKey is { } ck ? _localization.T(ck) : "";
    public string ContentErrorMessage => _contentErrorKey is { } ek ? _localization.T(ek) : "";
    public bool ShowCatalogLoading => _isCatalogLoading && !HasCatalogError;
    public bool ShowContentLoading => _catalogLoaded && _collectionId is not null && _isContentLoading && !HasContentError;
    /// <summary>未選択時に中央へ「コレクションを選択」プロンプトを出す(REQ-053)。</summary>
    public bool ShowCollectionPrompt => _catalogLoaded && !_isCatalogLoading && !HasCatalogError && _collectionId is null;
    /// <summary>グリッドペイン表示(コレクション選択済み かつ グリッドモード)。</summary>
    public bool ShowGridPane => _loaded && !IsContentLoading && !HasContentError && IsCollectionSelected && IsGrid;
    /// <summary>リストペイン表示(コレクション選択済み かつ リストモード)。</summary>
    public bool ShowListPane => _loaded && !IsContentLoading && !HasContentError && IsCollectionSelected && IsList;
    /// <summary>画像 0 件の空状態(コレクション選択済みのときのみ・未選択はプロンプトを優先。
    /// 未分類 0 件は専用の ShowUnclassifiedEmpty を優先する — ECO-084/REQ-094)。</summary>
    public bool ShowEmptyMessage => _loaded && !IsContentLoading && !HasContentError && IsCollectionSelected && Items.Count == 0 && !ShowUnclassifiedEmpty;
    /// <summary>ECO-060: 選択中collectionのbackground scan状態。</summary>
    public bool IsSelectedCollectionScanning =>
        _collectionId is not null && _scanningCollections.Contains(_collectionId);
    public string? ScanNotice => _scanNoticeKey is { } sk ? _localization.T(sk) : null;
    public bool HasScanNotice => _scanNoticeKey is not null;

    public bool IsViewAxis => _axis == "view";
    public bool IsFsActive => _axis == "fs";
    // ---- 表示モード(ECO-084/REQ-094): ビュー軸のみ表示。セグメントは狭幅収納の対象外(CAD image_tab.md 契約) ----
    public bool ShowDisplayModeToggle => _axis == "view" && _viewRoot is not null;
    public bool IsAllDisplayMode => !_viewUnclassified;
    public bool IsUnclassifiedDisplayMode => _viewUnclassified;
    /// <summary>未分類 0 件の専用空状態(REQ-094。汎用 ShowEmptyMessage とは区別して出す)。</summary>
    public bool ShowUnclassifiedEmpty { get; private set; }
    public string AxisLabel => _axis == "fs" ? _localization.T("collection.fileSystem") : _viewLabel;
    public bool AxisMenuOpen { get; private set; }
    public bool SortMenuOpen { get; private set; }
    public bool MoreMenuOpen { get; private set; }
    /// <summary>⋯ メンテナンス(トラッシュ/修復)はコレクションスコープ。未選択時は無効(REQ-053)。</summary>
    public bool CanOpenMaintenance => _collectionId is not null;
    // ---- ソート(ECO-025 β/FL-003 v2: リスト=ヘッダー+チップ / アイコン=並び替えメニュー・対象=表示列・状態共有) ----
    /// <summary>ソート要約チップを出すか。ソート中は list/grid とも表示(モック=isSorted・✕ でクリア)。アイコンでも唯一のクリア手段。</summary>
    public bool ShowSortChip => _sortColKey is not null;
    /// <summary>ソート方向矢印角(降順=180)。チップ・ヘッダー・メニュー共通。</summary>
    public double ColumnSortArrowAngle => _sortColDir == SortDirection.Desc ? 180 : 0;
    /// <summary>アイコンの「並び替え」ボタンのバッジ=現在のソート列名(未ソート=「なし」)。</summary>
    public string SortButtonBadge => _sortColKey is null
        ? _localization.T("view.sortNone")
        : (_listColumnDefs.FirstOrDefault(c => string.Equals(c.Key, _sortColKey, StringComparison.Ordinal))?.Label ?? _sortColKey);
    /// <summary>並び替えメニュー下部の 昇順/降順 セグメント。</summary>
    public bool SortAscActive => _sortColDir == SortDirection.Asc;
    public bool SortDescActive => _sortColDir == SortDirection.Desc;

    // ---- 表示列ポップオーバー(ECO-025 β-2・FL/VE-003: ライブ編集でビュー定義へ書き戻し) ----
    /// <summary>「表示列」ボタンの活性=アクティブビューがある(view 軸+ビュー選択)。FS 軸は書き戻し先が無いので隠す。</summary>
    public bool CanEditColumns => _axis == "view" && _viewId is not null;
    /// <summary>ツールバー上の「表示列」入口の可視。CAD(image_tab.md「ツールバー」)は文脈モード中に残す項目を
    /// 「表示軸・ソート・グリッド/リストと当該モードの終了」だけと定めるため、モード中は表示列も隠す(IMG-014
    /// 実機所見: モード中の表示列が狭幅で右クラスタへ潜り込む逸脱を解消)。列編集はモードを抜けてから行う。</summary>
    public bool ShowColumnsEntry => CanEditColumns && !InAnyMode;
    /// <summary>表示列ポップオーバーの開閉。</summary>
    public bool ColumnPickerOpen { get; private set; }
    /// <summary>ポップオーバーが host する列ピッカー(開くたびにアクティブビューから生成)。</summary>
    public ColumnPickerViewModel? ColumnPicker { get; private set; }
    public bool EditMode => _editMode;
    public string EditButtonLabel => _editMode ? _localization.T("toolbar.tagEditExit") : _localization.T("toolbar.tagEdit");
    public bool HomeActive { get; private set; }
    public string CountLabel { get; private set; } = "";
    public bool ShowChips { get; private set; }
    public bool ShowChipHint { get; private set; }
    public string ChipHintLabel { get; private set; } = "";
    public bool ShowEmptyTagNote { get; private set; }
    public bool PanelEmpty => _editMode && _selected.Count == 0;
    public bool PanelActive => _editMode && _selected.Count > 0;
    public bool HasSelection => _selected.Count > 0;
    public string SelectionLabel => _localization.T("view.selectedCount", new Dictionary<string, string> { ["count"] = _selected.Count.ToString() });
    public bool OnCurrentTab => _panelTab == "current";
    public bool OnAddTab => _panelTab == "add";
    public bool HasCurrentTags => CurrentTags.Count > 0;
    public bool NoCurrentTags => CurrentTags.Count == 0;
    public string CurrentNote { get; private set; } = "";
    public string NoCurrentLabel { get; private set; } = "";

    private string _addQuery = "";
    /// <summary>
    /// ECO-041: タグ追加の検索(mock addQuery 準拠)。trim・大文字小文字無視の部分一致で
    /// 種別グループ内を絞り込む(判定は BuildAddGroups)。入力即時反映・モード切替でも保持(mock 準拠)。
    /// </summary>
    public string AddQuery
    {
        get => _addQuery;
        set
        {
            if (_addQuery == value) return;
            _addQuery = value;
            OnPropertyChanged();
            BuildContextPanels(new HashSet<string>(_selected)); // タグ編集パネルのみ部分再構築(Items 不変)
        }
    }

    // ---- 整理モード(ECO-014)公開契約 ----
    public bool OrganizeMode => _organizeMode;
    public string OrganizeButtonLabel => _organizeMode ? _localization.T("toolbar.organizeExit") : _localization.T("toolbar.organize");
    /// <summary>いずれかの文脈モード中(タグ編集 or 整理 or 作業 or 削除 or ファイル操作)。モード中は他モード入口・⋯ を隠す(集中・排他可視化・幅)。ECO-017/018/112 で作業・削除・ファイル操作へ拡張。</summary>
    public bool InAnyMode => _editMode || _organizeMode || _workMode || _deleteMode || _fileOpsMode;

    // ---- 作業モード(ECO-017)公開契約 ----
    /// <summary>選択を有効化するモード(タグ編集 or 作業 or 削除 or ファイル操作)。グリッドの選択視覚=チェック/選択順バッジを出す
    /// (ファイル操作のみ番号バッジなしの白✓=VC-IMG-13・ECO-112)。</summary>
    public bool InSelectMode => _editMode || _workMode || _deleteMode || _fileOpsMode;
    public bool WorkMode => _workMode;
    public string WorkButtonLabel => _workMode ? _localization.T("toolbar.workExit") : _localization.T("navigation.work");
    /// <summary>作業モード中に選択がある=「追加」が活性。</summary>
    public bool HasWorkSelection => _workMode && _selected.Count > 0;
    public int WorkSelCount => _selected.Count;
    /// <summary>「追加」ボタンの活性(=選択あり)。</summary>
    public bool CanAddToWork => HasWorkSelection;
    /// <summary>作業対象が1件以上ある=「作業対象 N 枚」チップを出す。</summary>
    public bool HasWorkTargets => _workMode && Work.Count > 0;
    public string WorkTargetLabel => _localization.T("view.workTargetCount", new Dictionary<string, string> { ["count"] = Work.Count.ToString() });

    // ---- ツールバー モード入口の出し分け(ECO-017/018: 排他隠し統一) ----
    // 各モード入口は他モードの最中は隠れる(自モード中は「終了」として残る)。削除は ⋯ メニューから入る
    // ためツールバー入口を持たず、削除中は全入口・⋯ が隠れて「削除を終了」+「ゴミ箱へ移動」のみ残る。
    public bool ShowEditEntry => !_organizeMode && !_workMode && !_deleteMode && !_fileOpsMode;
    public bool ShowOrganizeEntry => !_editMode && !_workMode && !_deleteMode && !_fileOpsMode;
    public bool ShowWorkEntry => !_editMode && !_organizeMode && !_deleteMode && !_fileOpsMode;

    // ---- ツールバー狭幅レスポンシブ収納(IMG-014) ----
    // 判定はビューポート幅でなく「ツールバー実測幅」(content 幅=Border 幅−水平パディング)。左ペイン折り畳み
    // (276/64)・右ペイン開閉で使える幅が変わるため CSS メディアクエリ相当(ウィンドウ幅)では判定できない。
    // View が SizeChanged/初期シードで ReportToolbarWidth に content 幅を供給する。段階(モック権威):
    //   通常 → ラベル畳み(<820: 入口ボタンをアイコン化) → 「整理」を⋯へ退避(<640) → flex-wrap 折り返し(XAML)。
    // しきい値 820/640 はモック由来の目安で調整可。確定契約は px 値でなく ①重ならない ②畳む順序 ③離脱/実行ラベル維持。
    private double _toolbarWidth = double.PositiveInfinity; // seed 前は「広い」扱い=畳まない(初期フラッシュ防止)
    private bool _labelsCollapsed;
    private bool _organizeStowed;

    private const double LabelCollapseWidth = 820; // 入口ボタンをアイコンのみへ畳む(目安)
    private const double OrganizeStowWidth = 640;   // 「整理」を⋯メニューへ退避(目安)
    private const double HysteresisBand = 24;       // しきい値近傍のばたつき防止(戻すのは +band 超過時)
    private const double WidthEpsilon = 2;          // 微小変化(数px)は無視

    /// <summary>
    /// View がツールバー content 幅(水平パディング控除済み・実測)を報告する。ヒステリシス帯で段階フラグを更新し、
    /// 段階が変わったときだけ通知する。微小変化(&lt; WidthEpsilon)は無視してばたつきを避ける。
    /// </summary>
    public void ReportToolbarWidth(double contentWidth)
    {
        if (double.IsNaN(contentWidth) || contentWidth <= 0) return;
        if (Math.Abs(contentWidth - _toolbarWidth) < WidthEpsilon) return;
        _toolbarWidth = contentWidth;

        var labels = _labelsCollapsed;
        if (!labels && contentWidth < LabelCollapseWidth) labels = true;
        else if (labels && contentWidth > LabelCollapseWidth + HysteresisBand) labels = false;

        var stow = _organizeStowed;
        if (!stow && contentWidth < OrganizeStowWidth) stow = true;
        else if (stow && contentWidth > OrganizeStowWidth + HysteresisBand) stow = false;

        if (labels == _labelsCollapsed && stow == _organizeStowed) return; // 段階不変=再描画不要
        _labelsCollapsed = labels;
        _organizeStowed = stow;
        OnPropertyChanged(string.Empty);
    }

    /// <summary>入口ボタン(タグ編集・整理・作業)のラベルをアイコンのみへ畳む。通常閲覧時のみ効く
    /// (モード中の可視ボタンは「終了/実行」=離脱/実行導線なのでラベル維持=契約③)。</summary>
    public bool CollapseEntryLabels => _labelsCollapsed && !InAnyMode;
    /// <summary>「整理」入口を⋯メニューへ退避する段階。通常閲覧時のみ(整理モード中の「整理を終了」は退避しない)。</summary>
    public bool StowOrganizeToMenu => _organizeStowed && !InAnyMode;
    /// <summary>ツールバー上の「整理」入口ボタンの可視。退避中(StowOrganizeToMenu)は隠して⋯へ移す。</summary>
    public bool ShowOrganizeEntryButton => ShowOrganizeEntry && !StowOrganizeToMenu;

    // ---- tier3 回り込み(mock flex-wrap 相当) ----
    // 段階収納(ラベル畳み/退避)で吸収しきれない狭幅(特にモード中=畳みが効かず右ペインで中央が狭い)では、
    // 右クラスタ(ソート+グリッド/リスト)を2段目へ回り込ませて左右が横方向を共有しないようにする(重なり原理排除)。
    // 判定は View が左右クラスタの実測希望幅の合算 vs 使える幅で行い SetToolbarWrapped で反映する。
    private bool _toolbarWrapped;
    /// <summary>回り込み中(右クラスタが2段目)。Grid 配置(下記スパン/行列)を切り替える。</summary>
    public bool ToolbarWrapped => _toolbarWrapped;
    /// <summary>左クラスタの列スパン: 回り込み時は全幅(2)。</summary>
    public int LeftClusterColumnSpan => _toolbarWrapped ? 2 : 1;
    /// <summary>右クラスタ行: 広い時 row0(左と同段)、回り込み時 row1(下段)。</summary>
    public int RightClusterRow => _toolbarWrapped ? 1 : 0;
    /// <summary>右クラスタ列: 広い時 col1(右寄せ)、回り込み時 col0 全幅(右寄せ維持)。</summary>
    public int RightClusterColumn => _toolbarWrapped ? 0 : 1;
    public int RightClusterColumnSpan => _toolbarWrapped ? 2 : 1;

    /// <summary>View が実測(左右クラスタ希望幅の合算 vs 使える幅・ヒステリシス)で回り込み状態を設定する。変化時のみ通知。</summary>
    public void SetToolbarWrapped(bool wrapped)
    {
        if (wrapped == _toolbarWrapped) return;
        _toolbarWrapped = wrapped;
        OnPropertyChanged(string.Empty);
    }

    // ---- 削除モード(ECO-018)公開契約 ----
    public bool DeleteMode => _deleteMode;
    /// <summary>削除モード中に選択がある=「ゴミ箱へ移動」が活性。</summary>
    public bool HasDeleteSelection => _deleteMode && _selected.Count > 0;
    public int DeleteSelCount => _selected.Count;
    public bool CanDeleteToTrash => HasDeleteSelection;

    // ---- ファイル操作モード(ECO-112)公開契約 ----
    public bool FileOpsMode => _fileOpsMode;
    public int FileOpsSelCount => _selected.Count;
    /// <summary>「パスをコピー」の可視(VC-IMG-12: 選択 1 件以上)。</summary>
    public bool ShowCopyPaths => _fileOpsMode && _selected.Count >= 1;
    /// <summary>「ファイルの場所を開く」の可視(VC-IMG-12: 選択 1 件のときだけ)。</summary>
    public bool ShowOpenLocation => _fileOpsMode && _selected.Count == 1;
    /// <summary>コピー完了フィードバック(IMG-026②: ボタン内一時表示)中か。</summary>
    public bool CopyFeedbackActive => _copyFeedback;
    /// <summary>「パスをコピー」ボタンのラベル(フィードバック中は「コピーしました ✓」・表示時解決=ECO-106)。</summary>
    public string CopyPathsLabel => _copyFeedback
        ? _localization.T("toolbar.copyPathsDone")
        : _localization.T("toolbar.copyPaths");

    // ---- ⋯「ゴミ箱」バッジ・ゴミ箱ポップアップ(ECO-018/ECO-019)は Trash 子 VM へ移送(ECO-036 第1段)。
    //      状態・ロジックは Trash が所有。以下は既存テスト(CpUiG1TrashPopupTests 等・変更禁止)の
    //      旧公開契約を保つための委譲(CHEAT-E36S1 参照・cheat-report 記録済み)。XAML は Trash.* を直接参照する。
    public bool HasTrash => Trash.HasTrash;
    public int TrashCount => Trash.TrashCount;
    public bool TrashOpen => Trash.TrashOpen;
    public ObservableCollection<TrashPopupItemVM> TrashPopupItems => Trash.TrashPopupItems;
    public int TrashPopupCount => Trash.TrashPopupCount;
    public bool HasTrashItems => Trash.HasTrashItems;
    public bool TrashPopupEmpty => Trash.TrashPopupEmpty;
    public bool HasTrashSel => Trash.HasTrashSel;
    public int TrashSelCount => Trash.TrashSelCount;
    public string TrashSelCountLabel => Trash.TrashSelCountLabel;
    public string TrashSelectAllLabel => Trash.TrashSelectAllLabel;
    public bool CanRestoreTrash => Trash.CanRestoreTrash;
    public bool CanPurgeTrash => Trash.CanPurgeTrash;
    public IAsyncRelayCommand OpenTrashCommand => Trash.OpenTrashCommand;
    public IRelayCommand CloseTrashCommand => Trash.CloseTrashCommand;
    public IRelayCommand<TrashPopupItemVM> ToggleTrashItemCommand => Trash.ToggleTrashItemCommand;
    public IRelayCommand ToggleTrashSelectAllCommand => Trash.ToggleTrashSelectAllCommand;
    public IAsyncRelayCommand RestoreSelectedTrashCommand => Trash.RestoreSelectedTrashCommand;
    public IAsyncRelayCommand PurgeSelectedTrashCommand => Trash.PurgeSelectedTrashCommand;
    public IAsyncRelayCommand EmptyTrashCommand => Trash.EmptyTrashCommand;

    /// <summary>右ペインの文脈モード(タグ編集 / 整理)は排他。どちらかなら右ペインを出す。</summary>
    public bool ShowRightPane => _editMode || _organizeMode;
    public bool IsTagEditContext => _editMode;
    public bool IsOrganizeContext => _organizeMode;

    // マージ先(残す1枚)。状態(_mergeTargetId)は Organize 子 VM 所有(ECO-036 第3段)— ホストは _organizeMode との合成のみ。
    public bool HasMergeTarget => Organize.HasMergeTarget;
    public OrganizeSlotVM? MergeTarget { get; private set; }
    /// <summary>マージ先未設定: グリッドで残したい1枚を選ぶよう促す。</summary>
    public bool ShowMergeTargetPrompt => _organizeMode && !Organize.HasMergeTarget;

    // 整理対象(統合し削除対象)
    public bool HasOrganizeTargets => Organize.HasOrganizeTargets;
    /// <summary>マージ先はあるが整理対象が空: まとめる相手がない。</summary>
    public bool ShowOrganizeTargetsPrompt => _organizeMode && Organize.HasMergeTarget && !Organize.HasOrganizeTargets;
    public string OrganizeTargetsCountLabel => Organize.OrganizeTargetsCountLabel;

    // タグ統合トグルは ECO-044(IMG-011 裁定②)で撤去 — タグ union は常時 ON(選択肢ではない)。

    // 似た画像を探す
    public bool IsSimilarMethod => Organize.IsSimilarMethod;
    public bool IsCriteriaMethod => Organize.IsCriteriaMethod;
    /// <summary>類似度しきい値(%)。1枚から探す=マージ先起点。</summary>
    public int SimilarThreshold
    {
        get => Organize.SimilarThreshold;
        // 旧 setter と同一のホスト通知(自プロパティ+ラベル)を保存 — スライダー操作でラベルが追従する挙動の要
        set { Organize.SimilarThreshold = value; OnPropertyChanged(); OnPropertyChanged(nameof(SimilarThresholdLabel)); }
    }
    public string SimilarThresholdLabel => Organize.SimilarThresholdLabel;
    /// <summary>類似検索はマージ先(基準画像)が必要。</summary>
    public bool CanRunSimilar => Organize.CanRunSimilar;
    // ECO-055: 条件検索=マージ先との属性一致トグル(自由入力 2 欄は撤去・裁定②a)。
    // 転送セッターはホスト側で全通知する(GF-055-01: 子 VM の通知はホストにバインドされた XAML へ
    // 届かず CanRunSearch が固まる — ECO-038「転送殻の通知漏れ」同型。CR-6 全通知の先例に従う)
    public bool CondHash { get => Organize.CondHash; set { Organize.CondHash = value; OnPropertyChanged(string.Empty); } }
    public bool CondExt { get => Organize.CondExt; set { Organize.CondExt = value; OnPropertyChanged(string.Empty); } }
    public bool CondSize { get => Organize.CondSize; set { Organize.CondSize = value; OnPropertyChanged(string.Empty); } }
    public bool CondName { get => Organize.CondName; set { Organize.CondName = value; OnPropertyChanged(string.Empty); } }
    public bool CondDate { get => Organize.CondDate; set { Organize.CondDate = value; OnPropertyChanged(string.Empty); } }
    public bool Searching => Organize.Searching;
    public bool SearchPreparing => Organize.SearchPreparing;
    public bool SearchComparing => Organize.SearchComparing;
    public bool SearchCancelling => Organize.SearchCancelling;
    public bool ShowSearchProgress => Organize.ShowSearchProgress;
    public bool ShowSearchSettings => Organize.ShowSearchSettings;
    public bool ShowStartSearch => Organize.ShowStartSearch;
    public bool ShowCancelSearch => Organize.ShowCancelSearch;
    public bool CanCancelSearch => Organize.CanCancelSearch;
    public string SearchCancelButtonLabel => Organize.SearchCancelButtonLabel;
    public string SearchProgressLabel => Organize.SearchProgressLabel;
    public double SearchProgressValue => Organize.SearchProgressValue;
    public bool SearchProgressIndeterminate => Organize.SearchProgressIndeterminate;
    /// <summary>検索結果表示(中央ペインを候補一覧へ切替)。完了状態では出さない。</summary>
    public bool ShowSearchResults => _organizeMode && Organize.HasSearched && !Organize.OrganizeDone;
    public bool NoSearchResults => ShowSearchResults && SearchResults.Count == 0;
    /// <summary>検索実行可否: 条件検索は常に / 類似はマージ先(基準)が要る。</summary>
    public bool SimilarSearchBlocked => IsSimilarMethod && IsSelectedCollectionScanning;
    /// <summary>意味上の検索実行可否。スキャン中の類似方式はfalse。</summary>
    public bool CanRunSearch => Organize.CanRunSearch && !SimilarSearchBlocked;
    /// <summary>scan gate中も理由表示のclickを受けるため、ボタン自体は基礎条件だけで活性化する。</summary>
    public bool CanInvokeSearch => Organize.CanRunSearch;
    /// <summary>中央ブラウズグリッド: 検索結果表示中は譲る(整理モードでもグリッドで対象を選ぶため出す)。</summary>
    public bool ShowBrowseGrid => ShowGridPane && !ShowSearchResults;
    public bool ShowBrowseList => ShowListPane && !ShowSearchResults;
    // ECO-056(v2 3 ゾーン): 下部ピンの検索パネル開閉+検索結果ヘッダ(グリッドへ/件数/方式)
    public bool SearchOpen => Organize.SearchOpen;
    public string SearchMethodLabel => Organize.SearchMethodLabel;
    public string SearchResultsSubLabel => _localization.T("view.similarToTargetCount", new Dictionary<string, string> { ["name"] = MergeTarget?.Name ?? "", ["count"] = SearchResults.Count.ToString() });

    // 実行・完了
    public bool CanExecuteMerge => Organize.CanExecuteMerge;
    public string MergeButtonLabel => Organize.MergeButtonLabel;
    // ECO-056(v2 モック): 実行不可の理由注記(下部ピン=上のヒントが見えない場面で有効)
    public bool ShowMergeBlockedNote => _organizeMode && Organize.ShowMergeBlockedNote;
    public string MergeBlockedNote => Organize.MergeBlockedNote;
    public bool OrganizeDone => Organize.OrganizeDone;
    public string DoneSummary => Organize.DoneSummary;
    /// <summary>取り消し(ECO-044/IMG-011 裁定③): ログに基づく補償 Undo の実行可否。</summary>
    public bool CanUndo => Organize.CanUndo;
    /// <summary>取り消し不可時の理由(完了パネルに表示)。</summary>
    public string? UndoNote => Organize.UndoNote;
    public bool HasUndoNote => Organize.HasUndoNote;

    // =====================================================================
    //  Recompute
    // =====================================================================
    /// <summary>view 軸のチップ行を件数キャッシュ(_viewChipCounts)から構築(Recompute から抽出・ECO-118)。</summary>
    private void BuildViewChips()
    {
        Chips.Clear();
        _currentChildren.Clear();
        int ci = 0;
        foreach (var (child, matchedCount, unclassifiedCount) in _viewChipCounts)
        {
            int count = _viewUnclassified ? unclassifiedCount : matchedCount;
            // (REQ-096 裁定 d) 「0件の値ノードを隠す」: 表示のみスキップ(構築・意味論は不変)
            if (child.IsDefinedExpansion && child.HideEmptyValues && matchedCount == 0)
            {
                continue;
            }

            var key = "vc" + ci++;
            _currentChildren[key] = child;
            var color = TagColor(child.TagId is { } tid ? _tagById.GetValueOrDefault(tid) : null);
            // (REQ-095/096・CAD VC-IMG-6) 未定義値=琥珀破線+バッジ / 0 件の定義値=淡色で表示維持
            Chips.Add(child.IsUndefinedValue
                ? ChipVM.Undefined(key, child.DisplayName, count, _localization.T("view.undefinedBadge"))
                : child.IsDefinedExpansion && count == 0
                    ? ChipVM.ColoredZero(key, child.DisplayName, color, isNav: true)
                    : ChipVM.Colored(key, child.DisplayName, color, count, active: false, isNav: true));
        }
        if (Chips.Count > 0) { ShowChips = true; ShowChipHint = true; ChipHintLabel = _localization.T("view.drillHierarchy"); }
    }

    /// <summary>FS 軸のチップ行を件数キャッシュ(_fsChipCounts)から構築(Recompute から抽出・ECO-118)。</summary>
    private void BuildFsChips()
    {
        Chips.Clear();
        ShowChips = true; ShowChipHint = true; ChipHintLabel = _localization.T("view.filterByTag");
        Chips.Add(ChipVM.Neutral(_localization.T("common.clear"), _tagFilter is null));
        foreach (var (tid, count) in _fsChipCounts.OrderBy(c => c.Key, StringComparer.Ordinal))
        {
            if (!_tagById.TryGetValue(tid, out var def)) continue;
            Chips.Add(ChipVM.Colored(tid, def.Name, TagColor(def), count, _tagFilter == tid, isNav: false));
        }
    }

    /// <summary>entry 1 件がノード経路の全条件に一致するか(ECO-118 差分評価用)。</summary>
    private bool MatchesPath(IReadOnlyList<GraphNode> path, ImageEntry e) => ViewMatched(path, new[] { e }).Count > 0;

    private void Recompute()
    {
        // ECO-112/IMG-026②: 母集合・文脈の再計算はコピー完了フィードバックの解除遷移(ECO-104 教訓=タイマ以外を全列挙。
        // ナビゲーション/軸切替/言語切替/スキャン更新のいずれでも 2 秒の一時表示を持ち越さない)
        ClearCopyFeedback();
        // ECO-064: catalogはcontentより先に公開する。content未readyでもcollection行だけは描画する。
        Collections.Clear();
        foreach (var collection in _collections)
            Collections.Add(new CollectionRowVM(collection.Id, collection.Name, collection.Path,
                _collectionCounts.GetValueOrDefault(collection.Id), collection.Id == _collectionId,
                _scanningCollections.Contains(collection.Id)));
        if (!_loaded)
        {
            Items.Clear();
            _itemById.Clear(); _matchedIndexById.Clear(); _markedItemIds.Clear(); // ECO-113: Items と同期
            Crumbs.Clear();
            Chips.Clear();
            AxisOptions.Clear();
            CountLabel = "";
            OnPropertyChanged(string.Empty);
            return;
        }

        // 文脈モード中は「表示列」入口を隠す(ShowColumnsEntry)ため、開いていた列ピッカーは閉じておく
        // (プレースメント対象が消えたポップアップの浮き残り防止・IMG-014)。
        if (InAnyMode) ColumnPickerOpen = false;

        // ---- AxisOptions(FS + 保存ビュー) ----
        AxisOptions.Clear();
        AxisOptions.Add(new AxisOptionVM("fs", _localization.T("collection.fileSystem"), _localization.T("view.fsAxisDesc"), isView: false, _axis == "fs"));
        foreach (var v in _allViews)
            AxisOptions.Add(new AxisOptionVM(v.Id, v.Name, _localization.T("view.tagViewDesc"), isView: true, _axis == "view" && _viewId == v.Id));

        var folders = new List<(string Name, int Count)>();
        List<ImageEntry> files;
        List<string> crumbNames;
        Chips.Clear();
        ShowChips = false; ShowChipHint = false; ShowEmptyTagNote = false; ShowUnclassifiedEmpty = false;
        _currentChildren.Clear();

        if (_axis == "view" && _viewRoot is not null)
        {
            // ---- view 軸(M3b): ノード階層をチップで潜り・パンくずで戻る ----
            var fullPath = new List<GraphNode> { _viewRoot };
            fullPath.AddRange(_viewPath);
            var current = fullPath[^1];
            // (ECO-026/#3) 子 matched は現ノードの matched 内で評価=全 _entries を子数ぶん再評価しない。
            // (ECO-084/REQ-094) 子 matched は件数表示と未分類モードの減算の両方に使うため先に確保する。
            var allMatched = ViewMatched(fullPath);
            var childMatched = new List<(GraphNode Child, List<ImageEntry> Matched)>();
            foreach (var child in current.Children)
                childMatched.Add((child, ViewMatched(Append(fullPath, child), allMatched)));
            files = SortFiles(_viewUnclassified
                ? Unclassified(allMatched, childMatched.Select(c => c.Matched))
                : allMatched);
            ShowUnclassifiedEmpty = _viewUnclassified && files.Count == 0; // 専用空状態(REQ-094)
            crumbNames = _viewPath.Select(n => n.DisplayName).ToList();
            HomeActive = _viewPath.Count == 0;

            // ECO-118: 子ごとの件数をキャッシュしてからチップ VM を構築(差分更新と共有)
            _viewChipCounts.Clear();
            foreach (var (child, matched) in childMatched)
            {
                // (REQ-094) 未分類モードの件数はその子自身の未分類件数(孫 matched を減算)=表示とチップの数字を一致させる
                int uncls = _viewUnclassified
                    ? Unclassified(matched, child.Children.Select(g => ViewMatched(Append(Append(fullPath, child), g), matched))).Count
                    : 0;
                _viewChipCounts.Add((child, matched.Count, uncls));
            }
            BuildViewChips();
        }
        else
        {
            // ---- FS 軸 ----
            var ctx = ResolveFs();
            files = SortFiles(ctx.Files);
            folders = SortFolders(ctx.Folders);
            crumbNames = _fsPath.ToList();
            HomeActive = _fsPath.Count == 0;
            // ECO-118: 現スコープのタグ別件数をキャッシュしてからチップ VM を構築(差分更新と共有)
            _fsChipCounts.Clear();
            foreach (var (tid, count) in ctx.Chips) _fsChipCounts[tid] = count;
            if (ctx.AnyTagged)
            {
                BuildFsChips();
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
        CountLabel = _localization.T("view.itemCountLabel", new Dictionary<string, string> { ["count"] = (folders.Count + files.Count).ToString() });

        // ---- リスト列(ECO-025 β: アクティブビューの display_columns → ヘッダー列 + Grid テンプレート) ----
        BuildListColumns();

        // ---- items(ECO-026/#5: matched をキャッシュし列編集で BuildItemsFromMatched を再利用) ----
        _matchedFolders = folders;
        _matchedFiles = files;
        BuildItemsFromMatched();

        // ---- 編集パネル / 整理トレイ(選択依存・小コレクション)----
        BuildContextPanels(new HashSet<string>(_selected));

        OnPropertyChanged(string.Empty);
    }

    /// <summary>
    /// キャッシュした matched(<see cref="_matchedFolders"/>/<see cref="_matchedFiles"/>)から Items を作り直す。
    /// 列/セル/ソート項目のみに依存し、ビューグラフ評価・チップ・フォルダ算出は伴わない(ECO-026/#5 で再利用)。
    /// _matchedFiles は呼び出し前に表示順(SortFiles)であること。
    /// </summary>
    private void BuildItemsFromMatched()
    {
        var selSet = new HashSet<string>(_selected);
        var (sortItemIndex, sortItemLabel) = ResolveSortItem();

        Items.Clear();
        foreach (var (name, _) in _matchedFolders)
            Items.Add(new ImageItemVM(name, name, isFolder: true, isPlaceholder: false, hasThumb: false,
                thumbBrush: null, selectable: false, isSelected: false, hasTagDots: false,
                tagDots: new List<IBrush>(), sizeLabel: "—", dateLabel: "—", target: name,
                cells: [new ListCell(0, ListCellKind.BasicName, name, 0, null, true)]));
        foreach (var e in _matchedFiles)
        {
            Items.Add(CreateImageItem(e, selSet, sortItemIndex, sortItemLabel));
        }

        // ECO-113: 選択経路の差分更新用インデックスを再構築(ここは元々 O(母集合) の再構築サイト。
        // クリック毎の全走査をここへ一度だけ寄せる)
        _itemById.Clear();
        _matchedIndexById.Clear();
        _markedItemIds.Clear();
        for (int i = 0; i < _matchedFiles.Count; i++)
            _matchedIndexById[_matchedFiles[i].Record.Id] = i;
        foreach (var item in Items)
        {
            if (item.IsFolder) continue;
            _itemById[item.Id] = item;
            if (item.IsSelected || item.IsMergeTarget || item.IsOrganizeTarget) _markedItemIds.Add(item.Id);
        }
    }

    /// <summary>アイコンタイルのソート項目(ECO-025 β/FL-003 v2): ソート中かつ名前以外のとき列 index+ラベル(ECO-118 で抽出)。</summary>
    private (int Index, string? Label) ResolveSortItem()
    {
        if (_sortColKey is { } sortKey && !string.Equals(sortKey, ViewColumnModel.NameKey, StringComparison.Ordinal))
        {
            for (int i = 0; i < _listColumnDefs.Count; i++)
            {
                if (string.Equals(_listColumnDefs[i].Key, sortKey, StringComparison.Ordinal))
                {
                    return (i, _listColumnDefs[i].Label);
                }
            }
        }
        return (-1, null);
    }

    private ImageItemVM CreateImageItem(
        ImageEntry entry,
        HashSet<string> selectedIds,
        int sortItemIndex = -1,
        string? sortItemLabel = null)
    {
        bool selected = selectedIds.Contains(entry.Record.Id);
        // ECO-112/VC-IMG-13: ファイル操作モードは選択順の番号バッジを出さない(白✓)。順序は表示順が権威(IMG-026①)。
        int? order = selected && !_fileOpsMode ? _selected.IndexOf(entry.Record.Id) + 1 : null;
        var tagsOf = ImgTagIds(entry);
        bool inSelect = _editMode || _workMode || _deleteMode || _fileOpsMode;
        // ECO-114: ドットは常時構築(表示可否は hasTagDots)= モード中構築のアイテムが閲覧へ戻った時の
        // 欠落防止+ブラシは色キーでキャッシュ(26万件×3個の再生成を排除)
        var dots = tagsOf.Count > 0
            ? tagsOf.Take(3).Select(t => DotBrush(TagColor(_tagById.GetValueOrDefault(t)))).ToList()
            : new List<IBrush>();
        var cells = ListColumnBuilder.BuildCells(entry, _listColumnDefs, FmtSize, FmtDate);
        var sortItemCell = sortItemIndex >= 0 && sortItemIndex < cells.Count ? cells[sortItemIndex] : null;
        return new ImageItemVM(entry.Record.Id, entry.Record.FileName, isFolder: false, isPlaceholder: false,
            hasThumb: true, thumbBrush: null, selectable: inSelect, isSelected: selected,
            hasTagDots: !inSelect && tagsOf.Count > 0, tagDots: dots,
            sizeLabel: FmtSize(entry.Record.FileSize), dateLabel: FmtDate(entry.Record.ModifiedDate),
            target: null, absolutePath: entry.AbsolutePath, selectionOrder: order,
            isMergeTarget: _organizeMode && string.Equals(entry.Record.Id, Organize.MergeTargetId, StringComparison.Ordinal),
            isOrganizeTarget: _organizeMode && Organize.Targets.Contains(entry.Record.Id),
            cells: cells,
            sortItemLabel: sortItemCell is not null ? sortItemLabel : null,
            sortItemCell: sortItemCell,
            isPlainCheck: _fileOpsMode);
    }

    /// <summary>
    /// 表示列ライブ編集(ECO-026/#5)の部分再構築: 列(BuildListColumns=除去列がソート中なら解除を含む)+
    /// 現 matched の再ソート + Items 再構築のみ。母集合/チップ/フォルダ/パンくずは列変更で不変なので回さない。
    /// </summary>
    private void RebuildColumnsAndItems()
    {
        BuildListColumns();
        _matchedFiles = SortFiles(_matchedFiles); // 除去列がソート中なら解除された順へ整列し直す
        BuildItemsFromMatched();
        OnPropertyChanged(string.Empty);
    }

    /// <summary>
    /// ECO-114: モード開始/終了専用の軽量経路。母集合(表示集合/チップ/パンくず/件数/ソート)は
    /// モード遷移で不変のため、全面 Recompute(全件評価×(1+子数)+全件ソート+26万 VM 再構築+
    /// CollectionChanged の嵐)を通らず、在庫 Items のモード依存フラグと選択マーカーだけを
    /// その場更新する(ECO-026/#2「切替で Items を作り直さない」のモード遷移版)。
    /// 残存計算量= O(表示件数) の単純フラグパス 1 本(アロケーション/評価/ソート/再構築なし。
    /// 未実現アイテムの PropertyChanged はリスナー不在で軽量)。
    /// </summary>
    private void ApplyModeTransition()
    {
        if (!_loaded) { Recompute(); return; } // 未ロードは従来経路(空 UI の整合は Recompute が権威)
        ClearCopyFeedback(); // モード遷移はフィードバック解除遷移(ECO-112/IMG-026②)
        if (InAnyMode) ColumnPickerOpen = false; // 開いていた列ピッカーは閉じる(IMG-014・Recompute と同契約)
        bool inSelect = _editMode || _workMode || _deleteMode || _fileOpsMode;
        foreach (var item in Items)
        {
            if (item.IsFolder) continue;
            item.ApplyModeState(
                selectable: inSelect,
                showTagDots: !inSelect && item.TagDots.Count > 0,
                isPlainCheck: _fileOpsMode);
        }
        _markedItemIds.Clear(); // 選択マーカーは全クリア(モード遷移は選択クリアが契約=呼出側で _selected.Clear 済み)
        BuildContextPanels(new HashSet<string>(_selected));
        OnPropertyChanged(string.Empty);
    }

    /// <summary>
    /// 選択/マーカーのみが変化したときの軽量更新。Items(グリッド)を作り直さず、
    /// 既存の各 ImageItemVM をその場更新する(大量画像でのクリック応答性=Items 全再構築と
    /// CollectionChanged Reset・スクロールリセットを避ける)。membership が変わる遷移(フォルダ移動・
    /// チップ・軸/ソート)は従来どおり Recompute、モード切替は ApplyModeTransition(ECO-114)を使う。
    /// </summary>
    private void RefreshSelectionMarkers()
    {
        if (!_loaded) return;
        ClearCopyFeedback(); // ECO-112/IMG-026②: 選択・マーカー変化はフィードバック解除遷移(ECO-104 教訓=全列挙)
        var selSet = new HashSet<string>(_selected);
        // ECO-113: 差分更新 — 前回マーカー保持(_markedItemIds)∪今回対象だけを触る。Items 全走査は
        // 母集合比例(26万件で顕在化・e39d68a の残余)。選択順も IndexOf 反復でなく一度の辞書構築で解決。
        // モード遷移(isPlainCheck の切替)は Recompute(全再構築)または ApplyModeTransition
        // (ECO-114=全非フォルダへ一括適用)を必ず通るため、非対象アイテムの状態は現モードと常に一致する。
        var orderById = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < _selected.Count; i++) orderById[_selected[i]] = i + 1;
        var affected = new HashSet<string>(_markedItemIds, StringComparer.Ordinal);
        affected.UnionWith(selSet);
        if (_organizeMode)
        {
            if (Organize.MergeTargetId is { } mergeId) affected.Add(mergeId);
            affected.UnionWith(Organize.Targets);
        }
        _markedItemIds.Clear();
        foreach (var id in affected)
        {
            if (!_itemById.TryGetValue(id, out var item)) continue;
            bool selected = selSet.Contains(id);
            // ECO-112/VC-IMG-13: ファイル操作モードは番号バッジなし(白✓)
            int? order = selected && !_fileOpsMode ? orderById[id] : null;
            bool merge = _organizeMode && string.Equals(id, Organize.MergeTargetId, StringComparison.Ordinal);
            bool org = _organizeMode && Organize.Targets.Contains(id);
            item.SetSelectionMarkers(selected, order, merge, org, isPlainCheck: _fileOpsMode);
            if (selected || merge || org) _markedItemIds.Add(id);
        }
        BuildContextPanels(selSet);
        OnPropertyChanged(string.Empty);
    }

    /// <summary>選択依存パネル(タグ編集パネル+整理トレイ)の再構築。Items とは独立した小コレクション。</summary>
    private void BuildContextPanels(HashSet<string> selSet)
    {
        // ---- edit panel ----
        // ECO-113: 全件評価+ソート(AllLoadedImagesInContext)を撤去 — 選択は id 辞書で解決し、
        // 表示順(共通タグ計算の「先頭」)は Recompute 時構築の index 辞書で保つ。文脈内判定=
        // _matchedIndexById 含有(母集合変更は必ず Recompute 経由なのでスナップショットは常に現文脈)。
        var selectedEntries = selSet
            .Where(_matchedIndexById.ContainsKey)
            .OrderBy(id => _matchedIndexById[id])
            .Select(id => _entryById.GetValueOrDefault(id))
            .OfType<ImageEntry>()
            .ToList();
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
        CurrentNote = selectedEntries.Count > 1 ? _localization.T("tagging.commonTagsOfSelection") : _localization.T("tagging.tagsOnImage");
        NoCurrentLabel = selectedEntries.Count > 1 ? _localization.T("tagging.noCommonTags2") : _localization.T("tagging.noTagsYet");

        BuildAddGroups(selectedEntries);

        // ---- 整理トレイ(ECO-014・状態は Organize 子 VM 所有 — ECO-036 第3段) ----
        var mergeTargetId = Organize.MergeTargetId;
        MergeTarget = mergeTargetId is not null ? SlotFor(mergeTargetId) : null;
        OrganizeTargets.Clear();
        foreach (var id in Organize.Targets)
        {
            var slot = SlotFor(id);
            if (slot is not null) OrganizeTargets.Add(slot);
        }
        SearchResults.Clear();
        var inTray = new HashSet<string>(Organize.Targets, StringComparer.Ordinal);
        foreach (var (id, score, isCrit, relationship) in Organize.SearchResults)
        {
            var e = EntryById(id);
            if (e is null) continue; // マージ後に deleted 化した候補等は除外
            bool added = inTray.Contains(id) || id == mergeTargetId;
            SearchResults.Add(new OrganizeResultVM(id, e.Record.FileName, e.AbsolutePath,
                FmtSize(e.Record.FileSize), score, isCrit, added, _localization.T("view.criteriaMatch"), relationship));
        }
    }

    private ImageEntry? EntryById(string id)
        => _entryById.GetValueOrDefault(id); // ECO-113: 線形走査→辞書(26万件で選択パネル経路が母集合比例だった)

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
            (TagType.Simple, _localization.T("tag.type.simple"), _localization.T("tagging.simpleHint"), "#5b6473", "#f0f2f6"),
            (TagType.Textual, _localization.T("tag.type.textual"), _localization.T("tagging.textualHint"), "#2459cf", "#eaf1fe"),
            (TagType.Numeric, _localization.T("tag.type.numeric"), _localization.T("tagging.numericHint"), "#0f8a5e", "#eafaf3"),
        };
        string q = _addQuery.Trim().ToLowerInvariant(); // ECO-041: 検索(mock L811 — trim+大小無視の部分一致)
        foreach (var g in groups)
        {
            var rows = new List<AddRowVM>();
            foreach (var tag in _tagById.Values.Where(t => t.Type == g.Type)
                         .Where(t => q.Length == 0 || t.Name.ToLowerInvariant().Contains(q))
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
                        row.NumCurrent = cur is not null ? $"★ {cur}" : _localization.T("common.notSet");
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
                        row.NumCurrent = cur is not null ? $"★ {cur}" : _localization.T("common.notSet");
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
        if (_collectionId == id && (_loaded || _isContentLoading)) return;
        Organize.InvalidateSearchContext();
        _collectionId = id;
        _scanNoticeKey = null;
        _settings.LastCollectionId = id; // CR-5 書き戻し(永続化は SettingsStore / CaptureSettings)
        _fsPath.Clear(); _tagFilter = null; _selected.Clear(); _expandTag = null;
        await LoadContentAsync(id).ConfigureAwait(true);
    }

    [RelayCommand]
    private Task RetryCatalog() => InitializeAsync(_settings.LastCollectionId);

    [RelayCommand]
    private Task RetryContent() => _collectionId is { } id ? LoadContentAsync(id) : Task.CompletedTask;

    [RelayCommand]
    // ECO-124: サイドバー状態しか変えない=母集合パイプライン(Recompute)を通さず通知のみ
    // (WorkTab ToggleSidebar と対称。26万件で Recompute 結合が 1〜2 秒の体感遅延=経路7例目)。
    // ClearCopyFeedback は旧 Recompute 先頭の解除遷移を明示継承(ECO-113/114 の置換時と同じ・ECO-104 全列挙)
    private void ToggleSidebar() { ClearCopyFeedback(); _collapsed = !_collapsed; OnPropertyChanged(string.Empty); }

    /// <summary>
    /// コレクション「追加(+)」(ECO-013/IMG-009): コレクション管理(追加・スキャン・削除)ビューを開く
    /// 単一入口。閉じた後はベースデータを読み直し、FS 軸ルートへ戻す(選択コレクションは存続していれば維持・
    /// 削除されていれば未選択へフォールバック=REQ-053)。
    /// </summary>
    [RelayCommand]
    private async Task OpenFolderManagement()
    {
        Organize.InvalidateSearchContext();
        var keep = _collectionId;
        await _windows.ShowFolderManagementAsync().ConfigureAwait(true);
        _axis = "fs"; _viewId = null; _viewRoot = null; _viewPath.Clear();
        _fsPath.Clear(); _tagFilter = null; _selected.Clear(); _expandTag = null;
        await InitializeAsync(keep).ConfigureAwait(true);
    }

    [RelayCommand]
    private void ToggleAxisMenu() { AxisMenuOpen = !AxisMenuOpen; SortMenuOpen = false; MoreMenuOpen = false; ColumnPickerOpen = false; OnPropertyChanged(string.Empty); }

    public void CloseMenusFromDismiss()
    {
        if (!AxisMenuOpen && !SortMenuOpen && !MoreMenuOpen && !ColumnPickerOpen) return;
        AxisMenuOpen = false; SortMenuOpen = false; MoreMenuOpen = false; ColumnPickerOpen = false;
        OnPropertyChanged(string.Empty);
    }

    [RelayCommand]
    private async Task SelectAxis(string id)
    {
        Organize.InvalidateSearchContext();
        AxisMenuOpen = false;
        CloseColumnPicker(); // 軸/ビュー切替で表示列ピッカーを閉じる(古いビュー向けの書き戻し混線を防ぐ・ECO-025 β-2)
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
        await ReloadViewGraphAsync(view, preserveNavigation: false).ConfigureAwait(true);
    }

    /// <summary>
    /// view 軸 graph を保存済み階層から再構築する共通経路。通常の軸選択は home から開始し、
    /// ECO-096 の stale 再読込は現在 path を新 graph の同一ノードへ再束縛する。
    /// </summary>
    private async Task ReloadViewGraphAsync(View view, bool preserveNavigation)
    {
        IReadOnlyList<GraphNode> previousPath = preserveNavigation ? _viewPath.ToList() : [];
        _axis = "view"; _viewId = view.Id; _viewLabel = view.Name;
        if (!preserveNavigation)
        {
            // (ECO-084/REQ-094) ビュー毎の最終選択モードを復元。列挙外・欠落は既定「すべて」(裁定①=デバイスローカル)
            _viewUnclassified = _settings.ViewDisplayModes.TryGetValue(view.Id, out var displayMode)
                && string.Equals(displayMode, "unclassified", StringComparison.Ordinal);
        }

        _viewConditions = await _views.GetConditionsAsync(view.Id).ConfigureAwait(true);
        var hierarchy = await _views.GetHierarchyAsync(view.Id).ConfigureAwait(true);
        var valueIndex = TagValueIndex.Build(_entries.Select(e => e.ToImageWithTags()));
        var definedIndex = await BuildDefinedIndexAsync(hierarchy).ConfigureAwait(true);
        var result = _graphBuilder.BuildGraph(hierarchy, _tagById, valueIndex, definedIndex);
        _viewRoot = result.Root;
        _viewPath.Clear();
        if (preserveNavigation)
        {
            _viewPath.AddRange(RebindViewPath(_viewRoot, previousPath));
        }
        else
        {
            // ECO-063/REQ-037: 保存済み home を画像タブの初期 path へ接続。
            // null/参照切れは空 path のまま=root fallback。独自 DFS は持たず Core の決定規則を消費する。
            _viewPath.AddRange(_graphBuilder.ResolveHomePath(_viewRoot, view.HomeTagId));
        }

        Recompute();
    }

    /// <summary>
    /// ECO-096: 旧 graph の path を新 graph へ参照で持ち越さず、安定した論理同一性で再束縛する。
    /// 構造変更で次段が消えた場合は到達できる最長 prefix まで戻す(root を含む安全な縮退)。
    /// </summary>
    private static IReadOnlyList<GraphNode> RebindViewPath(GraphNode root, IReadOnlyList<GraphNode> previousPath)
    {
        var rebound = new List<GraphNode>(previousPath.Count);
        var parent = root;
        foreach (var previous in previousPath)
        {
            var current = parent.Children.FirstOrDefault(candidate =>
                string.Equals(candidate.HierarchyNodeId, previous.HierarchyNodeId, StringComparison.Ordinal) &&
                candidate.Kind == previous.Kind &&
                string.Equals(candidate.Value, previous.Value, StringComparison.Ordinal));
            if (current is null)
            {
                break;
            }

            rebound.Add(current);
            parent = current;
        }

        return rebound;
    }

    /// <summary>
    /// 定義値展開(REQ-096/ECO-086)の定義値供給。defined/defined_and_observed のノードがある場合のみ
    /// 対象タグの型別設定を取得して構築する(観測値契約 TagValueIndex とは別系統=INV-010 の混同禁止)。
    /// </summary>
    private async Task<TagDefinedValueIndex?> BuildDefinedIndexAsync(IReadOnlyList<HierarchyNode> hierarchy)
    {
        var tagIds = hierarchy
            .Where(n => n.ExpansionMode is HierarchyExpansionMode.Defined or HierarchyExpansionMode.DefinedAndObserved)
            .Select(n => n.TagId)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (tagIds.Count == 0)
        {
            return null;
        }

        var textual = new Dictionary<string, TextualTagSettings>(StringComparer.Ordinal);
        var numeric = new Dictionary<string, NumericTagSettings>(StringComparer.Ordinal);
        foreach (var tagId in tagIds)
        {
            if (!_tagById.TryGetValue(tagId, out var tag))
            {
                continue; // 参照切れは NodeGraphBuilder 側の INV-008 に委ねる
            }

            if (tag.Type == TagType.Textual &&
                await _tags.GetTextualSettingsAsync(tagId).ConfigureAwait(true) is { } ts)
            {
                textual[tagId] = ts;
            }
            else if (tag.Type == TagType.Numeric &&
                await _tags.GetNumericSettingsAsync(tagId).ConfigureAwait(true) is { } ns)
            {
                numeric[tagId] = ns;
            }
        }

        return TagDefinedValueIndex.Build(textual, numeric);
    }

    [RelayCommand]
    private void ToggleSortMenu() { SortMenuOpen = !SortMenuOpen; AxisMenuOpen = false; MoreMenuOpen = false; ColumnPickerOpen = false; OnPropertyChanged(string.Empty); }

    /// <summary>
    /// 「表示列」ポップオーバーを開閉(ECO-025 β-2)。開くときアクティブビューの display_columns +
    /// タグ母集合(ビューのタグ階層メンバー)で列ピッカーを生成し、編集は <see cref="OnColumnPickerChanged"/>
    /// でビュー定義へ即書き戻す(VE-003)。FS 軸・未選択では何もしない(書き戻し先が無い・CanEditColumns)。
    /// </summary>
    [RelayCommand]
    private async Task ToggleColumnPicker()
    {
        if (ColumnPickerOpen)
        {
            ColumnPickerOpen = false;
            OnPropertyChanged(string.Empty);
            return;
        }

        var view = _axis == "view" && _viewId is not null
            ? _allViews.FirstOrDefault(v => string.Equals(v.Id, _viewId, StringComparison.Ordinal))
            : null;
        if (view is null) { OnPropertyChanged(string.Empty); return; }

        var viewTags = await LoadActiveViewTagsAsync(view).ConfigureAwait(true);
        if (ColumnPicker is not null) ColumnPicker.Changed -= OnColumnPickerChanged;
        var picker = new ColumnPickerViewModel(view.DisplayColumns, viewTags, _localization);
        picker.Changed += OnColumnPickerChanged;
        ColumnPicker = picker;
        ColumnPickerOpen = true;
        AxisMenuOpen = false; SortMenuOpen = false; MoreMenuOpen = false;
        OnPropertyChanged(string.Empty);
    }

    /// <summary>表示列ピッカーを閉じてイベント購読を解除(軸/ビュー切替時に古いピッカーの書き戻しを止める)。</summary>
    private void CloseColumnPicker()
    {
        if (ColumnPicker is not null) ColumnPicker.Changed -= OnColumnPickerChanged;
        ColumnPicker = null;
        ColumnPickerOpen = false;
    }

    /// <summary>アクティブビューのタグ母集合=タグ階層メンバーを出現順で distinct(WindowService と同方針)。階層なしは空。</summary>
    private async Task<IReadOnlyList<Tag>> LoadActiveViewTagsAsync(View view)
    {
        var nodes = await _views.GetHierarchyAsync(view.Id).ConfigureAwait(true);
        if (nodes.Count == 0) return [];
        var result = new List<Tag>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            if (seen.Add(node.TagId) && _tagById.TryGetValue(node.TagId, out var tag))
            {
                result.Add(tag);
            }
        }
        return result;
    }

    /// <summary>
    /// 列ピッカーの編集(追加/削除/並べ替え)ごとにビュー定義 display_columns を書き戻し、リスト列/ソートを作り直す
    /// (VE-003 直接書き戻し・除去列がソート中なら BuildListColumns がソート解除)。async void=UI イベントハンドラ。
    /// </summary>
    private async void OnColumnPickerChanged(object? sender, EventArgs e)
    {
        try
        {
            if (_viewId is null || ColumnPicker is null) return;
            var idx = _allViews.FindIndex(v => string.Equals(v.Id, _viewId, StringComparison.Ordinal));
            if (idx < 0) return;

            var updated = _allViews[idx] with { DisplayColumns = ColumnPicker.Serialize() };
            var result = await _views.UpdateAsync(updated).ConfigureAwait(true);
            if (!result.IsSuccess) return;

            _allViews[idx] = result.Value!;
            // (ECO-026/#5) 列変更は母集合/チップ/フォルダ不変=列+セル(+除去列ソート解除)だけ部分再構築する
            RebuildColumnsAndItems();
        }
        catch (Exception)
        {
            // 書き戻し失敗で UI を落とさない(INV-008 系)。次の編集で再試行可能。
        }
    }

    [RelayCommand]
    private void ToggleMoreMenu() { MoreMenuOpen = !MoreMenuOpen; AxisMenuOpen = false; SortMenuOpen = false; ColumnPickerOpen = false; OnPropertyChanged(string.Empty); }

    // ---- ゴミ箱ポップアップ入口・操作コマンド(OpenTrash/CloseTrash/ToggleTrashItem/ToggleTrashSelectAll/
    //      RestoreSelectedTrash/PurgeSelectedTrash/EmptyTrash)は Trash 子 VM へ移送(ECO-036 第1段)。
    //      XAML は Trash.OpenTrashCommand 等へバインド。

    /// <summary>⋯ メニュー: 修復ライフサイクル(criteria/relink/復元)を既存モーダルで開く(ECO-015)。閉じ後にデータ再読込。</summary>
    [RelayCommand]
    private async Task OpenRepair()
    {
        MoreMenuOpen = false;
        if (_collectionId is null) { OnPropertyChanged(string.Empty); return; }
        await _windows.ShowRepairAsync(_collectionId).ConfigureAwait(true);
        await ReloadImagesAsync().ConfigureAwait(true);
        await Trash.RefreshCountAsync().ConfigureAwait(true); // 修復の除外(missing→deleted)で件数が変わる
        Recompute();
    }

    /// <summary>
    /// ⋯ メニュー: 設定 ▸ データとバックアップへの誘導(ECO-077/SS-001 再裁定=M5)。
    /// 書き出す/取り込むの実体はここに置かない。設定内での取り込みが表示中コレクションの
    /// 付与/参照を増やしうるため、閉じ後に再読込する(旧 ImportCollection と同じ理由)。
    /// </summary>
    [RelayCommand]
    private async Task OpenBackupSettings()
    {
        MoreMenuOpen = false;
        OnPropertyChanged(string.Empty);
        await _windows.ShowSettingsAsync(SettingsSection.DataBackup).ConfigureAwait(true);
        if (_collectionId is null) { return; }
        await ReloadImagesAsync().ConfigureAwait(true);
        Recompute();
    }

    /// <summary>アイコン「並び替え」メニューの 昇順/降順 セグメント(ECO-025 β/FL-003 v2)。ソート列がある時のみ効く。</summary>
    [RelayCommand]
    private void SetSortAsc() { if (_sortColKey is not null && _sortColDir != SortDirection.Asc) { _sortColDir = SortDirection.Asc; Recompute(); } }

    [RelayCommand]
    private void SetSortDesc() { if (_sortColKey is not null && _sortColDir != SortDirection.Desc) { _sortColDir = SortDirection.Desc; Recompute(); } }

    /// <summary>列ソートの入口(ECO-025 β/FL-003 v2・リスト列ヘッダー / アイコン並び替えメニュー候補で共有)。別列=昇順開始 / 同列再クリック=昇順⇄降順トグル。</summary>
    [RelayCommand]
    private void SelectColumnSort(string? key)
    {
        if (key is null) return;
        if (_collectionId is not null) _completedScanOrderByCollection.Remove(_collectionId);
        if (string.Equals(_sortColKey, key, StringComparison.Ordinal))
            _sortColDir = _sortColDir == SortDirection.Asc ? SortDirection.Desc : SortDirection.Asc;
        else { _sortColKey = key; _sortColDir = SortDirection.Asc; }
        Recompute();
    }

    /// <summary>ソート概要の「クリア」(ECO-025 β)。列ヘッダーソートを解除し元順へ。</summary>
    [RelayCommand]
    private void ClearColumnSort()
    {
        _sortColKey = null;
        if (_collectionId is not null) _completedScanOrderByCollection.Remove(_collectionId);
        Recompute();
    }

    // (ECO-026/#2) 表示形式切替は同一データ(Items)を作り直さない=表示状態のフリップのみ。
    // grid/list は同じ ImageItemVM(cells + ソート項目)を別レイアウトで描画するだけで、母集合・列・ソートは不変。
    [RelayCommand]
    private void SetGrid() { if (_layout == "grid") return; _layout = "grid"; _settings.DisplayMode = "grid"; OnPropertyChanged(string.Empty); } // CR-6

    // ---- 表示モード切替(ECO-084/REQ-094): すべて(累積) ⇄ 未分類(最深配置) ----
    [RelayCommand]
    private void SetDisplayModeAll() => SetDisplayMode(unclassified: false);

    [RelayCommand]
    private void SetDisplayModeUnclassified() => SetDisplayMode(unclassified: true);

    private void SetDisplayMode(bool unclassified)
    {
        if (_viewUnclassified == unclassified) return;
        _viewUnclassified = unclassified;
        // ビュー毎の最終選択モードをデバイスローカルに記憶(REQ-094。ファイル書き出しは CaptureSettings 経路=REQ-052)
        if (_viewId is { } viewId)
            _settings.ViewDisplayModes[viewId] = unclassified ? "unclassified" : "all";
        _selected.Clear(); // 表示集合の membership が変わる=非表示画像の選択残留を防ぐ(ClickChip の潜りと同型)
        Recompute();
    }

    [RelayCommand]
    private void SetList() { _layout = "list"; _settings.DisplayMode = "list"; SortMenuOpen = false; OnPropertyChanged(string.Empty); } // CR-6・FL-003 リスト切替で並び替えメニューを閉じる

    [RelayCommand]
    private void ToggleEdit()
    {
        _editMode = !_editMode;
        if (_editMode) { _organizeMode = false; Organize.ResetState(); _workMode = false; _deleteMode = false; _fileOpsMode = false; } // 整理・作業・削除・ファイル操作と排他(ECO-014/017/018/112)
        _selected.Clear(); _expandTag = null; _panelTab = "current";
        ApplyModeTransition(); // ECO-114: 母集合不変=全面 Recompute を通らない
    }

    [RelayCommand]
    private void GoHome()
    {
        if ((_axis == "view" && _viewPath.Count > 0) || (_axis != "view" && _fsPath.Count > 0))
            Organize.InvalidateSearchContext();
        if (_axis == "view") _viewPath.Clear();
        else { _fsPath.Clear(); _tagFilter = null; }
        _selected.Clear();
        Recompute();
    }

    [RelayCommand]
    private void GoCrumb(int depth)
    {
        var pathCount = _axis == "view" ? _viewPath.Count : _fsPath.Count;
        if (pathCount > depth + 1) Organize.InvalidateSearchContext();
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

    [RelayCommand] // public 化=IChipStripHost(ECO-094・共有部品からの direct ハンドラ経由呼び出し)
    public void ClickChip(ChipVM chip)
    {
        if (chip.IsNav)
        {
            // view 軸: 子ノードへ潜る
            if (_currentChildren.TryGetValue(chip.Id, out var node)) { Organize.InvalidateSearchContext(); _viewPath.Add(node); _selected.Clear(); Recompute(); }
            return;
        }
        if (chip.IsNeutral) _tagFilter = null;
        else _tagFilter = _tagFilter == chip.Id ? null : chip.Id;

        // ECO-097/IMG-025案A: filter変更後の操作対象は新しい可視画像だけに縮退する。
        // 解除時も既に落とした選択を復活させず、残った選択だけを維持する。
        var visibleIds = new HashSet<string>(
            ResolveFs().Files.Select(entry => entry.Record.Id),
            StringComparer.Ordinal);
        _selected.RemoveAll(id => !visibleIds.Contains(id));
        Recompute();
    }

    public void HandleItemClick(ImageItemVM item, bool ctrl, bool shift, bool isDoubleClick = false)
    {
        if (item.IsFolder)
        {
            if (item.Target is not null) { Organize.InvalidateSearchContext(); _fsPath.Add(item.Target); _tagFilter = null; _selected.Clear(); Recompute(); }
            return;
        }
        if (_organizeMode)
        {
            // 整理モード(ECO-014・モック準拠): マージ先未設定→マージ先に / 設定後→整理対象をトグル(選択ではない)
            if (Organize.MergeTargetId is null) SetMergeTarget(item.Id);
            else if (item.Id != Organize.MergeTargetId) ToggleOrganizeTarget(item.Id);
            return;
        }
        if (!_editMode && !_workMode && !_deleteMode && !_fileOpsMode)
        {
            // 閲覧モード(モック準拠): シングルクリックは無操作・ダブルクリックでビューアー起動(REQ-041)
            if (isDoubleClick) OpenViewer(item.Id);
            return;
        }
        ToggleSelect(item.Id, ctrl, shift); // 編集 or 作業 or 削除 or ファイル操作モード: 選択(inSelect・選択機構を再利用)
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
        // ECO-113: 母集合列挙(全件評価+ソート=AllLoadedImagesInContext)は表示順の範囲が要る
        // SHIFT 分岐だけが必要とする。無条件実行に置くと plain/Ctrl クリックのコストが
        // 母集合サイズに比例する(26万件で顕在化・混入=M3a 初版 6f7b4f9)。
        if (shift && _selected.Count > 0)
        {
            var list = AllLoadedImagesInContext().Select(e => e.Record.Id).ToList();
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
    // ECO-115: パネル状態(_panelTab/_expandTag)しか変えない操作は全面 Recompute(母集合再評価+
    // Items 再構築=26万件で応答劣化)を通らず、パネルのみ部分再構築する(AddQuery setter=ECO-041 の
    // 既存様式・WorkTab の TabCurrent/TabAdd/ClickAddRow と対称化)。
    private void TabCurrent() { _panelTab = "current"; OnPropertyChanged(string.Empty); }

    [RelayCommand]
    private async Task TabAdd()
    {
        _panelTab = "add"; _expandTag = null;
        BuildContextPanels(new HashSet<string>(_selected)); // 展開状態のリセットを行へ反映(Items 不変)
        OnPropertyChanged(string.Empty);
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
        // 展開: 設定をロードしてからパネルのみ再描画(ECO-115)
        if (_expandTag == row.Id)
        {
            _expandTag = null;
            BuildContextPanels(new HashSet<string>(_selected));
            OnPropertyChanged(string.Empty);
            return;
        }
        _expandTag = row.Id;
        await EnsureSettingsAsync(row.Id);
        BuildContextPanels(new HashSet<string>(_selected));
        OnPropertyChanged(string.Empty);
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

    /// <summary>
    /// タグ付与/剥奪後の表示反映(ECO-118: 選択規模の差分経路)。
    /// 変更されたのは選択画像の付与行だけ — 母集合規模の再読(コレクション全付与行)・全 ImageEntry
    /// 再構築・全面 Recompute(26 万件で約 2 秒)を通らず、選択分の旧/新評価の差分で反映する。
    /// 意味論は全面再計算と同一: 影響画像の表示・チップ件数・表示帰属(REQ-094 の未分類離脱/
    /// FS フィルタ離脱)。差分経路の前提=「選択が全員表示中」(このとき帰属は stay/leave のみで
    /// enter は起きない)。前提が破れるケース(過去の leave で表示外の選択=ゴーストが混在)は
    /// 全面経路へ fallback し、旧経路の「次のタグ操作で必ず再解決」と同一挙動を保つ(R8 F1)。
    /// fallback= 未ロード・選択なし・表示外選択の混在・大選択(逐次 DB 再読の定数劣化を避ける
    /// しきい値= R8 F4)・タグ列ソート中(値変更でソート位置が動く)。
    /// 残存計算量(明記= ECO-113 教訓 3): _entries 参照差し替えの O(母集合) ポインタ走査 1 本と、
    /// 除去発生時の _matchedIndexById 詰め直し(int 書き換え)のみ。アロケーション/評価/ソート/
    /// VM 再構築はすべて選択規模。
    /// </summary>
    private async Task ReloadTagsAsync()
    {
        if (!_loaded || _selected.Count == 0 || _selected.Count > DeltaSelectionLimit
            || (_sortColKey is { } sk && !ViewColumnModel.BasicKeys.Contains(sk))
            || _selected.Any(id => !_matchedIndexById.ContainsKey(id)))
        {
            await FullReloadTagsAsync().ConfigureAwait(true);
            return;
        }

        // ① 選択分のみ DB 再読(O(選択))
        var affected = new List<(string Id, ImageEntry Old, ImageEntry New)>();
        foreach (var id in SelectedIds.ToList())
        {
            if (!_entryById.TryGetValue(id, out var oldEntry)) continue; // 防御(選択⊆表示⊆母集合が通常)
            var rows = await _tags.GetImageTagsAsync(id).ConfigureAwait(true);
            if (rows.Count > 0) _imageTags[id] = rows.ToList(); else _imageTags.Remove(id);
            affected.Add((id, oldEntry, BuildEntry(oldEntry.Record)));
        }
        if (affected.Count == 0)
        {
            await FullReloadTagsAsync().ConfigureAwait(true);
            return;
        }

        // ② 差分エントリ更新(参照差し替えの O(母集合) ポインタ走査のみ・アロケーションなし)
        var newById = affected.ToDictionary(a => a.Id, a => a.New, StringComparer.Ordinal);
        for (int i = 0; i < _entries.Count; i++)
        {
            if (newById.TryGetValue(_entries[i].Record.Id, out var ne)) _entries[i] = ne;
        }
        foreach (var a in affected) _entryById[a.Id] = a.New;

        // ③ チップ件数差分+表示帰属(stay/leave)
        var leaving = new HashSet<string>(StringComparer.Ordinal);
        if (_axis == "view" && _viewRoot is not null)
        {
            var fullPath = new List<GraphNode> { _viewRoot };
            fullPath.AddRange(_viewPath);
            var current = fullPath[^1];
            for (int i = 0; i < _viewChipCounts.Count; i++)
            {
                var (child, m, u) = _viewChipCounts[i];
                var childPath = Append(fullPath, child);
                foreach (var a in affected)
                {
                    bool oldIn = MatchesPath(childPath, a.Old);
                    bool newIn = MatchesPath(childPath, a.New);
                    m += (newIn ? 1 : 0) - (oldIn ? 1 : 0);
                    if (_viewUnclassified)
                    {
                        // (REQ-094) 未分類件数=子に一致し、かつどの孫にも一致しない
                        bool oldU = oldIn && !child.Children.Any(g => MatchesPath(Append(childPath, g), a.Old));
                        bool newU = newIn && !child.Children.Any(g => MatchesPath(Append(childPath, g), a.New));
                        u += (newU ? 1 : 0) - (oldU ? 1 : 0);
                    }
                }
                _viewChipCounts[i] = (child, m, u);
            }
            ShowChips = false; ShowChipHint = false;
            BuildViewChips();
            foreach (var a in affected)
            {
                bool member = MatchesPath(fullPath, a.New);
                if (member && _viewUnclassified)
                {
                    member = !current.Children.Any(c => MatchesPath(Append(fullPath, c), a.New));
                }
                if (!member) leaving.Add(a.Id);
            }
        }
        else
        {
            foreach (var a in affected)
            {
                foreach (var tid in ImgTagIds(a.Old))
                {
                    if (!_fsChipCounts.TryGetValue(tid, out var c)) continue;
                    if (c <= 1) _fsChipCounts.Remove(tid); else _fsChipCounts[tid] = c - 1;
                }
                foreach (var tid in ImgTagIds(a.New))
                {
                    _fsChipCounts[tid] = _fsChipCounts.GetValueOrDefault(tid) + 1;
                }
                if (_tagFilter is { } tf && !ImgTagIds(a.New).Contains(tf)) leaving.Add(a.Id);
            }
            ShowChips = false; ShowChipHint = false; ShowEmptyTagNote = false;
            if (_fsChipCounts.Count > 0) BuildFsChips();
            else if (_fsPath.Count > 0) ShowEmptyTagNote = true;
        }

        // ④ Items 差分: stay= in-place 置換 / leave= 除去(挿入は起きない=③の帰属分析)
        var selSet = new HashSet<string>(_selected);
        var (sortItemIndex, sortItemLabel) = ResolveSortItem();
        var removals = new List<int>();
        foreach (var a in affected)
        {
            if (!_matchedIndexById.TryGetValue(a.Id, out var mi)) continue; // 到達不能(入口で表示外選択は fallback 済み)
            if (leaving.Contains(a.Id)) { removals.Add(mi); continue; }
            _matchedFiles[mi] = a.New;
            var item = CreateImageItem(a.New, selSet, sortItemIndex, sortItemLabel);
            Items[_matchedFolders.Count + mi] = item;
            _itemById[a.Id] = item;
            if (item.IsSelected || item.IsMergeTarget || item.IsOrganizeTarget) _markedItemIds.Add(a.Id);
            else _markedItemIds.Remove(a.Id);
        }
        if (removals.Count > 0)
        {
            removals.Sort();
            for (int i = removals.Count - 1; i >= 0; i--)
            {
                var mi = removals[i];
                var id = _matchedFiles[mi].Record.Id;
                _matchedFiles.RemoveAt(mi);
                Items.RemoveAt(_matchedFolders.Count + mi);
                _itemById.Remove(id);
                _markedItemIds.Remove(id);
            }
            // インデックス詰め直し(int 書き換えの O(表示件数) 走査のみ)
            _matchedIndexById.Clear();
            for (int i = 0; i < _matchedFiles.Count; i++) _matchedIndexById[_matchedFiles[i].Record.Id] = i;
            CountLabel = _localization.T("view.itemCountLabel",
                new Dictionary<string, string> { ["count"] = (_matchedFolders.Count + _matchedFiles.Count).ToString() });
        }

        // (R8 F2) REQ-094 専用空状態: 未分類モードで全員 leave した場合の追随(Recompute 行と同じ式)
        if (_axis == "view")
        {
            ShowUnclassifiedEmpty = _viewUnclassified && _matchedFiles.Count == 0;
        }

        // ⑤ パネル(現在のタグ/タグ追加)再構築+通知(ECO-041/115 様式)
        BuildContextPanels(selSet);
        OnPropertyChanged(string.Empty);
    }

    /// <summary>全面再計算の従来経路(ECO-118 の fallback: タグ列ソート中・未ロード時)。</summary>
    private async Task FullReloadTagsAsync()
    {
        await RefreshImageTagsAsync().ConfigureAwait(true);
        BuildEntries();
        Recompute();
    }

    // =====================================================================
    //  整理モード コマンド(ECO-014: 類似+マージ統合「整理トレイ」)
    //  ECO-036 第3段: 状態+実行部は Organize 子 VM(M-UI-ORGANIZE-034)。ホストは転送殻
    //  (排他リセットは Organize.ResetState() 直接呼び出し・現行どおりの位置で Recompute()/
    //  RefreshSelectionMarkers() を呼ぶ — order §12.2/§12.5)。
    // =====================================================================
    [RelayCommand]
    private void ToggleOrganize()
    {
        _organizeMode = !_organizeMode;
        if (_organizeMode) { _editMode = false; _workMode = false; _deleteMode = false; _fileOpsMode = false; } // タグ編集・作業・削除・ファイル操作と排他(ECO-014/017/018/112)
        Organize.ResetState();
        _selected.Clear();
        ApplyModeTransition(); // ECO-114
    }

    // =====================================================================
    //  作業モード コマンド(ECO-017: 作業対象セットの蓄積)
    // =====================================================================
    /// <summary>作業モード開始/終了。タグ編集/整理と排他・選択クリア(モック toggleWork 準拠)。</summary>
    [RelayCommand]
    private void ToggleWork()
    {
        _workMode = !_workMode;
        if (_workMode) { _editMode = false; _organizeMode = false; Organize.ResetState(); _deleteMode = false; _fileOpsMode = false; } // 他文脈モードと排他(ECO-018/112)
        _selected.Clear(); _expandTag = null;
        ApplyModeTransition(); // ECO-114
    }

    /// <summary>
    /// 選択中の画像を作業対象セットへ和集合追加し、選択をクリア(モック addToWork 準拠)。選択なしは無操作。
    /// ECO-020: さらにデフォルト作業スペースへ永続追加する(受け渡し DOM-0026・ECO-017 の session 蓄積=チップ表示を継続)。
    /// ECO-036 第2段: 蓄積+受け渡しの実体は Work 子 VM。旧実装の順序(蓄積→選択クリア→マーカー通知→
    /// await 受け渡し)を殻が厳密保持する(order §10.2 — 蓄積と受け渡しを別メソッドにした理由)。
    /// </summary>
    [RelayCommand]
    private async Task AddToWork()
    {
        if (_selected.Count == 0) return;
        var added = _selected.ToList();
        Work.AddTargets(added); // Set 意味論(重複なし・チップ表示用)
        _selected.Clear();
        RefreshSelectionMarkers(); // 選択クリア+チップ更新のみ=Items を作り直さない
        await Work.HandOffAsync(added).ConfigureAwait(true); // 受け渡し=デフォルトスペースへ永続追加
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
        _editMode = false; _organizeMode = false; Organize.ResetState(); _workMode = false; _fileOpsMode = false; // 排他
        _selected.Clear(); _expandTag = null;
        ApplyModeTransition(); // ECO-114
    }

    /// <summary>削除モードを終了(選択クリア)。</summary>
    [RelayCommand]
    private void ExitDelete()
    {
        _deleteMode = false;
        _selected.Clear();
        ApplyModeTransition(); // ECO-114
    }

    // =====================================================================
    //  ファイル操作モード コマンド(ECO-112: ⋯メニュー「ファイル操作」=参照系)
    // =====================================================================
    /// <summary>⋯メニュー「ファイル操作」: ファイル操作モードに入る。他文脈モードと排他・選択クリア・
    /// メニューを閉じる(CAD「開始と終了」)。右ペインは開かない(ShowRightPane は edit/organize のみで不変)。</summary>
    [RelayCommand]
    private void EnterFileOps()
    {
        MoreMenuOpen = false;
        _fileOpsMode = true;
        _editMode = false; _organizeMode = false; Organize.ResetState(); _workMode = false; _deleteMode = false; // 排他
        _selected.Clear(); _expandTag = null;
        ApplyModeTransition(); // ECO-114
    }

    /// <summary>ファイル操作モードを終了(選択解除・CAD「終了時は選択を解除する」)。</summary>
    [RelayCommand]
    private void ExitFileOps()
    {
        _fileOpsMode = false;
        _selected.Clear();
        ClearCopyFeedback(); // モード離脱=フィードバック解除遷移(IMG-026②/ECO-104 教訓)
        ApplyModeTransition(); // ECO-114
    }

    /// <summary>選択画像の絶対パスをクリップボードへ(1 行 1 ファイル)。IMG-026① 裁定=
    /// 表示順(AllLoadedImagesInContext は表示と同じソート順)・OS ネイティブ改行・末尾改行なし。
    /// クリップボード失敗は落とさない(参照系=RevealInFileManager と対称・R8 所見 2-3)。</summary>
    [RelayCommand]
    private async Task CopyPaths()
    {
        if (!_fileOpsMode || _selected.Count == 0) return;
        var selSet = new HashSet<string>(_selected, StringComparer.Ordinal);
        var text = string.Join(Environment.NewLine,
            AllLoadedImagesInContext().Where(e => selSet.Contains(e.Record.Id)).Select(e => e.AbsolutePath));
        try { await _fileOps.CopyTextAsync(text).ConfigureAwait(true); }
        catch { return; } // コピー不能環境でもアプリを落とさない(フィードバックも出さない=成功表示の誤りを避ける)
        StartCopyFeedback();
    }

    /// <summary>IMG-026② 裁定: ボタン内一時表示(約2秒→自動復帰)。トースト新設なし。ラベルは表示時解決(ECO-106)。
    /// 復帰タイマはコマンド実行から切り離す(R8 所見 2-1: コマンド内 await だと AsyncRelayCommand の
    /// 並行実行禁止で表示中ボタンが :disabled 化し、フィードバック文言がグレーアウトする)。</summary>
    private void StartCopyFeedback()
    {
        _copyFeedbackCts?.Cancel();
        _copyFeedbackCts?.Dispose();
        _copyFeedbackCts = new CancellationTokenSource();
        _copyFeedback = true;
        OnPropertyChanged(string.Empty);
        _ = RevertCopyFeedbackAsync(_copyFeedbackCts.Token); // fire-and-forget(解除遷移はトークンで先着勝ち)
    }

    private async Task RevertCopyFeedbackAsync(CancellationToken ct)
    {
        try { await Task.Delay(CopyFeedbackDuration, ct).ConfigureAwait(true); }
        catch (TaskCanceledException) { return; } // 解除遷移(モード離脱/選択変化/再コピー)が先行した
        if (ct.IsCancellationRequested || !_copyFeedback) return;
        _copyFeedback = false;
        OnPropertyChanged(string.Empty);
    }

    /// <summary>コピー完了フィードバックの解除(タイマ以外の解除遷移=モード離脱・選択/マーカー変化から呼ぶ)。</summary>
    private void ClearCopyFeedback()
    {
        _copyFeedbackCts?.Cancel();
        _copyFeedbackCts?.Dispose();
        _copyFeedbackCts = null;
        if (!_copyFeedback) return;
        _copyFeedback = false;
        OnPropertyChanged(string.Empty);
    }

    /// <summary>選択 1 件の親フォルダを OS ファイルマネージャで開く(可能ならファイル選択状態=CAD「ファイルの場所を開く」)。
    /// 2 件以上ではボタン自体を出さない(VC-IMG-12③)ため、ここでも 1 件以外は無操作でガードする。</summary>
    [RelayCommand]
    private void OpenFileLocation()
    {
        if (!_fileOpsMode || _selected.Count != 1) return;
        var entry = EntryById(_selected[0]);
        if (entry is null) return;
        _fileOps.RevealInFileManager(entry.AbsolutePath);
    }

    /// <summary>選択中の normal 画像をゴミ箱へ移動(normal→deleted のソフト削除・物理非破壊 INV-009・復元可)。選択なしは無操作。
    /// 実行部は Trash 子 VM(MoveToTrashAsync)へ移送(ECO-036 第1段)。呼び出し順序(ids取得→子の移送実行→
    /// 選択クリア→ReloadImages→子.RefreshCount→Recompute)は移送前と同一に保持する。</summary>
    [RelayCommand]
    private async Task DeleteToTrash()
    {
        if (_selected.Count == 0) return;
        var ids = _selected.ToList();
        await Trash.MoveToTrashAsync(ids).ConfigureAwait(true);
        _selected.Clear();
        await ReloadImagesAsync().ConfigureAwait(true); // deleted は normal 母集合から外れる(REQ-053)
        await Trash.RefreshCountAsync().ConfigureAwait(true);
        Recompute();
    }

    /// <summary>グリッドで残したい1枚をマージ先にする(整理対象に入っていれば外す)。実体は Organize 子 VM(ECO-036 第3段)。</summary>
    [RelayCommand]
    private void SetMergeTarget(string imageId)
    {
        if (!_organizeMode) return;
        Organize.SetMergeTarget(imageId);
        RefreshSelectionMarkers(); // マージ先マーカー+トレイのみ=Items を作り直さない
    }

    private void ToggleOrganizeTarget(string imageId)
    {
        if (!_organizeMode || Organize.MergeTargetId is null || imageId == Organize.MergeTargetId) return;
        Organize.ToggleTarget(imageId);
        RefreshSelectionMarkers(); // 整理対象マーカー+トレイのみ=Items を作り直さない
    }

    /// <summary>マージ先の解除(ECO-056/CAD v2・A-2 裁定=REQ-067): 整理対象は保持し、マージ先のみ未設定へ。
    /// 実体は Organize 子 VM。通知は RefreshSelectionMarkers の一括通知(GF-055-01: 転送殻は全通知)。</summary>
    [RelayCommand]
    private void ClearMergeTarget()
    {
        if (!_organizeMode) return;
        Organize.ClearMergeTarget();
        RefreshSelectionMarkers(); // タイルの宛先マーカー+トレイのみ=Items を作り直さない
    }

    /// <summary>整理対象をすべて外す(ECO-056/v2 モック「すべて解除」)。マージ先は保持。</summary>
    [RelayCommand]
    private void ClearOrganizeTargets()
    {
        if (!_organizeMode) return;
        Organize.ClearTargets();
        RefreshSelectionMarkers();
    }

    /// <summary>検索結果からグリッドへ戻る(ECO-056/CAD backToGrid — v1 モック定義・51ad8ee から欠落)。
    /// 結果は保持(再検索まで不変=モック実測)・整理モードは維持。</summary>
    [RelayCommand]
    private void BackToGrid()
    {
        if (!_organizeMode) return;
        Organize.BackToGrid();
        RefreshSelectionMarkers(); // ShowSearchResults/ShowBrowseGrid の切替は一括通知で反映
    }

    /// <summary>「似た画像を探す」パネルの開閉(ECO-056/v2 3 ゾーン: 下部ピン内の折りたたみ)。</summary>
    [RelayCommand]
    private void ToggleSearchOpen()
    {
        Organize.ToggleSearchOpen();
        OnPropertyChanged(string.Empty); // 転送殻の全通知(GF-055-01)
    }

    [RelayCommand]
    private void RemoveOrganizeTarget(string imageId)
    {
        if (Organize.Targets.Contains(imageId)) { Organize.RemoveTarget(imageId); RefreshSelectionMarkers(); }
    }

    /// <summary>整理対象をマージ先へ昇格し、元のマージ先を整理対象へ戻す(モック「マージ先にする」)。</summary>
    [RelayCommand]
    private void PromoteToMergeTarget(string imageId)
    {
        if (!Organize.Targets.Contains(imageId)) return;
        Organize.PromoteToMergeTarget(imageId);
        RefreshSelectionMarkers();
    }

    [RelayCommand]
    private void SetSearchMethod(string method)
    {
        Organize.SetSearchMethod(method);
        if (!Organize.IsSimilarMethod) _scanNoticeKey = null;
        // GF-056-02: Recompute()(Items 全再構築)はグリッドをちらつかせる。検索方式はグリッド内容と
        // 無関係のため、転送殻の全通知のみで足りる(51ad8ee 以来の過剰再構築の是正)
        OnPropertyChanged(string.Empty);
    }

    /// <summary>似た画像を探す: 類似(E-SIMSEARCH-032)または条件(E-CRITERIA-037)。結果を中央ペインへ。
    /// 実行部は Organize 子 VM(RunSearchAsync)。途中の searching 表示更新は子へ注入した Recompute で現行位置を保持。</summary>
    [RelayCommand]
    private async Task RunSearch()
    {
        if (!_organizeMode || _collectionId is null) return;
        if (SimilarSearchBlocked)
        {
            _scanNoticeKey = "view.availableAfterScan";
            OnPropertyChanged(string.Empty);
            return;
        }
        _scanNoticeKey = null;
        await Organize.RunSearchAsync().ConfigureAwait(true);
        // 末尾通知は子の _recompute(注入)が旧版と同位置・同回数で発行済み — 殻では重複させない(G-E36S3)
    }

    [RelayCommand]
    private void CancelSearch() => Organize.CancelSearch();

    /// <summary>検索結果の候補を整理対象へ追加する(マージ先が前提・モック「整理対象に追加」)。実体は Organize 子 VM。</summary>
    [RelayCommand]
    private void AddCandidateToTargets(string imageId)
    {
        if (Organize.MergeTargetId is null || string.Equals(imageId, Organize.MergeTargetId, StringComparison.Ordinal)) return;
        Organize.AddCandidate(imageId);
        // ECO-125(B-1): マーカー付与のみ=Items を作り直さない(グリッドクリック側 SetMergeTarget/
        // ToggleOrganizeTarget と同様式=ECO-113。ClearCopyFeedback は RefreshSelectionMarkers 先頭が継承)
        RefreshSelectionMarkers();
    }

    /// <summary>マージ実行: E-MERGE-034(原子・タグ union・source=deleted・物理非破壊 INV-009)。完了後にデータ再読込。
    /// 実行部は Organize 子 VM(ExecuteMergeAsync・reloadImagesAsync 注入経由で ReloadImagesAsync を呼ぶ)。
    /// ガード(マージ先/整理対象なし)は現行どおりホスト側で先に確認し、不成立なら Recompute を呼ばない。</summary>
    [RelayCommand]
    private async Task ExecuteMerge()
    {
        if (!Organize.HasMergeTarget || !Organize.HasOrganizeTargets) return;
        await Organize.ExecuteMergeAsync().ConfigureAwait(true);
        // 末尾通知は子の _recompute(注入)が旧版と同位置・同回数で発行済み — 殻では重複させない(G-E36S3)
    }

    /// <summary>取り消す(ECO-044/IMG-011 裁定③): ログに基づく補償 Undo。実体は Organize 子 VM。</summary>
    [RelayCommand]
    private async Task UndoMerge()
    {
        if (!Organize.OrganizeDone) return;
        await Organize.UndoMergeAsync().ConfigureAwait(true);
        // 末尾通知は子の _recompute(注入)が発行済み — 殻では重複させない(G-E36S3)
    }

    /// <summary>別の整理を続ける: 完了状態を畳んでトレイをリセット(整理モードは維持)。</summary>
    [RelayCommand]
    private void ContinueOrganize()
    {
        Organize.ContinueOrganize();
        _selected.Clear();
        // ECO-125(B-2): データ不変=トレイリセット+マーカー解除のみ(旧マーカーは _markedItemIds の
        // 差分クリアが消す=ECO-113 構造。ClearCopyFeedback は RefreshSelectionMarkers 先頭が継承)
        RefreshSelectionMarkers();
    }

    private void EnsureScanOrder(string folderId)
    {
        if (_scanOrderByCollection.ContainsKey(folderId)) return;
        _scanOrderByCollection[folderId] = _allNormal
            .Where(image => string.Equals(image.SyncFolderId, folderId, StringComparison.Ordinal))
            .Select(image => image.Id)
            .ToList();
    }

    /// <summary>
    /// ECO-060: batchごとの全Items再構築(O(total×batch数))を避け、現在文脈に現れる新規行だけを末尾へ追加する。
    /// 初回画像はタグを持たないためFSタグfilter中は非表示。view軸は新規batchだけを既存条件で評価する。
    /// </summary>
    private void AppendPublishedToCurrentView(IReadOnlyList<ImageEntry> published)
    {
        if (_collectionId is null || published.Count == 0) return;
        var visible = new List<ImageEntry>();
        if (_axis == "view" && _viewRoot is not null)
        {
            var fullPath = new List<GraphNode> { _viewRoot };
            fullPath.AddRange(_viewPath);
            visible.AddRange(ViewMatched(fullPath, published));
        }
        else
        {
            var prefix = _fsPath.Count == 0 ? "" : string.Join("/", _fsPath) + "/";
            foreach (var entry in published)
            {
                var relative = entry.Record.RelativePath;
                if (prefix.Length > 0 && !relative.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                var remainder = prefix.Length > 0 ? relative[prefix.Length..] : relative;
                var slash = remainder.IndexOf('/');
                if (slash >= 0)
                {
                    var folderName = remainder[..slash];
                    if (_matchedFolders.All(folder => !string.Equals(folder.Name, folderName, StringComparison.OrdinalIgnoreCase)))
                    {
                        _matchedFolders.Add((folderName, 1));
                        var insertAt = Items.TakeWhile(item => item.IsFolder).Count();
                        Items.Insert(insertAt, new ImageItemVM(folderName, folderName, isFolder: true,
                            isPlaceholder: false, hasThumb: false, thumbBrush: null, selectable: false,
                            isSelected: false, hasTagDots: false, tagDots: [], sizeLabel: "—", dateLabel: "—",
                            target: folderName,
                            cells: [new ListCell(0, ListCellKind.BasicName, folderName, 0, null, true)]));
                    }
                    continue;
                }

                // 新規scan画像にタグは無いため、既存タグfilter選択中には一致しない。
                if (_tagFilter is null) visible.Add(entry);
            }
        }

        var selected = new HashSet<string>(_selected, StringComparer.Ordinal);
        foreach (var entry in visible)
        {
            _matchedFiles.Add(entry);
            var item = CreateImageItem(entry, selected);
            Items.Add(item);
            // ECO-113: スキャン段階公開の append でも差分更新インデックスを維持(新規 scan 画像は未選択)
            _itemById[item.Id] = item;
            _matchedIndexById[item.Id] = _matchedFiles.Count - 1;
        }

        CountLabel = _localization.T("view.itemCountLabel", new Dictionary<string, string> { ["count"] = (_matchedFolders.Count + _matchedFiles.Count).ToString() });
    }

    private void RebuildCollectionRows()
    {
        Collections.Clear();
        foreach (var collection in _collections)
        {
            Collections.Add(new CollectionRowVM(
                collection.Id,
                collection.Name,
                collection.Path,
                _collectionCounts.GetValueOrDefault(collection.Id),
                collection.Id == _collectionId,
                _scanningCollections.Contains(collection.Id)));
        }
    }

    private async void OnScanUpdated(object? sender, CollectionScanUpdate update)
    {
        try
        {
            switch (update.Phase)
            {
                case CollectionScanPhase.Started:
                    _scanningCollections.Add(update.FolderId);
                    _completedScanOrderByCollection.Remove(update.FolderId);
                    EnsureScanOrder(update.FolderId);
                    _scanNoticeKey = null;
                    // ECO-125(B-3・R8 所見3 で条件化): 表示中コレクション自身の再スキャン開始は
                    // 従来どおり Recompute — Started はソート実現状態の変異点(取込順固定=ECO-060 の
                    // TryGetPreservedScanOrder が有効化)であり、その即時実現を部分更新は担えない。
                    // 他コレクションの Started は母集合・ソートとも不変=行バッジ+通知のみ
                    // (BatchCommitted の部分更新様式と対称)。他コレクション分岐の ClearCopyFeedback
                    // 非継承は機構列挙(Recompute 先頭)からの縮小=タイマ後詰めで軽微(cheat-log 判定送り)
                    if (_loaded)
                    {
                        if (string.Equals(update.FolderId, _collectionId, StringComparison.Ordinal)) Recompute();
                        else { RebuildCollectionRows(); OnPropertyChanged(string.Empty); }
                    }
                    break;

                case CollectionScanPhase.BatchCommitted:
                    _scanningCollections.Add(update.FolderId);
                    EnsureScanOrder(update.FolderId);
                    var order = _scanOrderByCollection[update.FolderId];
                    var appended = new List<ImageEntry>();
                    foreach (var image in update.Images)
                    {
                        if (image.Status != ImageStatus.Normal || !_normalIds.Add(image.Id)) continue;
                        _allNormal.Add(image);
                        order.Add(image.Id);
                        _collectionCounts[image.SyncFolderId] = _collectionCounts.GetValueOrDefault(image.SyncFolderId) + 1;
                        if (string.Equals(_collectionId, image.SyncFolderId, StringComparison.Ordinal))
                        {
                            var entry = BuildEntry(image);
                            _entries.Add(entry);
                            _entryById[image.Id] = entry; // ECO-113 R8 所見1: 段階公開分も entry 解決から脱落させない
                            appended.Add(entry);
                        }
                    }
                    if (_loaded)
                    {
                        RebuildCollectionRows();
                        AppendPublishedToCurrentView(appended);
                        BuildContextPanels(new HashSet<string>(_selected, StringComparer.Ordinal));
                        OnPropertyChanged(string.Empty);
                    }
                    break;

                case CollectionScanPhase.Completed:
                    _scanningCollections.Remove(update.FolderId);
                    if (_scanOrderByCollection.Remove(update.FolderId, out var finalOrder) && _sortColKey is null)
                    {
                        _completedScanOrderByCollection[update.FolderId] = finalOrder;
                    }
                    else
                    {
                        _completedScanOrderByCollection.Remove(update.FolderId);
                    }
                    _scanNoticeKey = null;
                    await ReloadImagesAsync().ConfigureAwait(true);
                    if (_loaded) Recompute();
                    break;
            }
        }
        catch (Exception)
        {
            // scan通知の描画反映失敗でbackground scan自体を落とさない。完了時のDB再読込で再同期可能。
        }
    }

    /// <summary>マージ後またはscan完了後のデータ再読込(source は deleted 化=母集合から外れる)。</summary>
    private async Task ReloadImagesAsync()
    {
        if (_collectionId is null)
        {
            ClearContentData();
            return;
        }
        _allNormal = (await _images.GetNormalByFolderAsync(_collectionId).ConfigureAwait(true)).ToList();
        _normalIds = _allNormal.Select(image => image.Id).ToHashSet(StringComparer.Ordinal);
        _collectionCounts[_collectionId] = _allNormal.Count;
        await RefreshImageTagsAsync().ConfigureAwait(true);
        BuildEntries();
    }
}
