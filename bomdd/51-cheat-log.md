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

---

# ずる台帳 — loop-v2-viewer(ビューア拡張+GF 是正)2026-06-13 追記

V2 は 2 ラン収束。Run1(factory-02)= 全 4 単位の製造、Run2(factory-03)= scroll 仮想化の収束再製造。
報告計 14 件(blocker 0 / friction 2 / minor 12)。一次資料は各工場の最終報告。

## Run1(factory-02)— 8 件(friction 1 / minor 7)

| # | 重大度 | 内容 | 裁定 |
|---|---|---|---|
| R1-01 | minor | 見開き位置表示「n-m / total」の番号順序(読み順で小さい番号を先頭) | **accept** — golden G-8 で表示判定。K-DESIGN の位置表示規定と整合 |
| R1-02 | minor | scroll 未ロード仮高さの代替として ViewerImage に MinHeight=64 | **accept** — Run1 時点の暫定。Run2 の仮想化で実体化制御が主機構に |
| R1-03 | **friction** | **scroll を ItemsControl 直置き(非仮想化)で実装 → REQ-055「全件一括読み込みしない」未達** | **収束再製造(ユーザー裁定)** — P-05 先行測定で FMEA-016 顕在化(全件フルサイズ Bitmap 常駐・1,000 枚 8〜48GB・確実 OOM)と確定。工場の friction 自己評価は過小(scroll では SourcePath 不変=Dispose 発火せず)。**設計側帰属の一面あり**(M-UI-018 は段階読み込みを指定済みだが「ScrollViewer ベース」の語が ItemsControl 直置きへ誘導しうる+K-AVALONIA のグリッド仮想化罠がビューア scroll に明示適用されていなかった)→ K-AVALONIA/M-UI-018 を Run2 向けに強化。Run2 で解消 |
| R1-04 | minor | SHIFT 単独 1 ページ送りの検知(Tunnel KeyDown/Up で SHIFT 状態を VM へ) | **accept** — Avalonia の Shift+矢印ジェスチャ煩雑回避の妥当策。キーリピート抑制なしは exploratory(P-06) |
| R1-05 | minor | 開き方向トグルを 2 ボタン化・モードアイコンをテキストラベルに簡略 | **accept** — golden G-8 で判定。K-DESIGN の PathIcon 厳密採用は G-8 所見があれば V3 申し送り |
| R1-06 | minor | i18n 新規キー(viewer.* / hierarchy.conditionSummary.* / hierarchy.addRootNamed 等)の命名・en 文面 | **accept** — K-I18N の `<画面>.<要素>` 規約・ja 正/en 併記に準拠。条件サマリ ja 文面は spec §2.6 採用 |
| R1-07 | minor | GF-03 家アイコンの StreamGeometry 形状(自作) | **accept** — golden G-6/G-8 で見栄え判定。形状未規定の沈黙次元 |
| R1-08 | minor | GF-02 行選択視覚除去を防御的に明示(既存 plain テンプレに Transparent 追加) | **accept** — 意味論不変。リスト表示の行選択視覚は正として維持 |

## Run2(factory-03)— 6 件(friction 1 / minor 5)

| # | 重大度 | 内容 | 裁定 |
|---|---|---|---|
| R2-01 | minor | scroll 仮想化に ListBox を採用(ItemsRepeater でなく — BOM は両許容) | **accept** — VirtualizingStackPanel 既定+ContainerClearing/GetRealizedContainers/ScrollIntoView の仮想化 API が揃い最小改修 |
| R2-02 | minor | 画面外解放のトリガに ListBox.ContainerClearing を採用 | **accept** — リサイクル時に ViewerImage.Release() を呼ぶ妥当な結線 |
| R2-03 | minor | CancellationToken の粒度(ViewerImage に CTS 1 本・SourcePath 変更/Release で張替) | **accept** — 在飛行ロードの打ち切り+完了レースの対象一致チェックで滞留防止 |
| R2-04 | **friction** | 仮想化で実体化コンテナが疎になるため、位置追跡の rect→index 写像を View 層が担う結線に変更(OC-11 計算核は不変) | **accept** — 計算核 ScrollPositionTracker(CP-VIEWER-014 合否対象)は無改変。View 結線のみ Run1 から変化。ViewModel に UpdateScrollPositionByIndex 追加(意味論不変) |
| R2-05 | minor | 先読みウィンドウ定数を VirtualizingStackPanel 既定挙動に委譲(原典の前10/後15 は移植せず) | **accept** — M-UI-018 が「実装定数・合否対象外」と明記。合格条件「総枚数非比例メモリ」は既定仮想化で充足 |
| R2-06 | minor | scroll クリック無操作を選択ハンドラ非結線+選択視覚除去+Focusable=False で実現 | **accept** — 観測可能な挙動ゼロ。ホイールスクロールは維持 |

## V2 構造的所見(最重要)

| 項目 | 帰属 | 裁定 |
|---|---|---|
| **scroll 非仮想化(R1-03)** | 設計側+製造側の両面 | M-UI-018 は段階読み込みを指定していたが、K-AVALONIA のグリッド仮想化罠(FMEA-013)がビューア scroll へ明示適用されていなかった(設計側の沈黙)。一方で工場が指定された読み込みウィンドウを実装しなかった(製造側)。**P-05 観測駆動で BOM へ仮想化必須を明文化 → fresh re-run で是正**。BomDD の「測定 → BOM 補正 → fresh 再製造」収束が機能した実例 |

## Run3(golden 駆動の視覚是正 — 設計者直接修正)2026-06-13 追記

golden 第1回判定(承認者 maintainer)で CP-UI-G8 承認・CP-UI-G1/G6 が GF-01/GF-02 で NG、加えて G-8 所見 GF-V2-01(方向トグルの選択状態不明)。
**裁定: 設計者直接修正(maintainer 選択)** — GF-01/02/V2-01 は視覚要件で、隔離工場も設計者もヘッドレスでは目視確認不可。fresh 工場では「一見正しいが実機で失敗」を再生産しがちなため、設計者が直接是正し最終検証は maintainer の golden 再承認とした。

| 項目 | 真因 | 是正 |
|---|---|---|
| GF-01(ダイアログ伸長) | 外側 ScrollViewer が全体を包む+ValuesList MaxHeight=0→140 の伸びに SizeToContent=Height が追従 | 外側 ScrollViewer 廃止→DockPanel(ボタン下端固定)。ValuesList を固定 Height=140(最初から固定サイズ・候補追加はリスト内部スクロールのみ・窓不変) |
| GF-02(行が hover/選択で反応) | Fluent テーマの :pointerover/:selected が行 ListBoxItem のテンプレート部品に背景適用(テンプレート定義値より Style 高優先)+セルクリックが行選択へバブル | (1)OnCellPressed で e.Handled=true (2)アプリスコープ :pointerover/:selected /template/ ContentPresenter スタイルで透明化 (3)セル単位 cellFrame:pointerover に淡い 8% アクセント(エクスプローラー同様セルのみ反応) |
| GF-V2-01(方向不明) | 見開き+方向サブトグルで現在方向が不明瞭 | view-prism 準拠の上位 4 ボタン(単一/縦スクロール/右開き/左開き)へ再構成(『見開き』抽象廃止=仕様 ViewerMode 4 値一致)・active を filled |

**設計判断(GF-02 の A/B)**: B(ItemsRepeater+UniformGridLayout で『行』撤廃の真のアイコンビュー)が抽象として理想だが、Avalonia.Controls.ItemsRepeater は Avalonia 12 でコア非同梱・retired 別パッケージ(maintainer 確認)。→ A(行リスト維持+選択視覚の確実な無効化)で承認を取り、B は採用可否スパイクとして V3 申し送り(31-kbom K-AVALONIA / 50-as-built carryover)。
**事実訂正**: 設計 AI が当初「ItemsRepeater は Avalonia コアの慣用手段」と述べたのは誤り。maintainer がソース(NuGet/GitHub)で retired 別パッケージと確認・訂正。K-AVALONIA に注記済み。

golden 再判定(第2回・2026-06-13): Run3 是正後 **CP-UI-G8/G1/G6 全承認**(50-as-built golden_approvals_v2 results_round2)。回帰: build 警告 0・unit 290/290・凍結オラクル 31/31 維持。

## V2 凍結オラクルとの関係
固定オラクル S-01〜S-18 は Run2 後・Phase 5 受入・**Run3 後**とも **全 31 facts PASS**(S-01〜12 は loop-v1-r1、S-13〜18 は loop-v2-r1 で凍結。ともに不変)。
V2 のずるも全件 **表面(仮想化方式・UI 形態・アイコン・i18n)と実装定数**に集中し、核(計算核 M-VIEWERCORE-017)の挙動乖離はゼロ。
唯一の品質欠陥(R1-03 scroll メモリ)も計算核でなく表示機構の問題で、P-05 観測 → BOM 強化 → Run2 で構造的に解消。core の挙動は凍結オラクルが退行ゼロを確認した。

---
# Loop V3(loop-v3-similarity)— factory-04 ずる裁定(2026-06-13)

製造 1 ラン(factory-04・収束再製造なし)。ずる 6 件・**blocker 0**(全 friction/minor)。物理ファイル操作の混入ゼロ(INV-009 厳守)。

| ID | 分類 | 重大度 | 内容 | 裁定 |
|---|---|---|---|---|
| CHEAT-01 | C3 pHash レシピ細部 | friction | cos 固定表+2 パス DCT で単色画像の非 DC 係数が ±1e-14 ぶれ、中央値比較が符号ノイズで暴れ縮退 0x8000000000000000 にならない → 左上 8×8 を `Math.Round(c,6,AwayFromZero)` で量子化 | **採択+BOM 織り込み**: 仕様 §2.10.1 の縮退規則を成立させる正当な決定性確保。8bit 整数輝度由来の有意係数は O(1)+ で 6 桁丸めはサブ ULP ノイズのみ畳み込み・実構造無影響。**K-PHASH に codify**(次回 fresh 再走で再発見不要)。固定オラクル S-19 が検査 |
| CHEAT-02 | C2 マイグレーション番号 | minor | 既存 001-views-description の続きで 002-similarity-tables を採番(2 テーブル+索引 2・LatestDdl 同期) | 採択(CP-DB-006 スキーマ同値・S-22 が L2 検査) |
| CHEAT-03 | C2 i18n キー命名 | minor | similar.*/merge.*/trash.* 新設(ja 正・en 併記)。原典 modals.* は範囲外機能含むため非再利用 | 採択(K-I18N 規約準拠) |
| CHEAT-04 | C3 レイアウト値 | minor | 類似検索 820×600・マージ 760×560・トラッシュ 720×520・類似度バッジ・実行ボタン非危険色 | 採択(K-DESIGN 範囲・golden G-9 承認対象) |
| CHEAT-05 | C3 閾値刻み・役割割当 | minor | スライダー整数刻み(TickFrequency=1・Snap)・入口=最後選択を基準/マージ先、残りをマージ元 | 採択(K-DESIGN 範囲・golden G-9 承認対象) |
| CHEAT-06 | C3 非類似テスト合成 | minor | CP-PHASH-016 の近傍/非近傍を 16hex 手書きせず性質ベースで合成(§2.10.1 受入(c)指示どおり) | 採択(性質ベース凍結方針に合致) |

**裁定総括**: blocker 0。核(計算核 M-PHASH-020/MergeCalculator)の挙動乖離ゼロ。ずるは全件 pHash 細部(決定性確保=CHEAT-01)・スキーマ/i18n/レイアウトの実装判断に集中。**収束再製造は不要**(自己受入緑+固定オラクル S-19〜24 全通過で受入)。CHEAT-01 のみ BOM(K-PHASH)へ織り込み、他は採択記録のみ。

## V3 固定オラクル受入(設計者側・工場非開示)
S-19〜24 を tests/ViewPrism2.Oracle へ設計者実装。**全 57 facts PASS**(S-01〜18 の 31 + S-19〜24 の 26)。S-01〜18 回帰ゼロ(loop-v1-r1/loop-v2-r1 不変)。S-19 単色=0x8000000000000000・距離関係、S-20 変換ベクタ exact、S-21 候補 normal 限定+降順安定、S-22 ペア正規化+連鎖無効化+CASCADE、S-23 マージ値決着、S-24 物理非破壊(EQ-003 スナップショット)を独立検証。
