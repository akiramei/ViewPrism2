using ViewPrism2.Core.Models;

namespace ViewPrism2.Core.Repositories;

/// <summary>
/// 画像特徴量(pHash)の永続化(M-SIMSEARCH-021。インターフェースは Core、実装は Infrastructure)。
/// 取得時に file_size/modified_date/hash が現行と異なれば呼び出し側が再計算→Upsert→
/// 関与 similarity 行を連鎖削除する(内容ベース無効化、仕様 §2.10.3)。
/// </summary>
public interface IImageFeatureRepository
{
    Task<ImageFeature?> GetAsync(string imageId);

    /// <summary>UPSERT(image_id PK)。再計算結果の保存。</summary>
    Task UpsertAsync(ImageFeature feature);

    Task DeleteByImageAsync(string imageId);
}
