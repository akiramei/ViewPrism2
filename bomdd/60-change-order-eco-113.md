# ECO-113: 選択系モードのクリック応答劣化 — 選択トグル経路の母集合再評価(26 万件で顕在化)

- 起票日: 2026-07-19
- 報告者: maintainer(実機観測)
- baseline: main `872d33c`
- 種別: 不具合是正候補(性能・実装層)

## §1 症状

タグ編集モード・ファイル操作モードで画像を選択(クリック/Ctrl クリック)すると
レスポンスが悪い。**26 万画像のビュー**で観測(2026-07-19・maintainer 実機)。

報告者の違和感が診断の核心を突いている: 選択は「表示されている画像にチェックが付く/
番号バッジが付くだけ」の視覚トグルであり、**画像の総件数に比例するコストが掛かるのは
構造がおかしい**。期待される計算量=選択集合サイズ(数件)に比例、実際=母集合サイズ
(26 万)に比例(しかも複数回)。

## §2 工程診断(R2)

| 工程 | 判定 | 根拠 |
| --- | --- | --- |
| CAD(ViewPrismUI) | **健全(非該当)** | 選択は視覚トグルのみの契約(image_tab.md 選択系モード共通)。mock はブラウザ実装で軽量。性能はアプリ NFR 層で CAD の管掌外 |
| BOM | **概ね健全・観点欠落** | REQ-041「1 万件で操作可能」+E-UI-BROWSE-022 の ECO-026 系 perf invariant(#2 切替で Items 再構築しない・#3 相当=選択クリックで Items 全再構築しない)は存在。ただし「**選択操作のコストは選択集合サイズに比例(母集合サイズに比例しない)**」という計算量観点は未宣言 → fix 時に invariant 追記(§5) |
| 実装 | **逸脱確定(3 サイト)** | §3.1。選択トグル経路に母集合の全評価+全ソートが 2 回入っている |

結論: **実装層の性能欠陥**。裁定は不要です。

## §3 切り分け済みの事実

### 3.1 確定(起票時実測・コード証拠)

選択クリック 1 回([ImageTabViewModel.cs](../src/ViewPrism2.App/ViewModels/ImageTabViewModel.cs)
`HandleItemClick`→`ToggleSelect`)で走る母集合規模の処理:

1. **`ToggleSelect`(:1964)**: 先頭で `AllLoadedImagesInContext().Select(...).ToList()` を
   **無条件実行**。この結果は **shift 分岐でしか使わない**のに、plain/Ctrl クリックでも毎回走る。
   混入= `6f7b4f9`(2026-06-18・M3a 初版)=**導入時から潜伏 約 1 ヶ月**。
2. **`AllLoadedImagesInContext`(:724)** はビュー軸で `ViewMatched`(:780)+`SortFiles`(:618)。
   `ViewMatched` は **全 `_entries`(26 万)の `ToImageWithTags()` 生成+条件評価**を毎回実行し、
   `SortFiles` は 26 万件の整列(SortKey 事前計算=ECO-025 でも O(n log n) は残る)。
3. **`RefreshSelectionMarkers`(:1361)**: `Items` **全件(26 万 VM)ループ**+選択項目ごとの
   `_selected.IndexOf`。混入= `e39d68a`(2026-06-29 の perf 是正)— Items 全再構築は止めたが
   O(母集合) の走査は残った(是正の残余)。
4. **`BuildContextPanels`(:1383・RefreshSelectionMarkers から毎回呼出)**: 先頭で
   `AllLoadedImagesInContext().Where(選択)` = **2 回目の全評価+全ソート**。選択エントリの
   解決に表示順は不要(共通タグ計算は順序非依存)。

つまり **クリック 1 回= 全件条件評価×2+全件ソート×2+全 VM ループ×1**。
選択集合サイズには依存せず母集合サイズに比例=報告者の違和感どおりの構造。

- **マスキング要因**: 通常規模(数百〜数千件)では体感不能。26 万件級は ECO-064(起動)・
  ECO-062(類似検索の全域走査抑止)で登場した規模で、選択経路は両 ECO のスコープ外だった。
- **read-across(同型・軽度)**: [WorkTabViewModel.cs](../src/ViewPrism2.App/ViewModels/WorkTabViewModel.cs)
  `ToggleSelect`(:1416)= `Items` 全 Id リスト化(ソート・条件評価はなし=軽度)/
  `RefreshSelectionMarkers`(:510)= 全件ループ。作業タブは workspace 規模が通常小さいが同一契約。

### 3.2 疑い(未検証)

- 4 サイトの寄与割合(条件評価 vs ソート vs VM ループ)。fix 時のプローブ/計測で確定する。
  構造上の筆頭疑いは ViewMatched の全件 ToImageWithTags 生成+条件評価×2。
- グリッド仮想化(ItemsRepeater)下で 26 万 VM への一括 OnPropertyChanged(string.Empty) が
  もつ描画側コスト(可視セルのみ再バインドのはずだが未計測)。

## §4 是正方針(案・着手時確定)

### 案A(推奨): 選択経路から母集合規模の処理を全撤去

1. `ToggleSelect`: 母集合リスト構築を **shift 分岐の中へ遅延移動**(plain/Ctrl はゼロコスト化)。
2. `RefreshSelectionMarkers`: **差分更新**へ — id→ImageItemVM 辞書を保持し、
   「前回選択∪今回選択(+整理マーカー変化分)」だけ `SetSelectionMarkers` を呼ぶ。
   選択順は `_selected` の id→index 辞書を 1 回構築(IndexOf の O(選択²) も併せて解消)。
3. `BuildContextPanels`: 選択エントリの解決を `EntryById` 系(id→entry 辞書)へ置換
   (全評価+全ソートを撤去。順序非依存の共通タグ計算に表示順は不要)。
4. 表示順が本当に必要な消費者(shift 範囲・OpenViewer・CopyPaths=ECO-112)は従来どおり
   **操作時のみ**母集合を引く(現行維持・変更なし)。
- 検査= ECO-058 方式(固定時間閾値を設けない構造ガード): 選択クリック 1 回で
  母集合列挙(AllLoadedImagesInContext / ViewMatched)が**呼ばれない**ことを構造 probe で pin
  +既存の選択意味論テスト(shift union・IMG-025 交差・ECO-112 出し分け)の全緑維持。
- WorkTab read-across は同一 ECO 内で同時是正(片面是正の面間非対称を作らない=§8.2)。

### 案B(最小): ToggleSelect の遅延移動のみ

plain/Ctrl クリックは救うが、BuildContextPanels の全評価×1 と全 VM ループが残る=
26 万件では依然比例コスト。推奨しない(真因構造=「選択経路の母集合依存」が残る)。

## §5 影響 BOM(案A 見込み)

- **src**: ImageTabViewModel(ToggleSelect/RefreshSelectionMarkers/BuildContextPanels)+
  WorkTabViewModel(同型 2 サイト)。視覚・意味論は不変(純粋な計算量是正)。
- **tests**: 構造 probe(選択クリックが母集合列挙を呼ばない)+既存選択系テスト全緑維持。
- **ebom**: E-UI-BROWSE-022 invariant 追記「選択操作のコストは選択集合サイズに比例
  (母集合サイズに比例しない)」(fix 時)。
- **CP**: CP-UI-G1/CP-NFR-026 系へ観点刻印(accept 時)。

## §6 残ゲート

- gate①(裁定): **不要**(実装層確定・視覚/意味論不変)。
- gate②(golden): 必要 — maintainer 実機で 26 万件ビューの選択レスポンス改善を体感確認
  (タグ編集/ファイル操作の両モード・plain/Ctrl/shift)。

## §7 実施記録(2026-07-19・/eco-fix)

### 7.1 プローブ先行(R5)と赤の実測

- 計器= `ImageTabViewModel.ContextEnumerationCount`(母集合列挙の累計回数)を先行追加
  (ECO-112 様式=観測に計器が必要な欠陥は計器先行+挙動赤。Core シームは全 sealed で外部観測不能)。
- 構造プローブ 3 本(CpUiG1ImageTabSelectionTests・ECO-058 方式=固定時間閾値なし):
  ①選択クリック(plain/Ctrl)は母集合列挙を走らせない ②ファイル操作モードでも同様
  ③SHIFT 範囲だけが 1 回だけ使う(union 意味論維持込み)→ **是正前 3/3 不合格を実測**
  (①②= delta 2〔ToggleSelect+BuildContextPanels〕・③= delta 2)。既存 819 全緑=計器の無影響も実証。
- R8 所見 1 の追加プローブ(7.4): スキャン段階公開×entry 解決(是正前赤=1/823)。

### 7.2 是正の構造(案A 採用=真因構造の撤去)

- **ToggleSelect**: 母集合列挙を SHIFT 分岐内へ遅延(plain/Ctrl はゼロコスト化)。
- **RefreshSelectionMarkers**: 全 Items 走査→**差分更新**(`_markedItemIds`=前回マーカー保持∪今回対象
  のみ更新・`_itemById` 辞書・選択順は orderById 辞書=IndexOf 反復も解消)。モード遷移は必ず
  Recompute=全再構築を通るため非対象アイテムの構築時状態は常に現モードと一致(コメントで根拠残置)。
- **BuildContextPanels**: 全件評価+ソート→ `_matchedIndexById`(表示順 index 辞書)+`_entryById`
  で解決(共通タグ計算の「先頭=表示順先頭」を保存)。
- **EntryById**: 線形走査→辞書(整理トレイ/場所を開く経路も母集合非比例化)。
- インデックス維持= `_entries` 3 変異サイト(BuildEntries/ClearContentData/スキャン append)+
  Items 4 変異サイト(BuildItemsFromMatched/スキャン append/未ロード Clear/フォルダ Insert=対象外)全部。
- **WorkTab read-across**: ToggleSelect 遅延+RefreshSelectionMarkers 差分更新+Items 構築時の
  インデックス再構築(同型 2 サイト+維持 1 サイト)。

### 7.3 機械受入(2026-07-19・全緑)

- dotnet build: 0 error。`--no-incremental` フルビルドで警告 0。
- dotnet test Tests: **823/823**(既存 819+新プローブ 4)。Oracle: 109+2skip(R6 不変)。
- validate_bom: 0/0。R7= 対象外(視覚・意味論不変の計算量是正=ECO-110 前例)。

### 7.4 セルフレビュー(R8)+処置

fresh context の独立レビュー(全変異サイト grep 悉皆+フルスイート実行)。所見全数と処置:

| 所見 | 分類 | 処置 |
| --- | --- | --- |
| 1 スキャン段階公開で `_entryById` 未維持=公開直後の画像が entry 解決(場所を開く/選択パネル/整理トレイ)から無言脱落 | スコープ内欠陥 | プローブ先行(スキャン中×ファイル操作=是正前赤)→ append サイトへ 1 行+宣言コメントの維持サイト記載も是正 |
| 2 ClearContentData が `_entryById` 残置(旧コレクション 26 万件のメモリ滞留) | スコープ内(軽微) | クリア 1 行追加 |
| 3 WorkTab BuildContextPanels の `_sourceImages.Where`(O(スペース内件数))残余 | 残余明記 | 作業スペースはユーザー選別集合で 26 万件級にならない(追加は明示操作のみ)。宣言スコープ=同型 2 サイトどおり。将来スペースが大規模化したら分離起票 |
| 4 プローブは是正 4 点中 2 点(①③)の構造 pin=②差分更新・④辞書化の**性能**退行は計器観測外 | 残余明記(検出限界の宣言) | 意味論は既存テストが差分経路で全検証(選択順/SHIFT/整理マーカー/IMG-025/ECO-112)。②④の性能 pin には呼び出し回数計器の増設が必要=過剰計器化を避け本欄に検出限界を刻む(ECO-107 教訓「lint 限界宣言には掃射手段紐づけ」の適用=再発時は ContextEnumerationCount と同型の計器を該当サイトへ) |
| 5〜9 インデックス同期悉皆/マーカー会計/挙動保存(未分類・IMG-025・スキャン取込順)/WorkTab 対称性/計器 | 問題なし確認 | 全数確認済み(レビュー記録) |
| 10 CpUiG6SaveBarTests 間欠不合格(レビュー実行時 1/1・本 ECO 無関係) | R3(既知 flaky) | 51-cheat-log へ発現追加(4 例目・一括起票候補の N 蓄積) |

未処置のスコープ内所見= **0**。
