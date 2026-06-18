# Change Order — ECO-014(画像タブ 機能完成①: 整理モード = 類似+マージ統合「整理トレイ」・形式化)

> **機能完成 ECO の第1弾**。原典撤去(ECO-013)の真の前提は「新 ImageTabView が原典 surface の周辺機能を等価に引き受けること」。そのうち **類似検索(REQ-065)+ マージ(REQ-067)を『整理』ボタンの整理トレイへ統合**する設計を spec-first(CAD→UI-IR→E-BOM)で形式化する。
> **帰属: design_decision(maintainer 裁定 2026-06-18)**。原典の独立モーダル(SimilarImageSearchModal / ImageMergeModal)を、画像タブ右ペインの**インペイン整理トレイ**へ**作り直す**(ECO-010 が NodeGraph/TagAssign を原典から作り直したのと同型)。本 ECO は **起票 + CAD 参照(ViewPrismUI 反映済)+ E-BOM(30)同期 + UI-IR(UQ-I07 部分解決)** 段階。実装は製造で適用。spec(20)/M-BOM(32)/Control Plan(33) の全面同期は後続 **M4**。
> **スコープ: 整理(類似+マージ)のみ**。トラッシュ完全削除/復元・修復(REQ-072)・詳細パネル/ノート編集、および **IMG-011**(マージ実行の削除・復元・タグ統合の永続化)・実ファイル操作、**作業/⋯ ボタン**(UQ-I07 残)は **本 ECO 対象外(後続 ECO へ繰り越し)**。§6 に列挙。

## 0. 変更前 baseline
- As-Built: commit `c816236`(ECO-013 §7 記録訂正後)。固定オラクル `tag:loop-v4-r1`(S-01〜S-31)不変。
- 本 ECO は **表面の知識更新(設計決定)**。スキーマ・固定オラクル・**Core 意味論(E-SIMSEARCH-032 / E-CRITERIA-037 / E-MERGE-034)不変**。
- **形式化のみ**(ドキュメント + E-BOM YAML)。**コード変更なし** → build/test は不変(検証は読み合わせ整合・§5)。

## 1. 変更要求
- ECO-ID: **ECO-014**
- 発生契機: ECO-013 の原典撤去ブロッカー精査で、新 ImageTabView に **類似/マージ/トラッシュ/修復/詳細** が未配線と判明(繰り越し)。このうち **類似+マージ** を「整理」ボタンへ統合する CAD を ViewPrismUI で設計完了。
- 内容: 整理トレイ設計を CAD 形式化し E-BOM を同期。UQ-I07 を **部分解決**(整理=決定 / 作業・⋯=残)。
- 種別: **設計決定(surface 作り直し)**。

## 2. 設計の核心 — 原典モーダル → インペイン整理トレイ
一次資料 `ViewPrismUI/資料/画像タブ/ViewPrism2 整理マージ (standalone).html` のメッセージ:**「2つの入口を、ひとつのトレイに。」**

| 項目 | 原典(現 E-BOM 記述) | 新 CAD(整理トレイ) |
|---|---|---|
| 起動 | 類似 = `SimilarImageSearchModal` / マージ = `ImageMergeModal`(別々のモーダル) | 画像タブ右ペインの**整理トレイ**1つ。「整理」ボタンで開始しタグ編集モードと排他(タグ編集と同じ「選択→右ペイン」の手触り) |
| 入口 | 類似検索とマージが別動線 | 「**1枚から探す**(類似)」「**複数枚を直接まとめる**(マージ)」を1トレイへ合流 |
| 検索 | 類似 = pHash 閾値モーダル | トレイ内「**似た画像を探す**」= ①類似画像検索(E-SIMSEARCH-032・pHash 閾値)②条件検索(**E-CRITERIA-037 を消費**・hash/拡張子/サイズ/名前/mtime) |
| 結果 | モーダル内一覧 | **中央ペイン全面**に候補+**一致率**(見比べ重視)・候補ごとに「整理対象に追加」 |
| マージ | `ImageMergeModal`(マージ先/元/プレビュー) | トレイの**マージ先**(残す1枚)/**整理対象**(統合し削除対象)/**タグ統合**(「マージ時にタグを含める」)/**マージを実行** |
| 完了 | モーダル閉じ | トレイで「統合しました」+結果(何枚を1枚へ/何枚削除)+「**取り消す**」「**別の整理を続ける**」 |

**Core は意味論不変**(再利用): E-SIMSEARCH-032(候補 = normal・同一コレクション REQ-053・閾値以上を類似度降順安定)/ E-CRITERIA-037(指定条件のみ AND・安定順)/ E-MERGE-034(タグ union・マージ先優先・source=deleted・原子・物理非破壊 INV-009)。**変わるのは surface(モーダル → インペイントレイ)のみ**。

## 3. disposition(maintainer 裁定 2026-06-18)
| ID | 決定 | 根拠 | 同期先 |
|---|---|---|---|
| **整理 = 整理トレイ統合** | 類似(REQ-065)+ マージ(REQ-067)を画像タブ右ペインの**整理トレイ**へ統合。原典の独立モーダルを作り直す。検索は条件検索(E-CRITERIA-037)+類似画像検索(E-SIMSEARCH-032)の2方式、結果は中央ペイン全面+一致率。マージはトレイのマージ先/整理対象/タグ統合/実行で完結 | [[mock-ui-ir-is-cad]]: モック = CAD = 正典。「2つの入口を、ひとつのトレイに」。タグ編集と同じ「選択→右ペイン」の一貫操作。検索結果を中央全面で見比べる情報設計は dedup に適う。Core を再利用し surface のみ作り直す = 「核は保全・表面は知識更新」の E-BOM 規律(ECO-010 と同型) | ViewPrismUI `docs/screens/image_tab.md`(整理モード節)/ `docs/review_points.md`(IMG-005)/ `docs/live_spec`(整理マージ + S06)/ **E-UI-SIMILARITY-035 / E-UI-MERGE-036 / E-UI-GRID-022 / E-CRITERIA-037** |

## 4. BOM 改訂
### CAD(ViewPrismUI)— maintainer 反映済(本 ECO は参照)
- `docs/screens/image_tab.md`: 「整理モード」節(整理トレイ・2入口・検索(条件/類似)・マージ実行・完了)+ インタラクション/状態表に整理行を追加。
- `docs/review_points.md`: **IMG-005**(整理ボタン)= 決定済へ更新。**IMG-010**(作業/⋯)・**IMG-011**(整理マージ実行の削除・復元・タグ統合ルール)を未設計として追加。
- `docs/live_spec/index.html`: 「整理マージ」モック + 代表シナリオ **S06 重複画像を整理する** を追加。

### E-BOM(`30-ebom.yaml`)— 本 ECO で同期
- **E-UI-SIMILARITY-035**: `(ECO-014)` 類似検索は独立モーダルでなく **整理トレイの find** として提供(②条件検索 = E-CRITERIA-037 消費を追加)。`depends_on` に **E-CRITERIA-037** 追加。
- **E-UI-MERGE-036**: `(ECO-014)` マージは独立モーダルでなく **整理トレイ**(マージ先/整理対象/タグ統合/実行/完了)。**IMG-011 永続化・実ファイル操作・トラッシュ/修復/詳細の整理 surface 配置は対象外**を明記。
- **E-UI-GRID-022**: `(ECO-014)` 右ペイン 2 文脈モード(タグ編集/整理)排他。整理モード = 中央ペイン「検索結果」+一致率、グリッドクリックはマージ先/整理対象割当へ振る舞い切替。
- **E-CRITERIA-037**: `graph_edges.consumers` に **E-UI-SIMILARITY-035**(整理トレイの条件検索 find)追加。
- 固定オラクル: **追加なし**(S-01〜S-31 不変)。**スキーマ不変・Core 意味論不変**。

### UI-IR(`bomdd/ui/image-tab/`)
- `unresolved-questions.md`: **UQ-I07 部分解決**(整理 = 決定 / 作業・⋯ = 残)+ 決定ログに ECO-014 追記。
- `design-system-bom.md`: 整理トレイの新部品(整理トレイパネル・マージ先/整理対象カード・検索フォーム・検索結果カード(一致率)・タグ統合トグル・完了状態)を台帳へ追加(製造前 DS ゲート)。

### 製造時 / M4 同期(後続)
- `20-spec.md` / `32-mbom.yaml` / `33-control-plan.yaml`: 整理トレイ surface の反映は **M4** で全面同期。

## 5. 受入(計画)
- 「整理」ボタン → 右ペインに整理トレイ。タグ編集モードと排他。「整理を終了」で通常閲覧へ戻る。
- 入口①「1枚から探す」= マージ先を起点に検索パネルを開いた状態 / 入口②「複数枚を直接まとめる」= マージ先+整理対象がトレイに入った状態。
- 「似た画像を探す」: **条件検索**(E-CRITERIA-037)・**類似画像検索**(E-SIMSEARCH-032・閾値スライダー)→ 中央ペインに候補+一致率。候補「整理対象に追加」でトレイ反映。
- **タグ統合**トグル(マージ時にタグを含める)→ E-MERGE-034 のタグ union 経路。
- 「マージを実行」(整理対象 ≥1 で有効)→ E-MERGE-034(タグ union・マージ先優先・source=deleted・物理非破壊 INV-009)。完了状態で結果と「取り消す」「別の整理を続ける」。
- Core 回帰: `dotnet test tests/ViewPrism2.Tests` + `tests/ViewPrism2.Oracle`(S-01〜S-31)退行ゼロ・build 警告0(**実装時**)。実機 golden(整理トレイ・2入口・検索・マージ・完了)。
- ※ **実行の永続化(取り消し保持範囲・タグ統合の永続化・削除手段)= IMG-011 は別 ECO**。本 ECO の受入は surface の挙動契約に限る。

## 6. スコープ外(後続 ECO へ繰り越し)— 原典完全撤去の残ブロッカー
| 項目 | 内容 | 紐付け |
|---|---|---|
| トラッシュ(復元/完全削除) | REQ-070/071 — E-TRASH-038 / E-UI-REPAIR-039 の最小トラッシュ以上の行き先 | UQ-I07(作業/⋯) |
| 修復ライフサイクル | REQ-072 — criteria/relink/トラッシュ統合(E-UI-REPAIR-039) | UQ-I07 |
| 詳細パネル / ノート編集 | REQ-043 — E-UI-DETAIL-023 の行き先(モックは「常時詳細パネル」を廃止) | — |
| マージ実行の永続化ルール | 削除手段(論理/ゴミ箱)・取り消し保持範囲・タグ統合の永続化単位・タグ衝突解決 | **IMG-011** |
| 作業 / ⋯ ボタン | TMP-UI-ACT-0062 / 0063 の挙動(トラッシュ/修復の入口候補) | UQ-I07 残 |

> これらが新 surface に行き先を得るまで、原典(`MainWindow.axaml` 画像タブ Grid・harness・legacy 画像タブ VM)は **撤去しない**(ECO-013 の裁定を継続)。本 ECO は撤去を進めず、整理 surface の設計確定のみ。

## 7. provenance / lesson
- 本 ECO は ECO-013 の原典撤去ブロッカー分析(類似/マージ/トラッシュ/修復/詳細が新 surface 未配線)を受け、**類似+マージを CAD 先行で機能完成へ進める第1弾**。
- read-across: 原典の独立モーダル(SimilarImageSearchModal / ImageMergeModal)を画像タブの「選択→右ペイン」一貫操作へ統合する判断は ECO-010(NodeGraph/TagAssign を原典から作り直し)と同型。**Core(E-SIMSEARCH-032 / E-CRITERIA-037 / E-MERGE-034)は意味論不変で再利用し surface のみ作り直す** = E-BOM 規律「核は requirement、表面は知識更新」どおり。
- lesson: 機能完成は「原典コマンドを新ボタンへ配線」ではなく「**CAD(整理トレイ)へ surface を作り直し、Core サービスを再利用**」。原典モーダルの単純流用は CAD(モック=正典)に反する。撤去前 golden(ECO-013 §7)で得た「新 surface = 原典の完全な機能等価か」の関門を、機能完成側でも surface ごとに CAD 起点で閉じていく。

## 8. golden 第1回(2026-06-18・maintainer 実機・harness ON)— 所見と是正
製造②(M1+M2 surface)後の実機 golden。機能は動作(マージ先・検索結果100%・条件入力・整理対象追加・マージ実行(1枚)有効)。視覚所見を是正。

| # | 所見 | 区分 | 是正 |
|---|---|---|---|
| 1 | 「類似画像/条件」セグメントの文字切れ | bug | grid/list 用の正方 `segBtn`(Width=38 固定)を流用していた → テキスト用 `segBtnText`(固定幅なし)を新設し差替え |
| 2 | 右ペインのカード/テキストが右へはみ出す | bug | **ECO-013 #5b と同型**(ScrollViewer 内で幅がビューポート非拘束)。整理トレイ2 ScrollViewer を Padding→Margin + `HorizontalScrollBarVisibility=Disabled` + 内側 StackPanel `Width={Binding $parent[ScrollViewer].Viewport.Width}` 束縛。カード/名前 TextBlock がペイン幅に収まり TextTrimming が効く |
| 3 | 右ペイン展開時に横長ツールバーが隣ペインへ描画はみ出し(中央列 約582px) | parity/layout | (a) 中央 DockPanel に `ClipToBounds`(左サイドバーと同じ封じ込め)で被り防止。(b) **design 裁定(maintainer): モード中は他モード入口(`タグ編集`⇄`整理`)・`作業`・`⋯` を非表示**にし、表示軸/ソート/レイアウトと当該モードの「終了」のみ残す(集中・排他可視化・狭い中央列でツールバーが収まる=約540px)。`作業`/`⋯` はモード中に操作対象がない(maintainer)。実装: `タグ編集`=`!OrganizeMode` / `整理`=`!EditMode` / `作業`・`⋯`=`!InAnyMode` |

- design 決定の追記(§3 disposition への補遺): **モード中のツールバーは文脈依存で項目を出し分ける**(モード入口の排他可視化 + browse 専用アクション=作業/⋯ を隠す)。モード直接切替(1クリック)は失うが「終了→入り直す」で代替・影響小。`作業`/`⋯` の本体挙動は引き続き **UQ-I07**(本 ECO スコープ外)。レスポンシブ overflow ツールバーは不採用(本質は狭窓制約・最大化/サイドバー折り畳みで解消)。
- 同期先: ViewPrismUI `docs/screens/image_tab.md`(ツールバー節へ「モード中の項目出し分け」を追記)/ `unresolved-questions.md` UQ-I07(作業/⋯=browse 専用・モード中非表示)。
- 検証: App build 0 警告 / Tests 416 / Oracle 74+2skip(S-01〜S-31 不変)。コミット列: surface=`795cde7` / GF #1#2=`9196ca1` / ClipToBounds=`13352b8` / モード文脈ツールバー=(本コミット)。
- lesson: ScrollViewer 内のカード幅は `Viewport.Width` 束縛が必須(`Disabled`+`Stretch` だけでは非拘束)= ECO-013 #5b の横展開。アイコン用 `segBtn` をテキストに流用しない。狭い中央列での横長ツールバーは、サイズを詰めるのでなく **文脈で項目を出し分ける(モード時は browse アクションを隠す)** のが自然解。
