# Change Order — ECO-073(implemented / golden待ち): コレクションの論理書き出し/取り込み(バックアップB層 V1)

> maintainer要求(2026-07-12)のバックアップ2層構成のうちB層。A層=DBスナップショット(ECO-072)と
> 対をなす。仕様は maintainer+外部アドバイザーとの事前議論で確定済みであり、本ECOはその
> 決定を台帳へ固定し、CAD mock(gate①)を待って製造に入る。

## 1. 要求(maintainer・2026-07-12)

- コレクション単位でバックアップしたい。リストア時、画像に付けたタグがタグ定義に存在しない
  場合の破綻(rev ずれ問題)を仕様で解決すること。
- 事前議論で確定した保証定義(仕様の核文):
  **論理インポートは、バックアップ時点のデータを現在のライブラリへ統合する機能であり、
  過去時点のタグ体系を厳密に再現する機能ではない。ライブラリ全体の時点復元にはDB
  スナップショット(ECO-072)を使用する。**
- UI用語は「書き出す/取り込む」(A層の「スナップショット作成/時点復元」と対比)。
  「選択的リストア」という表現は誤解を招くため使わない。
- UIモック(CAD)は maintainer が作成する。必要な画面情報は起票報告で提供する。

## 2. 工程診断

| 工程 | 判定 | 根拠 |
|---|---|---|
| CAD(ViewPrismUI) | **画面未定義・gate①必要** | `docs/screens/` に書き出し/取り込み/プレビュー&競合解決/結果レポートの画面は存在しない。mock 作成が製品コードに先行する。 |
| 要求・仕様 | **新機能、既存要求なし** | REQ/spec に論理エクスポート/インポート要求はない。既存の import/export 機構も皆無(DbMapping の JSON 変換はカラム内埋め込み用途のみ)。 |
| M-BOM・検査 | **未宣言** | 該当 E/M-BOM・CP なし。 |
| 実装 | **未実装・前提部品は既存** | `images.hash`=SHA-256 が全画像に NOT NULL で既存(`DatabaseSchema.cs:34`、ScanService が計算)。`images.file_size` も既存。`sync_folders.id`/`tags.id`/`images.id` はすべて UUID(TEXT PK)でポータブルIDの新設不要。タグは `name` UNIQUE+型別設定(textual/numeric)を持つ。 |
| DB | **マイグレーション1件が必要** | 永続タグマッピングテーブルと、DB内ライブラリUUID(メタデータ領域)が存在しない。両方を単一 migration で追加する(前例=ECO-020 方式)。 |

## 3. 切り分け済みの事実(事前議論での確定事項)

### 3.1 パッケージ形式(V1)

- **transport は単一 UTF-8 JSON ファイル**。`kind: "collection"` のみ。推奨拡張子は
  `*.viewprism2-collection.json` 形式。将来の ZIP コンテナは別 transport・別 `container_version`
  として追加し、V1 形式を流用しない。
- エンベロープ: `format` / `format_version` / `min_reader_version` / `features`(未知の必須
  feature のみ拒否・未知の任意フィールドは無視)/ `backup_id` / `source_library_id` /
  `created_at` / `app_version`(診断用)。**`schema_migrations` は互換性判定に使わない**
  (DB内部実装と論理形式を結合させない)。
- **自己完結**: コレクションが参照する全タグ定義の依存閉包(祖先チェーン・型別設定込み)を同梱。
- ポータブルIDは `source_id`(既存 UUID をそのまま使用)。絶対パスは識別子でなく
  `root_hint`(platform+path)としてのみ保存。
- **タグ値はフラット表現**: `string | null`。タグ定義が型情報の唯一の正。simple=`null`
  (**フィールド省略不可**)、textual=文字列、numeric=InvariantCulture 正規化済み数値文字列。
  JSON number は使わない。エクスポート時に正規形へ正規化(`4`/`4.0`/`04.000` の揺れを作らない)。
  桁区切り・カンマ小数点・NaN・Infinity・タグ設定範囲外値は拒否。数値の一致判定はパース後比較。
- 指紋: `fingerprint: { algorithm: "sha256", version: 1, value }`+任意 `size_bytes`
  (両方 DB 既存値から取得・書き出し時に画像ファイルを stat しない=オフラインでも書き出し可)。
- `relative_path` は `/` 区切りへ正規化。絶対パス・`..`・NUL・ルート外脱出を拒否。
  大小文字比較は復元先ファイルシステム規則に従う(書き出し側で潰さない)。

### 3.2 タグ照合規則(取り込み時)

1. **永続マッピング一致** → ローカルタグ存在確認+値互換性検証の上で採用(検証は省略しない)。
2. **UUID 一致** → 同一タグ。ただし差分を3分類: 表示差分(名前/色/説明=現行維持・報告のみ)、
   構造差分(親=現行維持・プレビュー表示)、**値互換差分(型/範囲/精度=値検証し不適合なら競合。
   黙ってクランプ・丸めしない)**。
3. **UUID 不一致・意味定義完全一致**(名前+型+型別設定+親の解決先)→ 自動マッピングし、
   対応関係を永続マッピングへ保存。
4. **名前衝突なし** → バックアップの UUID を維持して新規作成(親も再帰解決)。
5. **名前一致・意味定義不一致** → 競合。選択肢=中止/該当付与スキップ/別名取込/
   **既存タグへ手動マッピング**の4択。
- 値検証は二段階: バックアップ側定義に対する検証 → 取り込み先解決 → **現行側定義に対する再検証**。

### 3.3 画像照合規則(4状態)

| 状態 | 判定 |
|---|---|
| パス一致・SHA-256一致 | 自動採用 |
| パス一致・SHA-256不一致 | **競合(既定スキップ)**。「内容が変更された画像として取り込む」は競合画面の明示操作のみ |
| パス不一致・SHA-256が一意に一致 | 改名/移動として自動採用 |
| SHA-256が複数画像に一致 | 曖昧。自動選択しない |

- コレクション同定: `source_id`(UUID)一致を最優先、パスは候補提示のヒント。最終的な
  取り込み先ルートはユーザーが選択でき、別ルートなら relative_path をその配下へ解決。

### 3.4 マージ規則((image, tag) 単位・既定=追加型・削除なし)

| 現行 | バックアップ | 結果 |
|---|---|---|
| 付与なし | 付与あり | 追加 |
| 同じ値 | 同じ値 | 変更なし |
| 異なる値 | 値あり | 競合。既定は現行維持 |
| 付与あり | 記録なし | 変更しない |

- **破壊的「置換」は V1 では提供しない**(将来提供する場合は直前に A層スナップショット自動作成等を必須化)。

### 3.5 実行規律

- **書き出し**: 単一読み取りトランザクション(スナップショット読取)で全対象を取得し、
  自己矛盾ファイルを作らない。単一共有接続の semaphore を長時間占有しないため
  **書き出し専用の読み取り接続を別に開く**(WAL で可能)。`.partial` へ書き出し→flush→
  構造検証→アトミックリネーム。
- **取り込み**: 解析・プレビューはトランザクション外、実適用は単一トランザクション。
  プレビュー後に DB が変化していたら適用直前に再検証。**ドライラン(プレビュー)必須**:
  新規作成タグ/付け替えタグ/競合/未解決画像/取り込み先を実行前に提示。
- **読み込み・検証・プレビューも全件メモリ展開を前提にしない**(二回走査または一時 SQLite
  ステージング)。JSON プロパティ順に意味論を持たせない。
- 厳格拒否: 重複プロパティ・パッケージ内の重複タグ/画像ID・親タグ循環・存在しないタグ参照・
  不正パス・未知の必須 feature・過大文字列/異常ネスト・不正 numeric 値。手編集は「人間が読めるが、
  動作保証はスキーマ検証を通った場合に限る」に留める。

### 3.6 DB 変更(唯一の migration)

```sql
CREATE TABLE tag_import_mappings (
    source_library_id TEXT NOT NULL,
    source_tag_id     TEXT NOT NULL,
    local_tag_id      TEXT NOT NULL REFERENCES tags(id) ON DELETE CASCADE,
    created_at        TEXT NOT NULL,
    updated_at        TEXT NOT NULL,
    PRIMARY KEY (source_library_id, source_tag_id)
);
```

- あわせて **DB 内ライブラリ UUID**(新規メタデータ領域。設定ファイルではなく DB 内・
  スナップショット復元後も不変・インストール/マシンIDとは別物)を同一 migration で追加。
- 汎用多態マッピング(entity_kind 列)は FK を張れないため採用しない。将来は
  `view_import_mappings` 等を種類別に追加する。

### 3.7 スコープ外(V1)

- `tag-catalog`/`view-set`/`workspace` の単独書き出し/取り込みは対象外(全体復元は A層が担う)。
  ただしタグ依存閉包の列挙・親のトポロジカル解決・タグ照合と競合分類・永続マッピング参照更新・
  ドライラン計画生成は、後続 ECO から再利用可能な独立コンポーネント(TagImportPlanner 相当)として
  実装する(コレクション専用処理へ閉じ込めない)。
- 自動バックアップ・世代管理は対象外。

### 3.8 疑い・未検証(`/eco-fix` の probe で確定)

- System.Text.Json での重複プロパティ拒否(.NET 9+ の `AllowDuplicateProperties` 相当)と
  ストリーミング読み書き(`Utf8JsonReader`/`Utf8JsonWriter`)の実装形。
- 大規模コレクション(数万画像)でのプレビュー計画の保持方式(メモリ内 vs 一時 SQLite)の閾値。
- 書き出し専用読み取り接続と既存 `DatabaseManager` 直列化規律の共存形。

## 4. 是正方針(gate①)

仕様の実質はすでに maintainer 裁定済み(§3)。gate① で残るのは **CAD mock の作成と
UI 意匠の裁定**のみ。必要画面(名称・配置は CAD が正):

1. **書き出しダイアログ**: 対象コレクション選択・出力先・「画像ファイルは含まれません」明示・進捗。
2. **取り込み: ファイル選択→検証結果**: 形式/feature 互換・パッケージ概要(コレクション名・
   画像数・タグ数・作成日時・app_version)。
3. **取り込みプレビュー&競合解決**: タグ(新規作成/自動マッピング/競合4択+手動マッピング)、
   画像(4状態の件数と未解決一覧)、取り込み先ルート選択、マージ規則の要約表示。
4. **結果レポート**: 追加/変更なし/スキップ/競合解決の件数、未解決画像一覧。

規模: 大(Infrastructure エクスポータ/インポータ+TagImportPlanner+migration+UI 4面+CP 群)。
必要なら /eco-fix 段階でエンジン(書き出し+照合+ドライラン)と UI の2段に分けて受け入れる。

## 5. 影響BOM

- CAD: ViewPrismUI へ新画面4面(mock=maintainer 作成、`docs/screens/` 追加が先行)。
- 要求/仕様: 新 REQ 群(パッケージ形式・タグ照合・画像照合・マージ規則・実行規律)、
  `20-spec.md` 新節(§1 の核文と §3.1〜3.5 の決定文を含む)。
- E-BOM: 新規(パッケージエクスポータ/インポートプランナ(TagImportPlanner)/インポート適用器)。
- K-BOM: 追加依存なし予測(System.Text.Json は BCL)。
- M-BOM: 新規(Infrastructure エクスポート/インポート+App UI 4面)+ migration 1件
  (tag_import_mappings+ライブラリUUID。CP-DB-006 スキーマ同値を維持)。
- 検査: 新 CP — 事前議論で確定した受け入れテスト一覧を転記: 同一ファイル2回取込の冪等性/
  UUID一致リネームは現行名維持/UUID一致・型変更は事前競合停止/意味定義完全一致の自動マッピング/
  名前一致・範囲違いは自動マッピングしない/循環・欠落親の拒否/パス不一致→一意指紋解決/
  指紋複数一致は自動選択しない/破損・途中切れ・未知必須featureでDB無変更/例外・キャンセルで
  全ロールバック/画像ファイル無変更/プレビュー後DB変化時に旧プラン不実行。unit+一時DB fixture
  で機械検査、UI は golden。
- Oracle: 既存固定 Oracle 行は変更しない(R6)。

## 6. 残ゲート

1. ~~**gate① CAD mock 作成**(maintainer。画面情報は §4 と起票報告で提供済み)+
   `docs/screens/` への画面正典追加。~~ → CAD正典化+「参照のみ登録」裁定で完了(§7)
2. ~~**SS-001(UI 入口)の裁定**~~ → 裁定済み(2026-07-12)=(b) 分置: B層=画像タブ ⋯ メニュー。
3. ~~`/eco-fix ECO-073` で probe(§3.8)→migration→エンジン→UI→機械受入。~~ → 完了(§8。
   2段受入を採用: 第1段=migration+エンジン、第2段=UI 4面。EX-002=過半で確定、EX-003/場所を指定は
   V1 非搭載を沈黙次元へ裁定記録、EX-004=B-4 4区分で確定)
4. **gate② golden**: 書き出し→別状態のライブラリへ取り込み→プレビュー→競合解決→結果の実機確認(§8.6)。
5. `/eco-accept ECO-073` でクローズ。

## 8. 実施記録(2026-07-12 — 機械受入完了・golden待ち)

### 8.1 先行probe(R5)

- `CpPackage073Tests` へ Exporter/Importer/TagImportPlanner の存在(reflection)と migration
  `008-collection-package` の存在を要求する probe を製品コード変更前に追加した。
- 是正前実測は `ViewPrism2.Tests` **622件中2件不合格**(未実装を確認してから着手)。

### 8.2 §3.8 疑いの実測結果

- 重複プロパティ拒否は JsonNode(JsonObject.Add が重複キーで throw)+トップレベル seen-set で実装。
  images のストリーミングは増分 Utf8JsonReader ポンプ(JsonPump)で 1 要素ずつ生成し、
  途中で切れたファイルは JsonException→PackageFormatException で拒否される(実測緑)。
- プレビュー計画はタグ計画(小)+画像 5 状態カウント+未解決サンプル上限 100 で保持し、
  一時 SQLite ステージングは不要だった(images は 3 回のストリーム走査: 件数/プレビュー/適用)。
- 書き出し専用読取接続(`Mode=ReadOnly;Pooling=False`+単一トランザクション)は共有接続と
  共存し、WAL スナップショットで同一時点の自己無矛盾ファイルになることをテストで確認。
- Utf8JsonWriter は既定で非 ASCII を \uXXXX エスケープする — 「人間が読める」形式契約のため
  relaxed エスケープを採用した(UTF-8 ファイルへの出力では安全)。

### 8.3 是正裁定とdiff(2段受入)

- **第1段(migration+エンジン)**: migration `008-collection-package`(library_metadata+
  tag_import_mappings・LatestDdl 同値=CP-DB-006 緑維持)。`LibraryIdentity`(DB内ライブラリUUID・
  実行時冪等シード)。`CollectionPackage.cs`(形式定数/DTO/PackageJson ストリーミング読取+厳格拒否)。
  `TagImportPlanner`(**Core/Services/Package**・照合5段の純粋計算・§3.7 の再利用可能コンポーネント)+
  `TagValueFormat`(正規形・二段階検証・パース後比較)。`CollectionPackageExporter`(専用読取接続・
  2カーソルマージ結合ストリーム・.partial→構造検証→アトミック確定)。`CollectionPackageImporter`
  (ReadHeader/Preview/Apply=単一トランザクション・追加型マージ・missing 参照登録・鮮度再計画・過半ガード)。
- **第2段(UI 4面)**: B-1=`CollectionExportWindow`+VM(実進捗 done/total・キャンセル・既定名)。
  B-2〜B-4=`CollectionImportWindow`+VM(stepper・互換NG別面・競合行4択+型互換割当 ComboBox・
  画像5状態タイル・未解決一覧+missing 説明・過半警告+確認チェック・結果4タイル)。入口=画像タブ
  ⋯ メニュー2行(`ExportCollection/ImportCollectionCommand`・SS-001(b))+`WindowService.
  ShowCollectionExport/ImportAsync`+Save/OpenFilePicker。i18n `package.*` 57キー(ja/en)。
- **V1 差分の裁定記録(32-mbom 沈黙次元)**: B-3「取り込み先ルート変更」=非搭載(入口コレクション固定・
  誤ルートは過半ガード+別コレクションから取り込み直し)。「場所を指定」=非搭載(missing 登録→既存
  修復導線で解決)。競合行「中止」=選択解除(全体中止はフッター)。いずれも golden で maintainer 確認対象。
- **M4 同期**: REQ-093、仕様 §2.14(核文込み)、E-PACKAGE-047/E-UI-PACKAGE-048、
  M-PACKAGE-042/M-UI-PACKAGE-043+沈黙次元7行、CP-PACKAGE-032(unit)+CP-UI-G13(golden)。
- 既存画面の視覚・Core 既存意味論・既存 Oracle 期待値は変更していない。DB 変更は migration 008 のみ。

### 8.4 機械受入

- 先行probe+挙動テスト12本(冪等/照合5段/5状態/厳格拒否/ロールバック/スキャン生存+規則3a/鮮度/配線)を
  含む `ViewPrism2.Tests`: **634/634 pass**。
- `dotnet build ViewPrism2.sln --no-restore`: **0 warning / 0 error**。
- `ViewPrism2.Oracle`: **109 pass / 2 known skip**(R6 不変。CP-DB-006 スキーマ同値=migration 008 込みで緑)。
- `python bomdd/validate_bom.py`: **0 error / 0 warning**。

### 8.6 gate② golden 操作手順(CP-UI-G13)

準備: コレクション A(タグ付き画像あり)とコレクション B(空 or 別内容)を用意。

1. **書き出し(B-1)**: コレクション A の ⋯ メニュー「コレクションを書き出す…」→ コレクション名+件数・
   出力先の既定名 `<名前>.viewprism2-collection.json`・画像非含有 callout を確認 → 書き出す →
   実進捗(done/total)→ 完了。出力先に `.partial` が残っていない。コレクション未選択時は ⋯ 行が無効。
2. **取り込み・冪等(B-2〜B-4)**: 同じコレクション A へ同ファイルを取り込む → B-2 で互換OK+概要
   (名前/画像数/タグ数/作成日時/app_version)→ B-3 でタグ競合 0・画像は全て「一致」→ 実行 →
   B-4 で追加 0/変更なし N。DB が増えていない(冪等)。
3. **別コレクションへの取り込み**: コレクション B の ⋯ から取り込み → B-3 で未解決(missing で登録)
   が数えられ、**過半警告+確認チェック**が出る → チェックして実行 → B-4 の件数と missing 登録一覧 →
   画像タブに missing 行が現れ、タグ付与も付いている。
4. **タグ競合の 4 択**: B のタグ辞書に「同名・範囲違い」の numeric タグを作ってから取り込み →
   B-3 に競合行(要対応バッジ)・**未解決の間は実行ボタン無効+フッターに件数注記** →
   「別名で取込」→ 解決済みバッジ+要約 → 実行 → 別名タグが作成される。
   「既存へ割当」の候補が**型互換タグのみ**であること。
5. **互換性 NG(B-2 別面)**: パッケージ JSON の `"minReaderVersion": 1` を手で 99 に書き換えて選択 →
   赤系の「このパッケージは取り込めません」面(閉じる/別のファイルを選ぶ)。
6. **移動検出**: A の画像ファイル 1 枚を別サブフォルダへ移してスキャン後に取り込み → 「移動を検出
   (自動追随)」に数えられ、付与が新パスの画像へ着地する。
7. **鮮度**: B-3 プレビュー表示中に別途競合する同名タグを作成 → 実行 → 失敗メッセージ(DB 無変更)。
8. **物理非破壊**: 書き出し/取り込みの前後で画像ファイル・サムネイルが変更されない(エクスプローラ確認)。
9. **V1 差分の確認**: B-3 に「取り込み先ルート変更」「場所を指定」が無いこと(入口コレクション固定・
   missing→修復導線で解決)を許容できるか判定する(32-mbom 沈黙次元の裁定記録)。
10. ja/en 切替で B-1〜B-4 の文言が追随(⋯ メニュー行は既存流儀=ja 直書き)。既存画面に視覚回帰がない。

## 9. golden所見 GF-073-01 の是正(2026-07-12 — 再機械受入)

### 9.1 所見と工程診断

- maintainer が実機 B-1 と mock を並置比較し 4 点を指摘: ①「コレクションを書き出す」が
  Window.Title と本文見出しに**重複**(mock は擬似タイトルバーのみ=実装では Window.Title が対応。
  本文へ置いたのは実装の発明) ②コレクションカードの**フォルダグリフ欠落**でテキストが密集
  ③**ボタンテキストが左寄せ**(Avalonia Button の既定 HorizontalContentAlignment=Left の取り漏れ)
  ④キャンセルが**テーマ既定グレー**(mock の視覚言語=白+ボーダーの outline と不整合)。
- 工程診断: GF-072-01 と同族(視覚言語の拘束不足)だが、captures 同梱後も「タイトルの
  所在(擬似タイトルバー=Window.Title)」「ボタンの整列・二次操作の配色」という**ダイアログ共通言語**の
  次元が個別画面検査から漏れた。V1/V2 許容差分(擬似タイトルバー)の裏面=「では本文には何を
  置かないか」が沈黙次元だった。

### 9.2 先行probe(R5)

- `GfPackageVisualParityTests`(headless・CP-UI-G13)を製品コード変更前に追加:
  ①本文にタイトル文言の TextBlock が 0 個 ②collectionGlyph(Path)存在 ③footerBtn の
  HorizontalContentAlignment=Center ④outline ボタンの背景=白。
- 是正前実測: **1 件不合格**(4 観点とも欠落を確認)。

### 9.3 是正diff

- `CollectionExportWindow`: 本文見出しを撤去(タイトルは Window.Title のみ)。コレクションカードへ
  フォルダグリフ(角丸スクエア #EEF1F7+Path)+余白を追加し白カード化。出力先も白カード化。
  Window スタイルで全ボタン中央揃え+outlineButton(白+#D6E0EE)。キャンセル/閉じるを outline へ。
- `CollectionImportWindow`(同族水平展開): 本文見出し撤去・全ボタン中央揃え・キャンセル/戻るを
  outline へ。挙動・VM・エンジンは不変(視覚のみ)。

### 9.4 再機械受入

- `ViewPrism2.Tests`: **635/635 pass**(GF probe 緑転)。`dotnet build`: 0 warning / 0 error。
- `ViewPrism2.Oracle`: 109 pass / 2 known skip(R6 不変)。`validate_bom`: 0/0。

### 9.5 gate②再操作(§8.6 に追加)

11. B-1/取り込みウィザードとも、タイトルがウィンドウタイトルのみで本文に重複せず、コレクションカードに
    フォルダグリフ、フッターボタンが中央揃え・キャンセル/戻るが白 outline であることを captures と
    並置確認する。

## 7. gate①裁定(2026-07-12)

- maintainer裁定: **未解決画像(一致先なし)は「missing 行として参照のみ登録」を既定採用**し、
  §3.3 の「一致しない場合は未解決としてスキップまたはユーザー指定」を次のとおり更新する:
  既定=missing 行として参照登録(タグ付与も着地)、「場所を指定」で即時解決も可。
  採用条件は以下 4 点:
  1. **対象は「見つからない」のみ**。「パス一致・ハッシュ不一致」(競合・明示操作でのみ取込)と
     「ハッシュ複数一致」(曖昧・ユーザー指定のみ)は参照登録の対象外とし、曖昧さを固定化しない。
  2. **リンクは自動確定しない**。登録した missing 行は既存の規則 3a(`ScanJudge.cs:71-76`)により
     次回スキャンで pending+candidate_link_id の候補として自動提示され、修復画面(ECO-005)での
     確定で image_id 不変(INV-001)・タグ保持のままリンクされる。登録 status は **missing 固定**
     (pending で作ると次回スキャンの手順 5 で行削除されタグが消える — `ScanService.cs:163` 実測済み)。
  3. **パッケージの画像エントリに `file_size`・`created_date`・`modified_date` を必須同梱**
     (missing 行の NOT NULL 列を満たすため。§3.1 の「size_bytes 任意」を上書き。
     いずれも DB 既存値で書き出し時に stat 不要=オフライン書き出し性は維持)。
  4. **過半ガード**: 未解決が過半を占める場合は取り込み先ルートの指定ミスとして警告し、
     確認を挟んでから実行する(具体閾値=EX-002 は fix で確定)。
- 受入への追加(fix 時に CP へ固定): 参照登録した missing 行が後続スキャンで削除されない・
  同ハッシュ出現時に規則 3a の候補として提示される・確定時にタグが保持される。
- **CAD正典化**: ViewPrismUI `1fc5aaa38dd2` `docs/screens/snapshot_export_import.md`
  (B-1 書き出し/B-2 検証/B-3 プレビュー&競合解決/B-4 結果レポート。A層と1画面CAD集約)。
  一次資料=`資料/スナップショット・書き出し取り込み/`(SHA-256 `5104C8CE…2DC36CE7`)。
  タグ「既存へ自動対応」の同一判定規則は本 ECO(§3.2)が正であることを CAD 側にも明記済み。
- CAD レビュー項目のうち EX-001(ビュー非同梱)は §3.7 で裁定済み(view-set=将来 ECO)。
- 残: SS-001(UI 入口)のみ maintainer 裁定が必要(§6-2)。
- gate①完了。次の明示入口は `/eco-fix ECO-073`。本裁定では src/tests を変更しない。
