using Dapper;
using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using ViewPrism2.Core.Services.Package;
using ViewPrism2.Core.Services.Repair;
using ViewPrism2.Core.Services.Similarity;
using ViewPrism2.Infrastructure.Database;
using ViewPrism2.Infrastructure.I18n;
using ViewPrism2.Infrastructure.Imaging;
using ViewPrism2.Infrastructure.Scanning;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// CP-PACKAGE-032(ECO-073): コレクションの論理書き出し/取り込み(バックアップB層 V1)。
/// 先行プローブ(R5): 是正前は Exporter/Importer/TagImportPlanner と migration 008 が存在しない
/// ことを実測で固定してから製品コードへ着手した(622件中2不合格)。
/// 挙動テストは §3 の確定仕様(照合5段・画像5状態・追加型マージ・厳格拒否・鮮度)を一時 DB fixture で固定する。
/// </summary>
[Trait("cp", "CP-PACKAGE-032")]
public sealed class CpPackage073Tests : IDisposable
{
    private readonly TempDb _source = new();
    private readonly TempDb _target = new();

    public void Dispose()
    {
        _source.Dispose();
        _target.Dispose();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---- fixture 補助 ----

    private static Task AddTagAsync(TempDb db, string id, string name, string type,
        string? parent = null, double? min = null, double? max = null)
        => db.Manager.RunAsync(async conn =>
        {
            await conn.ExecuteAsync(
                "INSERT INTO tags (id, name, type, parent_id) VALUES (@id, @name, @type, @parent)",
                new { id, name, type, parent });
            if (min is not null || max is not null)
            {
                await conn.ExecuteAsync(
                    "INSERT INTO numeric_tag_settings (tag_id, min, max) VALUES (@id, @min, @max)",
                    new { id, min, max });
            }
        });

    private static async Task<SyncFolder> AddFolderAsync(TempDb db, string path)
    {
        var folder = new SyncFolder { Id = IdGenerator.NewId(), Name = Path.GetFileName(path), Path = path };
        await db.Folders.AddAsync(folder);
        return folder;
    }

    private static async Task<string> AddImageAsync(TempDb db, string folderId, string relPath, string hash)
    {
        var id = IdGenerator.NewId();
        await db.Images.AddAsync(new ImageRecord
        {
            Id = id,
            SyncFolderId = folderId,
            RelativePath = relPath,
            FileName = relPath[(relPath.LastIndexOf('/') + 1)..],
            FileSize = 10,
            Hash = hash,
            Status = ImageStatus.Normal,
            CreatedDate = "2026-07-01T00:00:00.000Z",
            ModifiedDate = "2026-07-01T00:00:00.000Z",
        });
        return id;
    }

    private static Task AssignAsync(TempDb db, string imageId, string tagId, string? value)
        => db.Manager.RunAsync(conn => conn.ExecuteAsync(
            "INSERT INTO image_tags (image_id, tag_id, value) VALUES (@imageId, @tagId, @value)",
            new { imageId, tagId, value }));

    private static string Hash(char c) => new(c, 64);

    /// <summary>source: simple「旅行」+numeric「評価」(0..5)+画像2枚(a=旅行/評価4.0, b=付与なし)。</summary>
    private async Task<(SyncFolder Folder, string PackagePath, string TagTrip, string TagRate)> ExportFixtureAsync()
    {
        var folder = await AddFolderAsync(_source, Path.Combine(_source.Directory, "col"));
        await AddTagAsync(_source, "tag-trip", "旅行", "simple");
        await AddTagAsync(_source, "tag-rate", "評価", "numeric", min: 0, max: 5);
        var imgA = await AddImageAsync(_source, folder.Id, "2026/a.jpg", Hash('a'));
        await AddImageAsync(_source, folder.Id, "2026/b.jpg", Hash('b'));
        await AssignAsync(_source, imgA, "tag-trip", null);
        await AssignAsync(_source, imgA, "tag-rate", "4.0"); // 書き出しで正規形 "4" へ
        var path = Path.Combine(_source.Directory, "col" + CollectionPackageFormat.FileExtension);
        var exporter = new CollectionPackageExporter(_source.Manager, _source.Clock, "9.9.9");
        var result = await exporter.ExportAsync(folder.Id, path, ct: Ct);
        Assert.True(result.IsSuccess, result.Message);
        return (folder, path, "tag-trip", "tag-rate");
    }

    private CollectionPackageImporter NewImporter() => new(_target.Manager, _target.Clock);

    private Task<long> TargetCountAsync(string sql)
        => _target.Manager.RunAsync(conn => conn.ExecuteScalarAsync<long>(sql));

    // ---- 先行プローブ(R5・是正前 2 不合格の実測記録) ----

    [Fact]
    public void 書き出し器と取り込み器とプランナが存在する()
    {
        var infra = typeof(DatabaseManager).Assembly;
        Assert.NotNull(infra.GetType("ViewPrism2.Infrastructure.Database.CollectionPackageExporter"));
        Assert.NotNull(infra.GetType("ViewPrism2.Infrastructure.Database.CollectionPackageImporter"));
        var core = typeof(Tag).Assembly;
        Assert.NotNull(core.GetType("ViewPrism2.Core.Services.Package.TagImportPlanner"));
    }

    [Fact]
    public void migration008がタグマッピングとライブラリ識別を追加する()
    {
        // 唯一の DB 変更(§3.6): tag_import_mappings+DB内ライブラリUUID(library_metadata)
        Assert.Contains(DatabaseSchema.Migrations, m => m.Id == "008-collection-package");
    }

    // ---- 挙動(§5 受け入れ観点) ----

    [Fact]
    public async Task 書き出しはpartialを残さず正規形の値と依存閉包を含む()
    {
        var (_, path, _, _) = await ExportFixtureAsync();
        Assert.True(File.Exists(path));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*" + CollectionPackageFormat.PartialExtension));

        using var stream = File.OpenRead(path);
        var header = PackageJson.ReadHeader(stream);
        Assert.Equal(2, header.ImageCount);
        Assert.Equal(2, header.Tags.Count); // 依存閉包=付与タグ(祖先なし)
        Assert.NotNull(header.SourceLibraryId);

        using var stream2 = File.OpenRead(path);
        var images = PackageJson.ReadImages(stream2, header.Tags.Select(t => t.SourceId).ToHashSet(StringComparer.Ordinal)).ToList();
        var tagged = Assert.Single(images, i => i.Tags.Count > 0);
        Assert.Equal("4", tagged.Tags.Single(t => t.TagSourceId == "tag-rate").Value); // 4.0 → 正規形
        Assert.Null(tagged.Tags.Single(t => t.TagSourceId == "tag-trip").Value);       // simple=null 明示
    }

    [Fact]
    public async Task 取り込みは未解決をmissing登録し2回目は冪等()
    {
        var (_, path, _, _) = await ExportFixtureAsync();
        var target = await AddFolderAsync(_target, Path.Combine(_target.Directory, "dst"));
        var importer = NewImporter();

        // 全画像が未解決(=過半)なので確認ゲートが先に働く(EX-002)
        var blocked = await importer.ApplyAsync(path, target.Id, ct: Ct);
        Assert.False(blocked.IsSuccess);
        Assert.Equal(ErrorCode.ValidationError, blocked.Error);

        var first = await importer.ApplyAsync(path, target.Id, acceptMajorityUnresolved: true, ct: Ct);
        Assert.True(first.IsSuccess, first.Message);
        Assert.Equal(2, first.Value!.CreatedTags);
        Assert.Equal(2, first.Value.RegisteredMissing);   // gate①: missing 行として参照のみ登録
        Assert.Equal(2, first.Value.AddedAssignments);    // タグ付与も着地
        Assert.Equal(2L, await TargetCountAsync("SELECT COUNT(*) FROM images WHERE status = 'missing'"));

        // 2 回目: 画像は id 一致で解決・タグ/付与は増えない(冪等)
        var second = await importer.ApplyAsync(path, target.Id, ct: Ct);
        Assert.True(second.IsSuccess, second.Message);
        Assert.Equal(0, second.Value!.CreatedTags);
        Assert.Equal(0, second.Value.RegisteredMissing);
        Assert.Equal(0, second.Value.AddedAssignments);
        Assert.Equal(2, second.Value.UnchangedAssignments);
        Assert.Equal(2L, await TargetCountAsync("SELECT COUNT(*) FROM tags"));
        Assert.Equal(2L, await TargetCountAsync("SELECT COUNT(*) FROM image_tags"));
    }

    [Fact]
    public async Task UUID一致はリネーム後も現行名を維持して対応付く()
    {
        var (_, path, _, _) = await ExportFixtureAsync();
        var target = await AddFolderAsync(_target, Path.Combine(_target.Directory, "dst"));
        await AddTagAsync(_target, "tag-trip", "旅行(改名済み)", "simple"); // 同 UUID・別名

        var importer = NewImporter();
        var preview = await importer.PreviewAsync(path, target.Id, ct: Ct);
        Assert.True(preview.IsSuccess, preview.Message);
        var item = preview.Value!.TagPlan.Items.Single(i => i.Source.SourceId == "tag-trip");
        Assert.Equal(TagImportDecision.MappedById, item.Decision);

        var result = await importer.ApplyAsync(path, target.Id, acceptMajorityUnresolved: true, ct: Ct);
        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(1L, await TargetCountAsync("SELECT COUNT(*) FROM tags WHERE name = '旅行(改名済み)'"));
        Assert.Equal(0L, await TargetCountAsync("SELECT COUNT(*) FROM tags WHERE name = '旅行'")); // 巻き戻さない
    }

    [Fact]
    public async Task UUID一致でも型変更は事前競合として停止する()
    {
        var (_, path, _, _) = await ExportFixtureAsync();
        var target = await AddFolderAsync(_target, Path.Combine(_target.Directory, "dst"));
        await AddTagAsync(_target, "tag-rate", "評価", "simple"); // 同 UUID・型変更(numeric→simple)

        var importer = NewImporter();
        var preview = await importer.PreviewAsync(path, target.Id, ct: Ct);
        Assert.True(preview.IsSuccess);
        var item = preview.Value!.TagPlan.Items.Single(i => i.Source.SourceId == "tag-rate");
        Assert.Equal(TagImportDecision.Conflict, item.Decision);
        Assert.Equal(TagConflictKind.TypeChanged, item.ConflictKind);

        var apply = await importer.ApplyAsync(path, target.Id, acceptMajorityUnresolved: true, ct: Ct);
        Assert.False(apply.IsSuccess); // 未解決競合は実行ブロック
        Assert.Equal(0L, await TargetCountAsync("SELECT COUNT(*) FROM image_tags")); // DB 無変更
    }

    [Fact]
    public async Task 意味定義完全一致は自動マッピングされ対応関係が保存される()
    {
        var (_, path, _, _) = await ExportFixtureAsync();
        var target = await AddFolderAsync(_target, Path.Combine(_target.Directory, "dst"));
        // UUID 不一致・名前+型+設定+親が完全一致
        await AddTagAsync(_target, "local-trip", "旅行", "simple");
        await AddTagAsync(_target, "local-rate", "評価", "numeric", min: 0, max: 5);

        var importer = NewImporter();
        var result = await importer.ApplyAsync(path, target.Id, acceptMajorityUnresolved: true, ct: Ct);
        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(0, result.Value!.CreatedTags);
        Assert.Equal(2, result.Value.MappedTags);
        Assert.Equal(2L, await TargetCountAsync("SELECT COUNT(*) FROM tag_import_mappings"));

        // 次回は永続マッピングが最優先(同じ判断を繰り返さない)
        var preview = await importer.PreviewAsync(path, target.Id, ct: Ct);
        Assert.All(preview.Value!.TagPlan.Items,
            i => Assert.Equal(TagImportDecision.MappedByPersistentMapping, i.Decision));
    }

    [Fact]
    public async Task 名前一致でも範囲違いは自動マッピングせず別名取込で解決できる()
    {
        var (_, path, _, _) = await ExportFixtureAsync();
        var target = await AddFolderAsync(_target, Path.Combine(_target.Directory, "dst"));
        await AddTagAsync(_target, "local-rate", "評価", "numeric", min: 0, max: 100); // 範囲違い

        var importer = NewImporter();
        var preview = await importer.PreviewAsync(path, target.Id, ct: Ct);
        var item = preview.Value!.TagPlan.Items.Single(i => i.Source.SourceId == "tag-rate");
        Assert.Equal(TagImportDecision.Conflict, item.Decision);
        Assert.Equal(TagConflictKind.NameCollision, item.ConflictKind);

        var resolutions = new Dictionary<string, TagConflictResolution>
        {
            ["tag-rate"] = new(TagImportDecision.ResolvedRename, RenameTo: "評価 (取込)"),
        };
        var result = await importer.ApplyAsync(path, target.Id, resolutions, acceptMajorityUnresolved: true, Ct);
        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(1L, await TargetCountAsync("SELECT COUNT(*) FROM tags WHERE name = '評価 (取込)'"));
    }

    [Fact]
    public async Task 移動と曖昧と変更ありを区別して自動採用は一意ハッシュのみ()
    {
        var (_, path, _, _) = await ExportFixtureAsync();
        var target = await AddFolderAsync(_target, Path.Combine(_target.Directory, "dst"));
        var movedId = await AddImageAsync(_target, target.Id, "moved/renamed-a.jpg", Hash('a')); // パス不一致・一意ハッシュ
        await AddImageAsync(_target, target.Id, "2026/b.jpg", Hash('x'));                        // パス一致・ハッシュ不一致

        var importer = NewImporter();
        var preview = await importer.PreviewAsync(path, target.Id, ct: Ct);
        Assert.Equal(1, (int)preview.Value!.Images.Moved);
        Assert.Equal(1, (int)preview.Value.Images.Changed);
        Assert.Equal(0, (int)preview.Value.Images.Unresolved);

        var result = await importer.ApplyAsync(path, target.Id, ct: Ct);
        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(0, (int)result.Value!.RegisteredMissing);
        // 移動先ローカル画像へ付与が着地し(自動追随)、変更ありへは触れない
        Assert.Equal(2L, await TargetCountAsync(
            $"SELECT COUNT(*) FROM image_tags WHERE image_id = '{movedId}'"));

        // 曖昧: 同一ハッシュを複数にすると自動選択しない
        await AddImageAsync(_target, target.Id, "dup/copy-a.jpg", Hash('a'));
        await _target.Manager.RunAsync(conn => conn.ExecuteAsync(
            "DELETE FROM image_tags WHERE image_id = @id", new { id = movedId }), Ct);
        var preview2 = await importer.PreviewAsync(path, target.Id, ct: Ct);
        Assert.Equal(1, (int)preview2.Value!.Images.Ambiguous);
    }

    [Fact]
    public async Task 破損や未知の必須featureや循環はDBを変更せず拒否する()
    {
        var (_, path, _, _) = await ExportFixtureAsync();
        var target = await AddFolderAsync(_target, Path.Combine(_target.Directory, "dst"));
        var importer = NewImporter();

        // 未知の必須 feature
        var future = Path.Combine(_target.Directory, "future.json");
        File.WriteAllText(future, File.ReadAllText(path).Replace("\"tag-definition-v1\"", "\"quantum-tags-v9\""));
        Assert.False(importer.ReadHeader(future).IsSuccess);

        // 途中で切れたファイル
        var truncated = Path.Combine(_target.Directory, "truncated.json");
        var bytes = File.ReadAllBytes(path);
        File.WriteAllBytes(truncated, bytes[..(bytes.Length / 2)]);
        Assert.False(importer.ReadHeader(truncated).IsSuccess);

        // 親循環(タグ 2 件が相互参照)
        var cyclic = Path.Combine(_target.Directory, "cyclic.json");
        File.WriteAllText(cyclic, File.ReadAllText(path)
            .Replace("{\"sourceId\":\"tag-trip\",\"name\":\"旅行\",\"type\":\"simple\"",
                "{\"sourceId\":\"tag-trip\",\"name\":\"旅行\",\"type\":\"simple\",\"parentSourceId\":\"tag-rate\"")
            .Replace("{\"sourceId\":\"tag-rate\",\"name\":\"評価\",\"type\":\"numeric\"",
                "{\"sourceId\":\"tag-rate\",\"name\":\"評価\",\"type\":\"numeric\",\"parentSourceId\":\"tag-trip\""));
        var cyclicPreview = await importer.PreviewAsync(cyclic, target.Id, ct: Ct);
        Assert.False(cyclicPreview.IsSuccess);

        foreach (var bad in new[] { future, truncated, cyclic })
        {
            var apply = await importer.ApplyAsync(bad, target.Id, acceptMajorityUnresolved: true, ct: Ct);
            Assert.False(apply.IsSuccess);
        }

        Assert.Equal(0L, await TargetCountAsync("SELECT COUNT(*) FROM tags"));   // DB 無変更
        Assert.Equal(0L, await TargetCountAsync("SELECT COUNT(*) FROM images"));
    }

    [Fact]
    public async Task 範囲外の値はクランプせずスキップされる()
    {
        var folder = await AddFolderAsync(_source, Path.Combine(_source.Directory, "col2"));
        await AddTagAsync(_source, "tag-rate2", "評価2", "numeric", min: 0, max: 5);
        var img = await AddImageAsync(_source, folder.Id, "c.jpg", Hash('c'));
        await AssignAsync(_source, img, "tag-rate2", "7"); // 設定範囲外の残存値
        var path = Path.Combine(_source.Directory, "col2.json");
        var exported = await new CollectionPackageExporter(_source.Manager, _source.Clock, "9.9.9")
            .ExportAsync(folder.Id, path, ct: Ct);
        Assert.True(exported.IsSuccess);

        var target = await AddFolderAsync(_target, Path.Combine(_target.Directory, "dst2"));
        var result = await NewImporter().ApplyAsync(path, target.Id, acceptMajorityUnresolved: true, ct: Ct);
        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(1, (int)result.Value!.SkippedAssignments);   // 黙って丸めない=不適合はスキップ
        Assert.Equal(0L, await TargetCountAsync("SELECT COUNT(*) FROM image_tags"));
    }

    [Fact]
    public async Task 登録されたmissing行はスキャンで消えず実体出現時に規則3a候補になる()
    {
        var (_, path, _, _) = await ExportFixtureAsync();
        var targetDir = Path.Combine(_target.Directory, "dst-real", "2026");
        Directory.CreateDirectory(targetDir);
        var target = await AddFolderAsync(_target, Path.Combine(_target.Directory, "dst-real"));
        var result = await NewImporter().ApplyAsync(path, target.Id, acceptMajorityUnresolved: true, ct: Ct);
        Assert.True(result.IsSuccess, result.Message);

        // スキャン(空フォルダ): missing 行は削除されない(pending と違い保持される)
        var scan = new ScanService(_target.Folders, _target.Images, _target.Clock);
        Assert.True((await scan.ScanAsync(target.Id, null, Ct)).IsSuccess);
        Assert.Equal(2L, await TargetCountAsync("SELECT COUNT(*) FROM images WHERE status = 'missing'"));

        // 実体が出現(同ハッシュになるよう missing 行の hash をファイル実ハッシュへ合わせる)
        var file = Path.Combine(targetDir, "restored-a.jpg");
        File.WriteAllBytes(file, [1, 2, 3]);
        string realHash;
        await using (var fs = File.OpenRead(file))
        {
            realHash = FileHasher.ComputeSha256(fs);
        }

        await _target.Manager.RunAsync(conn => conn.ExecuteAsync(
            "UPDATE images SET hash = @realHash WHERE relative_path = '2026/a.jpg' AND status = 'missing'",
            new { realHash }), Ct);
        Assert.True((await scan.ScanAsync(target.Id, null, Ct)).IsSuccess);
        // 規則 3a: 新規ファイルは pending+candidate_link_id で登録され、修復画面(ECO-005)確定でリンク
        Assert.Equal(1L, await TargetCountAsync(
            "SELECT COUNT(*) FROM images WHERE status = 'pending' AND candidate_link_id IS NOT NULL"));
    }

    [Fact]
    public async Task 三点メニューの書き出しと取り込みはコレクションIDで配線される()
    {
        // SS-001 裁定(b): B層入口=画像タブ ⋯ メニュー。VM コマンド → IWindowService 配線を pin
        var win = new CapturingWindows();
        var folder = await AddFolderAsync(_target, Path.Combine(_target.Directory, "wired"));
        var vm = new App.ViewModels.ImageTabViewModel(
            _target.Folders, _target.Images, _target.Tags, new ImageSorter(),
            new ViewService(_target.Views, _target.Clock), new NodeGraphBuilder(),
            new PathConditionConverter(), new ConditionEvaluator(),
            new SimilaritySearchService(_target.Folders, _target.Images, _target.Features, _target.Similarities, new FakePHashImageReader(), _target.Clock),
            new MergeService(_target.Images, _target.Tags, _target.Merges),
            new TrashService(_target.Images, _target.Folders, new FilePresenceProbe()),
            win, new AppSettings(), new WorkspaceService(_target.Workspaces, _target.Clock), TestLoc.Empty());
        await vm.InitializeAsync(folder.Id);

        await vm.ExportCollectionCommand.ExecuteAsync(null);
        await vm.ImportCollectionCommand.ExecuteAsync(null);

        Assert.Equal([folder.Id], win.ExportCalls);
        Assert.Equal([folder.Id], win.ImportCalls);
    }

    private sealed class CapturingWindows : App.Services.IWindowService
    {
        public List<string> ExportCalls { get; } = [];

        public List<string> ImportCalls { get; } = [];

        public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(true);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
        public Task ShowFolderManagementAsync() => Task.CompletedTask;
        public Task ShowSettingsAsync() => Task.CompletedTask;
        public Task ShowSnapshotsAsync() => Task.CompletedTask;
        public Task ShowCollectionExportAsync(string collectionId) { ExportCalls.Add(collectionId); return Task.CompletedTask; }
        public Task ShowCollectionImportAsync(string collectionId) { ImportCalls.Add(collectionId); return Task.CompletedTask; }
        public Task<bool> ShowTagEditorAsync(Tag? existing) => Task.FromResult(false);
        public Task<bool> ShowViewEditDialogAsync(View? existing) => Task.FromResult(false);
        public Task<IReadOnlyList<string>?> ShowNumericValueDialogAsync(Tag tag, NumericTagSettings? settings, int selectionCount)
            => Task.FromResult<IReadOnlyList<string>?>(null);
        public Task<App.Services.NodeConditionResult?> ShowNodeConditionDialogAsync(Tag tag, HierarchyConditionType? currentType, string? currentValueJson)
            => Task.FromResult<App.Services.NodeConditionResult?>(null);
        public Task ShowRelinkAsync(string folderId) => Task.CompletedTask;
        public void ShowViewer(IReadOnlyList<App.ViewModels.ImageEntry> ordered, int startIndex) { }
        public Task ShowTrashAsync(string collectionId) => Task.CompletedTask;
        public Task ShowRepairAsync(string collectionId) => Task.CompletedTask;
    }

    [Fact]
    public async Task プレビュー後にDBが変化して競合が生じたら適用しない()
    {
        var (_, path, _, _) = await ExportFixtureAsync();
        var target = await AddFolderAsync(_target, Path.Combine(_target.Directory, "dst"));
        var importer = NewImporter();
        var preview = await importer.PreviewAsync(path, target.Id, ct: Ct);
        Assert.Empty(preview.Value!.TagPlan.UnresolvedConflicts);

        // プレビュー後に競合する同名タグ(意味定義違い)が作られた
        await AddTagAsync(_target, "local-rate", "評価", "numeric", min: 0, max: 100);

        var apply = await importer.ApplyAsync(path, target.Id, acceptMajorityUnresolved: true, ct: Ct);
        Assert.False(apply.IsSuccess); // 適用直前の再計画が競合を検出して中止
        Assert.Equal(0L, await TargetCountAsync("SELECT COUNT(*) FROM image_tags"));
    }

    /// <summary>
    /// GF-073-05(golden 所見 2026-07-12): 元コレクションが同居する**同一ライブラリ内**の別コレクション
    /// へ取り込むと、missing 登録が package sourceId を再利用して images.id(PK=ライブラリ全体)に
    /// 衝突し全体が失敗した。衝突ガード(415行)の走査が取り込み先コレクション限定だったのが真因。
    /// </summary>
    [Fact]
    public async Task 同一ライブラリ内の別コレクションへの取り込みはID衝突せずmissing登録で成功する()
    {
        var folderA = await AddFolderAsync(_source, Path.Combine(_source.Directory, "colA"));
        await AddTagAsync(_source, "tag-trip", "旅行", "simple");
        var imgA = await AddImageAsync(_source, folderA.Id, "2026/a.jpg", Hash('a'));
        await AssignAsync(_source, imgA, "tag-trip", null);
        var path = Path.Combine(_source.Directory, "colA" + CollectionPackageFormat.FileExtension);
        var exporter = new CollectionPackageExporter(_source.Manager, _source.Clock, "9.9.9");
        Assert.True((await exporter.ExportAsync(folderA.Id, path, ct: Ct)).IsSuccess);

        // 同じ DB(=同一ライブラリ)の別コレクションへ取り込む
        var folderB = await AddFolderAsync(_source, Path.Combine(_source.Directory, "colB"));
        var importer = new CollectionPackageImporter(_source.Manager, _source.Clock);
        var result = await importer.ApplyAsync(path, folderB.Id, acceptMajorityUnresolved: true, ct: Ct);

        Assert.True(result.IsSuccess, result.Message);
        var rows = (await _source.Manager.RunAsync(async conn => (await conn.QueryAsync<(string Id, string Status)>(
            "SELECT id, status FROM images WHERE sync_folder_id = @Id", new { Id = folderB.Id })).ToList(), Ct));
        var registered = Assert.Single(rows);
        Assert.Equal("missing", registered.Status);
        Assert.NotEqual(imgA, registered.Id); // 元行(コレクション A)と衝突しない新 UUID

        // タグ付与は新 missing 行へ着地し、元コレクション A は無傷
        Assert.Equal(1L, await _source.Manager.RunAsync(conn => conn.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM image_tags WHERE image_id = @Id", new { Id = registered.Id }), Ct));
        Assert.Equal(1L, await _source.Manager.RunAsync(conn => conn.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM images WHERE sync_folder_id = @Id AND status = 'normal'", new { Id = folderA.Id }), Ct));
    }

    /// <summary>
    /// GF-073-08(golden 所見 2026-07-12・診断の実測固定): 同一コレクションへの再取込では、
    /// 移動済みファイル(旧行=missing・新パス=pending 候補)は **id 一致が優先**して「一致」に
    /// 数えられ、タグは missing 行へ着地する(§2.14.4=一致は id またはパス+ハッシュ)。
    /// 新パスへの引き継ぎは規則 3a の候補→修復画面(ECO-005)の確定が担う(gate①裁定)。
    /// 「移動を検出」はこの経路では構造的に発火しない=golden 項目 6 の旧手順は検査手順の欠陥。
    /// </summary>
    [Fact]
    public async Task 同一コレクション再取込では移動ファイルはid一致で一致に数えタグはmissing行へ着地する()
    {
        var folderA = await AddFolderAsync(_source, Path.Combine(_source.Directory, "colA"));
        await AddTagAsync(_source, "tag-trip", "旅行", "simple");
        var imgA = await AddImageAsync(_source, folderA.Id, "sub1/a.jpg", Hash('a'));
        await AssignAsync(_source, imgA, "tag-trip", null);
        var path = Path.Combine(_source.Directory, "colA" + CollectionPackageFormat.FileExtension);
        var exporter = new CollectionPackageExporter(_source.Manager, _source.Clock, "9.9.9");
        Assert.True((await exporter.ExportAsync(folderA.Id, path, ct: Ct)).IsSuccess);

        // 移動+再スキャン後の DB 状態を再現: 旧行 missing 化+新パスに pending 候補(規則 3a)
        await _source.Manager.RunAsync(conn => conn.ExecuteAsync(
            "UPDATE images SET status = 'missing' WHERE id = @Id", new { Id = imgA }), Ct);
        await _source.Manager.RunAsync(conn => conn.ExecuteAsync(
            """
            INSERT INTO images (id, sync_folder_id, relative_path, file_name, file_size, hash, status, created_date, modified_date)
            VALUES ('img-pending', @F, 'test/a.jpg', 'a.jpg', 10, @H, 'pending', '2026-07-01T00:00:00.000Z', '2026-07-01T00:00:00.000Z')
            """, new { F = folderA.Id, H = Hash('a') }), Ct);

        var importer = new CollectionPackageImporter(_source.Manager, _source.Clock);
        var result = await importer.ApplyAsync(path, folderA.Id, ct: Ct);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(1, result.Value!.Images.Exact);   // id 一致=「一致」
        Assert.Equal(0, result.Value.Images.Moved);    // 移動検出はこの経路では発火しない
        Assert.Equal(0, result.Value.Images.Unresolved);
        Assert.Equal(1L, await _source.Manager.RunAsync(conn => conn.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM image_tags WHERE image_id = @Id", new { Id = imgA }), Ct)); // missing 行へ着地(冪等=変更なし)
    }

    /// <summary>
    /// GF-073-08: 「移動を検出(自動追随)」が発火する正しい経路= id 不在の取り込み先で、
    /// パッケージ記録と異なるパスに同一ハッシュのファイルが一意に存在する場合。付与は新パスの行へ着地。
    /// </summary>
    [Fact]
    public async Task 別コレクションで別パス同一ハッシュの画像は移動検出され付与が新パスへ着地する()
    {
        var (_, path, tagTrip, _) = await ExportFixtureAsync(); // a=sub なし "2026/a.jpg"(旅行+評価4)
        _ = tagTrip;
        var target = await AddFolderAsync(_target, Path.Combine(_target.Directory, "dst"));
        // 取り込み先には同一内容(ハッシュ一致)のファイルが別サブフォルダに存在する
        var movedId = await AddImageAsync(_target, target.Id, "moved/a.jpg", Hash('a'));

        var importer = NewImporter();
        var result = await importer.ApplyAsync(path, target.Id, acceptMajorityUnresolved: true, ct: Ct);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(1, result.Value!.Images.Moved);   // 移動を検出(自動追随)
        Assert.Equal(2L, await _target.Manager.RunAsync(conn => conn.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM image_tags WHERE image_id = @Id", new { Id = movedId }), Ct)); // 旅行+評価が新パスの行へ
    }

    /// <summary>
    /// GF-073-06(golden 所見 2026-07-12): 大量 missing 登録の適用が UI スレッド上で同期実行され、
    /// ウィンドウが「応答なし」→ 完了画面(B-4)がメインウィンドウの背面に隠れた。
    /// プレビュー/実行は呼び出しスレッドをブロックせず、制御を即時返すこと。
    /// </summary>
    [Fact]
    public async Task プレビューと実行は呼び出しスレッドを塞がず制御を即時返す()
    {
        var folder = await AddFolderAsync(_source, Path.Combine(_source.Directory, "colA"));
        await AddTagAsync(_source, "tag-trip", "旅行", "simple");
        await _source.Manager.RunAsync(async conn =>
        {
            for (var i = 0; i < 2000; i++)
            {
                await conn.ExecuteAsync(
                    """
                    INSERT INTO images (id, sync_folder_id, relative_path, file_name, file_size, hash, status, created_date, modified_date)
                    VALUES (@Id, @F, @P, @N, 10, @H, 'normal', '2026-07-01T00:00:00.000Z', '2026-07-01T00:00:00.000Z')
                    """,
                    new { Id = $"img-{i:D5}", F = folder.Id, P = $"2026/{i:D5}.jpg", N = $"{i:D5}.jpg", H = i.ToString("D64") });
                await conn.ExecuteAsync(
                    "INSERT INTO image_tags (image_id, tag_id, value) VALUES (@Id, 'tag-trip', NULL)",
                    new { Id = $"img-{i:D5}" });
            }
        }, Ct);
        var path = Path.Combine(_source.Directory, "colA" + CollectionPackageFormat.FileExtension);
        var exporter = new CollectionPackageExporter(_source.Manager, _source.Clock, "9.9.9");
        Assert.True((await exporter.ExportAsync(folder.Id, path, ct: Ct)).IsSuccess);

        var target = await AddFolderAsync(_target, Path.Combine(_target.Directory, "dst"));
        var loc = new LocalizationService(I18nResourceLoader.Load(
            Path.Combine(AppContext.BaseDirectory, "Assets", "i18n")));
        var vm = new App.ViewModels.CollectionImportViewModel(NewImporter(), target, loc,
            _ => Task.FromResult<string?>(null), () => Task.FromResult<IReadOnlyList<Tag>>([]));
        vm.PackagePath = path;
        vm.Header = NewImporter().ReadHeader(path).Value;

        var preview = vm.ToPreviewCommand.ExecuteAsync(null);
        Assert.False(preview.IsCompleted, "GF-073-06: プレビューが呼び出しスレッドを同期ブロックしている");
        await preview;

        vm.AcceptMajority = true; // 2000/2000 未解決=過半ガードの確認
        Assert.True(vm.CanExecute);
        var execute = vm.ExecuteCommand.ExecuteAsync(null);
        Assert.False(execute.IsCompleted, "GF-073-06: 実行が呼び出しスレッドを同期ブロックしている");
        await execute;

        Assert.Equal(3, vm.Step); // 成功して完了面へ
        Assert.Equal(2000L, await TargetCountAsync("SELECT COUNT(*) FROM images WHERE status = 'missing'"));
    }
}
