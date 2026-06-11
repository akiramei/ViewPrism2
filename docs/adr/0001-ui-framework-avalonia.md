# ADR-0001: UI フレームワークに Avalonia UI 12.0 を採用する

- 状態: 承認済み(2026-06-11)
- 決定者: maintainer(要件指定)+ 設計AI

## 文脈
view-prism(Electron + Next.js + React)を .NET でネイティブ再実装する。UI フレームワークはユーザー要件として Avalonia UI 12.0 が指定されている。

## 決定
Avalonia UI **12.0.4**(NuGet 安定版、2026-06-11 確認)+ Fluent テーマを採用する。対象は Windows のみ(チャーター)だが、Avalonia 採用により将来のクロスプラットフォーム化の道を残す。

## 根拠
- 要件指定。加えて: XAML ベースの成熟した MVVM 対応、Skia ベース描画(SkiaSharp とネイティブバイナリを共有)、仮想化パネル・KeyBinding・DynamicResource など本件の要求(REQ-041 仮想化、REQ-044 キーボード操作、REQ-051 動的言語切替)に必要な機構が揃う
- WPF 比: クロスプラットフォーム余地、コンポジション描画の一貫性。WinUI 3 比: 配布が単純(MSIX 不要)

## 影響
- 見開き・スクロール等の高度な表示(Loop V2)はカスタムコントロールを書く前提になる
- UI 部品は BomDD 上「表面」となり、K-BOM(K-AVALONIA)で Avalonia 12 のイディオムを管理する
