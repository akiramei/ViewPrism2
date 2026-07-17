using System.Globalization;
using ViewPrism2.Core.Models;

namespace ViewPrism2.App.ViewModels;

/// <summary>ファイル一覧セルの描画種別(ECO-025 β・REQ-081)。kind がセル描画とソート規則を決める。</summary>
public enum ListCellKind
{
    BasicName,
    BasicSize,
    BasicDate,
    Num,
    Text,
    Simple,
}

/// <summary>
/// ファイル一覧の列定義 1 件(ECO-025 β)。アクティブなビューの display_columns 由来。
/// <see cref="Width"/> は Avalonia の Grid 列文字列(名前=伸縮 "1.7*"・他=固定 px)。
/// </summary>
public sealed record ListColumnDef(
    string Key,
    string? TagId,
    ListCellKind Kind,
    string Label,
    string? Color,
    string Width,
    bool IsNameLocked);

/// <summary>ファイル一覧のヘッダー列 1 件(ソートトグル・ECO-025 β)。Grid.Column=<see cref="ColumnIndex"/> に配置。</summary>
public sealed record ListColumnHeaderVM(
    int ColumnIndex,
    string Key,
    string Label,
    bool IsSortActive,
    double ArrowAngle);

/// <summary>
/// アイコン(グリッド)表示の「並び替え」メニュー候補 1 件(ECO-025 β・FL-003 v2)。候補=ビューの表示列。
/// 種別チップ+タグ色ドット・アクティブ強調+方向矢印を持つ。
/// </summary>
public sealed record SortOptionVM(
    string Key,
    string Label,
    string KindChip,
    string? Color,
    bool IsActive,
    double ArrowAngle)
{
    public bool ShowColorDot => Color is not null;
}

/// <summary>ファイル一覧セル 1 件(行×列)。kind に応じて XAML が描画する。</summary>
public sealed record ListCell(
    int ColumnIndex,
    ListCellKind Kind,
    string Text,
    int Stars,
    string? Color,
    bool HasValue)
{
    // XAML の kind 別テンプレ切替(enum 等価をバインドで書かずに済ませる)
    public bool IsBasic => Kind is ListCellKind.BasicName or ListCellKind.BasicSize or ListCellKind.BasicDate;
    public bool IsNum => Kind == ListCellKind.Num;
    public bool IsTextCell => Kind == ListCellKind.Text;
    public bool IsSimple => Kind == ListCellKind.Simple;

    /// <summary>数値セルの ★ 表示(値ぶんの星)。未設定は空。</summary>
    public string StarsText => HasValue ? new string('★', Stars) : string.Empty;

    /// <summary>未設定(空値)か。数値=「未設定」/テキスト=「—」/シンプル=オフ の淡色表示に使う。</summary>
    public bool IsEmpty => !HasValue;
}

/// <summary>
/// ファイル一覧の列/セル構築(ECO-025 β・REQ-081・純粋/決定論・unit 検査可能)。
///
/// - 列 = アクティブなビューの display_columns。名前(c_name)は先頭固定(VE-001)。削除タグ列は捨てる。
///   列が無い/破損は既定 3 列(name/size/modified_date)。
/// - kind は basic=name/size/modified_date、tag は タグ型→num/text/simple。
/// - 幅はモック colDefs 準拠(name=1.7* 伸縮・size=120・date=150・num=140・text=152・simple=110)。
/// 描画は golden(承認者 maintainer)。
/// </summary>
public static class ListColumnBuilder
{
    // モック colDefs の固定幅(px)。名前だけ伸縮(minmax 相当の star)。
    private const string NameWidth = "1.7*";
    private const string SizeWidth = "120";
    private const string DateWidth = "150";
    private const string NumWidth = "140";
    private const string TextWidth = "152";
    private const string SimpleWidth = "110";
    private const int MaxStars = 5;

    /// <summary>
    /// アクティブなビューの display_columns から列定義を組む。名前先頭固定・削除タグ除去・kind 解決。
    /// </summary>
    /// <param name="displayColumnsJson">views.display_columns(null/破損は既定 3 列)。</param>
    /// <param name="tagById">全タグ辞書(タグ列の kind/色/名前の解決・削除タグは列から除去)。</param>
    /// <param name="basicLabel">basic キー→ローカライズ表示名(name/size/modified_date)。</param>
    public static IReadOnlyList<ListColumnDef> Build(
        string? displayColumnsJson,
        IReadOnlyDictionary<string, Tag> tagById,
        Func<string, string> basicLabel)
    {
        ArgumentNullException.ThrowIfNull(tagById);
        ArgumentNullException.ThrowIfNull(basicLabel);

        // DisplayColumnParser で順序・削除タグ除去・既定 3 列を得る(β は描画側=母集合制限は不要)。
        var parsed = DisplayColumnParser.Parse(displayColumnsJson, tagById);

        var defs = new List<ListColumnDef>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // 名前先頭固定(VE-001): 既存位置に関わらず先頭へ。
        defs.Add(BasicDef(ViewColumnModel.NameKey, basicLabel));
        seen.Add(ViewColumnModel.NameKey);

        foreach (var col in parsed)
        {
            if (!seen.Add(col.Key))
            {
                continue;
            }

            if (col.Kind == DisplayColumnKind.Basic)
            {
                if (col.Key is ViewColumnModel.SizeKey or ViewColumnModel.ModifiedDateKey)
                {
                    defs.Add(BasicDef(col.Key, basicLabel));
                }
            }
            else if (tagById.TryGetValue(col.Key, out var tag))
            {
                defs.Add(TagDef(tag, col.Label));
            }
        }

        return defs;
    }

    /// <summary>Grid の列テンプレート文字列(各列 Width を "," 連結)。名前=伸縮・他=固定 px。</summary>
    public static string ColumnTemplate(IReadOnlyList<ListColumnDef> columns) =>
        string.Join(",", columns.Select(c => c.Width));

    /// <summary>
    /// kind→種別チップの i18n キー(基本/数値/テキスト/シンプル・並び替えメニュー候補用)。
    /// ECO-108: 直書き日本語を返していたため言語非追随だった(VM 直書き= XAML lint の死角)。
    /// 表示文言は呼び出し側が現在ロケールで解決する(REQ-050/051)。
    /// </summary>
    public static string KindChipKey(ListCellKind kind) => kind switch
    {
        ListCellKind.Num => "tag.type.numeric",
        ListCellKind.Text => "tag.type.textual",
        ListCellKind.Simple => "tag.type.simple",
        _ => "view.columnChipBasic",
    };

    /// <summary>画像 1 件の各列セルを組む(型別セル描画データ)。</summary>
    public static IReadOnlyList<ListCell> BuildCells(
        ImageEntry entry,
        IReadOnlyList<ListColumnDef> columns,
        Func<long, string> formatSize,
        Func<string, string> formatDate)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(columns);

        // (ECO-026/#4) 画像ごとに tagId→値 辞書を 1 回だけ作り O(1) 引き(行×列×タグ数の線形探索を排除)。
        // タグ列が無ければ辞書生成もしない。重複 tagId は先勝ち(旧 TagValue の先頭一致と同義)。
        Dictionary<string, string?>? tagValues = null;
        for (var i = 0; i < columns.Count; i++)
        {
            if (columns[i].TagId is not null)
            {
                tagValues = new Dictionary<string, string?>(StringComparer.Ordinal);
                foreach (var t in entry.Tags)
                {
                    tagValues.TryAdd(t.TagId, t.Value);
                }

                break;
            }
        }

        var cells = new List<ListCell>(columns.Count);
        for (var i = 0; i < columns.Count; i++)
        {
            cells.Add(BuildCell(i, columns[i], entry, tagValues, formatSize, formatDate));
        }

        return cells;
    }

    private static ListCell BuildCell(
        int index, ListColumnDef col, ImageEntry entry, IReadOnlyDictionary<string, string?>? tagValues,
        Func<long, string> formatSize, Func<string, string> formatDate)
    {
        switch (col.Kind)
        {
            case ListCellKind.BasicName:
                return new ListCell(index, col.Kind, entry.Record.FileName, 0, null, true);
            case ListCellKind.BasicSize:
                return new ListCell(index, col.Kind, formatSize(entry.Record.FileSize), 0, null, true);
            case ListCellKind.BasicDate:
                return new ListCell(index, col.Kind, formatDate(entry.Record.ModifiedDate), 0, null, true);
            case ListCellKind.Num:
            {
                var value = TagValue(tagValues, col.TagId);
                if (value is not null &&
                    double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var n))
                {
                    var stars = Math.Clamp((int)Math.Round(n, MidpointRounding.AwayFromZero), 0, MaxStars);
                    return new ListCell(index, col.Kind, value, stars, col.Color, true);
                }

                return new ListCell(index, col.Kind, string.Empty, 0, col.Color, false); // 未設定
            }

            case ListCellKind.Text:
            {
                var value = TagValue(tagValues, col.TagId);
                return string.IsNullOrEmpty(value)
                    ? new ListCell(index, col.Kind, string.Empty, 0, col.Color, false) // —
                    : new ListCell(index, col.Kind, value, 0, col.Color, true);
            }

            case ListCellKind.Simple:
            {
                var present = HasTag(tagValues, col.TagId);
                return new ListCell(index, col.Kind, present ? col.Label : string.Empty, 0, col.Color, present);
            }

            default:
                return new ListCell(index, col.Kind, string.Empty, 0, null, false);
        }
    }

    private static ListColumnDef BasicDef(string key, Func<string, string> basicLabel) => key switch
    {
        ViewColumnModel.NameKey => new ListColumnDef(
            key, null, ListCellKind.BasicName, basicLabel(key), null, NameWidth, IsNameLocked: true),
        ViewColumnModel.SizeKey => new ListColumnDef(
            key, null, ListCellKind.BasicSize, basicLabel(key), null, SizeWidth, IsNameLocked: false),
        _ => new ListColumnDef(
            key, null, ListCellKind.BasicDate, basicLabel(key), null, DateWidth, IsNameLocked: false),
    };

    private static ListColumnDef TagDef(Tag tag, string? label)
    {
        var (kind, width) = tag.Type switch
        {
            TagType.Numeric => (ListCellKind.Num, NumWidth),
            TagType.Simple => (ListCellKind.Simple, SimpleWidth),
            _ => (ListCellKind.Text, TextWidth),
        };
        return new ListColumnDef(tag.Id, tag.Id, kind, label ?? tag.Name, tag.Color, width, IsNameLocked: false);
    }

    private static string? TagValue(IReadOnlyDictionary<string, string?>? tagValues, string? tagId) =>
        tagId is not null && tagValues is not null && tagValues.TryGetValue(tagId, out var v) ? v : null;

    private static bool HasTag(IReadOnlyDictionary<string, string?>? tagValues, string? tagId) =>
        tagId is not null && tagValues is not null && tagValues.ContainsKey(tagId);
}
