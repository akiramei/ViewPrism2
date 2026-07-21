using System.Text;
using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services.Repair;
using ViewPrism2.Infrastructure.Scanning;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// CP-SCAN-004 拡張(ECO-129/REQ-101): pending 意味論の再定義。
/// R5 プローブ先行 — ①内容変更= normal→pending('changed') ②再スキャンの新規= pending('new')
/// ③missing 再出現= pending('reappeared') ④pending 消失= missing 保持(行削除しない・タグ保全)
/// ⑤初回スキャンのみ normal(gate① 裁定) ⑥裁定 3 遷移+pending 限定拒否+T14 原子性。
/// </summary>
[Trait("cp", "CP-SCAN-004")]
public sealed class CpPendingSemanticsTests : IDisposable
{
    private readonly TempDb _db = new();
    private readonly string _root;
    private readonly ScanService _scan;
    private readonly PendingReviewService _review;

    public CpPendingSemanticsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ViewPrism2.Tests", Guid.NewGuid().ToString("D"), "files");
        Directory.CreateDirectory(_root);
        _scan = new ScanService(_db.Folders, _db.Images, _db.Clock);
        _review = new PendingReviewService(_db.Images);
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

    // ---- ヘルパ ----

    private string WriteFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, Encoding.UTF8.GetBytes(content));
        return fullPath;
    }

    private async Task<SyncFolder> InitialScanAsync(params (string Path, string Content)[] files)
    {
        foreach (var (path, content) in files)
        {
            WriteFile(path, content);
        }

        var folder = new SyncFolder { Id = IdGenerator.NewId(), Name = "fixture", Path = _root };
        Assert.True((await _db.Folders.AddAsync(folder)).IsSuccess);
        var result = await _scan.ScanAsync(folder.Id, null, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess, result.Message);
        return folder;
    }

    private async Task<ImageRecord> RowAsync(string folderId, string relativePath)
        => (await _db.Images.GetByFolderAsync(folderId)).Single(
            r => string.Equals(r.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));

    private Task<ScanSummary> RescanAsync(string folderId)
        => _scan.ScanAsync(folderId, null, TestContext.Current.CancellationToken)
            .ContinueWith(t =>
            {
                Assert.True(t.Result.IsSuccess, t.Result.Message);
                return t.Result.Value!;
            }, TaskScheduler.Default);

    private async Task TagAsync(string imageId, string name)
    {
        var tag = new Tag { Id = IdGenerator.NewId(), Name = name, Type = TagType.Simple };
        await _db.Tags.AddAsync(tag);
        await _db.Tags.UpsertImageTagAsync(new ImageTag { ImageId = imageId, TagId = tag.Id, Value = null });
    }

    // ---- ① 内容変更 ----

    [Fact]
    public async Task 内容変更はpending化しoriginがchangedでタグは保持される()
    {
        var folder = await InitialScanAsync(("a.jpg", "content-a"));
        var before = await RowAsync(folder.Id, "a.jpg");
        Assert.Equal(ImageStatus.Normal, before.Status);
        await TagAsync(before.Id, "marked");

        WriteFile("a.jpg", "content-a-edited");
        await RescanAsync(folder.Id);

        var after = await RowAsync(folder.Id, "a.jpg");
        Assert.Equal(before.Id, after.Id);                          // image_id 不変
        Assert.Equal(ImageStatus.Pending, after.Status);            // T10
        Assert.Equal(PendingOrigin.Changed, after.PendingOrigin);
        Assert.NotEqual(before.Hash, after.Hash);                   // メタは更新される
        Assert.Single(await _db.Tags.GetImageTagsAsync(after.Id));  // タグ保持
    }

    // ---- ② 再スキャンの新規/⑤ 初回は normal ----

    [Fact]
    public async Task 初回スキャンはnormal_再スキャンの新規はpendingでoriginがnew()
    {
        var folder = await InitialScanAsync(("a.jpg", "content-a"));
        Assert.Equal(ImageStatus.Normal, (await RowAsync(folder.Id, "a.jpg")).Status);
        Assert.Null((await RowAsync(folder.Id, "a.jpg")).PendingOrigin);

        WriteFile("b.jpg", "content-b");
        var summary = await RescanAsync(folder.Id);

        var b = await RowAsync(folder.Id, "b.jpg");
        Assert.Equal(ImageStatus.Pending, b.Status);                // T2(v5.0=再スキャン新規は全て pending)
        Assert.Equal(PendingOrigin.New, b.PendingOrigin);
        Assert.Null(b.CandidateLinkId);                             // 同ハッシュ missing なし=候補なし
        Assert.Equal(1, summary.Pending);
        Assert.Equal(0, summary.Added);
    }

    [Fact]
    public async Task 再スキャンの新規が同ハッシュmissingに一致すると候補ヒントつきpending()
    {
        var folder = await InitialScanAsync(("a.jpg", "content-a"));
        var a = await RowAsync(folder.Id, "a.jpg");

        File.Move(Path.Combine(_root, "a.jpg"), Path.Combine(_root, "renamed.jpg"));
        await RescanAsync(folder.Id);

        var renamed = await RowAsync(folder.Id, "renamed.jpg");
        Assert.Equal(ImageStatus.Pending, renamed.Status);
        Assert.Equal(PendingOrigin.New, renamed.PendingOrigin);
        Assert.Equal(a.Id, renamed.CandidateLinkId);                // 旧 3a=候補ヒントの包含
        Assert.Equal(ImageStatus.Missing, (await RowAsync(folder.Id, "a.jpg")).Status);
    }

    // ---- ③ missing 再出現 ----

    [Fact]
    public async Task missingのパスにファイルが再出現するとpending化する_無条件normalに戻さない()
    {
        var folder = await InitialScanAsync(("a.jpg", "content-a"));
        var a = await RowAsync(folder.Id, "a.jpg");
        File.Delete(Path.Combine(_root, "a.jpg"));
        await RescanAsync(folder.Id);
        Assert.Equal(ImageStatus.Missing, (await RowAsync(folder.Id, "a.jpg")).Status);

        // 同一内容で再出現(メタは書き直しで更新日時が変わり得る=規則 1/2 どちらでも pending)
        WriteFile("a.jpg", "content-a");
        await RescanAsync(folder.Id);

        var reappeared = await RowAsync(folder.Id, "a.jpg");
        Assert.Equal(a.Id, reappeared.Id);
        Assert.Equal(ImageStatus.Pending, reappeared.Status);       // T11
        Assert.Equal(PendingOrigin.Reappeared, reappeared.PendingOrigin);
    }

    // ---- ④ pending 消失= missing 保持 ----

    [Fact]
    public async Task pendingのファイル消失は行削除せずmissing化しタグを保全する()
    {
        var folder = await InitialScanAsync(("a.jpg", "content-a"));
        var a = await RowAsync(folder.Id, "a.jpg");
        await TagAsync(a.Id, "marked");

        WriteFile("a.jpg", "content-a-edited");                     // 内容変更→pending
        await RescanAsync(folder.Id);
        Assert.Equal(ImageStatus.Pending, (await RowAsync(folder.Id, "a.jpg")).Status);

        File.Delete(Path.Combine(_root, "a.jpg"));                  // pending のファイル消失
        await RescanAsync(folder.Id);

        var after = await RowAsync(folder.Id, "a.jpg");             // 行は残る(旧手順 5=行削除の廃止)
        Assert.Equal(a.Id, after.Id);
        Assert.Equal(ImageStatus.Missing, after.Status);            // T12
        Assert.Null(after.PendingOrigin);                           // origin クリア
        Assert.Null(after.CandidateLinkId);
        Assert.Single(await _db.Tags.GetImageTagsAsync(a.Id));      // タグ保全
    }

    // ---- ⑥ 裁定 3 遷移 ----

    [Fact]
    public async Task 裁定_受け入れるはnormal化しタグとIDを保持する()
    {
        var folder = await InitialScanAsync(("a.jpg", "content-a"));
        var a = await RowAsync(folder.Id, "a.jpg");
        await TagAsync(a.Id, "marked");
        WriteFile("a.jpg", "content-a-edited");
        await RescanAsync(folder.Id);

        var accepted = await _review.AcceptAsync(a.Id);
        Assert.True(accepted.IsSuccess, accepted.Message);

        var after = await RowAsync(folder.Id, "a.jpg");
        Assert.Equal(a.Id, after.Id);
        Assert.Equal(ImageStatus.Normal, after.Status);             // T13
        Assert.Null(after.PendingOrigin);
        Assert.Single(await _db.Tags.GetImageTagsAsync(a.Id));

        // pending 限定: normal をもう一度裁定 → 拒否
        Assert.False((await _review.AcceptAsync(a.Id)).IsSuccess);
    }

    [Fact]
    public async Task 裁定_別画像として扱うは原子的な行置換で新IDかつタグ消滅かつ1パス1行()
    {
        var folder = await InitialScanAsync(("a.jpg", "content-a"));
        var a = await RowAsync(folder.Id, "a.jpg");
        await TagAsync(a.Id, "marked");
        WriteFile("a.jpg", "content-a-replaced");
        await RescanAsync(folder.Id);
        var pending = await RowAsync(folder.Id, "a.jpg");

        var replaced = await _review.TreatAsNewAsync(a.Id);
        Assert.True(replaced.IsSuccess, replaced.Message);
        var newId = replaced.Value!;
        Assert.NotEqual(a.Id, newId);                               // 新 image_id

        var rows = (await _db.Images.GetByFolderAsync(folder.Id))
            .Where(r => string.Equals(r.RelativePath, "a.jpg", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var row = Assert.Single(rows);                              // 1 パス 1 行の不変(T14)
        Assert.Equal(newId, row.Id);
        Assert.Equal(ImageStatus.Normal, row.Status);
        Assert.Null(row.PendingOrigin);
        Assert.Equal(pending.Hash, row.Hash);                       // パス/メタは維持
        Assert.Equal(pending.FileSize, row.FileSize);
        Assert.Null(await _db.Images.GetByIdAsync(a.Id));           // 旧行消滅
        Assert.Empty(await _db.Tags.GetImageTagsAsync(newId));      // タグは引き継がない
    }

    [Fact]
    public async Task 裁定_削除するはdeleted化しタグを保持する_pending以外は拒否()
    {
        var folder = await InitialScanAsync(("a.jpg", "content-a"));
        var a = await RowAsync(folder.Id, "a.jpg");
        await TagAsync(a.Id, "marked");
        WriteFile("a.jpg", "content-a-edited");
        await RescanAsync(folder.Id);

        var deleted = await _review.DeleteAsync(a.Id);
        Assert.True(deleted.IsSuccess, deleted.Message);
        var after = await RowAsync(folder.Id, "a.jpg");
        Assert.Equal(ImageStatus.Deleted, after.Status);            // T15(ゴミ箱へ)
        Assert.Null(after.PendingOrigin);
        Assert.Single(await _db.Tags.GetImageTagsAsync(a.Id));      // タグ保持(復元可)

        // pending 限定: deleted の再裁定・置換は拒否
        Assert.False((await _review.DeleteAsync(a.Id)).IsSuccess);
        Assert.False((await _review.TreatAsNewAsync(a.Id)).IsSuccess);
    }
}
