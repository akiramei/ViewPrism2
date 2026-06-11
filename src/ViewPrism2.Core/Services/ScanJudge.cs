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

/// <summary>スキャン判定の結果種別(REQ-012 の規則 1/2/3b/3a に対応)。</summary>
public enum ScanDecisionKind
{
    /// <summary>規則 (1): 変更なし(スキップ)。</summary>
    Skip,

    /// <summary>規則 (2): hash/file_size/modified_date を更新(status は変更しない)。</summary>
    UpdateMeta,

    /// <summary>規則 (3b): status=normal で新規登録。</summary>
    AddNormal,

    /// <summary>規則 (3a): status=pending+candidate_link_id で新規登録。</summary>
    AddPending,
}

/// <summary>スキャン判定の出力(OC-5)。Hash は判定中に計算した値(Skip では null)。</summary>
public sealed record ScanDecision(ScanDecisionKind Kind, string? Hash = null, string? CandidateLinkId = null);

/// <summary>
/// スキャン判定器(OC-5、REQ-012)。純粋関数(I/O なし。ハッシュは入力の遅延計算デリゲート経由)。
/// 各ファイルを優先順で判定する(最初に成立した規則のみ適用)。
/// </summary>
public sealed class ScanJudge
{
    public ScanDecision Judge(ScanFileFacts file, ScanDbFacts db, bool isInitialScan)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(db);

        if (db.ExistingAtPath is { } existing)
        {
            // (1) 同一相対パスの行があり、file_size と modified_date がともに一致 → 変更なし
            if (existing.FileSize == file.FileSize &&
                string.Equals(existing.ModifiedDate, file.ModifiedDate, StringComparison.Ordinal))
            {
                return new ScanDecision(ScanDecisionKind.Skip);
            }

            // (2) いずれかが異なる → SHA-256 再計算し hash/file_size/modified_date を更新(status 不変)
            return new ScanDecision(ScanDecisionKind.UpdateMeta, file.ComputeHash());
        }

        // (3) 新規 → SHA-256 計算
        var hash = file.ComputeHash();

        // (3a) 初回スキャンでない、かつ同一フォルダ内に同ハッシュ・status=missing の行が存在する
        //      → pending+candidate_link_id(複数一致時は id 昇順の先頭)
        if (!isInitialScan)
        {
            var candidate = db.MissingInFolder
                .Where(m => m.Status == ImageStatus.Missing &&
                            string.Equals(m.Hash, hash, StringComparison.Ordinal))
                .OrderBy(m => m.Id, StringComparer.Ordinal)
                .FirstOrDefault();
            if (candidate is not null)
            {
                return new ScanDecision(ScanDecisionKind.AddPending, hash, candidate.Id);
            }
        }

        // (3b) それ以外 → status=normal で登録
        return new ScanDecision(ScanDecisionKind.AddNormal, hash);
    }
}
