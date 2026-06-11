# ADR-0002: レイヤードアーキテクチャ + MVVM(3 プロジェクト構成)を採用する

- 状態: 承認済み(2026-06-11)
- 決定者: 設計AI(委任範囲)

## 文脈
原典は Electron の 2 プロセス(Renderer/Main)+ IPC 45 チャンネル構成だったが、.NET 単一プロセスでは IPC 層が消滅する。BomDD の核/表面分離(核=unit 受入、表面=golden 受入)を実現するには、ドメインロジックを UI から完全に切り離して観測契約(20-spec.md §2.8 OC-1〜8)単位でテスト可能にする必要がある。

## 決定
レイヤード + MVVM。プロジェクト構成:

```
src/ViewPrism2.Core            # 核: エンティティ、純粋ロジック(条件評価器・NodeGraph 構築器・
                               #     整列器・スキャン判定器・整形器)、サービス/リポジトリのインターフェース
                               #     依存: BCL のみ(UI・DB・画像ライブラリへの参照禁止)
src/ViewPrism2.Infrastructure  # SQLite(リポジトリ、マイグレーション)、ファイルシステム、
                               # サムネイル(SkiaSharp)、設定ストア、i18n ローダ、ログ
src/ViewPrism2.App             # 表面: Avalonia Views + ViewModels、DI 構成、リソース
tests/ViewPrism2.Tests         # 受入ハーネス(unit + L2)。治具として製品と同格に管理
```

依存方向: App → Infrastructure → Core(逆方向参照禁止)。DI は Microsoft.Extensions.DependencyInjection。

## 却下した代替案
- **クリーンアーキテクチャ(4+ 層)**: Application/Domain の分割は本規模(V1)では層の儀式コストが受入価値を上回らない。Core に統合
- **垂直スライス**: 機能間でドメイン(タグ⇔ビュー⇔NodeGraph)が密に絡むため、スライス間共有が肥大して利点が薄い

## 影響
- 観測契約 OC-1〜8 はすべて Core(+Infrastructure の一部)の公開 API として実装され、UI なしで検査できる
- ViewModel は Core のサービスを直接呼ぶ(原典の IPC 型定義 45ch は不要になる)
