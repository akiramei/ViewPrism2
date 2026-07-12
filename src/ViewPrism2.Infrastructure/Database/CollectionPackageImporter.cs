using Dapper;
using Microsoft.Data.Sqlite;
using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services.Package;

namespace ViewPrism2.Infrastructure.Database;

/// <summary>画像照合の 5 状態(ECO-073 §3.3+gate①採用の参照のみ登録)。</summary>
public enum ImageMatchKind
{
    /// <summary>一致(id またはパス+ハッシュ一致)→ 自動採用。</summary>
    Exact,

    /// <summary>移動を検出(パス不一致・ハッシュが一意に一致)→ 自動追随。</summary>
    Moved,

    /// <summary>変更あり(パス一致・ハッシュ不一致)→ 競合扱い。取込は明示操作のみ(V1 はスキップ)。</summary>
    Changed,

    /// <summary>曖昧(ハッシュが複数候補に一致)→ 自動選択しない(V1 はスキップ)。</summary>
    Ambiguous,

    /// <summary>未解決(一致先なし)→ missing 行として参照のみ登録(タグは着地・規則 3a で後日リンク)。</summary>
    Unresolved,
}

public sealed record ImageMatchCounts(long Exact, long Moved, long Changed, long Ambiguous, long Unresolved)
{
    public long Total => Exact + Moved + Changed + Ambiguous + Unresolved;

    /// <summary>過半ガード(§7 条件4): 未解決が過半=取り込み先ルート指定ミスの疑い。</summary>
    public bool MajorityUnresolved => Total > 0 && Unresolved * 2 > Total;
}

/// <summary>B-3 プレビュー(ドライラン必須・§3.5)。</summary>
public sealed record ImportPreview(
    PackageHeader Header,
    TagImportPlan TagPlan,
    ImageMatchCounts Images,
    IReadOnlyList<string> UnresolvedSamples,
    string TargetFolderId);

/// <summary>B-4 結果レポートの件数。</summary>
public sealed record ImportResult(
    long AddedAssignments,
    long UnchangedAssignments,
    long SkippedAssignments,
    long ConflictKeptAssignments,
    int CreatedTags,
    int MappedTags,
    long RegisteredMissing,
    ImageMatchCounts Images,
    IReadOnlyList<string> UnresolvedSamples);

/// <summary>
/// コレクション論理パッケージの取り込み(ECO-073)。解析/プレビューはトランザクション外、
/// 実適用は単一トランザクション(例外・キャンセルで全ロールバック)。適用直前にタグ計画を
/// 現在の DB 状態から再構築し、プレビュー後の DB 変化で競合が生じたら適用しない(鮮度再検証)。
/// マージは追加型: 追加はするが削除はしない。値競合は現行維持(§3.4)。
/// </summary>
public sealed class CollectionPackageImporter(DatabaseManager db, IClock clock)
{
    private const int UnresolvedSampleLimit = 100;

    /// <summary>B-2: ヘッダ検証+概要(失敗=互換性 NG またはファイル不正。DB は変更しない)。</summary>
    public Result<PackageHeader> ReadHeader(string packagePath)
    {
        try
        {
            using var stream = File.OpenRead(packagePath);
            return Result<PackageHeader>.Ok(PackageJson.ReadHeader(stream));
        }
        catch (PackageFormatException ex)
        {
            return Result<PackageHeader>.Fail(ErrorCode.ValidationError, ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<PackageHeader>.Fail(ErrorCode.IoError, ex.Message);
        }
    }

    /// <summary>B-3: ドライラン(タグ計画+画像 5 状態+未解決一覧)。DB は変更しない。</summary>
    public async Task<Result<ImportPreview>> PreviewAsync(
        string packagePath,
        string targetFolderId,
        IReadOnlyDictionary<string, TagConflictResolution>? resolutions = null,
        CancellationToken ct = default)
    {
        try
        {
            var header = ReadHeaderOrThrow(packagePath);
            var plan = TagImportPlanner.Plan(header.Tags, await LoadLocalTagStateAsync(header, ct).ConfigureAwait(false), resolutions);
            if (plan.Errors.Count > 0)
            {
                return Result<ImportPreview>.Fail(ErrorCode.ValidationError, string.Join(" / ", plan.Errors));
            }

            var local = await LoadLocalImagesAsync(targetFolderId, ct).ConfigureAwait(false);
            var (counts, samples) = MatchImages(packagePath, header, local, ct);
            return Result<ImportPreview>.Ok(new ImportPreview(header, plan, counts, samples, targetFolderId));
        }
        catch (PackageFormatException ex)
        {
            return Result<ImportPreview>.Fail(ErrorCode.ValidationError, ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException)
        {
            return Result<ImportPreview>.Fail(ErrorCode.IoError, ex.Message);
        }
    }

    /// <summary>
    /// 実適用(単一トランザクション)。タグ競合が未解決なら実行しない(B-3 ブロック)。
    /// 未解決画像が過半のときは acceptMajorityUnresolved=true を要求する(EX-002=過半・確認ゲート)。
    /// </summary>
    public async Task<Result<ImportResult>> ApplyAsync(
        string packagePath,
        string targetFolderId,
        IReadOnlyDictionary<string, TagConflictResolution>? resolutions = null,
        bool acceptMajorityUnresolved = false,
        CancellationToken ct = default)
    {
        try
        {
            var header = ReadHeaderOrThrow(packagePath);
            // 鮮度再検証: 適用直前に現在の DB 状態でタグ計画を再構築(プレビュー後の変化で競合が増えたら中止)
            var localState = await LoadLocalTagStateAsync(header, ct).ConfigureAwait(false);
            var plan = TagImportPlanner.Plan(header.Tags, localState, resolutions);
            if (plan.Errors.Count > 0)
            {
                return Result<ImportResult>.Fail(ErrorCode.ValidationError, string.Join(" / ", plan.Errors));
            }

            if (plan.UnresolvedConflicts.Count > 0)
            {
                return Result<ImportResult>.Fail(ErrorCode.ValidationError,
                    $"タグ競合 {plan.UnresolvedConflicts.Count} 件が未解決です。解決するまで実行できません。");
            }

            // 過半ガード(書き込み前の count-only 走査で判定してから適用する)
            var local = await LoadLocalImagesAsync(targetFolderId, ct).ConfigureAwait(false);
            var (counts, samples) = MatchImages(packagePath, header, local, ct);
            if (counts.MajorityUnresolved && !acceptMajorityUnresolved)
            {
                return Result<ImportResult>.Fail(ErrorCode.ValidationError,
                    $"未解決の画像が過半({counts.Unresolved}/{counts.Total})です。取り込み先ルートの指定を確認してください。");
            }

            var result = await db.RunAsync(conn =>
                Task.FromResult(ApplyInTransaction(conn, packagePath, targetFolderId, header, plan, counts, samples, ct)), ct)
                .ConfigureAwait(false);
            return result;
        }
        catch (PackageFormatException ex)
        {
            return Result<ImportResult>.Fail(ErrorCode.ValidationError, ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<ImportResult>.Fail(ErrorCode.IoError, ex.Message);
        }
    }

    // ---- 内部 ----

    private static PackageHeader ReadHeaderOrThrow(string packagePath)
    {
        using var stream = File.OpenRead(packagePath);
        return PackageJson.ReadHeader(stream);
    }

    private async Task<LocalTagState> LoadLocalTagStateAsync(PackageHeader header, CancellationToken ct)
        => await db.RunAsync(async conn =>
        {
            var tags = (await conn.QueryAsync<(string Id, string Name, string Type, string? ParentId, string? Color, string? Description)>(
                "SELECT id, name, type, parent_id, color, description FROM tags").ConfigureAwait(false))
                .Select(t => new Tag
                {
                    Id = t.Id,
                    Name = t.Name,
                    Type = t.Type switch { "simple" => TagType.Simple, "textual" => TagType.Textual, _ => TagType.Numeric },
                    ParentId = t.ParentId,
                    Color = t.Color,
                    Description = t.Description,
                })
                .ToList();
            var textual = (await conn.QueryAsync<(string TagId, string PredefinedValues)>(
                "SELECT tag_id, predefined_values FROM textual_tag_settings").ConfigureAwait(false))
                .ToDictionary(
                    t => t.TagId,
                    t => new TextualTagSettings { TagId = t.TagId, PredefinedValues = DbMapping.FromJsonArray(t.PredefinedValues) },
                    StringComparer.Ordinal);
            var numeric = (await conn.QueryAsync<(string TagId, double? Min, double? Max, double? Step, string? Unit)>(
                "SELECT tag_id, min, max, step, unit FROM numeric_tag_settings").ConfigureAwait(false))
                .ToDictionary(
                    t => t.TagId,
                    t => new NumericTagSettings { TagId = t.TagId, Min = t.Min, Max = t.Max, Step = t.Step, Unit = t.Unit },
                    StringComparer.Ordinal);
            var mappings = header.SourceLibraryId is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : (await conn.QueryAsync<(string SourceTagId, string LocalTagId)>(
                    "SELECT source_tag_id, local_tag_id FROM tag_import_mappings WHERE source_library_id = @Lib",
                    new { Lib = header.SourceLibraryId }).ConfigureAwait(false))
                    .ToDictionary(m => m.SourceTagId, m => m.LocalTagId, StringComparer.Ordinal);
            return new LocalTagState(tags, textual, numeric, mappings);
        }, ct).ConfigureAwait(false);

    private sealed record LocalImages(
        HashSet<string> Ids,
        Dictionary<string, (string Id, string Hash)> ByPath,
        Dictionary<string, List<string>> ByHash);

    private async Task<LocalImages> LoadLocalImagesAsync(string targetFolderId, CancellationToken ct)
        => await db.RunAsync(async conn =>
        {
            var rows = await conn.QueryAsync<(string Id, string RelativePath, string Hash)>(
                "SELECT id, relative_path, hash FROM images WHERE sync_folder_id = @Id",
                new { Id = targetFolderId }).ConfigureAwait(false);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var byPath = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
            var byHash = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var r in rows)
            {
                ids.Add(r.Id);
                byPath[PathNormalizer.Normalize(r.RelativePath)] = (r.Id, r.Hash);
                (byHash.TryGetValue(r.Hash, out var list) ? list : byHash[r.Hash] = []).Add(r.Id);
            }

            return new LocalImages(ids, byPath, byHash);
        }, ct).ConfigureAwait(false);

    private static (ImageMatchKind Kind, string? LocalImageId) MatchImage(PackageImage image, LocalImages local)
    {
        if (local.Ids.Contains(image.SourceId))
        {
            return (ImageMatchKind.Exact, image.SourceId); // 同一ライブラリ再取込=同一画像行
        }

        if (local.ByPath.TryGetValue(image.RelativePath, out var atPath))
        {
            return atPath.Hash == image.Fingerprint.Value
                ? (ImageMatchKind.Exact, atPath.Id)
                : (ImageMatchKind.Changed, atPath.Id);
        }

        if (local.ByHash.TryGetValue(image.Fingerprint.Value, out var byHash))
        {
            return byHash.Count == 1
                ? (ImageMatchKind.Moved, byHash[0])
                : (ImageMatchKind.Ambiguous, null);
        }

        return (ImageMatchKind.Unresolved, null);
    }

    private static (ImageMatchCounts Counts, List<string> Samples) MatchImages(
        string packagePath, PackageHeader header, LocalImages local, CancellationToken ct)
    {
        long exact = 0, moved = 0, changed = 0, ambiguous = 0, unresolved = 0;
        var samples = new List<string>();
        var knownTagIds = header.Tags.Select(t => t.SourceId).ToHashSet(StringComparer.Ordinal);
        using var stream = File.OpenRead(packagePath);
        foreach (var image in PackageJson.ReadImages(stream, knownTagIds))
        {
            ct.ThrowIfCancellationRequested();
            switch (MatchImage(image, local).Kind)
            {
                case ImageMatchKind.Exact: exact++; break;
                case ImageMatchKind.Moved: moved++; break;
                case ImageMatchKind.Changed: changed++; break;
                case ImageMatchKind.Ambiguous: ambiguous++; break;
                default:
                    unresolved++;
                    if (samples.Count < UnresolvedSampleLimit)
                    {
                        samples.Add(image.RelativePath);
                    }

                    break;
            }
        }

        return (new ImageMatchCounts(exact, moved, changed, ambiguous, unresolved), samples);
    }

    /// <summary>単一トランザクションの実適用(共有接続上・例外/キャンセルで全ロールバック)。</summary>
    private Result<ImportResult> ApplyInTransaction(
        SqliteConnection conn,
        string packagePath,
        string targetFolderId,
        PackageHeader header,
        TagImportPlan plan,
        ImageMatchCounts counts,
        List<string> unresolvedSamples,
        CancellationToken ct)
    {
        using var tx = conn.BeginTransaction();
        try
        {
            var now = clock.UtcNowIso();
            // 1) タグ新規作成(親→子順は plan が入力順で保証: Creations は package 順・親は閉包で先行)
            var localIdBySource = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var item in plan.Items)
            {
                localIdBySource[item.Source.SourceId] = item.Decision switch
                {
                    TagImportDecision.MappedByPersistentMapping or TagImportDecision.MappedById
                        or TagImportDecision.MappedBySemantic or TagImportDecision.ResolvedManualMap => item.LocalTagId,
                    TagImportDecision.CreateNew or TagImportDecision.ResolvedRename => item.Source.SourceId,
                    _ => null,
                };
            }

            var createdTags = 0;
            foreach (var item in plan.Creations)
            {
                var t = item.Source;
                var parentLocal = t.ParentSourceId is null ? null : localIdBySource.GetValueOrDefault(t.ParentSourceId);
                conn.Execute(
                    """
                    INSERT INTO tags (id, name, type, parent_id, color, description)
                    VALUES (@Id, @Name, @Type, @ParentId, @Color, @Description)
                    """,
                    new
                    {
                        Id = t.SourceId,
                        Name = item.CreateName,
                        Type = t.Type switch { TagType.Simple => "simple", TagType.Textual => "textual", _ => "numeric" },
                        ParentId = parentLocal,
                        t.Color,
                        t.Description,
                    }, tx);
                if (t.Type == TagType.Textual && t.PredefinedValues.Count > 0)
                {
                    conn.Execute(
                        "INSERT INTO textual_tag_settings (tag_id, predefined_values) VALUES (@Id, @Values)",
                        new { Id = t.SourceId, Values = DbMapping.ToJsonArray(t.PredefinedValues) }, tx);
                }

                if (t.Type == TagType.Numeric && ((t.Min ?? t.Max ?? t.Step) is not null || t.Unit is not null))
                {
                    conn.Execute(
                        "INSERT INTO numeric_tag_settings (tag_id, min, max, step, unit) VALUES (@Id, @Min, @Max, @Step, @Unit)",
                        new { Id = t.SourceId, t.Min, t.Max, t.Step, t.Unit }, tx);
                }

                createdTags++;
            }

            // 2) 永続マッピングの保存(意味定義一致の自動マッピング+手動マッピング。次回から同じ判断を繰り返さない)
            var mappedTags = 0;
            if (header.SourceLibraryId is { Length: > 0 } lib)
            {
                foreach (var item in plan.Items.Where(i =>
                    i.Decision is TagImportDecision.MappedBySemantic or TagImportDecision.ResolvedManualMap))
                {
                    conn.Execute(
                        """
                        INSERT INTO tag_import_mappings (source_library_id, source_tag_id, local_tag_id, created_at, updated_at)
                        VALUES (@Lib, @Source, @Local, @Now, @Now)
                        ON CONFLICT(source_library_id, source_tag_id)
                        DO UPDATE SET local_tag_id = @Local, updated_at = @Now
                        """,
                        new { Lib = lib, Source = item.Source.SourceId, Local = item.LocalTagId, Now = now }, tx);
                    mappedTags++;
                }
            }

            // 3) 画像+付与のストリーム適用
            var localTagTypes = conn.Query<(string Id, string Type)>("SELECT id, type FROM tags", transaction: tx)
                .ToDictionary(t => t.Id, t => t.Type switch { "simple" => TagType.Simple, "textual" => TagType.Textual, _ => TagType.Numeric }, StringComparer.Ordinal);
            var localNumeric = conn.Query<(string TagId, double? Min, double? Max, double? Step, string? Unit)>(
                "SELECT tag_id, min, max, step, unit FROM numeric_tag_settings", transaction: tx)
                .ToDictionary(
                    n => n.TagId,
                    n => new NumericTagSettings { TagId = n.TagId, Min = n.Min, Max = n.Max, Step = n.Step, Unit = n.Unit },
                    StringComparer.Ordinal);
            var localImages = new LocalImages([], new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase), new Dictionary<string, List<string>>(StringComparer.Ordinal));
            foreach (var r in conn.Query<(string Id, string RelativePath, string Hash)>(
                "SELECT id, relative_path, hash FROM images WHERE sync_folder_id = @Id", new { Id = targetFolderId }, transaction: tx))
            {
                localImages.Ids.Add(r.Id);
                localImages.ByPath[PathNormalizer.Normalize(r.RelativePath)] = (r.Id, r.Hash);
                (localImages.ByHash.TryGetValue(r.Hash, out var list) ? list : localImages.ByHash[r.Hash] = []).Add(r.Id);
            }

            // GF-073-05: missing 登録の ID 衝突ガードはライブラリ全域で判定する(images.id の PK は
            // コレクション横断。同一ライブラリ内の別コレクションへの取り込みで元行と衝突していた)
            var allImageIds = conn.Query<string>("SELECT id FROM images", transaction: tx)
                .ToHashSet(StringComparer.Ordinal);

            var existing = conn.Query<(string ImageId, string TagId, string? Value)>(
                """
                SELECT it.image_id, it.tag_id, it.value FROM image_tags it
                JOIN images i ON i.id = it.image_id WHERE i.sync_folder_id = @Id
                """, new { Id = targetFolderId }, transaction: tx)
                .ToDictionary(r => (r.ImageId, r.TagId), r => r.Value);

            long added = 0, unchanged = 0, skipped = 0, conflictKept = 0, registeredMissing = 0;
            var knownTagIds = header.Tags.Select(t => t.SourceId).ToHashSet(StringComparer.Ordinal);
            var packageTagById = header.Tags.ToDictionary(t => t.SourceId, StringComparer.Ordinal);
            using (var stream = File.OpenRead(packagePath))
            {
                foreach (var image in PackageJson.ReadImages(stream, knownTagIds))
                {
                    ct.ThrowIfCancellationRequested();
                    var (kind, localImageId) = MatchImage(image, localImages);
                    if (kind is ImageMatchKind.Changed or ImageMatchKind.Ambiguous)
                    {
                        skipped += image.Tags.Count; // 競合/曖昧は明示操作・ユーザー指定のみ(V1 スキップ)
                        continue;
                    }

                    if (kind == ImageMatchKind.Unresolved)
                    {
                        // gate①採用: missing 行として参照のみ登録(pending は次回スキャンで行削除されるため不可)。
                        // 実体出現時は規則 3a の候補提示→修復画面(ECO-005)確定で image_id 不変リンク。
                        // 再取込の冪等は登録行の path+hash 一致で維持される(sourceId 再利用は必須でない)
                        localImageId = allImageIds.Contains(image.SourceId) ? IdGenerator.NewId() : image.SourceId;
                        conn.Execute(
                            """
                            INSERT INTO images (id, sync_folder_id, relative_path, file_name, file_size, hash, status, created_date, modified_date)
                            VALUES (@Id, @FolderId, @RelativePath, @FileName, @FileSize, @Hash, 'missing', @CreatedDate, @ModifiedDate)
                            """,
                            new
                            {
                                Id = localImageId,
                                FolderId = targetFolderId,
                                image.RelativePath,
                                FileName = image.RelativePath[(image.RelativePath.LastIndexOf('/') + 1)..],
                                image.FileSize,
                                Hash = image.Fingerprint.Value,
                                image.CreatedDate,
                                image.ModifiedDate,
                            }, tx);
                        localImages.Ids.Add(localImageId);
                        allImageIds.Add(localImageId);
                        registeredMissing++;
                    }

                    foreach (var assignment in image.Tags)
                    {
                        var localTagId = localIdBySource.GetValueOrDefault(assignment.TagSourceId);
                        if (localTagId is null)
                        {
                            skipped++; // タグ側がスキップ解決
                            continue;
                        }

                        // 二段階検証(§3.2): パッケージ定義 → 取り込み先定義の順で値を検証(クランプしない)
                        var pkgTag = packageTagById[assignment.TagSourceId];
                        var pkgNumeric = pkgTag.Type == TagType.Numeric
                            ? new NumericTagSettings { TagId = pkgTag.SourceId, Min = pkgTag.Min, Max = pkgTag.Max, Step = pkgTag.Step, Unit = pkgTag.Unit }
                            : null;
                        var localType = localTagTypes.GetValueOrDefault(localTagId, pkgTag.Type);
                        if (TagValueFormat.Validate(pkgTag.Type, assignment.Value, pkgNumeric) is not null ||
                            TagValueFormat.Validate(localType, assignment.Value, localNumeric.GetValueOrDefault(localTagId)) is not null)
                        {
                            skipped++;
                            continue;
                        }

                        var key = (localImageId!, localTagId);
                        if (existing.TryGetValue(key, out var current))
                        {
                            if (TagValueFormat.ValuesEqual(localType, current, assignment.Value))
                            {
                                unchanged++; // 同じ値=変更なし
                            }
                            else
                            {
                                conflictKept++; // 異なる値=競合。既定は現行維持(§3.4)
                            }

                            continue;
                        }

                        conn.Execute(
                            "INSERT INTO image_tags (image_id, tag_id, value) VALUES (@ImageId, @TagId, @Value)",
                            new { ImageId = localImageId, TagId = localTagId, assignment.Value }, tx);
                        existing[key] = assignment.Value;
                        added++;
                    }
                }
            }

            tx.Commit();
            return Result<ImportResult>.Ok(new ImportResult(
                added, unchanged, skipped, conflictKept, createdTags, mappedTags, registeredMissing, counts, unresolvedSamples));
        }
        catch (SqliteException ex)
        {
            // UNIQUE 違反等=プレビュー後の DB 変化を含む。トランザクションは using で全ロールバック。
            // GF-073-05②: 生の SQL エラー文はユーザーへ見せない(診断用にエラーコードのみ添える)
            return Result<ImportResult>.Fail(ErrorCode.Database,
                $"適用に失敗したため全て取り消しました。データベースは変更されていません。プレビューからやり直してください。(DB エラー {ex.SqliteErrorCode})");
        }
    }
}
