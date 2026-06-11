using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>CP-SORT-003: 整列器が field×direction・安定性・OrdinalIgnoreCase を満たす(OC-4)。</summary>
[Trait("cp", "CP-SORT-003")]
public sealed class CpSort003Tests
{
    private static readonly ImageSorter Sorter = new();

    private static ImageRecord Rec(
        string id,
        string name = "x.jpg",
        long size = 0,
        string created = "2026-01-01T00:00:00.000Z",
        string modified = "2026-01-01T00:00:00.000Z")
        => new()
        {
            Id = id,
            SyncFolderId = "folder-1",
            RelativePath = name,
            FileName = name,
            FileSize = size,
            Hash = new string('0', 64),
            CreatedDate = created,
            ModifiedDate = modified,
        };

    private static string[] Ids(IReadOnlyList<ImageRecord> sorted) => sorted.Select(i => i.Id).ToArray();

    [Fact]
    public void Name昇順_大文字小文字を無視する()
    {
        var sorted = Sorter.Sort(
            [Rec("i2", "b.jpg"), Rec("i1", "A.jpg")],
            SortField.Name, SortDirection.Asc);
        Assert.Equal(["i1", "i2"], Ids(sorted)); // A.jpg, b.jpg
    }

    [Fact]
    public void FileSize降順()
    {
        var sorted = Sorter.Sort(
            [Rec("s1", size: 100), Rec("s2", size: 300), Rec("s3", size: 200)],
            SortField.FileSize, SortDirection.Desc);
        Assert.Equal(["s2", "s3", "s1"], Ids(sorted));
    }

    [Fact]
    public void CreatedDate昇順_ISO8601の序数比較()
    {
        var sorted = Sorter.Sort(
            [
                Rec("c1", created: "2026-03-01T00:00:00.000Z"),
                Rec("c2", created: "2025-12-31T23:59:59.999Z"),
                Rec("c3", created: "2026-01-15T12:00:00.000Z"),
            ],
            SortField.CreatedDate, SortDirection.Asc);
        Assert.Equal(["c2", "c3", "c1"], Ids(sorted));
    }

    [Fact]
    public void ModifiedDate降順()
    {
        var sorted = Sorter.Sort(
            [
                Rec("m1", modified: "2026-01-01T00:00:00.000Z"),
                Rec("m2", modified: "2026-06-01T00:00:00.000Z"),
                Rec("m3", modified: "2026-03-01T00:00:00.000Z"),
            ],
            SortField.ModifiedDate, SortDirection.Desc);
        Assert.Equal(["m2", "m3", "m1"], Ids(sorted));
    }

    [Fact]
    public void 同名は昇順でも降順でもid昇順()
    {
        ImageRecord[] images = [Rec("b", "same.jpg"), Rec("c", "same.jpg"), Rec("a", "same.jpg")];

        var asc = Sorter.Sort(images, SortField.Name, SortDirection.Asc);
        Assert.Equal(["a", "b", "c"], Ids(asc));

        // 二次キーは方向に依らず id 昇順(REQ-038)
        var desc = Sorter.Sort(images, SortField.Name, SortDirection.Desc);
        Assert.Equal(["a", "b", "c"], Ids(desc));
    }

    [Fact]
    public void 同一入力2回で同一出力_安定()
    {
        ImageRecord[] images =
        [
            Rec("x3", "dup.jpg", size: 5),
            Rec("x1", "dup.jpg", size: 5),
            Rec("x2", "dup.jpg", size: 5),
        ];

        var first = Ids(Sorter.Sort(images, SortField.FileSize, SortDirection.Asc));
        var second = Ids(Sorter.Sort(images, SortField.FileSize, SortDirection.Asc));
        Assert.Equal(first, second);
        Assert.Equal(["x1", "x2", "x3"], first);
    }

    [Fact]
    public void 空集合と1件は例外なし()
    {
        Assert.Empty(Sorter.Sort([], SortField.Name, SortDirection.Asc));

        var single = Sorter.Sort([Rec("only")], SortField.FileSize, SortDirection.Desc);
        Assert.Equal(["only"], Ids(single));
    }
}
