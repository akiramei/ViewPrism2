# ECO-114: モード開始/終了の応答劣化 — 母集合不変なのに全再評価+全再構築(26 万件で顕在化)

- 起票日: 2026-07-19
- 報告者: maintainer(実機観測)
- baseline: main `489b03f`
- 種別: 不具合是正候補(性能・実装層。視覚/意味論不変)

## §1 症状

26 万画像のビューで、タグ編集モード・整理モード等の**開始/終了に時間が掛かる**
(2026-07-19・maintainer 実機)。ECO-113(選択クリック)のクローズ直後に、同じ 26 万件
ビューの**別経路**として観測された。ECO-113 教訓 1「規模系 NFR は経路ごとに独立して破れる —
診断時は同規模に触る操作経路(クリック/モード遷移/…)を棚卸しすべき」が予見していた経路。

## §2 工程診断(R2)

| 工程 | 判定 | 根拠 |
| --- | --- | --- |
| CAD(ViewPrismUI) | **健全(非該当)** | モード遷移の視覚契約(出し分け・排他隠し)は確定済みで、性能は NFR 層。モード開始/終了が母集合を変えないことは仕様上自明 |
| BOM | **概ね健全・観点欠落** | グリッド⇔リスト切替には「Items を作り直さない」契約(ECO-026/#2)が既にあるが、**モード遷移**には同種の契約が未宣言 → fix 時に invariant 追記 |
| 実装 | **逸脱確定** | §3.1。全モードコマンドが無差別に全面 Recompute を呼ぶ |

結論: **実装層の性能欠陥**。裁定は不要。

## §3 切り分け済みの事実

### 3.1 確定(起票時実測・コード証拠)

全モードコマンド(ToggleEdit/ToggleOrganize/ToggleWork/EnterDelete/ExitDelete/
EnterFileOps/ExitFileOps)は末尾で `Recompute()` を呼ぶ。26 万件ビューではモード遷移 1 回で
([ImageTabViewModel.cs](../src/ViewPrism2.App/ViewModels/ImageTabViewModel.cs) Recompute):

1. **母集合の全再評価**(:1216)= `ViewMatched(fullPath)` — 全 26 万件の ToImageWithTags 生成+条件評価。
2. **チップ件数の全再計算**(:1219)= 子ノードごとに `ViewMatched` — ルートでは matched=26 万件が
   母集合のため子チップ数×26 万件評価(未分類モードは孫分まで=:1232)。
3. **全件ソート**(:1220)= `SortFiles`。
4. **26 万個の ImageItemVM 全再構築**(BuildItemsFromMatched)— セル構築(BuildCells)・
   タグドットのブラシ生成・サイズ/日付文字列整形を全件やり直し。
5. **ObservableCollection へ 26 万回 Add**= Clear+逐次 Add の CollectionChanged 通知の嵐
   (仮想化されていても通知処理は件数分)。

一方、**モード開始/終了で母集合(表示中の画像集合・チップ・パンくず・件数・ソート)は
1 件も変わらない**。変わるのは per-item のモード依存フラグ(Selectable/タグドット表示/白✓)と
選択マーカーのクリア、パネル/ツールバー状態だけ=1〜3 は純粋な無駄、4〜5 は在庫 Items の
その場更新で足りる仕事。

- **混入**: `6f7b4f9`(2026-06-18・M3a 初版)= モードトグル→全面 Recompute の結合は導入時から。
  ECO-113 と同根(潜伏約 1 ヶ月・通常規模では体感不能のマスキングも同一)。
- **検出限界どおりの出現**: ECO-113 の計器(ContextEnumerationCount)は AllLoadedImagesInContext
  経由のみ観測。Recompute は ViewMatched を直接呼ぶため観測外(ECO-113 §7.4 所見 4 で宣言済み)。
- **read-across(同型)**: WorkTabViewModel のモードトグル(ToggleEdit:942/ToggleOrganize:1026/
  EnterDelete:1291/ExitDelete:1302 等)も同様に全面 Recompute(規模は workspace 件数)。

### 3.2 疑い(未検証)

- 5 ブロックの寄与割合(fix 時プローブ/実測で確定)。構造上の筆頭疑いは 1+2(条件評価×(1+子数))と
  4+5(26 万 VM 再構築+通知)。

## §4 是正方針(案・着手時確定)

### 案A(推奨): モード遷移専用の軽量経路(母集合パイプラインをスキップ)

モードコマンドは全面 `Recompute()` でなく **ApplyModeTransition()** を呼ぶ:
- スキップ: 軸/チップ/パンくず/件数/列/母集合評価/ソート/Items 再構築(全て母集合不変で不要)。
- 実施: 在庫 Items の**その場フラグ更新**(Selectable=inSelect・タグドット表示=!inSelect・
  白✓=fileOps・選択マーカークリア)+BuildContextPanels(空選択)+ColumnPickerOpen 閉+全通知。
- ImageItemVM: Selectable/HasTagDots を観測可能化し、**タグドットは常時構築**(モード中構築の
  アイテムがモード離脱後にドット無しになる欠落を防ぐ)。ブラシは色キーでキャッシュし
  26 万×3 個のブラシ再生成も併せて解消。
- 残存計算量の明記(ECO-113 教訓 3): モード遷移は **O(表示件数) の単純フラグパス 1 本**が残る
  (アロケーションなし・評価/ソート/再構築なし)。ゼロにする案(モード依存視覚を VM レベル
  プロパティへの XAML バインドに変える)は全セルテンプレートの改変=視覚退行リスクが高く、
  フラグパスで実測十分なら採らない(fix 時に実測で判断)。
- 検査(ECO-058 方式・固定時間閾値なし): **Items の同一インスタンス維持**(モード遷移前後で
  ReferenceEquals=再構築されていない構造証拠)+フラグ/マーカーの正しさ+チップ・件数不変+
  既存モード系テスト全緑(排他/出し分け/ECO-110 アンカー/ECO-112/113)。
- WorkTab read-across を同時是正(ECO-113 と同じ対称方針)。

### 案B(最小): チップ件数再計算(ブロック 2)だけ current matched キャッシュ化

1・3〜5 が残るため 26 万件では依然遅い。推奨しない(真因=「母集合不変の遷移が母集合
パイプラインを通る」構造が残る)。

## §5 影響 BOM(案A 見込み)

- **src**: ImageTabViewModel(モードコマンド 7 本の遷移経路+ApplyModeTransition 新設+ドット常時構築)
  +ImageItemVM(Selectable/HasTagDots 観測可能化+モード更新メソッド)+WorkTabViewModel(同型)。
  視覚・意味論は不変。
- **tests**: 構造 probe(Items インスタンス同一性+フラグ遷移+チップ不変)+既存モード系全緑維持。
- **ebom**: E-UI-BROWSE-022/E-UI-MODE-041 へ invariant 追記(モード遷移は母集合パイプラインを
  通らない=ECO-026/#2 のモード遷移版)(fix 時)。
- **CP**: CP-UI-G1 へ観点刻印(accept 時)。

## §6 残ゲート

- gate①(裁定): **不要**(実装層確定・視覚/意味論不変)。
- gate②(golden): 必要 — maintainer 実機で 26 万件ビューのモード開始/終了(タグ編集・整理・
  作業・削除・ファイル操作)の体感確認+モード中/離脱後の視覚(ドット/チェック/バッジ)不変。
