using System.Reflection;
using System.Text;
using Dapper;
using ViewPrism2.App.Services;
using ViewPrism2.App.ViewModels;
using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services.Repair;
using ViewPrism2.Infrastructure.Database;
using ViewPrism2.Infrastructure.Scanning;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// ECO-140/CP-INTEGRITY-036: 統合裁定面(要確認の画像)の R5 先行プローブ。
/// 是正前 red で「旧 2 入口の並存・relink 選別の分散・統合裁定サービス不在」の症状を固定する
/// (是正後は本ファイルへ挙動ベクタが追記され、CP-INTEGRITY-036 の検査実体になる)。
/// </summary>
[Trait("cp", "CP-INTEGRITY-036")]
public sealed class CpIntegrityReviewTests : IDisposable
{
    private readonly TempDb _db = new();

    public void Dispose() => _db.Dispose();

    private static bool HasMethod(Type type, string name) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Any(m => string.Equals(m.Name, name, StringComparison.Ordinal));

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public void R5_入口統合_WindowServiceは統合裁定面のみを持ち旧2入口が残らない()
    {
        // ECO-140 gate① 案A: 入口は「要確認の画像…」1 本(REQ-102①)。旧 2 Window は撤去(dead 導線 0)
        var ws = typeof(WindowService);
        Assert.True(HasMethod(ws, "ShowIntegrityReviewAsync"), "統合裁定面の入口 ShowIntegrityReviewAsync が存在しない");
        Assert.False(HasMethod(ws, "ShowPendingReviewAsync"), "旧入口 ShowPendingReviewAsync が残存(置換されていない)");
        Assert.False(HasMethod(ws, "ShowRepairAsync"), "旧入口 ShowRepairAsync が残存(置換されていない)");
    }

    [Fact]
    public void R5_relink一本化_選別はE_RELINK_007が所有しPendingReviewServiceに残らない()
    {
        // gate① 裁定③: 選別+確定は E-RELINK-007(RelinkService)の API に一本化(REQ-102⑥)
        Assert.True(
            HasMethod(typeof(RelinkService), "SelectUniquelyRelinkable"),
            "高信頼一意組の選別が E-RELINK-007(RelinkService)に一本化されていない");
        Assert.False(
            HasMethod(typeof(PendingReviewService), "SelectUniquelyRelinkable"),
            "選別ロジックが PendingReviewService 側に残存(2 系統の分散が解消されていない)");
        var integritySource = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "ViewPrism2.Core", "Services", "Repair", "IntegrityReviewService.cs"));
        var classifyStart = integritySource.IndexOf("public static IReadOnlyList<IntegrityReviewEvent> Classify",
            StringComparison.Ordinal);
        var loadStart = integritySource.IndexOf("public async Task<IntegrityReviewSnapshot> LoadAsync",
            classifyStart, StringComparison.Ordinal);
        var classify = integritySource[classifyStart..loadStart];
        Assert.DoesNotContain(".Hash", classify, StringComparison.Ordinal);
    }

    [Fact]
    public void R5_統合裁定サービスがCoreに存在する()
    {
        // 事象分類(REQ-102③)+reappeared 裁定時 hash 確認(REQ-103)の宿り先
        var core = typeof(PendingReviewService).Assembly;
        var type = core.GetTypes().FirstOrDefault(t =>
            t.IsPublic && t.Name.Contains("IntegrityReview", StringComparison.Ordinal));
        Assert.True(type is not null, "統合裁定サービス(IntegrityReview*)が Core に存在しない");
    }

    [Fact]
    public void 事象分類は移動と再出現一致を自動_曖昧組と各由来を個別_missing単独を見つからないへ分ける()
    {
        var folderId = IdGenerator.NewId();
        var movedMissing = Row(folderId, "moved-old.jpg", ImageStatus.Missing);
        var ambiguousMissing = Row(folderId, "ambiguous-old.jpg", ImageStatus.Missing);
        var missingOnly = Row(folderId, "missing-only.jpg", ImageStatus.Missing);
        var moved = Row(
            folderId, "moved-new.jpg", ImageStatus.Pending, PendingOrigin.New, movedMissing.Id);
        var ambiguous1 = Row(
            folderId, "ambiguous-1.jpg", ImageStatus.Pending, PendingOrigin.New, ambiguousMissing.Id);
        var ambiguous2 = Row(
            folderId, "ambiguous-2.jpg", ImageStatus.Pending, PendingOrigin.New, ambiguousMissing.Id);
        var changed = Row(folderId, "changed.jpg", ImageStatus.Pending, PendingOrigin.Changed);
        var newOnly = Row(folderId, "new.jpg", ImageStatus.Pending, PendingOrigin.New);
        var restored = Row(folderId, "restored.jpg", ImageStatus.Pending, PendingOrigin.Restored);
        var reappearedMatch = Row(folderId, "returned.jpg", ImageStatus.Pending, PendingOrigin.Reappeared);
        var reappearedMismatch = Row(folderId, "changed-return.jpg", ImageStatus.Pending, PendingOrigin.Reappeared);
        var reappearedFailed = Row(folderId, "unreadable-return.jpg", ImageStatus.Pending, PendingOrigin.Reappeared);

        var events = IntegrityReviewService.Classify(
            [
                movedMissing, ambiguousMissing, missingOnly, moved, ambiguous1, ambiguous2,
                changed, newOnly, restored, reappearedMatch, reappearedMismatch, reappearedFailed,
            ],
            [moved.Id],
            [moved.Id, ambiguous1.Id, ambiguous2.Id],
            new Dictionary<string, IntegrityReviewHashOutcome>(StringComparer.Ordinal)
            {
                [reappearedMatch.Id] = IntegrityReviewHashOutcome.Match,
                [reappearedMismatch.Id] = IntegrityReviewHashOutcome.Mismatch,
                [reappearedFailed.Id] = IntegrityReviewHashOutcome.Failed,
            });

        Assert.Equal(10, events.Count);
        Assert.Equal(2, events.Count(e => e.Group == IntegrityReviewGroup.Automatic));
        Assert.Equal(7, events.Count(e => e.Group == IntegrityReviewGroup.Individual));
        Assert.Single(events, e => e.Group == IntegrityReviewGroup.Missing);
        Assert.Contains(events, e => e.Kind == IntegrityReviewKind.Moved && e.Counterpart?.Id == movedMissing.Id);
        Assert.Contains(events, e => e.Kind == IntegrityReviewKind.Reappeared
                                     && e.HashOutcome == IntegrityReviewHashOutcome.Match
                                     && e.Group == IntegrityReviewGroup.Automatic);
        Assert.All(
            events.Where(e => e.Primary.Id is var id && (id == ambiguous1.Id || id == ambiguous2.Id)),
            e =>
            {
                Assert.Equal(IntegrityReviewKind.Moved, e.Kind);
                Assert.Equal(IntegrityReviewGroup.Individual, e.Group);
                Assert.Equal(ambiguousMissing.Id, e.Counterpart?.Id);
            });
        Assert.DoesNotContain(events, e => e.Primary.Id == ambiguousMissing.Id);
        Assert.Contains(events, e => e.Primary.Id == missingOnly.Id && e.Group == IntegrityReviewGroup.Missing);
    }

    [Fact]
    public async Task reappeared_hash確認は一致だけ自動対象にし不一致と読取失敗を個別へ残してDBを書かない()
    {
        var root = Path.Combine(Path.GetTempPath(), "ViewPrism2.Tests", Guid.NewGuid().ToString("D"));
        Directory.CreateDirectory(root);
        try
        {
            var folder = await AddFolderAsync(root);
            var matched = await AddFileAndRowAsync(folder, "matched.jpg", "matched", new string('a', 64));
            var mismatched = await AddFileAndRowAsync(folder, "mismatched.jpg", "changed", new string('b', 64));
            var failed = await AddFileAndRowAsync(folder, "failed.jpg", "unreadable", new string('c', 64));
            var before = (await _db.Images.GetByFolderAsync(folder.Id))
                .OrderBy(r => r.Id, StringComparer.Ordinal).ToList();
            var hashes = new StubHashProvider(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                [Path.Combine(root, matched.RelativePath)] = matched.Hash,
                [Path.Combine(root, mismatched.RelativePath)] = new string('d', 64),
                [Path.Combine(root, failed.RelativePath)] = null,
            });
            var service = new IntegrityReviewService(
                _db.Images, new RelinkService(_db.Images, _db.Tags), hashes);

            var snapshot = await service.LoadAsync(folder, TestContext.Current.CancellationToken);

            Assert.True(snapshot.HashCheckComplete);
            Assert.Equal(3, hashes.Paths.Count);
            Assert.Contains(snapshot.Events, e => e.Primary.Id == matched.Id
                                                  && e.Group == IntegrityReviewGroup.Automatic
                                                  && e.HashOutcome == IntegrityReviewHashOutcome.Match);
            Assert.Contains(snapshot.Events, e => e.Primary.Id == mismatched.Id
                                                  && e.Group == IntegrityReviewGroup.Individual
                                                  && e.HashOutcome == IntegrityReviewHashOutcome.Mismatch);
            Assert.Contains(snapshot.Events, e => e.Primary.Id == failed.Id
                                                  && e.Group == IntegrityReviewGroup.Individual
                                                  && e.HashOutcome == IntegrityReviewHashOutcome.Failed);
            var after = (await _db.Images.GetByFolderAsync(folder.Id))
                .OrderBy(r => r.Id, StringComparer.Ordinal).ToList();
            Assert.Equal(before, after);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task IR7_hash確認待ちでも非自動の個別裁定行を先に公開する()
    {
        var root = Path.Combine(Path.GetTempPath(), "ViewPrism2.Tests", Guid.NewGuid().ToString("D"));
        Directory.CreateDirectory(root);
        try
        {
            var folder = await AddFolderAsync(root);
            var changed = await AddRowAsync(Row(
                folder.Id, "changed.jpg", ImageStatus.Pending, PendingOrigin.Changed));
            var reappeared = await AddRowAsync(Row(
                folder.Id, "returned.jpg", ImageStatus.Pending, PendingOrigin.Reappeared));
            var hashes = new BlockingHashProvider(reappeared.Hash);
            var interim = new CapturingProgress<IntegrityReviewSnapshot>();
            var service = new IntegrityReviewService(_db.Images, Relink(), hashes);

            var loading = service.LoadAsync(
                folder,
                TestContext.Current.CancellationToken,
                interimSnapshots: interim);
            await hashes.Started.Task.WaitAsync(TestContext.Current.CancellationToken);

            var snapshot = Assert.IsType<IntegrityReviewSnapshot>(interim.Last);
            Assert.False(snapshot.HashCheckComplete);
            Assert.Contains(snapshot.Events, e =>
                e.Primary.Id == changed.Id && e.Group == IntegrityReviewGroup.Individual);
            Assert.Contains(snapshot.Events, e =>
                e.Primary.Id == reappeared.Id
                && e.Group == IntegrityReviewGroup.Individual
                && e.HashOutcome == IntegrityReviewHashOutcome.Pending);

            hashes.Release.TrySetResult();
            var completed = await loading;
            Assert.True(completed.HashCheckComplete);
            Assert.Contains(completed.Events, e =>
                e.Primary.Id == reappeared.Id && e.Group == IntegrityReviewGroup.Automatic);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task IR7_hash確認完了までは自動裁定ボタンを提示しない()
    {
        var root = Path.Combine(Path.GetTempPath(), "ViewPrism2.Tests", Guid.NewGuid().ToString("D"));
        Directory.CreateDirectory(root);
        try
        {
            var folder = await AddFolderAsync(root);
            var missing = await AddRowAsync(Row(folder.Id, "old.jpg", ImageStatus.Missing));
            await AddRowAsync(Row(
                folder.Id, "moved.jpg", ImageStatus.Pending, PendingOrigin.New, missing.Id));
            var reappeared = await AddRowAsync(Row(
                folder.Id, "returned.jpg", ImageStatus.Pending, PendingOrigin.Reappeared));
            var hashes = new BlockingHashProvider(reappeared.Hash);
            var relink = Relink();
            var vm = new IntegrityReviewViewModel(
                new IntegrityReviewService(_db.Images, relink, hashes),
                new PendingReviewService(_db.Images),
                _db.Images,
                _db.Tags,
                relink,
                new TrashService(_db.Images, _db.Folders, new AlwaysMissingProbe()),
                TestLoc.Ja(),
                new ConfirmingWindows(),
                folder);

            var loading = vm.LoadAsync();
            await hashes.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
            await WaitUntilAsync(() => vm.HasAutomaticItems);

            Assert.True(vm.IsHashChecking);
            Assert.False(vm.HasAutomatic);
            Assert.True(vm.HasAutomaticItems);

            hashes.Release.TrySetResult();
            await loading;
            Assert.False(vm.IsHashChecking);
            Assert.True(vm.HasAutomatic);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task hash確認完了後の個別裁定は残存reappearedを再hashしない()
    {
        var root = Path.Combine(Path.GetTempPath(), "ViewPrism2.Tests", Guid.NewGuid().ToString("D"));
        Directory.CreateDirectory(root);
        try
        {
            var folder = await AddFolderAsync(root);
            var hashes = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < 3; i++)
            {
                var row = await AddFileAndRowAsync(
                    folder,
                    $"returned-{i}.jpg",
                    $"content-{i}",
                    new string((char)('a' + i), 64));
                hashes[Path.Combine(root, row.RelativePath)] = row.Hash;
            }

            var provider = new CountingHashProvider(hashes);
            var relink = Relink();
            var vm = new IntegrityReviewViewModel(
                new IntegrityReviewService(_db.Images, relink, provider),
                new PendingReviewService(_db.Images),
                _db.Images,
                _db.Tags,
                relink,
                new TrashService(_db.Images, _db.Folders, new AlwaysMissingProbe()),
                TestLoc.Ja(),
                new ConfirmingWindows(),
                folder);
            await vm.LoadAsync();
            Assert.Equal(3, provider.CallCount);
            vm.Selected = vm.Items[0];

            await vm.AcceptCommand.ExecuteAsync(null);

            Assert.Equal(3, provider.CallCount);
            Assert.Equal(2, vm.TotalCount);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task 曖昧な移動候補を1件受入後は残候補を再選別しmissingを二重計上しない()
    {
        var folder = await AddFolderAsync(@"C:\fixture");
        var hash = new string('7', 64);
        var missing = await AddRowAsync(Row(
            folder.Id, "old.jpg", ImageStatus.Missing, hash: hash));
        var first = await AddRowAsync(Row(
            folder.Id, "first.jpg", ImageStatus.Pending, PendingOrigin.New, missing.Id, hash));
        var second = await AddRowAsync(Row(
            folder.Id, "second.jpg", ImageStatus.Pending, PendingOrigin.New, missing.Id, hash));
        var vm = CreateViewModel(folder);
        await vm.LoadAsync();
        Assert.Equal(2, vm.IndividualCount);
        vm.Selected = Assert.Single(vm.Items, item => item.Record.Id == first.Id);

        await vm.AcceptCommand.ExecuteAsync(null);

        var remaining = Assert.Single(vm.Items);
        Assert.Equal(second.Id, remaining.Record.Id);
        Assert.Equal(IntegrityReviewGroup.Automatic, remaining.Event.Group);
        Assert.Equal(0, vm.MissingCount);
        Assert.Equal(1, vm.AutomaticCount);
    }

    [Fact]
    public async Task missingからpending候補へ手動relink後は消費した両事象を残さない()
    {
        var folder = await AddFolderAsync(@"C:\fixture");
        var missing = await AddRowAsync(Row(folder.Id, "missing.jpg", ImageStatus.Missing));
        var candidate = await AddRowAsync(Row(
            folder.Id, "candidate.jpg", ImageStatus.Pending, PendingOrigin.Changed));
        var vm = CreateViewModel(folder);
        await vm.LoadAsync();
        vm.Selected = Assert.Single(vm.Items, item => item.Record.Id == missing.Id);
        vm.NameContainsInput = "candidate";
        vm.ExtensionInput = null;
        vm.MtimeFromInput = null;
        vm.SizeToleranceInput = null;
        await vm.SearchCandidatesCommand.ExecuteAsync(null);
        vm.SelectedCandidate = Assert.Single(
            vm.Candidates, item => item.Candidate.ImageId == candidate.Id);

        await vm.CommitCandidateCommand.ExecuteAsync(null);

        Assert.Empty(vm.Items);
        Assert.Equal(0, vm.TotalCount);
        Assert.NotNull(await _db.Images.GetByIdAsync(missing.Id));
        Assert.Null(await _db.Images.GetByIdAsync(candidate.Id));
    }

    [Fact]
    public async Task 実ファイルhash_providerはSHA256を読み取り専用で再計算する()
    {
        var root = Path.Combine(Path.GetTempPath(), "ViewPrism2.Tests", Guid.NewGuid().ToString("D"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "reappeared.bin");
            await File.WriteAllBytesAsync(
                path,
                Encoding.UTF8.GetBytes("integrity-review"),
                TestContext.Current.CancellationToken);
            var before = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);

            var actual = await new IntegrityReviewFileHashProvider().ComputeSha256Async(
                path, TestContext.Current.CancellationToken);

            Assert.Equal("3f2ea5ce080e7e46a600715a950fede5d4bfa3bf4c39ef9265ccd55e87e6a856", actual);
            Assert.Equal(before, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task migration011はbaseline列をnullableで追加し既存相当行をNULLのまま保つ()
    {
        var migration = Assert.Single(
            DatabaseSchema.Migrations, m => m.Id == "011-pending-baseline-hash");
        Assert.Contains(
            "ALTER TABLE images ADD COLUMN pending_baseline_hash TEXT NULL",
            migration.Sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "CREATE INDEX idx_images_candidate_link ON images(sync_folder_id, status, candidate_link_id)",
            migration.Sql,
            StringComparison.Ordinal);

        var folder = await AddFolderAsync(@"C:\fixture");
        var existing = await AddRowAsync(Row(
            folder.Id, "existing.jpg", ImageStatus.Pending, PendingOrigin.Reappeared));
        var baseline = await PendingBaselineHashAsync(existing.Id);

        Assert.Null(baseline);
    }

    [Fact]
    public async Task 実スキャン_missingからUpdateMetaAndPendでpending化すると上書き前hashをbaselineへ保全する()
    {
        var root = Path.Combine(Path.GetTempPath(), "ViewPrism2.Tests", Guid.NewGuid().ToString("D"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "returned.jpg");
            await File.WriteAllTextAsync(
                path, "different-current-content", TestContext.Current.CancellationToken);
            var modified = new DateTime(2026, 7, 24, 1, 2, 3, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(path, modified);
            var folder = await AddFolderAsync(root, "2026-07-23T00:00:00.000Z");
            var oldHash = new string('1', 64);
            var row = await AddRowAsync(Row(
                folder.Id, "returned.jpg", ImageStatus.Missing, hash: oldHash) with
            {
                FileSize = 1,
                ModifiedDate = "2026-07-20T00:00:00.000Z",
            });
            var scan = new ScanService(_db.Folders, _db.Images, _db.Clock);

            var result = await scan.ScanAsync(
                folder.Id, null, TestContext.Current.CancellationToken);

            Assert.True(result.IsSuccess, result.Message);
            var after = Assert.IsType<ImageRecord>(await _db.Images.GetByIdAsync(row.Id));
            Assert.Equal(ImageStatus.Pending, after.Status);
            Assert.Equal(PendingOrigin.Reappeared, after.PendingOrigin);
            Assert.NotEqual(oldHash, after.Hash);
            Assert.Equal(oldHash, await PendingBaselineHashAsync(row.Id));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task 実スキャン_normalからUpdateMetaAndPendでchanged化しても上書き前hashをbaselineへ保全する()
    {
        var root = Path.Combine(Path.GetTempPath(), "ViewPrism2.Tests", Guid.NewGuid().ToString("D"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "changed.jpg");
            await File.WriteAllTextAsync(
                path, "different-current-content", TestContext.Current.CancellationToken);
            File.SetLastWriteTimeUtc(
                path, new DateTime(2026, 7, 24, 3, 4, 5, DateTimeKind.Utc));
            var folder = await AddFolderAsync(root, "2026-07-23T00:00:00.000Z");
            var oldHash = new string('5', 64);
            var row = await AddRowAsync(Row(
                folder.Id, "changed.jpg", ImageStatus.Normal, hash: oldHash) with
            {
                FileSize = 1,
                ModifiedDate = "2026-07-20T00:00:00.000Z",
            });
            var scan = new ScanService(_db.Folders, _db.Images, _db.Clock);

            var result = await scan.ScanAsync(
                folder.Id, null, TestContext.Current.CancellationToken);

            Assert.True(result.IsSuccess, result.Message);
            var after = Assert.IsType<ImageRecord>(await _db.Images.GetByIdAsync(row.Id));
            Assert.Equal(ImageStatus.Pending, after.Status);
            Assert.Equal(PendingOrigin.Changed, after.PendingOrigin);
            Assert.NotEqual(oldHash, after.Hash);
            Assert.Equal(oldHash, await PendingBaselineHashAsync(row.Id));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task 実スキャン_missingからPendInPlaceでpending化するとbaselineを保全しない()
    {
        var root = Path.Combine(Path.GetTempPath(), "ViewPrism2.Tests", Guid.NewGuid().ToString("D"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "returned.jpg");
            await File.WriteAllTextAsync(path, "same-metadata", TestContext.Current.CancellationToken);
            var modified = new DateTime(2026, 7, 24, 2, 3, 4, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(path, modified);
            var facts = new FileInfo(path);
            var folder = await AddFolderAsync(root, "2026-07-23T00:00:00.000Z");
            var oldHash = new string('2', 64);
            var row = await AddRowAsync(Row(
                folder.Id, "returned.jpg", ImageStatus.Missing, hash: oldHash) with
            {
                FileSize = facts.Length,
                ModifiedDate = IsoTimestamp.Format(facts.LastWriteTimeUtc),
            });
            var scan = new ScanService(_db.Folders, _db.Images, _db.Clock);

            var result = await scan.ScanAsync(
                folder.Id, null, TestContext.Current.CancellationToken);

            Assert.True(result.IsSuccess, result.Message);
            var after = Assert.IsType<ImageRecord>(await _db.Images.GetByIdAsync(row.Id));
            Assert.Equal(ImageStatus.Pending, after.Status);
            Assert.Equal(PendingOrigin.Reappeared, after.PendingOrigin);
            Assert.Equal(oldHash, after.Hash);
            Assert.Null(await PendingBaselineHashAsync(row.Id));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task スキャン上書き済み再出現は現hashでなくbaselineと比較して別内容を個別へ回す()
    {
        var root = Path.Combine(Path.GetTempPath(), "ViewPrism2.Tests", Guid.NewGuid().ToString("D"));
        Directory.CreateDirectory(root);
        try
        {
            var folder = await AddFolderAsync(root);
            var currentHash = new string('4', 64);
            var baselineHash = new string('3', 64);
            var row = await AddFileAndRowAsync(
                folder, "returned.jpg", "current-content", currentHash);
            await SetPendingBaselineHashAsync(row.Id, baselineHash);
            var service = new IntegrityReviewService(
                _db.Images,
                Relink(),
                new StubHashProvider(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    [Path.Combine(root, row.RelativePath)] = currentHash,
                }));

            var snapshot = await service.LoadAsync(folder, TestContext.Current.CancellationToken);

            Assert.Contains(snapshot.Events, e =>
                e.Primary.Id == row.Id
                && e.Group == IntegrityReviewGroup.Individual
                && e.HashOutcome == IntegrityReviewHashOutcome.Mismatch);
            Assert.DoesNotContain(snapshot.AutomaticEvents, e => e.Primary.Id == row.Id);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task 裁定確定T13_T14_T15とrelinkはbaselineをクリアする()
    {
        var folder = await AddFolderAsync(@"C:\fixture");
        var accept = await AddRowAsync(Row(
            folder.Id, "accept.jpg", ImageStatus.Pending, PendingOrigin.Reappeared));
        var replace = await AddRowAsync(Row(
            folder.Id, "replace.jpg", ImageStatus.Pending, PendingOrigin.Changed));
        var delete = await AddRowAsync(Row(
            folder.Id, "delete.jpg", ImageStatus.Pending, PendingOrigin.Changed));
        foreach (var row in new[] { accept, replace, delete })
        {
            await SetPendingBaselineHashAsync(row.Id, new string('9', 64));
        }

        var pending = new PendingReviewService(_db.Images);
        Assert.True((await pending.AcceptAsync(accept.Id)).IsSuccess);
        var replaced = await pending.TreatAsNewAsync(replace.Id);
        Assert.True(replaced.IsSuccess);
        Assert.True((await pending.DeleteAsync(delete.Id)).IsSuccess);

        Assert.Null(await PendingBaselineHashAsync(accept.Id));
        Assert.Null(await PendingBaselineHashAsync(replaced.Value!));
        Assert.Null(await PendingBaselineHashAsync(delete.Id));

        var missing = await AddRowAsync(Row(folder.Id, "old.jpg", ImageStatus.Missing));
        var moved = await AddRowAsync(Row(
            folder.Id, "moved.jpg", ImageStatus.Pending, PendingOrigin.New, missing.Id));
        await SetPendingBaselineHashAsync(missing.Id, new string('8', 64));
        await SetPendingBaselineHashAsync(moved.Id, new string('7', 64));

        var relinked = await Relink().CommitRelinkAsync(missing.Id, moved.Id);

        Assert.True(relinked.IsSuccess, relinked.Message);
        Assert.Null(await PendingBaselineHashAsync(missing.Id));
        Assert.Null(await _db.Images.GetByIdAsync(moved.Id));
    }

    [Fact]
    public async Task pending_missingの保持タグ件数をnormal限定APIで欠落させない()
    {
        var folder = await AddFolderAsync(@"C:\fixture");
        var pending = await AddRowAsync(Row(
            folder.Id, "pending.jpg", ImageStatus.Pending, PendingOrigin.Changed));
        var missing = await AddRowAsync(Row(folder.Id, "missing.jpg", ImageStatus.Missing));
        var tag = new Tag { Id = IdGenerator.NewId(), Name = "kept", Type = TagType.Simple };
        await _db.Tags.AddAsync(tag);
        await _db.Tags.UpsertImageTagAsync(new ImageTag { ImageId = pending.Id, TagId = tag.Id });
        await _db.Tags.UpsertImageTagAsync(new ImageTag { ImageId = missing.Id, TagId = tag.Id });
        var vm = CreateViewModel(folder);

        await vm.LoadAsync();

        vm.Selected = Assert.Single(vm.Items, item => item.Record.Id == pending.Id);
        Assert.True(vm.ShowTagRow);
        Assert.Contains("1", vm.TagCountText, StringComparison.Ordinal);
        vm.Selected = Assert.Single(vm.Items, item => item.Record.Id == missing.Id);
        Assert.True(vm.ShowTagRow);
        Assert.Contains("1", vm.TagCountText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 言語切替で由来チップと候補日時が権威値から即時再解決される()
    {
        var root = Path.Combine(Path.GetTempPath(), "ViewPrism2.Tests", Guid.NewGuid().ToString("D"));
        Directory.CreateDirectory(root);
        try
        {
            var folder = await AddFolderAsync(root);
            var changed = await AddRowAsync(Row(
                folder.Id, "changed.jpg", ImageStatus.Pending, PendingOrigin.Changed));
            var missing = await AddRowAsync(Row(folder.Id, "missing.jpg", ImageStatus.Missing));
            var candidate = await AddRowAsync(Row(folder.Id, "candidate.jpg", ImageStatus.Normal));
            var loc = TestLoc.Ja();
            var relink = Relink();
            var vm = new IntegrityReviewViewModel(
                new IntegrityReviewService(
                    _db.Images,
                    relink,
                    new StubHashProvider(new Dictionary<string, string?>())),
                new PendingReviewService(_db.Images),
                _db.Images,
                _db.Tags,
                relink,
                new TrashService(_db.Images, _db.Folders, new AlwaysMissingProbe()),
                loc,
                new ConfirmingWindows(),
                folder);
            await vm.LoadAsync();
            var changedItem = Assert.Single(vm.Items, item => item.Record.Id == changed.Id);
            Assert.Equal("内容変更", changedItem.OriginLabel);
            vm.Selected = Assert.Single(vm.Items, item => item.Record.Id == missing.Id);
            vm.NameContainsInput = "candidate";
            vm.ExtensionInput = null;
            vm.MtimeFromInput = null;
            vm.SizeToleranceInput = null;
            await vm.SearchCandidatesCommand.ExecuteAsync(null);
            var candidateItem = Assert.Single(
                vm.Candidates, item => item.Candidate.ImageId == candidate.Id);
            var jaModified = candidateItem.ModifiedText;

            loc.SetLocale("en");

            Assert.Equal("Changed", changedItem.OriginLabel);
            Assert.NotEqual(jaModified, candidateItem.ModifiedText);
            Assert.Equal(
                LocaleFormats.FormatTimestamp(candidate.ModifiedDate, "en"),
                candidateItem.ModifiedText);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void hashProviderは現在ファイルの読取り中もcancellationTokenを観測する()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot(),
            "src",
            "ViewPrism2.Infrastructure",
            "Scanning",
            "IntegrityReviewFileHashProvider.cs"));

        Assert.Contains("HashDataAsync(stream, ct)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.Run", source, StringComparison.Ordinal);
    }

    [Fact]
    public void 統合裁定のDB母集合_件数_タグ読取はDapperCommandへcancellationTokenを伝播する()
    {
        var imageSource = File.ReadAllText(Path.Combine(
            RepoRoot(),
            "src",
            "ViewPrism2.Infrastructure",
            "Database",
            "ImageRepository.cs"));
        var integrityStart = imageSource.IndexOf(
            "public Task<IReadOnlyList<ImageRecord>> GetIntegrityReviewByFolderAsync",
            StringComparison.Ordinal);
        var integrityEnd = imageSource.IndexOf(
            "public Task<IReadOnlyList<ImageRecord>> GetByIdsAsync",
            integrityStart,
            StringComparison.Ordinal);
        var integrityQueries = imageSource[integrityStart..integrityEnd];
        Assert.Contains("CommandDefinition", integrityQueries, StringComparison.Ordinal);
        Assert.Equal(
            2,
            integrityQueries.Split(
                "cancellationToken: ct",
                StringSplitOptions.None).Length - 1);

        var tagSource = File.ReadAllText(Path.Combine(
            RepoRoot(),
            "src",
            "ViewPrism2.Infrastructure",
            "Database",
            "TagRepository.cs"));
        var tagsStart = tagSource.IndexOf(
            "public Task<IReadOnlyList<ImageTag>> GetIntegrityReviewImageTagsByFolderAsync",
            StringComparison.Ordinal);
        var tagsEnd = tagSource.IndexOf(
            "public Task<IReadOnlyDictionary<string, int>> GetUsageCountsAsync",
            tagsStart,
            StringComparison.Ordinal);
        var tagQuery = tagSource[tagsStart..tagsEnd];
        Assert.Contains("CommandDefinition", tagQuery, StringComparison.Ordinal);
        Assert.Contains("cancellationToken: ct", tagQuery, StringComparison.Ordinal);
    }

    [Fact]
    public async Task missing候補検索は条件ゼロなら非実行で全件候補化しない()
    {
        var folder = await AddFolderAsync(@"C:\fixture");
        var missing = await AddRowAsync(Row(folder.Id, "missing.jpg", ImageStatus.Missing));
        await AddRowAsync(Row(folder.Id, "unrelated.jpg", ImageStatus.Normal));
        var vm = CreateViewModel(folder);
        await vm.LoadAsync();
        vm.Selected = Assert.Single(vm.Items, item => item.Record.Id == missing.Id);
        vm.NameContainsInput = null;
        vm.ExtensionInput = null;
        vm.MtimeFromInput = null;
        vm.SizeToleranceInput = null;

        Assert.False(vm.SearchCandidatesCommand.CanExecute(null));
        await vm.SearchCandidatesCommand.ExecuteAsync(null);
        Assert.Empty(vm.Candidates);
        Assert.False(vm.ShowNoCandidates);

        vm.NameContainsInput = "not-found";
        Assert.True(vm.SearchCandidatesCommand.CanExecute(null));
        await vm.SearchCandidatesCommand.ExecuteAsync(null);
        Assert.True(vm.ShowNoCandidates);
    }

    [Fact]
    public void 統合裁定のrelink選別と混在適用はタグ確認Nplus1を持たない()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot(),
            "src",
            "ViewPrism2.Infrastructure",
            "Scanning",
            "RelinkService.cs"));
        var selectionStart = source.IndexOf(
            "public async Task<RelinkSelection> GetRelinkSelectionAsync",
            StringComparison.Ordinal);
        var selectionEnd = source.IndexOf(
            "public async Task<IReadOnlyList<RelinkCandidate>> GetCandidatesAsync",
            selectionStart,
            StringComparison.Ordinal);
        var selection = source[selectionStart..selectionEnd];
        var batchStart = source.IndexOf(
            "public async Task<Result<int>> ApplyIntegrityReviewBatchAsync",
            StringComparison.Ordinal);
        var batch = source[batchStart..];

        Assert.DoesNotContain("GetImageTagsAsync", selection, StringComparison.Ordinal);
        Assert.Contains("GetIntegrityReviewImageTagsByFolderAsync", selection, StringComparison.Ordinal);
        Assert.DoesNotContain("GetImageTagsAsync", batch, StringComparison.Ordinal);
        Assert.Contains("ApplyIntegrityReviewBatchAsync", batch, StringComparison.Ordinal);
    }

    [Fact]
    public void missing候補検索は選択変更後の旧結果を公開しない世代ガードを持つ()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot(),
            "src",
            "ViewPrism2.App",
            "ViewModels",
            "IntegrityReviewViewModel.cs"));
        var searchStart = source.IndexOf(
            "private async Task SearchCandidatesAsync",
            StringComparison.Ordinal);
        var searchEnd = source.IndexOf(
            "private async Task CommitCandidateAsync",
            searchStart,
            StringComparison.Ordinal);
        var search = source[searchStart..searchEnd];

        Assert.Contains("_candidateSearchVersion", search, StringComparison.Ordinal);
        Assert.Contains("selectedId", search, StringComparison.Ordinal);
        Assert.Contains("Selected?.Record.Id", search, StringComparison.Ordinal);
        Assert.Contains("return;", search, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task 件数は母集合_callout_確認一覧_適用で一致しキャンセルはDB無変更(bool confirm)
    {
        var root = Path.Combine(Path.GetTempPath(), "ViewPrism2.Tests", Guid.NewGuid().ToString("D"));
        Directory.CreateDirectory(root);
        try
        {
            var folder = await AddFolderAsync(root);
            var missing = await AddRowAsync(Row(folder.Id, "old.jpg", ImageStatus.Missing));
            var moved = await AddRowAsync(Row(
                folder.Id, "new.jpg", ImageStatus.Pending, PendingOrigin.New, missing.Id));
            var windows = new CapturingWindows(confirm);
            var relink = new RelinkService(_db.Images, _db.Tags);
            var vm = new IntegrityReviewViewModel(
                new IntegrityReviewService(
                    _db.Images,
                    relink,
                    new StubHashProvider(new Dictionary<string, string?>())),
                new PendingReviewService(_db.Images),
                _db.Images,
                _db.Tags,
                relink,
                new TrashService(_db.Images, _db.Folders, new AlwaysMissingProbe()),
                TestLoc.Ja(),
                windows,
                folder);
            await vm.LoadAsync();

            Assert.Equal(1, await _db.Images.CountIntegrityReviewEventsAsync(
                folder.Id, TestContext.Current.CancellationToken));
            Assert.Equal(1, vm.TotalCount);
            Assert.Equal(1, vm.AutomaticCount);
            Assert.Equal("自動裁定（1 件）", vm.AutoButtonLabel);
            Assert.False(vm.IsHashChecking);
            Assert.True(vm.AutoAdjudicateCommand.CanExecute(null));

            await vm.AutoAdjudicateCommand.ExecuteAsync(null);

            Assert.Equal(1, windows.LastConfirmationItems?.Count);
            if (confirm)
            {
                Assert.True(vm.Adjudicated);
                Assert.Equal(0, await _db.Images.CountIntegrityReviewEventsAsync(
                    folder.Id, TestContext.Current.CancellationToken));
                Assert.Equal(ImageStatus.Normal, (await _db.Images.GetByIdAsync(missing.Id))!.Status);
                Assert.Null(await _db.Images.GetByIdAsync(moved.Id));
            }
            else
            {
                Assert.False(vm.Adjudicated);
                Assert.Equal(ImageStatus.Missing, (await _db.Images.GetByIdAsync(missing.Id))!.Status);
                Assert.Equal(ImageStatus.Pending, (await _db.Images.GetByIdAsync(moved.Id))!.Status);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void 統合裁定母集合はproductionのDB境界でpending_missingに限定する()
    {
        var repositorySource = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "ViewPrism2.Infrastructure", "Database", "ImageRepository.cs"));
        var start = repositorySource.IndexOf(
            "GetIntegrityReviewByFolderAsync", StringComparison.Ordinal);
        var end = repositorySource.IndexOf(
            "CountIntegrityReviewEventsAsync", start, StringComparison.Ordinal);
        var method = repositorySource[start..end];

        Assert.Contains("status IN ('pending','missing')", method, StringComparison.Ordinal);
        Assert.DoesNotContain("GetByFolderAsync", method, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 混在一括適用は移動T4と再出現T13を同時確定しIDタグを保持する()
    {
        var root = Path.Combine(Path.GetTempPath(), "ViewPrism2.Tests", Guid.NewGuid().ToString("D"));
        Directory.CreateDirectory(root);
        try
        {
            var folder = await AddFolderAsync(root);
            var missing = await AddRowAsync(Row(folder.Id, "old.jpg", ImageStatus.Missing));
            var moved = await AddRowAsync(Row(
                folder.Id, "moved.jpg", ImageStatus.Pending, PendingOrigin.New, missing.Id));
            var reappeared = await AddFileAndRowAsync(
                folder, "returned.jpg", "same", new string('e', 64));
            var tag = new Tag { Id = IdGenerator.NewId(), Name = "kept", Type = TagType.Simple };
            await _db.Tags.AddAsync(tag);
            await _db.Tags.UpsertImageTagAsync(new ImageTag { ImageId = missing.Id, TagId = tag.Id });
            await _db.Tags.UpsertImageTagAsync(new ImageTag { ImageId = reappeared.Id, TagId = tag.Id });
            var service = new IntegrityReviewService(
                _db.Images,
                new RelinkService(_db.Images, _db.Tags),
                new StubHashProvider(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    [Path.Combine(root, reappeared.RelativePath)] = reappeared.Hash,
                }));
            var snapshot = await service.LoadAsync(folder, TestContext.Current.CancellationToken);
            Assert.Equal(2, snapshot.AutomaticEvents.Count);

            var result = await service.ApplyAutomaticAsync(snapshot.AutomaticEvents);

            Assert.True(result.IsSuccess, result.Message);
            Assert.Equal(2, result.Value);
            var relinked = Assert.IsType<ImageRecord>(await _db.Images.GetByIdAsync(missing.Id));
            Assert.Equal(ImageStatus.Normal, relinked.Status);
            Assert.Equal(moved.RelativePath, relinked.RelativePath);
            Assert.Null(await _db.Images.GetByIdAsync(moved.Id));
            Assert.Single(await _db.Tags.GetImageTagsAsync(missing.Id));
            var accepted = Assert.IsType<ImageRecord>(await _db.Images.GetByIdAsync(reappeared.Id));
            Assert.Equal(ImageStatus.Normal, accepted.Status);
            Assert.Single(await _db.Tags.GetImageTagsAsync(reappeared.Id));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task 混在一括適用は再出現1件がstaleなら有効な移動も含め全rollbackする()
    {
        var root = Path.Combine(Path.GetTempPath(), "ViewPrism2.Tests", Guid.NewGuid().ToString("D"));
        Directory.CreateDirectory(root);
        try
        {
            var folder = await AddFolderAsync(root);
            var missing = await AddRowAsync(Row(folder.Id, "old.jpg", ImageStatus.Missing));
            var moved = await AddRowAsync(Row(
                folder.Id, "moved.jpg", ImageStatus.Pending, PendingOrigin.New, missing.Id));
            var reappeared = await AddFileAndRowAsync(
                folder, "returned.jpg", "same", new string('f', 64));
            var service = new IntegrityReviewService(
                _db.Images,
                new RelinkService(_db.Images, _db.Tags),
                new StubHashProvider(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    [Path.Combine(root, reappeared.RelativePath)] = reappeared.Hash,
                }));
            var snapshot = await service.LoadAsync(folder, TestContext.Current.CancellationToken);
            await _db.Images.AdjudicatePendingAsync(reappeared.Id, ImageStatus.Normal);

            var result = await service.ApplyAutomaticAsync(snapshot.AutomaticEvents);

            Assert.False(result.IsSuccess);
            Assert.Equal(ImageStatus.Missing, (await _db.Images.GetByIdAsync(missing.Id))!.Status);
            Assert.Equal(ImageStatus.Pending, (await _db.Images.GetByIdAsync(moved.Id))!.Status);
            Assert.Equal(ImageStatus.Normal, (await _db.Images.GetByIdAsync(reappeared.Id))!.Status);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void hash再計算seamはScanJudgeとScanStagingへ依存しない()
    {
        var constructors = typeof(IntegrityReviewService).GetConstructors();
        Assert.Contains(constructors, c =>
            c.GetParameters().Any(p => p.ParameterType == typeof(IIntegrityReviewHashProvider)));

        var core = typeof(IntegrityReviewService).Assembly;
        var scanJudge = core.GetType("ViewPrism2.Core.Services.ScanJudge", throwOnError: true)!;
        Assert.DoesNotContain(scanJudge.GetConstructors().SelectMany(c => c.GetParameters()),
            p => p.ParameterType == typeof(IIntegrityReviewHashProvider));
        Assert.DoesNotContain(scanJudge.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static),
            m => m.GetParameters().Any(p => p.ParameterType == typeof(IIntegrityReviewHashProvider)));
        Assert.DoesNotContain(typeof(ScanStaging).GetConstructors().SelectMany(c => c.GetParameters()),
            p => p.ParameterType == typeof(IIntegrityReviewHashProvider));
    }

    [Fact]
    public async Task autoRepairable選別は候補ちょうど1件だけを返しタグ付き候補を除外する()
    {
        var folder = await AddFolderAsync(@"C:\fixture");
        var uniqueMissing = await AddRowAsync(Row(
            folder.Id, "unique-old.jpg", ImageStatus.Missing, hash: new string('a', 64)));
        var unique = await AddRowAsync(Row(
            folder.Id, "unique-new.jpg", ImageStatus.Pending, hash: new string('a', 64)));
        var ambiguousMissing = await AddRowAsync(Row(
            folder.Id, "ambiguous-old.jpg", ImageStatus.Missing, hash: new string('b', 64)));
        await AddRowAsync(Row(
            folder.Id, "ambiguous-a.jpg", ImageStatus.Pending, hash: new string('b', 64)));
        await AddRowAsync(Row(
            folder.Id, "ambiguous-b.jpg", ImageStatus.Normal, hash: new string('b', 64)));
        var taggedMissing = await AddRowAsync(Row(
            folder.Id, "tagged-old.jpg", ImageStatus.Missing, hash: new string('c', 64)));
        var tagged = await AddRowAsync(Row(
            folder.Id, "tagged-new.jpg", ImageStatus.Normal, hash: new string('c', 64)));
        var tag = new Tag { Id = IdGenerator.NewId(), Name = "unsafe", Type = TagType.Simple };
        await _db.Tags.AddAsync(tag);
        await _db.Tags.UpsertImageTagAsync(new ImageTag { ImageId = tagged.Id, TagId = tag.Id });

        var pairs = await new RelinkService(_db.Images, _db.Tags)
            .GetAutoRepairablePairsAsync(folder.Id);

        var pair = Assert.Single(pairs);
        Assert.Equal(uniqueMissing.Id, pair.MissingImageId);
        Assert.Equal(unique.Id, pair.CandidateImageId);
        Assert.DoesNotContain(pairs, p => p.MissingImageId == ambiguousMissing.Id);
        Assert.DoesNotContain(pairs, p => p.MissingImageId == taggedMissing.Id);
    }

    [Fact]
    public async Task missing個別裁定は4条件を初期化し候補5要素を提示してmissing側IDのまま再リンクする()
    {
        var root = Path.Combine(Path.GetTempPath(), "ViewPrism2.Tests", Guid.NewGuid().ToString("D"));
        Directory.CreateDirectory(root);
        try
        {
            var folder = await AddFolderAsync(root);
            var missing = await AddRowAsync(Row(folder.Id, "archive/old.jpg", ImageStatus.Missing));
            var candidate = await AddRowAsync(Row(folder.Id, "found/new.jpg", ImageStatus.Normal));
            var vm = CreateViewModel(folder);
            await vm.LoadAsync();

            Assert.True(vm.IsSelectedMissing);
            Assert.Equal("old", vm.NameContainsInput);
            Assert.Equal(".jpg", vm.ExtensionInput);
            Assert.Equal(missing.ModifiedDate, vm.MtimeFromInput);
            Assert.Equal(missing.FileSize.ToString(), vm.SizeToleranceInput);

            vm.NameContainsInput = null;
            await vm.SearchCandidatesCommand.ExecuteAsync(null);
            var candidateVm = Assert.Single(vm.Candidates);
            Assert.Equal(candidate.FileName, candidateVm.FileName);
            Assert.Equal(candidate.RelativePath, candidateVm.RelativePath);
            Assert.NotEmpty(candidateVm.SizeText);
            Assert.NotEmpty(candidateVm.ModifiedText);
            Assert.Equal(Path.Combine(root, "found", "new.jpg"), candidateVm.AbsolutePath);

            vm.SelectedCandidate = candidateVm;
            await vm.CommitCandidateCommand.ExecuteAsync(null);

            var relinked = Assert.IsType<ImageRecord>(await _db.Images.GetByIdAsync(missing.Id));
            Assert.Equal(missing.Id, relinked.Id);
            Assert.Equal(candidate.RelativePath, relinked.RelativePath);
            Assert.Equal(ImageStatus.Normal, relinked.Status);
            Assert.Null(await _db.Images.GetByIdAsync(candidate.Id));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task missing個別裁定のゴミ箱へ移動はT9限定でdeleted化する()
    {
        var root = Path.Combine(Path.GetTempPath(), "ViewPrism2.Tests", Guid.NewGuid().ToString("D"));
        Directory.CreateDirectory(root);
        try
        {
            var folder = await AddFolderAsync(root);
            var missing = await AddRowAsync(Row(folder.Id, "missing.jpg", ImageStatus.Missing));
            var vm = CreateViewModel(folder);
            await vm.LoadAsync();

            await vm.ExcludeMissingCommand.ExecuteAsync(null);

            Assert.Equal(ImageStatus.Deleted, (await _db.Images.GetByIdAsync(missing.Id))!.Status);
            Assert.True(vm.Adjudicated);
            Assert.True(vm.IsEmpty);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// ECO-075/GF-075-01 の大量 missing 応答性ガードの新面移植(R8 独立レビュー所見 Med(b))。
    /// 旧 CpUiRepairViewModelTests の O(M×N) 封止プローブを統合裁定面 LoadAsync へ引き継ぐ。
    /// 粗い時間上限は係数爆発(2000×2000)検出用=歴代承認済みの ECO-075 様式(新規閾値ではない)。
    /// </summary>
    [Fact]
    public async Task ECO075移植_大量の移動ペアでもLoadAsyncが単一ロード相当で完了する()
    {
        var folder = await AddFolderAsync(@"C:\fixture");
        await BulkInsertPairsAsync(folder.Id, pairs: 2000, missingOnly: 0);

        var service = new IntegrityReviewService(
            _db.Images, Relink(), new StubHashProvider(new Dictionary<string, string?>()));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var snapshot = await service.LoadAsync(folder, TestContext.Current.CancellationToken);
        sw.Stop();

        Assert.Equal(2000, snapshot.Events.Count(e => e.Group == IntegrityReviewGroup.Automatic));
        Assert.Equal(0, snapshot.Events.Count(e => e.Group == IntegrityReviewGroup.Missing));
        Assert.True(sw.ElapsedMilliseconds < 5000,
            $"ECO-075 移植: LoadAsync が {sw.ElapsedMilliseconds}ms — missing ごとの全行再探索(O(M×N))の疑い");
    }

    [Fact]
    public async Task ECO075移植_大量missing単独と一意ペア1組でもLoadAsyncが単一ロード相当で完了する()
    {
        var folder = await AddFolderAsync(@"C:\fixture");
        await BulkInsertPairsAsync(folder.Id, pairs: 1, missingOnly: 1999);

        var service = new IntegrityReviewService(
            _db.Images, Relink(), new StubHashProvider(new Dictionary<string, string?>()));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var snapshot = await service.LoadAsync(folder, TestContext.Current.CancellationToken);
        sw.Stop();

        Assert.Equal(1, snapshot.Events.Count(e => e.Group == IntegrityReviewGroup.Automatic));
        Assert.Equal(1999, snapshot.Events.Count(e => e.Group == IntegrityReviewGroup.Missing));
        Assert.True(sw.ElapsedMilliseconds < 5000,
            $"ECO-075 移植: LoadAsync が {sw.ElapsedMilliseconds}ms — missing ごとの全行再探索(O(M×N))の疑い");
    }

    /// <summary>pairs 組の moved(missing+pending/new/candidate)+missingOnly 件の missing 単独を一括投入。</summary>
    private Task BulkInsertPairsAsync(string folderId, int pairs, int missingOnly) =>
        _db.Manager.RunAsync(async conn =>
        {
            for (var i = 0; i < pairs + missingOnly; i++)
            {
                var hash = i.ToString("D64", System.Globalization.CultureInfo.InvariantCulture);
                await conn.ExecuteAsync(
                    """
                    INSERT INTO images (id, sync_folder_id, relative_path, file_name, file_size, hash, status, created_date, modified_date)
                    VALUES (@M, @F, @Mp, @Mn, 100, @H, 'missing', '2026-01-01T00:00:00.000Z', '2026-01-01T00:00:00.000Z')
                    """,
                    new { M = $"m-{i:D5}", F = folderId, Mp = $"old/{i:D5}.jpg", Mn = $"{i:D5}.jpg", H = hash });
                if (i < pairs)
                {
                    await conn.ExecuteAsync(
                        """
                        INSERT INTO images (id, sync_folder_id, relative_path, file_name, file_size, hash, status, pending_origin, candidate_link_id, created_date, modified_date)
                        VALUES (@P, @F, @Pp, @Pn, 100, @H, 'pending', 'new', @M, '2026-01-01T00:00:00.000Z', '2026-01-01T00:00:00.000Z')
                        """,
                        new { P = $"p-{i:D5}", F = folderId, Pp = $"new/{i:D5}.jpg", Pn = $"{i:D5}.jpg", H = hash, M = $"m-{i:D5}" });
                }
            }

            return 0;
        });

    private IntegrityReviewViewModel CreateViewModel(SyncFolder folder)
    {
        var relink = new RelinkService(_db.Images, _db.Tags);
        return new IntegrityReviewViewModel(
            new IntegrityReviewService(
                _db.Images,
                relink,
                new StubHashProvider(new Dictionary<string, string?>())),
            new PendingReviewService(_db.Images),
            _db.Images,
            _db.Tags,
            relink,
            new TrashService(_db.Images, _db.Folders, new AlwaysMissingProbe()),
            TestLoc.Ja(),
            new ConfirmingWindows(),
            folder);
    }

    private RelinkService Relink() => new(_db.Images, _db.Tags);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ViewPrism2.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }

    private async Task<SyncFolder> AddFolderAsync(string root, string? lastScan = null)
    {
        var folder = new SyncFolder
        {
            Id = IdGenerator.NewId(),
            Name = "fixture",
            Path = root,
            LastScan = lastScan,
        };
        Assert.True((await _db.Folders.AddAsync(folder)).IsSuccess);
        return folder;
    }

    private Task<string?> PendingBaselineHashAsync(string imageId) =>
        _db.Manager.RunAsync(conn => conn.QuerySingleAsync<string?>(
            "SELECT pending_baseline_hash FROM images WHERE id = @ImageId",
            new { ImageId = imageId }));

    private Task SetPendingBaselineHashAsync(string imageId, string hash) =>
        _db.Manager.RunAsync(conn => conn.ExecuteAsync(
            "UPDATE images SET pending_baseline_hash = @Hash WHERE id = @ImageId",
            new { ImageId = imageId, Hash = hash }));

    private async Task<ImageRecord> AddFileAndRowAsync(
        SyncFolder folder, string name, string content, string recordedHash)
    {
        await File.WriteAllBytesAsync(
            Path.Combine(folder.Path, name),
            Encoding.UTF8.GetBytes(content),
            TestContext.Current.CancellationToken);
        return await AddRowAsync(Row(
            folder.Id, name, ImageStatus.Pending, PendingOrigin.Reappeared, hash: recordedHash));
    }

    private async Task<ImageRecord> AddRowAsync(ImageRecord row)
    {
        await _db.Images.AddAsync(row);
        return row;
    }

    private static ImageRecord Row(
        string folderId,
        string name,
        ImageStatus status,
        PendingOrigin? origin = null,
        string? candidateId = null,
        string? hash = null) => new()
        {
            Id = IdGenerator.NewId(),
            SyncFolderId = folderId,
            RelativePath = name,
            FileName = name[(name.LastIndexOf('/') + 1)..],
            FileSize = 10,
            Hash = hash ?? new string('a', 64),
            Status = status,
            PendingOrigin = origin,
            CandidateLinkId = candidateId,
            CreatedDate = "2026-07-24T00:00:00.000Z",
            ModifiedDate = "2026-07-24T00:00:00.000Z",
        };

    private sealed class StubHashProvider(
        IReadOnlyDictionary<string, string?> hashes) : IIntegrityReviewHashProvider
    {
        public List<string> Paths { get; } = [];

        public Task<string> ComputeSha256Async(string absolutePath, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Paths.Add(absolutePath);
            if (!hashes.TryGetValue(absolutePath, out var hash) || hash is null)
            {
                throw new IOException("fixture read failure");
            }

            return Task.FromResult(hash);
        }
    }

    private sealed class CountingHashProvider(
        IReadOnlyDictionary<string, string?> hashes) : IIntegrityReviewHashProvider
    {
        public int CallCount { get; private set; }

        public Task<string> ComputeSha256Async(string absolutePath, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            CallCount++;
            if (!hashes.TryGetValue(absolutePath, out var hash) || hash is null)
            {
                throw new IOException("fixture hash unavailable");
            }

            return Task.FromResult(hash);
        }
    }

    private sealed class BlockingHashProvider(string hash) : IIntegrityReviewHashProvider
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<string> ComputeSha256Async(string absolutePath, CancellationToken ct)
        {
            Started.TrySetResult();
            await Release.Task.WaitAsync(ct);
            return hash;
        }
    }

    private sealed class CapturingProgress<T> : IProgress<T>
    {
        public T? Last { get; private set; }

        public void Report(T value) => Last = value;
    }

    private sealed class AlwaysMissingProbe : IFilePresenceProbe
    {
        public bool Exists(string absoluteImagePath) => false;
    }

    private class ConfirmingWindows : IWindowService
    {
        public virtual Task<bool> ConfirmListAsync(
            string title,
            string lead,
            string supportingMessage,
            string confirmLabel,
            IReadOnlyList<ConfirmationListItem> items,
            string? cancelLabel = null) => Task.FromResult(true);

        public Task<bool> ConfirmAsync(
            string title,
            string message,
            string confirmLabel,
            bool destructive = false,
            string? cancelLabel = null) => Task.FromResult(true);

        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);

        public Task ShowFolderManagementAsync() => Task.CompletedTask;

        public Task ShowSettingsAsync() => Task.CompletedTask;

        public Task ShowSnapshotsAsync() => Task.CompletedTask;

        public Task<bool> ShowTagEditorAsync(Tag? existing) => Task.FromResult(false);

        public Task<bool> ShowViewEditDialogAsync(View? existing) => Task.FromResult(false);

        public Task<IReadOnlyList<string>?> ShowNumericValueDialogAsync(
            Tag tag, NumericTagSettings? settings, int selectionCount)
            => Task.FromResult<IReadOnlyList<string>?>(null);

        public Task<NodeConditionResult?> ShowNodeConditionDialogAsync(
            Tag tag, HierarchyConditionType? currentType, string? currentValueJson)
            => Task.FromResult<NodeConditionResult?>(null);

        public Task ShowRelinkAsync(string folderId) => Task.CompletedTask;

        public void ShowViewer(IReadOnlyList<ImageEntry> ordered, int startIndex)
        {
        }
    }

    private sealed class CapturingWindows(bool confirm) : ConfirmingWindows
    {
        public IReadOnlyList<ConfirmationListItem>? LastConfirmationItems { get; private set; }

        public override Task<bool> ConfirmListAsync(
            string title,
            string lead,
            string supportingMessage,
            string confirmLabel,
            IReadOnlyList<ConfirmationListItem> items,
            string? cancelLabel = null)
        {
            LastConfirmationItems = items;
            return Task.FromResult(confirm);
        }
    }
}
