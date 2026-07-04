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
