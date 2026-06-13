using Dapper;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Repositories;
using ViewPrism2.Core.Services.Similarity;

namespace ViewPrism2.Infrastructure.Database;

/// <summary>
/// 画像ペア類似度キャッシュリポジトリ(M-SIMSEARCH-021、image_similarity)。
/// ペアは id1&lt;id2 に正規化(<see cref="SimilarityPairKey"/>)・cache_key={id1}-{id2}。
/// (A,B)=(B,A) は同一キャッシュ。特徴量再計算で <see cref="DeleteInvolvingAsync"/> 連鎖無効化。
/// </summary>
public sealed class ImageSimilarityRepository : IImageSimilarityRepository
{
    private readonly DatabaseManager _db;

    public ImageSimilarityRepository(DatabaseManager db)
    {
        _db = db;
    }

    public Task<ImageSimilarity?> GetAsync(string idA, string idB)
    {
        var key = SimilarityPairKey.Create(idA, idB);
        return _db.RunAsync(async conn =>
        {
            var row = await conn.QuerySingleOrDefaultAsync<Row>("""
                SELECT cache_key AS CacheKey, image_id1 AS ImageId1, image_id2 AS ImageId2,
                       similarity_score AS SimilarityScore, last_compared AS LastCompared
                FROM image_similarity WHERE cache_key = @CacheKey
                """,
                new { CacheKey = key }).ConfigureAwait(false);
            return ToEntity(row);
        });
    }

    public Task UpsertAsync(string idA, string idB, int score, string lastCompared)
    {
        var (id1, id2) = SimilarityPairKey.Normalize(idA, idB);
        var key = $"{id1}-{id2}";
        return _db.RunAsync(conn => conn.ExecuteAsync("""
            INSERT INTO image_similarity (cache_key, image_id1, image_id2, similarity_score, last_compared)
            VALUES (@CacheKey, @ImageId1, @ImageId2, @Score, @LastCompared)
            ON CONFLICT(cache_key) DO UPDATE SET
                similarity_score = excluded.similarity_score, last_compared = excluded.last_compared
            """,
            new { CacheKey = key, ImageId1 = id1, ImageId2 = id2, Score = score, LastCompared = lastCompared }));
    }

    public Task DeleteInvolvingAsync(string imageId)
    {
        // 連鎖無効化: その画像が image_id1 または image_id2 に含まれる行を削除(仕様 §2.10.3)
        return _db.RunAsync(conn => conn.ExecuteAsync(
            "DELETE FROM image_similarity WHERE image_id1 = @ImageId OR image_id2 = @ImageId",
            new { ImageId = imageId }));
    }

    // SQLite の INTEGER は Int64 で返るため Row 側は long?(Dapper の positional ctor 型一致のため)
    private sealed record Row(
        string CacheKey, string ImageId1, string ImageId2, long? SimilarityScore, string? LastCompared);

    private static ImageSimilarity? ToEntity(Row? row)
    {
        return row is null
            ? null
            : new ImageSimilarity
            {
                CacheKey = row.CacheKey,
                ImageId1 = row.ImageId1,
                ImageId2 = row.ImageId2,
                SimilarityScore = (int)(row.SimilarityScore ?? 0),
                LastCompared = row.LastCompared ?? string.Empty,
            };
    }
}
