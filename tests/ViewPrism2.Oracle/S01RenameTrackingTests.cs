using SkiaSharp;
using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Infrastructure.Scanning;
using Xunit;

namespace ViewPrism2.Oracle;

/// <summary>
/// S-01: リネーム追跡 E2E(spec §2.1 規則 3a・遷移表・REQ-017、EQ-001)。
/// フォルダ登録 → 画像 3 枚スキャン → 1 枚にタグ 2 種+ノート → ファイル名変更 → 再スキャン →
/// 候補確認 → 再リンク確定。
/// </summary>
[Trait("oracle", "S-01")]
public sealed class S01RenameTrackingTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "ViewPrism2.Oracle", "s01-" + Guid.NewGuid().ToString("D"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
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
    public async Task リネーム追跡E2E_missing_pending_再リンクで関連保持()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = new OracleDb();

        // --- フォルダ登録+実ファイル 3 枚 ---
        var picturesDir = Path.Combine(_root, "pictures");
        OracleImages.WriteEncoded(Path.Combine(picturesDir, "a.png"), 64, 48, SKEncodedImageFormat.Png, new SKColor(0xDC, 0x26, 0x26));
        OracleImages.WriteEncoded(Path.Combine(picturesDir, "b.png"), 64, 48, SKEncodedImageFormat.Png, new SKColor(0x16, 0xA3, 0x4A));
        OracleImages.WriteEncoded(Path.Combine(picturesDir, "c.png"), 64, 48, SKEncodedImageFormat.Png, new SKColor(0x25, 0x63, 0xEB));

        var folder = new SyncFolder
        {
            Id = IdGenerator.NewId(),
            Name = "pictures",
            Path = picturesDir,
        };
        var registered = await db.Folders.AddAsync(folder);
        Assert.True(registered.IsSuccess);

        var scanner = new ScanService(db.Folders, db.Images, db.Clock);

        // --- 初回スキャン: 3 枚とも normal で登録(規則 3b) ---
        var first = await scanner.ScanAsync(folder.Id, progress: null, ct);
        Assert.True(first.IsSuccess);
        Assert.Equal(3, first.Value!.Added);

        var afterFirst = await db.Images.GetByFolderAsync(folder.Id);
        Assert.Equal(3, afterFirst.Count);
        Assert.All(afterFirst, i => Assert.Equal(ImageStatus.Normal, i.Status));

        var target = Assert.Single(afterFirst, i => i.RelativePath == "a.png");

        // EQ-001: ID は pattern 検査・日時は形式検査
        Assert.Matches(OraclePatterns.UuidV4, target.Id);
        Assert.Matches(OraclePatterns.IsoUtc, target.CreatedDate);
        Assert.Matches(OraclePatterns.IsoUtc, target.ModifiedDate);

        // --- タグ 2 種(simple+textual)+ノート ---
        var simpleTag = new Tag { Id = IdGenerator.NewId(), Name = "marked", Type = TagType.Simple };
        var textualTag = new Tag { Id = IdGenerator.NewId(), Name = "color", Type = TagType.Textual };
        await db.Tags.AddAsync(simpleTag);
        await db.Tags.AddAsync(textualTag);
        await db.Tags.UpsertImageTagAsync(new ImageTag { ImageId = target.Id, TagId = simpleTag.Id, Value = null });
        await db.Tags.UpsertImageTagAsync(new ImageTag { ImageId = target.Id, TagId = textualTag.Id, Value = "red" });
        await db.Images.UpdateNotesAsync(target.Id, "oracle note");

        // --- ファイル名変更 → 再スキャン ---
        File.Move(Path.Combine(picturesDir, "a.png"), Path.Combine(picturesDir, "renamed.png"));

        var second = await scanner.ScanAsync(folder.Id, progress: null, ct);
        Assert.True(second.IsSuccess);
        Assert.Equal(0, second.Value!.Added);
        Assert.Equal(1, second.Value.Missing);
        Assert.Equal(1, second.Value.Pending);
        Assert.Equal(2, second.Value.Skipped);

        // 再スキャン後: 旧行=missing+新行=pending(candidate=旧行 id)
        var afterSecond = await db.Images.GetByFolderAsync(folder.Id);
        Assert.Equal(4, afterSecond.Count);

        var oldRow = Assert.Single(afterSecond, i => i.Id == target.Id);
        Assert.Equal(ImageStatus.Missing, oldRow.Status);
        Assert.Equal("a.png", oldRow.RelativePath);

        var pendingRow = Assert.Single(afterSecond, i => i.Status == ImageStatus.Pending);
        Assert.Equal("renamed.png", pendingRow.RelativePath);
        Assert.Equal(target.Id, pendingRow.CandidateLinkId);
        Assert.Equal(target.Hash, pendingRow.Hash);
        Assert.Matches(OraclePatterns.UuidV4, pendingRow.Id);

        // --- 候補確認(REQ-017: 同一フォルダ・同ハッシュの pending を relative_path 昇順) ---
        var relink = new RelinkService(db.Images);
        var candidates = await relink.GetCandidatesAsync(target.Id);
        var candidate = Assert.Single(candidates);
        Assert.Equal(pendingRow.Id, candidate.ImageId);
        Assert.Equal("renamed.png", candidate.RelativePath);
        Assert.Matches(OraclePatterns.IsoUtc, candidate.ModifiedDate);

        // --- 再リンク確定 ---
        var committed = await relink.CommitRelinkAsync(target.Id, pendingRow.Id);
        Assert.True(committed.IsSuccess);

        // 確定後: 旧 image_id のまま relative_path 更新・status=normal・candidate_link_id=NULL
        var relinked = await db.Images.GetByIdAsync(target.Id);
        Assert.NotNull(relinked);
        Assert.Equal(target.Id, relinked.Id); // INV-001: image_id 不変
        Assert.Equal(ImageStatus.Normal, relinked.Status);
        Assert.Equal("renamed.png", relinked.RelativePath);
        Assert.Equal("renamed.png", relinked.FileName);
        Assert.Null(relinked.CandidateLinkId);
        Assert.Equal(pendingRow.Hash, relinked.Hash);

        // pending 行消滅
        Assert.Null(await db.Images.GetByIdAsync(pendingRow.Id));
        var finalRows = await db.Images.GetByFolderAsync(folder.Id);
        Assert.Equal(3, finalRows.Count);
        Assert.All(finalRows, i => Assert.Equal(ImageStatus.Normal, i.Status));

        // タグ 2 種とノート残存(集合は整列比較、EQ-001)
        var tags = await db.Tags.GetImageTagsAsync(target.Id);
        Assert.Equal(
            new[] { simpleTag.Id, textualTag.Id }.Order(StringComparer.Ordinal),
            tags.Select(t => t.TagId).Order(StringComparer.Ordinal));
        Assert.Null(Assert.Single(tags, t => t.TagId == simpleTag.Id).Value);
        Assert.Equal("red", Assert.Single(tags, t => t.TagId == textualTag.Id).Value);
        Assert.Equal("oracle note", relinked.Notes);
    }
}
