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

## 6. `/eco-fix` 実施記録(2026-07-17)

### 6.1 プローブ先行(R5)

CpI18n010TabVmLabelTests へ「画像タブの列見出しとソート候補と件数が言語切替で英語化する」を追加
(実アセット ja/en・コレクション 1 枚シード・InitializeAsync 後に SetLocale 往復)。
是正前= **赤**(en 切替後も ListColumns= 名前/サイズ/更新日 のまま=実機所見の再現固定)。
WorkTab 側の同型 probe は GF-079-01 で既存(列見出し)— 本 fix で検査対象が Recompute 拡大分へ広がる。

### 6.2 是正内容(悉皆処置: 是正 13+様式変換 4+精査済み根拠更新 3)

- **型b(明示再計算)**: ImageTab CultureChanged へ `Recompute()` 追加(**WorkTab の GF-079-01
  Rebuild 対と対称化**)— CountLabel/ChipHintLabel/ColumnSortLabel/列見出し/ソート候補/
  CurrentNote/NoCurrentLabel/row.NumCurrent を一括再解決(Recompute が BuildListColumns/
  BuildContextPanels/BuildAddGroups を内包)。WorkTab は既存 `BuildSortModels()` を `Recompute()` へ
  拡大(BuildSortModels 内包+CountLabel/ChipHint/タグ面ノートも再解決)。
- **VM 直書き日本語の是正**: `ListColumnBuilder.KindChipLabel`(数値/テキスト/シンプル/基本の
  直書き= XAML lint の死角)→ `KindChipKey`(i18n キー返却)へ変更し、呼び出し側 2 箇所
  (ImageTab/WorkTab のソート候補構築)で表示時解決。既存キー(tag.type.*/view.columnChipBasic)を
  使用=新キーなし。
- **型a(キー保持)**: ImageTab `_catalogError/_contentError/_scanNotice` → `*Key` フィールド+
  表示時解決 getter(culture handler の全再通知で再評価)。WorkTab `WsDeleteMessage` →
  権威値=件数(WsDeleteCount)保持+算出プロパティ。
- **精査済み(是正不要と確定)**: Settings.SnapshotSummary= CultureChanged で
  RefreshSnapshotSummary 再実行済み=**既に追随する**(lint 検出は set 時解決の様式によるもの・
  根拠更新)。_undoNote×2= Core result.Message 依存の一時ノート(Core 文言 i18n 化は別スコープの
  既知限界として記録・次操作でクリア)。
- lint allowlist 更新: キー保持化で消滅した 4 サイトを除去(死亡エントリ fail が変換を機械確認)・
  残余 13 サイトの根拠を「精査済み+再解決経路」へ更新・既知限界 2 サイトを (d) 層へ。

### 6.3 機械受入

build 0 error / **Tests 799/799**(probe 緑転+lint 双方向 allowlist 整合)/ Oracle 109+2skip
(R6 不変)/ validate_bom 0/0。

### 6.4 セルフゴールデン(R7)= 対象外

XAML 不変・ja 既定の表示文字列不変(解決タイミングのみ変更=静止 capture 差分なし)。
ECO-038/104/106 前例。en 切替の実挙動は gate②(軽量)で確認。

### 6.5 M4 要否

E-BOM/M-BOM 宣言変更なし(REQ-051 既存契約への適合是正)。CP 刻印は accept 時。
