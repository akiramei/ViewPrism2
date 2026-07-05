using Dapper;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Repositories;

namespace ViewPrism2.Infrastructure.Database;

/// <summary>
/// 画像特徴量(pHash)リポジトリ(M-SIMSEARCH-021、image_features)。
/// image_id PK・FK→images CASCADE。内容ベース無効化のため file_size/modified_date/hash を保持する。
/// </summary>
public sealed class ImageFeatureRepository : IImageFeatureRepository
{
    private readonly DatabaseManager _db;

    public ImageFeatureRepository(DatabaseManager db)
    {
        _db = db;
    }

    public Task<ImageFeature?> GetAsync(string imageId)
    {
        return _db.RunAsync(async conn =>
        {
            var row = await conn.QuerySingleOrDefaultAsync<Row>("""
                SELECT image_id AS ImageId, phash AS PHash, hash_adapter AS HashAdapter, file_size AS FileSize,
                       modified_date AS ModifiedDate, hash AS Hash, last_calculated AS LastCalculated,
                       phash_variants AS PhashVariants
                FROM image_features WHERE image_id = @ImageId
                """,
                new { ImageId = imageId }).ConfigureAwait(false);
            return ToEntity(row);
        });
    }

    public Task UpsertAsync(ImageFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        return _db.RunAsync(conn => conn.ExecuteAsync("""
            INSERT INTO image_features (image_id, phash, hash_adapter, file_size, modified_date, hash, last_calculated, phash_variants)
            VALUES (@ImageId, @PHash, @HashAdapter, @FileSize, @ModifiedDate, @Hash, @LastCalculated, @PhashVariants)
            ON CONFLICT(image_id) DO UPDATE SET
                phash = excluded.phash, hash_adapter = excluded.hash_adapter, file_size = excluded.file_size,
                modified_date = excluded.modified_date, hash = excluded.hash,
                last_calculated = excluded.last_calculated, phash_variants = excluded.phash_variants
            """,
            new
            {
                feature.ImageId,
                feature.PHash,
                feature.HashAdapter,
                feature.FileSize,
                feature.ModifiedDate,
                feature.Hash,
                feature.LastCalculated,
                feature.PhashVariants,
            }));
    }

    public Task DeleteByImageAsync(string imageId)
    {
        return _db.RunAsync(conn => conn.ExecuteAsync(
            "DELETE FROM image_features WHERE image_id = @ImageId", new { ImageId = imageId }));
    }

    private sealed record Row(
        string ImageId, string? PHash, string? HashAdapter, long? FileSize,
        string? ModifiedDate, string? Hash, string? LastCalculated, string? PhashVariants);

    private static ImageFeature? ToEntity(Row? row)
    {
        return row is null || row.PHash is null
            ? null
            : new ImageFeature
            {
                ImageId = row.ImageId,
                PHash = row.PHash,
                // 旧 DB(P-09 以前)の NULL は空文字へ。現行 adapter と必ず不一致=再計算される
                HashAdapter = row.HashAdapter ?? string.Empty,
                FileSize = row.FileSize ?? 0,
                ModifiedDate = row.ModifiedDate ?? string.Empty,
                Hash = row.Hash ?? string.Empty,
                LastCalculated = row.LastCalculated ?? string.Empty,
                // NULL は変種なし(migration 006 以前の旧レコード)— 変種対応 reader の下で stale 扱い(REQ-084)
                PhashVariants = row.PhashVariants,
            };
    }
}
