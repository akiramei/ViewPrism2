using ViewPrism2.Core.Models;

namespace ViewPrism2.Core.Repositories;

/// <summary>画像レコードの永続化(M-DB-007。インターフェースは Core、実装は Infrastructure)。</summary>
public interface IImageRepository
{
    Task AddAsync(ImageRecord image);

    /// <summary>
    /// ECO-059: スキャン中の追加・メタ更新・status更新・削除を単一トランザクションで原子適用する。
    /// 呼び出し側は件数上限を持つバッチだけを渡す。
    /// </summary>
    Task ApplyScanBatchAsync(ScanMutationBatch batch);

    Task<ImageRecord?> GetByIdAsync(string id);

    /// <summary>フォルダ配下の全行(全ステータス)。スキャン手順 1〜5 の基礎データ(仕様 §2.1)。</summary>
    Task<IReadOnlyList<ImageRecord>> GetByFolderAsync(string syncFolderId);

    /// <summary>status=normal の全画像(INV-010: 既定の画像一覧・ビュー評価の対象)。表示系の供給元。</summary>
    Task<IReadOnlyList<ImageRecord>> GetAllNormalAsync();

    /// <summary>
    /// ECO-064/IMG-019: collection catalog 用の status=normal 件数集約。
    /// ImageRecord 全件を materialize せず folder id→件数だけを返す。
    /// </summary>
    Task<IReadOnlyDictionary<string, int>> GetNormalCountsByFolderAsync(CancellationToken ct = default);

    /// <summary>ECO-064/IMG-019: 選択 collection の status=normal 画像だけを読む。</summary>
    Task<IReadOnlyList<ImageRecord>> GetNormalByFolderAsync(string syncFolderId, CancellationToken ct = default);

    /// <summary>ECO-098: 選択 collection の status=deleted 画像だけをファイル名順で読む。</summary>
    Task<IReadOnlyList<ImageRecord>> GetDeletedByFolderAsync(string syncFolderId, CancellationToken ct = default);

    /// <summary>ECO-129/E-UI-PENDING-049: 選択 collection の status=pending だけをファイル名順で読む(DB 境界限定)。</summary>
    Task<IReadOnlyList<ImageRecord>> GetPendingByFolderAsync(string syncFolderId, CancellationToken ct = default);

    /// <summary>
    /// ECO-139/PD-6: candidate_link_id の一致先を確認一覧へ提示するため、指定 ID だけを一括取得する。
    /// 既定実装は互換用の逐次取得。Infrastructure 実装は DB の IN 句を分割して取得する。
    /// </summary>
    async Task<IReadOnlyList<ImageRecord>> GetByIdsAsync(
        IReadOnlyCollection<string> ids, CancellationToken ct = default)
    {
        var rows = new List<ImageRecord>(ids.Count);
        foreach (var id in ids.Distinct(StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            if (await GetByIdAsync(id).ConfigureAwait(false) is { } row)
            {
                rows.Add(row);
            }
        }

        return rows;
    }

    /// <summary>
    /// ECO-129 T13/T15(裁定=受入れ/削除): pending 限定を UPDATE の WHERE で原子的に強制。
    /// candidate_link_id/pending_origin はクリア。false= 対象が pending でなかった(拒否)。
    /// </summary>
    Task<bool> AdjudicatePendingAsync(string id, ImageStatus status);

    /// <summary>
    /// ECO-139: 指定 ID 全件を単一トランザクションで裁定する。
    /// 各 UPDATE は pending 限定を WHERE で強制し、1 件でも対象外なら全件 rollback して false。
    /// candidate_link_id/pending_origin は全件クリアする。
    /// </summary>
    Task<bool> AdjudicatePendingBatchAsync(IReadOnlyCollection<string> ids, ImageStatus status)
        => throw new NotSupportedException("This repository does not support pending batch adjudication.");

    /// <summary>
    /// ECO-139/GF-139-01: candidate missing と pending の組を単一トランザクションで一括再リンクする。
    /// 各組は T4/REQ-017 と同じく pending 行を削除し、missing 側の image_id/タグを保持したまま
    /// pending のファイルメタへ付け替える。1 組でも stale/不正なら全件 rollback して false。
    /// </summary>
    Task<bool> ApplyRelinkBatchAsync(
        IReadOnlyCollection<(string MissingImageId, string PendingImageId)> pairs)
        => throw new NotSupportedException("This repository does not support batch relinking.");

    /// <summary>
    /// ECO-129 T14(裁定=別画像として扱う・PEND-001): 単一トランザクションの原子的な行置換。
    /// 旧 pending 行を DELETE(タグ/特徴量/類似は FK CASCADE 消滅)し replacement を INSERT する。
    /// false= 対象が pending でなかった(拒否・rollback)。1 パス 1 行の不変を維持する。
    /// </summary>
    Task<bool> ReplacePendingAsync(string oldId, ImageRecord replacement);

    /// <summary>ECO-064: collection行/ゴミ箱badge等の集約用。画像行をmaterializeしない。</summary>
    Task<int> CountByFolderAndStatusAsync(
        string syncFolderId, ImageStatus status, CancellationToken ct = default);

    /// <summary>スキャン規則 (2): hash/file_size/modified_date のみ更新(status は変更しない)。</summary>
    Task UpdateFileMetaAsync(string id, string hash, long fileSize, string modifiedDate);

    /// <summary>ステータス遷移の適用(仕様 §2.1 遷移表の範囲のみ)。</summary>
    Task UpdateStatusAsync(string id, ImageStatus status);

    /// <summary>
    /// ECO-128 T6'/T7: トラッシュ復元の status + pending_origin を原子更新する。
    /// deleted→pending は origin=Restored(復元だけで normal に戻さない=INV-013 v5.0)、
    /// deleted→missing は origin=NULL(T12 と同契約)。candidate_link_id には触れない
    /// (deleted 行は常に candidate_link_id=NULL)。deleted 限定は TrashService が事前検証済み。
    /// </summary>
    Task RestoreStatusAsync(string id, ImageStatus status, PendingOrigin? origin);

    /// <summary>ノート保存(REQ-043)。</summary>
    Task UpdateNotesAsync(string id, string? notes);

    Task DeleteAsync(string id);

    /// <summary>
    /// 再リンク確定の原子適用(REQ-017): 単一トランザクションで pending 行を削除し、
    /// missing 行へ pending 行の relative_path/file_name/file_size/modified_date/created_date/hash を
    /// 上書きして status=normal・candidate_link_id=NULL にする。missing 側 image_id は不変(INV-001)。
    /// </summary>
    Task ApplyRelinkAsync(string missingImageId, string pendingImageId);

    /// <summary>
    /// status=normal の画像に付与された当該タグの distinct 値(INV-010)。
    /// NodeGraph 値抽出(ITagValueSource 契約)の供給元。
    /// </summary>
    Task<IReadOnlyList<string>> GetDistinctNormalTagValuesAsync(string tagId);
}
