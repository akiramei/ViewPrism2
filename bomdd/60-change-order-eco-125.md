# ECO-125 — Recompute 結合点の予防棚卸し — 列挙軸を「経路種別」から「結合点」へ(ECO-124 教訓1 の実行)

- 起票日: 2026-07-21
- 報告者: AI(maintainer 指示= ECO-124 完了報告の提案採用)
- 種別: 予防棚卸し(結合点の全数分類)+分類結果に応じた是正(不要結合の除去)+再発防止 lint
- baseline: ViewPrism2 main `977b8e8`

---

## 1. 要求

26 万件経路の欠陥は 7 例(064 起動/062 検索/113 選択/114 モード遷移/115 パネル操作/118 タグ付与/
124 ペイン開閉)すべてが**事後の症状観測**から個別起票され、全て同根(`6f7b4f9`/`f211fa9` の
製造時結合)だった。ECO-114(モード軸)・ECO-115(パネル軸)の棚卸しは軸内で完全だったが、
サイドバー開閉はどちらの軸にも属さず谷間に落ちた(ECO-124 教訓1=「破れは経路単位・棚卸しは軸単位」)。

本 ECO は列挙軸を**結合点**(`Recompute()` を呼ぶ全サイト)へ切り替え、症状を待たずに全数を分類・処置する:

- **A(正当)**: 母集合再解決が意味論上必要(データ/母集合/ソート/フィルタ変化)= 現状維持。
- **B(不要結合= ECO-124 型)**: 状態通知のみで足りる= 本 ECO 内で probe 先行是正。
- **C(部分再構築で足りる= ECO-115 型)/条件付き正当**: 是正の規模が大きいものは分離起票へ。

## 2. 工程診断

| 工程 | 判定 | 根拠 |
| --- | --- | --- |
| CAD | 健全(非該当) | 計算量結合は実装内部の構造。CAD は挙動仕様のみ |
| BOM | 健全(拡張のみ) | E-UI-BROWSE-022 invariant(ECO-114/115/124 で段階拡張)が受け皿。棚卸し結果の刻印先 |
| 実装 | **棚卸し対象** | 起票時 grep 悉皆= **41 サイト**(ImageTabViewModel 28+WorkTabViewModel 13)。分類は fix の本体作業 |

## 3. 切り分け済みの事実

### 確定(起票時 grep 悉皆= 2026-07-21・`977b8e8` 時点)

1. **母集団= 41 サイト**(メソッド単位の内訳):
   - ImageTabViewModel(28): ctor:214 / LoadCatalogAsync:386 / LoadContentAsync:417,452 /
     ReloadTagCatalogAsync:540,548 / ApplyModeTransition:1446(未ロードガード) / SelectAxis:1716 /
     LoadViewAsync:1725 / ReloadViewGraphAsync:1762 / OpenRepair:1931 / OpenBackupSettings:1947 /
     SetSortAsc:1952 / SetSortDesc:1955 / SelectColumnSort:1966 / ClearColumnSort:1975 /
     SetDisplayMode:1998 / GoHome:2021 / GoCrumb:2039 / ClickChip:2048,2060 / HandleItemClick:2067
     (フォルダ潜り) / FullReloadTagsAsync:2365 / DeleteToTrash:2533 / AddCandidateToTargets:2640 /
     ContinueOrganize:2669 / OnScanUpdated:2765,2808
   - WorkTabViewModel(13): ctor:137 / LoadCurrentImagesAsync:432,444 / ReloadTagsAsync:1047 /
     RunSearch:1217 / AddCandidateToTargets:1243 / UndoMerge:1293 / ContinueOrganize:1303 /
     ClickChip:1524 / SelectColumnSort:1538 / SetSortAsc:1545 / SetSortDesc:1552 / ClearColumnSort:1559
2. **第一印象分類(起票時・fix で全数確定)**:
   - 大半は A 見込み(初期化/ロード・再読後・軸/ビュー/パンくず/チップ= 母集合変化・ソート 8 サイト=
     全件ソート本質・表示モード= 条件変化・スキャン= データ増加・削除/整理/マージ= データ変化)。
   - **条件付き正当の実測 2 サイト**: OpenRepair:1931 / OpenBackupSettings:1947 は
     **モーダル閉じ後に変化の有無を問わず無条件で ReloadImagesAsync+Recompute**(実装読取り済み)。
     意味論上の再解決が必要なのは「モーダル中に変化があった時だけ」= ECO-118 の精密化
     (「意味論上必要」と「O(母集合) 実装」は別命題)に照らすと、26 万件では
     「設定/修復を開いて閉じるだけで全再構築」= ECO-124 同族の症状候補。ただし変化検出の導入は
     規模が大きい= C(分離起票判断)の筆頭候補。
   - B(純粋な不要結合)は ECO-124 の ToggleSidebar 是正後、既知 0 件 — **fix の全数分類で確定させる**
     (第一印象を結論にしない= ECO-107 教訓2「粗スキャン結論は全数調査で再検証」)。
3. **既知の処置済みサイト(対照)**: ApplyModeTransition:1446 は未ロードガード内= 正当。
   ECO-113/114/115/118/124 の是正済み経路は Recompute を既に通らない。
4. **同型の外縁(スコープ宣言)**: 撮影ハーネス ImageTabSeedViewModel:506(cheat-log 2026-07-21
   記帳済み・性能契約適用外)は対象外。Core/他 VM に Recompute 相当の母集合パイプラインは無い
   (grep 済み= Recompute 命名は両タブ VM のみ)。

### 未検証(fix で確定)

- 41 サイトの A/B/C 全数分類(各サイトの根拠つき)。
- B が見つかった場合の是正 diff と probe。
- C(条件付き正当 2 サイト含む)の分離起票要否= 症状可能性(操作頻度×規模)で判断。

## 4. 是正方針(案・着手時確定)

**案A(推奨)**: 3 部構成。

1. **全数分類台帳**: 41 サイトを A/B/C+根拠(1 行ずつ)で ECO 本文へ記載(棚卸しの成果物)。
2. **B の是正**: probe 先行(ECO-124 様式= インスタンス同一性)で通知のみへ置換。
   置換時は**解除遷移の明示継承**(ECO-113/114/124 の定番観点 N=3= 置換元の先頭・末尾の
   副次呼び出しを列挙して残す/捨てるを宣言)。
3. **再発防止 lint**: `Recompute()` 呼び出しサイトの **allowlist pin**(ファイル:メソッド+分類根拠。
   ECO-107 様式+死亡エントリ検出)。新規サイトは lint が fail し分類を強制する=
   「列挙軸の谷間」を構造的に塞ぐ(ECO-124 教訓1 の機械化)。陽性対照同梱(N=5 の適用)。

C は台帳に判断(分離起票 or 許容+根拠)を記載して本 ECO の diff には含めない(R3)。

## 5. 影響 BOM

- **src**: B 判定サイトのみ(0 件の可能性あり= その場合 lint+台帳のみ)
- **tests**: lint 新設(allowlist pin+陽性対照)+B 是正時は probe(是正前赤)
- **ebom**: E-UI-BROWSE-022 invariant へ棚卸し完了の刻印(結合点全数分類済み・新規は lint 強制)
- **CP**: lint は automated CP として新設(accept 時・CP-REGISTRY-LINT-122 前例)

## 6. 残ゲート

- **gate①(裁定)**: 不要
- **gate②(golden)**: **必要(軽量実機)— B 是正 3 サイトが出たため。§8 の基準で維持者確認待ち。**

## 7. 実施記録(2026-07-21 fix)

- **全数分類の結果(台帳の正本= CpRecomputeCouplingLintTests.Ledger・機械 pin)**:
  直呼び 39 サイト(是正後: ImageTab 26+WorkTab 13)を A/A'/C で分類・全エントリ根拠つき。
  - **B(不要結合)= 3 サイト発見・全て本 ECO 内是正**(第一印象「既知 0」を全数調査が覆した=
    ECO-107 教訓2 の実証):
    - **B-1 AddCandidateToTargets**: 検索候補の「整理対象に追加」が全面 Recompute。グリッドクリック側
      5 サイト(SetMergeTarget 等)は ECO-113 で RefreshSelectionMarkers 済み= **面内非対称**の取り残し。
      → RefreshSelectionMarkers へ(ClearCopyFeedback は同メソッド先頭が継承)。
    - **B-2 ContinueOrganize**: データ不変のトレイリセット+マーカー解除で全面 Recompute。
      → RefreshSelectionMarkers へ。
    - **B-3 OnScanUpdated Started**: 母集合不変(画像未公開)で全面 Recompute。
      → **R8 所見3 で条件化**: 表示中コレクション自身の再スキャンは Recompute 残置(Started は
      取込順固定= ECO-060 TryGetPreservedScanOrder の有効化点であり即時実現が必要)・
      他コレクションは部分更新(RebuildCollectionRows+通知= BatchCommitted 様式と対称)。
  - **C(条件付き正当・予告記帳)3**: OpenRepair/OpenBackupSettings(モーダル閉じ後の無条件全再読)+
    R8 所見6 発見の **Organize 子 VM RunSearchAsync 成功末尾(注入 _recompute 経由・検索結果格納のみで
    全面 Recompute)**。いずれも経路名+是正型を cheat-log 予告記帳(ECO-114→115 様式)。
  - A'= DeleteToTrash(差分化は症状観測時)+WorkTab organize/tag 系(workspace 規模前提= ECO-113 R3/
    ECO-118 の残置裁定を引用)。残りは A。
- **R5**: probe 3 本(B-1/B-2= CpUiG1OrganizeTests・B-3= CpUiEco060ProgressiveScanTests の
  別コレクションスキャン単離)= **是正前赤 3 本**(Assert.Same 失敗)→ 是正で緑転。
- **lint 新設**: CpRecomputeCouplingLintTests(CP-RECOMPUTE-LINT-125)= 「ファイル:メソッド→件数」の
  台帳 pin+死亡エントリ検出+陽性対照。**新規の直呼び結合は fail して分類を強制**(列挙軸の谷間を
  構造的に塞ぐ= ECO-124 教訓1 の機械化)。検出限界を宣言(注入デリゲート 4 サイト・撮影ハーネス・
  メソッド同定の限界)。
- **E-BOM**: invariant へ棚卸し完了刻印(直呼び全数+限定句= R8 所見6 で過大主張を訂正)。
- **機械受入(4 点)**: フルビルド 0 error・0 警告 / Tests **858/858**(probe 3+lint 2 緑転)/
  Oracle 109+2skip / validate 0/0。
- **R7**: 対象外宣言(XAML 無変更・視覚不変)。
- **R8(独立レビュー・fresh context)**: 所見 9 件全処置。
  - **スコープ内 3= 全て処置済み**: 所見3(B-3 が表示中コレクション再スキャンで ECO-060「取込順固定」の
    実現を失う→ 条件分岐で旧挙動保存・再受入緑)・所見4(ECO-112 解除列挙の引用が正本と不一致→
    コメント/cheat-log を正本引用へ訂正+Started 他コレクション分岐を判定対象へ追加)・
    所見6(注入 _recompute 4 サイトの誤 characterization → 限界宣言訂正+RunSearchAsync:299 を C 記帳+
    E-BOM 限定句)。
  - **スコープ外 2= cheat-log 記帳**: _refreshSelectionMarkers 死亡配線(製造時から未行使)・
    lint パス可搬性ほか軽微(所見7)。
  - 問題なし 4(機械裏取り済み): B-1/B-2 の意味論被覆(affected 集合・子 VM 通知・転送プロパティ)・
    件数 pin の独立検算(grep 実測と全数一致)・probe 3 本の真因固定とレース耐性・A' 引用の
    正本一致(ECO-113 §7.4/ECO-118)。
- **diff 規模**: src= ImageTabViewModel 3 サイト・tests= probe 3+lint 1 クラス・E-BOM/cheat-log。

## 8. 停止点= golden 合格基準(gate②・軽量実機。26 万件推奨)

1. **整理モード・検索候補の追加(B-1)**: 26 万件ビューで整理モード→マージ先設定→類似 or 条件検索→
   結果の「整理対象に追加」が**即時**(従来は 1〜2 秒級)。追加した候補にチェック+「対象」バッジが
   付き、トレイの整理対象リストへ載る(意味論不変)。
2. **別の整理を続ける(B-2)**: マージ実行→完了→「別の整理を続ける」が**即時**。完了パネルが畳まれ、
   トレイが空に戻り、グリッドのマージ先/対象マーカーが消える(意味論不変)。
3. **別コレクションのスキャン開始(B-3)**: 26 万件ビュー表示中に別コレクションのスキャンを開始しても
   引っかかりがない。左ペインの当該行にスキャンバッジが出る。
4. **回帰(表示中コレクションの再スキャン)**: 表示中コレクション自身を再スキャンした場合の挙動が
   従来どおり(スキャン中は取込順で追記・完了時に最新ソート適用= ECO-060)。
5. **回帰(整理の実行系)**: マージ実行・取り消す・グリッドクリックでのマージ先/対象指定が従来どおり。

合格なら `/eco-accept eco-125` を指示してください。
