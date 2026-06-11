using Dapper;
using Microsoft.Data.Sqlite;
using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Infrastructure.Database;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// CP-DB-006(L2): スキーマ・PRAGMA・マイグレーション契約が仕様 §2.0・REQ-003〜005 と一致する。
/// PRAGMA table_info/foreign_key_list/index_list の比較+migrations 行数検査。
/// </summary>
[Trait("cp", "CP-DB-006")]
public sealed class CpDb006Tests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "ViewPrism2.Tests", Guid.NewGuid().ToString("D"));

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

    private string NewDbPath() => Path.Combine(_directory, Guid.NewGuid().ToString("D") + ".db");

    // ---- REQ-003: PRAGMA ----

    [Fact]
    public async Task 新規DBはWALかつFK有効()
    {
        using var db = new TempDb();
        var (journal, foreignKeys) = await db.Manager.RunAsync(async conn =>
        {
            var j = await conn.ExecuteScalarAsync<string>("PRAGMA journal_mode;");
            var f = await conn.ExecuteScalarAsync<long>("PRAGMA foreign_keys;");
            return (j, f);
        }, TestContext.Current.CancellationToken);

        Assert.Equal("wal", journal);
        Assert.Equal(1, foreignKeys);
    }

    // ---- REQ-004: マイグレーション意味論 ----

    [Fact]
    public async Task 新規DBはmigrations行数が定義数と一致し全id記録済み()
    {
        using var db = new TempDb();
        var ids = await db.Manager.RunAsync(async conn =>
            (await conn.QueryAsync<string>("SELECT id FROM migrations ORDER BY id")).ToList(),
            TestContext.Current.CancellationToken);

        Assert.Equal(DatabaseSchema.Migrations.Count, ids.Count);
        Assert.Equal(DatabaseSchema.Migrations.Select(m => m.Id).Order(StringComparer.Ordinal), ids);
    }

    [Fact]
    public void v0DBに全マイグレーション適用で新規DBとスキーマ同値()
    {
        // 新規 DB(最新スキーマ+全既適用マーク)
        var freshPath = NewDbPath();
        SchemaDump fresh;
        using (var db = DatabaseManager.Open(freshPath, new SystemClock()))
        {
            fresh = DumpSchema(freshPath);
        }

        // v0 DB: 初版 DDL のみ(migrations マークなし)→ Open で未適用分が適用される
        var v0Path = NewDbPath();
        ExecuteRaw(v0Path, DatabaseSchema.LatestDdl); // V1 の初版 DDL = LatestDdl
        using (var db = DatabaseManager.Open(v0Path, new SystemClock()))
        {
            var migrated = DumpSchema(v0Path);
            Assert.Equal(fresh, migrated);
        }
    }

    [Fact]
    public void ランナーは未適用分をID昇順で適用し新規DBと同値にする_合成マイグレーション()
    {
        // ランナー機構の実検査(V1 の Migrations が空でも意味論が成立していることの証明)
        const string v0Ddl = "CREATE TABLE a (id TEXT NOT NULL PRIMARY KEY);";
        const string latestDdl = """
            CREATE TABLE a (id TEXT NOT NULL PRIMARY KEY, note TEXT NULL);
            CREATE TABLE b (id TEXT NOT NULL PRIMARY KEY);
            """;
        var migrations = new List<Migration>
        {
            new("002-add-note", "ALTER TABLE a ADD COLUMN note TEXT NULL;"),
            new("001-add-b", "CREATE TABLE b (id TEXT NOT NULL PRIMARY KEY);"),
        };

        // 新規 DB: 最新 DDL+全既適用マーク
        var freshPath = NewDbPath();
        WithConnection(freshPath, conn =>
            MigrationRunner.Run(conn, new SystemClock(), latestDdl, migrations));
        var fresh = DumpSchema(freshPath);
        var freshIds = QueryIds(freshPath);
        Assert.Equal(["001-add-b", "002-add-note"], freshIds);

        // v0 DB: 初版 DDL のみ → 未適用 2 件が ID 昇順で適用される
        var v0Path = NewDbPath();
        ExecuteRaw(v0Path, v0Ddl);
        WithConnection(v0Path, conn =>
            MigrationRunner.Run(conn, new SystemClock(), latestDdl, migrations));

        Assert.Equal(fresh, DumpSchema(v0Path));
        Assert.Equal(freshIds, QueryIds(v0Path));

        // 再実行は冪等(適用済みはスキップ)
        WithConnection(v0Path, conn =>
            MigrationRunner.Run(conn, new SystemClock(), latestDdl, migrations));
        Assert.Equal(freshIds, QueryIds(v0Path));
    }

    // ---- FK 実動(FMEA-003 / FMEA-005) ----

    [Fact]
    public async Task タグ削除カスケード_4テーブルの状態が仕様どおり()
    {
        using var db = new TempDb();
        var folder = await SeedFolderAsync(db, "C:/pics");
        var image = await SeedImageAsync(db, folder.Id, "a.jpg");

        var parent = new Tag { Id = "tag-parent", Name = "Parent", Type = TagType.Textual };
        var child = new Tag { Id = "tag-child", Name = "Child", Type = TagType.Simple, ParentId = parent.Id };
        await db.Tags.AddAsync(parent);
        await db.Tags.AddAsync(child);
        await db.Tags.UpsertTextualSettingsAsync(new TextualTagSettings
        {
            TagId = parent.Id,
            PredefinedValues = ["red"],
        });
        await db.Tags.UpsertImageTagAsync(new ImageTag { ImageId = image.Id, TagId = parent.Id, Value = "red" });

        var view = new View { Id = "view-1", Name = "v", ModifiedAt = db.Clock.UtcNowIso() };
        await db.Views.AddAsync(view);
        await db.Views.AddConditionAsync(new ViewCondition
        {
            Id = "cond-1", ViewId = view.Id, TagId = parent.Id, Operator = ConditionOperator.Exists,
        });
        await db.Views.AddNodeAsync(new HierarchyNode
        {
            Id = "node-1", ViewId = view.Id, TagId = parent.Id, Position = 0,
        });

        await db.Tags.DeleteAsync(parent.Id);

        var (imageTags, settings, nodes) = await db.Manager.RunAsync(async conn =>
        {
            var it = await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM image_tags WHERE tag_id = 'tag-parent'");
            var ts = await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM textual_tag_settings WHERE tag_id = 'tag-parent'");
            var nd = await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM view_tag_hierarchies WHERE tag_id = 'tag-parent'");
            return (it, ts, nd);
        }, TestContext.Current.CancellationToken);

        Assert.Equal(0, imageTags); // image_tags 消滅(CASCADE)
        Assert.Equal(0, settings);  // 型別設定 消滅(CASCADE)
        Assert.Equal(0, nodes);     // 階層ノード 消滅(CASCADE)

        var condition = await db.Views.GetConditionByIdAsync("cond-1");
        Assert.NotNull(condition);
        Assert.Null(condition.TagId); // view_conditions.tag_id SET NULL

        var orphan = await db.Tags.GetByIdAsync(child.Id);
        Assert.NotNull(orphan);
        Assert.Null(orphan.ParentId); // 子タグ parent_id SET NULL
    }

    [Fact]
    public async Task フォルダ削除でimagesと付与が連鎖削除される()
    {
        using var db = new TempDb();
        var folder = await SeedFolderAsync(db, "C:/pics");
        var image = await SeedImageAsync(db, folder.Id, "a.jpg");
        var tag = new Tag { Id = "tag-1", Name = "T", Type = TagType.Simple };
        await db.Tags.AddAsync(tag);
        await db.Tags.UpsertImageTagAsync(new ImageTag { ImageId = image.Id, TagId = tag.Id, Value = null });

        await db.Folders.DeleteAsync(folder.Id);

        var (images, imageTags) = await db.Manager.RunAsync(async conn =>
        {
            var i = await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM images");
            var it = await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM image_tags");
            return (i, it);
        }, TestContext.Current.CancellationToken);

        Assert.Equal(0, images);
        Assert.Equal(0, imageTags);
    }

    // ---- sync_folders.path UNIQUE(case-insensitive) ----

    [Fact]
    public async Task パスの大文字小文字違いは重複として拒否される()
    {
        using var db = new TempDb();
        var first = await db.Folders.AddAsync(NewFolder("C:/a"));
        Assert.True(first.IsSuccess);

        var second = await db.Folders.AddAsync(NewFolder("c:/A"));
        Assert.False(second.IsSuccess);
        Assert.Equal(ErrorCode.DuplicateFolderPath, second.Error);

        // UNIQUE 制約自体も DB 層で実動している(直接 INSERT で違反)
        var ex = await Assert.ThrowsAsync<SqliteException>(() => db.Manager.RunAsync(conn =>
            conn.ExecuteAsync("""
                INSERT INTO sync_folders (id, name, path, exclude_patterns)
                VALUES ('x', 'x', 'C:/A', '[]')
                """), TestContext.Current.CancellationToken));
        Assert.Equal(19, ex.SqliteErrorCode); // SQLITE_CONSTRAINT
    }

    [Fact]
    public async Task COLLATE_NOCASEが主要列に付与されている()
    {
        using var db = new TempDb();
        var (folders, images) = await db.Manager.RunAsync(async conn =>
        {
            var f = await conn.ExecuteScalarAsync<string>(
                "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'sync_folders'");
            var i = await conn.ExecuteScalarAsync<string>(
                "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'images'");
            return (f, i);
        }, TestContext.Current.CancellationToken);

        Assert.Contains("COLLATE NOCASE", folders, StringComparison.Ordinal);
        Assert.Contains("COLLATE NOCASE", images, StringComparison.Ordinal);
    }

    // ---- ヘルパ ----

    private static SyncFolder NewFolder(string path) => new()
    {
        Id = IdGenerator.NewId(),
        Name = "pics",
        Path = path,
    };

    private static async Task<SyncFolder> SeedFolderAsync(TempDb db, string path)
    {
        var folder = NewFolder(path);
        var result = await db.Folders.AddAsync(folder);
        Assert.True(result.IsSuccess);
        return folder;
    }

    private static async Task<ImageRecord> SeedImageAsync(TempDb db, string folderId, string name)
    {
        var image = new ImageRecord
        {
            Id = IdGenerator.NewId(),
            SyncFolderId = folderId,
            RelativePath = name,
            FileName = name,
            FileSize = 1,
            Hash = new string('0', 64),
            Status = ImageStatus.Normal,
            CreatedDate = "2026-01-01T00:00:00.000Z",
            ModifiedDate = "2026-01-01T00:00:00.000Z",
        };
        await db.Images.AddAsync(image);
        return image;
    }

    /// <summary>テーブル・列・型・既定値・FK・索引のスキーマダンプ(L2 同値検査用)。</summary>
    private sealed record SchemaDump(string Text);

    private SchemaDump DumpSchema(string dbPath)
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
        return new SchemaDump(string.Join('\n', lines));
    }

    private List<string> QueryIds(string dbPath)
    {
        var ids = new List<string>();
        WithConnection(dbPath, conn =>
            ids.AddRange(conn.Query<string>("SELECT id FROM migrations ORDER BY id")));
        return ids;
    }

    private void ExecuteRaw(string dbPath, string sql)
    {
        WithConnection(dbPath, conn => conn.Execute(sql));
    }

    private void WithConnection(string dbPath, Action<SqliteConnection> action)
    {
        Directory.CreateDirectory(_directory);
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
