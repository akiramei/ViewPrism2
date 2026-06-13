using System.Security.Cryptography;
using SkiaSharp;
using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services.Similarity;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// CP-MERGE-001(L3 物理差分): マージの物理非破壊 — 操作前後でユーザー画像ファイルが不変
/// (INV-009 実アクション初適用)。一時ディレクトリに実画像+一時 SQLite。操作前後で
/// フォルダ内全ファイルの SHA-256・mtime・ファイル集合をスナップショットして完全一致を検査する。
/// </summary>
[Trait("cp", "CP-MERGE-001")]
public sealed class CpMerge001Tests : IDisposable
{
    private readonly string _imageDir = Path.Combine(
        Path.GetTempPath(), "ViewPrism2.Tests.MergeL3", Guid.NewGuid().ToString("D"));

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
    public async Task マージ前後で実画像ファイルのSHA256とmtimeとファイル集合が不変()
    {
        using var db = new TempDb();
        var folder = new SyncFolder { Id = "folder-1", Name = "F", Path = _imageDir };
        Assert.True((await db.Folders.AddAsync(folder)).IsSuccess);

        // 実画像ファイルを作成(マージ先・マージ元+無関係 1 枚)
        var targetPath = Path.Combine(_imageDir, "target.jpg");
        var sourcePath = Path.Combine(_imageDir, "source.png");
        var unrelatedPath = Path.Combine(_imageDir, "unrelated.jpg");
        ImageFixtures.WriteEncoded(targetPath, 64, 48, SKEncodedImageFormat.Jpeg);
        ImageFixtures.WriteEncoded(sourcePath, 32, 32, SKEncodedImageFormat.Png);
        ImageFixtures.WriteEncoded(unrelatedPath, 10, 10, SKEncodedImageFormat.Jpeg);

        var target = await SeedImageAsync(db, folder.Id, "target.jpg");
        var source = await SeedImageAsync(db, folder.Id, "source.png");
        await SeedImageAsync(db, folder.Id, "unrelated.jpg");

        // タグを付けて論理操作が実際に起きることを保証する
        var tag = new Tag { Id = "tag-1", Name = "T", Type = TagType.Textual };
        await db.Tags.AddAsync(tag);
        await db.Tags.UpsertImageTagAsync(new ImageTag { ImageId = source.Id, TagId = tag.Id, Value = "v" });

        // ---- 操作前スナップショット(全ファイルのハッシュ・mtime・集合) ----
        var before = Snapshot(_imageDir);

        // ---- マージ実行 ----
        var service = new MergeService(db.Images, db.Tags, db.Merges);
        var result = await service.MergeAsync(target.Id, [source.Id]);
        Assert.True(result.IsSuccess);

        // ---- 操作後スナップショット ----
        var after = Snapshot(_imageDir);

        // 物理非破壊: ファイル集合・各ファイルの SHA-256・mtime が完全一致(移動・削除・新規作成なし)
        Assert.Equal(before.Keys.OrderBy(k => k, StringComparer.Ordinal),
            after.Keys.OrderBy(k => k, StringComparer.Ordinal));
        foreach (var (path, beforeInfo) in before)
        {
            Assert.True(after.TryGetValue(path, out var afterInfo), $"ファイルが消失: {path}");
            Assert.Equal(beforeInfo.Sha256, afterInfo.Sha256);
            Assert.Equal(beforeInfo.LastWriteUtc, afterInfo.LastWriteUtc);
        }

        // DB 上は論理操作が起きている(空振りでない): マージ元 Deleted・タグ集約済み
        var sourceAfter = await db.Images.GetByIdAsync(source.Id);
        Assert.Equal(ImageStatus.Deleted, sourceAfter!.Status);
        var targetTags = await db.Tags.GetImageTagsAsync(target.Id);
        Assert.Contains(targetTags, t => t.TagId == tag.Id && t.Value == "v");
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

    private static async Task<ImageRecord> SeedImageAsync(TempDb db, string folderId, string name)
    {
        var image = new ImageRecord
        {
            Id = IdGenerator.NewId(),
            SyncFolderId = folderId,
            RelativePath = name,
            FileName = name,
            FileSize = 10,
            Hash = new string('a', 64),
            Status = ImageStatus.Normal,
            CreatedDate = "2026-01-01T00:00:00.000Z",
            ModifiedDate = "2026-01-01T00:00:00.000Z",
        };
        await db.Images.AddAsync(image);
        return image;
    }
}
