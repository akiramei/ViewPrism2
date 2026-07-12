# ECO-074: バックアップファイルの置き場所管理(B層パッケージの既定フォルダ規約)

- 起票: 2026-07-12(maintainer 所見・ECO-073 gate② golden 実機確認中)
- 種別: 機能拡張(UX 欠陥の是正を含む設計追加)
- 状態: staged(gate①=裁定 待ち)
- 関連: ECO-073(B層 V1・golden 進行中)/ ECO-072(A層・クローズ済み・SS-002)

## 1. 症状・懸念(maintainer 報告)

- B層取り込み(B-2)の「変更」からのファイル選択ダイアログは **OS 既定=最後に使ったフォルダ**から
  開く。バックアップという重要操作なのに、任意フォルダの任意 JSON を選べてしまい、
  **間違ったファイルを選択するリスクが高い**。
- パッケージが「バックアップファイルである」ヒントは**ファイル名しかない**
  (`<名前>.viewprism2-collection.json`)。汎用 JSON フォーマットゆえ拡張子でも区別できない。
- maintainer の方向性: **バックアップファイルしか存在しないフォルダから選ぶのが望ましい**。
  「対象コレクションの最新バックアップを初期選択」は便利だが**賛否あり=裁定事項**。

### 1.1 追補所見(2026-07-12・maintainer・GF-073-03 是正後の実機確認)

- B-2 を開いた直後の**未選択状態**は「グリフ+空白のカード」であり、設計された表示になっていない。
- 工程診断: CAD prose(snapshot_export_import.md §B-2 検証)は状態を**互換性 OK / NG の 2 つしか
  定義せず**、mock も選択済み状態のみ=**「未選択」という状態が CAD に存在しない**。実装は
  ウィンドウを開いても picker を自動起動しないため、CAD に無い未定義状態が毎回入口になる
  (CollectionImportWindow.axaml.cs — 表示時のコマンド起動なし)。GF-073-03 で足したグリフは
  暫定であり、未選択状態の設計そのものは本 ECO の裁定範囲(§4 と同一次元=ファイル選択 UX)。

| 工程 | 判定 | 証拠 |
|---|---|---|
| CAD(ViewPrismUI) | **沈黙(欠陥)** | mock B-1 の出力先 `D:\export\…` はプレースホルダ。snapshot_export_import の docs/captures に既定フォルダ・選択起点の規約なし |
| BOM(spec/E-BOM) | **沈黙** | ECO-073 spec §5 は「出力先」とだけ言い既定フォルダを規定せず。REQ-093 系にも置き場所規約なし |
| 実装 | 沈黙の忠実な転写 | 書き出し既定=`MyDocuments\<名前>…json`(CollectionExportViewModel.cs:37-39 — これ自体 spec に無い実装の発明)。Save/OpenFilePicker とも `SuggestedStartLocation` 未指定= OS の最後に使ったフォルダ(WindowService.cs:190-213) |

**結論: 要求の沈黙次元(設計欠陥)であり実装の転写ミスではない。** さらに A層(ECO-072)は
SS-002 で「アプリ管理の既定フォルダ(`%APPDATA%/ViewPrism2/snapshots`)+settings.json 永続
(`AppSettings.SnapshotDirectory`)」を持つ(SnapshotService.cs:54・SnapshotViewModel.cs:74)のに、
B層設計時にこの**管理フォルダ概念を read-across しなかった**設計工程の水平展開漏れ。
是正は CAD(ViewPrismUI)への規約明文化が先、spec/BOM→実装が後。

## 3. 切り分け済みの事実

確定:

- A層は管理済み: 既定 `%APPDATA%/ViewPrism2/snapshots`・settings 永続・A-1 は**フォルダ内の
  一覧から選ぶ** UI(ピッカーで任意ファイルを探させない)。
- B層書き出し既定は `MyDocuments`(spec/CAD に根拠なし)。picker 起点は両方向とも OS 任せ。
- パッケージはヘッダ(`kind=collection`・コレクション名・作成日時)を自己記述しており、
  フォルダ内のパッケージ列挙・対象コレクション判定・最新判定は**ヘッダ読取だけで実装可能**
  (PackageJson.ReadHeader は先頭ストリーミング読取・全件展開しない)。

疑い(未検証):

- `%APPDATA%` はマシン故障対策としてのバックアップ置き場に弱い(A層 SS-002 も同様の性質を
  既に受容しているが、B層は「他ライブラリへの持ち出し」用途もあるため要件が異なる可能性)。

## 4. 是正方針(案・着手時確定=gate①裁定)

- **案A(SS-002 準拠の最小)**: B層にも管理既定フォルダ(例 `%APPDATA%/ViewPrism2/collections`)
  +settings 永続(`CollectionPackageDirectory`)。書き出し既定=管理フォルダ・
  Save/OpenFilePicker の `SuggestedStartLocation`=管理フォルダ。取り込み初期選択なし。
  diff 小(WindowService+VM 2 面+settings+CAD/spec 注記)。golden 影響=B-1 出力先文字列と
  picker 起点のみ(視覚不変)。
- **案B(案A+最新初期選択)**: 取り込み(B-2)を開いたとき管理フォルダをヘッダ走査し、
  対象コレクションの最新パッケージを初期選択済みにする。diff 中(ヘッダ走査+初期状態変化)。
  golden 影響=B-2 初期状態の再定義(gate②項目の追加)。maintainer 自身が「賛否あり」。
- **案C(一覧起点 UI)**: A-1 と同型に「管理フォルダ内のバックアップ一覧から選ぶ」面を B-2 前段に
  置き、任意ファイル選択は「その他のファイル…」の逃げ道に降格。UX は最も堅いが CAD 新 mock が
  先行必要。diff 大・golden 全面。
- 共通の裁定事項: 既定フォルダの場所(`%APPDATA%` か ユーザー文書配下か)・A層 SS-002 との統一。
- **B-2 未選択状態の扱い(§1.1)も同時裁定**: 案イ=表示直後に picker 自動起動(CAD の「未選択
  状態は存在しない」前提に忠実・キャンセル残留時のみプレースホルダ文言)/案ロ=プレースホルダ
  文言のみ(文言の CAD 正典化が先)/案B・案C 採用ならそれぞれ初期選択・一覧起点により
  未選択状態は実質消滅し個別対処不要。

## 5. 影響 BOM(案により変動)

- CAD: ViewPrismUI snapshot_export_import(既定フォルダ規約の明文化。案C は新 mock)
- spec: §2.14(B層)へ置き場所規約の節追加(A層 SS-002 との対応関係を明記)
- E-BOM/M-BOM: E-047/048 の受入観点へ picker 起点、M-042/043 へ settings 項目(案A/B)
- CP: CP-PACKAGE-032 へ「既定フォルダ経由の書き出し→取り込み往復」観点
- 実装: WindowService・CollectionExport/ImportViewModel・AppSettings(いずれも /eco-fix で)

## 6. 残ゲート

- gate①: ~~是正方針の裁定(案A/B/C+既定フォルダ場所)~~ → **裁定済み(§7)**
- gate②: golden(操作手順は fix 時に確定)

## 7. gate①裁定(2026-07-12)

- maintainer 裁定: **案A(SS-002 準拠の管理既定フォルダ+picker 起点固定)**を採用。
  既定フォルダの場所は **ユーザー文書配下**(バックアップの持ち出し・目視確認のしやすさを優先)。
- 具体化(設計者適用): 既定=`<Documents>\ViewPrism2\collections`(A層 SS-002 の
  `%APPDATA%\ViewPrism2\snapshots` と命名対称)。settings 永続キー=`CollectionPackageDirectory`
  (null=既定)。書き出し既定出力先・Save/OpenFilePicker の `SuggestedStartLocation` とも
  この管理フォルダへ固定(picker での逸脱先は永続しない — 「最後に使ったフォルダ」の再発防止)。
- **B-2 未選択状態(§1.1)= 案イを設計者適用**(裁定で明示選択なし・golden で否認可):
  ウィザード表示直後に picker を自動起動(CAD の「未選択状態は存在しない」2 状態定義に忠実)。
  キャンセル残留時のみプレースホルダ文言を表示(文言は CAD 正典化してから実装)。
  案B(最新初期選択)は不採用(maintainer 自身が賛否ありとした点・本裁定に含めない)。
