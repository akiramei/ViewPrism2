using ViewPrism2.Core.Models;

namespace ViewPrism2.Core.Repositories;

/// <summary>
/// マージの原子適用(M-MERGE-022。インターフェースは Core、実装は Infrastructure)。
/// 単一トランザクションで、マージ後タグをマージ先へ UPSERT し、各マージ元の status を Deleted にする。
/// マージ元の image_tags は削除しない。物理ファイルには一切触れない(INV-009)。失敗時は全ロールバック。
/// </summary>
public interface IMergeRepository
{
    /// <summary>
    /// マージを原子適用する(仕様 §2.10.5、INV-011/INV-006)。
    /// </summary>
    /// <param name="targetId">マージ先 image_id。</param>
    /// <param name="mergedTags">マージ先へ UPSERT する (tag_id, value) 集合。</param>
    /// <param name="sourceIds">マージ元 image_id 群(status を Deleted にする)。</param>
    Task ApplyMergeAsync(
        string targetId, IReadOnlyList<ImageTag> mergedTags, IReadOnlyList<string> sourceIds);
}
