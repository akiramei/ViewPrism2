using System.Security.Cryptography;
using SkiaSharp;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services.Similarity;
using ViewPrism2.Infrastructure.Database;
using Xunit;

namespace ViewPrism2.Oracle;

/// <summary>
/// S-24: マージの物理非破壊(L3 物理差分・CP-MERGE-001・INV-009、EQ-003)。工場非開示。
/// 実画像ファイルを一時フォルダに置き、マージ前後でフォルダのスナップショット
/// (各ファイルの SHA-256・LastWriteTimeUtc・ファイル集合)が完全一致することを実証する。
/// 一方 DB 上はマージ元が Deleted・タグ集約済み(論理操作は起きている=空振りでない)。
/// </summary>
[Trait("oracle", "S-24")]
public sealed class S24MergePhysicalDiffTests
{
    [Fact]
    public async Task マージ前後で物理ファイルは不変かつDBは論理統合される()
    {
        using var db = new OracleDb();
        var imageDir = Path.Combine(Path.GetTempPath(), "ViewPrism2.Oracle.S24", Guid.NewGuid().ToString("D"));
        Directory.CreateDirectory(imageDir);
        try
        {
            // 実画像 3 枚を一時フォルダへ(色を変えてバイト列=ハッシュを変える)
            OracleImages.WriteEncoded(Path.Combine(imageDir, "t.png"), 16, 16, SKEncodedImageFormat.Png, SKColors.Red);
            OracleImages.WriteEncoded(Path.Combine(imageDir, "a.png"), 16, 16, SKEncodedImageFormat.Png, SKColors.Green);
            OracleImages.WriteEncoded(Path.Combine(imageDir, "b.png"), 16, 16, SKEncodedImageFormat.Png, SKColors.Blue);

            var folder = new SyncFolder { Id = "fld", Name = "c", Path = imageDir };
            Assert.True((await db.Folders.AddAsync(folder)).IsSuccess);
            await AddNormalAsync(db, "img-t", folder.Id, "t.png");
            await AddNormalAsync(db, "img-a", folder.Id, "a.png");
            await AddNormalAsync(db, "img-b", folder.Id, "b.png");
            await db.Tags.AddAsync(new Tag { Id = "t", Name = "Color", Type = TagType.Textual });
            await db.Tags.UpsertImageTagAsync(new ImageTag { ImageId = "img-a", TagId = "t", Value = "緑" });

            var before = Snapshot(imageDir);

            var merge = new MergeService(db.Images, db.Tags, new MergeRepository(db.Manager));
            Assert.True((await merge.MergeAsync("img-t", ["img-a"])).IsSuccess);

            var after = Snapshot(imageDir);

            // 物理: ファイル集合・各 SHA-256・mtime が完全一致(削除・移動・新規・改変なし)
            Assert.Equal(before.Keys.Order(StringComparer.Ordinal), after.Keys.Order(StringComparer.Ordinal));
            foreach (var (name, sig) in before)
            {
                Assert.Equal(sig, after[name]);
            }

            // DB: 論理統合は起きている(空振りでない)
            Assert.Equal(ImageStatus.Deleted, (await db.Images.GetByIdAsync("img-a"))!.Status);
            Assert.Contains(await db.Tags.GetImageTagsAsync("img-t"), t => t.TagId == "t" && t.Value == "緑");
        }
        finally
        {
            try
            {
                Directory.Delete(imageDir, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>フォルダ内全ファイルの (SHA-256, LastWriteTimeUtc.Ticks) を採取する。</summary>
    private static Dictionary<string, (string Sha, long Mtime)> Snapshot(string dir)
    {
        var snapshot = new Dictionary<string, (string, long)>(StringComparer.Ordinal);
        foreach (var path in Directory.GetFiles(dir))
        {
            var sha = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
            snapshot[Path.GetFileName(path)] = (sha, File.GetLastWriteTimeUtc(path).Ticks);
        }

        return snapshot;
    }

    private static async Task AddNormalAsync(OracleDb db, string id, string folderId, string relativePath)
        => await db.Images.AddAsync(new ImageRecord
        {
            Id = id,
            SyncFolderId = folderId,
            RelativePath = relativePath,
            FileName = relativePath,
            FileSize = 100,
            Hash = new string('0', 64),
            Status = ImageStatus.Normal,
            CreatedDate = "2026-01-01T00:00:00.000Z",
            ModifiedDate = "2026-01-01T00:00:00.000Z",
        });
}
