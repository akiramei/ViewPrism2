# Work Order — loop-v1-core

## 目的
ViewPrism2(タグ × 仮想ビューの Windows 向け画像管理アプリ、.NET 10 / Avalonia 12 / SQLite)の V1 コアを、本製造パッケージのみから製造する。

## 入力(これがすべて。これ以外を参照しない)
- `bomdd/20-spec.md`(仕様 — 観測契約 §2.8 を含む)
- `bomdd/30-ebom.yaml` / `31-kbom.yaml` / `32-mbom.yaml` / `33-control-plan.yaml` / `34-routing.yaml`
- `docs/adr/`(確定済みの技術決定)

原典 TypeScript 実装・設計対話・固定オラクルは供与されない。参照を試みないこと。

## 製造対象
- C# / .NET 10 / Avalonia 12.0.4。配置は 32-mbom.yaml の artifact.path のとおり
  (src/ViewPrism2.Core, src/ViewPrism2.Infrastructure, src/ViewPrism2.App, tests/ViewPrism2.Tests)

## 必須受入(自己受入)
- `dotnet build ViewPrism2.sln -c Release` が警告ゼロで成功する(TreatWarningsAsErrors)
- `dotnet test tests/ViewPrism2.Tests -c Release` が成功する
- 受入ハーネスの必須範囲: Control Plan の unit/L2/L3 行の test_vectors 全被覆 +
  **L1 スモーク(起動→フォルダ登録→スキャン→グリッド表示の正常系 1 本)**(unit 緑のまま表面が実行時全滅する盲点の対策)
- G 行(CP-UI-G1〜G5)は承認者(maintainer)の受入であり、工場の自己受入対象外。ただしチェックリスト項目を満たすよう実装する

## ずる報告(義務)
製造中に BOM/K-BOM/Control Plan から導けなかった判断は、**実装を止めずに**全件 cheat-log 形式で報告する:
```
### CHEAT-<ID> [C1〜C6] 一行要約
- 手法が与えなかったもの:
- 代替した判断(何をどう埋めたか):
- 重大度: blocker / friction / minor
```

特に以下の次元は判断したら必ず報告する:
- UI レイアウトの寸法・余白・配色で K-DESIGN に無い値を使った場合
- Avalonia の仮想化方式で K-AVALONIA の既定方式から逸脱した場合
- エラー表示・通知の形態(ダイアログ/トースト等)を独自判断した場合
- i18n の新規キー追加・キー命名

## 調達部品の規律
依存パッケージは `32-mbom.yaml` の `procurement` に列挙されたものだけを使う。列挙外のパッケージが必要だと判断した場合、その採用は**ずる報告対象**(理由・代替案込みで報告し、標準ライブラリで代替できるならそちらを優先)。

## 進めない級の問題(blocker)を発見した場合
BOM の自己矛盾・実装不能を発見した場合は、**当該製造単位を `blocked` とマークして他の単位を続行**し、cheat-log に C6(手戻り)として記録して納品時に報告する。製造を中断しての質問往復はしない(隔離の維持。修正は設計者側が BOM を改訂し fresh re-run する)。

## 自己受入が赤のままの場合
**自己受入に FAIL が残る状態は「納品」ではない。** 緑にできない場合は `stop/report`: 製造を停止し、FAIL 一覧・原因の見立て・試した修正を報告して終了する(赤のまま納品しない)。
