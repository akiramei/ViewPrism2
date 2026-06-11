# ADR-0007: テスト基盤に xUnit v3 を採用する(受入ハーネス=治具)

- 状態: 承認済み(2026-06-11)
- 決定者: 設計AI(委任範囲)

## 文脈
BomDD の受入は Control Plan(33-control-plan.yaml)のテストベクタを実行する治具を要求する。治具は製品 BOM と同格にリポジトリ管理する(method-v1 §4)。検査深さは unit(核)、L2(スキーマ・サムネイルメタデータ)、L3(NFR 計測)、G(golden+承認者)。

## 決定
- **xunit.v3 3.2.2** + Microsoft.Testing.Platform。`tests/ViewPrism2.Tests` に集約
- テストは Control Plan の特性 ID(CP-xxx)を Trait に付けてトレースする(例 `[Trait("cp", "CP-EVAL-001")]`)
- L2 検査(スキーマ同値・サムネイル寸法)も同プロジェクト内のテストとして実装(SQLite は一時ファイル/`:memory:`、画像はフィクスチャ)
- 固定オラクル(41、工場非開示)は設計者が同ハーネスの追加データセットとして納品後に実行する

## 却下した代替案
- NUnit/MSTest: 機能差は僅少。xUnit はフィクスチャ分離の既定が並列実行と相性が良く、v3 で .NET 10/MTP に最適化されている

## 影響
- 工場の自己受入コマンドが `dotnet build` + `dotnet test` に固定される(Work Order に記載)
