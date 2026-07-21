using System.Text;
using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Repositories;
using ViewPrism2.Core.Services;
using ViewPrism2.Infrastructure.Scanning;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// CP-SCAN-004: スキャン判定(OC-5)・遷移表・再リンクが仕様 §2.1 と一致する。
/// 一時ディレクトリ+実ファイル+一時ファイル DB で、判定結果と前後 DB 状態の完全一致を検査する。
/// </summary>
[Trait("cp", "CP-SCAN-004")]
public sealed class CpScan004Tests : IDisposable
{
    private readonly TempDb _db = new();
    private readonly string _root;
    private readonly ScanService _scan;
    private readonly RelinkService _relink;

    public CpScan004Tests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ViewPrism2.Tests", Guid.NewGuid().ToString("D"), "files");
        Directory.CreateDirectory(_root);
        _scan = new ScanService(_db.Folders, _db.Images, _db.Clock);
        _relink = new RelinkService(_db.Images);
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
        File.WriteAllBytes(fullPath, Encoding.UTF8.GetBytes(content)); // BOM なし(HashOf と一致させる)
        return fullPath;
    }

    private async Task<SyncFolder> RegisterFolderAsync(
        IReadOnlyList<string>? excludePatterns = null, bool includeSubfolders = true)
    {
        var folder = new SyncFolder
        {
            Id = IdGenerator.NewId(),
            Name = "fixture",
            Path = _root,
            IncludeSubfolders = includeSubfolders,
            ExcludePatterns = excludePatterns ?? [],
        };
        var result = await _db.Folders.AddAsync(folder);
        Assert.True(result.IsSuccess);
        return folder;
    }

    private async Task<ScanSummary> ScanOkAsync(string folderId)
    {
        var result = await _scan.ScanAsync(folderId, null, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess, result.Message);
        return result.Value!;
    }

    private static string HashOf(string content)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        return FileHasher.ComputeSha256(stream);
    }

    private Task<ImageRecord> SeedImageAsync(
        string folderId, string relativePath, ImageStatus status, string contentForHash, string id)
    {
        return SeedImageAsync(_db, folderId, relativePath, status, HashOf(contentForHash), id);
    }

    private static async Task<ImageRecord> SeedImageAsync(
        TempDb db, string folderId, string relativePath, ImageStatus status, string hash, string id)
    {
        var image = new ImageRecord
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
        };
        await db.Images.AddAsync(image);
        return image;
    }

    // ---- OC-5: 判定器の純粋規則 ----

    [Fact]
    public void 規則1_パス一致かつサイズ日時一致はSkipでハッシュを計算しない()
    {
        var judge = new ScanJudge();
        var hashCalled = false;
        var existing = NewRecord("img-1", "a.jpg", 10, "2026-01-01T00:00:00.000Z");
        var decision = judge.Judge(
            new ScanFileFacts("a.jpg", 10, "2026-01-01T00:00:00.000Z", "2026-01-01T00:00:00.000Z",
                () => { hashCalled = true; return new string('0', 64); }),
            new ScanDbFacts(existing, NoCandidates),
            isInitialScan: false);

        Assert.Equal(ScanDecisionKind.Skip, decision.Kind);
        Assert.False(hashCalled); // 遅延計算: Skip では呼ばれない
    }

    [Fact]
    public void 規則2_サイズまたは日時の差異はハッシュ再計算しnormal起点はpending化する()
    {
        // v5.0(ECO-129/REQ-101): 内容変更= 編集か差し替えかを機械判定しない → pending('changed')
        var judge = new ScanJudge();
        var existing = NewRecord("img-1", "a.jpg", 10, "2026-01-01T00:00:00.000Z");

        var bySize = judge.Judge(
            new ScanFileFacts("a.jpg", 20, "2026-01-01T00:00:00.000Z", "2026-01-01T00:00:00.000Z", () => "h1"),
            new ScanDbFacts(existing, NoCandidates), isInitialScan: false);
        Assert.Equal(ScanDecisionKind.UpdateMetaAndPend, bySize.Kind);
        Assert.Equal("h1", bySize.Hash);
        Assert.Equal(PendingOrigin.Changed, bySize.PendingOrigin);

        var byDate = judge.Judge(
            new ScanFileFacts("a.jpg", 10, "2026-01-02T00:00:00.000Z", "2026-01-01T00:00:00.000Z", () => "h2"),
            new ScanDbFacts(existing, NoCandidates), isInitialScan: false);
        Assert.Equal(ScanDecisionKind.UpdateMetaAndPend, byDate.Kind);

        // pending 起点= メタ追随のみ(origin 維持)/deleted 起点= 除外(メタ更新のみ・status 不変)
        var pendingRow = NewRecord("img-2", "b.jpg", 10, "2026-01-01T00:00:00.000Z", ImageStatus.Pending);
        Assert.Equal(ScanDecisionKind.UpdateMeta, judge.Judge(
            new ScanFileFacts("b.jpg", 20, "2026-01-01T00:00:00.000Z", "2026-01-01T00:00:00.000Z", () => "h3"),
            new ScanDbFacts(pendingRow, NoCandidates), isInitialScan: false).Kind);
        var deletedRow = NewRecord("img-3", "c.jpg", 10, "2026-01-01T00:00:00.000Z", ImageStatus.Deleted);
        Assert.Equal(ScanDecisionKind.UpdateMeta, judge.Judge(
            new ScanFileFacts("c.jpg", 20, "2026-01-01T00:00:00.000Z", "2026-01-01T00:00:00.000Z", () => "h4"),
            new ScanDbFacts(deletedRow, NoCandidates), isInitialScan: false).Kind);
    }

    [Fact]
    public void 規則3a_候補複数時はid昇順の先頭()
    {
        var judge = new ScanJudge();
        var hash = HashOf("x");
        var missingA = NewRecord("m-a", "old1.jpg", 1, "2026-01-01T00:00:00.000Z", ImageStatus.Missing, hash);
        var missingB = NewRecord("m-b", "old2.jpg", 1, "2026-01-01T00:00:00.000Z", ImageStatus.Missing, hash);

        var decision = judge.Judge(
            new ScanFileFacts("new.jpg", 1, "2026-01-01T00:00:00.000Z", "2026-01-01T00:00:00.000Z", () => hash),
            new ScanDbFacts(null, ScanJudge.BuildMissingCandidateIndex([missingB, missingA])),
            isInitialScan: false);

        Assert.Equal(ScanDecisionKind.AddPending, decision.Kind);
        Assert.Equal("m-a", decision.CandidateLinkId);
    }

    [Fact]
    public void 規則3b_初回スキャンは同ハッシュmissingがあってもAddNormal()
    {
        var judge = new ScanJudge();
        var hash = HashOf("x");
        var missing = NewRecord("m-1", "old.jpg", 1, "2026-01-01T00:00:00.000Z", ImageStatus.Missing, hash);

        var decision = judge.Judge(
            new ScanFileFacts("new.jpg", 1, "2026-01-01T00:00:00.000Z", "2026-01-01T00:00:00.000Z", () => hash),
            new ScanDbFacts(null, ScanJudge.BuildMissingCandidateIndex([missing])),
            isInitialScan: true);

        Assert.Equal(ScanDecisionKind.AddNormal, decision.Kind);
    }

    // ECO-134: missing 候補が無いケースの空写像(旧 API の空リスト [] に相当)。
    private static readonly IReadOnlyDictionary<string, string> NoCandidates =
        new Dictionary<string, string>();

    private static ImageRecord NewRecord(
        string id, string relativePath, long size, string modified,
        ImageStatus status = ImageStatus.Normal, string? hash = null)
        => new()
        {
            Id = id,
            SyncFolderId = "folder-1",
            RelativePath = relativePath,
            FileName = relativePath,
            FileSize = size,
            Hash = hash ?? new string('0', 64),
            Status = status,
            CreatedDate = "2026-01-01T00:00:00.000Z",
            ModifiedDate = modified,
        };

    // ---- サービス: 遷移と DB 状態 ----

    [Fact]
    public async Task 初回スキャンで新規追加_再スキャンで変更なしはスキップ()
    {
        WriteFile("a.jpg", "content-a");
        WriteFile("sub/b.png", "content-b");
        var folder = await RegisterFolderAsync();

        var first = await ScanOkAsync(folder.Id);
        Assert.Equal(2, first.Added);
        Assert.Equal(0, first.Skipped);

        var second = await ScanOkAsync(folder.Id);
        Assert.Equal(0, second.Added);
        Assert.Equal(2, second.Skipped); // 規則 (1)
        Assert.Equal(0, second.Updated);

        var rows = await _db.Images.GetByFolderAsync(folder.Id);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(ImageStatus.Normal, r.Status));
        Assert.Contains(rows, r => r.RelativePath == "sub/b.png"); // 正規形(INV-005)
    }

    [Fact]
    public async Task 初回スキャンは画像件数ごとの単行INSERTを行わない_FMEA039()
    {
        const int fileCount = 513;
        for (var i = 0; i < fileCount; i++)
        {
            WriteFile($"batch-{i:D3}.jpg", $"content-{i}");
        }

        var folder = await RegisterFolderAsync();
        var counting = new CountingImageRepository(_db.Images);
        var scan = new ScanService(_db.Folders, counting, _db.Clock);

        var result = await scan.ScanAsync(folder.Id, null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(fileCount, result.Value!.Added);
        Assert.Equal(0, counting.SingleAddCalls);
        Assert.Equal(2, counting.ScanBatchCalls);
        Assert.True(counting.MaxScanBatchCount <= 512);
    }

    [Fact]
    public async Task commit済みnormal画像だけをbatch順で公開通知する_ECO060()
    {
        const int fileCount = 513;
        for (var i = 0; i < fileCount; i++)
        {
            WriteFile($"publish-{i:D3}.jpg", $"content-{i}");
        }

        var folder = await RegisterFolderAsync();
        var published = new List<ScanBatchCommitted>();
        var result = await _scan.ScanAsync(
            folder.Id,
            progress: null,
            TestContext.Current.CancellationToken,
            new SyncProgress<ScanBatchCommitted>(published.Add));

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal([512, 1], published.Select(x => x.Images.Count));
        Assert.All(published.SelectMany(x => x.Images), image =>
        {
            Assert.Equal(folder.Id, image.SyncFolderId);
            Assert.Equal(ImageStatus.Normal, image.Status);
            Assert.Matches("^[0-9a-f]{64}$", image.Hash);
        });

        var persisted = (await _db.Images.GetByFolderAsync(folder.Id))
            .ToDictionary(x => x.Id, StringComparer.Ordinal);
        var publishedIds = published.SelectMany(x => x.Images).Select(x => x.Id).ToList();
        Assert.Equal(fileCount, publishedIds.Count);
        Assert.Equal(fileCount, publishedIds.Distinct(StringComparer.Ordinal).Count());
        Assert.All(publishedIds, id => Assert.True(persisted.ContainsKey(id), $"commit前の画像が通知されました: {id}"));
    }

    [Fact]
    public async Task commit失敗batchは公開通知しない_ECO060()
    {
        WriteFile("fail-publish.jpg", "content");
        var folder = await RegisterFolderAsync();
        var failing = new CountingImageRepository(_db.Images, failBatch: true);
        var scan = new ScanService(_db.Folders, failing, _db.Clock);
        var published = new List<ScanBatchCommitted>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => scan.ScanAsync(
            folder.Id,
            progress: null,
            TestContext.Current.CancellationToken,
            new SyncProgress<ScanBatchCommitted>(published.Add)));

        Assert.Empty(published);
        Assert.Empty(await _db.Images.GetByFolderAsync(folder.Id));
    }

    [Fact]
    public async Task 進捗通知は整数百分率の変化時だけで重複せず100で終わる_FMEA039()
    {
        const int fileCount = 250;
        for (var i = 0; i < fileCount; i++)
        {
            WriteFile($"progress-{i:D3}.jpg", $"content-{i}");
        }

        var folder = await RegisterFolderAsync();
        var values = new List<int>();
        var progress = new SyncProgress(values.Add);

        var result = await _scan.ScanAsync(folder.Id, progress, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Message);
        Assert.NotEmpty(values);
        Assert.Equal(100, values[^1]);
        Assert.True(values.Count <= 100, $"進捗通知が {values.Count} 回発生しました。");
        Assert.Equal(values, values.Distinct());
        Assert.True(values.SequenceEqual(values.Order()), "進捗は単調増加でなければなりません。");
    }

    [Fact]
    public async Task スキャンバッチ失敗は当該バッチを全ロールバックする_FMEA039()
    {
        var folder = await RegisterFolderAsync();
        var duplicatePath = "duplicate.jpg";
        var batch = new ScanMutationBatch(
            [
                NewBatchRecord(folder.Id, duplicatePath, "batch-1"),
                NewBatchRecord(folder.Id, duplicatePath, "batch-2"),
            ],
            [],
            [],
            []);

        await Assert.ThrowsAnyAsync<Exception>(() => _db.Images.ApplyScanBatchAsync(batch));

        Assert.Empty(await _db.Images.GetByFolderAsync(folder.Id));
    }

    [Fact]
    public async Task 同期完了repositoryでもScanAsyncは同期batchを待たず呼出threadを解放する_GF05901()
    {
        WriteFile("background.jpg", "content");
        var folder = await RegisterFolderAsync();
        using var batchEntered = new ManualResetEventSlim();
        using var releaseBatch = new ManualResetEventSlim();
        using var scanCallReturned = new ManualResetEventSlim();
        var counting = new CountingImageRepository(
            _db.Images,
            completeReadsSynchronously: true,
            batchEntered,
            releaseBatch);
        var scan = new ScanService(new ImmediateFolderRepository(folder), counting, _db.Clock);
        var callerReleasedBeforeBatch = false;
        var coordinator = Task.Run(
            () =>
            {
                Assert.True(batchEntered.Wait(TimeSpan.FromSeconds(5)), "scan batchへ到達しませんでした。");
                callerReleasedBeforeBatch = scanCallReturned.Wait(TimeSpan.FromMilliseconds(500));
                releaseBatch.Set();
            },
            TestContext.Current.CancellationToken);

        var scanTask = scan.ScanAsync(folder.Id, null, TestContext.Current.CancellationToken);
        scanCallReturned.Set();
        var result = await scanTask;
        await coordinator;

        Assert.True(result.IsSuccess, result.Message);
        Assert.True(callerReleasedBeforeBatch, "ScanAsync呼出元が同期scan batchの完了まで占有されました。");
    }

    private static ImageRecord NewBatchRecord(string folderId, string relativePath, string id)
        => new()
        {
            Id = id,
            SyncFolderId = folderId,
            RelativePath = relativePath,
            FileName = relativePath,
            FileSize = 1,
            Hash = new string('0', 64),
            Status = ImageStatus.Normal,
            CreatedDate = "2026-01-01T00:00:00.000Z",
            ModifiedDate = "2026-01-01T00:00:00.000Z",
        };

    [Fact]
    public async Task 規則2_内容変更でhashとメタが更新されnormal起点はpending化する()
    {
        // v5.0(ECO-129/REQ-101): 旧「status は変更しない」は superseded — 不確実は pending に倒す
        var path = WriteFile("a.jpg", "before");
        var folder = await RegisterFolderAsync();
        await ScanOkAsync(folder.Id);

        File.WriteAllText(path, "after-longer-content");
        var summary = await ScanOkAsync(folder.Id);

        Assert.Equal(1, summary.Updated);
        Assert.Equal(0, summary.Added);
        var row = Assert.Single(await _db.Images.GetByFolderAsync(folder.Id));
        Assert.Equal(HashOf("after-longer-content"), row.Hash);
        Assert.Equal(ImageStatus.Pending, row.Status);              // T10: 内容変更→pending
        Assert.Equal(PendingOrigin.Changed, row.PendingOrigin);
        Assert.Equal(Encoding.UTF8.GetBytes("after-longer-content").Length, row.FileSize);
    }

    [Fact]
    public async Task 手順4_物理消失したnormalはmissingになる()
    {
        var path = WriteFile("a.jpg", "content");
        var folder = await RegisterFolderAsync();
        await ScanOkAsync(folder.Id);

        File.Delete(path);
        var summary = await ScanOkAsync(folder.Id);

        Assert.Equal(1, summary.Missing);
        var row = Assert.Single(await _db.Images.GetByFolderAsync(folder.Id));
        Assert.Equal(ImageStatus.Missing, row.Status);
    }

    [Fact]
    public async Task 手順5_物理消失したpending行はmissingへ遷移し行は保持される()
    {
        // v5.0(ECO-129/REQ-101): 旧「行削除」は superseded — pending はタグを持ち得るため保全(T12)
        var folder = await RegisterFolderAsync();
        await ScanOkAsync(folder.Id); // last_scan を確定(以降は非初回)
        await SeedImageAsync(folder.Id, "ghost.jpg", ImageStatus.Pending, "ghost", "p-1");

        await ScanOkAsync(folder.Id);

        var row = Assert.Single(await _db.Images.GetByFolderAsync(folder.Id));
        Assert.Equal("p-1", row.Id);
        Assert.Equal(ImageStatus.Missing, row.Status);
        Assert.Null(row.CandidateLinkId);   // 候補関係の失効
        Assert.Null(row.PendingOrigin);     // origin クリア
    }

    [Fact]
    public async Task 拡張子は小文字化で判定し対象外は無視する()
    {
        WriteFile("upper.JPG", "a");   // 対象(大文字拡張子)
        WriteFile("note.txt", "b");    // 対象外
        WriteFile("vector.svg", "c");  // 対象外
        WriteFile("anim.webp", "d");   // 対象
        var folder = await RegisterFolderAsync();

        var summary = await ScanOkAsync(folder.Id);

        Assert.Equal(2, summary.Added);
        var rows = await _db.Images.GetByFolderAsync(folder.Id);
        Assert.Equal(["anim.webp", "upper.JPG"], rows.Select(r => r.RelativePath).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ExcludePatternsはファイル名の完全一致_大文字小文字無視_で除外する()
    {
        WriteFile("normal.jpg", "a");
        WriteFile("Cover.JPG", "b");      // パターン 'cover.jpg' に大文字小文字無視で一致 → 除外
        WriteFile("sub/Thumbs.db", "c");  // パターン例(REQ-010)
        var folder = await RegisterFolderAsync(excludePatterns: ["cover.jpg", "Thumbs.db"]);

        var summary = await ScanOkAsync(folder.Id);

        Assert.Equal(1, summary.Added);
        var row = Assert.Single(await _db.Images.GetByFolderAsync(folder.Id));
        Assert.Equal("normal.jpg", row.RelativePath);
    }

    [Fact]
    public async Task ロックされたファイルはスキップ計上されDBは不変_FMEA011()
    {
        WriteFile("ok.jpg", "fine");
        var lockedPath = WriteFile("locked.jpg", "locked-content");
        var folder = await RegisterFolderAsync();

        using (new FileStream(lockedPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var summary = await ScanOkAsync(folder.Id);
            Assert.Equal(1, summary.Added);
            Assert.Equal(1, summary.Skipped); // skipped 計上+警告ログ
        }

        var row = Assert.Single(await _db.Images.GetByFolderAsync(folder.Id));
        Assert.Equal("ok.jpg", row.RelativePath); // ロックファイルの行は作られない

        // 次回スキャンで再試行され、今度は取り込まれる(v5.0: 再スキャンの新規= pending)
        var retry = await ScanOkAsync(folder.Id);
        Assert.Equal(0, retry.Added);
        Assert.Equal(1, retry.Pending);
        var locked = Assert.Single(
            await _db.Images.GetByFolderAsync(folder.Id), r => r.RelativePath == "locked.jpg");
        Assert.Equal(ImageStatus.Pending, locked.Status);
    }

    [Fact]
    public async Task リネームはmissingとpendingになり再リンクでタグとidが保全される_FMEA004()
    {
        var oldPath = WriteFile("photos/original.jpg", "same-bytes");
        var folder = await RegisterFolderAsync();
        await ScanOkAsync(folder.Id);

        var image = Assert.Single(await _db.Images.GetByFolderAsync(folder.Id));
        var tag = new Tag { Id = "tag-keep", Name = "Keep", Type = TagType.Simple };
        await _db.Tags.AddAsync(tag);
        await _db.Tags.UpsertImageTagAsync(new ImageTag { ImageId = image.Id, TagId = tag.Id, Value = null });

        // ファイル名変更 → スキャン → missing+pending
        File.Move(oldPath, Path.Combine(Path.GetDirectoryName(oldPath)!, "renamed.jpg"));
        var summary = await ScanOkAsync(folder.Id);
        Assert.Equal(1, summary.Missing);
        Assert.Equal(1, summary.Pending);
        Assert.Equal(0, summary.Added); // 新規+missing の誤判定をしない(FMEA-004)

        var rows = await _db.Images.GetByFolderAsync(folder.Id);
        var missingRow = Assert.Single(rows, r => r.Status == ImageStatus.Missing);
        var pendingRow = Assert.Single(rows, r => r.Status == ImageStatus.Pending);
        Assert.Equal(image.Id, missingRow.Id);
        Assert.Equal(missingRow.Id, pendingRow.CandidateLinkId);

        // 再リンク確定 → 同一 image_id・新パス・status=normal・pending 行消滅・タグ残存
        var commit = await _relink.CommitRelinkAsync(missingRow.Id, pendingRow.Id);
        Assert.True(commit.IsSuccess);

        var after = Assert.Single(await _db.Images.GetByFolderAsync(folder.Id));
        Assert.Equal(image.Id, after.Id); // INV-001: id 不変
        Assert.Equal(ImageStatus.Normal, after.Status);
        Assert.Equal("photos/renamed.jpg", after.RelativePath);
        Assert.Null(after.CandidateLinkId);

        var tags = await _db.Tags.GetImageTagsAsync(image.Id);
        Assert.Single(tags); // タグ関連の保全
    }

    [Fact]
    public async Task 初回スキャンは同ハッシュmissingがあってもnormalで登録する()
    {
        var folder = await RegisterFolderAsync();
        await SeedImageAsync(folder.Id, "old.jpg", ImageStatus.Missing, "same", "m-1");
        WriteFile("new.jpg", "same");

        Assert.Null((await _db.Folders.GetByIdAsync(folder.Id))!.LastScan); // 初回
        var summary = await ScanOkAsync(folder.Id);

        Assert.Equal(1, summary.Added);
        Assert.Equal(0, summary.Pending);
        var added = Assert.Single(
            await _db.Images.GetByFolderAsync(folder.Id), r => r.RelativePath == "new.jpg");
        Assert.Equal(ImageStatus.Normal, added.Status);
    }

    [Fact]
    public async Task 候補が複数あるmissingはid昇順の先頭が候補になる()
    {
        var folder = await RegisterFolderAsync();
        await ScanOkAsync(folder.Id); // 非初回化
        await SeedImageAsync(folder.Id, "old-b.jpg", ImageStatus.Missing, "dup", "m-bbb");
        await SeedImageAsync(folder.Id, "old-a.jpg", ImageStatus.Missing, "dup", "m-aaa");
        WriteFile("new.jpg", "dup");

        var summary = await ScanOkAsync(folder.Id);

        Assert.Equal(1, summary.Pending);
        var pendingRow = Assert.Single(
            await _db.Images.GetByFolderAsync(folder.Id), r => r.Status == ImageStatus.Pending);
        Assert.Equal("m-aaa", pendingRow.CandidateLinkId);
    }

    [Fact]
    public async Task 再リンク候補はrelative_path昇順で列挙される()
    {
        var folder = await RegisterFolderAsync();
        var missing = await SeedImageAsync(folder.Id, "gone.jpg", ImageStatus.Missing, "dup", "m-1");
        await SeedImageAsync(folder.Id, "b/cand.jpg", ImageStatus.Pending, "dup", "p-b");
        await SeedImageAsync(folder.Id, "a/cand.jpg", ImageStatus.Pending, "dup", "p-a");
        await SeedImageAsync(folder.Id, "c/other.jpg", ImageStatus.Pending, "different", "p-c"); // 別ハッシュは候補外

        var candidates = await _relink.GetCandidatesAsync(missing.Id);

        Assert.Equal(["a/cand.jpg", "b/cand.jpg"], candidates.Select(c => c.RelativePath));
        // 各候補に相対パス・ファイルサイズ・更新日時を表示できる(REQ-017)
        Assert.All(candidates, c => Assert.Equal(1, c.FileSize));
        Assert.All(candidates, c => Assert.Equal("2026-01-01T00:00:00.000Z", c.ModifiedDate));
    }

    [Fact]
    public async Task 再リンクは遷移表外の組み合わせを拒否する()
    {
        var folder = await RegisterFolderAsync();
        var normal = await SeedImageAsync(folder.Id, "n.jpg", ImageStatus.Normal, "x", "n-1");
        var pending = await SeedImageAsync(folder.Id, "p.jpg", ImageStatus.Pending, "x", "p-1");

        var result = await _relink.CommitRelinkAsync(normal.Id, pending.Id); // missing でない
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.ValidationError, result.Error);

        var notFound = await _relink.CommitRelinkAsync("no-such-id", pending.Id);
        Assert.Equal(ErrorCode.NotFound, notFound.Error);
    }

    [Fact]
    public async Task 二重起動は同一フォルダで拒否される()
    {
        WriteFile("a.jpg", "content");
        var folder = await RegisterFolderAsync();

        Result<ScanSummary>? second = null;
        var probe = new SyncProgress(_ =>
        {
            // 1 本目の実行中(進捗通知中)に同一フォルダの 2 本目を要求する
            second ??= _scan.ScanAsync(folder.Id, null, CancellationToken.None).GetAwaiter().GetResult();
        });

        var first = await _scan.ScanAsync(folder.Id, probe, TestContext.Current.CancellationToken);

        Assert.True(first.IsSuccess);
        Assert.NotNull(second);
        Assert.False(second.IsSuccess);
        Assert.Equal(ErrorCode.ScanInProgress, second.Error);

        // 完了後は再スキャン可能
        var third = await _scan.ScanAsync(folder.Id, null, TestContext.Current.CancellationToken);
        Assert.True(third.IsSuccess);
    }

    [Fact]
    public async Task last_scanは完了後に更新され例外パスでも更新される()
    {
        WriteFile("a.jpg", "content");
        var folder = await RegisterFolderAsync();
        Assert.Null((await _db.Folders.GetByIdAsync(folder.Id))!.LastScan);

        await ScanOkAsync(folder.Id);
        var afterFirst = (await _db.Folders.GetByIdAsync(folder.Id))!.LastScan;
        Assert.NotNull(afterFirst);

        // 例外パス: ルートディレクトリ消失 → IoError でも last_scan は更新される
        Directory.Delete(_root, recursive: true);
        var failed = await _scan.ScanAsync(folder.Id, null, TestContext.Current.CancellationToken);
        Assert.False(failed.IsSuccess);
        Assert.Equal(ErrorCode.IoError, failed.Error);
        var afterFailed = (await _db.Folders.GetByIdAsync(folder.Id))!.LastScan;
        Assert.NotNull(afterFailed);
        Assert.True(string.CompareOrdinal(afterFailed, afterFirst) >= 0);

        // ルート消失時は手順 4 の一括 missing 化をしない(誤 missing 防止)
        var rows = await _db.Images.GetByFolderAsync(folder.Id);
        Assert.All(rows, r => Assert.Equal(ImageStatus.Normal, r.Status));
    }

    [Fact]
    public async Task 無効フォルダと未登録フォルダは拒否される()
    {
        var inactive = new SyncFolder
        {
            Id = IdGenerator.NewId(),
            Name = "inactive",
            Path = _root,
            IsActive = false,
        };
        Assert.True((await _db.Folders.AddAsync(inactive)).IsSuccess);

        var result = await _scan.ScanAsync(inactive.Id, null, TestContext.Current.CancellationToken);
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.ValidationError, result.Error);
        Assert.Null((await _db.Folders.GetByIdAsync(inactive.Id))!.LastScan); // スキャン不実施

        var notFound = await _scan.ScanAsync("no-such-folder", null, TestContext.Current.CancellationToken);
        Assert.Equal(ErrorCode.NotFound, notFound.Error);
    }

    [Fact]
    public async Task サブフォルダ除外設定では直下のみスキャンする()
    {
        WriteFile("top.jpg", "a");
        WriteFile("sub/nested.jpg", "b");
        var folder = await RegisterFolderAsync(includeSubfolders: false);

        var summary = await ScanOkAsync(folder.Id);

        Assert.Equal(1, summary.Added);
        var row = Assert.Single(await _db.Images.GetByFolderAsync(folder.Id));
        Assert.Equal("top.jpg", row.RelativePath);
    }

    /// <summary>同期的に呼び出す IProgress(Progress&lt;T&gt; の SynchronizationContext 依存を避ける)。</summary>
    private sealed class SyncProgress : IProgress<int>
    {
        private readonly Action<int> _onReport;

        public SyncProgress(Action<int> onReport)
        {
            _onReport = onReport;
        }

        public void Report(int value) => _onReport(value);
    }

    private sealed class SyncProgress<T> : IProgress<T>
    {
        private readonly Action<T> _onReport;

        public SyncProgress(Action<T> onReport)
        {
            _onReport = onReport;
        }

        public void Report(T value) => _onReport(value);
    }

    private sealed class CountingImageRepository : IImageRepository
    {
        private readonly IImageRepository _inner;
        private readonly bool _completeReadsSynchronously;
        private readonly ManualResetEventSlim? _batchEntered;
        private readonly ManualResetEventSlim? _releaseBatch;
        private readonly bool _failBatch;

        public CountingImageRepository(
            IImageRepository inner,
            bool completeReadsSynchronously = false,
            ManualResetEventSlim? batchEntered = null,
            ManualResetEventSlim? releaseBatch = null,
            bool failBatch = false)
        {
            _inner = inner;
            _completeReadsSynchronously = completeReadsSynchronously;
            _batchEntered = batchEntered;
            _releaseBatch = releaseBatch;
            _failBatch = failBatch;
        }

        public int SingleAddCalls { get; private set; }

        public int ScanBatchCalls { get; private set; }

        public int MaxScanBatchCount { get; private set; }

        public Task AddAsync(ImageRecord image)
        {
            SingleAddCalls++;
            return _inner.AddAsync(image);
        }

        public Task ApplyScanBatchAsync(ScanMutationBatch batch)
        {
            _batchEntered?.Set();
            if (_releaseBatch is not null && !_releaseBatch.Wait(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("scan batchの解放待ちがタイムアウトしました。");
            }

            if (_failBatch)
            {
                throw new InvalidOperationException("probe: batch commit failure");
            }

            ScanBatchCalls++;
            MaxScanBatchCount = Math.Max(MaxScanBatchCount, batch.Count);
            return _inner.ApplyScanBatchAsync(batch);
        }

        public Task<ImageRecord?> GetByIdAsync(string id) => _inner.GetByIdAsync(id);

        public Task<IReadOnlyList<ImageRecord>> GetByFolderAsync(string syncFolderId)
            => _completeReadsSynchronously
                ? Task.FromResult<IReadOnlyList<ImageRecord>>([])
                : _inner.GetByFolderAsync(syncFolderId);

        public Task<IReadOnlyList<ImageRecord>> GetAllNormalAsync() => _inner.GetAllNormalAsync();

        public Task<IReadOnlyDictionary<string, int>> GetNormalCountsByFolderAsync(CancellationToken ct = default)
            => _inner.GetNormalCountsByFolderAsync(ct);

        public Task<IReadOnlyList<ImageRecord>> GetNormalByFolderAsync(string syncFolderId, CancellationToken ct = default)
            => _inner.GetNormalByFolderAsync(syncFolderId, ct);

        public Task<IReadOnlyList<ImageRecord>> GetDeletedByFolderAsync(string syncFolderId, CancellationToken ct = default)
            => _inner.GetDeletedByFolderAsync(syncFolderId, ct);

        public Task<IReadOnlyList<ImageRecord>> GetPendingByFolderAsync(string syncFolderId, CancellationToken ct = default)
            => _inner.GetPendingByFolderAsync(syncFolderId, ct);

        public Task<bool> AdjudicatePendingAsync(string id, ImageStatus status)
            => _inner.AdjudicatePendingAsync(id, status);

        public Task<bool> ReplacePendingAsync(string oldId, ImageRecord replacement)
            => _inner.ReplacePendingAsync(oldId, replacement);

        public Task<int> CountByFolderAndStatusAsync(string syncFolderId, ImageStatus status, CancellationToken ct = default)
            => _inner.CountByFolderAndStatusAsync(syncFolderId, status, ct);

        public Task UpdateFileMetaAsync(string id, string hash, long fileSize, string modifiedDate)
            => _inner.UpdateFileMetaAsync(id, hash, fileSize, modifiedDate);

        public Task UpdateStatusAsync(string id, ImageStatus status) => _inner.UpdateStatusAsync(id, status);

        public Task RestoreStatusAsync(string id, ImageStatus status, PendingOrigin? origin)
            => _inner.RestoreStatusAsync(id, status, origin);

        public Task UpdateNotesAsync(string id, string? notes) => _inner.UpdateNotesAsync(id, notes);

        public Task DeleteAsync(string id) => _inner.DeleteAsync(id);

        public Task ApplyRelinkAsync(string missingImageId, string pendingImageId)
            => _inner.ApplyRelinkAsync(missingImageId, pendingImageId);

        public Task<IReadOnlyList<string>> GetDistinctNormalTagValuesAsync(string tagId)
            => _inner.GetDistinctNormalTagValuesAsync(tagId);
    }

    private sealed class ImmediateFolderRepository : ISyncFolderRepository
    {
        private readonly SyncFolder _folder;

        public ImmediateFolderRepository(SyncFolder folder)
        {
            _folder = folder;
        }

        public Task<Result> AddAsync(SyncFolder folder) => Task.FromResult(Result.Ok());

        public Task<SyncFolder?> GetByIdAsync(string id)
            => Task.FromResult<SyncFolder?>(id == _folder.Id ? _folder : null);

        public Task<SyncFolder?> GetByPathAsync(string path)
            => Task.FromResult<SyncFolder?>(PathNormalizer.Equals(path, _folder.Path) ? _folder : null);

        public Task<IReadOnlyList<SyncFolder>> GetAllAsync()
            => Task.FromResult<IReadOnlyList<SyncFolder>>([_folder]);

        public Task UpdateAsync(SyncFolder folder) => Task.CompletedTask;

        public Task DeleteAsync(string id) => Task.CompletedTask;

        public Task UpdateLastScanAsync(string id, string lastScan) => Task.CompletedTask;
    }
}
