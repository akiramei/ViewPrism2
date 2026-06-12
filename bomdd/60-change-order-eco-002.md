# Change Order — ECO-002(Phase 7 変更オーダー / golden 駆動の収束)

> 出所: golden 承認 CP-UI-G1〜G7(承認者 maintainer)の実機検証で表面欠陥+要求明確化が判明。固定オラクル(核)は全 PASS のまま、golden(表面)が捕捉した不適合を畳む。
> 規律: 要求変更は G1 根拠精度で REQ 化 / 影響なし予測を先行凍結 / **既存固定オラクル S-01〜S-12 は不変**(回帰のヤードスティック)/ 部分再製造は fresh factory(隔離維持)。
> ワークシート: 影響分析=本書 §2 / データ移行=なし(N/A) / 不要改変監査=Run5 で 63 相当を実施。

## 0. 変更前 baseline の凍結
- As-Maintained 個体: factory-01 / commit `789ddae`(BOM v1.2 + ECO-001 doc-rev)
- データ fixture: **N/A**(スキーマ変更なし・データ移行なし。後述 CR で DDL 不変)
- 既存固定オラクル: S-01〜S-12(`tag:loop-v1-r1` 凍結)。**本 ECO で不変**。回帰の基準

## 1. 変更要求(golden 指摘 → 要求変更 CR / 欠陥 DF)

> **出所分析**: 各指摘がどの工程で混入したかの帰属(証拠付き)は [reports/eco-002-origin-analysis.md](reports/eco-002-origin-analysis.md)。
> 要点 — 要求系(G1)は Phase 2 リバース仕様化の品質(新旧 UI 取り違え・抽出漏れ・曖昧表現の解釈拡大)、製造系(G4-G6)は知識パック欠落・仕様の沈黙・検査深さに帰着。「読まずに推論」は検出されず。是正 P0-1〜P1-6 を本 ECO に組み込む。

### 要求変更(設計判断確定済み 2026-06-12)
| ID | 内容 | 種別 | REQ 反映 | 出所 |
|---|---|---|---|---|
| CR-1 | グリッド列数 3/4/5/6 セレクタを撤去し、ビューポート幅からのレスポンシブ自動列に変更 | REQ 改訂 | REQ-041 改訂・REQ-052(settings.grid_columns 廃止) | G1①(原典はレスポンシブ自動列。列数概念なし) |
| CR-2 | コレクション選択で一覧/ビューを当該コレクションに絞り込む(コレクションファースト。横断「全画像」なし) | REQ 追加 | **REQ-053 新規** | G1④(原典 ImageGallery は selectedCollection で絞り込み。現 V1 は全合算) |
| CR-3 | グリッド/リストに SHIFT+クリックの連続範囲選択を追加 | REQ 改訂 | REQ-041 追記 | G1②(原典の一覧操作。現 V1 は click+Ctrl のみ) |
| CR-4 | ソート選択肢から作成日(created_date)を除外 | REQ 改訂 | REQ-038 改訂 | G1③(リスト基本列に作成日が無く非対称。用途的に不要) |
| CR-5 | 選択中コレクションを永続化し次回起動時に復元(CR-2 へ統合) | REQ 改訂 | REQ-052/053 | P0-1 照合(02§1。原典挙動) |
| CR-6 | 表示モード(グリッド/リスト)を設定として永続化 | REQ 改訂 | REQ-052 改訂 | P0-1 照合(02§2) |
| CR-7 | ビューアを背景クリックでも閉じられるようにする | REQ 改訂 | REQ-044 改訂 | P0-1 照合(02§2「ESC または背景クリック」) |
| CR-8 | コレクション項目に画像数+スキャン中表示を追加(CR-2 へ統合。パスはツールチップ可) | REQ 改訂 | REQ-053 | P0-1 照合(03§1 サイドバー) |

- **CR-3 の実装定義**(porting-spec 03 L117 の「採用時は受入条件を別途定義」に従い確定): SHIFT+クリック=最後の選択アイテムからクリック位置までの範囲を**既存選択へ union**(置換しない。原典 useImageSelection.ts:24-34 方式)。選択順バッジは index 順で末尾へ追番。unit ベクタ化する
- **仕様文書補正**: ビュー編集ダイアログは実装が IsFavorite 編集可(仕様 §2.6 は「名前+説明」)→ v1.3 で §2.6 に favorite を追記(実装・原典能力に整合)
- **明示 DEFER**(P0-1 の走査結果。詳細: [reports/eco-002-surface-crosscheck.md](reports/eco-002-surface-crosscheck.md)): 動的ソート(表示列由来。01 Should)・view_revisions・サイドバー折りたたみ・pending 承認フロー(V2 裁定注意書き付き)・修復/類似/マージ/作業/バックアップ(チャーター済み)。**G1④型の追加脱落は CR-5〜8 で打ち止めを確認**(RVP 全行+02/03/06 走査済み)

### 欠陥(golden ゲート不合格 = 劣化部品の交換)
| ID | 症状 | 一次切り分け | 受入(修正後に満たすべきこと) |
|---|---|---|---|
| DF-1 | アプリに**グローバル例外ハンドラが無く**、UI スレッド未処理例外で黙ってプロセス終了。ログ未出力(`%APPDATA%/ViewPrism2/logs/` 不在) | Program.cs/App に未捕捉例外フックなし。横断的堅牢性欠陥 | UI 例外でプロセスが即死しない。未処理例外は全文スタックを app ログへ記録し、ユーザーへ非モーダル通知。NFR: 「UI 例外で落ちない」 |
| DF-2 | G6/G7: textual タグ(性別/男/女)作成→保存で**クラッシュ**。G7 はこれにブロックされ作業不能 | **サービス層は無実**(CpTag011「predefined_values…」が同一フローを実 SQLite で PASS)。UI スレッド未処理例外。`TagService.SetTextualSettingsAsync` のみ DB 例外を try/catch していない点も是正対象 | textual タグ(候補値あり)を作成・保存してもクラッシュしない。失敗時は Result→非モーダルエラー。DF-1 で取得する正確なスタックで最終確定 |
| DF-3 | G5: 言語切替で ComboBox 値等は切替わるが**ラベル/ボタン名が不変**(タグ名不変は正しい挙動) | `LocalizationProxy` の `"Item[]"` インデクサ通知がコンパイル済み `{Binding Loc[key]}` を再評価しない。名前付きプロパティ(SortFieldOption.Label 等)は更新される | ja/en 切替で主要画面のラベル・ボタン・見出しが即時切替。生キー露出なし(G-5 再充足) |
| DF-4 | G4: グリッドのダブルクリックで**ビューアが起動しない** | 配線は存在(OnCellPressed→ClickCount≥2→OpenItemRequested→ShowViewer)。ListBox 仮想化セルでの二度目 PointerPressed 取得漏れ、または ViewerWindow 生成時例外(DF-1 で黙殺)のいずれか。DF-1 投入後に確定 | 通常モードでグリッド項目をダブルクリック→ビューア起動・端停止・「位置/総数」表示(G-4 再充足) |

## 2. 影響分析(トレース逆引き+影響なし予測)

| 段 | CR-1 列数 | CR-2 コレクション | CR-3 SHIFT | CR-4 作成日 | DF-1〜4 欠陥 |
|---|---|---|---|---|---|
| 仕様節 | §2.6(列数/G-1) | §2.6(左ペイン/空状態)・新節 | §2.6(選択規則) | §2.3 REQ-038 | §2.6 G-4/G-5・§4 NFR(堅牢性) |
| E-BOM | E-UI-GRID-022 改訂 | E-UI-SHELL-021 改訂(+スコープ) | E-UI-GRID-022 改訂 | E-SORT-004(列挙のみ・コア不変) | E-UI-SHELL-021/GRID-022/VIEWER-024/I18N-014 |
| M-BOM unit | M-UI-013 | M-UI-013 + M-UI スコープ | M-UI-013 | M-SORT-004(UI 選択肢のみ) | M-SLN-000(合成ルート)・M-UI-013/014 |
| Control Plan | CP-UI-G1 改訂 | **CP-UI-G2'/新 G 追加** | CP-UI-G1 追記 | CP-SORT-003(created_date ソート行は撤去/温存判断) | CP-UI-G4/G5・CP-L1-SMOKE |
| 固定オラクル行 | 不変 | 不変 | 不変(新規は unit 側) | **S-08 は created_date を使わず不変** | 不変 |
| 新規 unit/ベクタ | レスポンシブ列算出 | コレクションフィルタ | 範囲選択計算(start..end) | (削除のみ) | i18n 再評価・例外非伝播 |

**影響なし予測(反証可能・製造前に凍結)**:
- 核(ドメイン)は無傷。**E-SCAN-005 / E-EVAL-002 / E-TAGSVC-008 / E-DB-010 / E-GRAPH-003 とスキーマ DDL・マイグレーションは変更しない**。
- **固定オラクル S-01〜S-12 は全行 PASS のまま**(回帰)。特に S-08(整列安定性)は name/file_size のみ使用のため CR-4 の影響を受けない。
- 反証条件 = 上記核部品のテスト/オラクルが赤化、または DDL 差分が発生すること。発生すれば予測外れ(under-inclusion)として記録。
- CR-4 注意: `SortField.CreatedDate` enum とソータ実装は**温存**し、UI のソート選択肢からのみ除外する(契約最小変更)。DB に created_date 列は残す。

## 3. BOM 改訂

> **前提条件 P0-1: 実施済み(2026-06-12)** — porting-spec RVP-\* 全行+02/03/06 を行単位で全数照合した([reports/eco-002-surface-crosscheck.md](reports/eco-002-surface-crosscheck.md))。結果: 新規 CR 4 件(CR-5〜8、§1 に追加済み)・仕様文書補正 1 件・明示 defer 群を確定。追加の脱落なし。
> あわせて P0-2(K-AVALONIA へ CompiledBindings インデクサ通知の罠を追記)・P0-3(横断 NFR: UI 未処理例外封じ込め)を BOM 改訂に含める。
> CR-5〜8 の影響先: E-SETTINGS-013(REQ-052: display_mode・last_collection_id 追加)・E-UI-SHELL-021(コレクション項目表示)・E-UI-VIEWER-024(背景クリック)。いずれも表面のみ、核・スキーマ・固定オラクルへの影響なし(影響なし予測は §2 のまま有効)。
- bom_rev: **v1.2 → v1.3**(tag: `loop-v1-r3` を Run5 後に打つ。凍結オラクルは `loop-v1-r1` のまま不変)
- **改訂実施済み(2026-06-12)**: `20-spec.md`(§2.3 ソート軸 / §2.6 コレクションファースト・レスポンシブ列・SHIFT union・ビューア背景クリック・ビュー編集 favorite・堅牢性規則・golden G-1〜G-7 改訂(操作経路付き)/ §2.7 設定 / §5 トレース表 REQ-041 是正+REQ-053 追加)、`10-requirements.yaml`(REQ-038/041/044/052 改訂・REQ-053 新規)、`30-ebom.yaml`(E-UI-SHELL-021・E-UI-GRID-022 改訂・NFR-002 追加)、`31-kbom.yaml`(K-AVALONIA: CompiledBindings インデクサ通知の罠 — P0-2)、`33-control-plan.yaml`(CP-UI-G1/G4 改訂・CP-L1-SMOKE 経路深化(P1-6)・CP-ROBUST-001 新規(P0-3))
- **変更分の受入を先に追加(オラクル・ファースト)**:
  - 新 unit ベクタ: 範囲選択(SHIFT: start..end の選択順)・レスポンシブ列算出・コレクションフィルタ(選択コレクションの normal のみ)
  - golden 基準改訂: G-1(列数 UI 削除・レスポンシブ整列)、G-2/新 G(コレクション選択で一覧が切替)、G-4/G-5(再判定)
  - 固定オラクル(41): **新規行を追加しない方針**(本 ECO の新規挙動はいずれも UI golden + unit で受入可能。核の隠しシナリオ追加は不要)。既存 S-01〜12 不変
  - 堅牢性受入: 「UI で例外を発生させてもプロセスが生存しログに記録」を L1/L2 で検査

## 4. 部分再製造(Run5)
- 工場: **fresh factory(隔離)**。供与=改訂 BOM + 本 ECO + 変更前ソース複製(diff 基準点、63)。**非開示**=設計対話・固定オラクル(41)・探索プローブ(42)・原典/porting-spec
- 再製造/改修対象: 表面(M-UI-013/014)+合成ルート(M-SLN-000 のグローバル例外フック)。**核は再利用**(再製造しない)
- 欠陥は症状+受入で渡す(根本原因の断定は工場の diagnose に委ねるが、DF-1 グローバルハンドラは明示要件として渡す)
- 自己受入: 既存 190 テスト(回帰)+ 新規 unit ベクタ。赤=stop/report
- 設計者側検査: 固定オラクル 20/20(不変)+ golden CP-UI 再判定(承認者 maintainer)

## 5. 回帰+変更受入(失敗5分類で帰属)
- 既存 S-01〜12 失敗 = **regression** / 新規行失敗 = **change miss** / 影響分析外 diff = **unnecessary modification** / 自己受入赤での納品 = **nonconformance**
- **結果(2026-06-12・Run5=commit f7d6cf7)**:
  - **regression: 0** — 凍結固定オラクル S-01〜S-12 全 PASS(20/20、tag loop-v1-r1 不変)。既存 unit 190 全緑維持
  - **change miss: 0** — 新規 unit 44 全緑(レスポンシブ列・SHIFT union・コレクションフィルタ・settings ラウンドトリップ・CP-ROBUST-001)。合計 234/234・ビルド警告 0
  - **data-preservation miss: N/A**(スキーマ・移行なし)
  - **unnecessary modification: 0** — diff は申告済み影響集合+申告済み逸脱 4 ファイルのみ(設計者 diff 監査済み)
  - **nonconformance: 0** — 自己受入緑で納品
  - **影響なし予測の採点: under-inclusion 1 件** — DF-2 の根本原因が「変更しない」と予測した E-DB-010(TagRepository.GetUsageCountsAsync の Dapper materialization)にあった。工場は契約保持の最小修正で対応(CHEAT-R01、51-cheat-log で friction/設計側帰属に裁定)。教訓: 欠陥修正型 ECO では「症状の発火箇所(UI)」と「根本原因の所在」が別レイヤになり得るため、影響なし予測は欠陥系に対して弱い — 次回から DF には予測を「ベストエフォート」と明示する
  - DF-2/DF-4 の確定根本原因: ①Dapper×空 image_tags で COUNT 列が BLOB 化し型付き record 生成が失敗(タグ保存直後のパレット再読込で発火) ②Avalonia 12 の ClickCount が環境依存で 2 に達しない(DoubleClickDetector で補完)

## 6. 記録
- As-Built(50): Run5 を runs に追記・eco に ECO-002 / metrics(52): ECO 行 + 欠陥4・要求変更4 / cheat-log(51): Run5 のずるを C6 系で
- golden: CP-UI-G1〜G7 を再判定し golden_approvals 更新
