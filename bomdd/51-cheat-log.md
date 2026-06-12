# ずる台帳 — loop-v1-core(裁定記録)

工場 factory-01 の Run 1〜3 で報告されたずる(計 52 件: blocker 0 / friction 6 / minor 46)の裁定。
個票は bomdd/reports/factory-run-0{1,2,3}.md を一次資料とし、本台帳は裁定と帰属の確定を記録する。

## 裁定方針
- **accept**: 工場の判断を仕様化して採用(BOM へ転記)
- **correct**: 設計者が正解を確定し BOM を改訂(v1.2)→ Run 4 で部分再製造
- **note**: 採用するが次ループ/テンプレ改善の申し送り

## friction 6 件の個別裁定

| # | 出所 | 内容 | 裁定 |
|---|---|---|---|
| F-1 | Run1 | cheat 分類 C1〜C6 の定義表が製造パッケージに不在(暫定自定義) | **note** — Run 2 以降の Work Order 指示に定義を追記済み。テンプレ改善として BomDD 側へ申し送り |
| F-2 | Run2 | 仕様 §2.1 の手順順序(missing 検出・pending 清掃)と FMEA-004(リネーム追跡)の整合 — 手順 4・5 を判定前に適用して解消 | **accept** — 工場の解釈が正(リネーム追跡には missing 確定が先行する必要)。仕様 §2.1 の手順は「実行順は 4→5→3 の判定でも可(結果同値)」と次版で明文化 |
| F-3 | Run2 | ルート消失時の一括 missing 化保護(フォルダ自体が読めない場合は IoError 中断) | **accept** — 妥当な防御(USB 取り外しで全画像 missing 化する事故の防止)。仕様 §2.1 へ次版で転記 |
| F-4 | Run3 | 「全画像」固定入口を実装(E-BOM 保留事項) | **accept** — 仕様 §2.6 v1.2 で正式化(条件なし・全 normal 画像) |
| F-5 | Run3 | ビューア起動経路が未規定 → ダブルクリックに暫定割当 | **correct→accept** — 原典調査(通常モードのクリック=ビューア)を踏まえ、本実装は「ダブルクリック=ビューア、クリック=選択(常設詳細パネル)」で確定(意図的差分)。REQ-041/仕様 §2.6 v1.2 に反映 |
| F-6 | Run3 | 表示列/ホームタグ編集 UI 未提供(BOM に画面定義なし) | **correct** — タグタブ階層エディタ(ホームタグ設定・条件・別名)として仕様 §2.6 v1.2 に正式定義。Run 4 で製造 |

## 構造的な裁定(最重要)

| 項目 | 帰属 | 裁定 |
|---|---|---|
| **タグ付与 UI が E-BOM に不在**(Run3 申し送り筆頭) | 設計側欠陥(C3 工程欠落) | **correct** — REQ-046 を新設、E-UI-TAGASSIGN-029 / M-UI-016 / CP-TAGUI-013 / CP-UI-G7 を追加。原典の TaggingPanel 方式(タグ編集モード→右パネル→適用、numeric 固定値/連番)を採用。Run 4 で製造 |
| **シェル構成**(メニュー方式 vs 原典タブ方式) | 設計側の沈黙(C2 暗黙知)— 承認者提供のスクリーンショット+原典調査で正解確定 | **correct** — タブ式(タグ/画像+設定)+タグタブ 3 ペインへ改訂(仕様 §2.6 v1.2)。NodeGraph ナビは左ツリー(原典のフォルダ風+パンくずに対する意図的差分、golden で判定) |
| 階層エディタの保存単位 | 同上 | **correct** — バッチ保存(保存/キャンセル+確認)で確定。modified_at は保存時 1 回 |

## minor 46 件の一括裁定
- **accept(45 件)**: 未規定の補完(JSON 命名・TTL 起点・WindowX/Y 既定・GraphNode フィールド形・migrations V1 構成・MTP 接続設定・i18n 新規 48 キー・通知=ステータスバー等)はすべて工場判断を採用。次版 BOM へ転記対象としてマーク(reports 参照)
- **note(1 件)**: M-EVAL-002 の戻り値契約(集合のみ vs 警告併返)の矛盾 → EvaluationResult 複合戻り値を採用。M-BOM 次版で interface_contract を実装に合わせて改訂

## Run 4(収束再製造)の裁定 — 2026-06-11 追記

報告 14 件(blocker 0 / friction 2 / minor 12)。個票は bomdd/reports/factory-run-04.md。

| # | 内容 | 裁定 |
|---|---|---|
| CHEAT-053 (friction) | views.description がスキーマ契約(M-DB-007)に欠落 → migration 001-views-description で追加 | **accept** — 仕様 §2.0/REQ-030 には存在しており M-BOM への転記漏れ(設計側帰属 C1)。初の実マイグレーションとして REQ-004 機構の実証にもなった。教訓: CP-DB-006 のスキーマ同値検査は「自分自身との同値」のみで仕様列挙との照合が無い → 次ループで照合ベクタの追加を検討 |
| CHEAT-056 (friction) | view_conditions の直接編集 UI が仕様 v1.2 から消えたため撤去 | **accept** — V1 では階層ノード条件(condition_type)が同じ用途を担い、原典のタグタブにも直接編集は無い。view_conditions テーブル・評価器(REQ-031)は NodeGraph パス評価で使用継続。直接編集 UI は後続ループ候補として申し送り |
| minor 12 件 | レイアウト微調整・新規 i18n キー・UI Automation 代替スモーク等 | **accept** 一括(reports 参照) |

## 検査器(治具)の欠陥記録 — 2026-06-11

- **JIG-001**: オラクル S-05 の v0 フィクスチャが現行 `DatabaseSchema.LatestDdl` から導出されており、
  Run 4 でマイグレーションが初めて実在した際に「v0 なのに最新列を持つ」矛盾フィクスチャとなり偽陽性 FAIL。
  → 凍結スナップショット(tests/ViewPrism2.Oracle/V0SchemaFixture.cs)に固定して修理。
  **オラクルのケース定義・期待値(41)は不変**。製品の退行はゼロ(修理後 S-01〜S-12 全 PASS)。
  BomDD method-v1 の「検査器側の暗黙知(jig-side implicit knowledge)」の実例として記録。

## Run 5(ECO-002 golden 駆動の収束)の裁定 — 2026-06-12 追記

報告 5 件(工場自己申告: blocker 1 / friction 1 / minor 3。裁定後: blocker 0 / friction 2 / minor 3)。
個票は Run5 納品書(工場最終報告)を一次資料とする。commit f7d6cf7。

| # | 工場申告 | 内容 | 裁定 |
|---|---|---|---|
| CHEAT-R01 | blocker | DF-2 の根本原因が変更禁止領域(Infrastructure/Database の TagRepository.GetUsageCountsAsync — Dapper×空 image_tags の materialization 失敗)にあり、影響集合外を最小修正(公開契約保持・dynamic 化) | **friction へ再分類(設計側帰属)** — 真因は設計者の影響分析の **under-inclusion**(E-DB-010 を「変更しない」と予測したが DF-2 の根がそこにあった)。工場の逸脱は申告済み・契約保持・受入達成に不可欠で、受入を欺くものではない。ECO-002 §5 に「影響なし予測の外れ 1 件」として計上。凍結オラクル 20/20 PASS で挙動互換は実証済み |
| CHEAT-R02 | friction | DoubleClickDetector の閾値に user32!GetDoubleClickTime(P/Invoke、失敗時 500ms) | **accept** — Windows 専用(チャーター)で妥当。K-AVALONIA へ「ClickCount は環境により 2 に達しないことがある(DF-4)」を次版で追記 |
| CHEAT-R03 | minor | 通知文言キー error.unhandled を新設(既存 error 名前空間へ) | **accept** |
| CHEAT-R04 | minor | 初回起動の既定コレクション選択は「未選択(プロンプト)」とした(REQ-053 の沈黙次元) | **accept** — コレクションファースト裁定と整合。仕様 §2.6 へ次版で明文化 |
| CHEAT-R05 | minor | L1 スモーク隔離用に環境変数 VIEWPRISM2_DATA_DIR によるデータディレクトリ上書きを追加(未設定時は従来どおり) | **accept** — 製品挙動不変・検査容易性の向上。仕様 §2.7 へ次版で明文化 |

**特記**: DF-2 の一次切り分け(ECO-002 §1「サービス層は無実」)は不正確だった。既存テストが緑だったのは
「image_tags に行がある状態」のみを踏んでいたためで、空テーブル(初回タグ作成直後)の経路が未被覆だった。
検査被覆の教訓として記録(空状態ベクタは CpTag011 に追加済み — Run5)。

## 凍結オラクルとの関係
固定オラクル S-01〜S-12 は Run 3 後・Run 4 後(治具修理後)・**Run 5 後**とも **全 PASS**
(tag loop-v1-r1 凍結のまま不変。Run 5 後はテスト 20/20)。Run 4 後の唯一の FAIL は上記 JIG-001(治具側)で、製品起因の乖離はゼロ。
ずるはすべて表面(UI 構成・未規定次元)と契約転記漏れに集中し、核(ドメインロジック)の挙動乖離はゼロ —
BomDD の「ずるは表面側に現れる」観測と整合。Run 5 の CHEAT-R01(リポジトリ materialization)も
仕様挙動の乖離ではなく実装基盤の欠陥+検査被覆の穴であり、凍結オラクルは退行ゼロを確認した。
