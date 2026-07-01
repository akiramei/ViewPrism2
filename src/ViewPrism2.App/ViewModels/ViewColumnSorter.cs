using System.Globalization;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;

namespace ViewPrism2.App.ViewModels;

/// <summary>列ヘッダーソートの比較種別(ECO-025 β・REQ-081)。</summary>
public enum ColumnSortKind
{
    /// <summary>文字列順(localeCompare('ja'))。basic name/modified_date・テキストタグ。未設定は末尾。</summary>
    Text,

    /// <summary>数値順。basic size・数値タグ。未設定・非数値は末尾。</summary>
    Numeric,

    /// <summary>有無順(付与を先)。シンプルタグ。空扱いにしない。</summary>
    Simple,
}

/// <summary>ソート対象列の指定(ECO-025 β)。basic は Key=name/size/modified_date、tag は Key=タグ id。</summary>
public readonly record struct ViewSortColumn(string Key, bool IsTag, ColumnSortKind Kind)
{
    /// <summary>表示列の基本情報キー/タグ型から比較列を作る。</summary>
    public static ViewSortColumn ForBasic(string key) => new(
        key,
        IsTag: false,
        Kind: key == ViewColumnModel.SizeKey ? ColumnSortKind.Numeric : ColumnSortKind.Text);

    public static ViewSortColumn ForTag(string tagId, TagType type) => new(
        tagId,
        IsTag: true,
        Kind: type switch
        {
            TagType.Numeric => ColumnSortKind.Numeric,
            TagType.Simple => ColumnSortKind.Simple,
            _ => ColumnSortKind.Text,
        });
}

/// <summary>
/// ファイル一覧 列ヘッダーソート(ECO-025 β・REQ-081)。描画から独立した決定論ロジックで unit 検査可能。
///
/// 不変条件:
/// - 未設定タグの行は方向に関わらず常に末尾(空値末尾)。ただし Simple は空扱いにせず有無順(付与を先・昇順時)。
/// - 型別比較: Numeric=数値順 / Text=localeCompare('ja') / Simple=有無順。
/// - 同値のタイブレークは名前(FileName)の昇順で安定化(さらに id 昇順で全順序=完全に決定的)。
///
/// 画像一覧の sort_field 整列(<see cref="ImageSorter"/>)とは別軸(任意の表示列で並べる)。
/// </summary>
public static class ViewColumnSorter
{
    private static readonly CompareInfo Ja = CultureInfo.GetCultureInfo("ja-JP").CompareInfo;

    public static IReadOnlyList<ImageEntry> Sort(
        IReadOnlyList<ImageEntry> entries, ViewSortColumn column, SortDirection direction)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var comparer = new EntryComparer(column, direction);
        // OrderBy は安定ソート。比較器は id まで含む全順序のため結果は完全に決定的。
        return entries.OrderBy(e => e, comparer).ToList();
    }

    private sealed class EntryComparer(ViewSortColumn column, SortDirection direction) : IComparer<ImageEntry>
    {
        public int Compare(ImageEntry? a, ImageEntry? b)
        {
            if (ReferenceEquals(a, b))
            {
                return 0;
            }

            if (a is null)
            {
                return 1;
            }

            if (b is null)
            {
                return -1;
            }

            var desc = direction == SortDirection.Desc;

            if (column.Kind == ColumnSortKind.Simple)
            {
                // 有無順: 昇順=付与を先。降順は付与を後。空扱いにしない(常に方向で反転)。
                var pa = HasTag(a);
                var pb = HasTag(b);
                if (pa != pb)
                {
                    var r = pa ? -1 : 1; // 昇順=付与(true)を先
                    return desc ? -r : r;
                }

                return NameTiebreak(a, b);
            }

            // Text / Numeric: 未設定は方向に関わらず末尾(空値末尾)
            var ea = IsEmpty(a);
            var eb = IsEmpty(b);
            if (ea != eb)
            {
                return ea ? 1 : -1;
            }

            if (ea)
            {
                return NameTiebreak(a, b); // 双方未設定 → 名前昇順
            }

            var cmp = column.Kind == ColumnSortKind.Numeric ? CompareNumeric(a, b) : CompareText(a, b);
            if (desc)
            {
                cmp = -cmp;
            }

            return cmp != 0 ? cmp : NameTiebreak(a, b); // タイブレークは常に名前昇順(方向で反転しない)
        }

        private int CompareNumeric(ImageEntry a, ImageEntry b)
        {
            var na = NumericValue(a) ?? 0;
            var nb = NumericValue(b) ?? 0;
            return na.CompareTo(nb);
        }

        private int CompareText(ImageEntry a, ImageEntry b) =>
            Ja.Compare(TextValue(a) ?? string.Empty, TextValue(b) ?? string.Empty, CompareOptions.None);

        /// <summary>当該列で「未設定(空)」か(Text/Numeric)。basic name/size/date は常に非空。</summary>
        private bool IsEmpty(ImageEntry e) => column.Kind == ColumnSortKind.Numeric
            ? NumericValue(e) is null
            : TextValue(e) is null;

        private bool HasTag(ImageEntry e) =>
            e.Tags.Any(t => string.Equals(t.TagId, column.Key, StringComparison.Ordinal));

        private double? NumericValue(ImageEntry e)
        {
            if (!column.IsTag)
            {
                return column.Key == ViewColumnModel.SizeKey ? e.Record.FileSize : null;
            }

            var value = TagValue(e);
            return value is not null &&
                   double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var n)
                ? n
                : null;
        }

        private string? TextValue(ImageEntry e)
        {
            if (!column.IsTag)
            {
                return column.Key switch
                {
                    ViewColumnModel.NameKey => e.Record.FileName,
                    ViewColumnModel.ModifiedDateKey => e.Record.ModifiedDate,
                    _ => null,
                };
            }

            var value = TagValue(e);
            return string.IsNullOrEmpty(value) ? null : value;
        }

        private string? TagValue(ImageEntry e)
        {
            foreach (var tag in e.Tags)
            {
                if (string.Equals(tag.TagId, column.Key, StringComparison.Ordinal))
                {
                    return tag.Value;
                }
            }

            return null;
        }

        private static int NameTiebreak(ImageEntry a, ImageEntry b)
        {
            var byName = Ja.Compare(a.Record.FileName, b.Record.FileName, CompareOptions.None);
            return byName != 0 ? byName : string.CompareOrdinal(a.Record.Id, b.Record.Id);
        }
    }
}
