namespace ViewPrism2.Core.Models;

/// <summary>
/// マージ操作ログ 1 行(ECO-044 / IMG-011 裁定③)。マージと同一トランザクションで記録され、
/// 「取り消す」(補償操作)の根拠になる。revision カラムは持たず、destination/sources の
/// 内容指紋(status+hash+タグ集合)を記録して「マージ直後から変化していない」を直接検証する。
/// </summary>
public sealed record MergeOperationRecord
{
    public required string Id { get; init; }

    /// <summary>マージ先(destination)。images への FK CASCADE=完全削除でログも消滅。</summary>
    public required string TargetId { get; init; }

    /// <summary>マージ元(id 昇順)。FK は張らない — 完全削除は Undo 時の行不在で検出する。</summary>
    public required IReadOnlyList<string> SourceIds { get; init; }

    /// <summary>マージで destination に新規追加されたタグ行(tag_id 昇順)。補償= 行削除。</summary>
    public required IReadOnlyList<string> AddedTagIds { get; init; }

    /// <summary>NULL/空 補完で値が入ったタグ行 → 補完前の元値(null または "")。補償= 元値へ復帰。</summary>
    public required IReadOnlyDictionary<string, string?> FilledTags { get; init; }

    public required string ExecutedAt { get; init; }

    /// <summary>マージ直後の destination 内容指紋(status+hash+タグ集合)。</summary>
    public required string TargetFingerprint { get; init; }

    /// <summary>source id → マージ直後(status=deleted)の内容指紋。</summary>
    public required IReadOnlyDictionary<string, string> SourceFingerprints { get; init; }

    /// <summary>補償実行済みマーク(非 null=取り消し済み・二重 Undo 拒否)。</summary>
    public string? UndoneAt { get; init; }
}
