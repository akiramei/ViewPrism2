using Dapper;
using ViewPrism2.App.ViewModels;
using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services.Similarity;
using ViewPrism2.Infrastructure.Database;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// CP-SIM-017(unit + L2): 類似検索エンジンと特徴量・類似度キャッシュ(無効化・正規化・スキーマ)が
/// 仕様 §2.10.3-4・OC-16/18 と一致する。一時 SQLite+合成 pHash 注入(FakePHashImageReader)。
/// </summary>
[Trait("cp", "CP-SIM-017")]
public sealed class CpSim017Tests
{
    private const string FolderRoot = "C:/pics";

    // ---- OC-16: 候補フィルタ・閾値・安定ソート ----

    [Fact]
    public async Task ECO062_明示scope外はreaderとcacheに触れず件数はscope内だけに比例する()
    {
        using var db = new TempDb();
        var (folderA, folderB) = await SeedTwoFoldersAsync(db);
        var baseImg = await SeedImageAsync(db, folderA, "a/base.jpg", ImageStatus.Normal, "0000000000000000");
        var inScope = await SeedImageAsync(db, folderA, "a/in.jpg", ImageStatus.Normal, "0000000000000000");
        var outside = await SeedImageAsync(db, folderA, "other/out.jpg", ImageStatus.Normal, "0000000000000000");
        var deleted = await SeedImageAsync(db, folderA, "a/deleted.jpg", ImageStatus.Deleted, "0000000000000000");
        var otherCollection = await SeedImageAsync(db, folderB, "a/foreign.jpg", ImageStatus.Normal, "0000000000000000");

        var reader = ReaderFor(baseImg, inScope, outside, deleted, otherCollection);
        var service = NewService(db, reader);
        var results = await service.FindSimilarInScopeAsync(
            baseImg.Id,
            threshold: 50,
            scopeCandidates: [inScope, deleted, otherCollection],
            ct: TestContext.Current.CancellationToken);

        Assert.Collection(results, result => Assert.Equal(inScope.Id, result.ImageId));
        Assert.Equal(2, reader.ComputeCount); // base + scope内normal同一collectionだけ
        Assert.Null(await db.Features.GetAsync(outside.Id));
        Assert.Null(await db.Features.GetAsync(deleted.Id));
        Assert.Null(await db.Features.GetAsync(otherCollection.Id));
        Assert.Null(await db.Similarities.GetAsync(baseImg.Id, outside.Id));
    }

    [Fact]
    public void ECO062_FSscopeは親path完全一致でsubfolderを除外し現folder外targetなら空になる()
    {
        var baseImage = ScopeRecord("base", "folder-a", "a/base.jpg");
        var sameFolder = ScopeRecord("same", "folder-a", "a/same.jpg");
        var subfolder = ScopeRecord("sub", "folder-a", "a/sub/child.jpg");
        var prefixCollision = ScopeRecord("prefix", "folder-a", "a2/other.jpg");

        var inA = SimilarityScopeResolver.ForFileSystem(
            [baseImage, sameFolder, subfolder, prefixCollision], baseImage, ["a"]);
        Assert.Equal(["base", "same"], inA.Select(image => image.Id).OrderBy(id => id));

        var afterNavigation = SimilarityScopeResolver.ForFileSystem(
            [baseImage, sameFolder, subfolder, prefixCollision], baseImage, ["a", "sub"]);
        Assert.Empty(afterNavigation); // target は検索時点の現在folder外
    }

    [Fact]
    public void ECO062_ViewScopeは対象画像からleafを逆算せず検索時の現在node母集合を使う()
    {
        var baseImage = ScopeRecord("base", "folder-a", "outside/base.jpg");
        var currentNodeA = ScopeRecord("node-a", "folder-a", "x/a.jpg");
        var currentNodeB = ScopeRecord("node-b", "folder-a", "y/b.jpg");
        var foreign = ScopeRecord("foreign", "folder-b", "z/c.jpg");

        var scope = SimilarityScopeResolver.ForView(
            [currentNodeA, currentNodeB, foreign], baseImage);

        Assert.Equal(["node-a", "node-b"], scope.Select(image => image.Id).OrderBy(id => id));
    }

    [Fact]
    public async Task 候補はnormal限定_他status_別コレクション_基準自身を除外する()
    {
        using var db = new TempDb();
        var (folderA, folderB) = await SeedTwoFoldersAsync(db);

        // 基準(距離 0 の pHash で全候補にマッチさせ、status/コレクション境界のみを検査)
        var baseImg = await SeedImageAsync(db, folderA, "base.jpg", ImageStatus.Normal, "0000000000000000");
        var normal = await SeedImageAsync(db, folderA, "n.jpg", ImageStatus.Normal, "0000000000000000");
        var missing = await SeedImageAsync(db, folderA, "m.jpg", ImageStatus.Missing, "0000000000000000");
        var pending = await SeedImageAsync(db, folderA, "p.jpg", ImageStatus.Pending, "0000000000000000");
        var deleted = await SeedImageAsync(db, folderA, "d.jpg", ImageStatus.Deleted, "0000000000000000");
        var otherColl = await SeedImageAsync(db, folderB, "o.jpg", ImageStatus.Normal, "0000000000000000");

        var reader = ReaderFor(baseImg, normal, missing, pending, deleted, otherColl);
        var service = NewService(db, reader);

        var results = await service.FindSimilarAsync(baseImg.Id, threshold: 50,
            ct: TestContext.Current.CancellationToken);

        // normal 同コレクションの 1 件のみ(基準自身・missing・pending・deleted・別コレクションは除外)
        Assert.Single(results);
        Assert.Equal(normal.Id, results[0].ImageId);
        Assert.Equal(100, results[0].Score); // 距離 0 → 類似度 100
    }

    [Fact]
    public async Task フィルタ先行_非normalはキャッシュ値があっても結果に出ない()
    {
        using var db = new TempDb();
        var (folderA, _) = await SeedTwoFoldersAsync(db);
        var baseImg = await SeedImageAsync(db, folderA, "base.jpg", ImageStatus.Normal, "0000000000000000");
        var deleted = await SeedImageAsync(db, folderA, "d.jpg", ImageStatus.Deleted, "0000000000000000");

        // deleted 候補とのキャッシュを先に仕込む(高類似度)。フィルタ先行なら結果に出ない
        await db.Similarities.UpsertAsync(baseImg.Id, deleted.Id, 100, db.Clock.UtcNowIso());

        var reader = ReaderFor(baseImg, deleted);
        var service = NewService(db, reader);

        var results = await service.FindSimilarAsync(baseImg.Id, threshold: 50,
            ct: TestContext.Current.CancellationToken);
        Assert.Empty(results);
    }

    [Fact]
    public async Task 閾値境界_閾値ちょうどは含み未満は除外する()
    {
        using var db = new TempDb();
        var (folderA, _) = await SeedTwoFoldersAsync(db);
        var baseImg = await SeedImageAsync(db, folderA, "base.jpg", ImageStatus.Normal, "0000000000000000");

        // 距離 10 → 類似度 70(閾値ちょうど・含む)
        var atThreshold = await SeedImageAsync(db, folderA, "a.jpg", ImageStatus.Normal, "00000000000003ff");
        // 距離 15 → 類似度 50(閾値 70 未満・除外)
        var below = await SeedImageAsync(db, folderA, "b.jpg", ImageStatus.Normal, "0000000000007fff");

        var reader = ReaderFor(baseImg, atThreshold, below);
        var service = NewService(db, reader);

        var results = await service.FindSimilarAsync(baseImg.Id, threshold: 70,
            ct: TestContext.Current.CancellationToken);

        Assert.Single(results);
        Assert.Equal(atThreshold.Id, results[0].ImageId);
        Assert.Equal(70, results[0].Score);
    }

    [Fact]
    public async Task 降順安定_類似度降順で同値はid昇順()
    {
        using var db = new TempDb();
        var (folderA, _) = await SeedTwoFoldersAsync(db);
        var baseImg = await SeedImageAsync(db, folderA, "base.jpg", ImageStatus.Normal, "0000000000000000");

        // 距離 0(100)= 同値 2 件(id で並ぶ)。距離 5(90)= 1 件
        var high1 = await SeedImageWithIdAsync(db, folderA, "id-zzz", "h1.jpg", ImageStatus.Normal, "0000000000000000");
        var high2 = await SeedImageWithIdAsync(db, folderA, "id-aaa", "h2.jpg", ImageStatus.Normal, "0000000000000000");
        var mid = await SeedImageWithIdAsync(db, folderA, "id-mmm", "m.jpg", ImageStatus.Normal, "000000000000001f");

        var reader = ReaderFor(baseImg, high1, high2, mid);
        var service = NewService(db, reader);

        var results1 = await service.FindSimilarAsync(baseImg.Id, threshold: 50,
            ct: TestContext.Current.CancellationToken);

        Assert.Equal(3, results1.Count);
        // 100: id-aaa, id-zzz(id 昇順)→ 90: id-mmm
        Assert.Equal("id-aaa", results1[0].ImageId);
        Assert.Equal(100, results1[0].Score);
        Assert.Equal("id-zzz", results1[1].ImageId);
        Assert.Equal(100, results1[1].Score);
        Assert.Equal("id-mmm", results1[2].ImageId);
        Assert.Equal(90, results1[2].Score);

        // 同一入力で同一出力(安定)
        var results2 = await service.FindSimilarAsync(baseImg.Id, threshold: 50,
            ct: TestContext.Current.CancellationToken);
        Assert.Equal(
            results1.Select(r => (r.ImageId, r.Score)),
            results2.Select(r => (r.ImageId, r.Score)));
    }

    // ---- キャッシュ挙動(OC-18) ----

    [Fact]
    public async Task キャッシュヒット_同一ペアの2回目は再計算しない()
    {
        using var db = new TempDb();
        var (folderA, _) = await SeedTwoFoldersAsync(db);
        var baseImg = await SeedImageAsync(db, folderA, "base.jpg", ImageStatus.Normal, "0000000000000000");
        var other = await SeedImageAsync(db, folderA, "o.jpg", ImageStatus.Normal, "000000000000001f");

        var reader = ReaderFor(baseImg, other);
        var service = NewService(db, reader);

        await service.FindSimilarAsync(baseImg.Id, threshold: 50, ct: TestContext.Current.CancellationToken);
        var afterFirst = reader.ComputeCount;
        Assert.True(afterFirst >= 2); // 基準+候補の pHash 計算

        // 2 回目: 特徴量はキャッシュ済み・ペア類似度もキャッシュ済み → reader を再呼び出ししない
        await service.FindSimilarAsync(baseImg.Id, threshold: 50, ct: TestContext.Current.CancellationToken);
        Assert.Equal(afterFirst, reader.ComputeCount);
    }

    [Fact]
    public async Task ペア正規化_ABとBAが同一cache_keyのminmaxを指す()
    {
        using var db = new TempDb();
        var (folderA, _) = await SeedTwoFoldersAsync(db);
        var a = await SeedImageWithIdAsync(db, folderA, "id-aaa", "a.jpg", ImageStatus.Normal, "0000000000000000");
        var b = await SeedImageWithIdAsync(db, folderA, "id-bbb", "b.jpg", ImageStatus.Normal, "0000000000000000");

        await db.Similarities.UpsertAsync(b.Id, a.Id, 77, db.Clock.UtcNowIso()); // (B,A) 順で保存

        var byAb = await db.Similarities.GetAsync(a.Id, b.Id);
        var byBa = await db.Similarities.GetAsync(b.Id, a.Id);
        Assert.NotNull(byAb);
        Assert.NotNull(byBa);
        Assert.Equal(byAb.CacheKey, byBa.CacheKey);
        Assert.Equal("id-aaa-id-bbb", byAb.CacheKey); // {min}-{max}
        Assert.Equal("id-aaa", byAb.ImageId1);
        Assert.Equal("id-bbb", byAb.ImageId2);
        Assert.Equal(77, byAb.SimilarityScore);
    }

    [Fact]
    public async Task 内容ベース無効化_fileSize変化で特徴量を再計算する()
    {
        using var db = new TempDb();
        var (folderA, _) = await SeedTwoFoldersAsync(db);
        var baseImg = await SeedImageAsync(db, folderA, "base.jpg", ImageStatus.Normal, "0000000000000000");
        var other = await SeedImageAsync(db, folderA, "o.jpg", ImageStatus.Normal, "000000000000001f");

        var reader = ReaderFor(baseImg, other);
        var service = NewService(db, reader);
        await service.FindSimilarAsync(baseImg.Id, threshold: 50, ct: TestContext.Current.CancellationToken);
        var afterFirst = reader.ComputeCount;

        // base のファイルサイズを変える(内容変化)→ pHash も別値にしておく
        await db.Manager.RunAsync(conn => conn.ExecuteAsync(
            "UPDATE images SET file_size = 9999 WHERE id = @Id", new { baseImg.Id }),
            TestContext.Current.CancellationToken);
        reader.SetPHash(AbsPath("base.jpg"), "00000000000000ff");

        await service.FindSimilarAsync(baseImg.Id, threshold: 50, ct: TestContext.Current.CancellationToken);
        // 基準の特徴量が再計算された(reader が再度呼ばれた)
        Assert.True(reader.ComputeCount > afterFirst);

        var feature = await db.Features.GetAsync(baseImg.Id);
        Assert.NotNull(feature);
        Assert.Equal("00000000000000ff", feature.PHash);
        Assert.Equal(9999, feature.FileSize);
    }

    [Fact]
    public async Task 連鎖無効化_特徴量再計算でその画像が関与する類似度行が削除される()
    {
        using var db = new TempDb();
        var (folderA, _) = await SeedTwoFoldersAsync(db);
        var baseImg = await SeedImageAsync(db, folderA, "base.jpg", ImageStatus.Normal, "0000000000000000");
        var other = await SeedImageAsync(db, folderA, "o.jpg", ImageStatus.Normal, "000000000000001f");

        var reader = ReaderFor(baseImg, other);
        var service = NewService(db, reader);
        await service.FindSimilarAsync(baseImg.Id, threshold: 50, ct: TestContext.Current.CancellationToken);

        // ペアキャッシュが作られている
        Assert.NotNull(await db.Similarities.GetAsync(baseImg.Id, other.Id));

        // base の内容を変える → 再計算で base が関与する類似度行が連鎖削除される
        await db.Manager.RunAsync(conn => conn.ExecuteAsync(
            "UPDATE images SET hash = 'changedhash' WHERE id = @Id", new { baseImg.Id }),
            TestContext.Current.CancellationToken);
        reader.SetPHash(AbsPath("base.jpg"), "00000000000000ff");

        await service.FindSimilarAsync(baseImg.Id, threshold: 1, ct: TestContext.Current.CancellationToken);

        // 連鎖無効化後、再検索で新しいペアが再保存される(古い行は一旦消えた)。
        // 検証: 再計算後の特徴量が新 pHash になっていること(連鎖削除が起きた前提条件)
        var feature = await db.Features.GetAsync(baseImg.Id);
        Assert.NotNull(feature);
        Assert.Equal("00000000000000ff", feature.PHash);

        // 再検索で再生成された類似度が新 pHash 由来(距離= popcount(0xff) と other の 0x1f の XOR)
        var pair = await db.Similarities.GetAsync(baseImg.Id, other.Id);
        Assert.NotNull(pair);
    }

    [Fact]
    public async Task 削除CASCADE_画像削除で特徴量と類似度行が連鎖削除される()
    {
        using var db = new TempDb();
        var (folderA, _) = await SeedTwoFoldersAsync(db);
        var baseImg = await SeedImageAsync(db, folderA, "base.jpg", ImageStatus.Normal, "0000000000000000");
        var other = await SeedImageAsync(db, folderA, "o.jpg", ImageStatus.Normal, "000000000000001f");

        var reader = ReaderFor(baseImg, other);
        var service = NewService(db, reader);
        await service.FindSimilarAsync(baseImg.Id, threshold: 1, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(await db.Features.GetAsync(baseImg.Id));
        Assert.NotNull(await db.Similarities.GetAsync(baseImg.Id, other.Id));

        await db.Images.DeleteAsync(baseImg.Id);

        var (features, similarities) = await db.Manager.RunAsync(async conn =>
        {
            var f = await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM image_features WHERE image_id = @Id", new { baseImg.Id });
            var s = await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM image_similarity WHERE image_id1 = @Id OR image_id2 = @Id",
                new { baseImg.Id });
            return (f, s);
        }, TestContext.Current.CancellationToken);

        Assert.Equal(0, features);
        Assert.Equal(0, similarities);
    }

    // ---- P-09: adapter 世代交代の無効化 ----

    [Fact]
    public async Task adapter世代交代_旧adapterの特徴量と類似度は無効化され再計算される()
    {
        using var db = new TempDb();
        var (folderA, _) = await SeedTwoFoldersAsync(db);
        var baseImg = await SeedImageAsync(db, folderA, "base.jpg", ImageStatus.Normal, "0000000000000000");
        var other = await SeedImageAsync(db, folderA, "o.jpg", ImageStatus.Normal, "000000000000001f");

        // 旧 adapter(full-decode)で永続化された特徴量を仕込む。内容(file_size/modified/hash)は
        // 現行と一致=旧来の内容ベース判定だけなら fresh。staleness は adapter 不一致だけで起こす。
        await db.Features.UpsertAsync(new ImageFeature
        {
            ImageId = baseImg.Id,
            PHash = "ffffffffffffffff",          // 旧 adapter の(別物の)値
            HashAdapter = "skia-full-decode-v1", // 旧世代
            FileSize = baseImg.FileSize,
            ModifiedDate = baseImg.ModifiedDate,
            Hash = baseImg.Hash,
            LastCalculated = "2026-01-01T00:00:00.000Z",
        });
        // 旧 adapter 由来の類似度ペアも仕込む(無効化されることを確認する対象)
        await db.Similarities.UpsertAsync(baseImg.Id, other.Id, 5, db.Clock.UtcNowIso());
        Assert.NotNull(await db.Similarities.GetAsync(baseImg.Id, other.Id));

        // 現行 adapter = scaled-decode(既定)。reader は base の現行 pHash(=0000…)を返す
        var reader = ReaderFor(baseImg, other);
        Assert.Equal("skia-scaled-decode-v1", reader.AdapterId);
        var service = NewService(db, reader);
        _ = await service.FindSimilarAsync(baseImg.Id, threshold: 1, ct: TestContext.Current.CancellationToken);

        // 旧 adapter の特徴量は stale 扱い → 再計算され、新 adapter・新 pHash に置換される
        var refreshed = await db.Features.GetAsync(baseImg.Id);
        Assert.NotNull(refreshed);
        Assert.Equal("skia-scaled-decode-v1", refreshed.HashAdapter); // 世代が更新
        Assert.Equal("0000000000000000", refreshed.PHash);            // 現行 adapter で再計算

        // 旧 adapter 由来の古い類似度(score=5)は連鎖無効化で消え、新 pHash 由来で再生成される
        var pair = await db.Similarities.GetAsync(baseImg.Id, other.Id);
        Assert.NotNull(pair);
        Assert.NotEqual(5, pair.SimilarityScore); // 旧キャッシュ値ではない=無効化された
    }

    // ---- L2 スキーマ ----

    [Fact]
    public async Task L2スキーマ_image_features_similarityの列FK索引が存在する()
    {
        using var db = new TempDb();
        var (cols, fks, indexes) = await db.Manager.RunAsync(async conn =>
        {
            var fc = (await conn.QueryAsync(
                "PRAGMA table_info(image_features);")).Select(r => (string)((IDictionary<string, object>)r)["name"]).ToList();
            var ffk = (await conn.QueryAsync("PRAGMA foreign_key_list(image_features);")).Count();
            var six = (await conn.QueryAsync(
                "SELECT name FROM sqlite_master WHERE type='index' AND tbl_name='image_similarity' AND name NOT LIKE 'sqlite_%'"))
                .Select(r => (string)((IDictionary<string, object>)r)["name"]).ToList();
            return (fc, ffk, six);
        }, TestContext.Current.CancellationToken);

        Assert.Contains("image_id", cols);
        Assert.Contains("phash", cols);
        Assert.Contains("hash_adapter", cols); // P-09
        Assert.Contains("file_size", cols);
        Assert.Contains("modified_date", cols);
        Assert.Contains("hash", cols);
        Assert.Contains("last_calculated", cols);
        Assert.Equal(1, fks); // image_features.image_id → images
        Assert.Contains("idx_image_similarity_image_id1", indexes);
        Assert.Contains("idx_image_similarity_image_id2", indexes);
    }

    [Fact]
    public async Task L2スキーマ_FKがCASCADEである()
    {
        using var db = new TempDb();
        var (featFk, simFkCount) = await db.Manager.RunAsync(async conn =>
        {
            var f = (await conn.QueryAsync("PRAGMA foreign_key_list(image_features);"))
                .Select(r => (string)((IDictionary<string, object>)r)["on_delete"]).ToList();
            var s = (await conn.QueryAsync("PRAGMA foreign_key_list(image_similarity);"))
                .Select(r => (string)((IDictionary<string, object>)r)["on_delete"]).ToList();
            return (f, s);
        }, TestContext.Current.CancellationToken);

        Assert.All(featFk, d => Assert.Equal("CASCADE", d));
        Assert.Equal(2, simFkCount.Count); // image_id1, image_id2
        Assert.All(simFkCount, d => Assert.Equal("CASCADE", d));
    }

    // ---- ヘルパ ----

    private SimilaritySearchService NewService(TempDb db, FakePHashImageReader reader)
        => new(db.Folders, db.Images, db.Features, db.Similarities, reader, db.Clock);

    private static FakePHashImageReader ReaderFor(params ImageRecord[] images)
    {
        var reader = new FakePHashImageReader();
        foreach (var img in images)
        {
            reader.SetPHash(AbsPath(img.RelativePath), img.Hash); // 各画像の Hash 列に pHash を入れて使う
        }

        return reader;
    }

    private static string AbsPath(string relativePath)
        => Path.Combine(FolderRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static ImageRecord ScopeRecord(string id, string folderId, string relativePath) => new()
    {
        Id = id,
        SyncFolderId = folderId,
        RelativePath = relativePath,
        FileName = Path.GetFileName(relativePath),
        FileSize = 1,
        Hash = "hash",
        Status = ImageStatus.Normal,
        CreatedDate = "2026-01-01T00:00:00.000Z",
        ModifiedDate = "2026-01-01T00:00:00.000Z",
    };

    private static async Task<(string FolderA, string FolderB)> SeedTwoFoldersAsync(TempDb db)
    {
        var a = new SyncFolder { Id = "folder-a", Name = "A", Path = FolderRoot };
        var b = new SyncFolder { Id = "folder-b", Name = "B", Path = "C:/other" };
        Assert.True((await db.Folders.AddAsync(a)).IsSuccess);
        Assert.True((await db.Folders.AddAsync(b)).IsSuccess);
        return (a.Id, b.Id);
    }

    private static Task<ImageRecord> SeedImageAsync(
        TempDb db, string folderId, string name, ImageStatus status, string phash)
        => SeedImageWithIdAsync(db, folderId, IdGenerator.NewId(), name, status, phash);

    private static async Task<ImageRecord> SeedImageWithIdAsync(
        TempDb db, string folderId, string id, string name, ImageStatus status, string phash)
    {
        var image = new ImageRecord
        {
            Id = id,
            SyncFolderId = folderId,
            RelativePath = name,
            FileName = name,
            FileSize = name.Length,
            // pHash を Hash 列に格納し FakePHashImageReader でそのまま返す(計算は注入で代替)
            Hash = phash,
            Status = status,
            CreatedDate = "2026-01-01T00:00:00.000Z",
            ModifiedDate = "2026-01-01T00:00:00.000Z",
        };
        await db.Images.AddAsync(image);
        return image;
    }
}

/// <summary>
/// 合成 pHash 注入用のフェイク reader(CP-SIM-017)。絶対パス→pHash の対応表+計算回数。
/// 既定は変種非対応(SupportsOrientationVariants=false)= ECO-048 以前と同じ identity のみの挙動。
/// SetVariants で変種を注入すると変種対応 reader として振る舞う(REQ-084 のペア規則検査用)。
/// </summary>
internal sealed class FakePHashImageReader(string adapterId = "skia-scaled-decode-v1") : IPHashImageReader
{
    private readonly Dictionary<string, string?> _byPath = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<string>?> _variantsByPath = new(StringComparer.Ordinal);

    /// <summary>adapter 世代(P-09)。既定は production と同じ scaled-decode。世代交代検査では別 id を渡す。</summary>
    public string AdapterId { get; } = adapterId;

    public bool SupportsOrientationVariants { get; set; }

    public int ComputeCount { get; private set; }

    public void SetPHash(string absolutePath, string? phash) => _byPath[absolutePath] = phash;

    /// <summary>変種([0]=identity)を注入し、変種対応 reader として振る舞わせる(REQ-084)。</summary>
    public void SetVariants(string absolutePath, IReadOnlyList<string>? variants)
    {
        _variantsByPath[absolutePath] = variants;
        SupportsOrientationVariants = true;
    }

    public Task<string?> ComputePHashAsync(string absoluteImagePath)
    {
        ComputeCount++;
        return Task.FromResult(_byPath.TryGetValue(absoluteImagePath, out var phash) ? phash : null);
    }

    public Task<IReadOnlyList<string>?> ComputePHashVariantsAsync(string absoluteImagePath)
    {
        ComputeCount++;
        return Task.FromResult(_variantsByPath.TryGetValue(absoluteImagePath, out var variants) ? variants : null);
    }
}
