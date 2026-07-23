using ViewPrism2.Core.Models;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// ECO-138: 二段階スキャンの worst-case staging 保持形状は、件数 N に対して
/// O(N × 有界行サイズ) でなければならない。I/O/SQLite/GC タイミングを除外し、
/// StageCore ピークを構成する existing/adds/presentPaths と同形状の合成データを
/// current thread の allocation で測る。
/// </summary>
[Trait("cp", "CP-SCAN-004")]
public sealed class CpScanStagingMemoryTests
{
    private const int SmallRowCount = 4_096;
    private const int LargeRowCount = SmallRowCount * 2;
    private const double MinDoublingRatio = 1.75;
    private const double MaxDoublingRatio = 2.25;
    private const double MaxAllocatedBytesPerRow = 2_048;

    [Fact]
    public void staging構築allocationは件数比例かつ行サイズ上限内()
    {
        _ = MeasureAllocatedBytes(64); // JIT・静的初期化を測定区間から外す。

        var smallBytes = MeasureAllocatedBytes(SmallRowCount);
        var largeBytes = MeasureAllocatedBytes(LargeRowCount);
        var doublingRatio = (double)largeBytes / smallBytes;
        var allocatedBytesPerRow = (double)largeBytes / LargeRowCount;

        Assert.InRange(doublingRatio, MinDoublingRatio, MaxDoublingRatio);
        Assert.True(
            allocatedBytesPerRow <= MaxAllocatedBytesPerRow,
            $"allocated={allocatedBytesPerRow:F1} B/row exceeds {MaxAllocatedBytesPerRow:F0} B/row");
    }

    private static long MeasureAllocatedBytes(int rowCount)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        var existing = new List<ImageRecord>(rowCount);
        var adds = new List<ImageRecord>(rowCount);
        var presentPaths = new HashSet<string>(rowCount, StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < rowCount; i++)
        {
            var suffix = i.ToString("D8", System.Globalization.CultureInfo.InvariantCulture);
            var existingPath = $"old/{suffix}.jpg";
            var addedPath = $"new/{suffix}.jpg";
            var hash = $"{suffix}{new string('a', 56)}";

            existing.Add(NewRecord($"existing-{suffix}", existingPath, hash, ImageStatus.Missing));
            adds.Add(NewRecord($"added-{suffix}", addedPath, hash, ImageStatus.Pending));
            presentPaths.Add(addedPath);
        }

        var after = GC.GetAllocatedBytesForCurrentThread();
        GC.KeepAlive(existing);
        GC.KeepAlive(adds);
        GC.KeepAlive(presentPaths);
        return after - before;
    }

    private static ImageRecord NewRecord(
        string id,
        string relativePath,
        string hash,
        ImageStatus status) => new()
    {
        Id = id,
        SyncFolderId = "folder-1",
        RelativePath = relativePath,
        FileName = $"{id}.jpg",
        FileSize = 1,
        Hash = hash,
        Status = status,
        CreatedDate = "2026-01-01T00:00:00.000Z",
        ModifiedDate = "2026-01-01T00:00:00.000Z",
    };
}
