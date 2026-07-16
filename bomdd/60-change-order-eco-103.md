# Change Order — ECO-103(applied): タグタブ刷新 第三便 — 保存モデルの実装追随(mock v4=フローティング保存バー)+TAG-016 裁定+撮影ハーネスの正式資産化

- 起票: 2026-07-17(maintainer 実装依頼・第三便)
- 種別: 機能拡張(CAD 改版追随 — mock v4〔2026-07-17〕正典化済み。ECO-099 許容差分「ペインヘッダの保存/キャンセル」は v4 で除外=追随必須)+検査基盤の資産化
- baseline: main `c055173`

## 1. 要求(maintainer・2026-07-17)

前提= 第一便 ECO-099・第二便 ECO-100 クローズ・read-across 済み。CAD 側は **mock v4 を正典化済み**
(ViewPrismUI `1bea503`)。ECO-099 許容差分だった「ペインヘッダの保存/キャンセルボタン」は v4 正典化により
**許容差分から除外**(CAD golden 注記に例外明記済み)— 本便はその実装追随。

権威(CAD= `../ViewPrismUI`):
1. prose 正典 `docs/screens/tag_tab.md`「保存モデルと保存バー(2026-07-17 mock v4)」節+**VC-TAG-16**+
   インタラクション表 v4 追記行。
2. 視覚原器 mock v4(SHA-256 `8e0dfa1e…cca08` — **一致を実測確認済み**)。
3. キャプチャ `captures/tag_tab/`: **P2-dirty / P2-dirty-attention / P2-saved-toast / NAV-dirty**。

スコープ:
1. **ヘッダの保存/キャンセル撤去 → フローティング保存バー**(dirty 時のみ・中央ペイン下部中央・
   ダーク地 `#1d2430`・「破棄」ゴースト+「保存」アクセント✓)。旧「キャンセル」文言は**「破棄」**へ
   (接続先=既存バッチモデルの復元。破棄時は配置モード・開いた⋯メニューもクリア= mock discard どおり)。
2. **未保存の 3 表示**: ナビのタグタブ琥珀ドット(title「未保存の変更があります」)/中央ヘッダ
   「未保存の変更」チップ(琥珀)/保存バー。クリーン時は 3 点とも一切残らない。
3. **遷移ガード**: dirty 中の別ビュー選択・他タブ(画像/作業)遷移をブロックし、バーを shake+琥珀ボーダー+
   「移動する前に、保存するか破棄してください」へ 700ms 切替。
4. **保存トースト**: 「✓ 変更を保存しました」(緑 `#0f8a5e`・1.8s 自動消滅)。
5. **アニメーション**: vpRise(下から 0.3s)/vpShake(0.5s)相当。Avalonia 既存アニメ言語での近似可
   (近似差分は R7 で分類し golden 裁定へ)。
6. **i18n**: 新規文言 ja= mock 原文転写+en を ECO で起こして両輪追加(en は golden 裁定対象)。
   撤去するヘッダボタンの旧キーは棚卸しして削除(ECO-087/099 の代替導線撤去手順)。
7. **退化禁止**: 配置モデル・⋯メニュー・ホーム・D&D・TAG-013(操作契約 pin 19 本+全テスト緑)。
   layoutInvariant 維持(バーはペイン内フロート=ページ全体スクロールを作らない)。

**TAG-016 裁定(実装時確定・既定方向は依頼文提示)**: 契約方向=「未保存の編集を黙って失う遷移を作らない」。
(i)新規ビュー作成・ビュー行操作(リネーム/削除)= 同ガード様式でブロック
(ii)設定遷移・検索起動= 非破壊なら通過可(現行遷移構造に照らして裁定)
(iii)アプリ終了= デスクトップ慣例の OS 確認ダイアログ可(バー様式は求めない)
(iv)保存失敗= バー attention 維持+失敗理由の提示方法を裁定(新視覚面が必要ならモック改版へ差し戻し)。

**撮影ハーネスの正式資産化(maintainer 指示)**: 公開安全ゲート通過形で track・来歴付与
(ECO-100 教訓 3「headless+Skia self-golden 様式」の資産化・BomDD 昇格候補の位置づけを記録)・
置き場所/命名は製造側流儀・本便 R7 の 4 状態撮影に再利用。

## 2. 工程診断 — CAD 健全(v4 正典化済み)・実装が v3 準拠=実装追随。gate① 不要

| 工程 | 判定 | 根拠 |
| --- | --- | --- |
| CAD | **健全(2026-07-17 v4 正典化済み)** | 「保存モデルと保存バー」節が MOCK 裁定込みで逐条化・VC-TAG-16 新設・原器 4 面あり。旧 as-built(ヘッダ保存/キャンセル)の superseded を明記。残余は TAG-016 として台帳化済み(「実装時裁定で確定」手続き=ECO-099/100 と同型)。 |
| BOM | **追随要(欠陥ではない)** | E-UI-NODEGRAPH-025/M-UI-013/CP-UI-G6 は保存モデルの新契約(3 表示・バー・ガード・トースト)未宣言 → fix 時 M4 同期。撮影ハーネスは M-BOM に unit 未登録(scratch)→ 資産化で登録。 |
| 実装 | **乖離(変更対象)** | §3.1。ヘッダボタン+確認ダイアログ方式(v3 as-built)が残存。3 表示・バー・ガード・トーストは全て未実装。 |

- **gate①(裁定)不要**: TAG-016(i)〜(iv)は依頼文の既定方向を §4.2 で ECO 裁定として確定
  (ECO-099 TAG-015/ECO-100 TAG-014 と同型の確定手続き)。
- 未確定事項との関係: TAG-016 そのものが対象。

## 3. 切り分け済みの事実(2026-07-17 コード読解+mock v4 実測)

### 3.1 現行実装(v3 as-built)と v4 契約の乖離

| # | v4 契約 | 現行実装 | 判定 |
| --- | --- | --- | --- |
| 1 | 保存バー(dirty 時のみ・ペイン下部フロート) | ヘッダに保存/キャンセルボタン常設(`TagsTabView.axaml` 中央ペインヘッダ・IsDirty で活性制御)+StatusMessage テキスト | **置換対象** |
| 2 | 「破棄」=確認なし復元+配置モード/⋯メニューもクリア | `CancelAsync`= **確認ダイアログ**(modals.confirmDiscard)後に LoadAsync(LoadAsync が placing はクリア済み) | **乖離(ダイアログ撤去)** |
| 3 | 未保存 3 表示(ナビドット/ヘッダチップ/バー) | いずれも無し(IsDirty は VM に既存=表示だけ未実装) | **未実装** |
| 4 | 遷移ガード: 別ビュー選択=ブロック+shake | `SelectViewAsync:174`= `ConfirmDiscardIfDirtyAsync`(**確認ダイアログで破棄可**) | **乖離(ブロック方式へ)** |
| 5 | 遷移ガード: 他タブ=ブロック+shake | `MainWindowViewModel.OnSelectedTabIndexChanged:140`= **ガードなし**(dirty のまま遷移可・編集自体は保持) | **未実装** |
| 6 | 保存トースト(緑・1.8s) | `SaveAsync`= StatusMessage に success.saved 文言(ヘッダ常設テキスト) | **置換対象** |
| 7 | (i)ビュー操作の dirty ガード | `NewView/EditView/DeleteView`= ガードなし。**Delete は選択中 dirty ビューを削除可能=未保存編集の消失経路が現存** | **未実装(TAG-016)** |
| 8 | (iii)アプリ終了 | `App.axaml.cs:81 window.Closing`= 設定保存のみ(dirty 確認なし=黙って失う) | **未実装(TAG-016)** |

### 3.2 mock v4 実測(転写元)

- vpRise: `translate(-50%,12px)→0`・バー 0.3s/トースト 0.25s。vpShake: `±9px`・0.5s。
- バー: 琥珀ドット+文言「未保存の変更があります」(#dfe4ec w500)⇔ attention 時「移動する前に、
  保存するか破棄してください」(#f5d979 w600)・ボーダー rgba(232,185,49,.7)⇔rgba(255,255,255,.08)・
  700ms 復帰。「破棄」= ゴースト #c6cede/hover 白9%地。「保存」= アクセントグラデ+✓(stroke 2.4)。
- トースト: `#0f8a5e`・radius 11・padding 9,16・✓+「変更を保存しました」・bottom 20・1.8s。
- ナビドット: 7px `#e8b931`+3px リング rgba(232,185,49,.2)・title「未保存の変更があります」。
- ヘッダチップ: 「未保存の変更」・`#fbf3df`/`#9a7b1a`・6px ドット・padding 3,9・radius 6・fontSize 11。
- discard(mock JS): スナップショット復元+`placing:null, menuFor:null`。guardNav= ビュー行・他タブ
  クリックで attention 700ms。

### 3.3 撮影ハーネスの現況(資産化対象の確定)

- **依頼文の `.src009-capture-harness/` は現在の作業ツリーに存在しない**(2026-07-17 実測:
  ディレクトリ無し・git status clean・リポ内に SRC009 名の残存なし)。ECO-101 作業中(2026-07-17)には
  未追跡で存在し、公開安全対応(ProfilePath の実行時導出化)を施した来歴がある — その後の消失経緯は不明
  (maintainer ローカル操作と推定)。
- **資産化対象は ECO-100 教訓 3 が指す headless+Skia ハーネス**(ECO-099 R7 で開発・099/100 の R7 並置
  実績・現在 scratch)と解釈して進める: 依頼の目的節が「ECO-100 教訓 3『headless+Skia self-golden 様式』
  の資産化」と明記しており、R7 の 4 状態撮影への再利用も headless 方式で成立する。**SRC-009(実機
  プロファイル方式)を別途 track したい場合は素材の再供給が必要**(完了報告で明示する)。

### 3.4 未検証(fix 時に確定)

- Avalonia での vpRise/vpShake 近似(Animation/Transitions の既存言語で何処まで寄るか)— 近似差分は
  R7 で分類し golden 裁定へ(依頼許容)。
- 破棄時の「開いた⋯メニューのクリア」: Flyout は light dismiss(開いたまま破棄ボタンは押せない)の
  ため実質自明の可能性 — 実測して記録。
- (iv)保存失敗の提示: 現行 StatusMessage(ErrorMessages.Resolve)の受け皿がヘッダ撤去で失われる —
  バー attention 様式への移設で新視覚面が不要かを実装時に確定。

## 4. 是正方針(案・着手時確定)+TAG-016 裁定

### 4.1 是正方針(案)

1. **表示 3 点+バー+トースト**: TagsTabView 中央ペインを Panel 化しバー/トーストを下部中央フロートで
   重畳(ペイン内 absolute 相当=layoutInvariant 不変)。ナビドットは MainWindow のタグタブボタンに
   `TagsTab.Editor.IsDirty` バインド。ヘッダチップは既存ヘッダへ追加。ヘッダの保存/キャンセルボタンと
   StatusMessage 常設テキストは撤去(i18n 棚卸し込み)。
2. **遷移ガード**: `HierarchyEditorViewModel` に attention 状態(700ms 復帰タイマ)+`GuardNavigation()`
   (dirty なら attention 発火して false)を追加。消費= SelectViewAsync(ダイアログ→ブロック)・
   MainWindowViewModel のタブ切替(タグタブ dirty 中は遷移拒否+ガード発火)・(i)の New/Edit/DeleteView。
   `ConfirmDiscardIfDirtyAsync` は遷移系から撤去(残る消費者を棚卸し)。
3. **保存/破棄**: SaveAsync= 成功時トースト(1.8s タイマ)・失敗時 (iv) 様式。CancelAsync= 確認ダイアログ
   撤去し即復元(破棄)+placing/menu クリア。
4. **アニメ近似**: バー出現= Transitions(TranslateY+Opacity 0.3s)・shake= 既存アニメ言語
   (Animation keyframes)で ±9px 0.5s。決定論検査は状態(クラス/可視)で行いアニメは golden。
5. **ハーネス資産化**: scratch vp2cap → `tools/ViewPrism2.CaptureHarness/`(sln へ追加=ビルド退化防止)。
   来歴ヘッダ(ECO-099 R7 開発・099/100 実績・BomDD 昇格候補)+公開安全(絶対パスなし)。
   本便 R7 で dirty/attention/toast/NAV の 4 状態を撮影し原器 4 面と並置。
6. **プローブ(R5)**: dirty 3 表示の出現/クリーン時消滅・ガード(ビュー/タブ/ビュー操作)のブロック+
   attention 発火/700ms 復帰・破棄=確認なし復元+placing クリア・保存=トースト+1.8s 消滅・
   (iv)失敗時 attention 維持+理由提示。スタブ赤→緑転。

### 4.2 TAG-016 裁定(本 ECO で確定・2026-07-17。依頼文の既定方向を採択)

| 項 | 裁定 | 根拠 |
| --- | --- | --- |
| (i) 新規ビュー作成・ビュー行操作(リネーム/削除) | **dirty 中は同ガード様式(shake+琥珀)でブロック** | 特に Delete は未保存編集の消失経路が現存(§3.1-7)。「黙って失う遷移を作らない」の直接適用 |
| (ii) 設定遷移・検索起動 | **通過可**: 設定=モーダル(ShowSettingsAsync)でタグタブ状態を破壊しない・検索=パレット内(タブ内・非破壊)。画像タブ側検索等はタブ遷移ガードが覆う | 現行遷移構造の実測(§3.1)。破壊しない導線を塞ぐのは過剰ガード |
| (iii) アプリ終了/クローズ | **OS 確認ダイアログ**: dirty 中の Closing で既存 ConfirmDialog 様式の 2 択「破棄して終了/戻る」(戻る=キャンセルで保存バーから保存可能)。バー様式は適用しない | デスクトップ慣例。3 択(保存込み)は新ダイアログ面の発明になるため既存部品の 2 択で最小(golden 裁定対象として記録) |
| (iv) 保存失敗時 | **バーを attention 様式で維持+メッセージ位置に失敗理由**(ErrorMessages.Resolve 文言・琥珀)。次の操作まで維持(700ms 自動復帰は遷移ガードのみ)。**新視覚面は不要**= attention 様式の文言差し替えで成立見込み(実装で確認・不成立ならモック改版へ差し戻し) | mock の attention 様式の流用=発明なし。失敗理由の既存受け皿(StatusMessage)の後継 |

## 5. 影響 BOM(fix 時 M4 で同期)

- **src**: `TagsTabView.axaml`/`.cs`(ヘッダボタン撤去・バー/トースト/チップ・アニメ)+
  `HierarchyEditorViewModel`(attention/ガード/トースト状態・破棄の無確認化)+`TagsTabViewModel`
  (ビュー操作ガード)+`MainWindowViewModel`/`MainWindow.axaml`(タブ遷移ガード・ナビドット)+
  `App.axaml.cs`(Closing の dirty 確認)。
- **tools(新設)**: `tools/ViewPrism2.CaptureHarness/`(sln 追加・来歴付き)。
- **tests**: 新規 probe(§4.1-6)。既存 pin 19 本+全テスト緑維持。R6= 固定 Oracle 不変。
- **E-BOM**: E-UI-NODEGRAPH-025(保存モデル)+E-UI-SHELL-021(ナビドット・タブ遷移ガード)へ追補。
- **M-BOM**: M-UI-013 追記+撮影ハーネスの harness unit 新設。
- **CP**: CP-UI-G6 へ VC-TAG-16 次元。
- **i18n**: 新キー(バー文言 2・破棄/保存・トースト・チップ・ナビ title・終了確認)ja/en。
  旧キー棚卸し(common.cancel はグローバル・success.saved の消費者確認)。
- **CAD**: 変更しない。TAG-016 クローズ材料+golden 裁定結果(en copy・(iii)2 択・アニメ近似)を
  read-across 依頼メモとして納品(新状態の実機スクリーンショットは `artifacts/` 経由可=依頼指示)。

## 6. 残ゲート

- gate①: **不要**(§2。TAG-016(i)〜(iv)は §4.2 で確定済み)。
- gate②(golden): **必要**。maintainer 実機(依頼文チェックリスト):
  - VC-TAG-16 ①〜⑥(特に⑤クリーン時に何も残らない・⑥ガードで遷移してしまわない)を
    captures 4 面(P2-dirty/P2-dirty-attention/P2-saved-toast/NAV-dirty)と並置。
  - 旧ヘッダボタンの完全撤去(死んだ i18n キー・接続先なし=棚卸し記録)。
  - TAG-016 裁定(i)〜(iv)の実測成立。
  - 既存 pin 19 本+全テスト緑・layoutInvariant 不変。
  - en copy・(iii)の 2 択様式・アニメ近似差分の裁定。

## 7. `/eco-fix` 実施記録(2026-07-17)

### 7.1 プローブ先行(R5)

新規 `CpUiG6SaveBarTests`(8 本)を VC-TAG-16+TAG-016 裁定から生成し、VM へ API スタブ(no-op)を
置いて実行 → **8/8 不合格**(ガード 4 経路〔別ビュー/タブ/ビュー操作/attention 復帰〕・破棄無確認・
トースト・保存失敗 attention・headless 3 表示が全て赤=新契約の不在を実測)。既存 782 本は全緑を起点固定。

**既存テストの契約更新 3 本(記録・v4 で旧契約が superseded)**: CpUiG6HierarchyEditorTests の
「キャンセルは確認後に…」→「破棄は確認なしで…」(ConfirmCount 0 を pin)/「キャンセル確認でいいえなら
編集は保持される」→ **削除**(確認ダイアログ自体が撤去=検証対象消滅)/「ダーティ中の切替確認は
ConfirmDiscardIfDirtyAsyncで行う」→「ダーティ中の遷移はGuardNavigationが拒否する」。
固定 Oracle は不変(R6)。

### 7.2 是正内容

- **VM(HierarchyEditorViewModel)**: GuardNavigation(dirty=拒否+attention・700ms 自動復帰=
  検査用可変)+IsSaveBarAttention/SaveBarMessage(優先度=失敗理由>ガード>通常)+SaveError
  (TAG-016(iv)=自動復帰なし・保存/破棄/再読込で解除)+IsSavedToastVisible(1.8s=検査用可変)。
  SaveAsync= 成功でトースト+attention 解除/失敗で SaveError。CancelAsync= **確認なし復元**
  (旧 modals.confirmDiscard 撤去)。ConfirmDiscardIfDirtyAsync/StatusMessage は撤去。
- **ガード消費**: TagsTabViewModel= 別ビュー選択+New/Edit/DeleteView(TAG-016(i)。Delete の
  未保存編集消失経路を閉鎖)。MainWindowViewModel= ShowImagesTab/ShowWorkTab(タグタブ dirty 中は
  遷移拒否)。App.Closing= TAG-016(iii)= dirty 中は ConfirmDialog 2 択(破棄して終了/戻る)・
  承認後に再 Close で通常終了経路へ合流。
- **View**: ヘッダの保存/キャンセルボタン+StatusMessage 撤去 → 「未保存の変更」チップ(琥珀)。
  中央ペインを Panel 化し保存バー+トーストを下部中央フロート(layoutInvariant 不変)。
  vpRise 近似= Style.Animations(Opacity+TranslateTransform.Y 12→0・0.3s)/vpShake 近似=
  attention クラス付与時の keyframes(X ±9px・0.5s・再付与で再生)。保存ボタンのグラデは Fluent
  ContentPresenter へ(GF-056-02 系の罠回避)。MainWindow= ナビ琥珀ドット(7px+20%α リング+title)。
- **i18n 棚卸し**: 新キー 9(saveBar 4・savedToast・unsavedChip・closeConfirm 2 ほか)を ja=mock
  原文転写+en 両輪で追加。**撤去= modals.confirmDiscard.*(4 キー×2 言語・全消費者撤去済み)**。
  success.saved= FolderManagement が消費のため**残置**(エディタ参照のみ撤去)・common.save/cancel=
  グローバル(他ダイアログ消費)のため残置。
- **撮影ハーネスの正式資産化**: scratch(ECO-099 R7 開発)→ `tools/ViewPrism2.CaptureHarness/` へ
  移設・**sln へ追加**(ビルド退化防止)・来歴ヘッダ+M-BOM unit **M-CAPTURE-HARNESS-052** として登録
  (BomDD 昇格候補=「headless+Skia self-golden 様式」の位置づけを unit の provenance に記録)。
  公開安全= 絶対パスなし(出力先=引数)。v4 の 4 状態撮影を追加。
  **注記: 依頼文の `.src009-capture-harness/`(実機プロファイル方式)は作業ツリーに現存せず**
  (§3.3)— 資産化は依頼の目的節(ECO-100 教訓 3)が指す headless+Skia ハーネスで充足。

### 7.3 機械受入

build 0 error(sln にハーネス込み)/ **Tests 789/789**(プローブ 8 本緑転+契約更新 3 本・
既存 pin 19 本含む全緑)/ Oracle 109+2skip(R6 不変)/ validate_bom 0/0。
M4= E-UI-NODEGRAPH-025(保存モデル invariant)+E-UI-SHELL-021(ナビドット/タブガード/終了確認)+
M-UI-013(save_model)+M-CAPTURE-HARNESS-052 新設+CP-UI-G6(VC-TAG-16 次元)。

### 7.4 セルフゴールデン(R7・資産化ハーネスで撮影)

dirty/attention/toast/NAV の 4 状態を実描画し原器 4 面と並置(+クリーン基準=impl-full で ⑤ を確認):

| # | 差分/次元 | 分類 |
|---|---|---|
| 保存バー(ダーク地/琥珀ドット/文言/破棄ゴースト/保存グラデ✓)・ヘッダチップ・ナビドット+リング・attention(琥珀ボーダー+ガード文言)・トースト(緑✓)・クリーン時の完全消滅 | 転写(4 面並置+headless probe 実測) | — |
| vpRise/vpShake= Avalonia Style.Animations による**近似**(rise= Opacity+Y 平行移動 0.3s/shake= X ±9px 0.5s。cubic 曲線等の厳密一致は未検証=静止 capture では差分なし) | **要確認**(依頼が近似可と明記・golden で動きを裁定) | 要確認 |
| en copy 9 キー(saveBar.unsaved/guard/discard/save・savedToast・unsavedChip・closeConfirm ほか) | **要確認**(golden 裁定対象=依頼指示) | 要確認 |
| (iii) 終了確認= 既存 ConfirmDialog の 2 択(3 択の新ダイアログ面は発明になるため不採用) | **要確認**(TAG-016(iii)裁定の様式確認) | 要確認 |
| mock ヘッダ右の飾りアイコン・グローバル検索(NAV-dirty 原器に写る)= as-built 非搭載 | 既存 as-built(ECO-099 §7.4 系の分類済み差分) | 裁定済み |

転写漏れ 0(「要確認」は golden 裁定へ)。**(iv)の新視覚面は不要**= attention 様式の文言差し替えで
成立(モック改版への差し戻し事項なし)。

### 7.5 §3.4 の決着

- 破棄時の「開いた⋯メニューのクリア」= Flyout は light dismiss(メニューを開いたまま保存バーは
  押せない=クリックで先にメニューが閉じる)ため**実質自明**を実測確認。
- 保存失敗の提示= バー attention 様式の文言差し替えで成立(新視覚面不要)。

### 7.6 GF 是正(golden 所見 2 件・2026-07-17)

maintainer 実機 golden で保存バーに視覚転写漏れ 2 件(mock/実機キャプチャ並置で提示):

- **GF-103-01: 破棄/保存ボタンの文言が上寄り**。真因= 固定高(34)+Padding 縦 0 のボタンで
  `VerticalContentAlignment` 未指定 → Fluent ContentPresenter 既定 Stretch により TextBlock が
  ボタン全高へ引き伸ばされ、グリフがその上端に描かれる。**ECO-040 規約の再発(3 度目= ECO-040 →
  GF-091-01 → 本件)**。是正= saveBarDiscard/saveBarSave スタイルへ `VerticalContentAlignment=Center`
  明示(規約準拠・2 setter)。
- **GF-103-02: 保存バーの琥珀ドットにハロー欠落**。mock= `box-shadow 0 0 0 3px rgba(232,185,49,.22)`。
  実装は素の Ellipse 8px のみ。是正= ナビ unsavedDot と同一のリング様式(22%α 地 `#38e8b931`+
  Padding 3 の Border ホスト)へ置換。ヘッダチップ内 6px ドットは mock もハローなし=現状正を突合確認。

**プローブ(R5・是正前赤)**: CpUiG6SaveBarTests「保存バーのボタン文言は縦中央でドットはハローリング付き」。
上寄りの実測= **グリフ中心 7.9 vs ボタン中心 17.0**(9px 上寄り)。
**プローブ技法の教訓**: Stretch 起因の上寄りは TextBlock の Bounds 中心では検出不能(TextBlock 自体が
全高へ伸びて中心が一致してしまう)— **グリフ実高= TextLayout.Height 基準**で測る。初版 Bounds 基準の
プローブはボタン側を素通りしドット側でのみ落ちた(実測で判明→ TextLayout 基準へ改稿し赤を確認)。

機械受入(GF 後)= build 0 error / **Tests 790/790**(プローブ緑転)/ Oracle 109+2skip / validate 0/0。
セルフゴールデン再撮影(ハーネス)= dirty/attention とも文言縦中央+ハローを確認・原器と並置一致。
CAD 側= VC-TAG-16② の検査粒度へ「ドットのハロー」「ボタン文言の縦中央」を追記(チェックリスト
由来プローブの再発防止・mock は当初から権威を保持=CAD 欠陥ではなく転写漏れ+検査粒度の目こぼし)。

**スコープ外所見(R3→51-cheat-log)**: CpUiG1CollectionScopeTests
「ECO064_起動はcatalogと選択contentのloading状態を別々に公開する」が本 GF と無関係に間欠不合格
(1/3 run・Assert.True 47ms・タイミング系の疑い)。本 ECO の diff には混ぜず記録のみ。

## 8. CAD への read-across 依頼メモ(TAG-016 クローズ材料・成果物 4)

1. **TAG-016= クローズ可**。確定裁定:
   - (i) 新規ビュー作成・ビュー行操作(リネーム/削除)= dirty 中は同ガード様式(shake+琥珀)でブロック。
   - (ii) 設定遷移(モーダル)・パレット検索(タブ内)= 非破壊導線として通過可。
   - (iii) アプリ終了= 既存 ConfirmDialog 様式の 2 択(「未保存の変更があります。破棄して終了しますか？」
     = 破棄して終了/戻る)。3 択(保存込み)は新ダイアログ面の発明になるため不採用(戻る→保存バーから保存)。
   - (iv) 保存失敗= バーを attention 様式で維持+メッセージ位置に失敗理由(自動復帰なし・次の
     保存/破棄/再読込まで)。**新視覚面不要**=モック改版への差し戻しなし。
2. **en copy 全文**(golden 裁定対象): saveBar.unsaved="You have unsaved changes" / saveBar.guard=
   "Save or discard your changes before moving on" / saveBar.discard="Discard" / saveBar.save="Save" /
   savedToast="Changes saved" / unsavedChip="Unsaved changes" / closeConfirm.title="Unsaved changes" /
   closeConfirm.message="You have unsaved changes. Discard them and exit?"
3. アニメ近似(vpRise/vpShake= Style.Animations)の golden 裁定結果を反映。
4. 新状態の実機スクリーンショットが必要なら golden 時に採取し `artifacts/` 経由で納品可(SRC-009 方式)。

## 9. クローズ(2026-07-17・golden approved)

**maintainer 実機承認**: VC-TAG-16①〜⑥(dirty 中のみ 3 表示・クリーン時の完全消滅・遷移ガードで
遷移してしまわない・トースト・破棄無確認)を captures 4 面並置で確認。1 巡目所見= GF-103-01/02
(保存バーのボタン文言上寄り+琥珀ドットのハロー欠落。§7.6)→同日是正・再確認合格。持ち越し裁定 3 点も
承認= vpRise/vpShake の Style.Animations 近似・en copy 9 キー(§8-2 全文)・TAG-016(iii)=
ConfirmDialog 2 択様式(いずれも CP-UI-G6 へ「以後差分扱いしない」として刻印)。

**再発防止の場所**: CP-UI-G6(GF-103 刻印= グリフ実高基準の縦中央検査+ドットハロー粒度)/
VC-TAG-16②検査粒度追記(VPUI `766f943`)/ CpUiG6SaveBarTests(視覚転写 probe 恒久化)/
51-cheat-log(Stretch 上寄り N=2 到達=昇格判定 DUE 記帳)。

**教訓**:

1. **「規約は存在するだけでは新設スタイルに伝搬しない」(ECO-040 規約の再発 N=3)**: ECO-040 で
   規約化・Components.axaml 全域に適用済みでも、新規スタイル群(saveBar 系)を書く手が規約を
   参照しなければ再発する。既存教訓との関係= GF-091-01(N=1 の芽・51-cheat-log)の 2 例目到達で
   台帳自身の基準により **BomDD 昇格判定 DUE**。昇格素材= (a) playbook 既定作法「固定高+Padding
   縦 0 のコントロールは VerticalContentAlignment 明示」(b) 検査器候補= axaml 静的 lint(Height 指定
   +縦 Padding 0 の Button スタイルで VerticalContentAlignment 欠落を検出)— 人の注意でなく
   検査器で規約を運ぶ(i18n lint と同型の解)。
2. **視覚 probe の測定量はレイアウトボックスでなく描画実体(グリフ)**: Bounds 中心比較は Stretch
   された TextBlock を素通りする(本件で初版 probe が実際に素通り)。TextLayout.Height 基準の
   中心比較が正手 — GF-091-01 の芽が予言した盲点の実測確認であり、CP-UI-G6 へ検査手順として刻印。
3. **チェックリスト(visualContract)の粒度は装飾レイヤーで漏れる**: VC-TAG-16② は色・配置・構成
   要素を列挙したが box-shadow(ハロー)と文字ボックス内整列は暗黙だった。mock の style 属性を
   転写する際は「レイアウト(位置・寸法)/塗り(色)/装飾(shadow・整列・weight)」の 3 レイヤーで
   全数拾う — GF-073 系(共通言語の転写漏れ)の同族で、装飾レイヤー版。

**スコープ外の後続(cheat-log 記帳済み)**: CpUiG1CollectionScopeTests(ECO064)の間欠不合格
(タイミング系疑い・1/3 run)— 次回再現時に検査器ライフサイクル系一括起票との合流可否を判定。
