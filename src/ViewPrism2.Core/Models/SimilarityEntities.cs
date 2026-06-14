namespace ViewPrism2.Core.Models;

/// <summary>
/// 画像の特徴量(pHash)レコード(仕様 §2.10.3、image_features テーブル)。
/// image_id PK、FK→images CASCADE。内容ベース無効化のため file_size/modified_date/hash を保持する。
/// ORB 列(orb_descriptors/orb_keypoints)は本ループでは作らない(ORB defer)。
/// </summary>
public sealed record ImageFeature
{
    public required string ImageId { get; init; }

    /// <summary>pHash の 16 桁小文字 16 進(OC-14)。</summary>
    public required string PHash { get; init; }

    /// <summary>
    /// この pHash を生成した adapter の世代識別子(P-09、image_features.hash_adapter)。
    /// 現行 reader の <see cref="ViewPrism2.Core.Services.Similarity.IPHashImageReader.AdapterId"/> と
    /// 不一致なら内容が同じでも stale 扱い=再計算する(adapter をまたいだ pHash 値の混在防止)。
    /// 旧 DB の NULL は空文字へマップされ、現行 adapter と必ず不一致=再計算される。
    /// </summary>
    public required string HashAdapter { get; init; }

    /// <summary>計算時点のファイルサイズ(内容ベース無効化の判定材料)。</summary>
    public long FileSize { get; init; }

    /// <summary>計算時点の modified_date(ISO 8601 UTC)。</summary>
    public required string ModifiedDate { get; init; }

    /// <summary>計算時点の SHA-256 hash。</summary>
    public required string Hash { get; init; }

    /// <summary>特徴量の最終計算日時(ISO 8601 UTC)。</summary>
    public required string LastCalculated { get; init; }
}

/// <summary>
/// 画像ペアの類似度キャッシュ(仕様 §2.10.3、image_similarity テーブル)。
/// ペアは文字列比較で小さい方を ImageId1・大きい方を ImageId2 に正規化し、
/// cache_key = {ImageId1}-{ImageId2}。両 FK→images CASCADE。(A,B)=(B,A)。
/// </summary>
public sealed record ImageSimilarity
{
    /// <summary>正規化キー {ImageId1}-{ImageId2}(ImageId1 &lt; ImageId2、序数比較)。</summary>
    public required string CacheKey { get; init; }

    public required string ImageId1 { get; init; }

    public required string ImageId2 { get; init; }

    /// <summary>類似度%(0〜100、OC-15)。</summary>
    public int SimilarityScore { get; init; }

    /// <summary>比較日時(ISO 8601 UTC)。</summary>
    public required string LastCompared { get; init; }
}

/// <summary>類似検索の結果 1 件(OC-16)。Score 降順・同値 id 昇順で返される。</summary>
public sealed record SimilarResult
{
    public required string ImageId { get; init; }

    /// <summary>類似度%(0〜100)。</summary>
    public int Score { get; init; }
}
