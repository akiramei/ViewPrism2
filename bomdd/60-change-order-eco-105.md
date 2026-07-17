# Change Order — ECO-105(staged): 撮影ハーネスの再実行不能 — 同一出力先で 2 回目が DuplicateTagName→NRE(ECO-103 レビュー所見 P2)

- 起票: 2026-07-17(maintainer レビュー所見・ECO-103 applied 後の P2)
- 種別: 検査器(工具)の不具合是正(M-CAPTURE-HARNESS-052・製品コード非対象)
- baseline: main `ad51f92`

## 1. 症状(レビュー所見・2026-07-17・実測済み)

`tools/ViewPrism2.CaptureHarness` を**同じ出力ディレクトリで 2 回実行**すると、2 回目が
`Program.cs:87` 付近の NullReferenceException で非ゼロ終了する(レビュー実測: 初回成功・2 回目 NRE)。

## 2. 工程診断 — 検査器(工具)の実装欠陥。gate① 不要・golden n/a

| 工程 | 判定 | 根拠 |
| --- | --- | --- |
| CAD | 対象外 | 工具の実行規約はモックの管轄外。 |
| BOM | 健全 | M-CAPTURE-HARNESS-052 は登録済み。再実行冪等性は unit の受入観点として未宣言 → fix 時に追記。 |
| 実装(工具) | **欠陥(変更対象)** | §3。混入= `7370b7d`(ECO-103 資産化時。scratch 版由来の構造を track 時に検収しなかった)。 |

- マスキング要因: 機械受入 4 点にハーネス実行は含まれない+R7 実施は毎回新規の scratch 出力先を
  使う運用だったため、同一出力先の再実行が一度も走っていなかった。
- 製品コード(src/tests)は非対象 — 工具のみの是正で、golden(gate②)は **n/a**(ECO-090 前例)。

## 3. 切り分け済みの事実(2026-07-17 コード読解+レビュー実測)

- `Program.cs:61-63`= `Path.Combine(outDir, "db-work")` を `Directory.CreateDirectory` し、
  既存の `vp2.db` があっても**そのまま `DatabaseManager.Open`** する(初期化なし)。
- `Program.cs:70-71`= シードヘルパ `T()` が `(await tagService.CreateAsync(...)).Value!` —
  2 回目は既存タグと重複し `DuplicateTagName` 失敗 → `Value` null のまま `!` で進み、
  後続の参照(:87 付近= SetTextualSettingsAsync 等)で NRE。
- 失敗様式は fail-fast(非ゼロ終了)であり、**誤った撮影結果を黙って出す欠陥ではない**
  (được 撮影済み PNG は上書きされず残る)— 重大度は P2(運用性)で妥当。

## 4. 是正方針(案・着手時確定)

1. **案A(推奨・最小)**: 起動時に `db-work/` を削除してから作成(毎回新規 DB)。ハーネスの意図=
   「mock デモデータの決定論シードで撮影」であり、**前回 DB の再利用に価値がない**(シードが正本)。
   出力先内の自作ディレクトリの削除であり安全(outDir 直下の PNG は対象外)。
2. 案B: 実行ごとの一時ディレクトリ(GUID)+終了時削除 — 掃除漏れ時のゴミ増殖と、失敗時の
   死体解剖(DB 検分)がしにくくなる分だけ A に劣る。
3. 案C: 冪等シード(既存タグを引き当てて再利用)— 前回 run の残骸(途中失敗 DB)を引き継ぐ
   リスクがあり決定論性が下がる。不採用方向。
4. **検証(プローブ相当)**: 同一出力先での 2 回連続実行が両回とも exit 0+PNG 6 面生成を実測
   (tools は tests 管轄外のため、実行ログを ECO 実施記録に残す)。

## 5. 影響 BOM(fix 時 M4 で同期)

- **tools**: `ViewPrism2.CaptureHarness/Program.cs` のみ(シード前の DB 初期化)。
- **src/tests**: 変更なし。固定 Oracle 不変(R6)。
- **M-BOM**: M-CAPTURE-HARNESS-052 の受入観点へ「同一出力先での再実行冪等」を追記。

## 6. 残ゲート

- gate①: 不要(工具の実装欠陥)。
- gate②(golden): **n/a**(製品 UI 非対象・ECO-090 前例)。機械証拠= 2 回連続実行の実測ログ。
