# Change Order — ECO-036(設計・staged): god-VM 解体系列 — 画像タブの unit 再編成(scale-02)

> **status: staged(設計凍結のみ・未着手)**。scale-01(studies/scale-01-impact-retrospective.md)が特定した
> ハブ unit **M-UI-016 = ImageTabViewModel.cs(1,765 行・305 メンバ)**を、E-BOM 品目整合の子 VM 群へ
> **段階解体**する系列 ECO の枠組み。method 実験 **scale-02** を兼ねる:
> ①対策 regime(61 §1.2 実物点検+§1.4 ハブ台帳)下の**プロスペクティブ影響分析採点**(遡及 88% との前後比較)
> ②**M-BOM unit 分割 lineage(split/supersede)の初適用**(traceability-rules の語彙を M 層で初めて使う)。

## 0. 動機(実測)

- scale-01: 実 under を持つ ECO 14/16(88%)・**M-UI-016 を 10/16 ECO で取りこぼし** = 写像の系統誤差の発生源。
- 写像欠陥は二層:
  (a) **M-UI-016 は E-UI-TAGASSIGN-029 の 1 品目宣言なのに、実装は 9 責務クラスタを吸収**
  (コード内コメントが自認: コレクション選択 REQ-053/ECO-013・view/FS 軸 M3b・ソート/表示列 ECO-025・
  整理 ECO-014・作業 ECO-017・削除 ECO-018・ゴミ箱 ECO-019・ツールバー IMG-014・タグ付与=本来の宣言)。
  (b) それら責務の E 品目(E-UI-BROWSE-022 / E-UI-AXIS-NAV-040 / E-UI-MODE-041)は **catch-all の
  M-UI-013(src/ViewPrism2.App 全域)へ粗く帰属** — 予測が M-UI-013 を指し実 diff が M-UI-016 に落ちる。
- **E-BOM は既に正しい粒度を持つ**(上記品目が実在)→ 解体は E 側再設計なしで **M 側 unit 再編成に閉じる**。

## 1. 目標構造(candidate — 各段の実物点検で確定)

| 新 unit(仮 ID) | E 品目(既存) | 移送する責務クラスタ(現 god-VM 内の実測位置) |
|---|---|---|
| M-UI-TRASHMODE-032 | E-UI-MODE-041(部分) | 削除モード(ECO-018 公開契約 §634-)+ゴミ箱ポップアップ(ECO-019 §644-・§123-) |
| M-UI-WORKMODE-033 | E-UI-MODE-041(部分) | 作業モード(ECO-017 §108-/§543-/§1540-) |
| M-UI-ORGANIZE-034 | E-UI-MODE-041+E-UI-MERGE-036 連携 | 整理モード/整理トレイ(ECO-014 §90-/§537-/§917-/§1528-) |
| M-UI-AXISNAV-035 | E-UI-AXIS-NAV-040 | FS/view 軸・チップ・パンくず(§65-/§726-791)+ソート/表示列(ECO-025 §494-535)※列系は BROWSE 帰属の可能性 — 点検で裁定 |
| M-UI-BROWSE-036 | E-UI-BROWSE-022 | コレクション選択スコープ(REQ-053 §474-)・items 構築・選択/クリック(HandleItemClick §1388) |
| M-UI-016(縮退→系譜処理) | E-UI-TAGASSIGN-029 | 編集パネル/タグ付与のみ+**薄いコンポジションルート**(モード排他の調停は E-UI-MODE-041 の中核につき TRASHMODE/WORKMODE/ORGANIZE の親 or 専用 ModeCoordinator — 第1段の点検で裁定) |

- unit 粒度規準(playbook §4.1「独立に再製造・交換でき、単独で受入できる最小単位」)に適合させる。
- M-UI-013 の ebom_refs から BROWSE/AXIS/MODE を新 unit へ**再帰属**(catch-all の解消。
  traceability-rules `reattribution-incomplete` を踏まないよう CP・trace の消費側も同期)。

## 2. lineage(新次元 — M-BOM unit 分割の初適用)

- 系列完了時: M-UI-016 に `split_by: [M-UI-TRASHMODE-032, …]` を宣言し **superseded** へ
  (縮退後のタグ付与 unit は新 ID で立てる=「旧 ID の意味が変わる」を避ける)。
- 各段では部分 split を **register 註+50 系譜**で記録(段階中の中間状態は「M-UI-016(縮退中・第N段)」)。
- ハブ台帳(52 hub_ledger)の M-UI-016 行は系譜完了時に「解体済み(ECO-036 系列)」へ更新 —
  **効果の証明は将来 ECO の遡及採点**(impact-retrospective.py の定期実行で under 集中の消失を確認)。

## 3. 段階解体系列(一括でなく 1 モード= 1 ECO)

一括解体は回帰リスクと採点粒度の両面で不利 — **各段が独立に全再認証で閉じる**系列にする:

| 段 | 切り出し | 選定理由 |
|---|---|---|
| 1(ECO-036 本体) | 削除モード+ゴミ箱ポップアップ | 公開契約 region が明確・既存 TrashViewModel(165 行)の前例・依存最少 |
| 2 | 作業モード | 排他規約以外の結合が薄い |
| 3 | 整理モード | SimilarSearch/マージ連携で最複雑 — 前2段の学習後に |
| 4 | 軸ナビ+ソート/表示列 | データフロー中枢(Refresh 系)に触れる — 単独段 |
| 5(完了) | 残余整理+lineage 確定 | M-UI-016 supersede・M-UI-013 再帰属完了・ハブ台帳更新 |

各段の共通契約: **挙動不変**(オラクル・golden の期待値を 1 行も改訂しない)。改訂が必要になったら
それ自体が停止条件(§5)。

## 4. 影響分析・受入の枠組み(各段で具体化)

- **影響あり**: ImageTabViewModel.cs・新 VM ファイル・対応 View(XAML の DataContext/バインディングパス —
  視覚出力不変だがバインディング変更は必至につき影響集合に含める)・32-mbom(unit 宣言+lineage)・
  30-ebom 再帰属・CP の unit 参照更新。
- **影響なし予測(反証可能形)**: Core/Infrastructure 全域 diff ゼロ・他画面 VM diff ゼロ・
  **全オラクル期待値/golden の改訂ゼロ**。ハブ台帳 3 unit のうち M-VIEWSVC-012/M-UI-013(App 内 View を除く)は
  段ごとに点検して根拠を書く(61 §1.4 の実運用)。
- **受入**: Tests+固定オラクル全行(回帰 0)+ **golden 再ウォークスルー(視覚不変の確認)** +
  diff 監査(宣言影響集合への閉包)。設定永続化(CaptureSettings)の**保存形式互換**は第1段の
  先行点検事項(VM 構造依存の保存キーがあると data-preservation 次元が発生 — あれば移行オラクル要)。
- **製造形態(第1段で実測して裁定)**: 設計者が「移送表」(メンバ→行き先 unit の全数対応・K-BOM 化)を
  凍結し、fresh 工場 1 体が機械的移送を実施する形を試す — forward-04 の「知識パックが裁量を吸収し cheat 0」が
  **リファクタ移送表でも再現するか**が測定点。工場リスクが高いと出たら以降は設計者適用+全再認証へ切替。

## 5. 停止条件(演出防止と巻き戻し)

- golden に視覚差分が出る/オラクル期待値の改訂が必要になる → **stop/report**(挙動保存の失敗。
  当該段を破棄しロールバック — 「解体のための仕様変更」は本系列では禁止)。
- 移送表で分類できないメンバ(責務が品目間で不可分)が出る → その実測こそ価値(E-BOM 粒度への
  フィードバック)。無理に切らず記録して段を縮小する。

## 6. scale-02 としての測定(記録先: 各段 order §5+studies/)

1. 各段の影響なし予測の under/over(プロスペクティブ)— scale-01 の遡及 88% との前後比較。
2. 移送表方式の工場 cheat 件数(forward-04 の cheat 0 が挙動保存リファクタで再現するか)。
3. 系列完了後、以降の実 ECO 数件で impact-retrospective.py を再実行 — ハブ集中の消失(長期指標)。
4. lineage 機構(split/supersede)の運用コストと、bomdd-lint(R-系)が系譜違反を検出できるかの観察。

## 7. 着手条件

- staged のまま。着手はユーザー裁定(次の ViewPrism2 実 ECO と競合しないタイミング)。
- 着手時の最初の作業 = 第1段の実物点検(§1 表の TRASH/WORK 境界・CaptureSettings 保存形式・
  ModeCoordinator 裁定)→ 影響分析凍結 → 移送表作成 → 較正(全オラクル・golden が変更前個体で
  期待プロファイル一致することの確認)→ 製造。

---

## 8. 第1段 設計凍結(2026-07-04 着手 — 実物点検の結果と裁定)

### 8.1 先行点検 3 件の結果

| 点検 | 結果 |
|---|---|
| CaptureSettings 保存形式 | **問題なし** — 保存は LastCollectionId+DisplayMode のみ(§248)。trash/delete 非依存= data-preservation 次元は発生しない(移行オラクル不要) |
| TRASH/WORK 境界 | **共有状態なし** — 作業モード(_workMode/_workTargets)とは排他リセットの相互参照のみ |
| ModeCoordinator | **第1段では作らない(裁定)** — モード排他は E-UI-MODE-041 の中核で、対象4モード中3つが未解体のホスト内。調停の切り出しは MODE 系3段が揃う後続段で行う |

### 8.2 境界の裁定(§1 表の candidate を実測で補正)

- 新 unit = **M-UI-TRASH-032**(§1 の仮称 M-UI-TRASHMODE-032 から改称 — モードフラグはホスト残置のため
  「TRASHMODE」は不正確)。実体= 新規ファイル `src/ViewPrism2.App/ViewModels/ImageTabTrashViewModel.cs`
  (既存 TrashViewModel(ECO-015 モーダル)と別物 — 名前衝突回避)。
- **子 VM が所有**(ゴミ箱フィーチャ): バッジ(_trashCount・RefreshTrashCountAsync・HasTrash/TrashCount)/
  ポップアップ全状態(TrashOpen・TrashPopupItems・_trashSel)/コマンド(Open/Close/Load/ToggleItem/
  ToggleSelectAll/RefreshSelection/RestoreSelected/PurgeSelected/EmptyTrash)/ゴミ箱移動の実行部
  (TrashService 呼び出しループ)。依存: TrashService・IImageRepository・IWindowService(確認ダイアログ)。
- **ホストに残す**: _deleteMode フラグ・EnterDelete/ExitDelete(排他+共有選択 _selected の変異)・
  選択依存の公開契約(DeleteMode/HasDeleteSelection/DeleteSelCount/CanDeleteToTrash)・
  DeleteToTrash コマンドの殻(選択 ids を子へ渡し、選択クリア+ReloadImages+Recompute の順序を保持)。
- **接続面(子→ホスト逆参照の禁止)**: 子はホスト型を参照しない。コンストラクタで関数注入 —
  `getCollectionId: Func<string?>`・`reloadImagesAsync: Func<Task>`・`recompute: Action`・
  `fmtSize: Func<long,string>`(FmtSize の重複実装を避ける)。
- TrashPopupItemVM は ImageTabSeedViewModel.cs:886 定義 — **参照のみ・移動しない**(同ファイルは diff ゼロ予測)。

### 8.3 影響分析(製造前に凍結)

**影響あり**:
| ファイル | 変更 |
|---|---|
| src/ViewPrism2.App/ViewModels/ImageTabTrashViewModel.cs | **新設**(移送表 §8.4 の全メンバ) |
| src/ViewPrism2.App/ViewModels/ImageTabViewModel.cs | 移送メンバの除去+`Trash` 子 VM プロパティ+DeleteToTrash 殻+InitializeAsync/OpenRepair の RefreshTrashCount 呼び先変更 |
| src/ViewPrism2.App/Views/ImageTabView.axaml | trash 系バインディング(実測 35 箇所)を `Trash.*` パスへ(視覚出力不変) |
| bomdd/32-mbom.yaml | M-UI-TRASH-032 宣言+M-UI-016 註(部分 split 第1段) |

**影響なし予測(反証可能)**: 上記 4 ファイル以外の**全ファイル diff ゼロ**(特に: ImageTabSeedViewModel.cs・
TrashViewModel.cs(旧モーダル)・Core/Infrastructure 全域・他画面 VM/View・tests/)。
**全オラクル期待値・golden・CP・テストの改訂ゼロ**。ハブ台帳(61 §1.4): M-UI-016=本 ECO の対象そのもの/
M-UI-013=ImageTabView.axaml が対象内(宣言済み)・他の App 内ファイルは diff ゼロ/M-VIEWSVC-012=影響なし
(根拠: UI 層内の再配置のみ・Core サービスの呼び出しシグネチャ不変)。

**結果分類**: テスト/オラクルの失敗= regression(挙動保存の失敗→§5 停止条件)/ 影響 4 ファイル外への
diff= unnecessary modification / golden 視覚差分= 停止条件(ロールバック)。

### 8.4 移送表(工場入力 — メンバ→行き先の全数対応)

ImageTabViewModel.cs 内の trash 系 25 メンバの行き先(行番号は変更前個体):
- **子へ移送**: _trashCount(121)・_trashSel(126)/ HasTrash・TrashCount(641-642)/
  TrashOpen・TrashPopupItems・TrashPopupCount・HasTrashItems・TrashPopupEmpty・HasTrashSel・
  TrashSelCount・TrashSelCountLabel・TrashSelectAllLabel・CanRestoreTrash・CanPurgeTrash(645-656)/
  OpenTrash(1195)・CloseTrash(1206)・LoadTrashItemsAsync(1214)・ToggleTrashItem(1231)・
  ToggleTrashSelectAll(1239)・RefreshTrashSelection(1246)・RestoreSelectedTrash(1253)・
  PurgeSelectedTrash(1266)・EmptyTrash(1282)・RefreshTrashCountAsync(1607)・
  DeleteToTrash の実行ループ(1598-1599 → 子の MoveToTrashAsync(ids))
- **ホスト残置**: _deleteMode(120)・DeleteMode/HasDeleteSelection/DeleteSelCount/CanDeleteToTrash
  (635-639)・EnterDelete(1573)・ExitDelete(1584)・DeleteToTrash 殻(1593: ids 取得→子呼び出し→
  選択クリア→ReloadImages→子.RefreshCount→Recompute の**順序を厳密保持**)
- **呼び先変更**: InitializeAsync:217・OpenRepair:1330 の RefreshTrashCountAsync → Trash.RefreshCountAsync
- 通知規律: 子は ObservableObject。ホストの Recompute の `OnPropertyChanged(string.Empty)` は子には
  波及しないため、子は自メンバ変更時に自前で OnPropertyChanged(string.Empty) 相当を発行する
  (現行の RefreshTrashSelection:1250 と同型 — 挙動保存の要)。

### 8.5 M-BOM 宣言(第1段)

M-UI-TRASH-032: ebom_refs [E-UI-MODE-041(メンテナンス入口のゴミ箱面 — 部分・複数 unit 参照は正常形)]、
依存品目= E-TRASH-038(状態遷移の所有は M-TRASH-026 のまま)。acceptance_refs [CP-L1-SMOKE]+
note(popup 挙動の受入= golden(視覚)+Core 遷移 CP(CP-TRASH-020/001/021/022)で被覆 — UI 専用 CP は
新設しない=挙動不変 ECO で検査資産を増やさない)。M-UI-016 に partial-split 註(第1段・完了時 supersede)。

### 8.6 較正・受入(第1段)

- 較正: 変更前個体で `dotnet test`(Tests+Oracle 全数)緑を確認してから製造(実行記録は §9)。
- 受入: build 0 エラー・全テスト/オラクル**無改訂で**全緑・workspace lint error/warn 0・
  diff 監査(影響 4 ファイル閉包)・**golden 再ウォークスルー(maintainer 実機・視覚不変)= 人間ゲート**
  (機械受入全緑の後、register を implemented にし golden 待ちを明記)。

## 9. 第1段 記録(2026-07-04 実施 — 機械受入完了・golden 待ち)

| 測定 | 結果 |
|---|---|
| 較正(変更前個体) | Tests **526/526**・Oracle **100/102(skip 2 = 既知の明示実行系)** 全緑 |
| 工場(fresh sonnet・移送表方式) | 自己受入全緑・**移送表 25 メンバ全数実施**・逸脱 2(下記) |
| 設計者受入(独立) | build 0 エラー・Tests **526/526**・Oracle **100/102** — **期待値/テスト/golden 無改訂** |
| diff 監査 | コード変更= 宣言 3 ファイルに閉包(ImageTabViewModel.cs 194 行 / ImageTabView.axaml 44 行 / 新 VM)。ImageTabSeedViewModel.cs・TrashViewModel.cs・Core/Infrastructure・tests/ = diff ゼロ |
| workspace lint | error/warn 0 維持 |
| 工場ずる | **6 件(blocker 1 / friction 3 / minor 2)** — 全裁定 accept(51 参照・個票= reports/eco036-stage1-cheat-report.md) |

### scale-02 測定(第1段分)

- **ファイルレベルの影響予測: 的中**(影響 4 ファイル・影響なし全域 diff ゼロ)。ただし「tests diff ゼロ」は
  工場の blocker 対応(後方互換委譲)**によって**成立した — 予測が正しかったのではなく救済された。
- **設計凍結レベルの under = 3 件**: ①接続面 4→6 関数(closeMoreMenu / resolveAbsolutePath — 移送対象の
  副作用と private 依存の見落とし)②③**既存テスト 3 ファイル 84 箇所がホストの trash 公開契約に直結合**
  (E36S1-003 blocker)。帰属= **設計者の 61 §1.2 実行不完全**(移送メンバの全参照 grep を src/ に限定し
  tests/ を省いた。§1.2 の「全参照」は tests を除外していない= regime は正しく実行が不完全)。
  → method 還元候補: 61 §1.2 に「参照 grep は tests/ を含む」を明記+リファクタ系 ECO では
  「公開契約の消費者一覧(XAML/テスト/他 VM)」を移送表の必須列にする。
- **移送表方式の工場成績**: 表で与えた範囲の裁量ゼロ実施は forward-04 同様に成立。ずるは全て
  「表が与えなかった接続面」に集中= **移送表の完全性が品質を決める**(表の書き方が K-BOM 相当)。
- 経過措置の債務: ホストの後方互換委譲(テスト結合専用・XAML 非使用)— 除去は後続段でテスト移行
  (test-only 変更)と同時に行う。

### 治具/環境の副観測(H 系列)

- **`dotnet test`(MTP 接続)が非対話環境でハング**(50 分無進行・CPU ほぼゼロを実測)。テスト自体は
  健全(MTP 実行ファイル直接起動なら 526 本 2.5 秒)。受入手順は**直接起動を正**とする:
  `dotnet tests/<proj>/bin/Debug/net10.0/<proj>.dll`。残存 dotnet デーモンが直接起動も阻害する事例を
  1 回観測(プロセス kill で解消)— 受入前に残存プロセスを確認する。

### golden 第1回(2026-07-04・maintainer 実機)— 所見 2 件 → 設計者直接修正 → 再確認待ち

- 削除モード= 合格。所見 2 件(バッジ初期非表示/⋯メニューが閉じない)— **共通根因= 通知トポロジー**
  (god-VM の string.Empty 一括通知が分割の隠れた結合面。詳細と是正= 51 G-E36S1-1/2)。
  Tests 526 は通知漏れに構造的に盲目(プロパティ直読)= **golden が通知面の唯一の検査器**という
  受入設計上の発見。是正後の機械受入= Tests 526/526・Oracle 100/102 全緑(期待値無改訂)。
  scale-02 採点へ追加: 設計凍結 under 4 件目=通知トポロジー(移送表の必須列候補)。

### golden 第2回(2026-07-04・maintainer 実機)— **合格・第1段クローズ**

- 是正 2 件の再確認 OK(バッジ初期表示・メニュー閉鎖)。register= applied。
- 第1段の最終成績: 機械受入全緑(期待値/テスト/golden 無改訂)+golden 2 回(所見 2 件→是正→合格)。
  次= 第2段(作業モード)。移送表に「通知トポロジー」列を必須化して臨む(本段の教訓)。

### 残ゲート(第1段時点の記載 — クローズ済み)

- **golden 再ウォークスルー(maintainer 実機・視覚不変の確認)** — 完了後に register を applied へ。
  確認観点: ⋯メニュー(ゴミ箱バッジ件数)・ゴミ箱ポップアップ(開閉・選択・復元・完全削除・空にする・
  確認ダイアログ)・削除モード(入る/出る/ゴミ箱へ移動)の視覚・操作が従前どおりであること。

