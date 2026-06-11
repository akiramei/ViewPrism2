using ViewPrism2.Core.Models;

namespace ViewPrism2.Core.Services;

/// <summary>
/// 整列器(OC-4、REQ-038)。name=OrdinalIgnoreCase / 日時=序数(ISO 8601) / file_size=数値。
/// 二次キーは常に id 昇順(同値時も実行ごとに並びが変わらない安定ソート)。
/// </summary>
public sealed class ImageSorter
{
    public IReadOnlyList<ImageRecord> Sort(IEnumerable<ImageRecord> images, SortField field, SortDirection direction)
    {
        ArgumentNullException.ThrowIfNull(images);

        var ascending = direction == SortDirection.Asc;
        IOrderedEnumerable<ImageRecord> ordered = field switch
        {
            SortField.Name => OrderBy(images, i => i.FileName, StringComparer.OrdinalIgnoreCase, ascending),
            SortField.CreatedDate => OrderBy(images, i => i.CreatedDate, StringComparer.Ordinal, ascending),
            SortField.ModifiedDate => OrderBy(images, i => i.ModifiedDate, StringComparer.Ordinal, ascending),
            SortField.FileSize => OrderBy(images, i => i.FileSize, Comparer<long>.Default, ascending),
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
        };

        // 二次キーは方向に依らず id 昇順(REQ-038)
        return ordered.ThenBy(i => i.Id, StringComparer.Ordinal).ToList();
    }

    private static IOrderedEnumerable<ImageRecord> OrderBy<TKey>(
        IEnumerable<ImageRecord> images,
        Func<ImageRecord, TKey> keySelector,
        IComparer<TKey> comparer,
        bool ascending)
    {
        return ascending
            ? images.OrderBy(keySelector, comparer)
            : images.OrderByDescending(keySelector, comparer);
    }
}
