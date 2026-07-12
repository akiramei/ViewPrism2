using Dapper;

namespace ViewPrism2.Infrastructure.Database;

/// <summary>
/// DB 内ライブラリ UUID(ECO-073 §3.6)。設定ファイルでなく DB 内に永続し、
/// 再起動・別マシン移動・スナップショット復元後も変わらない(インストール/マシン ID とは別物)。
/// migration 008 は表(library_metadata)だけを作り、値のシードは本クラスが冪等に行う。
/// </summary>
public static class LibraryIdentity
{
    public const string Key = "library_id";

    public static Task<string> GetOrCreateAsync(DatabaseManager db, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.RunAsync(async conn =>
        {
            var existing = await conn.ExecuteScalarAsync<string?>(
                "SELECT value FROM library_metadata WHERE key = @Key", new { Key }).ConfigureAwait(false);
            if (existing is not null)
            {
                return existing;
            }

            await conn.ExecuteAsync(
                "INSERT OR IGNORE INTO library_metadata (key, value) VALUES (@Key, @Value)",
                new { Key, Value = Guid.NewGuid().ToString("D") }).ConfigureAwait(false);
            return (await conn.ExecuteScalarAsync<string>(
                "SELECT value FROM library_metadata WHERE key = @Key", new { Key }).ConfigureAwait(false))!;
        }, ct);
    }
}
