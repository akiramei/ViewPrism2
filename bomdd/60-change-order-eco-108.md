# Change Order — ECO-108(staged): メインタブ/設定面の常駐値 18 サイトの言語追随(ECO-107 lint③ の deferred 層)

- 起票: 2026-07-17(ECO-107 §7.2 の R3 分離起票 — lint③ が機械列挙した deferred 層)
- 種別: 不具合是正候補(常駐値の言語非追随= ECO-104/106 と同族の残余。実装層)
- baseline: main `3d9224a`

## 1. 症状(lint③ の機械列挙・2026-07-17)

`CpI18n010AssetLintTests` 次元③(解決済み文字列の VM 状態保持)が列挙した 34 サイトのうち、
モーダル 15・射影 1 を良性層別した**残余 18 サイト** — メインタブ面/設定面に常駐し、言語切替時に
再解決機会が保証されない疑い(ECO-106 が定義した脆弱クラス):

| VM | サイト(9+7+1+1) |
| --- | --- |
| ImageTabViewModel | `_catalogError` `_contentError` `ColumnSortLabel` `ChipHintLabel` `CountLabel` `CurrentNote` `NoCurrentLabel` `row.NumCurrent` `_scanNotice` |
| WorkTabViewModel | `CountLabel` `ChipHintLabel` `CurrentNote` `NoCurrentLabel` `row.NumCurrent` `WsDeleteMessage`(タブ内確認ポップアップ=境界例) `_undoNote` |
| ImageTabOrganizeViewModel | `_undoNote` |
| SettingsViewModel | `SnapshotSummary`(**設定画面内=言語切替 UI と同居**・最有力) |

### 1.1 実機顕在化(2026-07-17・ECO-107 golden スモーク中の maintainer 所見・スクリーンショットあり)

en 切替後、**画像タブ**のファイル一覧列見出し(名前/サイズ/更新日)・ソートポップオーバーの候補行
+「基本」チップが**日本語のまま**。同じ列モデルを使う**作業タブは英語に追随**・列ピッカー
(Display columns)も英語=面間の非対称が実機で確認された。

- 機構(コード確定): 両タブとも `BasicColLabel`(common.name/size/modifiedDate)を**構築時に
  焼き込む**。WorkTab は CultureChanged で `RebuildBasicSortColumns()+BuildSortModels()`
  (GF-079-01 是正)を持ち追随するが、**ImageTab には同等の再構築がない**(ハンドラは
  OnPropertyChanged(string.Empty)+ChipStrip のみ)。是正= WorkTab との**対称化**が本命。
- ECO-107 の退行ではない(証拠: 表示は生キーでなく正しい ja 訳・ja/en 同数削除でキー集合一致
  lint 緑・製品コード無変更)。
- **本サイト(列見出し/ソートモデルの焼き込み)は §1 の 18 サイトに含まれない** — lint③ の既知の
  限界(「=>」式メソッド `BasicColLabel` 経由の間接解決+コンストラクタ/初期化子への格納は代入
  検出に映らない)による。**本 ECO のスコープは「18 サイト+ImageTab 列/ソートモデルの再構築
  対称化」**とし、fix 時に間接解決サイトの追加走査(BasicColLabel/LabelFor 様式の呼び出し元
  棚卸し)で取り残しを掃射する。

## 2. 工程診断 — 実装層(横断規約= REQ-051 言語即時反映への逸脱疑い)。gate① 不要

- **確定実測(ECO-107 §7.2)**: ImageTabViewModel の CultureChanged ハンドラは
  `OnPropertyChanged(string.Empty)`(全プロパティ再通知)のみ — **保持値は再解決されない**
  (再通知は保存済みの旧言語文字列を再表示するだけ)。WorkTab は一部のみ明示再構築
  (RebuildBasicSortColumns/BuildSortModels/WsName= ECO-095 是正)で、上表サイトは対象外。
- **未検証(fix 時にサイト別プローブで確定)**: 各サイトの実害有無 — 表示中に言語切替が起きた場合の
  残留(例: `_catalogError` は読込失敗中のみ・`CountLabel` は次の再計算まで・`SnapshotSummary` は
  設定画面内で切替と同時に見えている)。一部は「切替後の最初の操作で自然回復」のため、
  ECO-106 級(次のユーザー操作まで固定)より軽い可能性がある。
- 混入= 各サイトの導入 ECO に分散(ECO-079 の一斉配線時代〜)。潜伏理由= golden の単一ロケール
  実施(ECO-104/106 と同)。

## 3. 是正方針(案・着手時確定)

1. **設計は ECO-104/106 で確立済みの 2 型から選ぶ**: (a) キー/コード保持+表示時解決の算出プロパティ化
   (常駐メッセージ型= _catalogError/_scanNotice/SnapshotSummary 等)(b) CultureChanged での明示
   再計算(集計ラベル型= CountLabel/ColumnSortLabel 等・WorkTab の既存 Rebuild 系に合流)。
2. **プローブ(R5)**: サイト別に「値を表示状態にして SetLocale → 追随」を実測(ja↔en 往復)。
   実害なし(自然回復が即時)と判明したサイトは lint allowlist の根拠を「精査済み・良性」へ更新して
   閉じる(全 18 の悉皆処置= 是正 or 根拠更新)。
3. クローズ時に ECO-107 の allowlist(c) 層(deferred 18)を解消= allowlist は (a)(b) 良性層のみになる。

## 4. 影響 BOM(fix 時 M4 で同期)

- **src**: ImageTabViewModel / WorkTabViewModel / ImageTabOrganizeViewModel / SettingsViewModel。
- **tests**: サイト別プローブ+CpI18n010AssetLintTests の allowlist 根拠更新。固定 Oracle 不変(R6)。
- **CP**: CP-UI-G1(作業タブ)/ CP-UI-G2 系(画像タブ)/ CP-I18N-010 へ観点刻印(accept 時)。

## 5. 残ゲート

- gate①: **不要**(実装層・設計は確立済み 2 型の適用)。
- gate②(golden): **軽量**: 代表面の実機確認(①設定のスナップショット要約を表示したまま言語切替→
  追随 ②画像タブで読込失敗/件数ラベル表示中に言語切替→追随 or 次操作で回復の裁定)。
