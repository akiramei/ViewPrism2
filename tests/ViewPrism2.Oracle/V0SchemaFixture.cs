namespace ViewPrism2.Oracle;

/// <summary>
/// v0(初版)DDL の凍結スナップショット(Run 1 当時の DatabaseSchema.LatestDdl の複製)。
/// 治具修理(2026-06-11): S-05 が現行の DatabaseSchema.LatestDdl から v0 を導出していたため、
/// マイグレーション(001-views-description)導入後に「v0 なのに最新列を持つ」矛盾フィクスチャとなり
/// 偽陽性 FAIL を出した。オラクルのフィクスチャは現行コードに結合せず、本スナップショットに固定する。
/// 以後この定数は変更しない(真の v0 を表す)。
/// </summary>
internal static class V0SchemaFixture
{
    public const string InitialDdl = """
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
}
