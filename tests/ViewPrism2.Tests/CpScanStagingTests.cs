using System.Text;
using ViewPrism2.App.ViewModels;
using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Infrastructure.Scanning;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// CP-SCAN-004 拡張(ECO-130/REQ-100): 再スキャンの二段階化。
/// R5 プローブ先行 — ①差分計算は DB 完全無変更 ②適用後状態は一段階スキャンと同値(パリティ)
/// ③キャンセル/破棄=無変更 ④読み取り失敗の独立集計 ⑤遷移別内訳の検算 ⑥例示の上限。
/// フィクスチャは CpScan004Tests と同型(一時ディレクトリ+実ファイル+一時ファイル DB)。
/// </summary>
[Trait("cp", "CP-SCAN-004")]
public sealed class CpScanStagingTests : IDisposable
{
    private readonly TempDb _db = new();
    private readonly string _root;
    private readonly ScanService _scan;

    public CpScanStagingTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ViewPrism2.Tests", Guid.NewGuid().ToString("D"), "files");
        Directory.CreateDirectory(_root);
        _scan = new ScanService(_db.Folders, _db.Images, _db.Clock);
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

    private void DeleteFile(string relativePath)
        => File.Delete(Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string HashOf(string content)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        return FileHasher.ComputeSha256(stream);
    }

    private async Task<SyncFolder> RegisterFolderAsync()
    {
        var folder = new SyncFolder
        {
            Id = IdGenerator.NewId(),
            Name = "fixture",
            Path = _root,
        };
        var result = await _db.Folders.AddAsync(folder);
        Assert.True(result.IsSuccess);
        return folder;
    }

    private Task SeedRowAsync(string folderId, string relativePath, ImageStatus status, string hash, string id)
        => SeedRowAsync(_db, folderId, relativePath, status, hash, id);

    private static async Task SeedRowAsync(
        TempDb db, string folderId, string relativePath, ImageStatus status, string hash, string id)
    {
        await db.Images.AddAsync(new ImageRecord
        {
            Id = id,
            SyncFolderId = folderId,
            RelativePath = relativePath,
            FileName = relativePath[(relativePath.LastIndexOf('/') + 1)..],
            FileSize = 1,
            Hash = hash,
            Status = status,
            CreatedDate = "2026-01-01T00:00:00.000Z",
            ModifiedDate = "2026-01-01T00:00:00.000Z",
        });
    }

    /// <summary>
    /// 標準フィクスチャ: 初回スキャン(a/b/e=normal)後に全遷移を仕込む。
    /// 期待: unchanged=e / meta=b / addNormal=c / addPending=d(candidate=a) / missing=a /
    /// pendingRemoved=p / deletedExcluded=x(メタ更新) / readFailure=locked。
    /// 返り値はロック保持ストリーム(caller が dispose するまで locked.jpg は読めない)。
    /// </summary>
    private async Task<(SyncFolder Folder, string AId, FileStream Lock)> BuildRescanFixtureAsync()
    {
        WriteFile("a.jpg", "content-a");
        WriteFile("b.jpg", "content-b");
        WriteFile("e.jpg", "content-e");
        var folder = await RegisterFolderAsync();
        var initial = await _scan.ScanAsync(folder.Id, null, TestContext.Current.CancellationToken);
        Assert.True(initial.IsSuccess, initial.Message);
        var rows = await _db.Images.GetByFolderAsync(folder.Id);
        var aId = rows.Single(r => r.RelativePath == "a.jpg").Id;

        // 遷移を仕込む(初回スキャン後=再スキャン相当の前提状態)
        DeleteFile("a.jpg");                                   // 手順4: normal→missing
        WriteFile("b.jpg", "content-b-changed");               // 規則2: メタ更新
        WriteFile("c.jpg", "content-c");                       // 規則3b: 新規 normal
        WriteFile("d.jpg", "content-a");                       // 規則3a: a と同ハッシュ → pending(candidate=a)
        await SeedRowAsync(folder.Id, "p.jpg", ImageStatus.Pending, HashOf("content-p"), "img-p0000000001");
        // 手順5: p.jpg は物理なし → 行削除
        WriteFile("deleted/x.jpg", "content-x");
        await SeedRowAsync(folder.Id, "deleted/x.jpg", ImageStatus.Deleted, HashOf("old-x"), "img-x0000000001");
        // deleted 行にパス一致+メタ不一致 → 規則2 のメタ更新は適用・再登録はしない(除外計上)
        var lockedPath = WriteFile("locked.jpg", "content-locked");
        var lockStream = new FileStream(lockedPath, FileMode.Open, FileAccess.Read, FileShare.None);
        return (folder, aId, lockStream);
    }

    private async Task<IReadOnlyList<ImageRecord>> SnapshotRowsAsync(string folderId)
        => await _db.Images.GetByFolderAsync(folderId);

    private static void AssertRowsIdentical(IReadOnlyList<ImageRecord> before, IReadOnlyList<ImageRecord> after)
    {
        Assert.Equal(before.Count, after.Count);
        var byId = after.ToDictionary(r => r.Id, StringComparer.Ordinal);
        foreach (var b in before)
        {
            Assert.True(byId.TryGetValue(b.Id, out var a), $"行が消えた: {b.RelativePath}");
            Assert.Equal(b, a);
        }
    }

    // ---- ① DB 無変更 ----

    [Fact]
    public async Task 差分計算はDBを一切変更しない_last_scanも不変()
    {
        var (folder, _, lockStream) = await BuildRescanFixtureAsync();
        using var _ = lockStream;
        var before = await SnapshotRowsAsync(folder.Id);
        var lastScanBefore = (await _db.Folders.GetByIdAsync(folder.Id))!.LastScan;

        var staged = await _scan.StageAsync(folder.Id, null, TestContext.Current.CancellationToken);
        Assert.True(staged.IsSuccess, staged.Message);

        var after = await SnapshotRowsAsync(folder.Id);
        AssertRowsIdentical(before, after);
        Assert.Equal(lastScanBefore, (await _db.Folders.GetByIdAsync(folder.Id))!.LastScan);
    }

    // ---- ⑤ 遷移別内訳の検算 ----

    [Fact]
    public async Task 遷移別集計が全ケースで一致する()
    {
        var (folder, aId, lockStream) = await BuildRescanFixtureAsync();
        using var _ = lockStream;

        var staged = await _scan.StageAsync(folder.Id, null, TestContext.Current.CancellationToken);
        Assert.True(staged.IsSuccess, staged.Message);
        var s = staged.Value!;

        Assert.Equal(1, s.Unchanged);            // e.jpg
        Assert.Equal(1, s.MetaUpdated);          // b.jpg
        Assert.Equal(1, s.AddedNormal);          // c.jpg
        Assert.Equal(1, s.AddedPending);         // d.jpg(candidate=a)
        Assert.Equal(1, s.MissingFromNormal);    // a.jpg
        Assert.Equal(1, s.PendingRemoved);       // p.jpg
        Assert.Equal(1, s.DeletedExcluded);      // deleted/x.jpg(メタ更新のみ)
        Assert.Equal(1, s.ReadFailures);         // locked.jpg
        Assert.Equal(5, s.TotalChanges);         // meta+addN+addP+missing+pendingRemoved
        Assert.Equal(5, s.ManagedTotal);         // a,b,e,p,x
        Assert.Equal(6, s.ScannedFiles);         // e,b,c,d,x,locked

        // 変更案の中身: candidate_link_id が missing 化される a を指す
        var pendingAdd = Assert.Single(s.Adds, r => r.Status == ImageStatus.Pending);
        Assert.Equal(aId, pendingAdd.CandidateLinkId);
        Assert.Equal(2, s.MetaUpdates.Count);    // b(規則2)+x(deleted 行のメタ更新も従来どおり)
        var statusUpdate = Assert.Single(s.StatusUpdates);
        Assert.Equal(aId, statusUpdate.Id);
        Assert.Equal(ImageStatus.Missing, statusUpdate.Status);
        Assert.Equal("img-p0000000001", Assert.Single(s.Deletes));
    }

    // ---- ⑥ 例示の上限 ----

    [Fact]
    public async Task 例示は遷移別に上限件数まで_件数は全数を保つ()
    {
        WriteFile("seed.jpg", "seed");
        var folder = await RegisterFolderAsync();
        var initial = await _scan.ScanAsync(folder.Id, null, TestContext.Current.CancellationToken);
        Assert.True(initial.IsSuccess);
        for (var i = 0; i < ScanStaging.ExamplesPerKind + 3; i++)
        {
            WriteFile($"new-{i:D2}.jpg", $"new-content-{i}");
        }

        var staged = await _scan.StageAsync(folder.Id, null, TestContext.Current.CancellationToken);
        Assert.True(staged.IsSuccess, staged.Message);
        var s = staged.Value!;

        Assert.Equal(ScanStaging.ExamplesPerKind + 3, s.AddedNormal);
        Assert.Equal(
            ScanStaging.ExamplesPerKind,
            s.Examples.Count(e => e.Kind == ScanTransitionKind.AddedNormal));
    }

    // ---- ③ キャンセル=無変更 ----

    [Fact]
    public async Task キャンセルはOperationCanceledでDB無変更()
    {
        var (folder, _, lockStream) = await BuildRescanFixtureAsync();
        using var _ = lockStream;
        var before = await SnapshotRowsAsync(folder.Id);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _scan.StageAsync(folder.Id, null, cts.Token));

        AssertRowsIdentical(before, await SnapshotRowsAsync(folder.Id));
    }

    // ---- ② 適用後の状態+パリティ ----

    [Fact]
    public async Task 適用後の状態は仕様どおり_last_scanを更新しsummaryを返す()
    {
        var (folder, aId, lockStream) = await BuildRescanFixtureAsync();
        using var _ = lockStream;

        var staged = await _scan.StageAsync(folder.Id, null, TestContext.Current.CancellationToken);
        Assert.True(staged.IsSuccess, staged.Message);
        var lastScanBefore = (await _db.Folders.GetByIdAsync(folder.Id))!.LastScan;

        var applied = await _scan.ApplyStagedAsync(staged.Value!, null, TestContext.Current.CancellationToken);
        Assert.True(applied.IsSuccess, applied.Message);

        var rows = await SnapshotRowsAsync(folder.Id);
        Assert.Equal(ImageStatus.Missing, rows.Single(r => r.Id == aId).Status);            // a: missing 化
        Assert.DoesNotContain(rows, r => r.RelativePath == "p.jpg");                        // p: 行削除
        Assert.Equal(HashOf("content-b-changed"), rows.Single(r => r.RelativePath == "b.jpg").Hash);
        var c = rows.Single(r => r.RelativePath == "c.jpg");
        Assert.Equal(ImageStatus.Normal, c.Status);
        var d = rows.Single(r => r.RelativePath == "d.jpg");
        Assert.Equal(ImageStatus.Pending, d.Status);
        Assert.Equal(aId, d.CandidateLinkId);
        var x = rows.Single(r => r.RelativePath == "deleted/x.jpg");
        Assert.Equal(ImageStatus.Deleted, x.Status);                                        // 再登録しない
        Assert.Equal(HashOf("content-x"), x.Hash);                                          // メタ更新は適用
        Assert.DoesNotContain(rows, r => r.RelativePath == "locked.jpg");                   // 読み取り失敗=非登録

        // last_scan 更新+summary(ScanAsync と同じ意味論)
        Assert.NotEqual(lastScanBefore, (await _db.Folders.GetByIdAsync(folder.Id))!.LastScan);
        var summary = applied.Value!;
        Assert.Equal(1, summary.Added);
        Assert.Equal(1, summary.Pending);
        Assert.Equal(1, summary.Missing);
        Assert.Equal(2, summary.Updated);   // b+x(deleted 行メタ更新も従来どおり計上)
        Assert.Equal(2, summary.Skipped);   // e(変更なし)+locked(読み取り失敗)
    }

    [Fact]
    public async Task 適用の失敗は例外でなくResult失敗で返る()
    {
        // R8 所見4: 適用中の DB 例外が未処置だと Applying 面でスタックし、✕ 経路(所見1)へ落ちる。
        // 有界バッチ単位の部分適用があり得ることは仕様へ明記(REQ-100)・UI は失敗理由表示+Summary 復帰
        var (folder, _, lockStream) = await BuildRescanFixtureAsync();
        using var _ = lockStream;
        var staged = await _scan.StageAsync(folder.Id, null, TestContext.Current.CancellationToken);
        Assert.True(staged.IsSuccess, staged.Message);

        _db.Dispose(); // DB を先に破壊= 適用中の SqliteException/ObjectDisposedException を誘発
        var applied = await _scan.ApplyStagedAsync(staged.Value!, null, TestContext.Current.CancellationToken);
        Assert.False(applied.IsSuccess); // throw ではなく Fail(VM が StatusMessage 表示+Summary 復帰できる)
    }

    [Fact]
    public async Task パリティ_ステージ適用と直接スキャンで同一の最終状態()
    {
        // 同一内容の双子フィクスチャ(別 DB・別ルート)を作り、片方は ScanAsync 直接・
        // 片方は StageAsync→ApplyStagedAsync で、正規化した行集合が一致することを確認する。
        using var twinDb = new TempDb();
        var twinRoot = Path.Combine(Path.GetTempPath(), "ViewPrism2.Tests", Guid.NewGuid().ToString("D"), "files");
        Directory.CreateDirectory(twinRoot);
        var twinScan = new ScanService(twinDb.Folders, twinDb.Images, twinDb.Clock);
        try
        {
            async Task<string> BuildAsync(TempDb db, ScanService scan, string root)
            {
                void W(string rel, string content)
                {
                    var full = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                    File.WriteAllBytes(full, Encoding.UTF8.GetBytes(content));
                }

                W("a.jpg", "content-a");
                W("b.jpg", "content-b");
                W("e.jpg", "content-e");
                var folder = new SyncFolder { Id = IdGenerator.NewId(), Name = "fixture", Path = root };
                Assert.True((await db.Folders.AddAsync(folder)).IsSuccess);
                Assert.True((await scan.ScanAsync(folder.Id, null, TestContext.Current.CancellationToken)).IsSuccess);
                File.Delete(Path.Combine(root, "a.jpg"));
                W("b.jpg", "content-b-changed");
                W("c.jpg", "content-c");
                W("d.jpg", "content-a");
                // R8 所見12: 手順 5(pending 行削除)と deleted 行メタ更新も双子へ含めて経路網羅する
                await SeedRowAsync(db, folder.Id, "p.jpg", ImageStatus.Pending, HashOf("content-p"), "img-p0000000001");
                W("deleted/x.jpg", "content-x");
                await SeedRowAsync(db, folder.Id, "deleted/x.jpg", ImageStatus.Deleted, HashOf("old-x"), "img-x0000000001");
                return folder.Id;
            }

            var folderId = await BuildAsync(_db, _scan, _root);
            var twinFolderId = await BuildAsync(twinDb, twinScan, twinRoot);

            // 直接スキャン(従来の一段階)
            Assert.True((await _scan.ScanAsync(folderId, null, TestContext.Current.CancellationToken)).IsSuccess);
            // 二段階(ステージ→適用)
            var staged = await twinScan.StageAsync(twinFolderId, null, TestContext.Current.CancellationToken);
            Assert.True(staged.IsSuccess, staged.Message);
            Assert.True((await twinScan.ApplyStagedAsync(
                staged.Value!, null, TestContext.Current.CancellationToken)).IsSuccess);

            // R8 所見12: 候補は「参照先の相対パス」で正規化(bool では別 missing への誤リンクを見逃す)。
            // FileSize も比較に含める(EQ-001: id/日時の具体値のみ無視)
            static List<(string Path, ImageStatus Status, string Hash, long Size, string? CandidatePath)> Normalize(
                IReadOnlyList<ImageRecord> rows)
            {
                var pathById = rows.ToDictionary(r => r.Id, r => r.RelativePath.ToLowerInvariant(), StringComparer.Ordinal);
                return rows
                    .Select(r => (
                        r.RelativePath.ToLowerInvariant(),
                        r.Status,
                        r.Hash,
                        r.FileSize,
                        r.CandidateLinkId is { } link ? pathById[link] : null))
                    .OrderBy(t => t.Item1, StringComparer.Ordinal)
                    .ToList();
            }

            Assert.Equal(
                Normalize(await _db.Images.GetByFolderAsync(folderId)),
                Normalize(await twinDb.Images.GetByFolderAsync(twinFolderId)));
        }
        finally
        {
            try
            {
                Directory.Delete(Path.GetDirectoryName(twinRoot)!, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}

/// <summary>
/// E-UI-SCANSTAGE-048 の決定論ロジック(missing 率 3 色・確認強度)。閾値= CAD SCAN-002 裁定値。
/// </summary>
[Trait("cp", "CP-UI-G1")]
public sealed class CpScanSummaryLogicTests
{
    [Theory]
    [InlineData(0, 100, MissingRateTier.Green)]
    [InlineData(9, 1000, MissingRateTier.Green)]    // 0.9% <1% はグリーン
    [InlineData(1, 100, MissingRateTier.Yellow)]    // 1% ちょうどはイエロー
    [InlineData(49, 100, MissingRateTier.Yellow)]
    [InlineData(50, 100, MissingRateTier.Red)]      // 50% ちょうどはレッド
    [InlineData(9, 12400, MissingRateTier.Green)]   // SC-2(0.07%)
    [InlineData(9860, 259984, MissingRateTier.Yellow)] // SC-3(3.8%)
    [InlineData(257400, 260000, MissingRateTier.Red)]  // SC-4(99.0%)
    [InlineData(0, 0, MissingRateTier.Green)]       // 空コレクションの縮退
    public void missing率の色段はSCAN002の閾値に一致(int missing, int total, MissingRateTier expected)
        => Assert.Equal(expected, ScanSummaryLogic.RateTier(missing, total));

    [Theory]
    [InlineData(0, ScanConfirmTier.Normal)]
    [InlineData(28, ScanConfirmTier.Normal)]        // SC-2
    [InlineData(99, ScanConfirmTier.Normal)]
    [InlineData(100, ScanConfirmTier.WithDetail)]
    [InlineData(999, ScanConfirmTier.WithDetail)]
    [InlineData(1000, ScanConfirmTier.ConfirmDialog)]
    [InlineData(10000, ScanConfirmTier.ConfirmDialog)] // SC-3/SC-6
    public void 確認強度は変更合計の段に一致(int totalChanges, ScanConfirmTier expected)
        => Assert.Equal(expected, ScanSummaryLogic.ConfirmTier(totalChanges));
}
