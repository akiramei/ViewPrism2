namespace ViewPrism2.Core.Models;

/// <summary>ステージングの遷移種別(ECO-130/REQ-100。語彙は現行判定規則=仕様 §2.1)。</summary>
public enum ScanTransitionKind
{
    /// <summary>規則 (2): 内容変更=メタ更新(status 不変)。</summary>
    MetaUpdated,

    /// <summary>規則 (3b): 新規発見(normal 登録)。</summary>
    AddedNormal,

    /// <summary>規則 (3a): 新規発見(再リンク候補 pending+candidate_link_id)。</summary>
    AddedPending,

    /// <summary>手順 4: 見つからない(normal→missing)。</summary>
    MissingFromNormal,

    /// <summary>手順 5: 候補消失(pending 行削除)。</summary>
    PendingRemoved,
}

/// <summary>詳細表示の例示行(遷移別に上限つき。全件は保持しない=26 万件経路の性能規律)。</summary>
public sealed record ScanTransitionExample(ScanTransitionKind Kind, string RelativePath);

/// <summary>
/// 再スキャンの差分計算結果(ECO-130/REQ-100)。DB 未反映の変更案+遷移別集計。
/// 変更案は有界レコード列で、DB への反映は ScanService.ApplyStagedAsync のみが行う
/// (キャンセル・破棄・失敗=本オブジェクトを捨てるだけ=DB 完全無変更)。
/// </summary>
public sealed class ScanStaging
{
    /// <summary>詳細表示に保持する例示の遷移別上限(CAD scan_summary.md SC-5=先頭数件+「ほか N 件」)。</summary>
    public const int ExamplesPerKind = 5;

    public required string FolderId { get; init; }

    /// <summary>ステージング時点の DB 行数(全 status。missing 率の分母)。</summary>
    public required int ManagedTotal { get; init; }

    /// <summary>列挙したファイル数。</summary>
    public required int ScannedFiles { get; init; }

    /// <summary>規則 (1) 変更なし(deleted 行への一致を除く)。</summary>
    public required int Unchanged { get; init; }

    /// <summary>規則 (2) 内容変更=メタ更新(deleted 行への一致を除く)。</summary>
    public required int MetaUpdated { get; init; }

    /// <summary>規則 (3b) 新規発見(normal)。</summary>
    public required int AddedNormal { get; init; }

    /// <summary>規則 (3a) 新規発見(再リンク候補 pending)。</summary>
    public required int AddedPending { get; init; }

    /// <summary>手順 4 見つからない(normal→missing)。</summary>
    public required int MissingFromNormal { get; init; }

    /// <summary>手順 5 候補消失(pending 行削除)。</summary>
    public required int PendingRemoved { get; init; }

    /// <summary>deleted 行にパス一致し規則 (1) で不変だった件数(再登録しない=REQ-100 の除外計上)。</summary>
    public required int DeletedUnchanged { get; init; }

    /// <summary>読み取り失敗(変更に数えない・DB 非変更・次回再試行=REQ-100)。</summary>
    public required int ReadFailures { get; init; }

    /// <summary>変更案: 新規登録行(3a/3b)。</summary>
    public required IReadOnlyList<ImageRecord> Adds { get; init; }

    /// <summary>変更案: メタ更新(規則 2。deleted 行への更新も従来どおり含む)。</summary>
    public required IReadOnlyList<ScanFileMetaUpdate> MetaUpdates { get; init; }

    /// <summary>変更案: status 更新(手順 4=missing 化)。</summary>
    public required IReadOnlyList<ScanStatusUpdate> StatusUpdates { get; init; }

    /// <summary>変更案: 行削除(手順 5=候補消失)。</summary>
    public required IReadOnlyList<string> Deletes { get; init; }

    /// <summary>詳細表示の例示(遷移別に <see cref="ExamplesPerKind"/> 件まで)。</summary>
    public required IReadOnlyList<ScanTransitionExample> Examples { get; init; }

    /// <summary>deleted のため除外(サマリー表示用: 不変+メタ更新のみ適用された deleted 行)。</summary>
    public int DeletedExcluded => DeletedUnchanged + (MetaUpdates.Count - MetaUpdated);

    /// <summary>見つからない合計(ECO-130 時点= normal→missing のみ)。missing 率の分子。</summary>
    public int MissingTotal => MissingFromNormal;

    /// <summary>変更合計(適用 CTA の件数=REQ-100。読み取り失敗・deleted 除外は含めない)。</summary>
    public int TotalChanges => MetaUpdated + AddedNormal + AddedPending + MissingFromNormal + PendingRemoved;
}
