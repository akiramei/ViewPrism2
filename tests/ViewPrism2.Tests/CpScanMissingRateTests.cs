using System.Text;
using ViewPrism2.App.Services;
using ViewPrism2.App.ViewModels;
using ViewPrism2.Core.Models;
using ViewPrism2.Infrastructure.Scanning;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// ECO-136: スキャン結果サマリーの missing 率カード/tier は「今回 missing 化した delta」ではなく
/// 「適用後の総 missing 数(既存 missing − 再出現 + 今回 missing 化)」を分子にしなければならない。
/// 旧実装は分子に MissingTotal(delta)を使い、既存 missing 行が残ると率/tier を過小表示した
/// (空フォルダなのに 100% にならない=混入 ECO-130)。分母 ManagedTotal は変わらず総管理数。
/// 遷移サマリー行(normal→missing の delta)は不変で、率/tier の分子だけを是正する。
/// </summary>
[Trait("cp", "CP-SCAN-004")]
public sealed class CpScanMissingRateTests : IDisposable
{
    private readonly TempDb _db = new();
    private readonly string _root;

    public CpScanMissingRateTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ViewPrism2.Tests", Guid.NewGuid().ToString("D"), "files");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        _db.Dispose();
        try
        {
            var parent = Path.GetDirectoryName(_root)!;
            if (Directory.Exists(parent))
            {
                Directory.Delete(parent, recursive: true);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // --- 主プローブ: VM の率/tier が総 missing を分子にする(既存 missing 混在で delta と乖離) ---

    [Fact]
    public void 率カードとtierは適用後の総missingを分子にする()
    {
        // 管理 10 = 今回 normal→missing 3(delta 30%=Yellow)+ 既存 missing 5 → 適用後総 missing 8(80%=Red)
        var staging = SyntheticStaging(missingFromNormal: 3, preexistingMissing: 5, reappeared: 0, managed: 10);
        Assert.Equal(3, staging.MissingTotal);            // delta(遷移行用)は不変
        Assert.Equal(8, staging.TotalMissingAfterApply);  // 率の分子

        var folder = new SyncFolder { Id = "folder-1", Name = "fixture", Path = @"C:\Photos" };
        var vm = new ScanSummaryViewModel(new ScanCoordinator(null!), TestLoc.Ja(), new NullWindows(), folder);
        vm.PresentSummary(staging);

        // delta(3/10=30%)なら Yellow だが、総 missing(8/10=80%)は Red でなければならない
        Assert.True(vm.IsRateRed);
        Assert.False(vm.IsRateYellow);
        // 率カードの表示件数・割合も総 missing 基準(旧: "3 / 10 件(30.0%)")
        Assert.Contains("8 / 10", vm.RateValueText);
        Assert.Contains("80.0", vm.RateValueText);
    }

    [Fact]
    public void 既存missingなしなら率は従来どおりdeltaと一致する()
    {
        // 後方互換: PreexistingMissing=0・Reappeared=0 のとき TotalMissingAfterApply == MissingTotal
        var staging = SyntheticStaging(missingFromNormal: 4, preexistingMissing: 0, reappeared: 0, managed: 10);
        Assert.Equal(staging.MissingTotal, staging.TotalMissingAfterApply);
    }

    [Fact]
    public void 再出現分は適用後の総missingから差し引かれる()
    {
        // 既存 missing 5 のうち 2 件が再出現(missing→pending)+ 今回 normal→missing 3
        // → 適用後総 missing = 5 − 2 + 3 = 6(re-appear した 2 件は missing でなくなる)
        var staging = SyntheticStaging(missingFromNormal: 3, preexistingMissing: 5, reappeared: 2, managed: 10);
        Assert.Equal(6, staging.TotalMissingAfterApply);
        Assert.Equal(3, staging.MissingTotal); // delta は再出現の影響を受けない
    }

    // --- 副プローブ: 実 StageAsync が PreexistingMissing を正しく採取し、空フォルダで総 missing=管理数 ---

    [Fact]
    public async Task 空フォルダの再スキャンは適用後総missingが管理総数に一致する()
    {
        var folder = new SyncFolder
        {
            Id = "folder-mr-1",
            Name = "fixture",
            Path = _root,
            LastScan = "2026-01-01T00:00:00.000Z", // 再スキャン文脈
        };
        Assert.True((await _db.Folders.AddAsync(folder)).IsSuccess);

        // 3 件 normal(ファイル不在→今回 missing 化)+ 2 件は既に missing(据え置き)。フォルダは空(走査 0)。
        await SeedRowAsync(folder.Id, "a.jpg", ImageStatus.Normal, "n-a");
        await SeedRowAsync(folder.Id, "b.jpg", ImageStatus.Normal, "n-b");
        await SeedRowAsync(folder.Id, "c.jpg", ImageStatus.Normal, "n-c");
        await SeedRowAsync(folder.Id, "x.jpg", ImageStatus.Missing, "m-x");
        await SeedRowAsync(folder.Id, "y.jpg", ImageStatus.Missing, "m-y");

        var scan = new ScanService(_db.Folders, _db.Images, _db.Clock);
        var result = await scan.StageAsync(folder.Id, null, CancellationToken.None);
        Assert.True(result.IsSuccess);
        var s = result.Value!;

        Assert.Equal(0, s.ScannedFiles);
        Assert.Equal(5, s.ManagedTotal);
        Assert.Equal(3, s.MissingFromNormal);   // delta(遷移行)
        Assert.Equal(2, s.PreexistingMissing);  // 既存 missing
        Assert.Equal(0, s.Reappeared);
        Assert.Equal(5, s.TotalMissingAfterApply); // 空フォルダ=全 5 件が見つからない=100%
        Assert.Equal(s.ManagedTotal, s.TotalMissingAfterApply);
    }

    private Task SeedRowAsync(string folderId, string relativePath, ImageStatus status, string id)
        => _db.Images.AddAsync(new ImageRecord
        {
            Id = id,
            SyncFolderId = folderId,
            RelativePath = relativePath,
            FileName = relativePath,
            FileSize = 1,
            Hash = new string('0', 64),
            Status = status,
            CreatedDate = "2026-01-01T00:00:00.000Z",
            ModifiedDate = "2026-01-01T00:00:00.000Z",
        });

    private static ScanStaging SyntheticStaging(
        int missingFromNormal, int preexistingMissing, int reappeared, int managed) => new()
        {
            FolderId = "folder-1",
            ManagedTotal = managed,
            ScannedFiles = 0,
            Unchanged = 0,
            ContentChanged = 0,
            AddedPending = 0,
            Reappeared = reappeared,
            MissingFromNormal = missingFromNormal,
            MissingFromPending = 0,
            DeletedUnchanged = 0,
            DeletedMetaRefreshed = 0,
            PendedWithoutMeta = 0,
            ReadFailures = 0,
            PreexistingMissing = preexistingMissing,
            Adds = [],
            MetaUpdates = [],
            StatusUpdates = [],
            Deletes = [],
            Examples = [],
        };

    private sealed class NullWindows : IWindowService
    {
        public Task<bool> ConfirmAsync(string title, string message, string confirmLabel,
            bool destructive = false, string? cancelLabel = null) => Task.FromResult(true);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
        public Task ShowFolderManagementAsync() => Task.CompletedTask;
        public Task ShowSettingsAsync() => Task.CompletedTask;
        public Task ShowSnapshotsAsync() => Task.CompletedTask;
        public Task<bool> ShowTagEditorAsync(Tag? existing) => Task.FromResult(false);
        public Task<bool> ShowViewEditDialogAsync(View? existing) => Task.FromResult(false);
        public Task<IReadOnlyList<string>?> ShowNumericValueDialogAsync(
            Tag tag, NumericTagSettings? settings, int imageCount)
            => Task.FromResult<IReadOnlyList<string>?>(null);
        public Task<NodeConditionResult?> ShowNodeConditionDialogAsync(
            Tag tag, HierarchyConditionType? conditionType, string? conditionValueJson)
            => Task.FromResult<NodeConditionResult?>(null);
        public Task ShowRelinkAsync(string folderId) => Task.CompletedTask;
        public void ShowViewer(IReadOnlyList<ImageEntry> ordered, int startIndex) { }
    }
}
