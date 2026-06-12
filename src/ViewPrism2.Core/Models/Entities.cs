namespace ViewPrism2.Core.Models;

/// <summary>同期フォルダ(仕様 §2.1 REQ-010)。</summary>
public sealed record SyncFolder
{
    public required string Id { get; init; }
    public required string Name { get; init; }

    /// <summary>絶対パス。システム内一意(大文字小文字無視)。</summary>
    public required string Path { get; init; }

    public bool IsActive { get; init; } = true;
    public bool IncludeSubfolders { get; init; } = true;

    /// <summary>ファイル名の完全一致(大文字小文字無視)で除外。glob/regex は対象外(REQ-010)。</summary>
    public IReadOnlyList<string> ExcludePatterns { get; init; } = [];

    /// <summary>最終スキャン日時(ISO 8601 UTC)。初期 null。</summary>
    public string? LastScan { get; init; }
}

/// <summary>画像レコード(仕様 §2.0/§2.1)。実体ファイルは取り込みコピーしない(INV-009)。</summary>
public sealed record ImageRecord
{
    /// <summary>UUIDv4 小文字。生成後不変(INV-001)。</summary>
    public required string Id { get; init; }

    public required string SyncFolderId { get; init; }

    /// <summary>正規形(スラッシュ区切り・先頭末尾スラッシュなし)のみ格納(INV-005)。</summary>
    public required string RelativePath { get; init; }

    public required string FileName { get; init; }
    public long FileSize { get; init; }

    /// <summary>SHA-256 小文字 hex 64 文字(REQ-013)。</summary>
    public required string Hash { get; init; }

    public ImageStatus Status { get; init; } = ImageStatus.Normal;

    /// <summary>pending 時の再リンク候補(missing 行の id)(REQ-012 規則 3a)。</summary>
    public string? CandidateLinkId { get; init; }

    /// <summary>ISO 8601 UTC 文字列(INV-002)。</summary>
    public required string CreatedDate { get; init; }

    /// <summary>ISO 8601 UTC 文字列(INV-002)。</summary>
    public required string ModifiedDate { get; init; }

    public string? Notes { get; init; }
}

/// <summary>タグ(仕様 §2.2)。名前はシステム全体で一意・case-sensitive(REQ-021)。</summary>
public sealed record Tag
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required TagType Type { get; init; }

    /// <summary>単一親階層。親削除で null(REQ-022)。</summary>
    public string? ParentId { get; init; }

    /// <summary>^#[0-9A-Fa-f]{6}$ のみ受理、null 可(REQ-023)。</summary>
    public string? Color { get; init; }

    public string? Description { get; init; }
}

/// <summary>textual タグの型別設定(REQ-024)。</summary>
public sealed record TextualTagSettings
{
    public required string TagId { get; init; }

    /// <summary>順序保持の定義済み値リスト。付与値はリスト外も許可(入力補助のみ)。</summary>
    public IReadOnlyList<string> PredefinedValues { get; init; } = [];
}

/// <summary>numeric タグの型別設定(REQ-025)。すべて null 可。</summary>
public sealed record NumericTagSettings
{
    public required string TagId { get; init; }
    public double? Min { get; init; }
    public double? Max { get; init; }
    public double? Step { get; init; }
    public string? Unit { get; init; }
}

/// <summary>画像-タグ付与(REQ-026)。(ImageId, TagId) で高々 1 行(INV-003)。</summary>
public sealed record ImageTag
{
    public required string ImageId { get; init; }
    public required string TagId { get; init; }

    /// <summary>simple=null / textual=文字列 / numeric=InvariantCulture 数値文字列(INV-007)。</summary>
    public string? Value { get; init; }
}

/// <summary>仮想ビュー(仕様 §2.3 REQ-030)。</summary>
public sealed record View
{
    public required string Id { get; init; }
    public required string Name { get; init; }

    /// <summary>説明(REQ-030。v1.2 ビュー作成/編集ダイアログ=名前+説明)。null 可。</summary>
    public string? Description { get; init; }

    public bool IsFavorite { get; init; }
    public SortField SortField { get; init; } = SortField.Name;
    public SortDirection SortDirection { get; init; } = SortDirection.Asc;

    /// <summary>表示列定義(JSON 配列、REQ-042)。</summary>
    public string? DisplayColumns { get; init; }

    /// <summary>ホームタグ(階層ノード id、REQ-037)。解決不能ならルート。</summary>
    public string? HomeTagId { get; init; }

    /// <summary>本体・条件・階層のいずれの変更でも更新。閲覧では不変(REQ-032)。</summary>
    public required string ModifiedAt { get; init; }
}

/// <summary>ビューの絞り込み条件(仕様 §2.3 REQ-031)。全条件 AND 結合。</summary>
public sealed record ViewCondition
{
    public required string Id { get; init; }
    public required string ViewId { get; init; }

    /// <summary>対象タグ。タグ削除時は SET NULL(仕様 §2.0)。</summary>
    public string? TagId { get; init; }

    public required ConditionOperator Operator { get; init; }
    public string? Value { get; init; }
    public string? Value2 { get; init; }
}

/// <summary>ビュー内タグ階層のノード(仕様 §2.4 REQ-034)。</summary>
public sealed record HierarchyNode
{
    public required string Id { get; init; }
    public required string ViewId { get; init; }
    public required string TagId { get; init; }

    /// <summary>null=ルート直下。</summary>
    public string? ParentId { get; init; }

    /// <summary>同一親内 0 起点昇順。</summary>
    public int Position { get; init; }

    /// <summary>null なら tag.name を表示。</summary>
    public string? Alias { get; init; }

    public HierarchyConditionType? ConditionType { get; init; }

    /// <summary>condition_type 別 JSON(仕様 §2.4 のスキーマ)。</summary>
    public string? ConditionValue { get; init; }
}

/// <summary>NodeGraph の展開済みノード(仕様 §2.4 REQ-035、OC-2 出力)。</summary>
public sealed class GraphNode
{
    public required NodeKind Kind { get; init; }
    public required string DisplayName { get; init; }

    /// <summary>展開元の階層ノード id(ルートは null)。</summary>
    public string? HierarchyNodeId { get; init; }

    public string? TagId { get; init; }

    /// <summary>展開元タグの種別(ルートは null)。パス→条件変換(OC-3)が参照する。</summary>
    public TagType? TagType { get; init; }

    /// <summary>値ノード・一体型ノードの値。</summary>
    public string? Value { get; init; }

    /// <summary>展開元の階層ノードの値条件種別(numeric ノードの条件生成に使用、REQ-036)。</summary>
    public HierarchyConditionType? ConditionType { get; init; }

    /// <summary>展開元の階層ノードの condition_value JSON(仕様 §2.4 のスキーマ)。</summary>
    public string? ConditionValue { get; init; }

    public List<GraphNode> Children { get; } = [];
}

/// <summary>再リンク候補の表示情報(REQ-017)。relative_path 昇順で列挙される。</summary>
public sealed record RelinkCandidate
{
    public required string ImageId { get; init; }
    public required string RelativePath { get; init; }
    public long FileSize { get; init; }

    /// <summary>ISO 8601 UTC 文字列(INV-002)。</summary>
    public required string ModifiedDate { get; init; }
}

/// <summary>スキャン完了サマリ(REQ-015)。</summary>
public sealed record ScanSummary
{
    public int Added { get; init; }
    public int Missing { get; init; }
    public int Pending { get; init; }
    public int Updated { get; init; }
    public int Skipped { get; init; }
}

/// <summary>
/// アプリ設定(REQ-052 v1.3、M-SET-010 スキーマ)。破損時は既定値で起動。
/// v1.3/ECO-002 CR-1/5/6: グリッド列数キーは廃止(JsonIgnore — 書き出さず、
/// 旧ファイルに残存していても読み込まず無視する)。表示モード・最後に選択したコレクションを追加。
/// </summary>
public sealed record AppSettings
{
    public string Locale { get; set; } = "ja";

    /// <summary>ウィンドウ X 座標。null=OS 既定(初回起動)。</summary>
    public int? WindowX { get; set; }

    /// <summary>ウィンドウ Y 座標。null=OS 既定(初回起動)。</summary>
    public int? WindowY { get; set; }

    public int WindowWidth { get; set; } = 1200;
    public int WindowHeight { get; set; } = 800;
    public bool IsMaximized { get; set; }

    /// <summary>
    /// 廃止(REQ-052 v1.3/CR-1: 列数はレスポンシブ自動算出)。JSON へは読み書きしない。
    /// プロパティ自体は既存契約(既定値 4)の互換のため温存する。
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int GridColumns { get; set; } = 4;

    /// <summary>表示モード(REQ-052 v1.3/CR-6): "grid" または "list"。既定 grid。</summary>
    public string DisplayMode { get; set; } = "grid";

    public string? LastViewId { get; set; }

    /// <summary>最後に選択したコレクション(同期フォルダ)id(REQ-052 v1.3/CR-5)。null=未選択。</summary>
    public string? LastCollectionId { get; set; }
}
