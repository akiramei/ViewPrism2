using System.Security.Cryptography;
using SkiaSharp;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services.Repair;
using ViewPrism2.Infrastructure.Imaging;
using Xunit;

namespace ViewPrism2.Oracle;

/// <summary>
/// S-44: 復元・完全削除の物理非破壊(新意味論・S-26 後継)。scope=cross-factory・EQ-003・L3、仕様 §2.11.3-4・ECO-128。工場非開示。
/// INV-009 第 2 の実アクション。実画像を一時フォルダに置き、deleted を復元(物理存在→Pending・origin=Restored・T6')+別の
/// deleted を完全削除→操作前後でフォルダ内ファイル集合/SHA-256/LastWriteTimeUtc が完全不変であることをスナップショット比較で実証する。
/// DB 上は復元で status 遷移(→pending)・完全削除で行消滅(=空振りでない)。
/// </summary>
[Trait("oracle", "S-44")]
[Trait("scope", "cross-factory")]
public sealed class S44TrashPhysicalDiffPendingTests
{
    [Fact]
    public async Task 復元_完全削除の前後で物理ファイルが完全不変_復元はPending遷移()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ViewPrism2.S44", Guid.NewGuid().ToString("D"));
        Directory.CreateDirectory(dir);
        try
        {
            // 実画像 3 枚(色を変えてバイト列を別に)
            OracleImages.WriteEncoded(Path.Combine(dir, "keep.png"), 8, 8, SKEncodedImageFormat.Png, new SKColor(10, 20, 30));
            OracleImages.WriteEncoded(Path.Combine(dir, "restore.png"), 8, 8, SKEncodedImageFormat.Png, new SKColor(40, 50, 60));
            OracleImages.WriteEncoded(Path.Combine(dir, "purge.png"), 8, 8, SKEncodedImageFormat.Png, new SKColor(70, 80, 90));

            using var db = new OracleDb();
            Assert.True((await db.Folders.AddAsync(
                new SyncFolder { Id = "fld", Name = "c", Path = dir })).IsSuccess);
            await AddImageAsync(db, "keep", "keep.png", ImageStatus.Normal);
            await AddImageAsync(db, "restore", "restore.png", ImageStatus.Deleted); // 物理存在 → 復元で Pending(T6')
            await AddImageAsync(db, "purge", "purge.png", ImageStatus.Deleted);     // 完全削除対象

            var before = Snapshot(dir);

            var trash = new TrashService(db.Images, db.Folders, new FilePresenceProbe());
            var r = await trash.RestoreAsync("restore");
            Assert.True(r.IsSuccess);
            Assert.Equal(ImageStatus.Pending, r.Value); // 物理存在するので Pending(T6'・復元だけで normal に戻さない)
            Assert.True((await trash.PermanentDeleteAsync("purge")).IsSuccess);

            var after = Snapshot(dir);

            // EQ-003: 物理ファイル集合・各 SHA-256・mtime が完全不変(削除・移動・新規作成なし)
            Assert.Equal(before.Keys.OrderBy(k => k, StringComparer.Ordinal), after.Keys.OrderBy(k => k, StringComparer.Ordinal));
            foreach (var (name, sig) in before)
            {
                Assert.Equal(sig, after[name]); // (SHA-256, mtime ticks)
            }

            // DB は論理遷移している(空振りでない): restore→Pending(origin=Restored)・purge→行消滅
            var restored = await db.Images.GetByIdAsync("restore");
            Assert.Equal(ImageStatus.Pending, restored!.Status);
            Assert.Equal(PendingOrigin.Restored, restored.PendingOrigin);
            Assert.Null(await db.Images.GetByIdAsync("purge"));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>フォルダ内全ファイルの (SHA-256, LastWriteTimeUtc.Ticks) スナップショット(EQ-003)。</summary>
    private static Dictionary<string, (string Sha, long Ticks)> Snapshot(string dir)
        => Directory.GetFiles(dir).ToDictionary(
            f => Path.GetFileName(f)!,
            f => (Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(f))), new FileInfo(f).LastWriteTimeUtc.Ticks),
            StringComparer.Ordinal);

    private static async Task AddImageAsync(OracleDb db, string id, string rel, ImageStatus status)
        => await db.Images.AddAsync(new ImageRecord
        {
            Id = id,
            SyncFolderId = "fld",
            RelativePath = rel,
            FileName = rel,
            FileSize = 100,
            Hash = new string('0', 64),
            Status = status,
            CreatedDate = "2026-01-01T00:00:00.000Z",
            ModifiedDate = "2026-01-01T00:00:00.000Z",
        });
}
