using Dapper;
using Microsoft.Data.Sqlite;
using ViewPrism2.Core.Common;

namespace ViewPrism2.Infrastructure.Database;

/// <summary>
/// DB 接続の所有者(M-DB-007 / ADR-0003 / K-SQLITE)。
/// アプリ全体で単一の SqliteConnection を共有し、SemaphoreSlim(1,1) でシリアル化する。
/// 接続確立ごとに PRAGMA journal_mode=WAL / foreign_keys=ON を明示発行する(既定値に依存しない)。
/// </summary>
public sealed class DatabaseManager : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    private DatabaseManager(SqliteConnection connection)
    {
        _connection = connection;
    }

    /// <summary>
    /// ディレクトリ作成 → 接続 → PRAGMA → マイグレーション(REQ-004)の順で DB を開く。
    /// </summary>
    public static DatabaseManager Open(string dbPath, IClock clock)
    {
        ArgumentException.ThrowIfNullOrEmpty(dbPath);
        ArgumentNullException.ThrowIfNull(clock);

        var directory = Path.GetDirectoryName(Path.GetFullPath(dbPath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var connection = new SqliteConnection($"Data Source={dbPath}");
        try
        {
            connection.Open();
            connection.Execute("PRAGMA journal_mode=WAL;");
            connection.Execute("PRAGMA foreign_keys=ON;");
            MigrationRunner.Run(connection, clock, DatabaseSchema.LatestDdl, DatabaseSchema.Migrations);
            return new DatabaseManager(connection);
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    /// <summary>共有接続上で処理を直列実行する(K-SQLITE: 単一接続+SemaphoreSlim)。</summary>
    public async Task<T> RunAsync<T>(Func<SqliteConnection, Task<T>> work, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await work(_connection).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>共有接続上で処理を直列実行する(戻り値なし)。</summary>
    public Task RunAsync(Func<SqliteConnection, Task> work, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        return RunAsync(async conn =>
        {
            await work(conn).ConfigureAwait(false);
            return true;
        }, ct);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _connection.Close();
        SqliteConnection.ClearPool(_connection); // ファイルハンドルを確実に解放(一時 DB の後始末)
        _connection.Dispose();
        _gate.Dispose();
    }
}
