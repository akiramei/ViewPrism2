using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ViewPrism2.App.ViewModels;

/// <summary>
/// 画像タブ製造(M2)の golden ハーネス用シード VM。モック
/// (ViewPrismUI:資料/画像タブ/ViewPrism2 画像タブ.dc.html)の state/logic を
/// CommunityToolkit.Mvvm へ忠実移植したもの。backend には一切依存せず、Components.axaml の
/// 画像タブ部品(M1)をモック既定どおりに描画して golden-in-the-loop で突合する。
/// タグ色依存の塗りはここで IBrush として算出し View へバインドする(M1 部品はレイアウト担当)。
/// 実データ配線(SyncFolder/Image/View/Tag リポジトリ)は M3 で差し替える。
/// </summary>
public sealed partial class ImageTabSeedViewModel : ObservableObject
{
    // ---------------- 色ヘルパ(モック hexA / dot / hsl 相当) ----------------
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

    private static IBrush White { get; } = Brushes.White;

    private static Color HslColor(double h, double s, double l)
    {
        h = ((h % 360) + 360) % 360 / 360.0;
        double r, g, b;
        if (s == 0) { r = g = b = l; }
        else
        {
            double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            double p = 2 * l - q;
            r = HueToRgb(p, q, h + 1.0 / 3);
            g = HueToRgb(p, q, h);
            b = HueToRgb(p, q, h - 1.0 / 3);
        }
        return Color.FromRgb((byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255));
    }

    private static double HueToRgb(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1.0 / 6) return p + (q - p) * 6 * t;
        if (t < 1.0 / 2) return q;
        if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
        return p;
    }

    private static IBrush ThumbBrush(double hue) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(HslColor(hue, 0.58, 0.70), 0),
            new GradientStop(HslColor((hue + 50) % 360, 0.62, 0.52), 1),
        },
    };

    // ---------------- タグ定義(モック tagDefs) ----------------
    private sealed record TagDef(string Id, string Name, string Color, string Type, string[]? Values = null, int Min = 0, int Max = 0);

    private static readonly Dictionary<string, TagDef> TagDefs = new()
    {
        ["samplealbum"] = new("samplealbum", "サンプルアルバム", "#f2912b", "シンプル"),
        ["featured"] = new("featured", "おすすめ", "#8b5cf6", "シンプル"),
        ["fav"] = new("fav", "お気に入り", "#e5484d", "シンプル"),
        ["job"] = new("job", "職種", "#2f6bed", "テキスト", new[] { "風景", "人物", "静物", "抽象" }),
        ["arts"] = new("arts", "アーツ", "#12a594", "テキスト", new[] { "写真", "イラスト", "3D" }),
        ["gender"] = new("gender", "性別", "#e93d82", "テキスト", new[] { "男", "女" }),
        ["rating"] = new("rating", "評価", "#e8b931", "数値", Min: 1, Max: 5),
    };

    private static readonly string[] AddOrder = { "samplealbum", "featured", "fav", "job", "arts", "gender", "rating" };

    // ---------------- アイテムモデル ----------------
    private sealed class SeedItem
    {
        public string Id = "";
        public string Type = "image";   // "folder" | "image"
        public string Name = "";
        public string? Target;          // folder の遷移先
        public int Count;
        public double Size;
        public string Date = "";
        public string[] BaseTags = Array.Empty<string>();
        public bool Placeholder;
        public double Hue;
    }

    private readonly List<SeedItem> _efImages;
    private readonly List<SeedItem> _rootItems;

    // ---------------- 状態(モック state) ----------------
    private bool _collapsed;
    private string _collection = "pics";
    private string _axis = "fs";          // "fs" | "view"
    private readonly List<string> _fsPath = new();
    private readonly List<string> _viewPath = new();
    private string _layout = "grid";      // "grid" | "list"
    private string? _sortCol = "name";    // null | name | size | date
    private string _sortDir = "asc";      // asc | desc
    private string? _tagFilter;
    private bool _editMode;
    private readonly List<string> _selected = new();
    private string _panelTab = "current"; // current | add
    private string? _expandTag;
    private readonly Dictionary<string, HashSet<string>> _extraTags = new();
    private readonly Dictionary<string, string> _textValues = new(); // "imgId:tagId" -> value
    private readonly Dictionary<string, int> _ratings = new();

    public ImageTabSeedViewModel()
    {
        _efImages = BuildSampleAlbumImages();
        _rootItems = new List<SeedItem>
        {
            new() { Id = "d_samplealbum", Type = "folder", Target = "samplealbum", Name = "SAMPLE_ALBUM", Count = 34 },
            new() { Id = "d_studioshots", Type = "folder", Target = "studioshots", Name = "StudioShots", Count = 18 },
            new() { Id = "d_shots", Type = "folder", Target = "shots", Name = "スクリーンショット", Count = 53 },
            new() { Id = "img_yume", Type = "image", Name = "sample-photo.jpg", Size = 2.4, Date = "2025/11/02", Hue = 210, BaseTags = Array.Empty<string>() },
        };
        Recompute();
    }

    private static List<SeedItem> BuildSampleAlbumImages()
    {
        var featured = new HashSet<int> { 5, 10, 15, 20, 25, 30, 33 };
        var arr = new List<SeedItem>();
        for (int i = 1; i <= 34; i++)
        {
            var tags = new List<string> { "samplealbum" };
            if (featured.Contains(i)) tags.Add("featured");
            arr.Add(new SeedItem
            {
                Id = "ef" + i,
                Type = "image",
                Name = "ch_" + i.ToString("D3") + ".png",
                Hue = (i * 47) % 360,
                Placeholder = i % 8 == 0,
                Size = 0.6 + (i % 5) * 0.7,
                Date = $"2025/11/{((i % 27) + 1):D2}",
                BaseTags = tags.ToArray(),
            });
        }
        return arr;
    }

    private List<SeedItem> FolderListing(string target)
    {
        if (target == "samplealbum") return _efImages.Select(Clone).ToList();
        int n = target == "studioshots" ? 18 : 12;
        var arr = new List<SeedItem>();
        for (int i = 1; i <= n; i++)
        {
            arr.Add(new SeedItem
            {
                Id = target + "_" + i,
                Type = "image",
                Name = (target == "studioshots" ? "shot_" : "cap_") + i.ToString("D2") + ".png",
                Hue = (i * 71) % 360,
                Placeholder = i % 3 == 0,
                Size = 0.3 + (i % 4) * 0.4,
                Date = $"2025/10/{((i % 27) + 1):D2}",
                BaseTags = Array.Empty<string>(),
            });
        }
        return arr;
    }

    private static SeedItem Clone(SeedItem s) => new()
    {
        Id = s.Id, Type = s.Type, Name = s.Name, Target = s.Target, Count = s.Count,
        Size = s.Size, Date = s.Date, BaseTags = s.BaseTags, Placeholder = s.Placeholder, Hue = s.Hue,
    };

    private IReadOnlyList<string> ImgTags(SeedItem img)
    {
        var set = new HashSet<string>(img.BaseTags);
        if (_extraTags.TryGetValue(img.Id, out var extra)) set.UnionWith(extra);
        return set.ToList();
    }

    private static string FmtSize(double mb) => mb >= 1 ? $"{mb:0.0} MB" : $"{Math.Round(mb * 1024)} KB";

    private string FolderName(string id) => _rootItems.FirstOrDefault(r => r.Target == id)?.Name ?? id;

    // ---------------- context() 解決 ----------------
    private sealed record ChipSpec(string Id, int Count, bool Nav = false, string? Alias = null, string? Color = null);
    private sealed record Context(List<(string Id, string Name)> Crumbs, List<SeedItem> Items, List<ChipSpec> Chips, string ChipMode, int Count, bool AnyTagged);

    private Context Resolve()
    {
        if (_axis == "fs")
        {
            if (_fsPath.Count == 0)
                return new Context(new(), _rootItems.Select(Clone).ToList(), new(), "none", _rootItems.Count, false);

            var target = _fsPath[^1];
            var imgs = FolderListing(target);
            foreach (var im in imgs) im.Type = "image";
            bool anyTagged = imgs.Any(im => ImgTags(im).Count > 0);
            var counts = new Dictionary<string, int>();
            foreach (var im in imgs)
                foreach (var t in ImgTags(im))
                    counts[t] = counts.GetValueOrDefault(t) + 1;
            var chips = counts.Keys.Select(id => new ChipSpec(id, counts[id])).ToList();
            if (_tagFilter != null) imgs = imgs.Where(im => ImgTags(im).Contains(_tagFilter)).ToList();
            var crumbs = _fsPath.Select((id, idx) => (id, FolderName(id))).ToList();
            return new Context(crumbs, imgs, chips, anyTagged ? "fs" : "none", imgs.Count, anyTagged);
        }

        // view 軸(サンプルアルバム)
        var efClone = _efImages.Select(Clone).ToList();
        if (_viewPath.Count == 0)
        {
            var chips = new List<ChipSpec>
            {
                new("samplealbum", 34, Nav: true),
                new("rating", 0, Nav: true, Alias: "評価1", Color: "#e8b931"),
                new("rating", 0, Nav: true, Alias: "評価2", Color: "#e8b931"),
                new("rating", 0, Nav: true, Alias: "評価3", Color: "#e8b931"),
                new("rating", 0, Nav: true, Alias: "評価4", Color: "#e8b931"),
                new("rating", 0, Nav: true, Alias: "評価5", Color: "#e8b931"),
            };
            return new Context(new(), efClone, chips, "view", efClone.Count, true);
        }
        int featuredCount = efClone.Count(im => ImgTags(im).Contains("featured"));
        var viewChips = new List<ChipSpec>
        {
            new("job", 0, Nav: true),
            new("arts", 0, Nav: true),
            new("featured", featuredCount, Nav: true),
        };
        var viewCrumbs = new List<(string, string)> { ("samplealbum", "サンプルアルバム") };
        return new Context(viewCrumbs, efClone, viewChips, "view", efClone.Count, true);
    }

    private List<SeedItem> SortItems(List<SeedItem> items)
    {
        if (_sortCol == null) return items;
        var folders = items.Where(i => i.Type == "folder").ToList();
        var files = items.Where(i => i.Type != "folder").ToList();
        Comparison<SeedItem> cmp = (a, b) =>
        {
            int r = _sortCol switch
            {
                "name" => string.Compare(a.Name, b.Name, StringComparison.Ordinal),
                "size" => a.Size.CompareTo(b.Size),
                "date" => string.Compare(a.Date, b.Date, StringComparison.Ordinal),
                _ => 0,
            };
            return _sortDir == "asc" ? r : -r;
        };
        folders.Sort(cmp); files.Sort(cmp);
        return folders.Concat(files).ToList();
    }

    private List<SeedItem> AllLoadedImages() => Resolve().Items.Where(i => i.Type != "folder").ToList();

    // =====================================================================
    //  公開: 派生コレクション + スカラー(View がバインド)
    // =====================================================================
    public ObservableCollection<CollectionRowVM> Collections { get; } = new();
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
    public bool IsViewAxis => _axis == "view";
    public string AxisLabel => _axis == "fs" ? "ファイルシステム" : "サンプルアルバム";
    public bool AxisMenuOpen { get; private set; }
    public bool SortMenuOpen { get; private set; }
    public bool IsFsActive => _axis == "fs";
    public string SortLabel => _sortCol switch { "name" => "名前", "size" => "サイズ", "date" => "更新日", _ => "ソートなし" };
    public bool SortNoneActive => _sortCol == null;
    public bool SortNameActive => _sortCol == "name";
    public bool SortDateActive => _sortCol == "date";
    public bool SortSizeActive => _sortCol == "size";
    public bool SortEnabled => _sortCol != null;
    public double SortArrowAngle => _sortDir == "desc" ? 180 : 0;
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
    //  Recompute: 状態から全派生を再構築(選択/ナビ/モード変更のたびに呼ぶ)
    // =====================================================================
    private void Recompute()
    {
        var ctx = Resolve();
        var items = SortItems(ctx.Items);

        // ---- collections ----
        Collections.Clear();
        foreach (var c in SeedCollections)
        {
            bool active = c.Id == _collection;
            Collections.Add(new CollectionRowVM(c.Id, c.Name, c.Path, c.Count, active));
        }

        // ---- breadcrumb ----
        HomeActive = ctx.Crumbs.Count == 0;
        Crumbs.Clear();
        for (int i = 0; i < ctx.Crumbs.Count; i++)
        {
            bool last = i == ctx.Crumbs.Count - 1;
            Crumbs.Add(new CrumbVM(ctx.Crumbs[i].Name, last, i));
        }
        CountLabel = $"{ctx.Count} 項目";

        // ---- chips ----
        Chips.Clear();
        ShowChips = false; ShowChipHint = false; ShowEmptyTagNote = false;
        if (ctx.ChipMode == "fs")
        {
            ShowChips = true; ShowChipHint = true; ChipHintLabel = "タグで絞り込み";
            Chips.Add(ChipVM.Neutral("クリア", _tagFilter == null));
            foreach (var c in ctx.Chips)
            {
                var def = TagDefs[c.Id];
                bool act = _tagFilter == c.Id;
                Chips.Add(ChipVM.Colored(c.Id, def.Name, def.Color, c.Count, act, isNav: false));
            }
        }
        else if (ctx.ChipMode == "view")
        {
            ShowChips = true; ShowChipHint = true; ChipHintLabel = "階層を掘る";
            foreach (var c in ctx.Chips)
            {
                var def = TagDefs.GetValueOrDefault(c.Id) ?? TagDefs["rating"];
                var color = c.Color ?? def.Color;
                var label = c.Alias ?? def.Name;
                Chips.Add(ChipVM.Colored(c.Id, label, color, c.Count, active: false, isNav: true));
            }
        }
        else
        {
            ShowEmptyTagNote = _axis == "fs" && _fsPath.Count > 0;
        }

        // ---- items(grid/list 共通) ----
        var selSet = new HashSet<string>(_selected);
        Items.Clear();
        foreach (var it in items)
        {
            bool isFolder = it.Type == "folder";
            bool selectable = _editMode && !isFolder;
            bool selected = selSet.Contains(it.Id);
            var tagsOf = isFolder ? new List<string>() : ImgTags(it).ToList();
            var dots = (!_editMode && tagsOf.Count > 0)
                ? tagsOf.Take(3).Select(t => HexA(TagDefs.GetValueOrDefault(t)?.Color ?? "#aaaaaa", 1)).ToList()
                : new List<IBrush>();
            bool hasThumb = !isFolder && !it.Placeholder;
            Items.Add(new ImageItemVM(
                it.Id, it.Name, isFolder, !isFolder && it.Placeholder, hasThumb,
                hasThumb ? ThumbBrush(it.Hue) : null,
                selectable, selected,
                !_editMode && tagsOf.Count > 0, dots,
                isFolder ? "—" : FmtSize(it.Size),
                isFolder ? "—" : it.Date,
                it.Target));
        }

        // ---- tag edit panel ----
        var selectedImgs = AllLoadedImages().Where(im => selSet.Contains(im.Id)).ToList();
        CurrentTags.Clear();
        if (selectedImgs.Count > 0)
        {
            var common = ImgTags(selectedImgs[0]).Where(t => selectedImgs.All(im => ImgTags(im).Contains(t)));
            foreach (var tid in common)
            {
                var d = TagDefs[tid];
                CurrentTags.Add(new CurrentTagVM(tid, d.Name, HexA(d.Color, 1),
                    HexA(d.Color, 0.12), HexA(d.Color, 0.28), HexA(d.Color, 1)));
            }
        }
        CurrentNote = selectedImgs.Count > 1 ? "選択画像に共通するタグ" : "この画像に付いているタグ";
        NoCurrentLabel = selectedImgs.Count > 1 ? "共通のタグはありません。" : "まだタグがありません。";

        // ---- add groups ----
        AddGroups.Clear();
        var groupDefs = new (string Key, string Label, string Hint, string Fg, string Bg)[]
        {
            ("シンプル", "シンプル", "タグ名のみ", "#5b6473", "#f0f2f6"),
            ("テキスト", "テキスト", "候補値から選ぶ", "#2459cf", "#eaf1fe"),
            ("数値", "数値", "値を選ぶ", "#0f8a5e", "#eafaf3"),
        };
        foreach (var g in groupDefs)
        {
            var rows = new List<AddRowVM>();
            foreach (var id in AddOrder.Where(id => TagDefs[id].Type == g.Key))
            {
                var d = TagDefs[id];
                bool added = selectedImgs.Count > 0 && selectedImgs.All(im => ImgTags(im).Contains(id)) && d.Type == "シンプル";
                bool expandable = d.Type != "シンプル";
                bool expanded = _expandTag == id;
                var row = new AddRowVM(id, d.Name, added, expandable, d.Type == "シンプル" && !added, expanded)
                {
                    NameBrush = added ? Solid("#aab1bd") : Solid("#1f2937"),
                    DotBrush = HexA(d.Color, 1),
                    DotOpacity = added ? 0.4 : 1.0,
                    RowBackground = expanded ? HexA(d.Color, 0.06) : (added ? Solid("#f9fafb") : Solid("#ffffff")),
                    RowBorderBrush = expanded ? HexA(d.Color, 0.4) : Solid("#e8ebf0"),
                };
                if (expandable && expanded && d.Type == "テキスト" && d.Values != null)
                {
                    foreach (var v in d.Values)
                    {
                        bool setNow = selectedImgs.Count > 0 && selectedImgs.All(im => _textValues.GetValueOrDefault(im.Id + ":" + id) == v);
                        row.ValueChips.Add(new ValueChipVM(id, v, setNow,
                            setNow ? Solid(d.Color) : HexA(d.Color, 0.1),
                            HexA(d.Color, setNow ? 1 : 0.28),
                            setNow ? White : HexA(d.Color, 1)));
                    }
                }
                if (expandable && expanded && d.Type == "数値")
                {
                    int? cur = CommonRating(selectedImgs);
                    row.NumRange = $"{d.Min}–{d.Max}";
                    row.NumCurrent = cur != null ? $"★ {cur}" : "未設定";
                    for (int v = d.Min; v <= d.Max; v++)
                    {
                        bool on = cur == v;
                        row.NumCells.Add(new NumCellVM(id, v,
                            on ? Solid(d.Color) : HexA(d.Color, 0.12),
                            HexA(d.Color, on ? 1 : 0.3),
                            on ? White : Solid("#9a7b1a")));
                    }
                }
                rows.Add(row);
            }
            if (rows.Count > 0)
                AddGroups.Add(new AddGroupVM(g.Label, g.Hint, Solid(g.Bg), Solid(g.Fg), rows));
        }

        OnPropertyChanged(string.Empty);
    }

    private int? CommonRating(List<SeedItem> imgs)
    {
        if (imgs.Count == 0) return null;
        if (!_ratings.TryGetValue(imgs[0].Id, out var first)) return null;
        return imgs.All(im => _ratings.GetValueOrDefault(im.Id, int.MinValue) == first) ? first : null;
    }

    private static readonly (string Id, string Name, int Count, string Path)[] SeedCollections =
    {
        ("pics", "画像", 105, @"C:\Demo\OneDrive\画像"),
        ("shots", "スクリーンショット", 53, @"C:\Demo\Pictures\Screenshots"),
        ("dl", "ダウンロード", 213, @"C:\Demo\Downloads"),
        ("camera", "カメラ", 67, @"C:\Demo\Pictures\Camera"),
        ("desk", "デスクトップ", 12, @"C:\Demo\Desktop"),
    };

    // =====================================================================
    //  コマンド(モックのハンドラ相当)
    // =====================================================================
    [RelayCommand]
    private void SelectCollection(string id)
    {
        _collection = id; _fsPath.Clear(); _viewPath.Clear(); _tagFilter = null;
        Recompute();
    }

    [RelayCommand]
    private void ToggleSidebar() { _collapsed = !_collapsed; Recompute(); }

    [RelayCommand]
    private void ToggleAxisMenu() { AxisMenuOpen = !AxisMenuOpen; SortMenuOpen = false; OnPropertyChanged(string.Empty); }

    /// <summary>Popup のライトディスミス(外側クリック)で閉じた時に VM 状態を同期する。</summary>
    public void CloseMenusFromDismiss()
    {
        if (!AxisMenuOpen && !SortMenuOpen) return;
        AxisMenuOpen = false; SortMenuOpen = false;
        OnPropertyChanged(string.Empty);
    }

    [RelayCommand]
    private void SelectAxis(string axis)
    {
        _axis = axis; AxisMenuOpen = false; _fsPath.Clear(); _viewPath.Clear(); _tagFilter = null;
        Recompute();
    }

    [RelayCommand]
    private void ToggleSortMenu() { SortMenuOpen = !SortMenuOpen; AxisMenuOpen = false; OnPropertyChanged(string.Empty); }

    [RelayCommand]
    private void SelectSort(string? col) { _sortCol = string.IsNullOrEmpty(col) ? null : col; SortMenuOpen = false; Recompute(); }

    [RelayCommand]
    private void ToggleSortDir() { if (_sortCol != null) { _sortDir = _sortDir == "asc" ? "desc" : "asc"; Recompute(); } }

    [RelayCommand]
    private void SetGrid() { _layout = "grid"; Recompute(); }

    [RelayCommand]
    private void SetList() { _layout = "list"; Recompute(); }

    [RelayCommand]
    private void ToggleEdit()
    {
        _editMode = !_editMode; _selected.Clear(); _expandTag = null; _panelTab = "current";
        Recompute();
    }

    [RelayCommand]
    private void GoHome()
    {
        if (_axis == "fs") { _fsPath.Clear(); _tagFilter = null; } else { _viewPath.Clear(); }
        Recompute();
    }

    [RelayCommand]
    private void GoCrumb(int depth)
    {
        if (_axis == "fs") { Trim(_fsPath, depth + 1); _tagFilter = null; } else { Trim(_viewPath, depth + 1); }
        Recompute();
    }

    private static void Trim(List<string> list, int len) { while (list.Count > len) list.RemoveAt(list.Count - 1); }

    [RelayCommand]
    private void ClickChip(ChipVM chip)
    {
        if (chip.IsNeutral) { _tagFilter = null; }
        else if (chip.IsNav)
        {
            if (chip.Id == "samplealbum") { _viewPath.Clear(); _viewPath.Add("samplealbum"); }
        }
        else { _tagFilter = _tagFilter == chip.Id ? null : chip.Id; }
        Recompute();
    }

    public void HandleItemClick(ImageItemVM item, bool ctrl, bool shift)
    {
        if (item.IsFolder)
        {
            if (item.Target != null) { _fsPath.Add(item.Target); _tagFilter = null; Recompute(); }
            return;
        }
        if (!_editMode) return; // 閲覧時クリック=ビューア(スコープ外)
        ToggleSelect(item.Id, ctrl, shift);
    }

    private void ToggleSelect(string id, bool ctrl, bool shift)
    {
        var list = AllLoadedImages().Select(i => i.Id).ToList();
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
        if (ctrl)
        {
            if (!_selected.Remove(id)) _selected.Add(id);
            Recompute();
            return;
        }
        if (_selected.Count == 1 && _selected[0] == id) _selected.Clear();
        else { _selected.Clear(); _selected.Add(id); }
        Recompute();
    }

    [RelayCommand]
    private void TabCurrent() { _panelTab = "current"; Recompute(); }

    [RelayCommand]
    private void TabAdd() { _panelTab = "add"; _expandTag = null; Recompute(); }

    [RelayCommand]
    private void ClickAddRow(AddRowVM row)
    {
        if (row.Added) return;
        if (!row.Expandable) { AddTag(row.Id); }
        else { _expandTag = _expandTag == row.Id ? null : row.Id; Recompute(); }
    }

    [RelayCommand]
    private void ApplyTextValue(ValueChipVM chip)
    {
        foreach (var id in _selected)
        {
            (_extraTags[id] ??= new()).Add(chip.TagId);
            _textValues[id + ":" + chip.TagId] = chip.Value;
        }
        Recompute();
    }

    [RelayCommand]
    private void ApplyRating(NumCellVM cell)
    {
        foreach (var id in _selected)
        {
            _ratings[id] = cell.Value;
            (_extraTags[id] ??= new()).Add("rating");
        }
        Recompute();
    }

    [RelayCommand]
    private void RemoveCurrentTag(CurrentTagVM tag)
    {
        foreach (var id in _selected)
            if (_extraTags.TryGetValue(id, out var set)) set.Remove(tag.Id);
        Recompute();
    }

    private void AddTag(string tid)
    {
        foreach (var id in _selected)
            (_extraTags[id] ??= new()).Add(tid);
        Recompute();
    }
}

// ============================ 子 VM(データホルダ) ============================

public sealed class CollectionRowVM
{
    public CollectionRowVM(string id, string name, string path, int count, bool isSelected)
    { Id = id; Name = name; Path = path; CountText = count.ToString(); IsSelected = isSelected; }
    public string Id { get; }
    public string Name { get; }
    public string Path { get; }
    public string CountText { get; }
    public bool IsSelected { get; }
}

public sealed class CrumbVM
{
    public CrumbVM(string name, bool isLast, int depth) { Name = name; IsLast = isLast; Depth = depth; }
    public string Name { get; }
    public bool IsLast { get; }
    public int Depth { get; }
}

/// <summary>表示軸セレクタのメニュー項目(M3b: FS + 保存ビュー)。</summary>
public sealed class AxisOptionVM
{
    public AxisOptionVM(string id, string label, string sub, bool isView, bool isActive)
    { Id = id; Label = label; Sub = sub; IsView = isView; IsActive = isActive; }
    public string Id { get; }
    public string Label { get; }
    public string Sub { get; }
    public bool IsView { get; }
    public bool IsActive { get; }
}

public sealed class ChipVM
{
    private ChipVM() { }
    public string Id { get; private init; } = "";
    public string Label { get; private init; } = "";
    public bool HasDot { get; private init; }
    public bool HasCount { get; private init; }
    public string Count { get; private init; } = "";
    public bool IsNav { get; private init; }
    public bool IsNeutral { get; private init; }
    public IBrush? DotBrush { get; private init; }
    public IBrush Background { get; private init; } = Brushes.White;
    public IBrush BorderBrush { get; private init; } = Brushes.Transparent;
    public IBrush LabelBrush { get; private init; } = Brushes.Black;
    public IBrush CountBackground { get; private init; } = Brushes.Transparent;
    public IBrush CountForeground { get; private init; } = Brushes.Black;

    private static Color Hex(string hex)
    {
        var h = hex.TrimStart('#');
        var n = Convert.ToInt32(h, 16);
        return Color.FromRgb((byte)((n >> 16) & 255), (byte)((n >> 8) & 255), (byte)(n & 255));
    }
    private static IBrush A(string hex, double a) { var c = Hex(hex); return new SolidColorBrush(Color.FromArgb((byte)Math.Round(a * 255), c.R, c.G, c.B)); }
    private static IBrush S(string hex) => new SolidColorBrush(Hex(hex));

    public static ChipVM Neutral(string label, bool active) => new()
    {
        Id = "__clear", Label = label, IsNeutral = true, HasDot = false, HasCount = false,
        Background = active ? S("#eaf1fe") : S("#ffffff"),
        BorderBrush = active ? S("#cfe0fc") : S("#e3e7ee"),
        LabelBrush = active ? S("#2f6bed") : S("#6b7480"),
    };

    public static ChipVM Colored(string id, string label, string color, int count, bool active, bool isNav) => new()
    {
        Id = id, Label = label, IsNav = isNav, HasDot = true, HasCount = true, Count = count.ToString(),
        DotBrush = S(color),
        Background = active ? A(color, 0.14) : S("#ffffff"),
        BorderBrush = active ? A(color, 0.45) : S("#e3e7ee"),
        LabelBrush = active ? A(color, 1) : S("#3a4150"),
        CountBackground = active ? S(color) : A(color, 0.14),
        CountForeground = active ? Brushes.White : A(color, 1),
    };
}

public sealed partial class ImageItemVM : ObservableObject
{
    public ImageItemVM(string id, string name, bool isFolder, bool isPlaceholder, bool hasThumb,
        IBrush? thumbBrush, bool selectable, bool isSelected, bool hasTagDots, List<IBrush> tagDots,
        string sizeLabel, string dateLabel, string? target, string? absolutePath = null,
        int? selectionOrder = null, bool isMergeTarget = false, bool isOrganizeTarget = false)
    {
        Id = id; Name = name; IsFolder = isFolder; IsPlaceholder = isPlaceholder; HasThumb = hasThumb;
        ThumbBrush = thumbBrush; Selectable = selectable; _isSelected = isSelected;
        HasTagDots = hasTagDots; TagDots = tagDots; SizeLabel = sizeLabel; DateLabel = dateLabel;
        Target = target; AbsolutePath = absolutePath; _selectionOrder = selectionOrder;
        _isMergeTarget = isMergeTarget; _isOrganizeTarget = isOrganizeTarget;
    }
    public string Id { get; }
    public string Name { get; }
    public bool IsFolder { get; }
    public bool IsPlaceholder { get; }
    public bool HasThumb { get; }
    public IBrush? ThumbBrush { get; }
    public bool Selectable { get; }
    public bool HasTagDots { get; }
    public List<IBrush> TagDots { get; }
    public string SizeLabel { get; }
    public string DateLabel { get; }
    public string? Target { get; }
    /// <summary>実画像の絶対パス(M3: ThumbnailImage 用)。シードハーネスでは null(ThumbBrush 使用)。</summary>
    public string? AbsolutePath { get; }
    public bool HasRealThumb => AbsolutePath is not null;

    // ---- 選択マーカー(可変・その場更新): Items 全再構築を避けクリック応答性を保つ ----
    [ObservableProperty] private bool _isSelected;
    /// <summary>タグ編集/作業モードの選択順(1 起点・REQ-041 CR-3)。未選択は null。連番付与の順序を可視化する。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectionOrderText))]
    private int? _selectionOrder;
    public string SelectionOrderText => SelectionOrder?.ToString() ?? "";
    /// <summary>整理モード(ECO-014): このセルがマージ先(残す1枚)か。</summary>
    [ObservableProperty] private bool _isMergeTarget;
    /// <summary>整理モード(ECO-014): このセルが整理対象(統合し削除対象)か。</summary>
    [ObservableProperty] private bool _isOrganizeTarget;

    /// <summary>選択/整理マーカーをその場更新(Items を作り直さない)。</summary>
    public void SetSelectionMarkers(bool isSelected, int? selectionOrder, bool isMergeTarget, bool isOrganizeTarget)
    {
        IsSelected = isSelected;
        SelectionOrder = selectionOrder;
        IsMergeTarget = isMergeTarget;
        IsOrganizeTarget = isOrganizeTarget;
    }
}

public sealed class AddGroupVM
{
    public AddGroupVM(string label, string hint, IBrush labelBg, IBrush labelFg, List<AddRowVM> tags)
    { Label = label; Hint = hint; LabelBackground = labelBg; LabelForeground = labelFg; Tags = tags; }
    public string Label { get; }
    public string Hint { get; }
    public IBrush LabelBackground { get; }
    public IBrush LabelForeground { get; }
    public List<AddRowVM> Tags { get; }
}

public sealed class AddRowVM
{
    public AddRowVM(string id, string name, bool added, bool expandable, bool plain, bool expanded)
    { Id = id; Name = name; Added = added; Expandable = expandable; Plain = plain; Expanded = expanded; }
    public string Id { get; }
    public string Name { get; }
    public bool Added { get; }
    public bool Expandable { get; }
    public bool Plain { get; }
    public bool Expanded { get; }
    public bool ShowCaret => Expandable;
    public bool ShowText => Expanded && ValueChips.Count > 0;
    public bool ShowNum => Expanded && NumCells.Count > 0;
    public double CaretAngle => Expanded ? 180 : 0;
    public IBrush NameBrush { get; set; } = Brushes.Black;
    public IBrush DotBrush { get; set; } = Brushes.Gray;
    public double DotOpacity { get; set; } = 1.0;
    public IBrush RowBackground { get; set; } = Brushes.White;
    public IBrush RowBorderBrush { get; set; } = Brushes.Transparent;
    public string NumRange { get; set; } = "";
    public string NumCurrent { get; set; } = "";
    public List<ValueChipVM> ValueChips { get; } = new();
    public List<NumCellVM> NumCells { get; } = new();
}

public sealed class ValueChipVM
{
    public ValueChipVM(string tagId, string value, bool isSelected, IBrush bg, IBrush border, IBrush fg)
    { TagId = tagId; Value = value; IsSelected = isSelected; Background = bg; BorderBrush = border; Foreground = fg; }
    public string TagId { get; }
    public string Value { get; }
    public string Label => Value;
    public bool IsSelected { get; }
    public IBrush Background { get; }
    public IBrush BorderBrush { get; }
    public IBrush Foreground { get; }
}

public sealed class NumCellVM
{
    public NumCellVM(string tagId, int value, IBrush bg, IBrush border, IBrush fg)
    { TagId = tagId; Value = value; Background = bg; BorderBrush = border; Foreground = fg; }
    public string TagId { get; }
    public int Value { get; }
    /// <summary>表示・付与する値文字列(実 VM は非整数も扱うため明示)。null ならシードの整数値。</summary>
    public string? ValueText { get; set; }
    public string Label => ValueText ?? Value.ToString();
    public IBrush Background { get; }
    public IBrush BorderBrush { get; }
    public IBrush Foreground { get; }
}

public sealed class CurrentTagVM
{
    public CurrentTagVM(string id, string label, IBrush dot, IBrush pillBg, IBrush pillBorder, IBrush pillFg)
    { Id = id; Label = label; DotBrush = dot; PillBackground = pillBg; PillBorderBrush = pillBorder; PillForeground = pillFg; }
    public string Id { get; }
    public string Label { get; }
    public IBrush DotBrush { get; }
    public IBrush PillBackground { get; }
    public IBrush PillBorderBrush { get; }
    public IBrush PillForeground { get; }
}

/// <summary>整理トレイのスロット(マージ先 / 整理対象)に表示する 1 枚(ECO-014)。</summary>
public sealed class OrganizeSlotVM
{
    public OrganizeSlotVM(string id, string name, string? absolutePath, string sizeLabel)
    { Id = id; Name = name; AbsolutePath = absolutePath; SizeLabel = sizeLabel; }
    public string Id { get; }
    public string Name { get; }
    public string? AbsolutePath { get; }
    public bool HasThumb => AbsolutePath is not null;
    public string SizeLabel { get; }
}

/// <summary>整理モードの検索結果候補(中央ペイン・一致率付き)(ECO-014)。</summary>
public sealed class OrganizeResultVM
{
    public OrganizeResultVM(string id, string name, string? absolutePath, string sizeLabel, int score, bool isCriteria, bool added)
    { Id = id; Name = name; AbsolutePath = absolutePath; SizeLabel = sizeLabel; Score = score; IsCriteria = isCriteria; Added = added; }
    public string Id { get; }
    public string Name { get; }
    public string? AbsolutePath { get; }
    public bool HasThumb => AbsolutePath is not null;
    public string SizeLabel { get; }
    public int Score { get; }
    public bool IsCriteria { get; }
    /// <summary>一致率表示。条件検索は「条件一致」、類似は「N%」。</summary>
    public string ScoreText => IsCriteria ? "条件一致" : $"{Score}%";
    public bool Added { get; }
}
