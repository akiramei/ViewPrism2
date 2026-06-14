using System.Security.Cryptography;
using SkiaSharp;
using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services.Repair;
using ViewPrism2.Infrastructure.Imaging;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// CP-TRASH-001(L3 物理差分): トラッシュ復元・完全削除の物理非破壊 — 操作前後でユーザー画像
/// ファイルが不変(INV-009 第 2 の実アクション・INV-013/014、仕様 §2.11.3-4)。
/// 一時ディレクトリに実画像 + 一時 SQLite。操作前後でフォルダ内全ファイルの SHA-256・mtime・
/// ファイル集合をスナップショットして完全一致を検査する。存在プローブは実 File.Exists(FilePresenceProbe)を使う。
/// </summary>
[Trait("cp", "CP-TRASH-001")]
public sealed class CpTrash001Tests : IDisposable
{
    private readonly string _imageDir = Path.Combine(
        Path.GetTempPath(), "ViewPrism2.Tests.TrashL3", Guid.NewGuid().ToString("D"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_imageDir))
            {
                Directory.Delete(_imageDir, recursive: true);
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
    public async Task 完全削除前後で実画像ファイルのSHA256とmtimeとファイル集合が不変()
    {
        using var db = new TempDb();
        var folder = new SyncFolder { Id = "folder-1", Name = "F", Path = _imageDir };
        Assert.True((await db.Folders.AddAsync(folder)).IsSuccess);

        // 実画像ファイルを作成(完全削除対象 + 無関係 1 枚)
        var deletedPath = Path.Combine(_imageDir, "deleted.jpg");
        var keepPath = Path.Combine(_imageDir, "keep.png");
        ImageFixtures.WriteEncoded(deletedPath, 32, 32, SKEncodedImageFormat.Jpeg);
        ImageFixtures.WriteEncoded(keepPath, 16, 16, SKEncodedImageFormat.Png);

        var deleted = await SeedImageAsync(db, folder.Id, "deleted.jpg", ImageStatus.Deleted);
        await SeedImageAsync(db, folder.Id, "keep.png", ImageStatus.Normal);

        // タグを付けておき、完全削除で CASCADE 消滅=論理操作が起きることを保証する
        var tag = new Tag { Id = "tag-1", Name = "T", Type = TagType.Simple };
        await db.Tags.AddAsync(tag);
        await db.Tags.UpsertImageTagAsync(new ImageTag { ImageId = deleted.Id, TagId = tag.Id });

        var before = Snapshot(_imageDir);

        var service = new TrashService(db.Images, db.Folders, new FilePresenceProbe());
        var result = await service.PermanentDeleteAsync(deleted.Id);
        Assert.True(result.IsSuccess);

        var after = Snapshot(_imageDir);

        // 物理非破壊: ファイル集合・SHA-256・mtime が完全一致(削除対象の物理ファイルも残る)
        AssertSnapshotsEqual(before, after);

        // DB 上は論理操作が起きている(空振りでない): images 行と image_tags が消滅
        Assert.Null(await db.Images.GetByIdAsync(deleted.Id));
        Assert.Empty(await db.Tags.GetImageTagsAsync(deleted.Id));
    }

    [Fact]
    public async Task 復元_物理存在前後でフォルダ内ファイル集合が同一_読み取り存在確認のみ()
    {
        using var db = new TempDb();
        var folder = new SyncFolder { Id = "folder-1", Name = "F", Path = _imageDir };
        Assert.True((await db.Folders.AddAsync(folder)).IsSuccess);

        var path = Path.Combine(_imageDir, "img.jpg");
        ImageFixtures.WriteEncoded(path, 24, 24, SKEncodedImageFormat.Jpeg);
        var image = await SeedImageAsync(db, folder.Id, "img.jpg", ImageStatus.Deleted);

        var before = Snapshot(_imageDir);

        var service = new TrashService(db.Images, db.Folders, new FilePresenceProbe());
        var result = await service.RestoreAsync(image.Id);
        Assert.True(result.IsSuccess);
        Assert.Equal(ImageStatus.Normal, result.Value); // 物理存在 → Normal(T6)

        var after = Snapshot(_imageDir);
        AssertSnapshotsEqual(before, after);

        // DB 上は status 遷移している(空振りでない)
        Assert.Equal(ImageStatus.Normal, (await db.Images.GetByIdAsync(image.Id))!.Status);
    }

    [Fact]
    public async Task 復元_物理不在でもフォルダ内ファイル集合が同一_missing化のみ()
    {
        using var db = new TempDb();
        var folder = new SyncFolder { Id = "folder-1", Name = "F", Path = _imageDir };
        Assert.True((await db.Folders.AddAsync(folder)).IsSuccess);

        // 無関係ファイルを 1 枚置くが、対象 "ghost.jpg" は作らない(物理不在)
        ImageFixtures.WriteEncoded(Path.Combine(_imageDir, "other.jpg"), 16, 16, SKEncodedImageFormat.Jpeg);
        var image = await SeedImageAsync(db, folder.Id, "ghost.jpg", ImageStatus.Deleted);

        var before = Snapshot(_imageDir);

        var service = new TrashService(db.Images, db.Folders, new FilePresenceProbe());
        var result = await service.RestoreAsync(image.Id);
        Assert.True(result.IsSuccess);
        Assert.Equal(ImageStatus.Missing, result.Value); // 物理不在 → Missing(T7)

        var after = Snapshot(_imageDir);
        // 不在ファイルを新規作成していない(集合不変)
        AssertSnapshotsEqual(before, after);

        Assert.Equal(ImageStatus.Missing, (await db.Images.GetByIdAsync(image.Id))!.Status);
    }

    private static void AssertSnapshotsEqual(
        Dictionary<string, (string Sha256, DateTime LastWriteUtc)> before,
        Dictionary<string, (string Sha256, DateTime LastWriteUtc)> after)
    {
        Assert.Equal(before.Keys.OrderBy(k => k, StringComparer.Ordinal),
            after.Keys.OrderBy(k => k, StringComparer.Ordinal));
        foreach (var (path, beforeInfo) in before)
        {
            Assert.True(after.TryGetValue(path, out var afterInfo), $"ファイルが消失: {path}");
            Assert.Equal(beforeInfo.Sha256, afterInfo.Sha256);
            Assert.Equal(beforeInfo.LastWriteUtc, afterInfo.LastWriteUtc);
        }
    }

    private static Dictionary<string, (string Sha256, DateTime LastWriteUtc)> Snapshot(string dir)
    {
        var snapshot = new Dictionary<string, (string, DateTime)>(StringComparer.Ordinal);
        foreach (var path in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
        {
            using var stream = File.OpenRead(path);
            var hash = Convert.ToHexStringLower(SHA256.HashData(stream));
            snapshot[Path.GetFileName(path)] = (hash, File.GetLastWriteTimeUtc(path));
        }

        return snapshot;
    }

    private static async Task<ImageRecord> SeedImageAsync(
        TempDb db, string folderId, string name, ImageStatus status)
    {
        var image = new ImageRecord
        {
            Id = IdGenerator.NewId(),
            SyncFolderId = folderId,
            RelativePath = name,
            FileName = name,
            FileSize = 10,
            Hash = new string('a', 64),
            Status = status,
            CreatedDate = "2026-01-01T00:00:00.000Z",
            ModifiedDate = "2026-01-01T00:00:00.000Z",
        };
        await db.Images.AddAsync(image);
        return image;
    }
}
