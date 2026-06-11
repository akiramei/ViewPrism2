using ViewPrism2.Core.Models;

namespace ViewPrism2.Core.Services;

/// <summary>
/// タグ付け状態スナップショットからの <see cref="ITagValueSource"/> 実装(M-GRAPH-003)。
/// status=Normal の画像の値のみを保持する(INV-010)。
/// </summary>
public sealed class TagValueIndex : ITagValueSource
{
    private readonly Dictionary<string, List<string>> _valuesByTag;

    private TagValueIndex(Dictionary<string, List<string>> valuesByTag)
    {
        _valuesByTag = valuesByTag;
    }

    /// <summary>評価器入力(OC-1)と同じスナップショットから構築する。Normal 以外の画像の値は含めない。</summary>
    public static TagValueIndex Build(IEnumerable<ImageWithTags> images)
    {
        ArgumentNullException.ThrowIfNull(images);

        var sets = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var image in images)
        {
            if (image.Status != ImageStatus.Normal)
            {
                continue; // INV-010
            }

            foreach (var tag in image.Tags)
            {
                if (tag.Value is null)
                {
                    continue;
                }

                if (!sets.TryGetValue(tag.TagId, out var set))
                {
                    set = new HashSet<string>(StringComparer.Ordinal);
                    sets[tag.TagId] = set;
                }

                set.Add(tag.Value);
            }
        }

        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (tagId, set) in sets)
        {
            result[tagId] = set.ToList();
        }

        return new TagValueIndex(result);
    }

    /// <summary>タグ id → distinct 値リストの素材から直接構築する(リポジトリ供給値用)。</summary>
    public static TagValueIndex FromValues(IReadOnlyDictionary<string, IReadOnlyList<string>> valuesByTag)
    {
        ArgumentNullException.ThrowIfNull(valuesByTag);

        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (tagId, values) in valuesByTag)
        {
            result[tagId] = values.ToList();
        }

        return new TagValueIndex(result);
    }

    public IReadOnlyList<string> GetDistinctValues(string tagId)
    {
        ArgumentNullException.ThrowIfNull(tagId);
        return _valuesByTag.TryGetValue(tagId, out var values) ? values : [];
    }
}
