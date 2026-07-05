using Dapper;
using SkiaSharp;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services.Similarity;
using ViewPrism2.Infrastructure.Imaging;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// CP-SIM-017 拡張(ECO-048 / REQ-084): 回転・鏡像された同一ソース画像の検出。
/// production reader(実 decode 経路)+一時ファイル PNG で E2E 検査する。
/// 回転・鏡像の生成はテスト側の独立実装(製品コードの変換に依存しない交差検証)。
/// </summary>
[Trait("cp", "CP-SIM-017")]
public sealed class CpSim048OrientationTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "ViewPrism2.Tests", Guid.NewGuid().ToString("D"));

    public CpSim048OrientationTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public async Task 対照_同一内容の複製は実decode経路でscore100検出される()
    {
        using var db = new TempDb();
        using (var bmp = CreateStructured())
        {
            Save(bmp, "base.png");
        }

        File.Copy(Path.Combine(_dir, "base.png"), Path.Combine(_dir, "copy.png"));

        var (baseImg, other) = await SeedPairAsync(db, "base.png", "copy.png");
        var results = await NewService(db).FindSimilarAsync(
            baseImg.Id, threshold: 70, ct: TestContext.Current.CancellationToken);

        Assert.Contains(results, r => r.ImageId == other.Id && r.Score == 100);
    }

    [Fact]
    public async Task 回転90度の同一ソース画像が既定閾値70で検出される()
    {
        using var db = new TempDb();
        using (var bmp = CreateStructured())
        {
            Save(bmp, "base.png");
            using var rotated = Rotate90Cw(bmp);
            Save(rotated, "rot90.png");
        }

        var (baseImg, other) = await SeedPairAsync(db, "base.png", "rot90.png");
        var results = await NewService(db).FindSimilarAsync(
            baseImg.Id, threshold: 70, ct: TestContext.Current.CancellationToken);

        Assert.Contains(results, r => r.ImageId == other.Id && r.Score >= 70);
    }

    [Fact]
    public async Task 鏡像_左右反転の同一ソース画像が既定閾値70で検出される()
    {
        using var db = new TempDb();
        using (var bmp = CreateStructured())
        {
            Save(bmp, "base.png");
            using var mirrored = FlipHorizontal(bmp);
            Save(mirrored, "mirror.png");
        }

        var (baseImg, other) = await SeedPairAsync(db, "base.png", "mirror.png");
        var results = await NewService(db).FindSimilarAsync(
            baseImg.Id, threshold: 70, ct: TestContext.Current.CancellationToken);

        Assert.Contains(results, r => r.ImageId == other.Id && r.Score >= 70);
    }

    [Fact]
    public async Task 対称性_探索方向によらずペアスコアが一致する()
    {
        // ペア規則(REQ-084): 小 id の identity × 大 id の全変種の最小 — 役割が id 順で決まるため
        // A→B と B→A で同一スコアになる(ペア正規化キャッシュと整合)。別 DB で両方向を実測して突合。
        var scoreAtoB = await SearchScoreAsync(searchFromBase: true);
        var scoreBtoA = await SearchScoreAsync(searchFromBase: false);
        Assert.Equal(scoreAtoB, scoreBtoA);
        Assert.True(scoreAtoB >= 70); // 回転ペアが検出されている前提の上での対称性
    }

    [Fact]
    public async Task 変種対応readerでは変種欠落の特徴量がstale扱いで再計算される()
    {
        using var db = new TempDb();
        var folder = new SyncFolder { Id = "folder-v", Name = "v", Path = "C:/pics-v" };
        Assert.True((await db.Folders.AddAsync(folder)).IsSuccess);

        var baseImg = await SeedFakeImageAsync(db, folder.Id, "id-base", "base.jpg");
        var other = await SeedFakeImageAsync(db, folder.Id, "id-other", "o.jpg");

        // 変種なしの旧特徴量(内容・adapter は現行一致=ECO-048 以前なら fresh)を仕込む
        await db.Features.UpsertAsync(new ImageFeature
        {
            ImageId = baseImg.Id,
            PHash = "0000000000000000",
            HashAdapter = "skia-scaled-decode-v1",
            FileSize = baseImg.FileSize,
            ModifiedDate = baseImg.ModifiedDate,
            Hash = baseImg.Hash,
            LastCalculated = "2026-01-01T00:00:00.000Z",
        });

        var reader = new FakePHashImageReader();
        reader.SetVariants(FakeAbsPath(folder.Path, "base.jpg"), IdentityVariants("0000000000000000"));
        reader.SetVariants(FakeAbsPath(folder.Path, "o.jpg"), IdentityVariants("0000000000000000"));

        var service = new SimilaritySearchService(
            db.Folders, db.Images, db.Features, db.Similarities, reader, db.Clock);
        _ = await service.FindSimilarAsync(baseImg.Id, threshold: 50, ct: TestContext.Current.CancellationToken);

        // 変種欠落の旧レコードは stale=再計算され、変種つきで置き換わる(REQ-084 自動アップグレード)
        Assert.True(reader.ComputeCount >= 1);
        var refreshed = await db.Features.GetAsync(baseImg.Id);
        Assert.NotNull(refreshed);
        Assert.NotNull(refreshed.PhashVariants);
        Assert.Equal(8, refreshed.PhashVariants.Split(',').Length);
    }

    [Fact]
    public async Task 後方互換_変種なし特徴量とはidentity同士の距離のみで比較される()
    {
        using var db = new TempDb();
        var folder = new SyncFolder { Id = "folder-l", Name = "l", Path = "C:/pics-l" };
        Assert.True((await db.Folders.AddAsync(folder)).IsSuccess);

        // 大きい id 側(ペア規則で変種側)に変種なしの旧特徴量。identity 距離は遠い(40bit)が、
        // 仮に変種があれば距離 0 になるハッシュ。変種が無いので identity 距離のみ=検出されないのが正
        var baseImg = await SeedFakeImageAsync(db, folder.Id, "id-aaa", "a.jpg");
        var legacy = await SeedFakeImageAsync(db, folder.Id, "id-zzz", "z.jpg");

        var reader = new FakePHashImageReader(); // 変種非対応(既定)= 旧レコードも fresh のまま
        foreach (var (img, phash) in new[] { (baseImg, "0000000000000000"), (legacy, "000000ffffffffff") })
        {
            await db.Features.UpsertAsync(new ImageFeature
            {
                ImageId = img.Id,
                PHash = phash,
                HashAdapter = "skia-scaled-decode-v1",
                FileSize = img.FileSize,
                ModifiedDate = img.ModifiedDate,
                Hash = img.Hash,
                LastCalculated = "2026-01-01T00:00:00.000Z",
            });
        }

        var service = new SimilaritySearchService(
            db.Folders, db.Images, db.Features, db.Similarities, reader, db.Clock);
        var results = await service.FindSimilarAsync(
            baseImg.Id, threshold: 70, ct: TestContext.Current.CancellationToken);

        Assert.Empty(results); // 距離 40 → 類似度 6 <70(変種なし=identity のみ・例外なし)
        Assert.Equal(0, reader.ComputeCount); // 変種非対応 reader の下では旧レコードは fresh のまま
    }

    [Fact]
    public async Task L2スキーマ_image_featuresにphash_variants列が存在する()
    {
        using var db = new TempDb();
        var cols = await db.Manager.RunAsync(async conn =>
            (await conn.QueryAsync("PRAGMA table_info(image_features);"))
                .Select(r => (string)((IDictionary<string, object>)r)["name"]).ToList(),
            TestContext.Current.CancellationToken);
        Assert.Contains("phash_variants", cols); // migration 006(ECO-048/REQ-084)
    }

    // ---- ヘルパ ----

    /// <summary>回転ペア(base/rot90)を作り、指定方向から検索したときの相手のスコアを返す。</summary>
    private async Task<int> SearchScoreAsync(bool searchFromBase)
    {
        using var db = new TempDb();
        var sub = Path.Combine(_dir, searchFromBase ? "ab" : "ba");
        Directory.CreateDirectory(sub);
        using (var bmp = CreateStructured())
        {
            SaveTo(bmp, Path.Combine(sub, "base.png"));
            using var rotated = Rotate90Cw(bmp);
            SaveTo(rotated, Path.Combine(sub, "rot90.png"));
        }

        var folder = new SyncFolder { Id = "folder-sym", Name = "sym", Path = sub };
        Assert.True((await db.Folders.AddAsync(folder)).IsSuccess);
        var baseImg = await SeedImageAsync(db, folder.Id, "base.png");
        var rot = await SeedImageAsync(db, folder.Id, "rot90.png");

        var (from, to) = searchFromBase ? (baseImg, rot) : (rot, baseImg);
        var results = await NewService(db).FindSimilarAsync(
            from.Id, threshold: 50, ct: TestContext.Current.CancellationToken);
        var hit = results.SingleOrDefault(r => r.ImageId == to.Id);
        Assert.NotNull(hit);
        return hit.Score;
    }

    private static string FakeAbsPath(string folderPath, string relativePath)
        => Path.Combine(folderPath, relativePath.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>identity のみ意味を持つ 8 変種(残り 7 は identity の複製)— 変種形式の最小注入。</summary>
    private static IReadOnlyList<string> IdentityVariants(string identity)
        => Enumerable.Repeat(identity, 8).ToList();

    private static async Task<ImageRecord> SeedFakeImageAsync(
        TempDb db, string folderId, string id, string name)
    {
        var image = new ImageRecord
        {
            Id = id,
            SyncFolderId = folderId,
            RelativePath = name,
            FileName = name,
            FileSize = name.Length,
            Hash = "hash-" + id,
            Status = ImageStatus.Normal,
            CreatedDate = "2026-01-01T00:00:00.000Z",
            ModifiedDate = "2026-01-01T00:00:00.000Z",
        };
        await db.Images.AddAsync(image);
        return image;
    }

    private SimilaritySearchService NewService(TempDb db)
        => new(db.Folders, db.Images, db.Features, db.Similarities,
            new PHashImageReaderScaledDecode(), db.Clock);

    /// <summary>非対称な構造画像(水平勾配+左上の明矩形+下端の暗帯)。回転・鏡像で pHash が大きく動く。</summary>
    private static SKBitmap CreateStructured()
    {
        const int size = 128;
        var bmp = new SKBitmap(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var v = (byte)(x * 2 % 256);
                if (x < 48 && y < 24)
                {
                    v = 255; // 左上の明矩形
                }
                else if (y > 104)
                {
                    v = (byte)(v / 4); // 下端の暗帯
                }

                bmp.SetPixel(x, y, new SKColor(v, v, v));
            }
        }

        return bmp;
    }

    /// <summary>時計回り 90° 回転(独立実装): dst(H-1-y, x) = src(x, y)。</summary>
    private static SKBitmap Rotate90Cw(SKBitmap src)
    {
        var dst = new SKBitmap(new SKImageInfo(
            src.Height, src.Width, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        for (var y = 0; y < src.Height; y++)
        {
            for (var x = 0; x < src.Width; x++)
            {
                dst.SetPixel(src.Height - 1 - y, x, src.GetPixel(x, y));
            }
        }

        return dst;
    }

    /// <summary>左右反転(独立実装): dst(W-1-x, y) = src(x, y)。</summary>
    private static SKBitmap FlipHorizontal(SKBitmap src)
    {
        var dst = new SKBitmap(new SKImageInfo(
            src.Width, src.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        for (var y = 0; y < src.Height; y++)
        {
            for (var x = 0; x < src.Width; x++)
            {
                dst.SetPixel(src.Width - 1 - x, y, src.GetPixel(x, y));
            }
        }

        return dst;
    }

    private void Save(SKBitmap bmp, string name) => SaveTo(bmp, Path.Combine(_dir, name));

    private static void SaveTo(SKBitmap bmp, string fullPath)
    {
        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var fs = File.Create(fullPath);
        data.SaveTo(fs);
    }

    private async Task<(ImageRecord Base, ImageRecord Other)> SeedPairAsync(
        TempDb db, string baseName, string otherName)
    {
        var folder = new SyncFolder { Id = "folder-orient", Name = "orient", Path = _dir };
        Assert.True((await db.Folders.AddAsync(folder)).IsSuccess);

        var baseImg = await SeedImageAsync(db, folder.Id, baseName);
        var other = await SeedImageAsync(db, folder.Id, otherName);
        return (baseImg, other);
    }

    private static async Task<ImageRecord> SeedImageAsync(TempDb db, string folderId, string name)
    {
        var image = new ImageRecord
        {
            Id = "img-" + name,
            SyncFolderId = folderId,
            RelativePath = name,
            FileName = name,
            FileSize = 1,
            Hash = "hash-" + name, // pHash は実ファイルから計算(合成注入しない)
            Status = ImageStatus.Normal,
            CreatedDate = "2026-01-01T00:00:00.000Z",
            ModifiedDate = "2026-01-01T00:00:00.000Z",
        };
        await db.Images.AddAsync(image);
        return image;
    }
}
