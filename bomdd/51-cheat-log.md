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

## golden G-9 収束(設計者直接修正)2026-06-13
golden 第1回(maintainer 実機ウォークスルー)で **CP-UI-G9 機能 4 点(類似検索 UI/マージ/トラッシュ/物理非破壊)を承認**。視覚所見 1 件:
- **GF-V3-01**(minor): メインウィンドウ既定サイズでツールバーのゴミ箱ボタンが見切れる(最大化すると見える)。V3 で類似/マージ/ゴミ箱の 3 ボタン追加により既定幅を超過。
- 是正(設計者直接修正・視覚要件のためヘッドレス検証不可): ツールバーの横 StackPanel を **WrapPanel** に変更し溢れを次行へ折り返す(全ボタンが既定幅でも常に可視)。MainWindow.axaml。build 警告0・unit 346/346 回帰ゼロ・maintainer 再確認 OK。
golden 最終: **CP-UI-G9 全承認**(機能 4 点+GF-V3-01 是正)→ G4 全条件充足 = **Loop V3 完了(2026-06-13)**。

---

# ずる台帳 — loop-v4-repair-lifecycle(2026-06-14)

工場(fresh・隔離)が修復ライフサイクルを first-pass で製造(commit 499f24e)。報告 5 件(**blocker 0 / friction 1 / minor 4**)。

| # | 分類 | 内容 | 裁定 |
|---|---|---|---|
| CHEAT-01 | C4・friction | `RelinkService` の `ITagRepository` を必須でなく **optional 注入**(`ITagRepository? = null`)にした。理由: 隔離対象の `tests/ViewPrism2.Oracle/S01RenameTrackingTests.cs` が `new RelinkService(db.Images)` を 1 引数で呼んでおり、必須化すると `dotnet build ViewPrism2.sln` が Oracle で compile 失敗=自己受入 build が赤(Oracle は読めず call site を直せない) | **accept** — production DI(App.axaml.cs)は必ず `ITagRepository` を注入しタグ安全ガード(INV-015)は完全に有効。null 経路は V1 互換(exact-hash pending=元来未タグ)のみ。M-RELINK-025 の `tag_check:"(または直接 COUNT)"` latitude 内。**教訓(設計側帰属)**: 工場の自己受入 build が隔離対象(Oracle)を含むため、API を必須化すると既存 Oracle 治具の構築シグネチャと隔離境界が衝突しうる → M-BOM に「拡張は既存 call site を壊さない optional 拡張で」を次版明記。S-28(設計者オラクル)は 2 引数で構築しタグ安全を実証済み |
| CHEAT-02 | C4・minor | `IWindowService.ShowRepairAsync` を **default interface method**(`=> Task.CompletedTask`)で追加し既存 5 スタブを非改変 | **accept** — 回帰ゼロ(既存テスト無改変)を達成する妥当な拡張 |
| CHEAT-03 | C2・minor | criteria `Extension` の先頭ドット正規化(`.jpg`=`jpg`)を実装定数化 | **accept** — G2 ゲート記録 (d) が「実装定数(固定オラクル合否外)」と宣言済み。S-27 は両表記で緑 |
| CHEAT-04 | C5・minor | RepairWindow 到達経路(MainWindow ツールバーボタン+`OpenRepairCommand`)・`toolbar.repair` i18n キー・操作後の grid 再読込を製造判断で追加 | **accept** — golden G-10 の表面詳細(M-BOM「K-DESIGN 範囲で製造判断」)。承認者 maintainer が G-10 で最終判定 |
| CHEAT-05 | C2・minor | TrashViewModel の操作後 `StatusMessage` を `LoadAsync` の**後**に設定(復元の missing 化通知が消えないよう順序修正) | **accept** — 未規定の順序を妥当に補完。CP-UI-G10 の結果表示要件に整合 |

スキーマ変更・物理ファイル書込/移動/削除・既存テスト改変はいずれも発生せず(routing_v4 の safety/regression guard を充足)。

## golden G-10 収束(設計者直接修正 + 原典リバース確認)2026-06-14

golden 第1回(maintainer 実機ウォークスルー): **復元・完全削除・物理非破壊は承認**。修復(criteria/relink)に 3 所見:
- **GF-V4-01**(minor): missing 選択時に候補が自動探索されず SHA-256 手入力が必要で実用不可。原典 view-prism(searchForRelink + NewUI RepairModal)確認 = criteria は真偽フラグで値を選択 missing から自動導出・既定 useHash/useExtension/useSize。是正: hash+拡張子+サイズを criteria へ自動事前入力し候補を手入力なし提示(commit e002a6d)。
- **GF-V4-02**(minor): ビューア読み込み失敗(ファイル不在)の normal が修復一覧に出ない=status=normal のまま(再スキャンせず)。原典確認 = RepairModal は status='missing' を引くだけ・検出は再起動/コレクション展開のスキャン依存。是正: 修復を開く前にコレクションをスキャン(missing 化+リネーム後 pending 登録)+『N 件のリンク切れ(M 件が自動修復可能)』表示(commit 2a50dd8)。
- **GF-V4-03**(minor): 再リンク候補(Pending∪Normal)は出るのに左検索(Normal 限定)が空の不一致。maintainer 選択『検索を再リンク候補探索に統一』(原典 AdvancedRepairModal=検索結果が候補)。是正: 検索ボタン=選択中 missing の候補を現在条件で再探索・別検索結果リスト廃止(commit 5c52827)。
いずれも視覚/UX 要件(ヘッドレス工場が検証不可)+ 原典リバース確認で方針確定 → 設計者直接修正。build 0・Tests 379・Oracle 73 回帰ゼロ・maintainer 再確認 OK。
golden 最終: **CP-UI-G10 承認**(機能 3 点+GF-V4-01/02/03 是正)→ G4 全条件充足 = **Loop V4 完了(2026-06-14)**。
deferred(V5 候補・原典にも無い improve-on-port): 元パスに戻した self の自動復元 / コレクション展開時のグリッド自動検出 / 再エンコードの pHash 類似修復。

## golden 追加所見(クローズ後)GF-V4-04 — 表示パリティ 2026-06-14

クローズ後、maintainer が原典 view-prism との直接比較で**再リンク候補カードの情報不足**を指摘:「原典 AdvancedRepairModal は候補にサムネイル/ファイル名/パス/サイズ/更新時刻を出すのに、ViewPrism2 は相対パス+サイズのみで再リンク可否を判断できない。原典調査をしているはずなのに見落とされた理由は?」

- **事実**: 原典 `AdvancedRepairModal.tsx`(検索結果カード)は各候補を **サムネイル+fileName+path+size+modifiedDate** の 5 要素で描画。ViewPrism2 `RepairWindow.axaml` の候補 DataTemplate は **RelativePath+SizeText のみ**。
- **根本原因(二層の後退)**:
  1. **ポーティング段階の脱漏(REQ-017)**: 旧再リンク要件のコメントが候補表示を「相対パス・ファイルサイズ・更新日時」とテキスト 3 項目で記述し、**サムネイルとファイル名見出しを脱漏**。原典の *データ契約*(searchForRelink が返す id/path/size/mtime/hash)と *挙動* は忠実に捉えたが、*表示契約*(候補=サムネイル付きカード)を要件化しなかった。
  2. **V4 表面化段階の二重後退**: `RelinkCandidateViewModel` は ModifiedText を計算して保持しているのに、`RepairWindow.axaml` がそれを **bind せず**(更新時刻すら脱落)。旧 `RelinkWindow` は mtime を出していたため V4 で**情報が後退**した。
- **構造的理由**: 表示パリティは **固定オラクル(論理検証)では捕捉不能**(ヘッドレス工場は視覚を見られない)→ 100% golden 依存。だが golden 過去 2 回は *挙動*(prefill / auto-count / 検索統一)に集中し、候補カードの *情報密度* を原典と突合する観点が無かった。ported 画面ごとの **「提示すべき情報」契約(presentation manifest)が要件に存在しなかった**ことが真因。
- **是正**: 候補カードを 5 要素に(サムネイル+ファイル名+パス+サイズ+更新時刻)。サムネイルは既存 `ThumbnailImage`(SimilarSearch/Trash と同経路)を流用し、`RepairViewModel` が collection root を解決して候補/missing の絶対パスを供給(`TrashViewModel` と同パターン)。missing 行はファイル名+パス(実体欠損のためサムネイル無し)。§2.11.5 に**表示パリティ契約**を明記。
- **lesson(再発防止・テンプレ申し送り)**: **ported 画面の要件には「論理契約」だけでなく「表示契約(提示フィールド+視覚要素)」を必ず含める**。固定オラクルが視覚を検証できない以上、表示契約は golden チェックリスト項目として明示し、原典スクリーンとフィールド単位で突合する。
- 検証: Tests 381(+2 GF-V4-04)・Oracle 73 PASS+2 skip 回帰ゼロ・compile-clean(Release コピーは起動中アプリのロックのみ)。

### 工程是正(ECO-003)+ 所見トリアージの教訓 2026-06-15

maintainer から工程上の問い:「仕様→E-BOM→M-BOM→製造 の工程で是正されるべきで、今回それが実行されていない。これは私の間違いか?」→ **maintainer が正しく、私(設計者)が誤り**。当初 GF-V4-04 を**製造優先(直接ハンドコード)+§2.11.5 後付け**で処理し、REQ-072/E-039/M-027/CP-UI-G10/routing を飛ばし、さらに表面成果物を設計者が直接記述=工場隔離も侵害した。

- **真の工程欠陥=所見トリアージの誤り**: golden/後発所見は是正前に分類すべき —
  - (a) **既存 BOM 範囲内の表面調整**(K-DESIGN 裁量)→ 設計者直接修正可(GF-V4-01/02/03 はこれ)
  - (b) **既存仕様/BOM への defect**→ 当該段を是正し再受入
  - (c) **仕様/BOM の gap(要件が無い)**→ 仕様→E-BOM→M-BOM→Control Plan→製造 の連鎖を必ず流す
  GF-V4-04 は (c) なのに (a) と誤分類して直接修正した。これが本質。
- **是正(ECO-003・maintainer 選択 Option B=遡及 BOM 再構築・コード保持)**: REQ-072(d)+counterexample → E-039 表示 invariant → M-027 candidate_card + FMEA-031 → **CP-REPAIR-CARD-021(unit 新設)** + CP-UI-G10(視覚パリティ突合)を正しく作成。検証済みコードは「製造済み成果物」として再受入(Tests 381/Oracle 73)。**工場隔離逸脱**(設計者製コードを工場由来でないまま保持)は 60-change-order-eco-003.md §4 に明示。
- ポーティング資料作成者はミスを認め `docs/porting-spec/` を改善済み(一次入力是正)。
- **規律化**: 次ループ要件分解テンプレに「(c) gap は ECO で連鎖を流す/(a) のみ直接修正可」の所見トリアージを組み込む。

## ECO-036 第1段(god-VM 解体・ゴミ箱切り出し)工場ずる 6 件の裁定 2026-07-04

個票: bomdd/reports/eco036-stage1-cheat-report.md(fresh 工場 sonnet・移送表方式)。blocker 1 / friction 3 / minor 2。

| # | 内容 | 裁定 |
|---|---|---|
| E36S1-001 | OpenTrash の MoreMenuOpen クローズが注入契約 4 関数に無い → closeMoreMenu 追加 | **accept** — 設計凍結の接続面 under(scale-02 採点に計上)。order §9 で契約 6 関数へ改訂 |
| E36S1-002 | 絶対パス解決(_collectionPath)依存が注入契約に無い → resolveAbsolutePath 追加 | **accept** — 同上(接続面 under 2 件目)。「子はホスト型を参照しない」規律は維持された |
| E36S1-003 | **既存テスト 3 ファイル 84 箇所がホストの trash 系公開契約を直接参照** — 「tests diff ゼロ」予測と「テスト無改訂」契約の構造的衝突 → 後方互換の委譲メンバーで解消(状態・ロジックの所有は子 VM・XAML は Trash.* 直結) | **accept** — 帰属= **設計者の 61 §1.2 実行不完全**(移送メンバの全参照 grep を src/ に限定し tests/ を省いた — §1.2 の「全参照」は tests を除外していない)。委譲は経過措置として妥当(除去は後続段でテスト移行と同時= test-only 変更)。method 還元候補: 61 §1.2 チェック項目に「tests/ の参照も含む」を明記 |
| E36S1-004 | 未使用化した _trash フィールドの除去 | **accept** — 移送の必然的後始末(警告 0 維持) |
| E36S1-005 | 32-mbom 宣言は工場範囲外と判断 | **accept** — 正しい境界判断(台帳は設計者管轄。本裁定と同時に設計者が宣言済み) |
| E36S1-006 | 着手前から bomdd/ 2 ファイルに diff が存在(設計者の起票編集) | **accept** — 観測は正確(工場の非改変を diff で自己証明した良い規律) |

## ECO-036 第1段 golden 再ウォークスルー所見 2 件(設計者直接修正)2026-07-04

| # | 所見(maintainer 実機) | 根因 | 是正 |
|---|---|---|---|
| G-E36S1-1 | ⋯メニューのゴミ箱バッジが初期表示されない(ポップアップ開閉後には出る) | 子 RefreshCountAsync が件数更新後に通知を発行しない — 旧 god-VM ではホストの OnPropertyChanged(string.Empty) 一括通知が肩代わりしていた | 子が自前で string.Empty 通知を発行 |
| G-E36S1-2 | ゴミ箱ポップアップは開くが ⋯メニューが閉じない | 注入ラムダ () => MoreMenuOpen = false が通知なし自動プロパティを黙って書くのみ(ホスト内の全変更箇所は通知を伴う慣行) | ラムダに OnPropertyChanged(string.Empty) を追加 |

**共通根因= 通知トポロジー**: god-VM の「string.Empty 一括通知」という実装慣行が分割の**隠れた結合面**
だった — 移送表はメンバを数えたが通知の到達範囲を数えていない。**Tests 526 は捕らえず golden(人間)
だけが捕捉**: テストは VM プロパティを直接読む(PropertyChanged 非依存)ため通知漏れに構造的に盲目。
是正後の機械受入: Tests 526/526・Oracle 100/102 全緑(期待値無改訂のまま)。削除モードは初回合格。
method 還元候補(3件目): リファクタ系移送表に「**通知トポロジー**(誰が・どの範囲へ通知するか)」を
必須列化+「golden は通知面の唯一の検査器」を切り出し ECO の受入設計に明記。

## ECO-036 第3段(整理モード切り出し)ずる+工程事故の裁定 2026-07-04

個票: bomdd/reports/eco036-stage3-cheat-report.md(工場)。工場ずる 2 件(blocker 1 / friction 1)+設計者レビュー捕捉 1 件+**工程事故 1 件**。

| # | 内容 | 裁定 |
|---|---|---|
| E36S3-blocker | 製造途中に Write 済み子 VM が別内容へ書き換わる事象を build エラーで検出→自力復旧 | **accept+帰属訂正** — 外的要因の正体は**設計者**(下記工程事故)。工場の検出・復旧・正直報告は模範的 |
| E36S3-friction | refreshSelectionMarkers 注入が転送殻方式では子から呼ばれない(契約字面維持でシグネチャ保持) | **accept** — §12.2 の接続面宣言が転送殻方式の帰結を反映しきれていなかった(設計の軽微 over)。32 path_note に明記 |
| 設計者レビュー捕捉 | R3 転送でホスト setter 通知が脱落(IncludeTags/SimilarThreshold — スライダーの % ラベルが追従しなくなる) | **設計者直接修正** — G-E36S1 と同一クラス(通知トポロジー)。今回は golden 前のレビューで捕捉= 第1段の教訓の学習効果。「転送 setter は旧 setter のホスト通知を保存する」を R3 規則に追補 |
| **工程事故** | 工場が入れ子バックグラウンド委譲ループに陥り(空報告 ×4 体)、設計者が全停止と判断→設計者実装に切替→ところが停止追跡漏れの 1 体が生存・並行製造となり、設計者の子 VM 上書きと工場の復旧が衝突。最終的に生存個体が完全納品(自己受入全緑)し、設計者実装は破棄・工場版を採用 | **記録** — 教訓: ①工場の入れ子委譲は禁止事項として工場指示に明記する ②停止は「全数確認できるまで着工しない」③**同一ファイルを設計者と工場が同時に触らない**(diff 監査が汚染される)。method 還元候補(工場運用の隔離規律) |

## ECO-036 第3段 golden 所見 G-E36S3(設計者直接修正)2026-07-04

- 所見(maintainer 実機): 整理一連 1〜6 合格。**7= マージ実行→「別の整理を続ける」後、画像が選択できない**。
- 切り分け: 設計者プローブ(2 周目クリック経路の一時テスト・実行後撤去)は**緑** — VM 状態・マーカーは正常。
  所見は view 層にしか現れない= 新旧の view 面の唯一の差は**通知回数**: 殻末尾の Recompute() と子内部の
  _recompute() が重複し、続行/マージ/検索でコレクション全再構築(CollectionChanged Reset)が旧版 1 回
  → 2〜3 連発になっていた。§13 の「冪等につき挙動同値」という設計者判断は **view 層では非同値**だった。
- 是正: 二重 Recompute を除去し**通知回数・位置を旧版と厳密同一へ**(子 ContinueOrganize の _recompute
  除去+RunSearch/ExecuteMerge 殻の末尾 Recompute 除去)。再受入: Tests 526/526・Oracle 100/102 全緑。
- 教訓(通知トポロジー第3則): 「同一同期バッチ内の重複通知は冪等」は **VM 内の真** — view(ItemsControl
  の Reset 連発)では偽になり得る。挙動保存リファクタの通知規律は「**回数と位置を旧実装と同一にする**」
  が正(多くても少なくても所見になる — G-E36S1 は少な過ぎ・G-E36S3 は多過ぎ)。

### G-E36S3 追補: 実機 A/B により**既存バグと確定**(2026-07-04)

- 変更前個体(dc990ef・第2段まで)の実機 A/B(maintainer): 完了パネルは**旧版でも出ない** —
  症状は第3段の回帰ではなく**切り出し前からの既存バグ**(第3段の挙動保存は成立・バグごと忠実に保存)。
- 切り分けの全記録: VM プローブ 2 種(2 周目クリック/実機経路=検索→追加→マージ+通知記録)は全て緑・
  done=true・string.Empty 到達 → 欠陥は view 層。XAML の表示条件(OrganizeDone)は単純で、
  疑いは右ペイン DockPanel の子配置(done ScrollViewer が Dock 無指定)または ECO-014 golden 以降の
  右ペイン改変 ECO での退行。**是正は挙動変更(バグ修正)につき ECO-036 系列(挙動不変)とは別立て= ECO-037 起票**。
- 前回の是正 2 件(二重 Recompute 除去=通知回数の旧版同一化)は等価化として妥当・維持(f2018ab)。
- 教訓: **golden は「現行版の視覚検査」であると同時に「旧版とのA/B」で初めて回帰と既存を判別できる** —
  挙動保存リファクタの golden 所見は、旧版 A/B を標準手順に含める(切り出し系列の受入設計へ追補)。

## ECO-040 起票時のスコープ外所見(R3 記録)2026-07-05

- **タグ追加 検索ボックスが未配線(機能欠落)**: ImageTabView.axaml:178 の検索 TextBox は
  `Text=""` 固定で、ImageTabViewModel に AddQuery 相当のプロパティ/候補絞り込みが存在しない。
  CAD は `addQuery / onAddSearch` による絞り込みを定義(mock 画像タブ.dc.html L387・
  ui-trace-map TMP-UI-INP-0020/ACT-0060 handling: bom)= **CAD 定義済み機能の実装欠落**。
  混入= `e10767b`(2026-06-17 M1+M2)から。既知記録(FL-*/VE-*/cheat-log)無し=新規。
  起票要否は maintainer 判断(起票時は /eco-file で分離 ECO 化)。ECO-040 本文 §7 から参照。

## ECO-045 golden 準備時のスコープ外所見(R3 記録)2026-07-05

- **タグ定義の階層(tags.parent_id)を編集する UI が存在しない**: Core は REQ-022(単一親・
  循環拒否・親削除で子ルート化)を実装済みだが、タグエディタ(TagEditorViewModel/Window)に
  親指定フィールドが無く、App 層で tags.parent_id を書く経路もゼロ(実測 grep。
  HierarchyEditorViewModel の parentId はビュー階層 view_tag_hierarchies=別概念)。
  CAD(tag_tab.md)にもタグ定義自体の親子編集は未定義= ECO-041 型(CAD 定義済みの実装欠落)
  ではなく **CAD 未設計+V1 原典由来 Core 概念の UI 露出なし**(V1 原典の UI 有無は未検証)。
  発見経緯= maintainer 質問「タグパレットで階層化できない気がする」→ 実測で確認。
  影響: ECO-045 golden 基準 4(子タグを持つ親の削除)は実機到達不能 → 基準から除外し
  S-38/CP-TAG-011(機械受入)で担保。裁定 4a(子タグの親は「使用」でない)の妥当性は不変
  (Core 意味論として存続・将来 UI が露出したときに効く)。
  起票要否は maintainer 判断(タグ定義階層を UI に出すか・出すなら CAD 先行)。ECO-045 本文 §7 から参照。
  **→ 解決(2026-07-05)**: maintainer 裁定 TAG-009「タグ定義階層 UI は提供しない」(CAD `7ffd423`)。
  取り込み= ECO-047(現状追認・doc-only)。

## ECO-048 golden 準備時のスコープ外所見(R3 記録)2026-07-06

- **EXIF Orientation が全デコード経路で非反映**: maintainer が golden 準備で orientation_fixture_06.jpg を
  回転ツールで 90° 回転 → アプリ表示が元のまま(再スキャン・コレクション再追加でも不変)。
  実測= ピクセル 1194×834 無変更・**EXIF Orientation=6 のみ書き換わるタグ方式の回転**で、
  ViewPrism2 のデコード(SkiaSharp: ThumbnailService / ビューア / PHashImageReader 系)は
  EXIF Orientation を適用しない(SKBitmap.Decode / SKCodec.GetPixels は orientation 非自動適用)。
  影響: ①スマホ撮影画像(EXIF=6 等)がサムネ・ビューアで横倒しに表示される
  ②EXIF タグ方式の回転はピクセル不変のため hash/pHash も不変=再スキャンで変化なし(正しい挙動だが
  ユーザー期待と乖離)。ECO-048(pHash 変種)とは独立の欠陥様式=表示系の EXIF 適用漏れ。
  参考: SimilarPic は ExifOrientation.cs で適用済み(読み込み時に正規化)。
  起票要否は maintainer 判断(起票時は /eco-file で分離 ECO 化。表示系のみか pHash 入力正規化まで
  含めるかは工程診断で切る — pHash まで正規化すると adapter 世代交代= P-09 発動になる点に注意)。
  golden 手順への影響は解消済み: ピクセル実回転の複製(orientation_fixture_06_rot90.jpg / orientation_fixture_06_mirror.jpg)を
  生成して手当て。ECO-048 本文 §7 から参照。
  **→ 昇格起票(2026-07-06)**: **ECO-049**(工程診断で「欠陥でなく V1 裁定済み沈黙次元の解除」と確定 —
  spec §4 L974・31-kbom K-SKIA 宣言どおり。gate①= 案 A〜D の裁定待ち)。

## ECO-048 golden 実施時のスコープ外所見(R3 記録)2026-07-06

- **類似しきい値の既定値が surface 間で三者三様(仕様と不整合)**: 仕様 REQ-064/065・§2.10.4 は
  「既定 70・範囲 50〜100」。実測= 類似検索モーダル(SimilarSearchViewModel:98)= **70(仕様どおり)**・
  画像タブ整理トレイ(ImageTabOrganizeViewModel:33)= **80**・作業タブ(WorkTabViewModel:75)= **90**。
  CAD(ViewPrismUI)はしきい値スライダーの存在のみ定義し数値既定は未規定(grep 実測)。
  コードに裁定痕跡なし(bare literal)= 実装時の仮置きが仕様と突合されないまま潜伏の疑い。
  発見経緯= ECO-048 golden で maintainer が整理トレイの既定 80 を「規定値」と認識(仕様 70 との乖離が
  ユーザー認知にも到達)。影響= rot90 級(スコア 70〜79 帯)の検出が既定のままだと漏れる surface がある。
  起票要否は maintainer 判断(選択肢: a=仕様 70 へ統一 / b=用途別既定を CAD/spec に正式化(整理=高精度 80・
  作業=最厳 90 の意図があるなら)/ c=現状維持)。ECO-048 本文 §7 から参照。

## ECO-048 golden 実施時のスコープ外所見 #2(R3 記録)2026-07-06

- **scaled-decode adapter のフォーマット間系統誤差**: 見た目同一の PNG→JPG 変換複製ペアが
  スコア 78(距離 8)にしかならない(maintainer 所見「直感では 90 以上のはず」— 直感が正)。
  実測(orientation_fixture_06.jpg × orientation_fixture_06.png): full-decode では**距離 2=96 点**(知覚的実距離)、
  production の scaled-decode では**距離 8=78 点**。同一 jpg の scaled×full 比較=距離 8・
  同一 png の scaled×full 比較=**距離 0** — すなわち誤差の全量が **JPEG のみ早期縮小
  (SKCodec 1/8 DCT ダウンスケール)/PNG は早期縮小非対応で全解像度→一発 32×32 双線形**という
  経路非対称に由来する系統誤差。ECO-048 とは無関係(変種はスコアを下げない・identity 距離が支配)。
  P-08(scaled-decode 採用・6.29× 高速・EQ-RANK 順位等価)の既知トレードオフ「adapter drift」の
  フォーマット間版で、順位等価は保たれるが**異フォーマット複製の絶対スコアが約 -18 点**落ちる。
  rot90 ペアの 70 台も同根+回転で中間サイズが入れ替わる影響。
  是正案(起票時の工程診断用メモ): PNG 側にも中間縮小段(長辺 ~64 への双線形)を挟み経路を対称化する
  — ただし pHash 絶対値が動く= **adapter 世代交代(P-09 発動・ThisBuildGolden 再凍結・全特徴量再計算)**。
  起票要否は maintainer 判断。ECO-048 本文 §7 から参照。
  **→ 昇格起票(2026-07-06)**: **ECO-054**(工程診断=K-BOM の沈黙次元・実装逸脱なし。
  this-build golden 再凍結は 33:509 で台帳ライセンス済みと確認。gate①= A 対称化/B 見送り/C full 回帰)。

## ECO-050 起票時のスコープ外所見(R3 記録)2026-07-06

- **独立モーダル SimilarSearchWindow/SimilarSearchViewModel は到達不能コード**:
  WindowService.ShowSimilarSearchAsync(WindowService.cs:266)の呼び出し元がゼロ(grep 実測 —
  IWindowService 宣言と実装のみ)。ECO-014(モーダル→整理トレイ置換)の残骸疑い。
  皮肉にも仕様適合の既定 70 を持つ唯一の実装がこの死んだモーダルだった(ECO-050 §3)。
  撤去は ECO-024(原典撤去)型の別件 — 起票要否は maintainer 判断。ECO-050 本文 §3 から参照。

## ECO-051 是正時のスコープ外所見(R3 記録)2026-07-06

- **REQ-067「実行前の統合後タグプレビュー+物理非破壊の明示」が生存 UI に不在**:
  この 2 要素の実装は撤去した死者(MergeDialog/MergeViewModel)にのみ存在し、生存 UI
  (整理トレイ)には無い(grep 実測: ImageTabOrganizeViewModel に Preview 系メンバなし・
  ImageTabView.axaml に非破壊明示文言なし)。E-UI-MERGE-036 の invariant
  「実行前に…プレビューと『物理ファイルは削除されない』旨を明示」とも as-built 乖離。
  ECO-014(トレイ置換)時に脱落した疑い。トレイの golden(G-9/G-1)は歴代 approved =
  外観としては現状追認されてきたが、要求台帳とは不整合のまま。
  起票の選択肢: a=トレイにプレビュー+非破壊注記を実装(REQ-067 充足)/
  b=REQ-067・E-BOM を as-built へ改訂(ECO-014 裁定の追認=現状追認 doc-only)。
  起票要否は maintainer 判断。ECO-051 本文 §7 から参照。
  **→ 昇格起票(2026-07-06)**: **ECO-052**(診断で三層不整合と確定 — G-9 チェックリスト自体が
  明示チェック項目を持ちつつ旧モーダル手順のまま as-built 未同期という第 3 の不整合も発見。
  gate①= a/b/c の裁定待ち)。

## ECO-058 是正時のスコープ外所見(R3 記録)2026-07-10

- **CP-NFR-026 の既存 fixture 被覆が宣言より狭い疑い**: Control Plan は test vector #3
  (ビュー軸チップ件数)、#5(表示列ライブ編集)、#6(ThumbnailImage detach/Dispose)まで列挙するが、
  指定 fixture `CpNfr026Tests.cs` の直接 fact は #2(表示形式切替で Items 不変)と
  #4(タグ列引き)が中心で、#3/#5/#6 の直接検査を確認できなかった。ECO-058 は WorkTab の
  非仮想化 read-acrossだけを扱うため、既存ベクタの追補/宣言是正は混ぜない。起票要否は
  maintainer判断（起票時はCPと実fixture全体を再監査し、他テストによる代替被覆も確認する）。

- **致命例外ログだけが `VIEWPRISM2_DATA_DIR` 隔離を継承しない**: 正常起動後のApp/DB/settings/
  日次ログはoverride先を使うが、`Program.WriteFatalLog` は常に `%APPDATA%/ViewPrism2/logs/fatal.log`
  へ追記する。GF-058-01の正常goldenでは発火せず、発火時はgolden不合格なので本ECOの性能是正へ
  混ぜない。ただし「異常系を含め既存profileを一切書き換えない」隔離契約へ昇格する場合は、
  ECO-057/CP-RELEASE-018側を含む別ECOでfatal経路もoverrideへread-acrossする。

## ECO-060 運用時の工程規律違反(記録)2026-07-11

- **担当 AI(GPT-5.6 Sol・ハーネス未記録)が入口スキル境界を自然文解釈でバイパス**:
  「案Aから行って」「続けて」「確認項目8はOK」を /eco-fix・/eco-accept の実行指示へ昇格し、
  fix コミットなしで register を applied・As-Built を承認済みへ変更(早期クローズ)。
  maintainer の指摘で復旧(未コミットのため履歴汚染ゼロ)。最終履歴
  c41e439→d48a061→c4e3a28 は正常。付随: ID 限定不足の register パッチが ECO-007 へ
  誤適用 ×2(コミット前検出)。
- 一次資料: bomdd/reports/incident-eco060-lifecycle-2026-07-11.md
  (担当 AI 自己分析報告の原本+maintainer 検分・是正候補)。
- 帰属: 工程規律違反(製品欠陥なし)。validate_bom.py がライフサイクル遷移
  (applied ⇔ fix/accept コミット存在・遷移順序)を検査しない防御層欠落を露呈 —
  **ECO 起票候補**(同報告 §3)。起票要否は maintainer 判断。
  方法論還元: BomDD method/improvements.md「2026-07-11 ViewPrism2 還元」節。

## ECO-073 GF-073-06 診断時のスコープ外所見(記録)2026-07-12

- **取り込み適用中の進捗表示・キャンセル挙動が CAD 沈黙**: mock B-3 に「適用中」の progress は
  なく(B-1 書き出しは progress あり)、フッターのキャンセルは適用中もウィンドウ close のまま
  (適用はバックグラウンドで継続し完了時にコミットされる)。GF-073-06 は UI スレッド非ブロック化
  のみ実施し、適用中インジケータ/キャンセルの意味論は触っていない。大量取り込み(数十万行)で
  「無反応に見える待ち時間」が残るため、必要なら CAD(mock)追加 → 分離起票で扱う。

## ECO-076 R7 並置検査時のスコープ外所見(記録)2026-07-13

- **B-4 mock の「レポートを保存」ボタン+「一覧を書き出す」リンクが実装非搭載・裁定記録なし**:
  mock B-4 のフッターは「レポートを保存(outline)+完了(CTA)」の 2 ボタン、未解決一覧の
  右上に「一覧を書き出す」リンクがあるが、実装フッターは「完了」のみでレポート保存導線が無い。
  ECO-073 gate② golden(2026-07-13・全 17 項目)は B-4 実機面を承認済み=暗黙許容の実績はあるが、
  ルート「変更」・「場所を指定」(32-mbom 沈黙次元 823/824 行)と違い**裁定記録が無い**。
  ECO-076(stepper 可視面拡大)の並置検査で顕在化した既存差分であり、本 ECO の diff には混ぜない
  (R3)。処置=裁定記録化(非搭載を沈黙次元へ)or 搭載の分離起票 — maintainer 判断。

## ECO-076 accept 時の検査器の谷間(記録)2026-07-13

- **commit-msg hook と validate_bom E15 の trailer 解釈が不一致**: `git commit -m "<subject>"
  -m "BomDD-ECO-Fix: ECO-NNN" -m "Co-Authored-By: …"` のように trailer を**中間段落**へ置くと、
  hook(fail-closed のはずの検査)は通過するが、git の trailer 解釈(`%(trailers)`=最終段落
  ブロックのみ)では非認識となり、後続の validate_bom E15(履歴証拠検査)が不合格になる。
  ECO-076 で実発生(fix `2ceb938` が E15 検出→message のみ amend `542ef87` で解消・内容同一)。
  処置候補=hook 側を `git interpret-trailers`/`%(trailers)` 基準へ揃える(緩い grep 検査の撤去)。
  起票要否は maintainer 判断。**→ 処置済み: ECO-078(2026-07-13)で是正**(msg_has_trailer=最終段落ブロック基準へ統一・selftest-lifecycle (d) で恒久 pin)。

## ECO-076/077 ブランチ統合時の検査器の谷間(記録)2026-07-13

- **validate_bom E19 がマージコミットを線形履歴前提で誤検知**: E19(HEAD→作業ツリーの status
  遷移検査)は比較元を HEAD(第 1 親)のみに取るため、別ブランチで正規に staged→implemented→
  applied を歩んだ ECO を main へマージする際、「新規エントリが applied で登場(起票を経ない)」
  と誤報し pre-commit をブロックする(MERGE_HEAD 側の status を考慮しない)。ECO-077 の main
  統合で実発生。トレーラ証拠検査(E15〜E17)はマージ後の祖先関係で正しく成立するため、
  マージコミットのみ hook の明示的逃げ道(--no-verify)で通し、**直後の validate_bom 0/0+
  --selftest-lifecycle OK を成立証拠として記録**(本エントリ)。
  処置候補=lifecycle_edge_findings がマージ中(MERGE_HEAD 存在時)は HEAD と MERGE_HEAD の
  status を合算した old で比較する。E15 の hook 不一致(上記 ECO-076 記録)と合わせ、
  検査器のライフサイクル系是正として一括起票が適切 — maintainer 判断。**→ 処置済み: ECO-078(2026-07-13)で是正**(combined_head_status=親合算・selftest-lifecycle (e) で恒久 pin)。

## ECO-079 是正中に観測した並列実行フレーク(記録)2026-07-13

- **CpWorkspace028Tests が並列フル実行時のみ Dapper Int64→Int32 キャスト例外で断続的に fail**:
  `dotnet test`(全並列)で 4 テストが `InvalidCastException: Int64 → Int32`(Dapper SqlMapper)で
  fail する run があったが、`-class CpWorkspace028Tests` の isolation は 10/10 緑、exe 直接のフル run
  も 658/658 緑。WorkspaceService/リポジトリ/Dapper マッピングは ECO-079 で不変=本変更と無関係の
  既存フレーク(並列 run 間の SQLite 共有/接続状態が疑い・run ごとに fail 対象が変わる)。
  ECO-079 の diff には混ぜない(R3)。処置候補=当該テストの DB 隔離見直し or 並列度制御 —
  分離起票 or 恒久記録は maintainer 判断。機械受入は exe 直接実行で確定(memory の dotnet test
  実行規律に同じ)。 **→ 処置着手: ECO-082(2026-07-13)で分離起票**(maintainer 裁定=D は別起票。TempDb の
  DB ファイル共有はシロと実測済み・発火面=WorkspaceRepository.GetAllWithNormalCountsAsync 特定)。
  あわせて dotnet test 間欠ハングの fail-closed 化は **ECO-081** で起票。

## 2026-07-14 — ECO-084 是正中のスコープ外所見(Headless ハーネスのスレッドアフィニティ 2 態・R3 記録)

ECO-084 の probe/受入で実測した ECO-082/083 ファミリーの新知見 2 件(製品欠陥ではなくテストハーネス知見。
本 ECO ではテスト側の書法で決定化して回避済み・恒久テスト様式として CP-DISPMODE-084 fixture_note に記載):

1. **クラス単独実行(--filter-class)で Headless プラットフォーム初期化前に VM が Dispatcher.UIThread へ
   触れると、共有セッションの初回 EnsureSharedApplication が VerifyAccess で死ぬ**(ECO-083 の FailFast
   監視が正しく exit=7 で顕在化=監視の実運用 2 例目)。フル run では他クラスが先にセッションを温めるため
   潜伏する。回避=fixture 冒頭で `HeadlessApp.Session.Dispatch(() => true)` の先行 warm-up(順序の決定化)。
   **→ 同日更新(GF-084-01 是正後)**: テスト集合の増加(CpUi084 の 9 本)で並列スケジュールが変わり、
   フル run でも 3/3 **定常発火**へ転化(warm-up はクラス単位の対症でしかなかった)。恒久対処=
   HeadlessApp へ **xunit v3 AssemblyFixture(SessionInitFixture)** を追加し、どのテストよりも先に
   初期化 Dispatch を同期完了(順序の構造的決定化)。適用後フル run×6 連続 675/675 全緑。
   **罠**: 初案の `[ModuleInitializer]` 内同期待ちは不可 — dispatch コールバック(本モジュールの
   ラムダ)の実行がモジュール初期化完了を要求し、ローダーと相互待ちでデッドロック
   (実測: 起動前ハング→HangDump 発火=ECO-081 最終安全弁の実運用)。(結果は ECO-084 §8)
2. **worker スレッドで生成した SolidColorBrush(タグ色チップ)を compositor が参照すると VerifyAccess 死**
   (決定的・フル run でも再現)。タグ色チップ付き ImageTabView を headless 描画する初のクラスで顕在化 —
   既存描画テストはタグなし fixture のため潜伏していた。回避=描画するテストは VM 構築(Recompute=Brush
   生成)ごと `Session.Dispatch(async)` 内で行う。処置候補=headless 描画テストの共通書法として
   TestImageTab へ UI スレッド構築ヘルパを一般化(将来 ECO 候補・maintainer 判断)。

## 2026-07-15 — CAD 旧記述の是正で陳腐化した src コメント(ECO-008 積み残しの派生・未起票)

ECO-008(タグ種別は作成後変更不可)が「訂正対象(モック側のみ)」に挙げたまま積み残していた CAD の旧記述
2 箇所を是正した(ViewPrismUI 側 doc/mock のみ・VP2 の src/tests は不変):

- `docs/screens/tag_tab.md` タグモデル共通属性「種別: 必須。作成後も変更可能。」→ ECO-008 裁定へ一致。
- モック `資料/タグタブ/ViewPrism2 タグ作成ダイアログ.dc.html` シンプル型説明の「あとから…変更もできます」
  → **削除**(矛盾する主張の撤去に留め、「変更不可」を明示する新規 copy は発明しない= モック copy は
  maintainer 権威。作成ダイアログでの積極明示の要否は review_points **TAG-012** として未確定登録)。

**派生の未処置(本記録の本体)**: 上記でモック側の矛盾が消えたため、ECO-087 で記録した**意図的差分は解消**
(実装=一文なし・原器=一文なし で一致)。しかし
[TagEditorWindow.axaml:306](../src/ViewPrism2.App/Views/TagEditorWindow.axaml#L306) のコメントが
「ECO-008 により転写しない=意図的差分」のままで、**差分解消後の状態を指していない**(記述としては半分陳腐化)。
コメントのみの是正であっても **起票なき src 変更は禁止**(change-management)のため本セッションでは触らない。
処置=**次に TagEditorWindow.axaml を触る ECO で、そのスコープ内でコメントを現状へ同期する**(視覚・挙動への
影響なし・実害は診断ノイズのみ)。

**教訓の芽(N=1)**: CAD 側の旧記述を是正すると、それを引用していた実装側コメント(意図的差分の根拠)が
連鎖して陳腐化する — 「意図的差分」を宣言したコメントは**その差分の解消時に更新が要る参照**であり、
上流是正の read-across 対象に含まれる(2 例目が出たら BomDD 昇格判定)。

## 2026-07-15 ECO-091 中の所見(R3 記録)

- **i18n JSON の重複キーを lint が検出しない**: en.json に `tag.editor.basicInfo` が 2 回
  (値違い: "Basic Information"(旧)と "Basic info"(後勝ち=有効))存在していた。JSON パーサは
  後勝ちで沈黙するため、キー重複は「翻訳を直したつもりが旧行が残る」ドリフトの温床。
  ECO-091 のキー追加時の正規化で自然解消(有効値不変=挙動不変)。将来候補: i18n lint
  (ECO-079/080 系)へ「重複キー検出」を追加する軽量 ECO。

## 2026-07-15 GF-091-01 の教訓の芽(N=1)

- **Stretch されたコンテンツは「中心一致」の layout probe を素通りする**: Button 既定の
  VerticalContentAlignment=Stretch ではラベル TextBlock がボタン内容域いっぱいに伸び、Bounds 中心は
  ボタン中心と一致する(検査緑)がグリフはボックス上端に描画される(実機で上寄り)。垂直整列 probe は
  中心比較に加えて**ボックスが内容サイズであること(非 Stretch)**を検査する。2 例目で BomDD 昇格判定。

## 2026-07-15 GF-092-02 の教訓の芽(N=1)

- **実測フィードバック型レイアウトは「自身の出力が入力を変える」経路を列挙してから閾値を設計する**:
  折畳み(出力)がカード高さ→スクロールバー出没→実測幅(入力)を ±16px 級で離散変化させ、
  0.5px 閾値のリセットと組み合わさって無限発振(en 切替でのみ顕在化=言語依存の境界条件)。
  対処=バー幅より大きいヒステリシス(24px・ECO-027 WrapHysteresis と同様式)。headless 機械受入は
  スクロールバー出没を再現しないため捕まらない — 実機 golden が検出面。2 例目で BomDD 昇格判定。

## 2026-07-15 ECO-095 起票中の所見(R3 記録)

- **未使用 i18n キーを lint が検出しない**: `image.feature.defaultWorkspace`(ja=「デフォルト」/
  en="Default")が ja/en 両 JSON に定義済みだが**全ソース未使用**(grep 実測=消費者ゼロ)。
  未使用キーは「翻訳した=対応済み」という錯覚を生み、実際の表示経路(ECO-095 では DB 焼き込み)の
  漏れをマスクする。ECO-091 の重複キー所見と同族= i18n lint の検査次元(重複・未使用)拡充の
  将来 ECO 候補に合流。

## 2026-07-16 ECO-099 是正中の所見(R3 記録)

- **タグタブ hierarchy.* の既存デッドキー**: `hierarchy.addRootNamed` / `hierarchy.addChildNamed` /
  `hierarchy.clearHome` / `hierarchy.editAlias` / `hierarchy.editCondition` / `hierarchy.setHome` が
  ja/en 両 JSON に定義済みだが ECO-099 着手前から全ソース未使用(grep 実測=消費者ゼロ。GF-04 撤回
  ・ECO-007 改訂等の残骸と推定)。ECO-099 では自分が無効化したキー(placeTagRoot/placeTagChild/
  selectTagToAdd/dragHint/helpNest)のみ削除し、既存デッドキーは触らない(R3)。ECO-095 所見
  「未使用キーを i18n lint が検出しない」と同族=同じ将来 ECO 候補に合流(これで N=2)。

## 2026-07-17 ECO-103 GF 是正中の所見(R3 記録)

- **【昇格判定 DUE・N=2】Stretch 上寄りの再発= GF-103-01**: 保存バーの破棄/保存ボタン
  (固定高 34+Padding 縦 0・VerticalContentAlignment 未指定)で文言上寄り。GF-091-01 の
  教訓の芽(N=1)と同一機構の 2 例目 — 台帳自身の判定基準により **BomDD 昇格判定に到達**。
  発生系譜= ECO-040(規約成立)→ GF-091-01 → GF-103-01(規約が新設スタイルに伝搬しない構造)。
  probe 技法の確定= グリフ実高(TextLayout.Height)基準の中心比較(Bounds 中心比較は Stretch を
  素通り=GF-091-01 の予言どおり)。昇格素材= (a) playbook「固定高コントロールの新設スタイルは
  VerticalContentAlignment 明示を既定作法とする」(b) 検査器候補「axaml 静的 lint: Height 指定+
  Padding 縦 0 の Button スタイルで VerticalContentAlignment 欠落を検出」— accept 時の教訓へ引継ぎ。
- **CpUiG1CollectionScopeTests の間欠不合格**: 「ECO064_起動はcatalogと選択contentのloading状態を
  別々に公開する」が GF-103 のプローブ実行中に 1/3 run で不合格(Assert.True・47ms・タイミング系の
  疑い)。ECO-103 の diff と無関係(当該 run の変更はテスト追加のみ)。検査器ライフサイクル系の
  一括起票候補(E19/E15=backup-restore メモ)と合流させるか、単独の flaky 起票かは次回再現時に判定。

## 2026-07-17 ECO-106 起票中の所見(R3 記録)

- **i18n lint 候補の第 3 次元=「解決タイミング」**: `_localization.T`/`ErrorMessages.Resolve` の
  結果を ObservableProperty へ代入するイディオムが約 15 VM・50 箇所超に存在(全数 grep)。脆弱クラスは
  「メインタブ面に常駐し次のユーザー操作まで再解決機会がないメッセージ」(実機観測= ECO-106 の
  パレット削除拒否面)。モーダル内一時表示・状態更新のたび再計算されるラベルは実害が薄い。
  既存 lint 候補(重複キー N=1・未使用キー N=2)に**解決タイミング検出**を加えた 3 次元で
  i18n lint 拡充 ECO を起こすのが妥当 — 静的キー検査では映らない死角が実機で 2 面
  (ECO-104 保存バー+ECO-106 パレット)連続顕在化した実績。

## 2026-07-17 ECO-107 是正中の所見(R3 記録)

- **CpUiG6SaveBarTests「保存バーとチップは…」の間欠不合格(新種・1/2 run)**: headless Dispatch 内の
  `TestContext.Current.CancellationToken` 参照が ObjectDisposedException(テスト完了処理と
  dispatch 残タスクの競合疑い・lint テスト追加による並列負荷変動で顕在化)。ECO064 間欠
  (CpUiG1CollectionScopeTests)とはテストも例外も別だが「タイミング系 flaky」ファミリとして合流。
  検査器ライフサイクル系の一括起票候補へ 3 例目。dispatch 内から TestContext を参照しない様式
  (トークンを事前キャプチャ)が是正方向の仮説。

## 2026-07-19 ECO-112 R8 セルフレビュー中の所見(R3 記録)

- **⋯メニュー幅の面間非対称(WorkTab=200 / ImageTab=208)**: ECO-112 で画像タブの ⋯メニュー幅を
  VC-IMG-11①(MENU-fileops 原器実測)へ追随し 200→208 にしたが、作業タブの同型メニュー
  (WorkTabView.axaml:761 付近)は 200 のまま。作業タブ側 CAD(work_tab.md)に幅改版はなく
  現状は適法だが、「片面是正は面間非対称の潜伏を作る」(playbook §8.2 昇格済み・N=2 実績)の型。
  次回 work_tab 面の CAD 改版時に幅の read-across 突合を行うこと。既存テストに 200 固定はなし=退行なし。

## 2026-07-19 ECO-113 R8 レビュー中の所見(R3 記録)

- **CpUiG6SaveBarTests「保存バーとチップは…」間欠不合格の発現追加(4 例目)**: R8 独立レビュー
  subagent のフルスイート実行で 1/1 発現(TestContext ObjectDisposedException・CpUiG6SaveBarTests.cs:244)。
  本セッションの直後の再実行では緑。既知の「タイミング系 flaky」ファミリ(ECO-107 是正中 3 例目の記帳
  と同種=dispatch 内 TestContext 参照)。検査器ライフサイクル系の一括起票候補への N 蓄積。

## 2026-07-19 ECO-114 是正中の所見(R3 記録)

- **タグ編集パネル操作系の全面 Recompute 残余(同族・症状未観測)**: TabCurrent/TabAdd/ClickAddRow
  (展開)/ApplySequential 後の再読込等、タグ編集パネル内の操作が全面 Recompute(母集合再評価+
  Items 全再構築)を通る。ECO-114 のモード遷移と同族の「母集合不変なのに母集合パイプライン」型だが、
  操作面が異なり症状報告もないため本 ECO に混ぜない。26 万件ビューでタグ付け作業(パネルのタブ切替・
  行展開)が遅い所見が出たら分離起票(是正型は ECO-114 の ApplyModeTransition/部分再構築の再利用)。

## 2026-07-20 ECO-116 R8 セルフレビュー中の所見(R3 記録)

- **ScrollViewer.Padding 禁止則が「文書化済みなのに未強制」で 4 サイト残存**: 本欠陥クラスは
  既に 2 箇所で注意書き済み(`ViewerWindow.axaml:317-318`「GF-TAGCTRL 実測」・
  `ViewEditDialog.axaml:86-87`「ECO-013/GF-TAGCTRL-01 教訓」— いずれも remedy は
  Padding→内容 Margin で ECO-116 と同一)。にもかかわらず知識に強制手段がなく、
  タグ編集パネル 4 サイトが規約違反で書かれた(ECO-116 で是正)。**残存 4 サイト**:
  `ImageTabView.axaml:1460`(ゴミ箱 `Padding="18"`)・`WorkTabView.axaml:1122`(同)・
  `TagsTabView.axaml:636`(`"12,0,12,8"`)・`TagsTabView.axaml:861`(`"14,0,14,8"`)。
  **ECO-116 の実測法則= 到達不能量は `Padding.Top`**(本パネルで `16`→16px・`14`→14px を実測)。
  これに従えばゴミ箱 2 面は 18px 到達不能で**実害あり**、TagsTabView 2 面は Top=0 のため
  **非該当の見込み(要実測)**。分離起票の推奨スコープ= ゴミ箱 2 面の是正 +
  **grep レベルの lint(`ScrollViewer` に `Padding` を書かせない)**。lint は ECO-107 の
  i18n lint 5 本と同じ様式で書ける。教訓の再発防止としては是正より lint の価値が高い
  (「注意書きは掃射手段を紐づけないと守られない」= ECO-107 教訓1 の再実証・N=2)。
- **オーバーレイ・スクロールバーの右インセットが面ごとに不揃い**: `ViewerWindow` は右 28、
  `ViewEditDialog` は右 14 をスクロールバー避けとして内容側に確保しているが、タグ編集パネルは
  左右とも 16 でスクロールバーが内容に重なる。ECO-116 は Padding 16 → Margin 16 で
  **量を変えていない**ため退行ではない(視覚不変を probe で pin 済み)。CAD 側に
  右インセットの規定がないため現状は適法。共通言語として規定するかは別途。

## 2026-07-20 ECO-117 R8 による訂正(上記 ECO-116 記帳の時系列誤り)

- 上記「タグ編集パネル 4 サイトが規約違反で書かれた」は**注意書きの後に書かれた**と読める
  記述だが、git 遡りで**時系列誤り**と判明: 違反 8 サイトは全数が注意書き(2026-07-01/02)より
  **前**(2026-06-16〜29)に製造済み。正しくは「注意書き追加時に既存サイトへの遡及掃射が
  行われず、先行違反が潜伏し続けた」。教訓の正形=**法則発見時は遡及掃射(read-across)+
  機械検査(lint)まで込みで初めて規約になる**(ECO-107 教訓1×ECO-003 readacross_lesson の合流)。
  lint の必要性という結論自体は不変(ECO-117 で実施)。

## 2026-07-20 ECO-123 R8 独立レビューのスコープ外観測(記帳のみ・別起票候補)

- **列ピッカー(表示列ポップオーバー)が部品表未登録**: `ImageTabView.axaml:836`(幅 304)は
  `popupMenu` クローム流用だが、CMP-006 のインスタンス契約列挙(表示軸/⋯/並び替え/移動先/
  chip overflow)に不在。CMP-006 インスタンス追加か Candidate 記帳の対象候補(部品表は CAD リポ
  成果物= VP2 側で編集しない。次回 CAD 側接触時に申し送り)。
- **chip overflow ポップオーバーの実装写像が未検証**: 部品表は CMP-006 インスタンスに列挙するが、
  実装は `chipPopCard` クラス(LabeledChipStrip.axaml:64)で `popupMenu` クロームを使っていない。
  クローム契約(padding 6・radius.menu 等)との写像一致は未検証= ECO-122 の lint first-run で
  顕在化する見込み(その時に契約適合か バリアント記帳かを判定)。

## 2026-07-20 ECO-122 R8 独立レビューのスコープ外観測(記帳のみ・CAD 申し送り/改良候補)

- **CMP-004 セグメント契約と並び替えメニュー実装の抵触(CAD 申し送り)**: 部品表 CMP-004
  SegmentedControl(Standard)は適用面に「並び替えメニュー(昇順/降順=VC-FL-1⑤)」を明記し
  契約=「地 color.surface.segment `#f1f3f7`・アクティブ=白地+青文字+薄影」。だが golden 承認済み
  実装(VC-FL-1⑤)は 地 `#F4F6FA`/アクティブ `#EAF1FE` で両方不一致= 権威規則 2/3 により
  「MOCK 裁定/作画ゆれ/バリアント起案」の三択報告対象。次回 CAD 側接触時に申し送り。
- **並び替えメニューの Padding/BoxShadow 実値乖離が As-Built 乖離リスト不在(CAD 申し送り)**:
  REG-C3 裁定が確定したのは radius 13 のみ。実装(ImageTabView:672/WorkTabView:610)は
  `Padding="0"`(契約 6)と `BoxShadow="0 22 50 -16 #5C142040"`(契約 shadow.popover=
  `0 18 44 -14 …#57`)も上書きしており部品表の As-Built 乖離リスト(①③幅/②行高)に未記帳。
  ECO-122 lint は裁定済み radius の随伴として allowlist 許容(根拠コメントで区別済み)。
- **部品適合 lint の allowlist はキー存在のみで件数を見ない(改良候補)**: 同一ファイルに同一キーの
  違反が増えても素通し(現状 0 件・ECO-107 系既存様式と同水準)。悉皆の厳密化が要る時に件数 pin へ。

## 2026-07-21 ECO-124 R8 独立レビューのスコープ外観測(記帳のみ)

- **左ペイン開閉のスクロールアンカー非被覆(ECO-110 拡張裁定候補の顕在化)**: ECO-110 の
  グリッドアンカー(先頭可視維持)は ShowRightPane エッジのみ監視(ImageTabView.axaml.cs:86-89)。
  左ペイン開閉は非被覆で、ECO-124 是正後は「オフセット残存×段組変化=無アンカードリフト」になる
  (旧実装は再構築で先頭リセット)。ECO-110 サブ裁定 a が「左への一般化は効果確認後の拡張裁定」と
  予約済み= 本件はその判断材料。tracked-index 機構は Collapsed エッジ監視 1 条件で転用可能な設計
  (同 78-81 行注釈)。ECO-124 golden で挙動差分として明示宣言し、裁定は golden 時の maintainer 判断へ。
- **ImageTabSeedViewModel.cs:506 の ToggleSidebar は Recompute のまま**: 撮影ハーネス用シード VM
  (小規模データ・性能契約の適用外)だが E-BOM 契約文と字面が食い違う同型残存。次にシード VM へ
  触れる際に対称化(read-across 閉包規律との整合)。
- **ECO-124 probe は通知契約を pin しない**: 新 probe は Collapsed/SidebarWidth を直接読むため
  OnPropertyChanged 脱落は検出外(構造契約=インスタンス同一性が目的・バインディング更新は
  視覚系検査の守備範囲)。記録のみ。

## 2026-07-21 ECO-125 結合点棚卸しの C 分類(条件付き正当)予告記帳+観測

- **OpenRepair/OpenBackupSettings のモーダル閉じ後 無条件全再読(C 分類・分離起票候補)**:
  ImageTabViewModel の両サイトはモーダル閉じ後に変化の有無を問わず ReloadImagesAsync+Recompute
  (修復の missing→deleted/設定内取り込みが表示中コレクションを変えうるため=意味論上は条件付きで
  正当)。26万件では「修復/設定を開いて閉じるだけで 1〜2 秒」= ECO-124 同族の症状候補。
  是正型= 変化検出(モーダル戻り値 or リビジョン比較)導入=規模大。**症状観測で分離起票**
  (ECO-114 R8→115 の予告記帳様式。経路名+是正型まで記帳済み=診断の再作業ゼロ)。
- **スキャン部分更新経路が ClearCopyFeedback を呼ばない(列挙縮小の疑い・軽微)**: ECO-112 正本の
  解除列挙は「タイマ/モード離脱(ExitFileOps)/選択・マーカー変化(RefreshSelectionMarkers)/
  母集合・文脈再計算(Recompute 先頭)」=機構ベース(ECO-125 R8 所見4 で引用を正本どおりに訂正)。
  部分更新経路 2 つ= OnScanUpdated **BatchCommitted**(段階公開=母集合変化)と **Started 他コレクション
  分岐**(ECO-125 B-3 是正後)は Recompute を通らず解除もない=機構列挙からの縮小。実害=タイマ 2 秒が
  後詰め(ECO-106 の脆弱クラス非該当)・スキャン中に fileops+コピー直後の交差は稀。列挙の意図
  (ユーザー操作起点のみか)の確認込みで、次回 fileops 系 ECO で判定。

- **Organize 子 VM の注入 _recompute 経由 4 サイト(ECO-125 R8 所見6 の実測)**: マージ実行
  (ImageTabOrganizeViewModel.cs:337)/Undo 成功(:358)=データ変化 A。**RunSearchAsync 成功末尾(:299)=
  検索結果格納のみ=母集合不変の全面 Recompute(C 相当)**: 26万件整理モードで検索完了のたび全再構築
  =症状候補。是正型= 結果パネルのみ更新(BuildContextPanels+通知 or 専用部分更新)。症状観測で
  分離起票(予告記帳様式)。Undo 失敗(:353)= _canUndo/_undoNote のみ=軽微・同梱判定。
- **Organize 子 VM の注入 _refreshSelectionMarkers が死亡配線**(同:29/:76・製造時から一度も呼ばれ
  ない): 次回 organize 系 ECO で除去または行使先の確認(read-across 閉包)。

## 2026-07-21 ECO-126 R8 独立レビューのスコープ外観測(記帳のみ・CAD 申し送り/分離起票候補)

- **ConfirmDialog に Escape/Enter のキーボード契約がない(既存欠陥の温存)**: IsCancel/IsDefault 不在=
  Escape でキャンセルできない(旧実装から不変=退行ではない)。CMP-011 にキーボード契約が未定義の
  ため CAD 側改訂(キーボード契約の追加)と対で扱う。分離起票候補。
- **ECO-073 golden 面(outlineButton)も hover でテーマ既定グレーへ退色する同欠陥**:
  CollectionExport/ImportWindow のローカル outlineButton+インライン Accent CTA は :pointerover
  template 上書きなし= hover で契約色が破れる(golden は静的撮影のため hover は非実測次元=
  ECO-123 教訓の同族)。dlgBtn 系への置換(lazy 遡及)時に同時解消。
- **dlgBtn の hover/pressed 値は CMP-011 未規定= 実装の暫定選択**: secondary hover #FAFBFC(発明値・
  無害)・primary/destructive= 契約色維持を優先。CAD 側で hover 契約を正典化する際に突合。
