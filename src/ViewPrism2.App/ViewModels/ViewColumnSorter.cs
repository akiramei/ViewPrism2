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
///
/// 性能(ECO-025 β perf・maintainer 2026-07-02): ソートキーはエントリごとに1回だけ事前計算する
/// (decorate-sort-undecorate)。タグ値の線形探索・数値 parse・カルチャ比較を毎比較で繰り返さない。
/// テキストは <see cref="SortKey"/>(<see cref="CompareInfo.GetSortKey(string,CompareOptions)"/>)に
/// しておき、比較を高速なバイト比較にする(localeCompare('ja') を O(n log n) 回呼ばない)。
/// </summary>
public static class ViewColumnSorter
{
    private static readonly CompareInfo Ja = CultureInfo.GetCultureInfo("ja-JP").CompareInfo;

    public static IReadOnlyList<ImageEntry> Sort(
        IReadOnlyList<ImageEntry> entries, ViewSortColumn column, SortDirection direction)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var desc = direction == SortDirection.Desc;

        // decorate: ソートキーを1回だけ事前計算(タグ線形探索・parse・GetSortKey をエントリ数ぶんに限定)。
        var keys = new SortEntry[entries.Count];
        for (var i = 0; i < entries.Count; i++)
        {
            keys[i] = Decorate(entries[i], column, i);
        }

        // 比較器は事前計算済みキーのみ参照。id まで含む全順序のため結果は完全に決定的。
        var kind = column.Kind;
        Array.Sort(keys, (a, b) => Compare(a, b, kind, desc));

        var result = new List<ImageEntry>(keys.Length);
        foreach (var k in keys)
        {
            result.Add(k.Entry);
        }

        return result;
    }

    /// <summary>事前計算済みソートキー(decorate-sort-undecorate の decorate 結果)。</summary>
    private readonly struct SortEntry(
        ImageEntry entry, int index, bool isEmpty, double num, SortKey? text, bool present, SortKey nameKey)
    {
        public ImageEntry Entry { get; } = entry;
        public int Index { get; } = index;               // 予備(現状はタイブレーク=id を使用)
        public bool IsEmpty { get; } = isEmpty;           // Text/Numeric の未設定(末尾)
        public double Num { get; } = num;                 // Numeric 値(未設定は 0・IsEmpty で除外)
        public SortKey? Text { get; } = text;             // Text ソートキー(未設定は null)
        public bool Present { get; } = present;           // Simple 有無
        public SortKey NameKey { get; } = nameKey;        // タイブレーク(名前昇順)
    }

    private static SortEntry Decorate(ImageEntry e, ViewSortColumn column, int index)
    {
        var nameKey = Ja.GetSortKey(e.Record.FileName, CompareOptions.None);
        switch (column.Kind)
        {
            case ColumnSortKind.Simple:
                return new SortEntry(e, index, isEmpty: false, num: 0, text: null,
                    present: HasTag(e, column.Key), nameKey);
            case ColumnSortKind.Numeric:
            {
                var n = NumericValue(e, column);
                return new SortEntry(e, index, isEmpty: n is null, num: n ?? 0, text: null, present: false, nameKey);
            }

            default: // Text
            {
                var t = TextValue(e, column);
                return new SortEntry(e, index, isEmpty: t is null, num: 0,
                    text: t is null ? null : Ja.GetSortKey(t, CompareOptions.None), present: false, nameKey);
            }
        }
    }

    private static int Compare(in SortEntry a, in SortEntry b, ColumnSortKind kind, bool desc)
    {
        if (kind == ColumnSortKind.Simple)
        {
            // 有無順: 昇順=付与を先。降順は付与を後(空扱いにしない=常に方向で反転)。
            if (a.Present != b.Present)
            {
                var r = a.Present ? -1 : 1;
                return desc ? -r : r;
            }

            return NameTiebreak(a, b);
        }

        // Text / Numeric: 未設定は方向に関わらず末尾(空値末尾)。
        if (a.IsEmpty != b.IsEmpty)
        {
            return a.IsEmpty ? 1 : -1;
        }

        if (a.IsEmpty)
        {
            return NameTiebreak(a, b); // 双方未設定 → 名前昇順
        }

        var cmp = kind == ColumnSortKind.Numeric ? a.Num.CompareTo(b.Num) : SortKey.Compare(a.Text!, b.Text!);
        if (desc)
        {
            cmp = -cmp;
        }

        return cmp != 0 ? cmp : NameTiebreak(a, b); // タイブレークは名前昇順(方向で反転しない)
    }

    private static int NameTiebreak(in SortEntry a, in SortEntry b)
    {
        var byName = SortKey.Compare(a.NameKey, b.NameKey);
        return byName != 0 ? byName : string.CompareOrdinal(a.Entry.Record.Id, b.Entry.Record.Id);
    }

    private static bool HasTag(ImageEntry e, string tagId)
    {
        foreach (var t in e.Tags)
        {
            if (string.Equals(t.TagId, tagId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static double? NumericValue(ImageEntry e, ViewSortColumn column)
    {
        if (!column.IsTag)
        {
            return column.Key == ViewColumnModel.SizeKey ? e.Record.FileSize : null;
        }

        var value = TagValue(e, column.Key);
        return value is not null &&
               double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var n)
            ? n
            : null;
    }

    private static string? TextValue(ImageEntry e, ViewSortColumn column)
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

        var value = TagValue(e, column.Key);
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static string? TagValue(ImageEntry e, string tagId)
    {
        foreach (var t in e.Tags)
        {
            if (string.Equals(t.TagId, tagId, StringComparison.Ordinal))
            {
                return t.Value;
            }
        }

        return null;
    }
}
