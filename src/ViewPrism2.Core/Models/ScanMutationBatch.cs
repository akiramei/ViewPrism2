namespace ViewPrism2.Core.Models;

/// <summary>スキャン規則 (2) で更新するファイルメタデータ。</summary>
public sealed record ScanFileMetaUpdate(
    string Id,
    string Hash,
    long FileSize,
    string ModifiedDate);

/// <summary>スキャン規則 4 で適用するステータス変更。</summary>
public sealed record ScanStatusUpdate(string Id, ImageStatus Status);

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
