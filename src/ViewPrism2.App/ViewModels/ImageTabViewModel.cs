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

    public ImageTabViewModel(
        ISyncFolderRepository folders,
        IImageRepository images,
        ITagRepository tags,
        ImageSorter sorter,
        ViewService views,
        NodeGraphBuilder graphBuilder,
        PathConditionConverter pathConverter,
        ConditionEvaluator evaluator,
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
        // 編集モードの選択母集合=現在の文脈で表示中の画像。
        if (_axis == "view" && _viewRoot is not null)
        {
            var fullPath = new List<GraphNode> { _viewRoot };
            fullPath.AddRange(_viewPath);
            return ViewMatched(fullPath);
        }
        return ResolveFs().Files; // FS: 現在のフォルダに直接ある画像(サブフォルダは含めない)
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
            var tagsOf = ImgTagIds(e);
            var dots = (!_editMode && tagsOf.Count > 0)
                ? tagsOf.Take(3).Select(t => HexA(TagColor(_tagById.GetValueOrDefault(t)), 1)).ToList()
                : new List<IBrush>();
            Items.Add(new ImageItemVM(e.Record.Id, e.Record.FileName, isFolder: false, isPlaceholder: false,
                hasThumb: true, thumbBrush: null, selectable: _editMode, isSelected: selected,
                hasTagDots: !_editMode && tagsOf.Count > 0, tagDots: dots,
                sizeLabel: FmtSize(e.Record.FileSize), dateLabel: FmtDate(e.Record.ModifiedDate),
                target: null, absolutePath: e.AbsolutePath));
        }

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

        OnPropertyChanged(string.Empty);
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
    private void ToggleAxisMenu() { AxisMenuOpen = !AxisMenuOpen; SortMenuOpen = false; OnPropertyChanged(string.Empty); }

    public void CloseMenusFromDismiss()
    {
        if (!AxisMenuOpen && !SortMenuOpen) return;
        AxisMenuOpen = false; SortMenuOpen = false;
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
    private void ToggleSortMenu() { SortMenuOpen = !SortMenuOpen; AxisMenuOpen = false; OnPropertyChanged(string.Empty); }

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
        _editMode = !_editMode; _selected.Clear(); _expandTag = null; _panelTab = "current";
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

    public void HandleItemClick(ImageItemVM item, bool ctrl, bool shift)
    {
        if (item.IsFolder)
        {
            if (item.Target is not null) { _fsPath.Add(item.Target); _tagFilter = null; _selected.Clear(); Recompute(); }
            return;
        }
        if (!_editMode) return; // 閲覧時クリック=ビューア(本スコープ外)
        ToggleSelect(item.Id, ctrl, shift);
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
                Recompute();
                return;
            }
        }
        if (ctrl) { if (!_selected.Remove(id)) _selected.Add(id); Recompute(); return; }
        if (_selected.Count == 1 && _selected[0] == id) _selected.Clear();
        else { _selected.Clear(); _selected.Add(id); }
        Recompute();
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
}
