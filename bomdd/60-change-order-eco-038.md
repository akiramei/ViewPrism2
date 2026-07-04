# Change Order — ECO-038(起票・staged): 作業タブ 画像一覧のグリッド/リスト切替が本体表示に反映されない

> maintainer 報告(2026-07-04)の実害是正。起票時に工程診断(mock/UI-IR/BOM/実装 — ECO-025 retro 規律)を実施済み。

## 1. 症状(maintainer 報告・2026-07-04)

- 作業タブ 中央ツールバーの segmented ボタン(グリッド/リスト)を押下しても、
  **本体一覧の表示形式(グリッド⇔リスト)が切り替わらない**。
- コード読解からの予測(実機確認は golden 時): ボタンの active 状態(IsGrid/IsList)は切り替わる・
  スペース切替や再読込を挟むと押下済みのレイアウトが**遅延反映**される(§3 マスキング)。

## 2. 工程診断 — 欠陥は実装層に局在(CAD/BOM 改訂不要)

| 工程 | 判定 | 根拠 |
|---|---|---|
| CAD(ViewPrismUI) | **健全** | `docs/screens/work_tab.md` L81「グリッド / リスト切替・ソート」・L14「画像タブと同一部品・同一意味論」— 切替は設計原器で確定済み |
| BOM | **健全** | E-UI-WORKSPACE-043「中央は現スペースの画像をグリッド/リストで閲覧」(30-ebom) |
| 実装 | **欠陥** | WorkTabViewModel の派生プロパティ通知漏れ(§3) |

- FL-001(グリッド/リスト間の列・ソート共有 — ECO-025 で未確定のまま後続 ECO 扱い)とは**無関係**。
  切替そのものは CAD 確定済みで、本件は純粋な実装欠陥(欠陥是正 ECO)。

## 3. 切り分け済みの事実(コード読解で確定・実測プローブ未)

- 本体の表示条件は `ShowBrowseGrid`/`ShowBrowseList` にバインド
  (WorkTabView.axaml 657 グリッド / 706 リスト)。
- ところが切替コマンドの通知経路 `NotifyLayout()`(WorkTabViewModel.cs 1223–1229)は
  **IsGrid/IsList/ShowGrid/ShowList の 4 つしか通知しない** — XAML が実際に見ている
  `ShowBrowseGrid`/`ShowBrowseList`(= `ShowGrid && !ShowSearchResults` の派生)が再評価されず、
  本体 IsVisible が不変のまま。
- `ShowBrowse*` は Recompute()(375–376)とモード変更(703–704)では通知される
  → スペース切替・再読込で押下済みレイアウトが遅延反映= **潜伏のマスキング**。
- 対照(read-across): 画像タブ ImageTabViewModel.SetGrid/SetList(1256/1259)は
  `OnPropertyChanged(string.Empty)` 全通知(CR-6)のため健全 = **作業タブ固有**。
- 混入時期(履歴で確定): `81500a8`(2026-06-29・ECO-020/021 作業タブ導入)で
  VM/XAML 同時に導入 = **導入時から潜伏**。ECO-020/021 golden は切替の即時反映を
  明示観点にしていなかった(または再読込マスキングで見逃し)。

## 4. 是正方針(案 — 着手時に確定)

- 最小是正: `NotifyLayout()` に `ShowBrowseGrid`/`ShowBrowseList` の通知 2 行を追加。
- 代替: 画像タブ CR-6 と同型の `OnPropertyChanged(string.Empty)` 全通知へ寄せる
  (切替は低頻度操作・全再評価コストは小 — 実装時に裁定)。
- 回帰: headless テストで SetList → ShowBrowseList==true+PropertyChanged 発火を検証
  (ECO-037 のプローブ実測と同じ規律 — 「コード読解で確定」を実測で裏取りしてから是正)。
- 再発防止: 作業タブ golden CP へ「グリッド⇔リスト切替の即時反映」観点を明記。
  教訓= **派生プロパティ連鎖(computed chain)の通知漏れ** — 手書き通知リストは
  派生の追加(ShowBrowse* は検索結果導入時の派生)に追随しない。ECO-037
  (条件付き IsVisible の裏面崩壊)の VM 版 read-across。

## 5. 影響 BOM

- impacted: E-UI-WORKSPACE-043(surface)/ M-UI-WORKSPACE-029(WorkTabViewModel.cs)。
- CAD・E-BOM 改訂なし(§2 診断どおり実装層に局在)。オラクル影響なし(view 層+VM 通知のみ)。

## 6. 残ゲート

1. 是正実施(§4 の裁定→最小 diff)
2. 機械受入: build 0 / Tests / Oracle / validate_bom 0 error
3. golden(maintainer 実機): グリッド⇔リスト押下で**即時**切替(往復)+active 状態一致
4. クローズ時: CP 観点明記(§4 再発防止)+register status 更新
