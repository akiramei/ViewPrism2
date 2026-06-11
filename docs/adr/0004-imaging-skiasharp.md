# ADR-0004: 画像処理(サムネイル・デコード)に SkiaSharp を採用する

- 状態: 承認済み(2026-06-11)
- 決定者: 設計AI(委任範囲)

## 文脈
REQ-040(サムネイル: 長辺 256px・inside fit・拡大なし・PNG/JPEG q80)と REQ-011(対応 6 形式: jpg/jpeg/png/gif/bmp/webp)、REQ-043(解像度表示)に画像デコード/エンコードが必要。原典は Sharp(libvips)を使用。

## 決定
**SkiaSharp 3.119.4** を採用する。

## 根拠
- Avalonia 自体が Skia で描画しており、ネイティブ依存が一系統に揃う(配布サイズ・互換リスク減)
- 対象 6 形式すべてデコード可(GIF はアニメの先頭フレーム)。PNG/JPEG エンコードの品質指定が REQ-040 にそのまま対応
- MIT ライセンス

## 却下した代替案
- **ImageSharp**: 純マネージドで API は良いが、Six Labors Split License(商用条件)と依存系統の追加が難点
- **System.Drawing.Common**: Windows 専用かつ事実上非推奨。WebP 非対応
- **Magick.NET**: 高機能だがバイナリが巨大。V1 の要件に過剰

## 影響
- サムネイル生成(M-THUMB-008)は「表面」部品: SkiaSharp API への依存判断は K-BOM(K-SKIA)に転記し、受入は L2(出力ファイルの寸法・形式メタデータ検査)
- EXIF Orientation は適用しない(仕様 §4 で宣言済み)。類似検索(Loop V4)で OpenCvSharp を追加する際も描画系とは独立
