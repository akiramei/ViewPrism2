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
            modified_at     TEXT    NOT NULL,
            description     TEXT    NULL
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

        CREATE TABLE image_features (
            image_id        TEXT    NOT NULL PRIMARY KEY REFERENCES images(id) ON DELETE CASCADE,
            phash           TEXT    NULL,
            file_size       INTEGER NULL,
            modified_date   TEXT    NULL,
            hash            TEXT    NULL,
            last_calculated TEXT    NULL,
            hash_adapter    TEXT    NULL
        );

        CREATE TABLE image_similarity (
            cache_key        TEXT    NOT NULL PRIMARY KEY,
            image_id1        TEXT    NOT NULL REFERENCES images(id) ON DELETE CASCADE,
            image_id2        TEXT    NOT NULL REFERENCES images(id) ON DELETE CASCADE,
            similarity_score INTEGER NULL,
            last_compared    TEXT    NULL
        );
        CREATE INDEX idx_image_similarity_image_id1 ON image_similarity(image_id1);
        CREATE INDEX idx_image_similarity_image_id2 ON image_similarity(image_id2);
        """;

    /// <summary>
    /// 既存 DB への増分マイグレーション(REQ-004: 未適用分を ID 昇順で適用、各々 1 トランザクション)。
    /// スキーマ変更時はここへ追記し、LatestDdl も併せて更新する。
    /// 注意: ALTER TABLE ADD COLUMN は末尾に列を足すため、LatestDdl 側でも同じ列順(末尾)になるよう定義し、
    /// 新規 DB とマイグレーション適用 DB のスキーマ同値(CP-DB-006)を保つ。
    /// </summary>
    public static IReadOnlyList<Migration> Migrations { get; } =
    [
        // v1.2: ビュー作成/編集ダイアログ=名前+説明(REQ-030 の description を views へ追加)
        new("001-views-description", "ALTER TABLE views ADD COLUMN description TEXT NULL;"),

        // v3.0: 類似検索の特徴量・類似度キャッシュ(REQ-063 / 仕様 §2.10.3)。
        // image_features(pHash 等) と image_similarity(ペア類似度キャッシュ)。両 FK→images CASCADE。
        // 索引: idx_image_similarity_image_id1/image_id2。ORB 列は作らない(ORB defer)。
        // LatestDdl と同値になるよう列順・索引名を揃える(CP-DB-006 スキーマ同値)。
        new("002-similarity-tables", """
            CREATE TABLE image_features (
                image_id        TEXT    NOT NULL PRIMARY KEY REFERENCES images(id) ON DELETE CASCADE,
                phash           TEXT    NULL,
                file_size       INTEGER NULL,
                modified_date   TEXT    NULL,
                hash            TEXT    NULL,
                last_calculated TEXT    NULL
            );

            CREATE TABLE image_similarity (
                cache_key        TEXT    NOT NULL PRIMARY KEY,
                image_id1        TEXT    NOT NULL REFERENCES images(id) ON DELETE CASCADE,
                image_id2        TEXT    NOT NULL REFERENCES images(id) ON DELETE CASCADE,
                similarity_score INTEGER NULL,
                last_compared    TEXT    NULL
            );
            CREATE INDEX idx_image_similarity_image_id1 ON image_similarity(image_id1);
            CREATE INDEX idx_image_similarity_image_id2 ON image_similarity(image_id2);
            """),

        // P-09: pHash adapter 世代交代(scaled-decode 採用)。image_features に hash_adapter を追加。
        // 既存行は NULL のまま=現行 adapter と不一致で stale 判定 → 次回検索で再計算され、
        // 連鎖無効化で旧 adapter 由来の類似度キャッシュも purge される(adapter をまたいだ値の混在防止)。
        // ALTER ADD COLUMN は末尾に列を足す。LatestDdl も image_features 末尾へ同じ列を定義しスキーマ同値を保つ。
        new("003-image-features-hash-adapter",
            "ALTER TABLE image_features ADD COLUMN hash_adapter TEXT NULL;"),
    ];
}
