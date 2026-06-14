using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services.Repair;
using Xunit;

namespace ViewPrism2.Oracle;

/// <summary>
/// S-27: criteria 条件検索の横断契約(scope=cross-factory・OC-19、仕様 §2.11.1)。工場非開示。
/// 指定条件のみ AND・対象 status は statusTargets 限定・安定順(relative_path 昇順→id 昇順)・
/// **全条件未指定なら空列**(全件を返さない)。純粋関数 CriteriaMatcher.Match を直接呼ぶ。
/// </summary>
[Trait("oracle", "S-27")]
[Trait("scope", "cross-factory")]
public sealed class S27CriteriaSearchTests
{
    private static readonly IReadOnlySet<ImageStatus> NormalOnly = new HashSet<ImageStatus> { ImageStatus.Normal };
    private static readonly IReadOnlySet<ImageStatus> PendingNormal =
        new HashSet<ImageStatus> { ImageStatus.Pending, ImageStatus.Normal };

    private static ImageRecord Img(string id, string name, string hash, long size, string mtime, ImageStatus status)
        => new()
        {
            Id = id,
            SyncFolderId = "fld",
            RelativePath = name,
            FileName = name,
            FileSize = size,
            Hash = hash,
            Status = status,
            CreatedDate = "2026-01-01T00:00:00.000Z",
            ModifiedDate = mtime,
        };

    // alpha/beta=png(beta は大文字 PNG=case-insensitive 検査)/ photo.jpg / photo-pending.png(Pending)/ deleted / missing
    private static List<ImageRecord> Sample() =>
    [
        Img("n1", "alpha.png", "h-aaaa", 100, "2026-02-01T00:00:00.000Z", ImageStatus.Normal),
        Img("n2", "beta.PNG", "h-bbbb", 500, "2026-03-01T00:00:00.000Z", ImageStatus.Normal),
        Img("n3", "photo.jpg", "h-cccc", 300, "2026-02-15T00:00:00.000Z", ImageStatus.Normal),
        Img("p1", "photo-pending.png", "h-dddd", 200, "2026-02-10T00:00:00.000Z", ImageStatus.Pending),
        Img("d1", "alpha-del.png", "h-eeee", 100, "2026-02-01T00:00:00.000Z", ImageStatus.Deleted),
        Img("m1", "beta-missing.png", "h-ffff", 500, "2026-03-01T00:00:00.000Z", ImageStatus.Missing),
    ];

    [Fact]
    public void 拡張子のみ_status限定_他条件無視_case_insensitive()
    {
        // .png かつ Normal: alpha.png(n1)・beta.PNG(n2)。jpg(n3)・Pending(p1)・deleted/missing は除外。relative_path 昇順
        var r = CriteriaMatcher.Match(Sample(), new SearchCriteria { Extension = ".png" }, NormalOnly);
        Assert.Equal(["n1", "n2"], r);
    }

    [Fact]
    public void 複数条件はAND_ORにならない()
    {
        // NameContains "photo" + Extension ".png" の AND(PendingNormal): photo.jpg は jpg で除外 → photo-pending.png のみ
        var r = CriteriaMatcher.Match(
            Sample(), new SearchCriteria { NameContains = "photo", Extension = ".png" }, PendingNormal);
        Assert.Equal(["p1"], r);
    }

    [Fact]
    public void status対象で母集合が変わる()
    {
        // "photo": NormalOnly → photo.jpg(n3) のみ / PendingNormal → photo-pending.png(p1)・photo.jpg(n3)
        // 安定順: "photo-pending.png" < "photo.jpg"('-'(0x2D) < '.'(0x2E))
        Assert.Equal(["n3"], CriteriaMatcher.Match(Sample(), new SearchCriteria { NameContains = "photo" }, NormalOnly));
        Assert.Equal(["p1", "n3"], CriteriaMatcher.Match(Sample(), new SearchCriteria { NameContains = "photo" }, PendingNormal));
    }

    [Fact]
    public void mtime範囲_ISO序数比較()
    {
        // 2026-02-10〜2026-02-28 の Normal: photo.jpg(02-15)のみ(alpha=02-01 下回り・beta=03-01 上回り)
        var r = CriteriaMatcher.Match(
            Sample(),
            new SearchCriteria { MtimeFrom = "2026-02-10T00:00:00.000Z", MtimeTo = "2026-02-28T00:00:00.000Z" },
            NormalOnly);
        Assert.Equal(["n3"], r);
    }

    [Fact]
    public void hash完全一致()
    {
        var r = CriteriaMatcher.Match(Sample(), new SearchCriteria { Hash = "h-cccc" }, NormalOnly);
        Assert.Equal(["n3"], r);
    }

    [Fact]
    public void 空条件は非実行_全件を返さない()
    {
        Assert.Empty(CriteriaMatcher.Match(Sample(), new SearchCriteria(), NormalOnly));
    }
}
