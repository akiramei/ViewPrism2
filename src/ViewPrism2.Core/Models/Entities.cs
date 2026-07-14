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

/// <summary>
/// 作業スペース(ECO-020 / REQ-074)。名前付き・永続のユーザー管理画像集合。
/// デフォルト(<see cref="IsDefault"/>)は画像タブ作業モード「追加」の自動追加先で、同時に厳密に 1 つ(INV-W1)。
/// 所属画像は workspace_images(多対多・集合)。物理ファイル・画像 ID には触れない(INV-W4 / INV-009)。
/// </summary>
public sealed record Workspace
{
    /// <summary>UUIDv4 小文字。生成後不変(INV-001)。</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>自動追加先。同時に true は 1 つだけ(INV-W1・DB 部分 UNIQUE で担保)。</summary>
    public bool IsDefault { get; init; }

    /// <summary>作成順の連番(一覧の安定順・新規ほど大)。</summary>
    public int Seq { get; init; }

    /// <summary>ISO 8601 UTC 文字列(INV-002)。</summary>
    public required string CreatedAt { get; init; }
}

/// <summary>作業スペースと所属 normal 画像数(サイドバー一覧・ECO-020)。件数は status=normal のみ(INV-W2)。</summary>
public sealed record WorkspaceWithCount(Workspace Workspace, int NormalImageCount);

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

/// <summary>textual タグの型別設定(REQ-024, REQ-095)。</summary>
public sealed record TextualTagSettings
{
    public required string TagId { get; init; }

    /// <summary>順序保持の定義済み値リスト。付与値はリスト外も許可(入力補助のみ)。</summary>
    public IReadOnlyList<string> PredefinedValues { get; init; } = [];

    /// <summary>候補値リストの意味(REQ-095/ECO-086)。既定 Suggest。Closed でも付与は拒否しない。</summary>
    public TagValueDomain ValueDomain { get; init; } = TagValueDomain.Suggest;
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

    /// <summary>展開モード(REQ-096/ECO-086)。既定 Observed=REQ-035 の現行挙動(DB 列 NULL 互換)。</summary>
    public HierarchyExpansionMode ExpansionMode { get; init; } = HierarchyExpansionMode.Observed;

    /// <summary>件数 0 の定義値ノードを表示側で隠す(REQ-096/裁定 d)。既定 false=0 件も表示。</summary>
    public bool HideEmptyValues { get; init; }
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

    /// <summary>定義値展開(defined/defined_and_observed)由来の値ノード(REQ-096)。0 件淡色表示の判定に使う。</summary>
    public bool IsDefinedExpansion { get; init; }

    /// <summary>未定義値の検出ノード(REQ-095 closed の定義外付与値・REQ-096/裁定 c)。UI は通常ノードと区別する。</summary>
    public bool IsUndefinedValue { get; init; }

    /// <summary>展開元ノードの hide_empty_values(REQ-096/裁定 d)。表示側が件数 0 の定義値ノードを隠す判定に使う。</summary>
    public bool HideEmptyValues { get; init; }

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
/// <summary>
/// ビュー毎の表示モード記憶の値等価辞書(ECO-084/REQ-094)。
/// AppSettings(record)の全体等価契約(CP-SET-009 ラウンドトリップ)を保つため、
/// 内容一致で Equals する。JSON へは素の object map として往復する(Dictionary 派生)。
/// </summary>
public sealed class ViewDisplayModeMap : Dictionary<string, string>
{
    public override bool Equals(object? obj) =>
        obj is ViewDisplayModeMap other && Count == other.Count &&
        this.All(kv => other.TryGetValue(kv.Key, out var v) && string.Equals(v, kv.Value, StringComparison.Ordinal));

    // 等価なら必ず同数(キー順に依存しない安定ハッシュとして件数で十分 — 設定用途で衝突性能は無関係)
    public override int GetHashCode() => Count;
}

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

    /// <summary>表示モード(REQ-052 v1.3/CR-6): "grid" または "list"。既定 grid。画像タブ用(FL-004=D-b でタブ独立)。</summary>
    public string DisplayMode { get; set; } = "grid";

    /// <summary>作業タブの表示モード(FL-004=D-b・ECO-039): "grid"|"list"。null=未保存(初回は DisplayMode を初期値に読む)。</summary>
    public string? WorkTabDisplayMode { get; set; }

    public string? LastViewId { get; set; }

    /// <summary>
    /// ビュー毎の表示モード記憶(ECO-084/REQ-094): view_id → "all"|"unclassified"。
    /// デバイスローカル — ビュー定義(DB)には持たず、パッケージ(REQ-093)/スナップショット(REQ-092)
    /// では搬送しない(裁定①)。列挙外・欠落は読出し側で既定「すべて」へ落とす。
    /// 型は値等価の辞書(<see cref="ViewDisplayModeMap"/>)— AppSettings は record で、設定の
    /// ラウンドトリップ契約(CP-SET-009)が全体等価に依存するため、参照等価の素の Dictionary は使えない。
    /// </summary>
    public ViewDisplayModeMap ViewDisplayModes { get; set; } = new();

    /// <summary>最後に選択したコレクション(同期フォルダ)id(REQ-052 v1.3/CR-5)。null=未選択。</summary>
    public string? LastCollectionId { get; set; }

    /// <summary>スナップショット保存先(ECO-072/SS-002: アプリ共通)。null=既定(%APPDATA%/ViewPrism2/snapshots)。</summary>
    public string? SnapshotDirectory { get; set; }

    /// <summary>コレクションパッケージの管理フォルダ(ECO-074/案A)。null=既定(&lt;Documents&gt;/ViewPrism2/collections)。</summary>
    public string? CollectionPackageDirectory { get; set; }

    // ---- v2.0 追加(REQ-059 ビューア設定の永続化。M-SET-010 拡張) ----
    // 列挙系は文字列のまま格納し、型付き化と既定化は ViewerSettingsModel が担う(項目単位の破損耐性)。
    // 文字列系は TolerantStringConverter で「文字列以外の型・null」を許容し、不正値は項目単位で既定へ落とす
    // (ファイル全体を破損扱いにしない — CP-SET-009 v2.0)。

    /// <summary>表示モード(REQ-054): "normal"|"scroll"|"spread-right"|"spread-left"。既定 normal。</summary>
    [System.Text.Json.Serialization.JsonConverter(typeof(TolerantStringConverter))]
    public string ViewerMode { get; set; } = "normal";

    /// <summary>見開き高さ統一(REQ-058): "matchLargerHeight"|"matchSmallerHeight"|"noResize"。既定 noResize。</summary>
    [System.Text.Json.Serialization.JsonConverter(typeof(TolerantStringConverter))]
    public string ViewerResizeMode { get; set; } = "noResize";

    /// <summary>見開き垂直揃え(REQ-058): "top"|"middle"|"bottom"。既定 middle。</summary>
    [System.Text.Json.Serialization.JsonConverter(typeof(TolerantStringConverter))]
    public string ViewerAlignMode { get; set; } = "middle";

    /// <summary>ギャップ方式(REQ-058): "tight"|"loose"。既定 tight。</summary>
    [System.Text.Json.Serialization.JsonConverter(typeof(TolerantStringConverter))]
    public string ViewerGapMode { get; set; } = "tight";

    /// <summary>ギャップ px(REQ-059): 0〜100。範囲外・型不正の保存値は既定 0 として読む。</summary>
    [System.Text.Json.Serialization.JsonConverter(typeof(TolerantGapPxConverter))]
    public int ViewerCustomGapPx { get; set; }

    /// <summary>ページ送りモード(REQ-057): "doublePage"|"singlePage"。既定 doublePage。</summary>
    [System.Text.Json.Serialization.JsonConverter(typeof(TolerantStringConverter))]
    public string ViewerPageTurnMode { get; set; } = "doublePage";

    /// <summary>空白ページ開始(REQ-056)。既定 false。</summary>
    [System.Text.Json.Serialization.JsonConverter(typeof(TolerantBoolConverter))]
    public bool ViewerStartWithEmptyPage { get; set; }

    /// <summary>単一フィット方式(モック改善): "fit"|"width"|"one"。既定 fit。</summary>
    [System.Text.Json.Serialization.JsonConverter(typeof(TolerantStringConverter))]
    public string ViewerFitMode { get; set; } = "fit";

    /// <summary>キャンバス下地色(モック改善): "dark"|"light"|"checker"。既定 dark。</summary>
    [System.Text.Json.Serialization.JsonConverter(typeof(TolerantStringConverter))]
    public string ViewerBackground { get; set; } = "dark";

    /// <summary>縦スクロール横揃え(モック改善): "left"|"center"|"right"。既定 center。</summary>
    [System.Text.Json.Serialization.JsonConverter(typeof(TolerantStringConverter))]
    public string ViewerScrollHAlign { get; set; } = "center";

    // ---- ECO-022 追加(REQ-077 タグ制御モードの永続化。E-SETTINGS-013 拡張) ----
    // enableTagControl(既定 OFF)+ action→tag_id 6 個(既定すべて未割り当て=null)を settings.json へ保存。
    // 破損・欠損・未知値は安全な既定(OFF / 未割り当て)へ正規化(項目単位の破損耐性)。
    // 現存しない tag_id を指すマッピングは永続値を保持してよい(解決時は画像が当該 tag_id を持たず自然に無視)。

    /// <summary>タグ制御モード(REQ-077)。既定 OFF。OFF のときビューアは §2.9 と完全同一。</summary>
    [System.Text.Json.Serialization.JsonConverter(typeof(TolerantBoolConverter))]
    public bool EnableTagControl { get; set; }

    /// <summary>forceLeftPage に割り当てる tag_id(REQ-077)。null=未割り当て。</summary>
    [System.Text.Json.Serialization.JsonConverter(typeof(TolerantNullableStringConverter))]
    public string? TagActionForceLeftPage { get; set; }

    /// <summary>forceRightPage に割り当てる tag_id(REQ-077)。null=未割り当て。</summary>
    [System.Text.Json.Serialization.JsonConverter(typeof(TolerantNullableStringConverter))]
    public string? TagActionForceRightPage { get; set; }

    /// <summary>spread に割り当てる tag_id(REQ-077)。null=未割り当て。</summary>
    [System.Text.Json.Serialization.JsonConverter(typeof(TolerantNullableStringConverter))]
    public string? TagActionSpread { get; set; }

    /// <summary>skip に割り当てる tag_id(REQ-077)。null=未割り当て。</summary>
    [System.Text.Json.Serialization.JsonConverter(typeof(TolerantNullableStringConverter))]
    public string? TagActionSkip { get; set; }

    /// <summary>leftPageEmpty に割り当てる tag_id(REQ-077)。null=未割り当て。</summary>
    [System.Text.Json.Serialization.JsonConverter(typeof(TolerantNullableStringConverter))]
    public string? TagActionLeftPageEmpty { get; set; }

    /// <summary>rightPageEmpty に割り当てる tag_id(REQ-077)。null=未割り当て。</summary>
    [System.Text.Json.Serialization.JsonConverter(typeof(TolerantNullableStringConverter))]
    public string? TagActionRightPageEmpty { get; set; }

    /// <summary>
    /// ビューア設定の列挙系文字列を正規化する(CP-SET-009 v2.0)。読み込み後に呼び、
    /// 列挙外文字列・null を項目単位で既定値へ落とす(ViewerSettingsModel が唯一の真実)。
    /// customGapPx の範囲外は TolerantGapPxConverter が既に 0 化済み。
    /// </summary>
    public void NormalizeViewerSettings()
    {
        ViewerMode = Services.Viewer.ViewerSettingsModel.ToString(
            Services.Viewer.ViewerSettingsModel.ParseMode(ViewerMode));
        ViewerResizeMode = Services.Viewer.ViewerSettingsModel.ToString(
            Services.Viewer.ViewerSettingsModel.ParseResize(ViewerResizeMode));
        ViewerAlignMode = Services.Viewer.ViewerSettingsModel.ToString(
            Services.Viewer.ViewerSettingsModel.ParseAlign(ViewerAlignMode));
        ViewerGapMode = Services.Viewer.ViewerSettingsModel.ToString(
            Services.Viewer.ViewerSettingsModel.ParseGap(ViewerGapMode));
        ViewerPageTurnMode = Services.Viewer.ViewerSettingsModel.ToString(
            Services.Viewer.ViewerSettingsModel.ParseTurn(ViewerPageTurnMode));
        ViewerCustomGapPx = Services.Viewer.ViewerSettingsModel.NormalizeGapPx(ViewerCustomGapPx);
        ViewerFitMode = Services.Viewer.ViewerSettingsModel.ToString(
            Services.Viewer.ViewerSettingsModel.ParseFit(ViewerFitMode));
        ViewerBackground = Services.Viewer.ViewerSettingsModel.ToString(
            Services.Viewer.ViewerSettingsModel.ParseBackground(ViewerBackground));
        ViewerScrollHAlign = Services.Viewer.ViewerSettingsModel.ToString(
            Services.Viewer.ViewerSettingsModel.ParseScrollHAlign(ViewerScrollHAlign));
    }
}

/// <summary>
/// 文字列プロパティの寛容な読み取り(CP-SET-009 v2.0・REQ-052 破損耐性)。
/// JSON 値が文字列なら採用、それ以外の型(数値・真偽・null)はスキップして
/// プロパティ既定値を維持する(項目単位の既定化。ファイル全体を破損扱いにしない)。
/// </summary>
internal sealed class TolerantStringConverter : System.Text.Json.Serialization.JsonConverter<string>
{
    public override string? Read(
        ref System.Text.Json.Utf8JsonReader reader,
        Type typeToConvert,
        System.Text.Json.JsonSerializerOptions options)
    {
        if (reader.TokenType == System.Text.Json.JsonTokenType.String)
        {
            return reader.GetString();
        }

        reader.Skip(); // 文字列以外は無視(既定値が残る)
        return null;
    }

    public override void Write(
        System.Text.Json.Utf8JsonWriter writer,
        string value,
        System.Text.Json.JsonSerializerOptions options)
        => writer.WriteStringValue(value);
}

/// <summary>customGapPx の寛容な読み取り: 整数 0〜100 のみ採用、範囲外・型不正は 0(REQ-059)。</summary>
internal sealed class TolerantGapPxConverter : System.Text.Json.Serialization.JsonConverter<int>
{
    public override int Read(
        ref System.Text.Json.Utf8JsonReader reader,
        Type typeToConvert,
        System.Text.Json.JsonSerializerOptions options)
    {
        if (reader.TokenType == System.Text.Json.JsonTokenType.Number && reader.TryGetInt32(out var value)
            && value is >= 0 and <= 100)
        {
            return value;
        }

        reader.Skip(); // 範囲外・型不正は既定 0
        return 0;
    }

    public override void Write(
        System.Text.Json.Utf8JsonWriter writer,
        int value,
        System.Text.Json.JsonSerializerOptions options)
        => writer.WriteNumberValue(value);
}

/// <summary>bool の寛容な読み取り: 真偽値のみ採用、型不正は既定 false。</summary>
internal sealed class TolerantBoolConverter : System.Text.Json.Serialization.JsonConverter<bool>
{
    public override bool Read(
        ref System.Text.Json.Utf8JsonReader reader,
        Type typeToConvert,
        System.Text.Json.JsonSerializerOptions options)
    {
        if (reader.TokenType is System.Text.Json.JsonTokenType.True or System.Text.Json.JsonTokenType.False)
        {
            return reader.GetBoolean();
        }

        reader.Skip();
        return false;
    }

    public override void Write(
        System.Text.Json.Utf8JsonWriter writer,
        bool value,
        System.Text.Json.JsonSerializerOptions options)
        => writer.WriteBooleanValue(value);
}

/// <summary>
/// nullable 文字列の寛容な読み取り(ECO-022・REQ-077 タグ制御マッピングの破損耐性)。
/// JSON 値が文字列なら採用、それ以外の型(数値・真偽・JSON null・欠損)は null=未割り当て。
/// ファイル全体を破損扱いにせず、当該 action のみ未割り当てへ落とす。
/// </summary>
internal sealed class TolerantNullableStringConverter
    : System.Text.Json.Serialization.JsonConverter<string?>
{
    public override string? Read(
        ref System.Text.Json.Utf8JsonReader reader,
        Type typeToConvert,
        System.Text.Json.JsonSerializerOptions options)
    {
        if (reader.TokenType == System.Text.Json.JsonTokenType.String)
        {
            return reader.GetString();
        }

        reader.Skip(); // 文字列以外(数値・真偽・null)は未割り当て
        return null;
    }

    public override void Write(
        System.Text.Json.Utf8JsonWriter writer,
        string? value,
        System.Text.Json.JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteStringValue(value);
        }
    }
}
