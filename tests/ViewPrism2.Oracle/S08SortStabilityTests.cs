using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using Xunit;

namespace ViewPrism2.Oracle;

/// <summary>
/// S-08: 整列安定性(spec §2.3 REQ-038、EQ-001)。
/// 同名(大文字小文字違い)・同サイズの画像 4 枚を name asc → file_size desc → name asc の順で 3 回整列。
/// 各整列で同値グループ内は id 昇順。3 回目の出力は 1 回目と完全一致。
/// </summary>
[Trait("oracle", "S-08")]
public sealed class S08SortStabilityTests
{
    private static ImageRecord NewImage(string id, string fileName) => new()
    {
        Id = id,
        SyncFolderId = "folder-1",
        RelativePath = fileName,
        FileName = fileName,
        FileSize = 4096, // 全件同サイズ
        Hash = new string('a', 64),
        Status = ImageStatus.Normal,
        CreatedDate = "2026-01-01T00:00:00.000Z",
        ModifiedDate = "2026-01-01T00:00:00.000Z",
    };

    [Fact]
    public void 同値グループはid昇順で3回目の出力は1回目と完全一致()
    {
        // 同名(大文字小文字違い)= OrdinalIgnoreCase で全件同値(REQ-038)
        var images = new[]
        {
            NewImage("11111111-aaaa-4aaa-8aaa-000000000003", "PHOTO.png"),
            NewImage("11111111-aaaa-4aaa-8aaa-000000000001", "photo.PNG"),
            NewImage("11111111-aaaa-4aaa-8aaa-000000000004", "Photo.Png"),
            NewImage("11111111-aaaa-4aaa-8aaa-000000000002", "photo.png"),
        };
        string[] expectedIdAscending =
        [
            "11111111-aaaa-4aaa-8aaa-000000000001",
            "11111111-aaaa-4aaa-8aaa-000000000002",
            "11111111-aaaa-4aaa-8aaa-000000000003",
            "11111111-aaaa-4aaa-8aaa-000000000004",
        ];

        var sorter = new ImageSorter();

        // 1 回目: name asc
        var first = sorter.Sort(images, SortField.Name, SortDirection.Asc).Select(i => i.Id).ToList();
        Assert.Equal(expectedIdAscending, first);

        // 2 回目: file_size desc(同値時も二次キーは id 昇順)
        var second = sorter.Sort(images, SortField.FileSize, SortDirection.Desc).Select(i => i.Id).ToList();
        Assert.Equal(expectedIdAscending, second);

        // 3 回目: name asc → 1 回目と完全一致
        var third = sorter.Sort(images, SortField.Name, SortDirection.Asc).Select(i => i.Id).ToList();
        Assert.Equal(first, third);
    }
}
