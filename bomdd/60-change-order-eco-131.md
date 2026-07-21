# ECO-131 — クロスタブ状態不整合 — 作業タブの状態変更が画像タブ母集合を無効化しない(implemented)

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

- **gate①(裁定)**: 不要(実装の構造欠陥=CAD 未定義ではない)。
- **gate②(golden)**: 是正完了・§9 に合格基準を提示。**ECO-128 と合同**(復元→pending がクロスタブでも
  正しく見える状態で両 ECO を同時検収)。

## 8. 実施記録(2026-07-21 fix)

- **是正の裁定= 案A(対称化)**: 作業タブ(=唯一のクロスタブ status 変更源。タグタブは status を
  変えず、画像タブ自身の操作は自前で `ReloadImagesAsync` 済み)への入場で content-stale を立て、
  画像タブ復帰で母集合を再取得する。イベント購読(案B)より最小で、入場ベースは「mutation の取りこぼし
  不能」で堅牢(delete/restore/purge/empty/merge/undo-merge を保守的に全捕捉)。
- **R5(プローブ先行)**: 新設 `ImageTabViewModel.RefreshContentAsync` を**現行クロスタブ経路
  (`ReloadTagCatalogAsync`=母集合非取得)で仮実装**し、CpUiG1CrossTabRefreshTests 3 本
  (①外部 delete→restore→pending が母集合鮮度回復で反映 ②外部 delete で normal 母集合から外れる
  ③シェル配線=作業タブ訪問→画像タブ復帰で反映)が**是正前赤 3/3** を実測 → 母集合再読込
  (`ReloadImagesAsync`+`Trash.RefreshCountAsync`+`Recompute`= 画像タブ自身の裁定後経路
  `OpenPendingReview` と同一)へ是正して緑転。
- **是正**: ①`RefreshContentAsync` 新設(母集合再読込・選択/ナビ/モード保持)②シェル
  `OnSelectedTabIndexChanged` を対称化(value==2 で content-stale・value==1 で消費して
  `RefreshContentAsync`・fire-and-forget タスクは観測用 `ImagesRefreshInFlight` に保持)。
  ③予防 lint 追随= ECO-125 Recompute 台帳に `RefreshContentAsync`(分類 A)を登録
  (新規 Recompute 結合を台帳 lint が検出=設計どおり作動)。
- **R8(セルフレビュー・fresh context 独立)**: fix diff を独立 subagent でレビュー。所見=
  **スコープ内 1**(`RefreshContentAsync` の fire-and-forget が `ReloadImagesAsync` の await 中の
  コレクション切替と競合し、別コレクション母集合〔A-normal/B-pending の torn 代入〕で上書きし得る=
  ECO-131 が新たに開いた競合。既存 `ReloadImagesAsync` 呼出は全てモーダル閉後=非対話で無害だった)
  →**本 ECO 内でプローブ先行是正**: `ReloadImagesAsync` に世代/コレクション ガード(`LoadContentAsync`
  と同規律)を追加=母集合をローカルへ取得し、世代・コレクション不変時だけ確定。TCS ゲート
  リポジトリの決定論プローブ(CpUiG1CollectionScopeTests `ECO131_RefreshContentのawait中…`)で
  **ガード無効時=赤・有効時=緑**を実測。共有 `ReloadImagesAsync` の他呼出(scan Completed 等)も
  同ガードで strict に改善。**スコープ外 2**(①作業タブ訪問単位の無条件再読込=26 万件のタブ往復
  コスト〔精密化=作業タブ StatusChanged イベント駆動・別 ECO 候補〕②view 軸で母集合減少時の
  0 件 value ノード残存=`OpenPendingReview`/`OpenRepair` と同一の先在共有特性)→ 51-cheat-log 記帳。
  view 軸の graph 再構築漏れ懸念は独立検証で否定(status のみ変更なら graph 構造不変・`Recompute` が
  刷新済み `_entries` で再評価=メンバシップ変化は正しく反映・既存 `OpenPendingReview` と同経路)。
- **R7(セルフゴールデン)**: 対象外を宣言(新規/視覚変更サーフェスなし=母集合のデータ鮮度回復のみ。
  pending バッジ/グリッド描画は ECO-129 の既存サーフェス)。
- **横断規約(ECO-080)**: 新規文言なし=i18n 非該当。観測点 `ImagesRefreshInFlight` は public 読取専用
  (外部書込不可)= K-MVVM 適合。
- **機械受入(4 点)**: build 0 error/0 警告・Tests **920/920**(プローブ +4)・Oracle 109+4skip・
  validate 0/0。
- **spec/M4**: VM 内部のコヒーレンシ是正(新規ユーザー可視ルールではない)につき spec 改訂は不要と判断
  (ECO-038 型)。クロスタブ鮮度の観点は accept 時に CP-UI-G1 へ刻印する。
- **diff 規模**: src 2 ファイル(MainWindowViewModel シェル配線+ImageTabViewModel の RefreshContentAsync
  +ReloadImagesAsync ガード)・tests 新規 1 クラス(CrossTabRefresh 3 本)+CollectionScope 競合プローブ 1 本
  +Recompute 台帳 1 行。

## 9. 停止点= golden 合格基準(gate②・実機・ECO-128 と合同)

1. **クロスタブ削除の反映**: 作業タブで画像を削除(ゴミ箱へ)→**画像タブへ切り替えると、その画像が
   一覧から消える**(旧: normal のまま残りタグ編集可だった)。
2. **クロスタブ復元→未裁定の反映**(ECO-128 と合同): 作業タブで復元 →画像タブへ切替 → その画像が
   **未裁定バッジ付き(pending)**で現れ、⋯「未裁定の画像」ダイアログとグリッドが**一致**する
   (旧: グリッドは normal・ダイアログは pending で食い違い)。
3. **コレクション切替の安全**: 大きいコレクションで作業タブ→画像タブ復帰の直後に別コレクションへ
   素早く切り替えても、前コレクションの母集合が混入しない(世代ガード)。
4. **回帰**: 画像タブ単独のゴミ箱削除/復元(従来どおり即時反映)・裁定 4 操作・タグタブ変更の反映・
   スキャン・26 万件のタブ往復体感。

合格なら `/eco-accept eco-131`(続けて `/eco-accept eco-128`)を指示してください。
不合格所見(GF-*)は本 ECO の手順 1 から。

## 7. 起票時の申し送り

- **golden は ECO-128 と合同**(maintainer 裁定 2026-07-21): 復元→pending がクロスタブでも正しく見える
  状態で両 ECO を同時に検収する。ECO-128 の accept は本 ECO のクローズまで保留。
- **R7/R8 適用**: src fix のため R8(独立レビュー)必須。視覚変更なしなら R7 は対象外宣言。
