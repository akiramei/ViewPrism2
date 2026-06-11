# Factory Run 2 報告 — ViewPrism2 (loop-v1-core)

- 製造装置: factory-01
- 実施日: 2026-06-11
- 範囲: M-GRAPH-003 / M-DB-007 / M-SCAN-005 / M-VIEWSVC-012 / M-HARNESS-015(CP-GRAPH-002 / CP-DB-006 / CP-SCAN-004 / CP-TAG-011 / CP-VIEW-012)
- 隔離規律: 遵守(41-fixed-oracle.yaml・42-exploratory-probes.yaml・原典 view-prism・BomDD リポジトリは未参照。bomdd/・docs/ 既存ファイルは未変更。本報告の新規作成のみ)
- cheat 分類: Run 2 指示書供与の定義を使用 — C1=表現ギャップ / C2=暗黙知 / C3=工程欠落 / C4=受入不能 / C5=粒度崩壊 / C6=手戻り

## 1. 製造単位 → 成果物パス対応表

| 製造単位 | 成果物 |
|---|---|
| M-GRAPH-003 | `src/ViewPrism2.Core/Services/NodeGraphBuilder.cs`(OC-2。ITagValueSource 契約・GraphWarning・NodeGraphResult・展開規則 0/1/2 件・condition_type 値制限・循環打ち切り・参照切れスキップ・ResolveHome)/ `Services/PathConditionConverter.cs`(OC-3)/ `Services/HierarchyConditionValue.cs`(condition_value JSON スキーマ読取)/ `Services/TagValueIndex.cs`(ITagValueSource 実装、Normal 限定)/ `Models/Entities.cs` の GraphNode 確定(TagType/ConditionType/ConditionValue 追加 — Run 1 CHEAT-013 の確定) |
| M-DB-007(Core 側) | `src/ViewPrism2.Core/Repositories/`(ISyncFolderRepository / IImageRepository / ITagRepository / IViewRepository) |
| M-DB-007(Infrastructure 側) | `src/ViewPrism2.Infrastructure/Database/`(DatabaseSchema.cs=DDL 定数+Migration 型 / MigrationRunner.cs=REQ-004 意味論 / DatabaseManager.cs=単一接続+SemaphoreSlim+接続毎 PRAGMA / DbMapping.cs=enum⇔小文字トークン / SyncFolderRepository.cs / ImageRepository.cs / TagRepository.cs(UPSERT=ON CONFLICT DO UPDATE)/ ViewRepository.cs)。日時は TEXT のまま(DateTime へマップしない)、COLLATE NOCASE、FK で CASCADE/SET NULL |
| M-SCAN-005 | `src/ViewPrism2.Core/Services/ScanJudge.cs`(OC-5 純粋関数。ScanFileFacts=ハッシュ遅延計算 / ScanDbFacts / ScanDecision)/ `src/ViewPrism2.Infrastructure/Scanning/ScanService.cs`(手順 1〜6・K-WINFS 列挙規約・二重起動拒否・読み取り不能スキップ+警告ログ・last_scan 例外時更新)/ `Scanning/RelinkService.cs`(候補列挙 relative_path 昇順+確定=id 不変)+ `Models/Entities.cs` に RelinkCandidate |
| M-VIEWSVC-012 | `src/ViewPrism2.Core/Services/TagService.cs`(バリデーション=名前空白・重複(case-sensitive)・color 形式・numeric 範囲・循環 / UPSERT 付与 / 原子バッチ / 冪等解除 / GetAllWithUsageAsync)/ `Services/ViewService.cs`(CRUD・modified_at 規則・お気に入り/最近・条件/階層ノード CRUD+MoveNode 循環拒否) |
| M-HARNESS-015(Run 2 追加分) | `tests/ViewPrism2.Tests/`(TempDb.cs=一時ファイル DB フィクスチャ・CpGraph002Tests.cs・CpDb006Tests.cs・CpScan004Tests.cs(一時ディレクトリ+実ファイル)・CpTag011Tests.cs・CpView012Tests.cs。全テストに `[Trait("cp", "CP-xxx")]`) |
| 調達 | Infrastructure へ Dapper 2.1.79 / Microsoft.Data.Sqlite 10.0.9 / Microsoft.Extensions.Logging 10.0.*(いずれも調達表記載内)。SkiaSharp / Serilog 系は該当製造単位の Run で追加予定 |

Run 2 対象外(未着手・計画どおり): M-THUMB-008 / M-I18N-011(資産統合)/ M-UI-013 / M-UI-014、および CP-THUMB-007 / CP-L1-SMOKE / CP-UI-G1〜G5。

## 2. 受入実行ログ要約

- `dotnet build ViewPrism2.sln -c Release`(Rebuild)→ **成功(警告 0・エラー 0)**(TreatWarningsAsErrors=true)
- `dotnet test tests/ViewPrism2.Tests -c Release` → **全 132 件成功(不合格 0・スキップ 0)**、実行時間 1.2s
- Run 1 の 63 件は全件退行なし

| CP | depth | テスト数 | 結果 | 備考(test_vectors 被覆) |
|---|---|---|---|---|
| CP-GRAPH-002 | unit | 18 | PASS | simple 1 ノード+子接続・textual 値 0/1/2 件境界(0=タグ名のみ+exists、1=一体型「タグ名: 値」、2=値ノード序数昇順+子複製)・Normal 限定値抽出(INV-010)・自由入力値・values 制限・alias・position 順・パス→条件([root,simple,値]→[exists,equals]・一体型=equals・numeric range=between・numeric equals=equals)・循環打ち切り+警告(FMEA-008、相互参照と自己親)・参照切れスキップ・ホームタグ 3 ケース |
| CP-DB-006 | L2 | 8 | PASS | journal_mode=wal/foreign_keys=1・migrations 行数=定義数+全 id 記録・v0 DB+全適用=新規 DB とスキーマ同値(PRAGMA table_info/foreign_key_list/index_list ダンプ比較)・ランナー機構の合成マイグレーション実検査(ID 昇順適用・冪等)・タグ削除カスケード 4 テーブル(FMEA-003/005)・フォルダ削除連鎖・path UNIQUE case-insensitive('C:/a' vs 'c:/A')+制約実動・COLLATE NOCASE 付与確認 |
| CP-SCAN-004 | unit〜L2 | 20 | PASS | 判定器純粋規則 4 本(規則 1 Skip=ハッシュ未計算・規則 2 UpdateMeta・3a 候補 id 昇順先頭・3b 初回は missing 一致でも AddNormal)+サービス 16 本(新規/スキップ・規則 2 status 不変・手順 4 missing 化・手順 5 pending 清掃・拡張子大文字/.txt/.svg 除外・exclude_patterns 完全一致大文字小文字無視・ロックファイル skipped 計上+DB 不変+次回再試行(FMEA-011)・リネーム→missing+pending→再リンク確定で id/タグ保全(FMEA-004)・候補複数 id 昇順・候補列挙 relative_path 昇順・遷移表外拒否・二重起動 ScanInProgress・last_scan 例外パス更新・無効/未登録拒否・サブフォルダ除外) |
| CP-TAG-011 | unit | 14 | PASS | 重複名 DuplicateTagName+'Tag'/'tag' 別名・空白名 ValidationError・color '#1A2b3c' 受理/'red'/'#GGGGGG' 拒否/NULL 可・numeric min=1,max=5 境界(1・5 受理/0・6 拒否)+設定なし任意・predefined_values 順序ラウンドトリップ+リスト外付与許可・UPSERT 行数 1+値上書き・解除冪等・バッチ 3 中 1 失敗で全ロールバック(0 適用)+成功時全適用・自己親/孫を親 CircularReference・削除カスケード 4 状態・simple/textual 型規則・一覧 OrdinalIgnoreCase 昇順+distinct 使用数 |
| CP-VIEW-012 | unit | 9 | PASS | 空白名 ValidationError(作成/更新)・ビュー削除で条件+階層の孤児ゼロ・modified_at 更新トリガ(本体/条件追加/ノード追加・移動/alias 変更)と閲覧不変(FakeClock)・お気に入り name 昇順/最近 modified_at 降順 limit+同値 id 昇順・ノード移動 自己/子孫 CircularReference・ノード削除で子 SET NULL・condition_type/value ラウンドトリップ・display_columns(basic+tag 列)ラウンドトリップ+ソート既定値 |
| Run 1 既存 7 CP | unit/L3 | 63 | PASS | 退行なし |
| 合計 | — | **132** | **全件 PASS** | |

## 3. ずる報告(cheat-log 全件)

Run 1 からの通し番号(CHEAT-014 以降)。

### CHEAT-014 [C1] 仕様 §2.1 の手順順序とリネーム遷移(FMEA-004)の矛盾
- 手法が与えなかったもの: 手順を 3(判定)→4(missing 化)の順で字義どおり実行すると、リネームの単一スキャンで新ファイル判定時に missing 行が存在せず AddNormal になり、CP-SCAN-004 ベクタ「スキャン → missing+pending」・FMEA-004(タグ関連喪失の防止)と両立しない
- 代替した判断(何をどう埋めたか): 手順 4(missing 化)・5(pending 清掃)を判定(手順 3)の前に適用し、規則 3a の照合対象を missing 化後の状態とした。遷移表・他ベクタへの影響なし
- 重大度: friction

### CHEAT-015 [C1] M-GRAPH-003 の BuildGraph 戻り値と警告チャネルが単一シグネチャで両立しない
- 手法が与えなかったもの: interface_contract は `GraphNode BuildGraph(...)` だが invariants は「打ち切り+警告」を要求(Run 1 CHEAT-004 と同型)
- 代替した判断(何をどう埋めたか): `NodeGraphResult { GraphNode Root; IReadOnlyList<GraphWarning> Warnings }` の複合戻り値に統合
- 重大度: minor

### CHEAT-016 [C1] textual 値 0 件時の階層子ノードの接続先が未規定
- 手法が与えなかったもの: REQ-035 は 1 件=一体型配下・2 件以上=各値ノード配下を規定するが、0 件時の子の接続先が無い
- 代替した判断(何をどう埋めたか): タグ名ノードの配下に接続(値数 1→0 の変化と連続的な構造)
- 重大度: minor

### CHEAT-017 [C1] numeric ノードの condition_type が NULL・不正な場合のパス→条件規則が未規定
- 手法が与えなかったもの: REQ-036 は numeric ノード=「condition_type に応じ equals または between」のみ規定
- 代替した判断(何をどう埋めたか): condition_type が NULL・condition_value が不正・Pattern/Values 指定の場合は exists へフォールバック(INV-008 の趣旨を準用)
- 重大度: minor

### CHEAT-018 [C1] GraphNode の確定フィールド(Run 1 CHEAT-013 の確定)
- 手法が与えなかったもの: OC-3 が入力を「ノード列」のみとするため、変換に必要なタグ種別・条件種別がノード自身に必要だが、フィールド定義が無い
- 代替した判断(何をどう埋めたか): GraphNode に TagType / ConditionType / ConditionValue を追加して確定した
- 重大度: minor

### CHEAT-019 [C1] OC-3 出力 ViewCondition の Id / ViewId(合成条件の同一性フィールド)が未規定
- 手法が与えなかったもの: 評価(OC-1)には TagId/Operator/Value(2) のみ効くが、ViewCondition 型は Id/ViewId が必須
- 代替した判断(何をどう埋めたか): Id=`{階層ノードid}#{index}`(決定的・警告紐付け用)、ViewId=空文字列
- 重大度: minor

### CHEAT-020 [C2] 参照切れタグノードの「スキップ」範囲
- 手法が与えなかったもの: ノード単体をスキップして子を昇格するか、枝ごと落とすかが未規定
- 代替した判断(何をどう埋めたか): 配下の枝ごとスキップ(子の昇格はしない)+ MissingTag 警告
- 重大度: minor

### CHEAT-021 [C2] condition_value が不正(JSON 破損・不正 regex)な場合の値制限の扱い
- 手法が与えなかったもの: 値制限が適用不能な場合の規則
- 代替した判断(何をどう埋めたか): REQ-031 の不正 regex 規則を準用し「制限結果 0 件+警告」。pattern のマッチは K-REGEX 規約(部分一致・1 秒タイムアウト)。range は textual 値にも数値比較で適用(変換不能値は除外)
- 重大度: minor

### CHEAT-022 [C2] REQ-004「初版 DDL」と migrations 一覧の V1 構成
- 手法が与えなかったもの: V1 初版時点の migrations 定義(初版 DDL 自体を migration 001 とするか、ベースラインとするか)
- 代替した判断(何をどう埋めたか): LatestDdl=初版 DDL・Migrations=空(以後のスキーマ変更で追記)とし、新規 DB=LatestDdl+全既適用マーク。ランナー機構自体は受入で合成マイグレーション(2 件・ID 昇順・冪等)により実検査した
- 重大度: minor

### CHEAT-023 [C1] textual_tag_settings / numeric_tag_settings の FK が仕様 §2.0 の列挙に無い
- 手法が与えなかったもの: 型別設定テーブルの参照整合性規則
- 代替した判断(何をどう埋めたか): tags(id) への ON DELETE CASCADE(タグ削除で設定孤児を残さない)
- 重大度: minor

### CHEAT-024 [C2] 索引の選定と images の一意性
- 手法が与えなかったもの: M-BOM は sync_folders.path の UNIQUE のみ規定。他の索引・images の相対パス一意性は未規定
- 代替した判断(何をどう埋めたか): UNIQUE INDEX(sync_folder_id, relative_path)(COLLATE NOCASE 列のため case-insensitive。スキャン規則が「同一相対パスの行」を単数前提とすることから導出)+検索用索引(folder+status / hash / image_tags.tag_id / view 系 view_id)。再リンクは pending 削除→missing 更新の順で UNIQUE 競合を回避
- 重大度: minor

### CHEAT-025 [C1] ScanSummary 各カウントの定義が未規定
- 手法が与えなかったもの: REQ-015 は {added, missing, pending, updated, skipped} の項目名のみ
- 代替した判断(何をどう埋めたか): added=規則 3b / pending=規則 3a / updated=規則 2 / missing=手順 4 の遷移数 / skipped=規則 1(変更なし)+読み取り不能。手順 5 の行削除はサマリ非計上
- 重大度: minor

### CHEAT-026 [C2] ルートディレクトリ消失時(ドライブ未接続等)の挙動
- 手法が与えなかったもの: 手順 4 を字義どおり適用するとルート消失時に全画像が missing 化される(missing→normal は再リンクのみのため回復困難)
- 代替した判断(何をどう埋めたか): ルート不在時は手順 3〜5 を実行せず IoError で中断(DB 不変)。last_scan は「例外時も更新」の規定どおり更新
- 重大度: friction

### CHEAT-027 [C1] 無効フォルダ・未登録フォルダへのスキャン要求のエラーコード
- 手法が与えなかったもの: REQ-010 は「is_active=false はスキャン対象外」のみで、要求時の応答が未規定
- 代替した判断(何をどう埋めたか): 未登録=NotFound / 無効=ValidationError。いずれも last_scan は更新しない(スキャン不実施のため)
- 重大度: minor

### CHEAT-028 [C1] IProgress&lt;int&gt; の値の意味が未規定
- 手法が与えなかったもの: M-SCAN-005 シグネチャの progress の単位
- 代替した判断(何をどう埋めたか): 処理済みファイルの百分率(0〜100)とした
- 重大度: minor

### CHEAT-029 [C2] 再リンク確定の事前検証範囲と候補ソートの照合方式
- 手法が与えなかったもの: CommitRelink の入力検証の深さ、relative_path「昇順」の照合方式
- 代替した判断(何をどう埋めたか): missing×pending の status 組+同一フォルダ+同ハッシュを強制(遷移表外は ValidationError/NotFound)。候補ソートは OrdinalIgnoreCase(INV-005 のパス比較規則に整合)+同値 id 昇順
- 重大度: minor

### CHEAT-030 [C1] K-WINFS の AttributesToSkip=ReparsePoint と仕様 §4「ファイルのリンクは通常ファイルとして扱う」の競合
- 手法が与えなかったもの: EnumerationOptions.AttributesToSkip はファイルにも効くため、K-WINFS の定型ではファイルのシンボリックリンクも列挙から外れる(また既定の Hidden|System スキップが解除され隠しファイルが対象になる)
- 代替した判断(何をどう埋めたか): K-WINFS の定型を正として採用(仕様 §4 側の記述との整合は設計者へ申し送り)
- 重大度: minor

### CHEAT-031 [C2] K-SQLITE「bool ⇔ INTEGER は Dapper で自動」が位置引数レコードでは不成立
- 手法が与えなかったもの: Dapper のコンストラクタマッピングは型厳密(SQLite INTEGER=Int64)で、bool/int 宣言の DTO では実行時例外になる
- 代替した判断(何をどう埋めたか): 行 DTO は SQLite ネイティブ型(long)で受けて変換層で bool/int 化。また一時 DB の確実なファイル解放のため Dispose 時に SqliteConnection.ClearPool を呼ぶ(接続文字列は K-SQLITE どおり `Data Source=` のみ)
- 重大度: minor

### CHEAT-032 [C2] numeric タグ値の格納時の正規化
- 手法が与えなかったもの: 「数値の不変文字列表現」が入力文字列の保持か再整形(例 '5.0'→'5')かが未規定
- 代替した判断(何をどう埋めたか): InvariantCulture でパース可能なことを検証のうえ入力文字列のまま格納(再整形しない)。比較は常に数値(INV-007)のため観測挙動に影響しない
- 重大度: minor

### CHEAT-033 [C2] 使用数(REQ-029)のステータス非依存
- 手法が与えなかったもの: COUNT(DISTINCT image_id) に missing/pending 画像の付与を含むか
- 代替した判断(何をどう埋めたか): 規定どおり全付与行を対象(INV-010 は表示系=一覧・評価・値抽出の規定と解釈)
- 重大度: minor

### CHEAT-034 [C2] ScanService の二重起動ガードの実装前提と追加バリデーション
- 手法が与えなかったもの: ガードの保持単位(プロセス/インスタンス)、タグ作成時の親存在検証、numeric 設定の min≦max 検証
- 代替した判断(何をどう埋めたか): ScanService はアプリ全体で単一インスタンス(DI シングルトン)前提のインスタンス内 ConcurrentDictionary ガード。親タグ不存在=NotFound、min&gt;max=ValidationError を追加検証
- 重大度: minor

**CHEAT 集計: 21 件(blocker 0 / friction 2 / minor 19)**

### 導出メモ(cheat ではなく BOM からの導出と判断したもの)
- enum⇔DB 文字列トークン(normal/simple/exists/name/asc/equals 等)は仕様 §2.0〜2.4 の表記から導出
- お気に入り/最近・タグ一覧のソートはサービス層 LINQ(OrdinalIgnoreCase 要求は SQL の NOCASE と非 ASCII で挙動差があるため)
- IImageRepository.GetDistinctNormalTagValuesAsync を追加(ITagValueSource 契約「Normal の distinct 値のみ」の供給元)。Core 側 TagValueIndex は評価器入力(ImageWithTags)からの構築と値辞書からの直接構築の 2 系統
- 規則 1 の比較は modified_date の ISO 文字列同値(双方とも共通フォーマッタ経由のため正規形)
- スキャン中の読み取りは FileShare.ReadWrite|Delete(K-WINFS)・ハッシュは遅延計算(OC-5)で Skip 時は計算しない

## 4. blocked 単位

なし。

## 5. 申し送り(設計者向け)

1. **仕様 §2.1 の手順順序の改訂推奨**(CHEAT-014): 手順 4・5 を判定前に置くか、規則 3a の照合対象を「本スキャンで missing 化される行を含む」と明記すると、工場間のばらつきが消える
2. M-GRAPH-003 の BuildGraph 戻り値+警告の両立方法(CHEAT-015。Run 1 CHEAT-004 と同型)と、GraphNode 確定フィールド(CHEAT-018)の M-BOM/E-BOM 反映を推奨
3. K-WINFS の AttributesToSkip とファイルリンク・隠しファイルの扱い(CHEAT-030)の整合確認を推奨
4. K-SQLITE の「bool ⇔ INTEGER 自動」記述の補正(CHEAT-031)を推奨
5. REQ-004 の「初版 DDL」と migrations 一覧の関係(CHEAT-022)の明文化を推奨
6. ルート消失時の保護動作(CHEAT-026)は仕様化の価値が高い(ドライブ未接続での一括 missing 化はユーザーデータ事故に直結)
7. 次 Run(表面)への足場: ITagValueSource の DB 供給元(GetDistinctNormalTagValuesAsync)実装済み。Serilog 系・SkiaSharp は未調達のまま(該当製造単位で追加)
