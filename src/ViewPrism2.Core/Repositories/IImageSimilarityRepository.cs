using ViewPrism2.Core.Models;

namespace ViewPrism2.Core.Repositories;

/// <summary>
/// 画像ペア類似度キャッシュの永続化(M-SIMSEARCH-021。インターフェースは Core、実装は Infrastructure)。
/// ペアは id1&lt;id2 に正規化し cache_key={id1}-{id2}(モード接尾辞なし)。(A,B)=(B,A)。
/// 特徴量再計算時は <see cref="DeleteInvolvingAsync"/> で連鎖無効化する(仕様 §2.10.3)。
/// </summary>
public interface IImageSimilarityRepository
{
    /// <summary>(id1,id2) を正規化して照会する。順不同で同一キャッシュを指す。</summary>
    Task<ImageSimilarity?> GetAsync(string idA, string idB);

    /// <summary>UPSERT(cache_key PK)。引数のペアは正規化して保存する。</summary>
    Task UpsertAsync(string idA, string idB, int score, string lastCompared);

    /// <summary>当該画像が image_id1 または image_id2 に含まれる行を削除する(連鎖無効化)。</summary>
    Task DeleteInvolvingAsync(string imageId);
}
