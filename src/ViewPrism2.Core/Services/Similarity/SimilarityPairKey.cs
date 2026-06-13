namespace ViewPrism2.Core.Services.Similarity;

/// <summary>
/// 画像ペアの正規化キー生成(仕様 §2.10.3)。
/// ペアは文字列比較(序数)で小さい方を id1・大きい方を id2 に正規化し、
/// cache_key = {id1}-{id2}(本ループは pHash 単一モードのためモード接尾辞は付けない)。
/// (A,B) と (B,A) は同一キャッシュを指す。
/// </summary>
public static class SimilarityPairKey
{
    /// <summary>(idA, idB) を正規化して (Id1, Id2)(Id1 ≤ Id2、序数比較)を返す。</summary>
    public static (string Id1, string Id2) Normalize(string idA, string idB)
    {
        ArgumentNullException.ThrowIfNull(idA);
        ArgumentNullException.ThrowIfNull(idB);
        return string.CompareOrdinal(idA, idB) <= 0 ? (idA, idB) : (idB, idA);
    }

    /// <summary>正規化された cache_key = {min}-{max}(序数比較)。</summary>
    public static string Create(string idA, string idB)
    {
        var (id1, id2) = Normalize(idA, idB);
        return $"{id1}-{id2}";
    }
}
