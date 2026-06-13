# ADR-0008: 知覚ハッシュ(pHash)を SkiaSharp 上で自作 DCT 実装する

- 状態: 承認済み(2026-06-13)
- 決定者: 設計AI(委任範囲)+ maintainer(Loop V3 スコープ裁定)

## 文脈
Loop V3(類似検索)の REQ-061/062 は、画像の知覚ハッシュ(pHash)と、その距離→類似度%変換を必要とする。
原典 view-prism は sharp(libvips)で DCT ベースの pHash を、detailed モードでは OpenCV.js の ORB を併用していた。
本ループは **pHash-only**(ORB/detailed は defer — 00-charter-v3.md §裁定記録 2)。また pHash は固定オラクルで
凍結するため**決定性**が必須であり、互換ポリシー CPOL-103 は「pHash/ORB は preserve_with_adapter(ライブラリ・
特徴量表現は変更可)」とする。

## 決定
**pHash を SkiaSharp(既存依存 ADR-0004)のピクセルアクセス上で自作 DCT 実装する。新規パッケージは採用しない。**
レシピは 20-spec.md §2.10.1 に固定(32×32 双線形リサイズ→`(int)(y+0.5)` グレースケール→**orthonormal 2 次元
DCT-II**(行→列 2 パス・cos 事前計算固定表)→左上 8×8→DC 除外の中央値しきい→行優先 64bit→16hex)。
距離はハミング距離(popcount XOR)、類似度%は §2.10.2 の区分線形+`Floor(raw+0.5)`+クランプ。

## 根拠
- **新規ネイティブ依存ゼロ**: SkiaSharp は既に画像処理基盤(ADR-0004)。pHash は数十行の純粋計算で実装でき、
  OpenCV 系(OpenCvSharp/Emgu.CV)の重いネイティブ配布を持ち込まない。ORB を defer したことで OpenCV が不要に
- **決定性の完全制御**: 自作なら補間方式・丸め・DCT 規約・cos 表をすべて spec/本 ADR で pin でき、固定オラクル
  (S-19 系)の凍結が成立する。第三者ライブラリは版差・内部実装の決定性が外部依存になる
- **CPOL-103 の許容**: 原典とのビット完全一致は不要。当方レシピを正準として再現可能であればよい
- pHash 計算は core(計算核)であり、SkiaSharp はピクセル取得のみに使う(描画系と独立)

## 却下した代替案
- **OpenCvSharp / Emgu.CV**: ORB(detailed)に必要だが本ループは pHash-only。重いネイティブ依存・配布サイズ増。
  ORB を導入する後続ループで別 ADR として再評価する(ADR-0004 影響欄の想定どおり)
- **Shipwreck.Phash / CoenM.ImageHash 等の pHash ライブラリ**: 調達を 1 つ増やし、決定性・版固定・SkiaSharp との
  デコード二重化が生じる。自作の方が決定性 pin と procurement 最小化(40-work-order)に勝る
- **平均ハッシュ(aHash)/差分ハッシュ(dHash)**: 実装は容易だが原典の DCT-pHash と検出特性が異なり、
  preserve_with_adapter の「同等の検索体験」から外れる

## 影響
- pHash 計算核(M-PHASH-020)は core 部品: DCT/距離/変換の規約は K-BOM(**K-PHASH** 新設)へ転記。受入は
  unit(OC-14 性質ベース=決定性・縮退・距離 0・近傍/非近傍、OC-15 変換ベクタ exact)+固定オラクル S-19/S-20
- SkiaSharp のリサイズはサンプリング指定(双線形)を明示し、**版を exact ピン**(53-service-bom)。版差で
  pHash が変わり得るため、版更新時は S-19 系の再凍結要否を判断する(S-BOM 監視項目)
- 16hex の係数レベル exact 値は本 ADR/実装で確定(本書外で閉じる)。spec は性質ベースで合否を閉じる
- ORB/detailed・OpenCvSharp は後続ループ。本 ADR は pHash-only の範囲に限る
