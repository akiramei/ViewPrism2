using System.Text;
using System.Text.RegularExpressions;
using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Infrastructure.Scanning;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// ECO-137: 再スキャンの missing 判定は、ディレクトリ列挙とは別の DB 行ごとの
/// File.Exists を行わない。存在確認呼び出しを数える seam で走査回数を固定し、
/// 存在/不在・大小文字差・スキャン候補列挙外の既存ファイルの判定 bit を pin する。
/// </summary>
[Trait("cp", "CP-SCAN-004")]
public sealed class CpScanFilePresenceTests : IDisposable
{
    private readonly TempDb _db = new();
    private readonly string _root;

    public CpScanFilePresenceTests()
    {
        _root = Path.Combine(
            Path.GetTempPath(), "ViewPrism2.Tests", Guid.NewGuid().ToString("D"), "files");
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
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public async Task 再スキャンのmissing判定は行ごとのFileExistsを呼ばない()
    {
        WriteFile("present-a.jpg", "a");
        WriteFile("present-b.jpg", "b");
        var folder = await RegisterFolderAsync();
        await SeedRowAsync(folder.Id, "present-a.jpg", "img-present-a");
        await SeedRowAsync(folder.Id, "present-b.jpg", "img-present-b");
        await SeedRowAsync(folder.Id, "absent-a.jpg", "img-absent-a");
        await SeedRowAsync(folder.Id, "absent-b.jpg", "img-absent-b", ImageStatus.Pending);

        var calls = 0;
        bool CountedFileExists(string path)
        {
            calls++;
            return File.Exists(path);
        }

        var scan = new ScanService(_db.Folders, _db.Images, _db.Clock, CountedFileExists);
        var staged = await scan.StageAsync(folder.Id, null, TestContext.Current.CancellationToken);

        Assert.True(staged.IsSuccess, staged.Message);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task missing判定は存在不在_大小文字差_候補列挙外でも不変()
    {
        WriteFile("present.jpg", "present");
        WriteFile("CASE.JPG", "case");
        WriteFile("excluded.jpg", "excluded");
        WriteFile("unsupported.dat", "unsupported");
        var hiddenPath = WriteFile("hidden.jpg", "hidden");
        File.SetAttributes(hiddenPath, File.GetAttributes(hiddenPath) | FileAttributes.Hidden);
        var folder = await RegisterFolderAsync(["excluded.jpg"]);
        await SeedRowAsync(folder.Id, "present.jpg", "img-present");
        await SeedRowAsync(folder.Id, "missing.jpg", "img-missing");
        await SeedRowAsync(folder.Id, "case.jpg", "img-case");
        await SeedRowAsync(folder.Id, "excluded.jpg", "img-excluded");
        await SeedRowAsync(folder.Id, "unsupported.dat", "img-unsupported");
        await SeedRowAsync(folder.Id, "hidden.jpg", "img-hidden");

        var scan = new ScanService(_db.Folders, _db.Images, _db.Clock);
        var staged = await scan.StageAsync(folder.Id, null, TestContext.Current.CancellationToken);

        Assert.True(staged.IsSuccess, staged.Message);
        Assert.Equal(
            ["img-missing"],
            staged.Value!.StatusUpdates
                .Where(update => update.Status == ImageStatus.Missing)
                .Select(update => update.Id)
                .OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public async Task サブフォルダ対象外へ変更後も配下の既存行は存在すればmissingにしない()
    {
        WriteFile("legacy/present.jpg", "present");
        var folder = new SyncFolder
        {
            Id = IdGenerator.NewId(),
            Name = "non-recursive-fixture",
            Path = _root,
            IncludeSubfolders = false,
            LastScan = "2026-01-01T00:00:00.000Z",
        };
        var added = await _db.Folders.AddAsync(folder);
        Assert.True(added.IsSuccess, added.Message);
        await SeedRowAsync(folder.Id, "legacy/present.jpg", "img-legacy-present");
        await SeedRowAsync(folder.Id, "legacy/absent.jpg", "img-legacy-absent");

        var scan = new ScanService(_db.Folders, _db.Images, _db.Clock);
        var staged = await scan.StageAsync(folder.Id, null, TestContext.Current.CancellationToken);

        Assert.True(staged.IsSuccess, staged.Message);
        Assert.Equal(
            ["img-legacy-absent"],
            staged.Value!.StatusUpdates
                .Where(update => update.Status == ImageStatus.Missing)
                .Select(update => update.Id));
        Assert.Empty(staged.Value.Adds); // 対象外サブフォルダを新規候補として走査しない
    }

    [Fact]
    public async Task 初回ScanCoreはexistingゼロのため行ごとのFileExistsもゼロ件()
    {
        WriteFile("initial.jpg", "initial");
        var folder = new SyncFolder
        {
            Id = IdGenerator.NewId(),
            Name = "initial-fixture",
            Path = _root,
        };
        var added = await _db.Folders.AddAsync(folder);
        Assert.True(added.IsSuccess, added.Message);

        var calls = 0;
        bool CountedFileExists(string path)
        {
            calls++;
            return File.Exists(path);
        }

        var scan = new ScanService(_db.Folders, _db.Images, _db.Clock, CountedFileExists);
        var result = await scan.ScanAsync(folder.Id, null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(0, calls);
    }

    [Fact]
    public void 非再帰の既存親prefix構築もキャンセルに到達する()
    {
        var existing = new[]
        {
            NewRow("folder/deep/image.jpg", "img-cancel"),
        };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => ScanService.BuildPresenceDirectories(existing, cts.Token));
    }

    [Fact]
    public void ScanServiceはFileExistsを直接呼ばず計数seamへ集約する()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "ViewPrism2.Infrastructure", "Scanning", "ScanService.cs"));
        var directCalls = Regex.Matches(source, @"\bFile\.Exists\s*\(");

        Assert.Empty(directCalls.Cast<Match>());
    }

    private string WriteFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, Encoding.UTF8.GetBytes(content));
        return fullPath;
    }

    private async Task<SyncFolder> RegisterFolderAsync(IReadOnlyList<string>? excludePatterns = null)
    {
        var folder = new SyncFolder
        {
            Id = IdGenerator.NewId(),
            Name = "fixture",
            Path = _root,
            LastScan = "2026-01-01T00:00:00.000Z",
            ExcludePatterns = excludePatterns ?? [],
        };
        var result = await _db.Folders.AddAsync(folder);
        Assert.True(result.IsSuccess, result.Message);
        return folder;
    }

    private async Task SeedRowAsync(
        string folderId,
        string relativePath,
        string id,
        ImageStatus status = ImageStatus.Normal)
    {
        await _db.Images.AddAsync(NewRow(relativePath, id, folderId, status));
    }

    private static ImageRecord NewRow(
        string relativePath,
        string id,
        string folderId = "folder-fixture",
        ImageStatus status = ImageStatus.Normal)
        => new()
        {
            Id = id,
            SyncFolderId = folderId,
            RelativePath = relativePath,
            FileName = relativePath[(relativePath.LastIndexOf('/') + 1)..],
            FileSize = 1,
            Hash = HashOf(relativePath),
            Status = status,
            CreatedDate = "2026-01-01T00:00:00.000Z",
            ModifiedDate = "2026-01-01T00:00:00.000Z",
        };

    private static string HashOf(string value)
        => Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string RepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ViewPrism2.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("ViewPrism2.sln が出力パスから見つからない");
    }
}
