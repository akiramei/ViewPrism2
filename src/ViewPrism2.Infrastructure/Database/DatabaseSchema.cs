namespace ViewPrism2.Infrastructure.Database;

/// <summary>スキーマ変更 1 件(REQ-004)。Id 昇順で適用される。</summary>
public sealed record Migration(string Id, string Sql);

/// <summary>
/// スキーマ DDL 定数(M-DB-007)。列・FK・既定値は仕様 §2.0 準拠。
/// path/relative_path は COLLATE NOCASE(K-SQLITE)。sync_folders.path に UNIQUE。
/// 日時は TEXT(ISO 8601 UTC)、BOOLEAN は INTEGER 0/1。
/// </summary>
public static class DatabaseSchema
{
    /// <summary>
    /// 最新スキーマの DDL(migrations テーブル以外)。新規 DB はこれを 1 回で適用する(REQ-004)。
    /// V1 時点では初版 DDL と同一(<see cref="Migrations"/> は空)。
    /// </summary>
    public const string LatestDdl = """
        CREATE TABLE sync_folders (
            id                 TEXT    NOT NULL PRIMARY KEY,
            name               TEXT    NOT NULL,
            path               TEXT    NOT NULL COLLATE NOCASE UNIQUE,
            is_active          INTEGER NOT NULL DEFAULT 1,
            include_subfolders INTEGER NOT NULL DEFAULT 1,
            exclude_patterns   TEXT    NOT NULL DEFAULT '[]',
            last_scan          TEXT    NULL
        );

        CREATE TABLE images (
            id                TEXT    NOT NULL PRIMARY KEY,
            sync_folder_id    TEXT    NOT NULL REFERENCES sync_folders(id) ON DELETE CASCADE,
            relative_path     TEXT    NOT NULL COLLATE NOCASE,
            file_name         TEXT    NOT NULL,
            file_size         INTEGER NOT NULL,
            hash              TEXT    NOT NULL,
            status            TEXT    NOT NULL DEFAULT 'normal',
            candidate_link_id TEXT    NULL,
            created_date      TEXT    NOT NULL,
            modified_date     TEXT    NOT NULL,
            notes             TEXT    NULL
        );
        CREATE UNIQUE INDEX idx_images_folder_path ON images(sync_folder_id, relative_path);
        CREATE INDEX idx_images_folder_status ON images(sync_folder_id, status);
        CREATE INDEX idx_images_hash ON images(hash);

        CREATE TABLE tags (
            id          TEXT NOT NULL PRIMARY KEY,
            name        TEXT NOT NULL UNIQUE,
            type        TEXT NOT NULL,
            parent_id   TEXT NULL REFERENCES tags(id) ON DELETE SET NULL,
            color       TEXT NULL,
            description TEXT NULL
        );

        CREATE TABLE textual_tag_settings (
            tag_id            TEXT NOT NULL PRIMARY KEY REFERENCES tags(id) ON DELETE CASCADE,
            predefined_values TEXT NOT NULL DEFAULT '[]'
        );

        CREATE TABLE numeric_tag_settings (
            tag_id TEXT NOT NULL PRIMARY KEY REFERENCES tags(id) ON DELETE CASCADE,
            min    REAL NULL,
            max    REAL NULL,
            step   REAL NULL,
            unit   TEXT NULL
        );

        CREATE TABLE image_tags (
            image_id TEXT NOT NULL REFERENCES images(id) ON DELETE CASCADE,
            tag_id   TEXT NOT NULL REFERENCES tags(id) ON DELETE CASCADE,
            value    TEXT NULL,
            PRIMARY KEY (image_id, tag_id)
        );
        CREATE INDEX idx_image_tags_tag ON image_tags(tag_id);

        CREATE TABLE views (
            id              TEXT    NOT NULL PRIMARY KEY,
            name            TEXT    NOT NULL,
            is_favorite     INTEGER NOT NULL DEFAULT 0,
            sort_field      TEXT    NOT NULL DEFAULT 'name',
            sort_direction  TEXT    NOT NULL DEFAULT 'asc',
            display_columns TEXT    NULL,
            home_tag_id     TEXT    NULL,
            modified_at     TEXT    NOT NULL
        );

        CREATE TABLE view_conditions (
            id       TEXT NOT NULL PRIMARY KEY,
            view_id  TEXT NOT NULL REFERENCES views(id) ON DELETE CASCADE,
            tag_id   TEXT NULL REFERENCES tags(id) ON DELETE SET NULL,
            operator TEXT NOT NULL,
            value    TEXT NULL,
            value2   TEXT NULL
        );
        CREATE INDEX idx_view_conditions_view ON view_conditions(view_id);

        CREATE TABLE view_tag_hierarchies (
            id              TEXT    NOT NULL PRIMARY KEY,
            view_id         TEXT    NOT NULL REFERENCES views(id) ON DELETE CASCADE,
            tag_id          TEXT    NOT NULL REFERENCES tags(id) ON DELETE CASCADE,
            parent_id       TEXT    NULL REFERENCES view_tag_hierarchies(id) ON DELETE SET NULL,
            position        INTEGER NOT NULL DEFAULT 0,
            alias           TEXT    NULL,
            condition_type  TEXT    NULL,
            condition_value TEXT    NULL
        );
        CREATE INDEX idx_view_tag_hierarchies_view ON view_tag_hierarchies(view_id);
        """;

    /// <summary>
    /// 既存 DB への増分マイグレーション(REQ-004: 未適用分を ID 昇順で適用、各々 1 トランザクション)。
    /// V1 は初版のため空。スキーマ変更時はここへ追記し、LatestDdl も併せて更新する。
    /// </summary>
    public static IReadOnlyList<Migration> Migrations { get; } = [];
}
