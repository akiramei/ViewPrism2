using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Infrastructure.Scanning;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// ECO-135: スキャンルートがディスク上で失効(フォルダの移動/改名でパスが実在しない)したとき、
/// スキャンは汎用 IoError ではなく専用の ScanRootMissing を返し、失敗理由を識別できなければならない。
/// 旧実装は StageCore/ScanCore の Directory.Exists=false を一律 IoError で返し、UI では
/// 「ファイルの読み書きに失敗しました」に潰れて改名/権限/一時 I/O が区別できなかった(手動テストの支障)。
/// 二段階(StageAsync)・一段階(ScanAsync)の両経路を pin する。
/// </summary>
[Trait("cp", "CP-SCAN-004")]
public sealed class CpScanRootMissingTests : IDisposable
{
    private readonly TempDb _db = new();
    private readonly string _missingRoot;

    public CpScanRootMissingTests()
    {
        // 一度作って消す=登録は成功し、スキャン時点でルートが実在しない(移動/改名を模す)
        _missingRoot = Path.Combine(
            Path.GetTempPath(), "ViewPrism2.Tests", Guid.NewGuid().ToString("D"), "renamed-away");
        Directory.CreateDirectory(_missingRoot);
    }

    public void Dispose() => _db.Dispose();

    private async Task<SyncFolder> RegisterThenRenameAwayAsync()
    {
        var folder = new SyncFolder
        {
            Id = IdGenerator.NewId(),
            Name = "fixture",
            Path = _missingRoot,
            LastScan = "2026-01-01T00:00:00.000Z", // 過去に成功スキャン済み=再スキャン文脈
        };
        var added = await _db.Folders.AddAsync(folder);
        Assert.True(added.IsSuccess);

        // ディスク上のルートを消す(=フォルダを別名/別所へ移動して DB パスが失効した状態)
        Directory.Delete(_missingRoot, recursive: true);
        Assert.False(Directory.Exists(_missingRoot));
        return folder;
    }

    private ScanService NewScan() => new(_db.Folders, _db.Images, _db.Clock);

    [Fact]
    public async Task 二段階再スキャン_ルート失効は専用コードで識別できる()
    {
        var folder = await RegisterThenRenameAwayAsync();

        var result = await NewScan().StageAsync(folder.Id, null, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.ScanRootMissing, result.Error);
    }

    [Fact]
    public async Task 一段階スキャン_ルート失効は専用コードで識別できる()
    {
        var folder = await RegisterThenRenameAwayAsync();

        var result = await NewScan().ScanAsync(folder.Id, null, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.ScanRootMissing, result.Error);
    }
}
