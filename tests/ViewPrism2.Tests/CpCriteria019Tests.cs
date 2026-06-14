using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services.Repair;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// CP-CRITERIA-019(M-CRITERIA-024 / OC-19): criteria 条件検索が指定条件のみ AND・
/// status 用途別・安定順・空条件非実行で絞り込む(仕様 §2.11.1)。
/// CriteriaMatcher は純粋関数のため固定 ImageRecord 集合で exact 検査し、
/// CriteriaSearchService は TempDb 経由で 1 本(コレクション境界+写像)を検査する。
/// </summary>
[Trait("cp", "CP-CRITERIA-019")]
public sealed class CpCriteria019Tests
{
    private static readonly IReadOnlySet<ImageStatus> NormalOnly = new HashSet<ImageStatus> { ImageStatus.Normal };

    private static readonly IReadOnlySet<ImageStatus> PendingOrNormal =
        new HashSet<ImageStatus> { ImageStatus.Pending, ImageStatus.Normal };

    private static ImageRecord Image(
        string id,
        string relativePath,
        string fileName,
        ImageStatus status = ImageStatus.Normal,
        string hash = "h",
        long fileSize = 100,
        string modifiedDate = "2026-01-01T00:00:00.000Z") => new()
        {
            Id = id,
            SyncFolderId = "folder-1",
            RelativePath = relativePath,
            FileName = fileName,
            FileSize = fileSize,
            Hash = hash,
            Status = status,
            CreatedDate = "2026-01-01T00:00:00.000Z",
            ModifiedDate = modifiedDate,
        };

    [Fact]
    public void 単一条件_拡張子pngのみ指定でpng画像のみ_他条件は無視()
    {
        var images = new[]
        {
            Image("a", "a.png", "a.png"),
            Image("b", "b.jpg", "b.jpg"),
            Image("c", "c.PNG", "c.PNG"),
        };

        var result = CriteriaMatcher.Match(images, new SearchCriteria { Extension = "png" }, NormalOnly);

        // .png(大小無視で .PNG も含む)かつ Normal のみ
        Assert.Equal(["a", "c"], result);
    }

    [Fact]
    public void 拡張子は先頭ドット有無を吸収する()
    {
        var images = new[] { Image("a", "a.png", "a.png"), Image("b", "b.jpg", "b.jpg") };

        var withDot = CriteriaMatcher.Match(images, new SearchCriteria { Extension = ".png" }, NormalOnly);
        var withoutDot = CriteriaMatcher.Match(images, new SearchCriteria { Extension = "png" }, NormalOnly);

        Assert.Equal(["a"], withDot);
        Assert.Equal(["a"], withoutDot);
    }

    [Fact]
    public void 複数条件AND_名前部分一致とサイズ範囲は両方満たす画像のみ_ORにならない()
    {
        var images = new[]
        {
            Image("a", "IMG_001.jpg", "IMG_001.jpg", fileSize: 150),  // 名前◯ サイズ◯
            Image("b", "IMG_002.jpg", "IMG_002.jpg", fileSize: 50),   // 名前◯ サイズ×
            Image("c", "photo.jpg", "photo.jpg", fileSize: 150),      // 名前× サイズ◯
        };

        var result = CriteriaMatcher.Match(
            images,
            new SearchCriteria { NameContains = "img_", SizeMin = 100, SizeMax = 200 },
            NormalOnly);

        Assert.Equal(["a"], result);
    }

    [Fact]
    public void status対象_pendingNormalでdeletedMissingを除外_NormalのみでPending除外()
    {
        var images = new[]
        {
            Image("n", "n.jpg", "n.jpg", ImageStatus.Normal),
            Image("p", "p.jpg", "p.jpg", ImageStatus.Pending),
            Image("m", "m.jpg", "m.jpg", ImageStatus.Missing),
            Image("d", "d.jpg", "d.jpg", ImageStatus.Deleted),
        };

        var criteria = new SearchCriteria { Extension = "jpg" };

        var pendingNormal = CriteriaMatcher.Match(images, criteria, PendingOrNormal);
        Assert.Equal(["n", "p"], pendingNormal);

        var normalOnly = CriteriaMatcher.Match(images, criteria, NormalOnly);
        Assert.Equal(["n"], normalOnly);
    }

    [Fact]
    public void 安定順_relativePath昇順_同値はid昇順()
    {
        var images = new[]
        {
            Image("z", "sub/b.jpg", "b.jpg"),
            Image("a", "sub/a.jpg", "a.jpg"),
            // relative_path 同値("dup.jpg")で id 昇順を確認
            Image("y", "dup.jpg", "dup.jpg"),
            Image("x", "dup.jpg", "dup.jpg"),
        };

        var result = CriteriaMatcher.Match(images, new SearchCriteria { Extension = "jpg" }, NormalOnly);

        Assert.Equal(["dup.jpg", "dup.jpg", "sub/a.jpg", "sub/b.jpg"],
            result.Select(id => images.First(i => i.Id == id).RelativePath));
        // 同値 dup.jpg は id 昇順 x→y
        Assert.Equal(["x", "y", "a", "z"], result);
    }

    [Fact]
    public void 空条件_全項目nullは空列_全件を返さない()
    {
        var images = new[]
        {
            Image("a", "a.jpg", "a.jpg"),
            Image("b", "b.jpg", "b.jpg"),
        };

        var result = CriteriaMatcher.Match(images, new SearchCriteria(), NormalOnly);

        Assert.Empty(result);
    }

    [Fact]
    public void 名前はOrdinalIgnoreCase部分一致_拡張子はcaseInsensitive完全一致_hashはOrdinal完全一致()
    {
        var images = new[]
        {
            Image("a", "Photo_RED.jpg", "Photo_RED.jpg", hash: "abc123"),
            Image("b", "photo_blue.JPG", "photo_blue.JPG", hash: "ABC123"),
        };

        // 名前: 大小無視部分一致("PHOTO" は両方にマッチ)。
        // 安定順は relative_path OrdinalIgnoreCase 昇順 → "photo_blue.JPG"(b) < "Photo_RED.jpg"(a)
        Assert.Equal(["b", "a"], CriteriaMatcher.Match(
            images, new SearchCriteria { NameContains = "PHOTO" }, NormalOnly));

        // 拡張子: case-insensitive 完全一致(.jpg/.JPG 両方)。同じく b→a の安定順
        Assert.Equal(["b", "a"], CriteriaMatcher.Match(
            images, new SearchCriteria { Extension = "JPG" }, NormalOnly));

        // hash: Ordinal 完全一致(大小区別 — "abc123" は a のみ)
        Assert.Equal(["a"], CriteriaMatcher.Match(
            images, new SearchCriteria { Hash = "abc123" }, NormalOnly));
    }

    [Fact]
    public void mtime範囲はISO8601序数比較で境界を含む()
    {
        var images = new[]
        {
            Image("a", "a.jpg", "a.jpg", modifiedDate: "2026-01-01T00:00:00.000Z"),
            Image("b", "b.jpg", "b.jpg", modifiedDate: "2026-06-15T12:00:00.000Z"),
            Image("c", "c.jpg", "c.jpg", modifiedDate: "2026-12-31T23:59:59.999Z"),
        };

        var result = CriteriaMatcher.Match(
            images,
            new SearchCriteria
            {
                MtimeFrom = "2026-06-15T12:00:00.000Z",  // b の境界を含む(以上)
                MtimeTo = "2026-12-31T23:59:59.999Z",    // c の境界を含む(以下)
            },
            NormalOnly);

        Assert.Equal(["b", "c"], result);
    }

    [Fact]
    public async Task SearchAsync_コレクション境界内のみ絞り込み_安定順でレコードを返す()
    {
        using var db = new TempDb();
        await db.Folders.AddAsync(new SyncFolder { Id = "f1", Name = "F1", Path = "C:/f1" });
        await db.Folders.AddAsync(new SyncFolder { Id = "f2", Name = "F2", Path = "C:/f2" });

        await db.Images.AddAsync(Image("a", "a.png", "a.png") with { SyncFolderId = "f1" });
        await db.Images.AddAsync(Image("b", "b.png", "b.png") with { SyncFolderId = "f1" });
        // 別コレクションの .png は対象外
        await db.Images.AddAsync(Image("c", "c.png", "c.png") with { SyncFolderId = "f2" });

        var service = new CriteriaSearchService(db.Images);
        var result = await service.SearchAsync(
            "f1", new SearchCriteria { Extension = "png" }, NormalOnly, TestContext.Current.CancellationToken);

        Assert.Equal(["a", "b"], result.Select(r => r.Id));
        Assert.All(result, r => Assert.Equal("f1", r.SyncFolderId));
    }
}
