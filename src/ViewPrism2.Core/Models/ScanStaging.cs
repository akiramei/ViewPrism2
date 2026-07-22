namespace ViewPrism2.Core.Models;

/// <summary>ステージングの遷移種別(REQ-100/REQ-101。語彙は判定規則 v5.0=仕様 §2.1・ECO-129)。</summary>
public enum ScanTransitionKind
{
    /// <summary>規則 (2): 内容変更(normal→pending。pending 起点のメタ追随を含む)。</summary>
    ContentChanged,

    /// <summary>規則 (3-再): 新規発見(pending 登録。candidate ヒントの有無は内数)。</summary>
    AddedPending,

    /// <summary>規則 (1)(2) 例外: 再出現(missing→pending)。</summary>
    Reappeared,

    /// <summary>手順 4: 見つからない(normal→missing)。</summary>
    MissingFromNormal,

    /// <summary>手順 5: 候補消失(pending→missing。v5.0=行削除の置換)。</summary>
    MissingFromPending,
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

    /// <summary>規則 (2) 内容変更(normal→pending。pending 起点のメタ追随を含む=v5.0)。</summary>
    public required int ContentChanged { get; init; }

    /// <summary>規則 (3-再) 新規発見(pending 登録)。</summary>
    public required int AddedPending { get; init; }

    /// <summary>規則 (1)(2) 例外: 再出現(missing→pending)。</summary>
    public required int Reappeared { get; init; }

    /// <summary>手順 4 見つからない(normal→missing)。</summary>
    public required int MissingFromNormal { get; init; }

    /// <summary>手順 5 候補消失(pending→missing)。</summary>
    public required int MissingFromPending { get; init; }

    /// <summary>deleted 行への規則 (1) 一致(不変=適用なし)。</summary>
    public required int DeletedUnchanged { get; init; }

    /// <summary>deleted 行への規則 (2) 一致(メタ更新のみ適用・再登録しない)。</summary>
    public required int DeletedMetaRefreshed { get; init; }

    /// <summary>規則 (1) 例外のうちメタ一致の再出現(pending 化のみ=メタ更新なし。サマリー数値パリティ用)。</summary>
    public required int PendedWithoutMeta { get; init; }

    /// <summary>deleted のため除外(サマリー表示用=不変+メタ更新のみ)。</summary>
    public int DeletedExcluded => DeletedUnchanged + DeletedMetaRefreshed;

    /// <summary>読み取り失敗(変更に数えない・DB 非変更・次回再試行=REQ-100)。</summary>
    public required int ReadFailures { get; init; }

    /// <summary>
    /// スキャン開始時点で既に status=missing だった行数(ECO-136)。適用後の総 missing 率の分子に必要。
    /// 既存 missing のうち再出現(missing→pending)しなかった分は適用後も missing に残る。
    /// </summary>
    public required int PreexistingMissing { get; init; }

    /// <summary>変更案: 新規登録行(3a/3b)。</summary>
    public required IReadOnlyList<ImageRecord> Adds { get; init; }

    /// <summary>変更案: メタ更新(規則 2。deleted 行への更新も従来どおり含む)。</summary>
    public required IReadOnlyList<ScanFileMetaUpdate> MetaUpdates { get; init; }

    /// <summary>変更案: status 更新(手順 4/5=missing 化・規則 1/2=pending 化)。</summary>
    public required IReadOnlyList<ScanStatusUpdate> StatusUpdates { get; init; }

    /// <summary>変更案: 行削除(v5.0 では常に空=手順 5 の行削除は廃止。適用器の互換のため残置)。</summary>
    public required IReadOnlyList<string> Deletes { get; init; }

    /// <summary>詳細表示の例示(遷移別に <see cref="ExamplesPerKind"/> 件まで)。</summary>
    public required IReadOnlyList<ScanTransitionExample> Examples { get; init; }

    /// <summary>見つからない合計(normal→missing+pending→missing=v5.0)。今回スキャンの delta(遷移サマリー行用)。</summary>
    public int MissingTotal => MissingFromNormal + MissingFromPending;

    /// <summary>
    /// 適用後の総 missing 数(ECO-136): 既存 missing のうち再出現しなかった分 + 今回 missing 化した分。
    /// = PreexistingMissing − Reappeared(missing→pending)+ MissingFromNormal + MissingFromPending。
    /// missing 率カード/tier の分子=「現在どれだけ見つからないか」。delta(<see cref="MissingTotal"/>)とは別。
    /// </summary>
    public int TotalMissingAfterApply =>
        PreexistingMissing - Reappeared + MissingFromNormal + MissingFromPending;

    /// <summary>裁定対象の合計(新規+内容変更+再出現=適用後に pending になる件数)。</summary>
    public int PendingTotal => AddedPending + ContentChanged + Reappeared;

    /// <summary>変更合計(適用 CTA の件数=REQ-100。読み取り失敗・deleted 除外は含めない)。</summary>
    public int TotalChanges =>
        ContentChanged + AddedPending + Reappeared + MissingFromNormal + MissingFromPending;
}
