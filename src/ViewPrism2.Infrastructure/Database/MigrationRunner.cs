using Dapper;
using Microsoft.Data.Sqlite;
using ViewPrism2.Core.Common;

namespace ViewPrism2.Infrastructure.Database;

/// <summary>
/// マイグレーションランナー(M-DB-007、REQ-004 の意味論):
/// 新規 DB = 最新スキーマを適用し、全マイグレーションを既適用としてマークする。
/// 既存 DB = migrations テーブルに無い分を ID 昇順で適用する(各々 1 トランザクション)。
/// </summary>
public static class MigrationRunner
{
    private const string MigrationsTableDdl = """
        CREATE TABLE IF NOT EXISTS migrations (
            id         TEXT NOT NULL PRIMARY KEY,
            applied_at TEXT NOT NULL
        );
        """;

    public static void Run(SqliteConnection connection, IClock clock, string latestDdl, IReadOnlyList<Migration> migrations)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(migrations);

        var isNew = connection.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' AND name <> 'migrations'") == 0;

        if (isNew)
        {
            // 新規 DB: 最新スキーマ+全マイグレーション既適用マーク(1 トランザクション)
            using var tx = connection.BeginTransaction();
            connection.Execute(MigrationsTableDdl, transaction: tx);
            connection.Execute(latestDdl, transaction: tx);
            foreach (var migration in migrations)
            {
                MarkApplied(connection, tx, migration.Id, clock);
            }

            tx.Commit();
            return;
        }

        connection.Execute(MigrationsTableDdl);
        var applied = connection
            .Query<string>("SELECT id FROM migrations")
            .ToHashSet(StringComparer.Ordinal);

        foreach (var migration in migrations.OrderBy(m => m.Id, StringComparer.Ordinal))
        {
            if (applied.Contains(migration.Id))
            {
                continue;
            }

            // 未適用分を ID 昇順で適用(各々 1 トランザクション、K-SQLITE)
            using var tx = connection.BeginTransaction();
            connection.Execute(migration.Sql, transaction: tx);
            MarkApplied(connection, tx, migration.Id, clock);
            tx.Commit();
        }
    }

    private static void MarkApplied(SqliteConnection connection, SqliteTransaction tx, string id, IClock clock)
    {
        connection.Execute(
            "INSERT INTO migrations (id, applied_at) VALUES (@Id, @AppliedAt)",
            new { Id = id, AppliedAt = clock.UtcNowIso() },
            tx);
    }
}
