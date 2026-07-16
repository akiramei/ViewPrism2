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

    /// <summary>ECO-064: collection行/ゴミ箱badge等の集約用。画像行をmaterializeしない。</summary>
    Task<int> CountByFolderAndStatusAsync(
        string syncFolderId, ImageStatus status, CancellationToken ct = default);

    /// <summary>スキャン規則 (2): hash/file_size/modified_date のみ更新(status は変更しない)。</summary>
    Task UpdateFileMetaAsync(string id, string hash, long fileSize, string modifiedDate);

    /// <summary>ステータス遷移の適用(仕様 §2.1 遷移表の範囲のみ)。</summary>
    Task UpdateStatusAsync(string id, ImageStatus status);

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
