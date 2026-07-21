using ViewPrism2.Core.Models;

namespace ViewPrism2.Core.Services;

/// <summary>スキャン判定の入力: ファイル情報(REQ-012、OC-5)。ハッシュは遅延計算。</summary>
/// <param name="RelativePath">正規形相対パス(INV-005)。</param>
/// <param name="FileSize">ファイルサイズ(バイト)。</param>
/// <param name="ModifiedDate">更新日時(ISO 8601 UTC、INV-002)。</param>
/// <param name="CreatedDate">作成日時(ISO 8601 UTC、INV-002)。</param>
/// <param name="ComputeHash">SHA-256 の遅延計算(REQ-013)。必要な規則でのみ呼ばれる。</param>
public sealed record ScanFileFacts(
    string RelativePath,
    long FileSize,
    string ModifiedDate,
    string CreatedDate,
    Func<string> ComputeHash);

/// <summary>スキャン判定の入力: DB 状態(OC-5)。</summary>
/// <param name="ExistingAtPath">同一相対パス(case-insensitive)の既存行。無ければ null。</param>
/// <param name="MissingInFolder">同一フォルダ内の status=missing の行(規則 3a の照合対象)。</param>
public sealed record ScanDbFacts(
    ImageRecord? ExistingAtPath,
    IReadOnlyList<ImageRecord> MissingInFolder);

/// <summary>スキャン判定の結果種別(v5.0=ECO-129/REQ-101。仕様 §2.1 規則 1/2/3 に対応)。</summary>
public enum ScanDecisionKind
{
    /// <summary>規則 (1): 変更なし(スキップ。missing 起点を除く)。</summary>
    Skip,

    /// <summary>規則 (2): メタ更新のみ・status 不変(pending 起点=origin 維持/deleted 起点=除外)。</summary>
    UpdateMeta,

    /// <summary>規則 (2): メタ更新+pending 化(normal→'changed'/missing→'reappeared')。</summary>
    UpdateMetaAndPend,

    /// <summary>規則 (1) 例外: missing 起点・メタ一致の再出現 → pending 化のみ('reappeared')。</summary>
    PendInPlace,

    /// <summary>規則 (3-初回): status=normal で新規登録(初回スキャンのみ=登録行為が裁定)。</summary>
    AddNormal,

    /// <summary>規則 (3-再): status=pending('new')で新規登録(同ハッシュ missing があれば candidate 付き)。</summary>
    AddPending,
}

/// <summary>スキャン判定の出力(OC-5)。Hash は判定中に計算した値(Skip/PendInPlace では null)。</summary>
public sealed record ScanDecision(
    ScanDecisionKind Kind,
    string? Hash = null,
    string? CandidateLinkId = null,
    PendingOrigin? PendingOrigin = null);

/// <summary>
/// スキャン判定器(OC-5、REQ-012/REQ-101)。純粋関数(I/O なし。ハッシュは入力の遅延計算デリゲート経由)。
/// 各ファイルを優先順で判定する(最初に成立した規則のみ適用)。
/// v5.0(ECO-129): 不確実は pending に倒す=機械観測とユーザー裁定の分離。
/// </summary>
public sealed class ScanJudge
{
    public ScanDecision Judge(ScanFileFacts file, ScanDbFacts db, bool isInitialScan)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(db);

        if (db.ExistingAtPath is { } existing)
        {
            // (1) 同一相対パスの行があり、file_size と modified_date がともに一致
            if (existing.FileSize == file.FileSize &&
                string.Equals(existing.ModifiedDate, file.ModifiedDate, StringComparison.Ordinal))
            {
                // 例外(v5.0): missing のパスに再出現 → pending(無条件 normal 化はしない=REQ-101)
                return existing.Status == ImageStatus.Missing
                    ? new ScanDecision(ScanDecisionKind.PendInPlace, PendingOrigin: Models.PendingOrigin.Reappeared)
                    : new ScanDecision(ScanDecisionKind.Skip);
            }

            // (2) いずれかが異なる → SHA-256 再計算+メタ更新。status は起点別(v5.0)
            var updatedHash = file.ComputeHash();
            return existing.Status switch
            {
                ImageStatus.Normal => new ScanDecision(
                    ScanDecisionKind.UpdateMetaAndPend, updatedHash, PendingOrigin: Models.PendingOrigin.Changed),
                ImageStatus.Missing => new ScanDecision(
                    ScanDecisionKind.UpdateMetaAndPend, updatedHash, PendingOrigin: Models.PendingOrigin.Reappeared),
                // pending=origin 維持のままメタだけ追随/deleted=再登録しない(除外)がメタは従来どおり更新
                _ => new ScanDecision(ScanDecisionKind.UpdateMeta, updatedHash),
            };
        }

        // (3) 新規 → SHA-256 計算
        var hash = file.ComputeHash();

        // (3-初回) 初回スキャン= normal(フォルダ登録行為を裁定とみなす — gate① 裁定/CAD SCAN-001)
        if (isInitialScan)
        {
            return new ScanDecision(ScanDecisionKind.AddNormal, hash);
        }

        // (3-再) 再スキャンの新規はすべて pending('new')。同一フォルダ内の同ハッシュ missing は
        // candidate_link_id ヒント(複数一致時は id 昇順の先頭=旧規則 3a の包含)
        var candidate = db.MissingInFolder
            .Where(m => m.Status == ImageStatus.Missing &&
                        string.Equals(m.Hash, hash, StringComparison.Ordinal))
            .OrderBy(m => m.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        return new ScanDecision(
            ScanDecisionKind.AddPending, hash, candidate?.Id, Models.PendingOrigin.New);
    }
}
