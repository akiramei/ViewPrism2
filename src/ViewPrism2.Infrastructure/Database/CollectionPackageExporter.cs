using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services.Package;

namespace ViewPrism2.Infrastructure.Database;

public sealed record ExportSummary(string FilePath, long Images, int Tags);

/// <summary>
/// コレクション論理パッケージの書き出し(ECO-073 §3.1/§3.5)。
/// 書き出し専用の読み取り接続を別に開き(共有接続の semaphore を長時間占有しない)、
/// 単一読み取りトランザクション内の同一時点スナップショットから、参照タグの依存閉包
/// (祖先チェーン・型別設定込み)と画像参照(指紋=既存 SHA-256・file_size/日時同梱)を
/// .partial へストリーミング書き出し→構造検証→アトミック確定する。画像実体は含めない。
/// 対象 status=normal/missing(deleted=トラッシュ・pending=未確定は含めない)。
/// </summary>
public sealed class CollectionPackageExporter(DatabaseManager db, IClock clock, string appVersion)
{
    private const int ProgressBatch = 500;

    /// <summary>B-1 表示用の件数(対象画像数・参照タグ数=依存閉包ではなく付与タグの distinct)。</summary>
    public Task<(long Images, long Tags)> CountAsync(string collectionId, CancellationToken ct = default)
        => db.RunAsync(async conn =>
        {
            var images = await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM images WHERE sync_folder_id = @Id AND status IN ('normal','missing')",
                new { Id = collectionId }).ConfigureAwait(false);
            var tags = await conn.ExecuteScalarAsync<long>(
                """
                SELECT COUNT(DISTINCT it.tag_id) FROM image_tags it
                JOIN images i ON i.id = it.image_id
                WHERE i.sync_folder_id = @Id AND i.status IN ('normal','missing')
                """,
                new { Id = collectionId }).ConfigureAwait(false);
            return (images, tags);
        }, ct);

    /// <summary>コレクション 1 件を outputPath へ書き出す。</summary>
    public async Task<Result<ExportSummary>> ExportAsync(
        string collectionId,
        string outputPath,
        IProgress<(long Done, long Total)>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(collectionId);
        ArgumentException.ThrowIfNullOrEmpty(outputPath);
        var libraryId = await LibraryIdentity.GetOrCreateAsync(db, ct).ConfigureAwait(false);
        var partialPath = outputPath + CollectionPackageFormat.PartialExtension;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
            File.Delete(partialPath);
            long images;
            int tags;
            // 書き出し専用読み取り接続+単一トランザクション=同一時点スナップショット(自己矛盾ファイル防止)
            await using (var conn = new SqliteConnection($"Data Source={db.DbPath};Mode=ReadOnly;Pooling=False"))
            {
                await conn.OpenAsync(ct).ConfigureAwait(false);
                using var tx = conn.BeginTransaction();
                (images, tags) = await WritePackageAsync(conn, collectionId, libraryId, partialPath, progress, ct)
                    .ConfigureAwait(false);
            }

            ct.ThrowIfCancellationRequested();
            // 構造検証: 書いたファイルを読み直し、途中切れ・件数不一致を拒否してから正式名へ
            await using (var verify = File.OpenRead(partialPath))
            {
                var header = PackageJson.ReadHeader(verify);
                if (header.ImageCount != images || header.Tags.Count != tags)
                {
                    throw new PackageFormatException($"構造検証で件数不一致(images {header.ImageCount}/{images})");
                }
            }

            File.Delete(outputPath);
            File.Move(partialPath, outputPath);
            return Result<ExportSummary>.Ok(new ExportSummary(outputPath, images, tags));
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException or PackageFormatException or OperationCanceledException)
        {
            SnapshotService.TryDelete(partialPath);
            if (ex is OperationCanceledException)
            {
                throw;
            }

            return Result<ExportSummary>.Fail(
                ex is PackageFormatException ? ErrorCode.ValidationError : ErrorCode.IoError, ex.Message);
        }
    }

    private async Task<(long Images, int Tags)> WritePackageAsync(
        SqliteConnection conn,
        string collectionId,
        string libraryId,
        string partialPath,
        IProgress<(long, long)>? progress,
        CancellationToken ct)
    {
        var folder = await conn.QuerySingleOrDefaultAsync<(string Id, string Name, string Path)?>(
            "SELECT id, name, path FROM sync_folders WHERE id = @Id", new { Id = collectionId }).ConfigureAwait(false)
            ?? throw new PackageFormatException($"コレクションが見つかりません: {collectionId}");

        var total = await conn.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM images WHERE sync_folder_id = @Id AND status IN ('normal','missing')",
            new { Id = collectionId }).ConfigureAwait(false);

        var tagDefs = await LoadTagClosureAsync(conn, collectionId).ConfigureAwait(false);
        var tagTypeById = tagDefs.ToDictionary(t => t.SourceId, t => t.Type, StringComparer.Ordinal);

        await using var stream = File.Create(partialPath);
        // 人間が読める形式(§3.5): 日本語タグ名を \uXXXX にしない(UTF-8 ファイルへの relaxed エスケープは安全)
        await using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
        writer.WriteStartObject();
        writer.WriteString("format", CollectionPackageFormat.Format);
        writer.WriteNumber("formatVersion", CollectionPackageFormat.FormatVersion);
        writer.WriteNumber("minReaderVersion", 1);
        writer.WriteStartArray("features");
        foreach (var f in CollectionPackageFormat.KnownFeatures.Order(StringComparer.Ordinal))
        {
            writer.WriteStringValue(f);
        }

        writer.WriteEndArray();
        writer.WriteString("kind", CollectionPackageFormat.Kind);
        writer.WriteString("backupId", Guid.NewGuid().ToString("D"));
        writer.WriteString("sourceLibraryId", libraryId);
        writer.WriteString("createdAt", clock.UtcNowIso());
        writer.WriteString("appVersion", appVersion);

        writer.WriteStartObject("collection");
        writer.WriteString("sourceId", folder.Id);
        writer.WriteString("name", folder.Name);
        writer.WriteStartObject("rootHint"); // 識別子でなく復元先推測のヒント(§3.3)
        writer.WriteString("platform", OperatingSystem.IsWindows() ? "windows" : "other");
        writer.WriteString("path", folder.Path);
        writer.WriteEndObject();
        writer.WriteEndObject();

        writer.WriteStartArray("tags");
        foreach (var t in tagDefs)
        {
            WriteTag(writer, t);
        }

        writer.WriteEndArray();

        // images: 画像カーソルと付与カーソルを image_id 順のマージ結合でストリーム(全件メモリ展開しない)
        writer.WriteStartArray("images");
        long done = 0;
        var assignments = conn.Query<(string ImageId, string TagId, string? Value)>(
            """
            SELECT it.image_id, it.tag_id, it.value FROM image_tags it
            JOIN images i ON i.id = it.image_id
            WHERE i.sync_folder_id = @Id AND i.status IN ('normal','missing')
            ORDER BY it.image_id, it.tag_id
            """,
            new { Id = collectionId }, buffered: false).GetEnumerator();
        var pendingAssignment = assignments.MoveNext() ? assignments.Current : default((string, string, string?)?);

        foreach (var img in conn.Query<(string Id, string RelativePath, long FileSize, string Hash, string CreatedDate, string ModifiedDate)>(
            """
            SELECT id, relative_path, file_size, hash, created_date, modified_date FROM images
            WHERE sync_folder_id = @Id AND status IN ('normal','missing')
            ORDER BY id
            """,
            new { Id = collectionId }, buffered: false))
        {
            ct.ThrowIfCancellationRequested();
            writer.WriteStartObject();
            writer.WriteString("sourceId", img.Id);
            writer.WriteString("relativePath", PathNormalizer.Normalize(img.RelativePath));
            writer.WriteNumber("fileSize", img.FileSize);
            writer.WriteString("createdDate", img.CreatedDate);
            writer.WriteString("modifiedDate", img.ModifiedDate);
            writer.WriteStartObject("fingerprint");
            writer.WriteString("algorithm", CollectionPackageFormat.FingerprintAlgorithm);
            writer.WriteNumber("version", 1);
            writer.WriteString("value", img.Hash);
            writer.WriteNumber("sizeBytes", img.FileSize);
            writer.WriteEndObject();
            writer.WriteStartArray("tags");
            while (pendingAssignment is { } a && string.CompareOrdinal(a.Item1, img.Id) <= 0)
            {
                if (a.Item1 == img.Id)
                {
                    writer.WriteStartObject();
                    writer.WriteString("tagSourceId", a.Item2);
                    // numeric は正規形へ(4/4.0/04.000 の揺れを作らない・§3.1)。
                    // value は省略不可: simple の null も WriteString(null)=JSON null で明示する
                    var value = tagTypeById.GetValueOrDefault(a.Item2) == TagType.Numeric && a.Item3 is not null
                        ? TagValueFormat.TryNormalizeNumeric(a.Item3) ?? a.Item3
                        : a.Item3;
                    writer.WriteString("value", value);
                    writer.WriteEndObject();
                }

                pendingAssignment = assignments.MoveNext() ? assignments.Current : null;
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            if (++done % ProgressBatch == 0)
            {
                await writer.FlushAsync(ct).ConfigureAwait(false);
                progress?.Report((done, total));
            }
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        await writer.FlushAsync(ct).ConfigureAwait(false);
        progress?.Report((done, total));
        return (done, tagDefs.Count);
    }

    private static void WriteTag(Utf8JsonWriter writer, PackageTagDef t)
    {
        writer.WriteStartObject();
        writer.WriteString("sourceId", t.SourceId);
        writer.WriteString("name", t.Name);
        writer.WriteString("type", t.Type switch
        {
            TagType.Simple => "simple",
            TagType.Textual => "textual",
            _ => "numeric",
        });
        if (t.ParentSourceId is not null)
        {
            writer.WriteString("parentSourceId", t.ParentSourceId);
        }

        if (t.Color is not null)
        {
            writer.WriteString("color", t.Color);
        }

        if (t.Description is not null)
        {
            writer.WriteString("description", t.Description);
        }

        if (t.PredefinedValues.Count > 0)
        {
            writer.WriteStartArray("predefinedValues");
            foreach (var v in t.PredefinedValues)
            {
                writer.WriteStringValue(v);
            }

            writer.WriteEndArray();
        }

        WriteOptionalNumber(writer, "minimum", t.Min);
        WriteOptionalNumber(writer, "maximum", t.Max);
        WriteOptionalNumber(writer, "step", t.Step);
        if (t.Unit is not null)
        {
            writer.WriteString("unit", t.Unit);
        }

        writer.WriteEndObject();
    }

    private static void WriteOptionalNumber(Utf8JsonWriter writer, string name, double? value)
    {
        if (value is { } v)
        {
            writer.WriteNumber(name, v);
        }
    }

    /// <summary>参照タグの依存閉包(付与タグ+祖先チェーン)を親→子のトポロジカル順で返す。</summary>
    private static async Task<List<PackageTagDef>> LoadTagClosureAsync(SqliteConnection conn, string collectionId)
    {
        var allTags = (await conn.QueryAsync<(string Id, string Name, string Type, string? ParentId, string? Color, string? Description)>(
            "SELECT id, name, type, parent_id, color, description FROM tags").ConfigureAwait(false))
            .ToDictionary(t => t.Id, StringComparer.Ordinal);
        var textual = (await conn.QueryAsync<(string TagId, string PredefinedValues)>(
            "SELECT tag_id, predefined_values FROM textual_tag_settings").ConfigureAwait(false))
            .ToDictionary(t => t.TagId, t => t.PredefinedValues, StringComparer.Ordinal);
        var numeric = (await conn.QueryAsync<(string TagId, double? Min, double? Max, double? Step, string? Unit)>(
            "SELECT tag_id, min, max, step, unit FROM numeric_tag_settings").ConfigureAwait(false))
            .ToDictionary(t => t.TagId, StringComparer.Ordinal);

        var used = await conn.QueryAsync<string>(
            """
            SELECT DISTINCT it.tag_id FROM image_tags it
            JOIN images i ON i.id = it.image_id
            WHERE i.sync_folder_id = @Id AND i.status IN ('normal','missing')
            """,
            new { Id = collectionId }).ConfigureAwait(false);

        // 祖先チェーンを含む閉包を親→子順で構築
        var closure = new List<PackageTagDef>();
        var visited = new HashSet<string>(StringComparer.Ordinal);

        void Add(string id)
        {
            if (!visited.Add(id) || !allTags.TryGetValue(id, out var t))
            {
                return;
            }

            if (t.ParentId is not null)
            {
                Add(t.ParentId);
            }

            closure.Add(new PackageTagDef(
                t.Id, t.Name,
                t.Type switch { "simple" => TagType.Simple, "textual" => TagType.Textual, _ => TagType.Numeric },
                t.ParentId, t.Color, t.Description,
                textual.TryGetValue(t.Id, out var pv) ? DbMapping.FromJsonArray(pv) : [],
                numeric.TryGetValue(t.Id, out var n) ? n.Min : null,
                numeric.TryGetValue(t.Id, out n) ? n.Max : null,
                numeric.TryGetValue(t.Id, out n) ? n.Step : null,
                numeric.TryGetValue(t.Id, out n) ? n.Unit : null));
        }

        foreach (var id in used)
        {
            Add(id);
        }

        return closure;
    }
}
