using Dapper;
using ViewPrism2.App.ViewModels;
using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Infrastructure.Database;
using ViewPrism2.Infrastructure.Settings;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// CP-SNAPSHOT-031(ECO-072): カタログ DB スナップショットの作成・検証・時点復元。
/// 先行プローブ(R5): 是正前は SnapshotService/SnapshotRestoreBootstrap が存在せず、
/// 設定に入口コマンドが無いことを実測で固定してから製品コードへ着手した(612 件中 2 不合格)。
/// 以降の挙動テストは §3.1 の作成/復元契約を一時 DB fixture で固定する。
/// </summary>
[Trait("cp", "CP-SNAPSHOT-031")]
public sealed class CpSnapshot072Tests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "ViewPrism2.Tests", Guid.NewGuid().ToString("D"));

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

    private string NewDir(string name) => Path.Combine(_root, name);

    private static Task AddTagAsync(DatabaseManager manager, string name)
        => manager.RunAsync(conn => conn.ExecuteAsync(
            "INSERT INTO tags (id, name, type) VALUES (@Id, @Name, 'simple')",
            new { Id = Guid.NewGuid().ToString("D"), Name = name }));

    [Fact]
    public void スナップショットの作成器と復元ブートストラップが存在する()
    {
        var infra = typeof(DatabaseManager).Assembly;
        Assert.NotNull(infra.GetType("ViewPrism2.Infrastructure.Database.SnapshotService"));
        Assert.NotNull(infra.GetType("ViewPrism2.Infrastructure.Database.SnapshotRestoreBootstrap"));
    }

    [Fact]
    public void 設定にスナップショット管理への入口コマンドがある()
    {
        // SS-001 裁定(b): A層の入口は設定ウィンドウの「バックアップ(スナップショット)」節
        var vm = typeof(SettingsViewModel);
        Assert.NotNull(vm.GetProperty("OpenSnapshotsCommand"));
    }

    [Fact]
    public async Task 作成は検証済みスナップショットを確定しpartialを残さない()
    {
        using var db = new TempDb();
        await AddTagAsync(db.Manager, "旅行");
        var service = new SnapshotService(db.Manager, db.Clock, db.Directory, "9.9.9");
        var dir = NewDir("snapshots");

        var result = await service.CreateAsync(dir, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var info = result.Value!;
        Assert.True(File.Exists(info.FilePath));
        Assert.StartsWith(SnapshotService.FilePrefix, Path.GetFileName(info.FilePath));
        Assert.True(info.IsVerified);
        Assert.Equal("9.9.9", info.AppVersion);
        Assert.Empty(Directory.GetFiles(dir, "*" + SnapshotService.PartialExtension));
        Assert.True(File.Exists(info.FilePath + SnapshotService.MetaSuffix));

        // 一覧: 検証済みとして列挙され、スナップショット内容は作成時点と一貫する
        var listed = Assert.Single(service.List(dir));
        Assert.True(listed.IsVerified);
        using var conn = new Microsoft.Data.Sqlite.SqliteConnection(
            $"Data Source={info.FilePath};Mode=ReadOnly;Pooling=False");
        conn.Open();
        Assert.Equal(1L, conn.ExecuteScalar<long>("SELECT COUNT(*) FROM tags WHERE name = '旅行'"));
    }

    [Fact]
    public void メタ欠落ファイルは検証待ちで列挙され復元前検証が拒否する()
    {
        using var db = new TempDb();
        var service = new SnapshotService(db.Manager, db.Clock, db.Directory, "9.9.9");
        var dir = NewDir("snapshots");
        Directory.CreateDirectory(dir);
        var junk = Path.Combine(dir, SnapshotService.FilePrefix + "junk.db");
        File.WriteAllBytes(junk, [0xDE, 0xAD, 0xBE, 0xEF]);

        var listed = Assert.Single(service.List(dir));
        Assert.False(listed.IsVerified); // CAD A-1: 検証待ち=復元不可の裏面
        Assert.False(service.ValidateForRestore(junk).IsSuccess);
    }

    [Fact]
    public async Task 未知のmigrationを含むスナップショットは互換性なしとして拒否する()
    {
        using var db = new TempDb();
        await db.Manager.RunAsync(conn => conn.ExecuteAsync(
            "INSERT INTO migrations (id, applied_at) VALUES ('zzz-future', '2099-01-01T00:00:00.000Z')"), TestContext.Current.CancellationToken);
        var service = new SnapshotService(db.Manager, db.Clock, db.Directory, "9.9.9");
        var snapshot = await service.CreateAsync(NewDir("snapshots"), TestContext.Current.CancellationToken);
        Assert.True(snapshot.IsSuccess);

        var validation = service.ValidateForRestore(snapshot.Value!.FilePath);

        Assert.False(validation.IsSuccess);
        Assert.Equal(ErrorCode.ValidationError, validation.Error);
        Assert.Contains("zzz-future", validation.Message);
    }

    [Fact]
    public async Task 復元予約は次回起動時に差し替わり安全スナップショットを残す()
    {
        // 現行 DB(タグ=current)と、復元したいスナップショット(タグ=snapshot)を用意する
        var appDataDir = NewDir("appdata");
        var dbPath = Path.Combine(appDataDir, "viewprism2.db");
        using (var current = DatabaseManager.Open(dbPath, new SystemClock()))
        {
            await AddTagAsync(current, "current");
            var service = new SnapshotService(current, new SystemClock(), appDataDir, "9.9.9");
            var created = await service.CreateAsync(NewDir("snapshots"), TestContext.Current.CancellationToken);
            Assert.True(created.IsSuccess);
            await AddTagAsync(current, "after-snapshot");
            service.RequestRestore(created.Value!.FilePath, NewDir("snapshots"));
        }

        var result = SnapshotRestoreBootstrap.Apply(appDataDir, "9.9.9");

        Assert.Equal(SnapshotRestoreStatus.Applied, result.Status);
        Assert.False(File.Exists(Path.Combine(appDataDir, SnapshotService.PendingRestoreFileName))); // 一回限り
        Assert.NotEmpty(Directory.GetFiles(NewDir("snapshots"), SnapshotService.SafetyFilePrefix + "*.db"));
        using var restored = DatabaseManager.Open(dbPath, new SystemClock()); // migration 前進も既存経路で走る
        Assert.Equal(1L, await restored.RunAsync(c => c.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM tags WHERE name = 'current'"), TestContext.Current.CancellationToken));
        Assert.Equal(0L, await restored.RunAsync(c => c.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM tags WHERE name = 'after-snapshot'"), TestContext.Current.CancellationToken)); // 時点より後の変更は巻き戻る
    }

    [Fact]
    public async Task 復元失敗時は現行DBのまま起動を継続する()
    {
        var appDataDir = NewDir("appdata");
        var dbPath = Path.Combine(appDataDir, "viewprism2.db");
        using (var current = DatabaseManager.Open(dbPath, new SystemClock()))
        {
            await AddTagAsync(current, "current");
        }

        var junk = Path.Combine(NewDir("snapshots"), SnapshotService.FilePrefix + "junk.db");
        Directory.CreateDirectory(NewDir("snapshots"));
        File.WriteAllBytes(junk, [0x00, 0x01]);
        File.WriteAllText(
            Path.Combine(appDataDir, SnapshotService.PendingRestoreFileName),
            $$"""{ "snapshotPath": {{System.Text.Json.JsonSerializer.Serialize(junk)}}, "safetyDirectory": {{System.Text.Json.JsonSerializer.Serialize(NewDir("snapshots"))}}, "requestedAt": "2026-07-12T00:00:00.000Z" }""");

        var result = SnapshotRestoreBootstrap.Apply(appDataDir, "9.9.9");

        Assert.Equal(SnapshotRestoreStatus.Failed, result.Status);
        Assert.False(File.Exists(Path.Combine(appDataDir, SnapshotService.PendingRestoreFileName)));
        using var manager = DatabaseManager.Open(dbPath, new SystemClock());
        Assert.Equal(1L, await manager.RunAsync(c => c.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM tags WHERE name = 'current'"), TestContext.Current.CancellationToken)); // 現行 DB は無傷
    }

    [Fact]
    public async Task 復元コマンドは確認と検証を通ってから予約し再起動を要求する()
    {
        using var db = new TempDb();
        await AddTagAsync(db.Manager, "旅行");
        var service = new SnapshotService(db.Manager, db.Clock, db.Directory, "9.9.9");
        var dir = NewDir("snapshots");
        Assert.True((await service.CreateAsync(dir, TestContext.Current.CancellationToken)).IsSuccess);

        var settings = new AppSettings { SnapshotDirectory = dir };
        var store = new SettingsStore(NewDir("settings"));
        var restartRequested = 0;
        var confirmAnswer = false;
        var vm = new SnapshotViewModel(
            service, settings, store, TestLoc.Empty(),
            _ => Task.FromResult<string?>(null),
            _ => Task.FromResult(confirmAnswer),
            () => restartRequested++);
        vm.Load();
        var item = Assert.Single(vm.Items);

        // A-2 でキャンセル → 予約されない
        await vm.RestoreCommand.ExecuteAsync(item);
        Assert.Equal(0, restartRequested);
        Assert.False(File.Exists(Path.Combine(db.Directory, SnapshotService.PendingRestoreFileName)));

        // A-2 で確定 → 復元直前検証を通り、予約+再起動要求
        confirmAnswer = true;
        await vm.RestoreCommand.ExecuteAsync(item);
        Assert.Equal(1, restartRequested);
        Assert.True(File.Exists(Path.Combine(db.Directory, SnapshotService.PendingRestoreFileName)));
    }
}
