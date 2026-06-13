using ViewPrism2.Core.Models;

namespace ViewPrism2.Core.Services.Similarity;

/// <summary>マージ後のタグ集合(OC-17 出力)。マージ先へ UPSERT すべき (tag_id, value) の集合。</summary>
public sealed record MergedTags
{
    /// <summary>マージ後のタグ集合(tag_id ごとに高々 1 件、INV-003)。</summary>
    public required IReadOnlyList<ImageTag> Tags { get; init; }
}

/// <summary>
/// マージ計算の純粋ロジック(M-MERGE-022 / E-MERGE-034 / OC-17、仕様 §2.10.5)。
/// タグ union: マージ元のタグをマージ先へ集約する。
///   - 衝突(同一 tag_id): マージ先の値を保持。マージ先が NULL/空でマージ元に値があればマージ元値を採用
///   - 多元(マージ元複数): id 昇順で処理し、マージ先が NULL/空のとき最初に非空値を与えた元の値を採用
///     (以降の元の同タグ値では上書きしない=id 昇順先勝ち)
///   - simple タグ(値なし): 存在の union のみ
/// 純粋計算(DB・UI 非依存。固定オラクル S-23 が直接呼ぶ)。
/// </summary>
public static class MergeCalculator
{
    /// <summary>
    /// マージ先タグ集合とマージ元タグ集合(id 昇順で並べた list of list)から統合後タグ集合を計算する(OC-17)。
    /// </summary>
    /// <param name="targetTags">マージ先の現在タグ。</param>
    /// <param name="sourcesTagsByIdAsc">マージ元のタグ集合を「マージ元 id 昇順」で並べたもの(1 つ以上)。</param>
    public static MergedTags Merge(
        IReadOnlyList<ImageTag> targetTags,
        IReadOnlyList<IReadOnlyList<ImageTag>> sourcesTagsByIdAsc)
    {
        ArgumentNullException.ThrowIfNull(targetTags);
        ArgumentNullException.ThrowIfNull(sourcesTagsByIdAsc);

        // tag_id → 現在の値(マージ先優先)。挿入順を保つため insertion-ordered Dictionary を使う。
        var merged = new Dictionary<string, string?>(StringComparer.Ordinal);
        // マージ先で「値が確定済み(非空)」または「補完済み(マージ元値を採用済み)」かを記録する。
        var filled = new HashSet<string>(StringComparer.Ordinal);
        var order = new List<string>();

        // 1) マージ先のタグを先に置く(マージ先優先)
        foreach (var t in targetTags)
        {
            if (!merged.ContainsKey(t.TagId))
            {
                order.Add(t.TagId);
            }

            merged[t.TagId] = t.Value;
            if (!IsEmpty(t.Value))
            {
                filled.Add(t.TagId); // マージ先が非空 → 以降は上書きしない
            }
        }

        // 2) マージ元を id 昇順で処理(先勝ち)
        foreach (var sourceTags in sourcesTagsByIdAsc)
        {
            foreach (var s in sourceTags)
            {
                if (!merged.ContainsKey(s.TagId))
                {
                    // マージ先に無いタグ → 追加(値はそのまま。simple は null)。最初の非空で確定
                    order.Add(s.TagId);
                    merged[s.TagId] = s.Value;
                    if (!IsEmpty(s.Value))
                    {
                        filled.Add(s.TagId);
                    }

                    continue;
                }

                // 既存(マージ先 or 既出のマージ元由来)。未確定で今回非空なら補完(id 昇順先勝ち)
                if (!filled.Contains(s.TagId) && !IsEmpty(s.Value))
                {
                    merged[s.TagId] = s.Value;
                    filled.Add(s.TagId);
                }
            }
        }

        var result = order.Select(tagId => new ImageTag
        {
            ImageId = string.Empty, // マージ先 image_id は呼び出し側(MergeService)で割り当てる
            TagId = tagId,
            Value = merged[tagId],
        }).ToList();

        return new MergedTags { Tags = result };
    }

    private static bool IsEmpty(string? value) => string.IsNullOrEmpty(value);
}
