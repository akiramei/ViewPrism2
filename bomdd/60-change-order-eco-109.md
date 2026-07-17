# ECO-109: ファイル一覧 並び替え UI の視覚追随(mock 精緻化改版 2026-07-17 への転写)

- 起票日: 2026-07-18
- 報告者: maintainer(実機とモックの並び替えドロップダウン比較による視覚乖離指摘 → mock 精緻化改版納品)
- 種別: CAD mock 改版追随(視覚転写のみ・ECO-076 前例と同型)
- status: staged

## §1 症状・要求

maintainer が実機とモックの並び替えドロップダウン(アイコン表示の単一「並び替え」メニュー)を
比較して視覚乖離を指摘し、CAD 側 mock が精緻化改版された(2026-07-17 納品)。本 ECO はその
**視覚転写のみの追随**。ソートの動作モデル(FL-003: 対象=ビューの表示列・状態は表示形式間共有・
チップ✕で解除・同列再選択で方向トグル等)は**変更なし**。

CAD 権威(`../ViewPrismUI`・乖離時は常に CAD が正):

1. prose 正典: `docs/screens/file_list.md` —「アイコン表示: 単一『並び替え』メニュー」節
   (L145-159・2026-07-17 視覚詳細追記)+視覚契約 **VC-FL-1〜4**(L237-240・同日新設)。
2. 視覚原器(mock): `資料/画像タブ/ViewPrism2 ファイル一覧.dc.html`
   (SHA-256 `ff98c15a818e25f1822f48a84bce11dce1e67fcf66bfce532c7c5cdbb289ce94` — 起票時に実測一致確認済み)。
3. キャプチャ(R7 並置の原器): `docs/screens/captures/file_list/` —
   SORT-menu.png / TB-grid.png / LIST-sorted.png / GRID-sorted.png / full.png(5 面・存在確認済み)。

## §2 工程診断(R2)

| 工程 | 判定 | 根拠 |
| --- | --- | --- |
| CAD(ViewPrismUI) | **健全(権威改版済み)** | prose に視覚詳細節+VC-FL-1〜4 が 2026-07-17 新設済み。mock SHA-256 一致・captures 5 面納品済み。CAD 欠陥ではなく CAD の精緻化改版=追随対象。 |
| BOM(30-ebom/32-mbom) | **軽微追随要(fix/M4 で同期)** | E-UI-BROWSE-022(30-ebom:439-446)の FL-003 動作宣言は健全。ただし視覚受入は旧 mock 水準(リストヘッダー強調 #2459cf 等は既記載・VC-FL-1 の精緻化詳細〔種別チップ配色・列グリフ・38px 行〕は未参照)→ acceptance へ VC-FL-1〜4 参照を追記。 |
| 実装 | **追随対象(逸脱ではなく旧版準拠)** | ImageTabView.axaml:632-711 は旧 mock 水準の転写のまま。混入コミットは無し(改版前は適合していた)。§3 に乖離実測。 |

**診断確定: 実装層の視覚転写(CAD 改版追随)。gate①(CAD 裁定)不要** — CAD 側は maintainer
自身の改版納品で確定済み。→ /eco-fix eco-109 で着手可。

## §3 切り分け済みの事実

### 3.1 確定(起票時実測・ImageTabView.axaml + Components.axaml + ImageTabViewModel.cs)

**VC-FL-1(並び替えメニュー)の乖離**:

| # | mock(VC-FL-1) | 現行実装 | 箇所 |
| --- | --- | --- | --- |
| a | ポップオーバー幅 252・角丸 13 | 幅 **248**・popupMenu 角丸 **12** | ImageTabView.axaml:661 / Components.axaml:392 |
| b | 候補行 先頭 20px アイコン列: 基本情報=列グリフの灰ボックス `#f0f2f6` | **不在**(基本情報行は先頭アイコンなし・タグ列の色ドットのみ) | ImageTabView.axaml:688-692 |
| c | タグ列の色ドット 9px | Ellipse **10px** | ImageTabView.axaml:689 |
| d | 種別チップ 種別配色(基本=灰 `#f0f2f6`/数値=緑 `#eafaf3`/`#0f8a5e`/テキスト=青 `#eaf1fe`/`#2459cf`/シンプル=紫 `#f3eefe`/`#7c4fd6`) | **一律** CardSelectedBgBrush/TextMutedBrush(kind 別配色なし) | ImageTabView.axaml:684-687 |
| e | アクティブ行=青地 `#f3f8ff`+**太字 `#2459cf` ラベル**+行末矢印 | 青地+矢印はあるが**ラベルは常に TextPrimaryBrush・太字なし** | ImageTabView.axaml:669-671, 691 |
| f | 候補行 高さ 38px(角丸 9) | Padding 10,9 の自然高(**固定 38px でない**)。角丸 9=OK | Components.axaml:396-402 |
| g | 下部セグメント アクティブ=青地 `#eaf1fe`/文字 `#2459cf`・非アクティブ=灰地 `#f4f6fa`/文字 `#7b8595` | segBtn active=**白地**(AppBackgroundBrush)/AccentBrush・非アクティブ地=BadgeBg | Components.axaml:504-529 |
| h | 非アクティブ行 hover `#f6f8fb` | menuRow hover=ValueChipBg(実値は fix 時に照合) | Components.axaml:407-409 |

**VC-FL-2(ツールバー)の乖離**:

| # | mock(VC-FL-2) | 現行実装 | 箇所 |
| --- | --- | --- | --- |
| i | ソート列名バッジ=青地・**太字**(未ソート「なし」) | 青地(CardSelectedBgBrush)/AccentBrush だが**太字なし** | ImageTabView.axaml:653-655 |
| j | メニュー開時はボタン枠濃色 `#cfd6e1` | **なし**(開閉で枠不変) | ImageTabView.axaml:649 |

**適合確認済み(是正不要)**:

- ソートチップは `ShowSortChip`(=isSorted)のみが条件で**リスト/アイコン共通に既に出ている**
  (ImageTabViewModel.cs:848・XAML :632)。CAD 明確化「片形式にしか出していなければ是正」→ 該当なし。
- チップ文言=`view.columnSortLabel`「{列名}（{方向}）」+✕+方向矢印(降順180°)・
  未ソート時バッジ「なし」(`view.sortNone`)=既適合(ImageTabViewModel.cs:708, 852-854)。
- **メニュー候補の並び順=`_listColumnDefs` 順(表示列の定義順)**(ImageTabViewModel.cs:698-703)
  = mock(`sortOptions = cols`)と一致。**オーダーの裁定候補①は実装一致につき裁定不要**
  (既定方向=表示列順を追認。パレットの name 昇順=REQ-029 とは別面)。

**作業タブへの波及(確定)**: 並び替えメニューは共通部品ではなく **WorkTabView.axaml:610- に同型の
複製実装**(M-UI-WORK-033・32-mbom:372=SortOptionVM/ListColumnBuilder は共有だが XAML surface は別)。
→ スコープに含め同時追随(基本3列限定・FL-003 read-across)。

### 3.2 未検証(fix 時に確定)

- VC-FL-3(リスト: アクティブ列ヘッダー強調・型別セル)/VC-FL-4(タイルのソート項目表示)は
  実装済みだが精緻化 mock との全数突合は未実施 → fix の R7 並置(LIST-sorted.png/GRID-sorted.png)で
  乖離を全数分類(是正 or 裁定済み差分の記録)。
- hover 色(#h)・チップ境界 `#cfe0fc`(既に XAML :632 に直値あり=適合見込み)等の実値照合。
- 種別チップの i18n キー(`ListColumnBuilder.KindChipKey`=ECO-108 でキー化済み)と mock 文言の一致。

## §4 是正方針(案・着手時確定)

1. **VC-FL-1 転写**: ImageTabView.axaml の並び替え Popup(:659-710)を mock 準拠へ —
   幅 252/角丸 13(popupMenu の共有クラス改変は他面へ波及するため surface ローカル上書きを優先検討)・
   候補行 38px 固定高+先頭 20px アイコン列(基本=列グリフ灰ボックス/タグ=色ドット 9px)・
   種別チップ kind 別配色(SortOptionVM へ kind 露出 or 配色プロパティ追加)・
   アクティブ行太字 `#2459cf`・セグメント青地配色(共有 segBtn でなく専用クラス化を検討=他面退化禁止)。
2. **VC-FL-2 転写**: バッジ太字・開時トリガー枠 `#cfd6e1`(SortMenuOpen バインドの枠色切替)。
3. **VC-FL-3/4 突合**: R7 並置で乖離全数分類(是正 or 記録)。
4. **作業タブ同時追随**: WorkTabView.axaml の同型面へ同一転写(基本3列限定)。
5. 既知の再発点(オーダー明記)を検査に織り込む:
   - 装飾3レイヤー転写(ECO-103 教訓3)=レイアウト/塗り/装飾で全数拾う。文言縦中央は
     グリフ実高基準(GF-103: Bounds 中心比較は Stretch を素通り)。
   - 固定高+縦 Padding 0 は VerticalContentAlignment 明示(ECO-040 規約・N=3 再発中)。
     38px 候補行・セグメント・チップが該当しやすい。
   - CP-UI-G6 の歴代承認済み許容差分(アイコン塗りシルエット等)は再議論しない。
     新規面(列グリフ・色ドット)は mock 転写。
6. **退化禁止**: ソート動作契約(FL-001〜004・ECO-069/070 type-primary 等)と既存テスト全緑を維持。
   検証は既存プローブ+撮影ハーネス(tools/ViewPrism2.CaptureHarness・M-CAPTURE-HARNESS-052)の
   R7 並置を再利用。

## §5 影響 BOM

- src: ImageTabView.axaml(並び替え Popup+ツールバー右クラスタ)・WorkTabView.axaml(同型面)・
  SortOptionVM(kind 配色露出が要る場合・ListColumnModel.cs)・Components.axaml(専用クラス追加時。
  共有クラス改変は他面波及のため原則回避)。
- tests: 視覚プローブ(headless)追加+既存ソート系テスト維持。R7=captures 並置(SORT-menu/TB-grid/
  LIST-sorted/GRID-sorted)。
- BOM: E-UI-BROWSE-022 acceptance へ VC-FL-1〜4 参照追記・M-UI-WORK-033 の sort 注記追随(M4)。
- CP: 転写完了時に CP-UI-G6 系へ観点刻印(accept 時)。

## §6 残ゲート

- gate①(CAD 裁定): **不要**(CAD 改版は maintainer 納品済み・裁定候補①=候補並び順は実装一致で解消)。
- gate②(golden): **必要**。基準= VC-FL-1 ①〜⑥・VC-FL-2 ①〜⑤の SORT-menu.png/TB-grid.png 並置突合+
  VC-FL-3/4 突合結果(乖離ゼロ or 分類済み差分一覧)+未ソート状態(バッジ「なし」・チップなし)+
  リスト⇄アイコン切替でメニューが閉じソート状態共有(既存契約の実測再確認)。
  Avalonia 制約(影・寸法)による近似が出た場合は R7 で分類し golden 裁定へ。
- 裁定事項が発生したら本 ECO へ記録し、CAD への read-across 依頼メモ
  (実機スクリーンショットは `artifacts/` 経由=SRC-009 方式)を作成。
