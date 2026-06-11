# Factory Run 1 報告 — ViewPrism2 (loop-v1-core)

- 製造装置: factory-01
- 実施日: 2026-06-11
- 範囲: M-SLN-000 / M-CORE-001 / M-EVAL-002 / M-SORT-004 / M-CACHE-009 / M-SET-010 / M-I18N-011(コア)/ M-HARNESS-015(Run 1 該当 CP)
- 隔離規律: 遵守(41-fixed-oracle.yaml・42-exploratory-probes.yaml・原典 view-prism・BomDD リポジトリは未参照。bomdd/・docs/ 既存ファイルは未変更)

## 1. 製造単位 → 成果物パス対応表

| 製造単位 | 成果物 |
|---|---|
| M-SLN-000 | `ViewPrism2.sln` / `Directory.Build.props`(net10.0・nullable enable・TreatWarningsAsErrors=true・ImplicitUsings)/ `.editorconfig` / 4 プロジェクト(`src/ViewPrism2.Core`, `src/ViewPrism2.Infrastructure`, `src/ViewPrism2.App`, `tests/ViewPrism2.Tests`)。依存方向 App → Infrastructure → Core を csproj 参照で固定 |
| M-SLN-000(App スタブ) | `src/ViewPrism2.App/Program.cs`・`App.axaml(+.cs)`・`MainWindow.axaml(+.cs)`・`app.manifest`。Avalonia 12.0.4 + Fluent(Light)+ Inter。1200×800・最小 900×600(K-DESIGN)。UI 本体・DI 合成・多重起動防止は後続 Run(M-UI-013) |
| M-CORE-001 | `src/ViewPrism2.Core/Models/Enums.cs`(ImageStatus/TagType/ConditionOperator/SortField/SortDirection/HierarchyConditionType/NodeKind)・`Models/Entities.cs`(SyncFolder/ImageRecord/Tag/TextualTagSettings/NumericTagSettings/ImageTag/View/ViewCondition/HierarchyNode/GraphNode/ScanSummary/AppSettings)・`Common/IdGenerator.cs`・`Common/IClock.cs`(+SystemClock)・`Common/IsoTimestamp.cs`(共通フォーマッタ、K-WINFS 申し送り分)・`Common/FileHasher.cs`・`Common/PathNormalizer.cs`・`Common/ByteSizeFormatter.cs`・`Common/ErrorCode.cs`(8 種全列挙)・`Common/Result.cs`(Result / Result&lt;T&gt;) |
| M-EVAL-002 | `src/ViewPrism2.Core/Services/ConditionEvaluator.cs`(OC-1。演算子 5 種・AND 結合・§2.3 エッジケース規則・EvalWarning・regex 1 秒タイムアウト・例外を漏らさない) |
| M-SORT-004 | `src/ViewPrism2.Core/Services/ImageSorter.cs`(OC-4。name=OrdinalIgnoreCase / 日時=序数 / size=数値、二次キー id 昇順固定) |
| M-CACHE-009 | `src/ViewPrism2.Core/Services/ImageMemoryCache.cs`(OC-6。容量 50・TTL 3 分・LRU・IClock 注入・LoadCount) |
| M-SET-010 | `src/ViewPrism2.Infrastructure/Settings/SettingsStore.cs`(既定 %APPDATA%/ViewPrism2/settings.json。破損→ .bak 退避+既定値で再生成、例外なし) |
| M-I18N-011(コア部分) | `src/ViewPrism2.Core/Services/LocalizationService.cs`(OC-8。辞書注入・要求ロケール→ja→キーのフォールバック・{name} 補間・CultureChanged)。資産 `Assets/i18n/{ja,en}.json` は後続 Run で統合 |
| M-HARNESS-015(Run 1 範囲) | `tests/ViewPrism2.Tests/`(xunit.v3 3.2.2、MTP 実行。`FakeClock.cs`・`CpUtil005Tests.cs`・`CpEval001Tests.cs`・`CpSort003Tests.cs`・`CpCache008Tests.cs`・`CpSet009Tests.cs`・`CpI18n010Tests.cs`・`CpNfr001Tests.cs`。全テストに `[Trait("cp", "CP-xxx")]`) |

Run 1 対象外(未着手・計画どおり): M-GRAPH-003 / M-SCAN-005 / M-DB-007 / M-THUMB-008 / M-VIEWSVC-012 / M-UI-013 / M-UI-014、および CP-GRAPH-002 / CP-SCAN-004 / CP-DB-006 / CP-THUMB-007 / CP-TAG-011 / CP-VIEW-012 / CP-L1-SMOKE / CP-UI-G1〜G5。

## 2. 受入実行ログ要約

- `dotnet build ViewPrism2.sln -c Release` → **成功(警告 0・エラー 0)**(TreatWarningsAsErrors=true)
- `dotnet test tests/ViewPrism2.Tests -c Release` → **全 63 件成功(不合格 0・スキップ 0)**、実行時間 1.5s
- App スタブ起動確認(参考、CP-L1-SMOKE は後続 Run): Release ビルドの exe を起動し、ウィンドウタイトル `ViewPrism2` でプロセス常駐を確認後に終了

| CP | depth | テスト数 | 結果 | 備考(test_vectors 被覆) |
|---|---|---|---|---|
| CP-UTIL-005 | unit | 13 | PASS | UUIDv4 1000 件・IClock 形式・SHA-256 既知 2 ベクタ・パス正規化/比較・サイズ整形 6 ベクタ |
| CP-EVAL-001 | unit | 27 | PASS | exists/equals(textual・numeric)/between(境界・'9'vs'10' FMEA-001)/regexp(マッチ・不正 '('・タイムアウト)/in(一致・不一致・不正 JSON)・タグ未付与×5 演算子・simple+値演算子・value=NULL 無視+警告・AND 結合・Normal 限定防御(INV-010)・条件なし |
| CP-SORT-003 | unit | 7 | PASS | name asc(case 無視)・size desc・created asc・modified desc・同値 id 昇順(asc/desc 両方向)・安定性・空/1 件 |
| CP-CACHE-008 | unit | 4 | PASS | 同一キー 2 回=Load 1・51 個投入で最古破棄(FMEA-007)・アクセスによる LRU 更新・TTL 3 分(FakeClock) |
| CP-SET-009 | unit | 4 | PASS | 全項目ラウンドトリップ・破損 '{{{' → 既定値+.bak+再生成(FMEA-009)・欠落→既定値・既定値スキーマ |
| CP-I18N-010 | unit | 7 | PASS | en あり/en 欠落→ja/両欠落→キー・補間・引数欠落で {name} 残置・CultureChanged 1 回発火 |
| CP-NFR-001 | L3 | 1 | PASS | 1,000 画像×各 5 タグ×3 条件、ウォームアップ 1 回+3 回計測の中央値 ≦ 200ms(Release。実測はフィルタ実行全体で 0.09s と余裕大) |
| 合計 | — | **63** | **全件 PASS** | |

## 3. ずる報告(cheat-log 全件)

> 注: 分類記号は CHEAT-001 のとおり暫定自定義。
> C1=仕様/契約の欠落を補完, C2=契約の曖昧さ・矛盾の解消, C3=調達逸脱, C4=治具・受入手段の判断, C5=表面の独自判断, C6=手戻り(Work Order 既定)

### CHEAT-001 [C1] cheat 分類 C1〜C6 の定義が製造パッケージ内に無い
- 手法が与えなかったもの: 34-routing.yaml / 40-work-order.md は「分類 C1〜C6」を参照するが、その定義表が供与文書に存在しない(C6=手戻り のみ Work Order から判明)
- 代替した判断(何をどう埋めたか): 上記の暫定分類を自定義して全件に付与した
- 重大度: friction

### CHEAT-002 [C3] Avalonia.Diagnostics 12.0.4 が NuGet に存在しない
- 手法が与えなかったもの: procurement 固定バージョン 12.0.4 の入手可能性(実在は 11.3.17 まで。12 系で未発行)
- 代替した判断(何をどう埋めたか): substitutable: true・Debug 構成限定の診断用途であり Run 1 の App はスタブのため、参照自体を見送った(代替バージョンへの差し替えはせず、調達表の改訂を設計者へ申し送り)
- 重大度: minor

### CHEAT-003 [C4] dotnet test → Microsoft.Testing.Platform の接続方法
- 手法が与えなかったもの: ADR-0007 は「xunit.v3 + MTP」を決定しているが、`dotnet test` から MTP 実行へ接続する具体設定が未規定
- 代替した判断(何をどう埋めたか): xunit.v3 標準の最小構成(csproj に `OutputType=Exe` + `TestingPlatformDotnetTestSupport=true`)を採用。調達外パッケージ(Microsoft.NET.Test.Sdk / xunit.runner.visualstudio 等)は追加していない
- 重大度: minor

### CHEAT-004 [C2] M-EVAL-002 の戻り値契約が単一シグネチャで両立しない
- 手法が与えなかったもの: interface_contract.signature は `IReadOnlySet<string> Evaluate(...)` だが、warnings 項は「IReadOnlyList&lt;EvalWarning&gt; として併せて返す」を要求しており、戻り値 1 つでは両立しない
- 代替した判断(何をどう埋めたか): `EvaluationResult { IReadOnlySet<string> MatchedImageIds; IReadOnlyList<EvalWarning> Warnings }` の複合戻り値に統合(out 引数は不採用)
- 重大度: minor

### CHEAT-005 [C1] OC-1 入力「タグ付け状態」の具体形が未規定
- 手法が与えなかったもの: ImageWithTags の構造定義。equals の textual/numeric 分岐(REQ-031)にはタグ種別の知識が必須だがシグネチャにタグ定義の引数が無い
- 代替した判断(何をどう埋めたか): 付与 1 件を `EvalTagValue(TagId, TagType, Value)` とし、タグ種別を入力に内包させた(`ImageWithTags(ImageId, Status, Tags)`)
- 重大度: minor

### CHEAT-006 [C1] between の value2=NULL・境界値が数値変換不能の場合の扱い
- 手法が与えなかったもの: §2.3 エッジケース規則は「value が NULL → 条件無視+警告」のみ規定。value2=NULL、境界値('1','5' 側)が数値変換不能のケースは未規定
- 代替した判断(何をどう埋めたか): value2=NULL は必須入力欠落として value=NULL 規則を準用(条件無視+警告)。境界値が数値変換不能の場合は「存在するが不正な入力」として不正 regex 規則を準用(条件不成立+警告)
- 重大度: minor

### CHEAT-007 [C1] in 演算子の JSON 配列が不正な場合の扱い
- 手法が与えなかったもの: value の JSON がパース不能・配列でない場合の規則
- 代替した判断(何をどう埋めたか): 不正 regex 規則を準用し「条件不成立+警告(EvalWarningKind.InvalidValueList)」。例外は漏らさない
- 重大度: minor

### CHEAT-008 [C1] REQ-045 の TTL 起点と境界の解釈
- 手法が与えなかったもの: 「有効期限 3 分」の起点(ロード時刻か最終アクセスか=absolute/sliding)と、ちょうど 3 分経過時の扱い
- 代替した判断(何をどう埋めたか): ロード時刻起点(absolute)・経過 ≧ 3 分で失効とした。受入は境界を避け 2:59(ヒット)/3:01(再ロード)で検査
- 重大度: minor

### CHEAT-009 [C1] AppSettings の WindowX/WindowY の既定値が未規定
- 手法が与えなかったもの: M-SET-010 schema は Width=1200/Height=800 等を規定するが X/Y の既定値が無い
- 代替した判断(何をどう埋めたか): `int?` の null(初回起動はウィンドウ位置を OS 既定に委ねる)とした
- 重大度: minor

### CHEAT-010 [C1] settings.json のプロパティ命名規約が未規定
- 手法が与えなかったもの: JSON のキー表記(camelCase / PascalCase)
- 代替した判断(何をどう埋めたか): JSON 慣行の camelCase で書き出し、読み取りは case-insensitive とした(将来の表記揺れにも耐性)
- 重大度: minor

### CHEAT-011 [C2] CultureChanged の発火条件(同一ロケール再設定時)
- 手法が与えなかったもの: SetLocale で現在と同じロケールを指定した場合に発火するか
- 代替した判断(何をどう埋めたか): 実変更時のみ発火(全 UI 再バインドの無駄打ち回避)。CP-I18N-010 の「発火 1 回」ベクタはどちらの解釈でも成立
- 重大度: minor

### CHEAT-012 [C4] CP-CACHE-008 oracle「保持キー集合」の観測手段
- 手法が与えなかったもの: M-CACHE-009 の interface_contract.metrics は LoadCount のみで、oracle が要求する「保持キー集合」を観測する API が無い
- 代替した判断(何をどう埋めたか): 受入用に読み取り専用の `Keys` プロパティ(スナップショット)を追加
- 重大度: minor

### CHEAT-013 [C1] GraphNode / NodeKind の具体フィールドが未規定
- 手法が与えなかったもの: M-CORE-001 は「NodeGraph 型(GraphNode, NodeKind)」の存在のみ規定。フィールド定義が無い
- 代替した判断(何をどう埋めたか): OC-2 の出力記述(型・表示名・親子・順序・各ノードの条件)から `NodeKind {Root, TagName, Value, Combined}`・`GraphNode {Kind, DisplayName, HierarchyNodeId, TagId, Value, Children}` を暫定構成。M-GRAPH-003(Run 2)実装時に過不足を確定する
- 重大度: minor

**CHEAT 集計: 13 件(blocker 0 / friction 1 / minor 12)**

### 導出メモ(cheat ではなく BOM からの導出と判断したもの)
- 条件の tag_id=NULL(タグ削除で SET NULL)→ 条件無視+警告: INV-008「無視またはフォールバック」から導出
- ByteSizeFormatter の小数点は `.` 固定(InvariantCulture): CP-UTIL-005 の exact oracle('1.0 KB')から導出
- ImageMemoryCache 内の経過時間計算は `IClock.UtcNowIso()` の文字列を共通フォーマッタで `DateTime` に逆変換して実施(IClock 契約は変更していない)
- Infrastructure の Run 1 パッケージ参照は 0 件(SettingsStore は BCL の System.Text.Json で充足)。SQLite/Dapper/SkiaSharp/Serilog は該当製造単位の Run で追加する

## 4. blocked 単位

なし。

## 5. 申し送り(設計者向け)

1. procurement の Avalonia.Diagnostics 12.0.4 は NuGet 未発行(CHEAT-002)。12 系での提供形態を確認のうえ調達表の改訂を推奨
2. M-EVAL-002 の signature と warnings の両立方法(CHEAT-004)を M-BOM 改訂で確定すると、後続工場のばらつきが消える
3. cheat 分類 C1〜C6 の定義表(CHEAT-001)を製造パッケージへ同梱推奨
