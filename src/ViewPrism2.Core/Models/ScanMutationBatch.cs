namespace ViewPrism2.Core.Models;

/// <summary>スキャン規則 (2) で更新するファイルメタデータ。</summary>
public sealed record ScanFileMetaUpdate(
    string Id,
    string Hash,
    long FileSize,
    string ModifiedDate,
    bool PreservePendingBaselineHash = false);

/// <summary>
/// スキャン手順 4/5・規則 1/2 で適用するステータス変更(v5.0=ECO-129: pending 化に由来を添える)。
/// PendingOrigin は遷移ごとに明示上書き(null=クリア)。pending 以外へ遷移する行は
/// candidate_link_id もクリアされる(適用側の契約=ImageRepository)。
/// </summary>
public sealed record ScanStatusUpdate(string Id, ImageStatus Status, PendingOrigin? PendingOrigin = null);

/// <summary>
/// ECO-059: スキャン中のDB変更を単一トランザクションで適用する有界バッチ。
/// 各コレクションの合計件数は呼び出し側の上限以下とする。
/// </summary>
public sealed record ScanMutationBatch(
    IReadOnlyList<ImageRecord> Adds,
    IReadOnlyList<ScanFileMetaUpdate> FileMetaUpdates,
    IReadOnlyList<ScanStatusUpdate> StatusUpdates,
    IReadOnlyList<string> Deletes)
{
    public int Count => Adds.Count + FileMetaUpdates.Count + StatusUpdates.Count + Deletes.Count;
}
