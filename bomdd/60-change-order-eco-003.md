# Change Order — ECO-003(再リンク候補カードの表示パリティ)

> クローズ済み Loop V4 への**後発の仕様欠陥**の再入口(playbook §8)。本 ECO は ECO-001(doc-only)と異なり、**仕様・E-BOM・M-BOM・Control Plan の実欠陥**であり、是正は **遡及 BOM 再構築(コード保持)**(maintainer 選択 Option B)で行う。
> 重要: 製造物(コード)は工場隔離エージェントでなく**設計者が直接記述したものを保持**する。これは工場隔離規律からの**逸脱**であり、本書 §4 に明示記録する。

## 0. 変更前 baseline の凍結
- As-Built 個体: Loop V4 / commit `e08887b`(BOM v4.0、固定オラクル `tag:loop-v4-r1`)
- データ fixture: **N/A**(永続データ・移行を伴わない=表示のみの変更)
- 既存固定オラクル: S-01〜S-30+S-19b(`tag:loop-v4-r1`)。**本 ECO で不変**(表示パリティは固定オラクル=論理では捕捉できない次元のため、横断契約に追加行は無い)

## 1. 変更要求
- ECO-ID: **ECO-003**
- 変更内容:
  再リンク候補の表示が**相対パス+サイズのみ**で、原典 `AdvancedRepairModal.tsx` の候補カードが提示する
  **サムネイル+ファイル名+パス+サイズ+更新日時**のうちサムネイル・ファイル名・更新時刻を欠き、ユーザーが
  正しい置換先かを判断できない。再リンクはタグ/関連を別画像へ付け替える操作のため、誤判断のコストが高い。
- 種別: **仕様欠陥(表示契約の脱漏)** — ECO-001(プローズ忠実度)と違い、要件そのものに表示契約が無かった
- REQ への反映: **REQ-072 を改訂**(項目 (d) 表示契約+counterexample 追加)。§2.11.5 に表示パリティ契約を明文化

### 発見経緯
- golden G-10 クローズ後、maintainer が原典 view-prism と ViewPrism2 を**直接比較**し候補カードの情報不足を指摘。
- maintainer からの工程上の問い:「原典調査をしているのに、なぜ見落とされたのか」「仕様→E-BOM→M-BOM→製造 の工程で是正されるべきで、今回それが実行されていない」。
- **ポーティング資料作成者へのヒアリング**: ミスを認め、ポーティング資料(`docs/porting-spec/`)を改善済み(一次入力=リバース資料の是正は完了)。

### 根本原因(欠陥は仕様で生まれ BOM 連鎖を素通り)
| 段 | 成果物 | 欠陥 |
|---|---|---|
| **仕様(一次)** | REQ-072 | counterexamples が完全削除/復元 missing 化/確認なし実行の3点のみ。**候補カードの表示情報を要件化せず** |
| E-BOM | E-UI-REPAIR-039 | `depends_on` に E-THUMB-020(サムネイル部品)を宣言済みなのに、**「候補はサムネイル+…を提示」という表示 invariant が無い** |
| M-BOM | M-UI-REPAIR-027 | `relink_ui` が「候補を提示→選択→確定」のみ。**提示フィールド/サムネイルの製造指示が無い** |
| 工程管理 | CP-UI-G10 | golden characteristic に**「候補カードの表示パリティ」項目が無い**(→ golden 2 回が見逃せた) |

**構造的真因**: 表示パリティは**固定オラクル(論理検証)では捕捉不能**(ヘッドレス工場は視覚を見られない)→ 100% golden 依存。だが golden 過去 2 回は*挙動*(prefill/auto-count/検索統一)に集中し、候補カードの*情報密度*を原典とフィールド単位で突合する観点が無かった。= ported 画面の**表示契約(presentation manifest)が要件に存在しなかった**。

### 設計者(私)の工程誤り(本 ECO はこれ自体の是正でもある)
GF-V4-04 を当初**製造優先で直接コード修正+§2.11.5 後付け**で処理し、REQ-072/E-039/M-027/CP-UI-G10/routing を飛ばした(流れの反転)。さらに表面成果物を**設計者が直接ハンドコード=工場隔離も侵害**。GF-V4-01/02/03 の「設計者直接修正」前例に引きずられたが、あれらは既存 BOM 範囲内の挙動調整(K-DESIGN 裁量)で、GF-V4-04 は **BOM に無い新規表示要件**=連鎖が必要だった。この**所見トリアージの誤り**が真の工程欠陥(§6 lesson)。

## 2. 影響分析(トレース逆引き)
| 段 | 影響 ID | 是正 |
|---|---|---|
| 仕様節 | 20-spec.md §2.11.5 / REQ-072 | 表示パリティ契約を追記・(d) 項+counterexample |
| E-BOM 部品 | E-UI-REPAIR-039 | 表示 invariant 1 行追加・acceptance_refs に CP-REPAIR-CARD-021 |
| M-BOM unit | M-UI-REPAIR-027 | interface_contract に `candidate_card` 追加・FMEA-031 新設 |
| Control Plan 特性 | **CP-REPAIR-CARD-021(新設・unit)** / CP-UI-G10(表示パリティ突合を追加) | VM の表示フィールド公開を unit で固定+golden に視覚突合項目 |
| 固定オラクル行 | — | **変更なし**(表示は横断契約=論理では捕捉不能。golden+unit で担保) |
| K-BOM / 調達 | — | 影響なし(既存 K-AVALONIA/K-MVVM/K-DESIGN・E-THUMB-020 で充足) |
| 製造物(code/test) | RepairWindow.axaml / RepairViewModel.cs / RelinkViewModel.cs(共有 VM)/ WindowService.cs / CpUiRepairViewModelTests.cs | 候補カード5要素化・絶対パス解決・VM フィールド公開・GF_V4_04 unit 追加 |

## 3. BOM 改訂
- bom_rev: **v4.0 → v4.0(eco:ECO-003)** — 固定オラクル凍結 tag `loop-v4-r1` は不変。仕様/E/M/CP の改訂+表面製造物の改修
- 変更分の受入(オラクル・ファースト): 新規 **CP-REPAIR-CARD-021**(unit・GF_V4_04 ベクタ)。視覚パリティは CP-UI-G10(golden)へ項目追加
- 治具の凍結条件: 横断固定オラクル不変(`loop-v4-r1`)。新規 unit は M-HARNESS-015 配下

## 4. 部分再製造(Option B = 遡及 BOM 再構築・コード保持)
- **採用方針**: maintainer 選択により、検証済みコードを**「製造済み成果物」として保持**し、再構築した BOM の受入ゲートに通す。
- **工程逸脱の明示記録(重要)**: 当該コードは**工場隔離エージェントの製造物ではなく設計者が直接記述**したもの。したがって本成果物は工場由来のトレーサビリティ(work-order→隔離製造→cheat-log)を持たない。隔離純度を厳密に保つ場合、次回同種の表示欠陥は**隔離工場で再製造**する(本書を前例とする)。
- 再利用 unit: 既存 V4 製造物すべて(意味論不変)。候補カードは M-THUMB-008/E-THUMB-020 の既存サムネイル経路を流用(SimilarSearch/Trash と同一)。
- 工場投入: **本 ECO では行わない**(Option B)。

## 5. 回帰+変更受入(失敗5分類で帰属)
- 自己受入: `dotnet build`(Debug)compile-clean / **Tests 381**(既存 379+GF_V4_04 unit 2)/ **Oracle 73 PASS+2 skip**(S-01〜S-30 回帰ゼロ)。
  - Release は起動中アプリが DLL をロックするため未実行だが、コンパイル自体はコピー段まで到達=compile-clean を確認(コード問題ではない)。
- 結果: `regression`=なし(Oracle/既存 unit 不変)。`change miss`=なし。`data-preservation miss`=N/A(永続変更なし)。`unnecessary modification`=なし(表示のみ)。
- 残: **golden 再ウォークスルー**(候補カードにサムネイル+5要素が出るか・原典とフィールド突合)で CP-UI-G10 を再承認。

## 6. 記録 + lesson
- As-Built(50): `golden_findings_v4.GF-V4-04` を ECO-003 として整理・`golden_rounds:3`
- cheat-log(51): GF-V4-04 根本原因 + **所見トリアージの lesson**
- metrics(52): `presentation_parity_lesson` + 所見トリアージ
- routing(34): `routing_v4.eco_003`

### lesson(再発防止・次ループ要件分解テンプレへ)
1. **表示契約を要件に含める**: ported 画面の要件には「論理契約」だけでなく「**表示契約(提示フィールド+視覚要素)**」を必ず含め、原典スクリーンと**フィールド単位**で golden 突合する。固定オラクルは視覚を検証できない以上、表示契約は golden チェックリスト項目+VM フィールド公開の unit で二重化する。
2. **所見トリアージ(本 ECO の中核教訓)**: golden/後発の所見は是正前に必ず分類する —
   - (a) **既存 BOM 範囲内の表面調整**(K-DESIGN 裁量)→ 設計者直接修正可(例: GF-V4-01/02/03)
   - (b) **既存仕様/BOM への defect**(規定はあるが実装/記述が違う)→ 当該段を是正し再受入
   - (c) **仕様/BOM の gap(要件そのものが無い)**→ **仕様→E-BOM→M-BOM→Control Plan→製造**の連鎖を必ず流す(例: GF-V4-04=ECO)
   GF-V4-04 を (a) と誤分類して直接修正したのが工程欠陥。(c) を (a) として処理しないことを規律化する。
