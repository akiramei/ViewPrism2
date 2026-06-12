using System.Globalization;
using Dapper;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Repositories;

namespace ViewPrism2.Infrastructure.Database;

/// <summary>
/// タグリポジトリ(M-DB-007)。付与は ON CONFLICT(image_id, tag_id) DO UPDATE の UPSERT(INV-003)。
/// カスケードは FK で実現し、アプリ側で再実装しない(REQ-028)。
/// </summary>
public sealed class TagRepository : ITagRepository
{
    private const string SelectColumns = """
        SELECT id AS Id, name AS Name, type AS Type, parent_id AS ParentId,
               color AS Color, description AS Description
        FROM tags
        """;

    private readonly DatabaseManager _db;

    public TagRepository(DatabaseManager db)
    {
        _db = db;
    }

    public Task AddAsync(Tag tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        return _db.RunAsync(conn => conn.ExecuteAsync("""
            INSERT INTO tags (id, name, type, parent_id, color, description)
            VALUES (@Id, @Name, @Type, @ParentId, @Color, @Description)
            """,
            new { tag.Id, tag.Name, Type = tag.Type.ToDb(), tag.ParentId, tag.Color, tag.Description }));
    }

    public Task<Tag?> GetByIdAsync(string id)
    {
        return _db.RunAsync(async conn =>
        {
            var row = await conn.QuerySingleOrDefaultAsync<Row>(
                $"{SelectColumns} WHERE id = @Id", new { Id = id }).ConfigureAwait(false);
            return ToEntity(row);
        });
    }

    public Task<Tag?> GetByNameAsync(string name)
    {
        return _db.RunAsync(async conn =>
        {
            // name 列は既定照合(BINARY)= case-sensitive 一致(REQ-021)
            var row = await conn.QuerySingleOrDefaultAsync<Row>(
                $"{SelectColumns} WHERE name = @Name", new { Name = name }).ConfigureAwait(false);
            return ToEntity(row);
        });
    }

    public Task<IReadOnlyList<Tag>> GetAllAsync()
    {
        return _db.RunAsync<IReadOnlyList<Tag>>(async conn =>
        {
            var rows = await conn.QueryAsync<Row>($"{SelectColumns} ORDER BY id").ConfigureAwait(false);
            return rows.Select(r => ToEntity(r)!).ToList();
        });
    }

    public Task UpdateAsync(Tag tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        return _db.RunAsync(conn => conn.ExecuteAsync("""
            UPDATE tags
            SET name = @Name, type = @Type, parent_id = @ParentId, color = @Color, description = @Description
            WHERE id = @Id
            """,
            new { tag.Id, tag.Name, Type = tag.Type.ToDb(), tag.ParentId, tag.Color, tag.Description }));
    }

    public Task DeleteAsync(string id)
    {
        return _db.RunAsync(conn => conn.ExecuteAsync("DELETE FROM tags WHERE id = @Id", new { Id = id }));
    }

    public Task<TextualTagSettings?> GetTextualSettingsAsync(string tagId)
    {
        return _db.RunAsync(async conn =>
        {
            var row = await conn.QuerySingleOrDefaultAsync<TextualRow>(
                "SELECT tag_id AS TagId, predefined_values AS PredefinedValues FROM textual_tag_settings WHERE tag_id = @TagId",
                new { TagId = tagId }).ConfigureAwait(false);
            return row is null
                ? null
                : new TextualTagSettings
                {
                    TagId = row.TagId,
                    PredefinedValues = DbMapping.FromJsonArray(row.PredefinedValues),
                };
        });
    }

    public Task UpsertTextualSettingsAsync(TextualTagSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return _db.RunAsync(conn => conn.ExecuteAsync("""
            INSERT INTO textual_tag_settings (tag_id, predefined_values)
            VALUES (@TagId, @PredefinedValues)
            ON CONFLICT(tag_id) DO UPDATE SET predefined_values = excluded.predefined_values
            """,
            new { settings.TagId, PredefinedValues = DbMapping.ToJsonArray(settings.PredefinedValues) }));
    }

    public Task<NumericTagSettings?> GetNumericSettingsAsync(string tagId)
    {
        return _db.RunAsync(async conn =>
        {
            var row = await conn.QuerySingleOrDefaultAsync<NumericRow>(
                "SELECT tag_id AS TagId, min AS Min, max AS Max, step AS Step, unit AS Unit FROM numeric_tag_settings WHERE tag_id = @TagId",
                new { TagId = tagId }).ConfigureAwait(false);
            return row is null
                ? null
                : new NumericTagSettings
                {
                    TagId = row.TagId,
                    Min = row.Min,
                    Max = row.Max,
                    Step = row.Step,
                    Unit = row.Unit,
                };
        });
    }

    public Task UpsertNumericSettingsAsync(NumericTagSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return _db.RunAsync(conn => conn.ExecuteAsync("""
            INSERT INTO numeric_tag_settings (tag_id, min, max, step, unit)
            VALUES (@TagId, @Min, @Max, @Step, @Unit)
            ON CONFLICT(tag_id) DO UPDATE
            SET min = excluded.min, max = excluded.max, step = excluded.step, unit = excluded.unit
            """,
            new { settings.TagId, settings.Min, settings.Max, settings.Step, settings.Unit }));
    }

    private const string UpsertImageTagSql = """
        INSERT INTO image_tags (image_id, tag_id, value)
        VALUES (@ImageId, @TagId, @Value)
        ON CONFLICT(image_id, tag_id) DO UPDATE SET value = excluded.value
        """;

    public Task UpsertImageTagAsync(ImageTag imageTag)
    {
        ArgumentNullException.ThrowIfNull(imageTag);
        return _db.RunAsync(conn => conn.ExecuteAsync(
            UpsertImageTagSql, new { imageTag.ImageId, imageTag.TagId, imageTag.Value }));
    }

    public Task RemoveImageTagAsync(string imageId, string tagId)
    {
        // 冪等: 無い行の解除はエラーにしない(REQ-026)
        return _db.RunAsync(conn => conn.ExecuteAsync(
            "DELETE FROM image_tags WHERE image_id = @ImageId AND tag_id = @TagId",
            new { ImageId = imageId, TagId = tagId }));
    }

    public Task TagImagesAsync(IReadOnlyList<string> imageIds, string tagId, string? value)
    {
        ArgumentNullException.ThrowIfNull(imageIds);
        // INV-006: 単一トランザクション、失敗時全ロールバック
        return _db.RunAsync(async conn =>
        {
            using var tx = conn.BeginTransaction();
            try
            {
                foreach (var imageId in imageIds)
                {
                    await conn.ExecuteAsync(
                        UpsertImageTagSql, new { ImageId = imageId, TagId = tagId, Value = value }, tx)
                        .ConfigureAwait(false);
                }

                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        });
    }

    public Task TagImagesWithValuesAsync(string tagId, IReadOnlyList<(string ImageId, string? Value)> assignments)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        // INV-006: 単一トランザクション、失敗時全ロールバック(REQ-046 連番適用)
        return _db.RunAsync(async conn =>
        {
            using var tx = conn.BeginTransaction();
            try
            {
                foreach (var (imageId, value) in assignments)
                {
                    await conn.ExecuteAsync(
                        UpsertImageTagSql, new { ImageId = imageId, TagId = tagId, Value = value }, tx)
                        .ConfigureAwait(false);
                }

                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        });
    }

    public Task UntagImagesAsync(IReadOnlyList<string> imageIds, string tagId)
    {
        ArgumentNullException.ThrowIfNull(imageIds);
        return _db.RunAsync(async conn =>
        {
            using var tx = conn.BeginTransaction();
            try
            {
                foreach (var imageId in imageIds)
                {
                    await conn.ExecuteAsync(
                        "DELETE FROM image_tags WHERE image_id = @ImageId AND tag_id = @TagId",
                        new { ImageId = imageId, TagId = tagId }, tx).ConfigureAwait(false);
                }

                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        });
    }

    public Task<IReadOnlyList<ImageTag>> GetImageTagsAsync(string imageId)
    {
        return _db.RunAsync<IReadOnlyList<ImageTag>>(async conn =>
        {
            var rows = await conn.QueryAsync<ImageTag>(
                "SELECT image_id AS ImageId, tag_id AS TagId, value AS Value FROM image_tags WHERE image_id = @ImageId ORDER BY tag_id",
                new { ImageId = imageId }).ConfigureAwait(false);
            return rows.ToList();
        });
    }

    public Task<IReadOnlyList<ImageTag>> GetAllImageTagsAsync()
    {
        return _db.RunAsync<IReadOnlyList<ImageTag>>(async conn =>
        {
            var rows = await conn.QueryAsync<ImageTag>(
                "SELECT image_id AS ImageId, tag_id AS TagId, value AS Value FROM image_tags ORDER BY image_id, tag_id")
                .ConfigureAwait(false);
            return rows.ToList();
        });
    }

    public Task<IReadOnlyDictionary<string, int>> GetUsageCountsAsync()
    {
        // 使用数 = COUNT(DISTINCT image_id)(REQ-029)。
        // v1.3/ECO-002 DF-2 根本修正: image_tags が空のとき COUNT 列に型親和性が無く
        // Microsoft.Data.Sqlite が列型を BLOB(byte[])と報告するため、Dapper の型付き
        // レコード(UsageRow)materialization が空リーダーで失敗していた(UI 未処理例外でクラッシュ)。
        // 行ごとに値を読む dynamic オーバーロードに切り替え(型付きデシリアライザを構築しない)、
        // 値は Convert で long 化する。GROUP BY は空集合で 0 行のため安全。
        return _db.RunAsync<IReadOnlyDictionary<string, int>>(async conn =>
        {
            var rows = await conn.QueryAsync(
                "SELECT tag_id AS TagId, COUNT(DISTINCT image_id) AS UsageCount FROM image_tags GROUP BY tag_id")
                .ConfigureAwait(false);
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var row in rows)
            {
                var dict = (IDictionary<string, object?>)row;
                if (dict["TagId"] is string tagId)
                {
                    result[tagId] = Convert.ToInt32(dict["UsageCount"], CultureInfo.InvariantCulture);
                }
            }

            return result;
        });
    }

    private sealed record Row(
        string Id, string Name, string Type, string? ParentId, string? Color, string? Description);

    private sealed record TextualRow(string TagId, string PredefinedValues);

    private sealed record NumericRow(string TagId, double? Min, double? Max, double? Step, string? Unit);

    private static Tag? ToEntity(Row? row)
    {
        return row is null
            ? null
            : new Tag
            {
                Id = row.Id,
                Name = row.Name,
                Type = DbMapping.ToTagType(row.Type),
                ParentId = row.ParentId,
                Color = row.Color,
                Description = row.Description,
            };
    }
}
