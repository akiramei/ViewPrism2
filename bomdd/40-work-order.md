# Work Order — loop-v3-similarity(v3.0)

> V1(loop-v1-core)・V2(loop-v2-viewer)の Work Order は git 履歴(tag loop-v1-r1〜r3 / loop-v2-r1)を参照。本書は V3 増分製造の指示書。

## 目的
ViewPrism2 の V1/V2 完成コードベース(タグ × 仮想ビューの Windows 向け画像管理アプリ、.NET 10 / Avalonia 12 / SQLite、unit 290 全緑)に対し、**類似画像検索(pHash)とマージ**を、本製造パッケージのみから増分製造する。検出系(pHash 計算・距離・検索)は核、操作系(検索 UI・マージ・トラッシュ)は表面。

## 入力(これがすべて。これ以外を参照しない)
- `bomdd/20-spec.md` v3.0(**§2.10 類似検索とマージが今回の中心**。観測契約 §2.8 OC-14〜18 を含む)
- `bomdd/30-ebom.yaml` / `31-kbom.yaml`(**K-PHASH 必読**)/ `32-mbom.yaml` / `33-control-plan.yaml` / `34-routing.yaml`(routing_v3)
- `docs/adr/`(**ADR-0008 pHash 必読**)
- 既存コードベース `src/` / `tests/ViewPrism2.Tests`(V1/V2 完成品 — 読解・拡張してよい)

原典 TypeScript 実装・設計対話・固定オラクル(`tests/ViewPrism2.Oracle` 含む)は供与されない。参照を試みないこと。

## 製造対象(増分)
| 製造単位 | 内容 | 受入 |
|---|---|---|
| M-PHASH-020 | pHash 計算核(DCT 知覚ハッシュ・ハミング距離・類似度%変換。純粋計算) | CP-PHASH-016(unit) |
| M-SIMSEARCH-021 | 特徴量・類似度の永続化(image_features/image_similarity+新規マイグレーション)+類似検索エンジン | CP-SIM-017(unit/L2)+CP-DB-006 |
| M-MERGE-022 | マージ計算+マージサービス(タグ union・status=Deleted・原子・**物理非破壊**) | CP-MERGE-018(unit)+**CP-MERGE-001(L3 物理差分)** |
| M-UI-SIMILARITY-023 | 類似検索 UI・マージ UI・トラッシュ表示+入口 | CP-UI-G9 / CP-L1-SMOKE |

## 回帰の規律(増分製造の絶対条件)
- **既存の意味論を変更しない**。既存 unit テスト 290 件(v1 234+v2 56)の修正・削除は原則禁止。
  期待値変更が必要だと判断した場合は当該単位を `blocked` にして C6 報告(テストを書き換えて緑にしない)。
  例外: 新規 CP の新規テスト追加・新規マイグレーション追加は許可
- 既存機能のリファクタリングは今回のスコープに必要な最小限に留める(不要改変はずる報告対象)

## 安全の規律(INV-009 — 初の実アクション)
- **マージは DB 上の論理操作のみ**(タグ union+マージ元 status=Deleted)。**物理画像ファイルへの書き込み・移動・削除・リネームを一切行わない**(読み取りのみ)
- `CP-MERGE-001`(L3 物理差分)が操作前後でファイルのバイト・mtime 不変を実証する。物理ファイル操作の実装を検知したら当該単位を `blocked` とし C6 報告
- pHash 計算はファイルを**読み取るだけ**(デコード)。一時ファイルを作らない

## 必須受入(自己受入)
- `dotnet build ViewPrism2.sln -c Release` が警告ゼロで成功する(TreatWarningsAsErrors)
- `dotnet test tests/ViewPrism2.Tests -c Release` が成功する(**既存 290 件+v3 新規の全緑 = 回帰ゼロ**)
- 受入ハーネスの必須範囲: CP-PHASH-016 / CP-SIM-017 / CP-MERGE-018 / **CP-MERGE-001** / CP-DB-006(v3.0 スキーマ)の test_vectors 全被覆
- **L1 スモーク(CP-L1-SMOKE v3.0 経路)**: 起動→スキャン→グリッド→**画像選択して類似検索→結果表示→マージ先/元選択→マージ実行→トラッシュで deleted 確認→マージ元ファイルがディスク上に残存することを確認**
- G 行(CP-UI-G9)は承認者(maintainer)の受入であり、工場の自己受入対象外。ただしチェックリスト項目(20-spec.md §2.6 G-9)を満たすよう実装する

## 調達部品の規律
依存パッケージは `32-mbom.yaml` の `procurement` に列挙されたものだけを使う(全 exact ピン)。**V3 で新規パッケージは追加しない** — pHash は SkiaSharp(既存)のピクセルアクセスで自作実装する(ADR-0008)。OpenCV 系(OpenCvSharp/Emgu.CV)・サードパーティ pHash ライブラリの採用は**ずる報告対象**(ORB/detailed は本ループ対象外)。

## ずる報告(義務)
製造中に BOM/K-BOM/Control Plan から導けなかった判断は、**実装を止めずに**全件 cheat-log 形式で報告する:
```
### CHEAT-<ID> [C1〜C6] 一行要約
- 手法が与えなかったもの:
- 代替した判断(何をどう埋めたか):
- 重大度: blocker / friction / minor
```

特に以下の次元は判断したら必ず報告する(V3 重点):
- **pHash レシピ**(リサイズ補間・グレースケール丸め・DCT 正規化/符号・cos 計算・中央値の DC 扱い)を K-PHASH/§2.10.1 と異なる形にした場合(決定性の生命線 — FMEA-021)
- **類似検索の候補 status**(normal 限定)・閾値境界(≧)・ソート規則を仕様と変えた場合(FMEA-022)
- **マージの値タグ衝突・多元決着順**(マージ先優先・id 昇順先勝ち)を仕様と変えた場合(FMEA-024)
- **キャッシュ無効化方式**(内容ベースのみ・連鎖無効化・ペア正規化)を変えた場合(FMEA-025)
- 物理ファイルに触れる実装を入れた場合(**最優先報告** — INV-009/FMEA-023)
- UI レイアウトの寸法・配色・アイコン・マージ先/元の視覚区別で K-DESIGN(v3.0 追補含む)に無い値を使った場合
- i18n の新規キー追加・命名(similar.* / merge.* / trash.*)

## 進めない級の問題(blocker)を発見した場合
BOM の自己矛盾・実装不能を発見した場合は、**当該製造単位を `blocked` とマークして他の単位を続行**し、cheat-log に C6(手戻り)として記録して納品時に報告する。製造を中断しての質問往復はしない(隔離の維持。修正は設計者側が BOM を改訂し fresh re-run する)。

## 自己受入が赤のままの場合
**自己受入に FAIL が残る状態は「納品」ではない。** 緑にできない場合は `stop/report`: 製造を停止し、FAIL 一覧・原因の見立て・試した修正を報告して終了する(赤のまま納品しない)。
