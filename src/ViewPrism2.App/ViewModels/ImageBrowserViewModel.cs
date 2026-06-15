using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;

namespace ViewPrism2.App.ViewModels;

/// <summary>言語切替で表示文言が追随する選択肢(ソートフィールド・方向の ComboBox 用)。</summary>
public abstract class LabeledOption<T> : ObservableObject
{
    private readonly LocalizationService _localization;
    private readonly string _labelKey;

    protected LabeledOption(LocalizationService localization, T value, string labelKey)
    {
        _localization = localization;
        Value = value;
        _labelKey = labelKey;
        localization.CultureChanged += (_, _) => OnPropertyChanged(nameof(Label));
    }

    public T Value { get; }

    public string Label => _localization.T(_labelKey);
}

/// <summary>整列フィールドの選択肢(XAML の x:DataType 用に非ジェネリック化)。</summary>
public sealed class SortFieldOption : LabeledOption<SortField>
{
    public SortFieldOption(LocalizationService localization, SortField value, string labelKey)
        : base(localization, value, labelKey)
    {
    }
}

/// <summary>整列方向の選択肢(XAML の x:DataType 用に非ジェネリック化)。</summary>
public sealed class SortDirectionOption : LabeledOption<SortDirection>
{
    public SortDirectionOption(LocalizationService localization, SortDirection value, string labelKey)
        : base(localization, value, labelKey)
    {
    }
}

/// <summary>グリッドの 1 行(K-AVALONIA: 1 行 = N セルの行リスト化で VirtualizingStackPanel に載せる)。</summary>
public sealed record GridRowViewModel(IReadOnlyList<ImageItemViewModel> Cells);

/// <summary>リスト表示の列ヘッダ+実列幅。</summary>
public sealed partial class ListColumnViewModel : ObservableObject
{
    public ListColumnViewModel(DisplayColumn spec, string header)
    {
        Spec = spec;
        _header = header;
    }

    public DisplayColumn Spec { get; }

    [ObservableProperty]
    private string _header;

    [ObservableProperty]
    private double _pixelWidth = 100;
}

/// <summary>リスト表示のセル(列参照+表示文字列)。</summary>
public sealed class ListCellViewModel
{
    public ListCellViewModel(ListColumnViewModel column, string text)
    {
        Column = column;
        Text = text;
    }

    public ListColumnViewModel Column { get; }

    public string Text { get; }
}

/// <summary>グリッド/リスト共通の画像アイテム(選択状態・選択順バッジを保持)。</summary>
public sealed partial class ImageItemViewModel : ObservableObject
{
    public ImageItemViewModel(ImageBrowserViewModel browser, ImageEntry entry)
    {
        Browser = browser;
        Entry = entry;
    }

    public ImageBrowserViewModel Browser { get; }

    public ImageEntry Entry { get; }

    public ImageRecord Record => Entry.Record;

    public string FileName => Record.FileName;

    public string AbsolutePath => Entry.AbsolutePath;

    /// <summary>
    /// ファイルサイズの整形文字列(DC-GRID-001/A-2、ByteSizeFormatter)。
    /// リスト列にはサイズ列があるが、グリッドセルには欠落していた(原典準拠で従テキストとして提示)。
    /// </summary>
    public string SizeText => ByteSizeFormatter.Format(Record.FileSize);

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>選択順(1 起点昇順、REQ-041 / G-1)。未選択は null。</summary>
    [ObservableProperty]
    private int? _selectionOrder;

    public string SelectionOrderText => SelectionOrder?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    public ObservableCollection<ListCellViewModel> ListCells { get; } = [];

    partial void OnSelectionOrderChanged(int? value) => OnPropertyChanged(nameof(SelectionOrderText));
}

/// <summary>
/// グリッド⇔リスト表示の中核 ViewModel(M-UI-013 v1.3、REQ-041/042、E-UI-GRID-022)。
/// 選択(クリック=単一、Ctrl+クリック=トグル+選択順バッジ、SHIFT+クリック=範囲 union)・
/// 空状態判定・レスポンシブ自動列(clamp(⌊幅/220⌋, 2, 8) — v1.3/ECO-002 CR-1)・整列を
/// UI から分離して unit 検査可能にする。描画は XAML 側(surface)。
/// </summary>
public sealed partial class ImageBrowserViewModel : ObservableObject
{
    /// <summary>セル間ギャップ(K-DESIGN: 8px)。</summary>
    public const double CellGap = 8;

    /// <summary>レスポンシブ列算出の基準セル幅(仕様 §2.6 v1.3: 列数 = clamp(⌊幅/220px⌋, 2, 8))。</summary>
    public const double ResponsiveCellWidth = 220;

    private readonly LocalizationService _localization;
    private readonly ImageSorter _sorter;
    private readonly List<ImageItemViewModel> _selection = [];
    private List<ImageItemViewModel> _items = [];
    private IReadOnlyList<DisplayColumn> _columnSpecs;
    private IReadOnlyDictionary<string, Tag> _tagById = new Dictionary<string, Tag>(StringComparer.Ordinal);

    public ImageBrowserViewModel(LocalizationService localization, ImageSorter sorter)
    {
        ArgumentNullException.ThrowIfNull(localization);
        ArgumentNullException.ThrowIfNull(sorter);
        _localization = localization;
        _sorter = sorter;
        _columnSpecs = DisplayColumnParser.Parse(null, _tagById);

        // v1.3/ECO-002 CR-4(REQ-038): created_date は UI ソート軸から除外。
        // SortField 列挙・整列実装・DB 既存値の受理は温存(後方互換は _legacySortField で扱う)
        SortFieldOptions =
        [
            new(localization, SortField.Name, "sort.name"),
            new(localization, SortField.ModifiedDate, "common.modifiedDate"),
            new(localization, SortField.FileSize, "sort.size"),
        ];
        SortDirectionOptions =
        [
            new(localization, SortDirection.Asc, "sort.ascending"),
            new(localization, SortDirection.Desc, "sort.descending"),
        ];
        _selectedSortFieldOption = SortFieldOptions[0];
        _selectedSortDirectionOption = SortDirectionOptions[0];

        localization.CultureChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(EmptyMessage));
            RebuildColumnHeaders();
            RebuildListCells();
        };
        RebuildColumns();
    }

    public IReadOnlyList<SortFieldOption> SortFieldOptions { get; }

    public IReadOnlyList<SortDirectionOption> SortDirectionOptions { get; }

    public ObservableCollection<GridRowViewModel> Rows { get; } = [];

    public ObservableCollection<ImageItemViewModel> ListItems { get; } = [];

    public ObservableCollection<ListColumnViewModel> Columns { get; } = [];

    /// <summary>
    /// グリッド列数(REQ-041 v1.3/CR-1): コンテンツ幅から自動算出 — clamp(⌊幅/220px⌋, 2, 8)。
    /// 固定の列数セレクタは置かない。幅が未供給(レイアウト前)の間は暫定値 4。
    /// </summary>
    [ObservableProperty]
    private int _gridColumns = 4;

    [ObservableProperty]
    private bool _isListMode;

    /// <summary>DB 既存値 created_date 等、UI 選択肢に無い整列フィールドの後方互換(REQ-038 v1.3)。</summary>
    private SortField? _legacySortField;

    [ObservableProperty]
    private SortFieldOption _selectedSortFieldOption;

    [ObservableProperty]
    private SortDirectionOption _selectedSortDirectionOption;

    /// <summary>一覧コンテンツ領域の幅(View 側が SizeChanged で供給)。</summary>
    [ObservableProperty]
    private double _viewportWidth;

    /// <summary>正方形セルの 1 辺(K-DESIGN: 正方形セル。ビューポート幅と列数から算出)。</summary>
    [ObservableProperty]
    private double _cellSize = 180;

    /// <summary>タグ編集モード中はダブルクリックのビューア起動を無効化する(REQ-041 v1.2)。</summary>
    [ObservableProperty]
    private bool _suppressOpenItem;

    /// <summary>空状態判定(仕様 §2.6: 画像 0 件 → 中央メッセージ)。</summary>
    public bool IsEmpty => _items.Count == 0;

    public string EmptyMessage => _localization.T("image.gridView.noImages");

    /// <summary>現在の整列結果(ビューア呼び出し元一覧、REQ-044)。</summary>
    public IReadOnlyList<ImageItemViewModel> SortedItems => _items;

    /// <summary>選択中アイテム(選択順)。</summary>
    public IReadOnlyList<ImageItemViewModel> Selection => _selection;

    /// <summary>最後に選択されたアイテム(詳細パネルの表示対象)。</summary>
    public ImageItemViewModel? LastSelected => _selection.Count > 0 ? _selection[^1] : null;

    public SortField SortField => _legacySortField ?? SelectedSortFieldOption.Value;

    public SortDirection SortDirection => SelectedSortDirectionOption.Value;

    /// <summary>レスポンシブ列算出(仕様 §2.6 v1.3/CR-1): 列数 = clamp(⌊コンテンツ幅/220px⌋, 2, 8)。</summary>
    public static int ComputeColumns(double contentWidth)
        => Math.Clamp((int)Math.Floor(contentWidth / ResponsiveCellWidth), 2, 8);

    public event EventHandler? SelectionChanged;

    /// <summary>ダブルクリックによる単一画像表示要求(REQ-044 のエントリポイント)。</summary>
    public event EventHandler<ImageItemViewModel>? OpenItemRequested;

    /// <summary>表示画像集合を差し替える(整列は現在のソート設定、選択はクリア)。</summary>
    public void SetImages(IReadOnlyList<ImageEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var byId = entries.ToDictionary(e => e.Record.Id, StringComparer.Ordinal);
        var sorted = _sorter.Sort(entries.Select(e => e.Record), SortField, SortDirection);
        _items = sorted.Select(r => new ImageItemViewModel(this, byId[r.Id])).ToList();

        ClearSelectionCore();
        RebuildRows();
        RebuildListCells();
        RefreshListItems();
        OnPropertyChanged(nameof(IsEmpty));
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>リスト表示列を設定する(view.display_columns、REQ-042)。</summary>
    public void SetColumns(string? displayColumnsJson, IReadOnlyDictionary<string, Tag> tagById)
    {
        ArgumentNullException.ThrowIfNull(tagById);
        _tagById = tagById;
        _columnSpecs = DisplayColumnParser.Parse(displayColumnsJson, tagById);
        RebuildColumns();
        RebuildListCells();
    }

    /// <summary>
    /// ソート初期値の設定(ビュー定義から。ビュー定義は変更しない=閲覧扱い、REQ-032)。
    /// UI 選択肢に無いフィールド(DB 既存値 created_date — REQ-038 v1.3 後方互換)は
    /// 従来どおり整列に使い、選択肢へは追加しない。
    /// </summary>
    public void SetSort(SortField field, SortDirection direction)
    {
        var option = SortFieldOptions.FirstOrDefault(o => o.Value == field);
        SelectedSortFieldOption = option ?? SortFieldOptions[0];
        SelectedSortDirectionOption = SortDirectionOptions.First(o => o.Value == direction);

        var legacy = option is null ? field : (SortField?)null;
        if (_legacySortField != legacy)
        {
            _legacySortField = legacy;
            Resort();
        }
    }

    /// <summary>
    /// アイテムへのポインタ操作(REQ-041 v1.3): クリック=単一選択 / Ctrl+クリック=トグル(選択順バッジ)/
    /// SHIFT+クリック=最後の選択アイテムからクリック位置までの index 範囲を既存選択へ追加
    /// (union・置換しない — v1.3/ECO-002 CR-3)/ ダブルクリック=単一画像表示。
    /// 空状態(アイテムなし)では呼ばれないため何もしない規則も満たす。
    /// </summary>
    public void HandleItemPointer(ImageItemViewModel item, bool ctrl, bool shift, bool isDoubleClick)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (isDoubleClick)
        {
            // タグ編集モード中はビューア起動無効(REQ-041 v1.2)
            if (!SuppressOpenItem)
            {
                OpenItemRequested?.Invoke(this, item);
            }

            return;
        }

        if (shift)
        {
            RangeSelect(item);
        }
        else if (ctrl)
        {
            ToggleSelect(item);
        }
        else
        {
            SingleSelect(item);
        }

        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearSelection()
    {
        ClearSelectionCore();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 画像集合の差し替え後に旧選択を選択順のまま復元する(タグ付与適用後の継続操作用)。
    /// 見つからない id は読み飛ばす(INV-008 のフォールバック方針)。
    /// </summary>
    public void RestoreSelection(IReadOnlyList<string> imageIdsInOrder)
    {
        ArgumentNullException.ThrowIfNull(imageIdsInOrder);
        ClearSelectionCore();
        var byId = _items.ToDictionary(i => i.Record.Id, StringComparer.Ordinal);
        foreach (var id in imageIdsInOrder)
        {
            if (byId.TryGetValue(id, out var item) && !item.IsSelected)
            {
                _selection.Add(item);
                item.IsSelected = true;
                item.SelectionOrder = _selection.Count;
            }
        }

        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>ビュー側コンテンツ幅の供給(セル辺・列幅の再計算)。</summary>
    public void UpdateViewportWidth(double width)
    {
        if (width <= 0 || Math.Abs(width - ViewportWidth) < 0.5)
        {
            return;
        }

        ViewportWidth = width;
    }

    private void SingleSelect(ImageItemViewModel item)
    {
        foreach (var selected in _selection)
        {
            selected.IsSelected = false;
            selected.SelectionOrder = null;
        }

        _selection.Clear();
        _selection.Add(item);
        item.IsSelected = true;
        item.SelectionOrder = 1;
    }

    /// <summary>
    /// SHIFT+クリックの範囲選択(REQ-041 v1.3/CR-3 — 原典 useImageSelection 方式):
    /// 最後の選択アイテムからクリック位置までの一覧 index 範囲を既存選択へ union する(置換しない)。
    /// 新規選択分の選択順は index 昇順で末尾へ追番。選択が空のときはクリック項目のみ追加。
    /// </summary>
    private void RangeSelect(ImageItemViewModel item)
    {
        var anchor = LastSelected;
        if (anchor is null)
        {
            // 既存選択なし: 範囲の縮退 = クリック項目のみを追加
            AddToSelection(item);
            return;
        }

        var from = _items.IndexOf(anchor);
        var to = _items.IndexOf(item);
        if (from < 0 || to < 0)
        {
            return; // 並び替え直後等で解決できない場合は何もしない(INV-008 のフォールバック方針)
        }

        var lower = Math.Min(from, to);
        var upper = Math.Max(from, to);
        for (var i = lower; i <= upper; i++)
        {
            if (!_items[i].IsSelected)
            {
                AddToSelection(_items[i]);
            }
        }
    }

    private void AddToSelection(ImageItemViewModel item)
    {
        _selection.Add(item);
        item.IsSelected = true;
        item.SelectionOrder = _selection.Count;
    }

    private void ToggleSelect(ImageItemViewModel item)
    {
        if (item.IsSelected)
        {
            item.IsSelected = false;
            item.SelectionOrder = null;
            _selection.Remove(item);
            Renumber();
        }
        else
        {
            _selection.Add(item);
            item.IsSelected = true;
            item.SelectionOrder = _selection.Count;
        }
    }

    private void Renumber()
    {
        // 選択順バッジは 1 起点昇順を保つ(G-1)
        for (var i = 0; i < _selection.Count; i++)
        {
            _selection[i].SelectionOrder = i + 1;
        }
    }

    private void ClearSelectionCore()
    {
        foreach (var selected in _selection)
        {
            selected.IsSelected = false;
            selected.SelectionOrder = null;
        }

        _selection.Clear();
    }

    partial void OnGridColumnsChanged(int value)
    {
        RebuildRows();
        UpdateCellSize();
    }

    partial void OnViewportWidthChanged(double value)
    {
        // レスポンシブ自動列(REQ-041 v1.3/CR-1): コンテンツ幅から列数を再算出する
        GridColumns = ComputeColumns(value);
        UpdateCellSize();
        UpdateColumnWidths();
    }

    partial void OnSelectedSortFieldOptionChanged(SortFieldOption value)
    {
        // ユーザー操作・初期化のどちらでも、選択肢が確定したら後方互換フィールドは解除する
        // (SetSort の created_date 経路は選択肢設定後に _legacySortField を再設定する)
        _legacySortField = null;
        Resort();
    }

    partial void OnSelectedSortDirectionOptionChanged(SortDirectionOption value) => Resort();

    private void Resort()
    {
        if (_items.Count == 0)
        {
            return;
        }

        var byId = _items.ToDictionary(i => i.Record.Id, StringComparer.Ordinal);
        var sorted = _sorter.Sort(_items.Select(i => i.Record), SortField, SortDirection);
        _items = sorted.Select(r => byId[r.Id]).ToList();
        RebuildRows();
        RefreshListItems();
    }

    private void RebuildRows()
    {
        Rows.Clear();
        var columns = Math.Max(1, GridColumns);
        for (var i = 0; i < _items.Count; i += columns)
        {
            Rows.Add(new GridRowViewModel(_items.Skip(i).Take(columns).ToList()));
        }
    }

    private void RefreshListItems()
    {
        ListItems.Clear();
        foreach (var item in _items)
        {
            ListItems.Add(item);
        }
    }

    private void UpdateCellSize()
    {
        if (ViewportWidth <= 0)
        {
            return;
        }

        var columns = Math.Max(1, GridColumns);
        // セル間ギャップ 8px(K-DESIGN)+選択枠 2px ぶんの余裕(列ごとに 4px)を除いた正方形辺
        var available = ViewportWidth - CellGap * (columns + 1) - 4 * columns;
        if (available > columns)
        {
            CellSize = Math.Floor(available / columns);
        }
    }

    private void RebuildColumns()
    {
        Columns.Clear();
        foreach (var spec in _columnSpecs)
        {
            Columns.Add(new ListColumnViewModel(spec, ResolveHeader(spec)));
        }

        UpdateColumnWidths();
    }

    private void RebuildColumnHeaders()
    {
        foreach (var column in Columns)
        {
            column.Header = ResolveHeader(column.Spec);
        }
    }

    private string ResolveHeader(DisplayColumn spec)
    {
        if (spec.Label is not null)
        {
            return spec.Label;
        }

        return spec.Kind == DisplayColumnKind.Basic
            ? _localization.T(spec.Key switch
            {
                "size" => "common.size",
                "modified_date" => "common.modifiedDate",
                _ => "common.name",
            })
            : (_tagById.TryGetValue(spec.Key, out var tag) ? tag.Name : spec.Key);
    }

    private void UpdateColumnWidths()
    {
        if (Columns.Count == 0 || ViewportWidth <= 0)
        {
            return;
        }

        // 残り列の star で全幅を按分(AUDIT-102。削除済みタグ列はパース時点で除外済み)
        var totalStar = Columns.Sum(c => c.Spec.Star);
        var available = Math.Max(0, ViewportWidth - 24);
        foreach (var column in Columns)
        {
            column.PixelWidth = Math.Floor(available * column.Spec.Star / totalStar);
        }
    }

    private void RebuildListCells()
    {
        foreach (var item in _items)
        {
            item.ListCells.Clear();
            foreach (var column in Columns)
            {
                item.ListCells.Add(new ListCellViewModel(column, CellText(item, column.Spec)));
            }
        }
    }

    private string CellText(ImageItemViewModel item, DisplayColumn spec)
    {
        if (spec.Kind == DisplayColumnKind.Basic)
        {
            return spec.Key switch
            {
                "size" => ByteSizeFormatter.Format(item.Record.FileSize),
                "modified_date" => LocaleFormats.FormatTimestamp(item.Record.ModifiedDate, _localization.CurrentLocale),
                _ => item.Record.FileName,
            };
        }

        var tagValue = item.Entry.Tags.FirstOrDefault(
            t => string.Equals(t.TagId, spec.Key, StringComparison.Ordinal));
        if (tagValue is null)
        {
            return string.Empty;
        }

        // simple タグ(value=NULL)は付与の有無のみ表示
        return tagValue.Value ?? "✓";
    }
}
