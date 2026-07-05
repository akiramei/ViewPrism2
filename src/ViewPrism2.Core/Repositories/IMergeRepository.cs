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

    // ---- ECO-044(IMG-011 裁定③): マージ操作ログ+補償 Undo ----
    // 既存スタブ(テスト)を壊さない optional 拡張として default 実装を持つ(CHEAT-02 前例)。

    /// <summary>マージ+操作ログを同一トランザクションで原子適用する(operation=null はログ省略)。</summary>
    Task ApplyMergeAsync(
        string targetId, IReadOnlyList<ImageTag> mergedTags, IReadOnlyList<string> sourceIds,
        MergeOperationRecord? operation)
        => ApplyMergeAsync(targetId, mergedTags, sourceIds);

    /// <summary>操作ログを id で取得する。</summary>
    Task<MergeOperationRecord?> GetOperationAsync(string operationId)
        => Task.FromResult<MergeOperationRecord?>(null);

    /// <summary>指定 destination の最新の操作ログ(記録順の末尾)を取得する。</summary>
    Task<MergeOperationRecord?> GetLatestOperationAsync(string targetId)
        => Task.FromResult<MergeOperationRecord?>(null);

    /// <summary>
    /// 補償 Undo を原子適用する: 追加タグ行の削除+補完値の元値復帰+sources deleted→normal+
    /// undone_at マーク。実行可能条件の判定は呼び出し側(MergeService)の責務。
    /// </summary>
    Task ApplyUndoAsync(MergeOperationRecord operation, string undoneAtIso)
        => throw new NotSupportedException("このリポジトリは補償 Undo をサポートしません。");
}
