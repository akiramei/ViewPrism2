# ECO-131 — クロスタブ状態不整合 — 作業タブの状態変更が画像タブ母集合を無効化しない(staged)

- 起票日: 2026-07-21
- 報告者: maintainer 実機(ECO-128 gate② 実施中に発見・GF-128-01 由来)
- 種別: 不具合(UI/VM 層のクロスタブ・キャッシュ不整合。実装逸脱=構造欠陥)
- baseline: ViewPrism2 main `cefa69c`
- 関連: **ECO-128(復元→pending が顕在化させた=一緒に golden する方針)** / ECO-129(pending 意味論) /
  GF-129-01(同経路の母集合鮮度=`ReloadImagesAsync` の pending 再取得漏れ・単一タブ内は是正済み)

---

## 1. 症状(観測)

maintainer 実機(2026-07-21・ECO-128 gate②):

1. 作業タブで画像を削除 → ゴミ箱へ(DB は `images.status = deleted`)。
2. **画像タブへ切り替えると、その画像が normal のまま一覧に表示され、タグ編集も可能**
   (DB は deleted なのに)。
3. 作業タブで復元 → pending(DB は `status = pending, origin = restored`・ECO-128 T6')。
4. 画像タブの ⋯「未裁定の画像」ダイアログは**その画像を pending として正しく表示**する。
5. **しかし同じ画像タブのグリッドでは依然 normal 表示・タグ編集可能**。
   → 同一画面で「ダイアログ(DB 真実)」と「グリッド(古いキャッシュ)」が食い違う。

報告者の当初仮説「画像タブと作業タブで状態が二重管理されている」= **診断で否定**(§2)。

## 2. 工程診断

| 工程 | 判定 | 根拠 |
| --- | --- | --- |
| DB / 状態モデル | **健全(二重管理ではない)** | `images.status` は単一列=単一の真実。作業タブ削除=`TrashService.DeleteToTrashAsync`(normal→deleted)・復元=`RestoreAsync`(deleted→pending・ECO-128)とも DB を正しく更新。⋯ダイアログが DB 直読みで pending を正しく表示することが単一真実の証拠 |
| CAD | 対象外(見込み) | 視覚仕様の欠陥ではなく、母集合鮮度の VM 配線欠陥。CAD 追随は不要見込み(fix 時確認) |
| 実装(VM/シェル) | **欠陥(クロスタブ・キャッシュ不整合)** | 画像タブの母集合 `_allNormal`/`_allPending` が、作業タブの状態変更で無効化されない。タブ切替の再読込は `_imagesTabStale` が**タグタブ変更でしか立たず**、かつ発火しても `ReloadTagCatalogAsync` は**母集合を再取得しない** |

**結論: UI/VM 層の構造欠陥。実装追随(母集合無効化の配線)で是正。**

## 3. 切り分け済みの事実

### 確定(証拠あり)

1. **タブ切替の再読込は条件付き+母集合を触らない**:
   [MainWindowViewModel.cs:171](../src/ViewPrism2.App/ViewModels/MainWindowViewModel.cs)
   画像タブ(value==1)への切替は **`_imagesTabStale` が真のときだけ** `ReloadTagCatalogAsync` を呼ぶ。
   `_imagesTabStale` を立てるのは [`TagsTab.DataChanged`](../src/ViewPrism2.App/ViewModels/MainWindowViewModel.cs) の 1 箇所のみ(:63)。
   **作業タブの状態変更では立たない**(WorkTabViewModel に `DataChanged` イベント自体が存在しない=grep 0 件)。
2. **再読込しても母集合は古いまま**:
   [`ReloadTagCatalogAsync`](../src/ViewPrism2.App/ViewModels/ImageTabViewModel.cs:526) は
   `_tagById`/`_allViews`/画像タグ/`BuildEntries` を再取得するが、**`_allNormal`/`_allPending`
   (status 母集合)は再取得しない**。母集合は `LoadContentAsync`/`ReloadImagesAsync` でしか更新されない。
3. **非対称(逆方向は健全)**:
   作業タブ(value==2)への切替は**無条件で** [`WorkTab.RefreshAsync()`](../src/ViewPrism2.App/ViewModels/MainWindowViewModel.cs:169)
   を呼ぶ(f211fa9=作業タブ導入時から)。よって画像タブ→作業タブは鮮度が保たれる。
   画像タブ側に対応する無条件無効化が無いのが非対称の実体。
4. **単一タブ内は既に鮮度が保たれる**:
   画像タブ自身のゴミ箱経路(ImageTabTrashViewModel の復元)は `ReloadImagesAsync`(GF-129-01 で
   `_allPending` 再取得込み)を呼ぶため正しく反映。**壊れているのはクロスタブ経路のみ**。
5. **先在欠陥・ECO-128 が顕在化**:
   母集合無効化の欠落は作業タブ導入(f211fa9)/`_imagesTabStale` 導入(c103967)以来の構造。
   旧仕様では作業タブ復元→normal が画像タブの「古い normal」と偶然一致して**マスク**されていた。
   ECO-128 の復元→pending が初めて食い違いを可視化(ECO-087 教訓「マスクされた欠陥の顕在化」と同型)。

### 未検証(疑い)

- **影響は復元に限らない全状態変更**(疑い・強): 同じ機序で作業タブの delete/exclude/merge/完全削除/
  pending 裁定も画像タブ母集合に反映されないはず。症状 2(削除後の stale normal)が既に復元非依存で成立
  している=構造的裏付けあり。fix 時に各経路を probe で網羅。
- **画像タブ→作業タブの一部経路**(疑い・弱): value==2 は無条件 `RefreshAsync` のため健全な見込みだが、
  画像タブでの裁定/削除直後に作業タブが同一画像を保持する場合の鮮度は fix 時に確認。
- **同時可視の 2 ペイン内食い違い**: グリッドと ⋯ バッジ件数(`PendingCount => _allPending.Count`=古い)も
  食い違う可能性。fix 時に件数バッジも母集合再取得の対象に含める。

## 4. 是正方針(案・着手時確定)

**案A(推奨・対称化)**: 作業タブが value==2 で無条件 `WorkTab.RefreshAsync()` するのと対称に、
画像タブ(value==1)への切替でも**母集合の鮮度を回復**する。最小形は「他タブでの状態変更があり得る
前提で、切替時に画像タブの content(母集合)を再取得する」= `ReloadImagesAsync` 相当を切替フックに接続
(選択・ナビ・モードは保持)。過剰再読込を避けるなら `WorkTab.DataChanged`(新設)→専用 stale フラグ
→切替時に**母集合込みの再読込**(`ReloadTagCatalogAsync` は母集合を触らないため不足=別経路が要る)。

**案B(イベント駆動)**: 状態変更サービス層(TrashService/PendingReviewService 経由の mutation)を
横断イベント化し、購読側(画像タブ/作業タブ)が該当コレクションの母集合を無効化する。堅牢だが diff 大。

diff 規模(案A 最小): MainWindowViewModel の切替フック+ImageTabViewModel の母集合再読込公開 API・
プローブ(クロスタブ delete/restore/その他 mutation の反映)。視覚変更なし見込み。

**前提**: ECO-128 適用済み(本欠陥は ECO-128 と一緒に golden する方針=maintainer 裁定 2026-07-21)。

## 5. 影響 BOM

- **src**: MainWindowViewModel(切替フック)+ ImageTabViewModel(母集合再読込の公開/接続)。
  必要なら WorkTabViewModel(DataChanged 新設・案A 亜種)。
- **spec**: クロスタブ鮮度は挙動契約=§2.11.7 近傍または横断関心事として明文化(fix 時に立地判断)。
- **tests**: クロスタブ mutation → 画像タブ母集合反映の VM 統合プローブ(delete/restore/裁定の各経路)。
- **CAD**: 視覚変更なし見込み(fix 時確認)。
- **CP**: CP 刻印は accept 時(CP-UI-G1= シェルのタブ協調+母集合鮮度の観点を追加見込み)。

## 6. 残ゲート

- **gate①(裁定)**: 不要見込み(実装の構造欠陥=CAD 未定義ではない)。着手条件=なし(ECO-128 と並行可)。
- **gate②(golden)**: 是正後、**ECO-128 と一緒に**提示(クロスタブ delete/restore の画像タブ即時反映・実機)。

## 7. 起票時の申し送り

- **golden は ECO-128 と合同**(maintainer 裁定 2026-07-21): 復元→pending がクロスタブでも正しく見える
  状態で両 ECO を同時に検収する。ECO-128 の accept は本 ECO のクローズまで保留。
- **R7/R8 適用**: src fix のため R8(独立レビュー)必須。視覚変更なしなら R7 は対象外宣言。
