# Change Order — ECO-051(staged): V3 旧 UI(SimilarSearchWindow/MergeDialog/TrashView)一式の撤去 — ECO-024 原典撤去の撤去漏れ残骸

> ECO-050 診断中の R3 所見(51-cheat-log 2026-07-06)から昇格起票。当初所見は「モーダル 1 つ」だったが、
> 診断で **WindowService 経由の V3 旧 UI 一式(3 Window+3 VM+IF 3 メンバ)が全て到達不能**と確定。

## 1. 所見(2026-07-06・ECO-050 診断中)

- 仕様適合の既定 70 を持つ唯一の実装=独立モーダル SimilarSearchWindow が、実は到達不能コードだった。
- 到達不能コードの実害: ①grep・診断のノイズ(ECO-050 で「三者三様」の 1 つがこの死者だった)
  ②将来の変更が死者にも波及してコストを生む(ECO-050 は literal 修正が 3 箇所に見えた)
  ③M-BOM(M-UI-SIMILARITY-023)が死んだ artifact を指したまま=台帳の as-built 乖離。

## 2. 工程診断 — 実装層の残骸(ECO-024 の撤去漏れ)+M-BOM の as-built 乖離

| 工程 | 判定 | 根拠 |
|---|---|---|
| CAD | 無関係 | ECO-014 裁定で類似検索・マージは整理トレイに確定(UQ-I07/IMG-005)。モーダルは CAD に存在しない |
| 仕様 | 健全 | REQ-065/067 の受入は整理トレイ(E-UI-SIMILARITY-035/E-UI-MERGE-036 の ECO-014 invariant)が充足 |
| BOM | **as-built 乖離** | M-UI-SIMILARITY-023 の artifact が死んだ 3 Window+3 VM を指したまま(32-mbom:425-441)。生きた実体は M-UI-ORGANIZE-034(ECO-036 切り出し)等に既に存在 |
| 実装 | **残骸(撤去漏れ)** | §3 — 呼び出し元の消滅は `8a34c43`(ECO-024 原典撤去)。legacy Grid が最後の呼び出し元で、WindowService 間接参照のため ECO-024 の撤去範囲(直接参照の legacy 5 ファイル+FolderPane/Tagging)から漏れた |

- 裁定不要(挙動不変・視覚不変の残骸撤去 = ECO-024 と同型)。**ECO-024 の maintainer 裁定
  「テスト移行を確認してから削除」を前例として踏襲**する(§4)。

## 3. 切り分け済みの事実(確定と未検証を分離)

確定(コード読解・履歴・grep 全数):

- **到達不能の連鎖(全数)**:
  1. `WindowService.ShowSimilarSearchAsync`(:266)— 呼び出し元ゼロ
  2. `WindowService.ShowMergeAsync`(:278)— 唯一の呼び出し元= SimilarSearchViewModel:188(死者自身)
  3. `WindowService.ShowTrashAsync`(:293)— 呼び出し元ゼロ
  → 死者一式: Views/SimilarSearchWindow.axaml(+.cs)・Views/MergeDialog.axaml(+.cs)・
    Views/TrashView.axaml(+.cs)・ViewModels/{SimilarSearch,Merge,Trash}ViewModel.cs・
    IWindowService の 3 メンバ+ WindowService の 3 実装。
- **生存者との区別**: 生きたトラッシュ表示は ImageTabTrashViewModel(インペイン ポップアップ・
  ImageTabViewModel:145 から構築)で別物。整理トレイのマージは MergeService 直呼び
  (ECO-044 配線)で MergeViewModel を経由しない。RepairViewModel の TrashViewModel 言及は
  コメント(パターン参照)のみ。
- **残骸化の履歴**: 導入= `48d1d01`(V3 Phase 4 製造)→ ECO-014 で新 ImageTabView は整理トレイへ
  置換(ただし legacy Grid は引き続きモーダルを呼んでいた)→ `8a34c43`(ECO-024 原典撤去)で
  最後の呼び出し元が消滅=**この時点から残骸**。間接参照(IWindowService 経由)のため
  ECO-024 の依存検証から漏れた。
- **テスト依存**: CpUiSimilarityViewModelTests.cs が SimilarSearchViewModel/MergeViewModel/
  TrashViewModel を unit 検査(死者の検査)。IWindowService の 3 メンバは Tests 内の
  stub 実装 15 箇所前後に波及(メンバ削除で機械的修正が必要 — Tests のみ・Oracle は非参照(grep 済み))。
- M-UI-SIMILARITY-023 の acceptance= CP-UI-G9(golden)+CP-L1-SMOKE — CP-UI-G9 の実体は
  整理トレイで承認済み(ECO-044/048 golden)。モーダル固有の golden 承認実績はない。

未検証(着手時に確認):

- 死者のテストが担っていた受入観点(結果整列・空状態・プレビュー算出・deleted 一覧)が
  生存側テスト(CpUiG1OrganizeTests・CpMerge044UndoTests・CpUiG1TrashPopupTests・CpSim017 系)で
  カバー済みか — **対応表を作り、不足があれば移行してから削除**(ECO-024 裁定の前例)。
- i18n キー(similar.*/merge.*/trash.*)のモーダル専用分の有無(生存 UI と共有の可能性 —
  削除は専用と証明できたもののみ・疑わしければ残置)。

## 4. 是正方針(案 — 裁定不要・ECO-024 前例踏襲)

1. **受入観点の対応表**: CpUiSimilarityViewModelTests の各 fact が担う観点を列挙し、
   生存側テストとの対応を確認(不足のみ生存側へ移行)。
2. **撤去**: 死者一式(3 Window+3 VM)+ IWindowService/WindowService の 3 メンバ+
   Tests の stub 3 メンバ(機械的)+ CpUiSimilarityViewModelTests(対応表確認後)。
3. **M4**: M-UI-SIMILARITY-023 を retire(撤去記録 — ECO-024 方式)。E-BOM は不変
   (E-UI-SIMILARITY-035/E-UI-MERGE-036 の実体は整理トレイで存続・ECO-014 invariant 記載済み)。
4. プローブ(R5)の扱い: 削除系につき「是正前不合格の回帰テスト」は非該当。代替の実測裏取り=
   **削除前に到達不能の grep 全数証拠(§3)+削除後の機械受入全緑**(ECO-024/036 と同じ規律)。
   加えて L1 スモーク(起動+主要画面)で視覚不変を機械側からも担保。

- golden 影響: なし(到達不能 UI の削除=視覚不変)。gate② は軽量確認(起動+画像タブ/作業タブ/
  ビューア一巡で異常なし)を提案 — 実施要否は maintainer 判断(機械受入+L1 で足りるとの判断も可)。

## 5. 影響 BOM

- impacted: M-UI-SIMILARITY-023(retire)・M-UI-013(IWindowService/WindowService — App 殻)・
  tests/ViewPrism2.Tests(stub 3 メンバ×15 箇所前後+CpUiSimilarityViewModelTests)。
- 不変: E-BOM(実体=整理トレイで存続)・Core/Infrastructure・DB・オラクル(非参照)・CAD。

## 6. 残ゲート

1. ~~工程診断~~ → 完了(残骸撤去・**裁定不要** — ECO-024 前例踏襲)
2. /eco-fix eco-051 — 受入観点対応表 → 撤去 → 機械受入(+L1 スモーク)
3. gate②: 軽量確認(起動+主要画面一巡)— 実施要否は maintainer 判断
4. クローズ時: M-UI-SIMILARITY-023 retire の M4+register applied+教訓
