using Dapper;
using Microsoft.Data.Sqlite;
using ViewPrism2.Core.Common;
using ViewPrism2.Infrastructure.Database;
using Xunit;

namespace ViewPrism2.Oracle;

/// <summary>
/// S-05: マイグレーション同値(spec §2.0・REQ-004、EQ-001、L2)。
/// 初版 DDL 相当の空 DB に全マイグレーション適用 vs 新規作成 DB で
/// PRAGMA table_info/foreign_key_list/index_list が全テーブルで一致し、migrations 行数も一致する。
/// フィクスチャは CP-DB-006(tests/ViewPrism2.Tests/CpDb006Tests.cs)と同種。
/// </summary>
[Trait("oracle", "S-05")]
public sealed class S05MigrationEquivalenceTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "ViewPrism2.Oracle", "s05-" + Guid.NewGuid().ToString("D"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
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
    public void 初版DDLのDBへ全マイグレーション適用は新規作成DBとスキーマ同値()
    {
        // 新規作成 DB(最新スキーマ+全マイグレーション既適用マーク)
        var freshPath = NewDbPath();
        using (DatabaseManager.Open(freshPath, new SystemClock()))
        {
        }

        var freshSchema = DumpSchema(freshPath);
        var freshMigrationCount = CountMigrations(freshPath);

        // 初版 DDL のみの DB(migrations マークなし)→ Open で未適用分が全適用される
        // 治具修理(2026-06-11): v0 は現行 DatabaseSchema.LatestDdl からの導出を止め、凍結スナップショットに固定
        var v0Path = NewDbPath();
        ExecuteRaw(v0Path, V0SchemaFixture.InitialDdl);
        using (DatabaseManager.Open(v0Path, new SystemClock()))
        {
        }

        var migratedSchema = DumpSchema(v0Path);
        var migratedMigrationCount = CountMigrations(v0Path);

        // PRAGMA table_info/foreign_key_list/index_list が全テーブルで一致
        Assert.Equal(freshSchema, migratedSchema);

        // migrations 行数一致
        Assert.Equal(freshMigrationCount, migratedMigrationCount);
        Assert.Equal(DatabaseSchema.Migrations.Count, freshMigrationCount);
    }

    private string NewDbPath()
    {
        Directory.CreateDirectory(_directory);
        return Path.Combine(_directory, Guid.NewGuid().ToString("D") + ".db");
    }

    /// <summary>PRAGMA table_info / foreign_key_list / index_list の全テーブルダンプ。</summary>
    private static string DumpSchema(string dbPath)
    {
        var lines = new List<string>();
        WithConnection(dbPath, conn =>
        {
            var tables = conn.Query<string>(
                    "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name")
                .ToList();
            foreach (var table in tables)
            {
                lines.Add($"TABLE {table}");
                foreach (var col in conn.Query($"PRAGMA table_info({table});"))
                {
                    var c = (IDictionary<string, object>)col;
                    lines.Add($"  COL {c["name"]} {c["type"]} notnull={c["notnull"]} default={c["dflt_value"] ?? "NULL"} pk={c["pk"]}");
                }

                foreach (var fk in conn.Query($"PRAGMA foreign_key_list({table});"))
                {
                    var f = (IDictionary<string, object>)fk;
                    lines.Add($"  FK {f["from"]} -> {f["table"]}({f["to"]}) on_delete={f["on_delete"]}");
                }

                foreach (var ix in conn.Query($"PRAGMA index_list({table});"))
                {
                    var i = (IDictionary<string, object>)ix;
                    var cols = conn.Query($"PRAGMA index_info({i["name"]});")
                        .Select(r => (string)((IDictionary<string, object>)r)["name"]!);
                    lines.Add($"  IX {i["name"]} unique={i["unique"]} cols=({string.Join(',', cols)})");
                }
            }
        });
        return string.Join('\n', lines);
    }

    private static int CountMigrations(string dbPath)
    {
        var count = 0;
        WithConnection(dbPath, conn =>
            count = (int)conn.ExecuteScalar<long>("SELECT COUNT(*) FROM migrations"));
        return count;
    }

    private static void ExecuteRaw(string dbPath, string sql)
    {
        WithConnection(dbPath, conn => conn.Execute(sql));
    }

    private static void WithConnection(string dbPath, Action<SqliteConnection> action)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        try
        {
            action(conn);
        }
        finally
        {
            conn.Close();
            SqliteConnection.ClearPool(conn);
        }
    }
}
