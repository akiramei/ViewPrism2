# Architecture Decision Records — ViewPrism2

| ADR | 決定 | 状態 |
|---|---|---|
| [0001](0001-ui-framework-avalonia.md) | UI フレームワーク = Avalonia UI 12.0.4 | 承認済み |
| [0002](0002-architecture-layered-mvvm.md) | レイヤード + MVVM(Core / Infrastructure / App / Tests) | 承認済み |
| [0003](0003-data-access-sqlite-dapper.md) | Microsoft.Data.Sqlite + Dapper、手書き DDL + 自作マイグレーションランナー | 承認済み |
| [0004](0004-imaging-skiasharp.md) | 画像処理 = SkiaSharp | 承認済み |
| [0005](0005-mvvm-communitytoolkit.md) | MVVM 基盤 = CommunityToolkit.Mvvm | 承認済み |
| [0006](0006-i18n-json-resources.md) | i18n = JSON リソース + LocalizationService(動的切替) | 承認済み |
| [0007](0007-testing-xunit-v3.md) | テスト = xUnit v3(受入ハーネス=治具) | 承認済み |
| [0008](0008-phash-self-implemented-dct.md) | 知覚ハッシュ = SkiaSharp 上で自作 DCT 実装(pHash-only・新規依存なし) | 承認済み |

運用: 新しい決定・変更は新番号で追加(上書きしない)。BomDD の表面部品に関わる決定は K-BOM(bomdd/31-kbom.yaml)へ知識を転記する。
