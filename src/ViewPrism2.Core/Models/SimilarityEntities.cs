using ViewPrism2.Core.Services.Similarity;

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

    /// <summary>
    /// 8 オリエンテーション変種の pHash を ',' で連結した文字列(REQ-084 / ECO-048・仕様 §2.10.1a の順序・
    /// [0]=identity)。NULL = 変種なし(migration 006 以前の旧レコード/変種非対応 reader)。
    /// 現行 reader が変種対応(SupportsOrientationVariants)の場合、NULL は stale 扱い=再計算される。
    /// </summary>
    public string? PhashVariants { get; init; }
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

    /// <summary>ECO-067: 重複関係検証結果。NULL=未検証/旧cache。</summary>
    public DuplicateRelationship? DuplicateRelationship { get; init; }

    /// <summary>詳細検証の連続類似度。最終表示はpHash SimilarityScoreとの小さい方。</summary>
    public int? CandidateScore { get; init; }

    /// <summary>重複関係検証器の世代。現行と不一致なら再検証する。</summary>
    public string? VerifierAdapter { get; init; }

    /// <summary>比較日時(ISO 8601 UTC)。</summary>
    public required string LastCompared { get; init; }
}

/// <summary>類似検索の結果 1 件(OC-16)。Score 降順・同値 id 昇順で返される。</summary>
public sealed record SimilarResult
{
    public required string ImageId { get; init; }

    /// <summary>類似度%(0〜100)。</summary>
    public int Score { get; init; }

    /// <summary>ECO-067: 内部の重複安全分類。NULLは検証器を注入しない互換経路のみ。</summary>
    public DuplicateRelationship? Relationship { get; init; }

    /// <summary>GF-067-04: UI表示・検索しきい値に使う連続類似度(pHashと詳細検証の小さい方)。</summary>
    public int CandidateScore { get; init; }
}
