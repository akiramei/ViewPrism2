# Change Order — ECO-048(applied): 類似検索の回転・鏡像・クロップ耐性 — SimilarPic 検証済みアルゴリズムの BCL 移植を候補とする機能拡張

> maintainer 要求(2026-07-06)。別リポ `../SimilarPic` で実装・検証した類似画像判定ライブラリの
> 知見を ViewPrism2 の類似検索(Loop V3)へ還流する機能拡張。起票時に工程診断を実施済み。
> **欠陥是正ではない**(§2 — 現行実装は要求台帳・charter-v3 裁定に完全適合)。

## 1. 要求(maintainer・2026-07-06)

- 現行の類似検索(pHash 単独)は、**回転(90/180/270°)・鏡像・クロップされた同一ソース画像を
  検出できない**。これらを検出できるようにしたい。
- 移植候補: `../SimilarPic`(2026-07-06 作成・git 未初期化)。
  pHash+dHash+Edge ヒストグラム+Tile ハッシュ+Visual stats の重み合成
  +8 オリエンテーション(Identity/Rotate90/180/270/FlipH/FlipV/Transpose/Transverse)総当たり
  +ORB 二段幾何検証(OpenCvSharp・スコア 0.66–0.82 の Possible 帯のみ遅延実行)。
  検証コンソール 112 ケース(回転/鏡像/グレースケール/セピア/ノイズ/ブラー/クロップ 65–80%)全パス。

## 2. 工程診断 — 欠陥ではなく要求拡張(上流=要求台帳から入る)

| 工程 | 判定 | 根拠 |
|---|---|---|
| 要求(10-requirements) | **要求が存在しない** | REQ-061〜067(Loop V3 類似検索+マージ)に回転・鏡像・クロップ耐性の要求なし。charter-v3 §スコープ「含まない」+裁定記録 2(2026-06-13 maintainer)で ORB/detailed モードは**明示 defer**(OpenCV ネイティブ依存回避) |
| CAD(ViewPrismUI) | 健全(最小案では改訂不要) | 類似検索 UI(閾値・結果一覧)は定義済み。検出耐性の向上は同一 UI の裏側の変化。detailed モード新設(案 C)を選ぶ場合のみ CAD 改訂が先 |
| BOM(30-ebom) | 健全 | E-PHASH-031/E-SIMSEARCH-032/E-SIMCACHE-033 の宣言は現行仕様(pHash 単独)と整合 |
| 実装 | **仕様に完全適合(逸脱なし)** | §3。単一オリエンテーション pHash は仕様 §2.10.1(OC-14)・ADR-0008 どおり |

- 結論: **新 REQ の起こし(要求新設)から入る機能拡張**。混入コミット・潜伏期間の概念は非該当。
- charter-v3 裁定記録 2(ORB defer)との関係: 案 A/B は OpenCV 非依存のため**裁定を覆さない**。
  ORB 二段検証(案 C)のみ裁定の再裁定+新 ADR が必要(charter-v3 §前提「将来 ORB 採用時に別 ADR」)。

## 3. 切り分け済みの事実(確定と未検証を分離)

確定(コード読解・台帳):

- 現行は**単一オリエンテーションの pHash 1 本のみ**: `PerceptualHash.Compute`
  (src/ViewPrism2.Core/Services/Similarity/PerceptualHash.cs:38 — 32×32 行優先 DCT・変種生成なし)、
  検索は保存済み phash 同士のハミング距離のみ(SimilaritySearchService.cs:89 `ComputePairScoreAsync`)。
- DCT-pHash は 90° 回転で係数配置が転置・鏡像で符号反転し 64bit が大きく乖離、
  クロップは低周波構造自体が変わる=**原理的に検出不能**(解析的事実。pHash の既知特性)。
- 台帳側もこれを要求していない: 仕様 §2.10.1 のレシピは単一向き固定・INV-012(決定性)で丸めまで固定。
  既存オラクル(loop-v3-r1 系: pHash 計算/ハミング距離/類似度変換)は現レシピを凍結済み。
- SimilarPic 側の実測(外部リポ・参考証拠): 回転/鏡像=8 変種の最良スコア採用で Similar 判定、
  クロップ 65–80%=Tile ハッシュ+ORB で Possible 以上、無関係画像/平坦色/ランダムノイズ=非類似
  (`artifacts/validation/report.md` 112 チェック全パス)。うち **OpenCV 依存は ORB のみ** —
  dHash/Edge/Tile/stats/8 オリエンテーションは BCL で完結する設計。

未検証(疑い — 着手時プローブで実測):

- 実ユーザーデータでの取りこぼし頻度(回転・クロップ済み重複がどの程度存在するか)は未計測。
- 8 オリエンテーション化のコスト(特徴量計算 8 倍 or 保存 8 ハッシュ)が既存キャッシュ設計
  (image_features/image_similarity・内容ベース無効化)に与える影響は見積のみ。
- SimilarPic の重み(Global/CropResilient 合成)・しきい値(0.82/0.66)は SimilarPic のサンプル母集団
  (漫画ページ/キャラアート/ゲーム風景/UI)で調整済み — ViewPrism2 の 0–100 段階式類似度
  (SimilarityScore 区分線形)への写像は設計が必要。

## 4. 是正方針(案 — gate① 裁定で確定)

前提(全案共通の制約):

- **SimilarPic をライブラリ参照(NuGet/ProjectReference)することはできない**:
  `net10.0-windows`+System.Drawing(GDI+)+OpenCvSharp win ランタイム= Windows 専用で
  Avalonia クロスプラットフォーム構成・ADR-0002 層規律(Core は BCL のみ)と衝突。
  永続化 API なし・単体テスト基盤なし。**活用形態は「検証済みアルゴリズムの移植」一択**。
- 既存固定オラクル(R6)は不変: pHash レシピ(OC-14)・段階式類似度(OC-16)の既存行は触らない。
  新特徴量は新規オラクル行+新 adapter 世代(`hash_adapter` 列・P-09)として追加し、
  既存の内容ベース無効化・世代管理に乗せる(再計算は自動)。

| 案 | 内容 | diff 規模 | golden 影響 |
|---|---|---|---|
| **A: 回転・鏡像のみ(最小)** | pHash の 8 オリエンテーション化のみ移植。保存時に 8 変種ハッシュを計算し image_features へ格納(migration)、ペア類似度=8×8 でなく基準 Identity×候補 8 変種の最小距離(SimilarPic 方式)。クロップ・色調耐性は対象外 | 中(Core 変種生成+DB 列+検索 1 箇所) | 小(検索結果の増加のみ・UI 不変) |
| **B: 多特徴量合成(SimilarPic フル・ORB 除く)** | A に加え dHash/Edge/Tile/Visual stats を BCL 移植し重み合成スコア。クロップ 65–80%・色調変化も検出。0–100 類似度への写像設計+しきい値再調整が必要 | 大(Core 新特徴量 4 種+合成+DB+写像設計) | 中(スコア意味論が変わる=閾値既定 70 の体感変化・UI 構造は不変) |
| **C: B+ORB 二段検証(detailed モード)** | charter-v3 裁定 2 の**再裁定が必要**。OpenCvSharp ネイティブ依存を Infrastructure に opt-in 導入+新 ADR。クロップ 65% 級の確度が上がる | 特大 | 中+モード UI(CAD 改訂先行) |
| **D: 見送り(現状維持)** | 実害(取りこぼし実測)が確認されるまで defer 継続 | 0 | なし |

- 推奨: **A で起点を作り、B は A の実測(プローブ+golden)を見て別 ECO で積む**。
  A は既存レシピ・オラクル・UI を一切変えず「検出漏れが純減する」方向の拡張で、
  R6/INV-012 との折り合いが最も単純。B のしきい値・写像設計は A の運用実測が入力になる。
- いずれの案でも着手時に新 REQ 採番(REQ-084〜 見込み)+仕様 §2.10 追補+新規オラクル行凍結。

## 5. 影響 BOM(案により変動 — 着手時確定)

- 案 A: E-PHASH-031(変種生成)/ E-SIMSEARCH-032 / E-SIMCACHE-033+E-DB-010
  (image_features 拡張= migration・62 適用=ECO-020/044 前例)/ M-PHASH-020 / M-SIMSEARCH-021 /
  10-requirements(新 REQ)/ 20-spec §2.10 / 41-fixed-oracle(新規行)。UI・CAD 不変。
- 案 B: 上記+ Core 新特徴量 unit(E-BOM 新設候補)+ SimilarityScore 写像+ E-UI-SIMILARITY-035(閾値意味論)。
- 案 C: 上記+ K-BOM/S-BOM(OpenCvSharp ピン)+新 ADR + CAD(モード UI)。

## 6. 残ゲート

1. ~~**gate①(裁定・human)**: 案 A/B/C/D の選択(§4)~~ → **受領: 案 A**(maintainer 2026-07-06)
2. ~~裁定後: /eco-fix eco-048 — プローブ先行→新 REQ 採番→是正→機械受入~~ → 完了(§7)
3. **golden(maintainer 実機)**: 回転・鏡像した重複画像が類似検索で検出される(§7 末尾の基準)。
4. クローズ時: CP 観点明記+register applied+教訓。

## 7. 実施記録(2026-07-06 — 案 A 実装・機械受入完了・golden 待ち)

- **gate① 裁定**: 案 A(8 オリエンテーション pHash — 回転・鏡像の最小移植。クロップ・多特徴量・ORB は対象外のまま)。
- **プローブ先行(R5・実測裏取り)**: CpSim048OrientationTests に実 decode 経路(production reader
  PHashImageReaderScaledDecode)+一時 PNG の E2E 回帰テストを是正前に追加し実行 —
  **回転 90°=不合格(検索結果 空)・鏡像=不合格(検索結果 空)・対照(同一複製)=合格(score 100)**。
  既存 552 テストは全緑。単一オリエンテーション pHash では原理的に検出不能(§3)を実測で固定してから着手。
- **是正の裁定(折り合い 2 点 — 起票時想定からの変更)**:
  - **adapter 世代は bump しない**: 起票時は「新 adapter 世代」を想定したが、Oracle の
    ThisBuildGoldenTests が production AdapterId(`skia-scaled-decode-v1`)と pHash golden 値を凍結
    しており bump は R6 抵触。8 変種の追加は identity pHash の絶対値を動かさない= P-09 の
    世代交代発動条件に該当しないため、旧レコードの自動アップグレードは**変種欠落= stale** の
    無効化条件追加(IsFresh の第 3 条件)で実現(仕様 §2.10.3 追補)。
  - **インターフェース非破壊**: Oracle S-21/S-22 が IPHashImageReader を private fake で実装しており
    メソッド追加は Oracle 改変を強制する。**default interface method**(SupportsOrientationVariants=false
    既定+ComputePHashVariantsAsync=null 既定)で既存実装・fake を無改変に保つ
    (ECO-046 optional 注入= V4 CHEAT-01 と同系の折り合い)。
- **実装(上流→下流)**:
  - 台帳: REQ-084 新設・spec §2.10.1a(変種 8 種の順序固定=D4)/§2.10.3(phash_variants 列+無効化第 3 条件)/
    §2.10.4(ペア距離規則)追補・S-40 追加(41)・E-BOM 宣言補完(E-PHASH-031/E-SIMSEARCH-032/E-SIMCACHE-033)。
  - Core: PHashOrientations 新設(32×32 格子の添字置換のみ= BCL・決定的・[0]=identity はレシピ pHash と一致)。
    SimilaritySearchService — ペア距離=**序数比較で小さい id の identity × 大きい id の全変種の最小**
    (役割が id 順で決まるため対称=ペア正規化キャッシュと整合・変種[0]を含むため単調拡張)。
    変種なし特徴量とは identity 同士のみ(後方互換)。
  - Infrastructure: 両 reader(full/scaled)にデコード共通化+ComputePHashVariantsAsync 実装。
    migration 006(image_features.phash_variants TEXT NULL)+ LatestDdl 同値(CP-DB-006 前例踏襲)。
  - UI・CAD: 無変更(検索 UI の裏側の変化のみ)。
- **機械受入(4 点全緑)**: build 0 error/0 warning・**Tests 559/559**(552+7 新規: E2E 回転/鏡像/対照・
  対称性・stale アップグレード・後方互換・L2 列)— **プローブ 2 件は合格に転化**・
  **Oracle 104+2skip**(102+S-40 2 件・凍結オラクル回帰ゼロ= S-21/S-22/S-25 順位等価/ThisBuildGolden 全緑)・
  validate_bom 0 error/0 warning。
- diff: Core 3 ファイル(+1 新設)・Infrastructure 3・Tests 2(+1 新設)・Oracle +1 新設(S-40)・bomdd 5。

### golden 合格基準(gate② — maintainer 実機)

1. 画像タブで任意の画像を 1 枚選び類似検索 → **その画像を 90° 回転して保存した複製**(ペイント等で回転)が
   既定閾値 70 で結果に出る(高スコア)。
2. 同様に**左右反転した複製**が結果に出る。
3. 回帰: 従来検出できていた「同一/再エンコードの複製」が引き続き検出される(スコア低下なし)。
4. 回帰: 無関係な画像が新たに紛れ込まない(結果の妥当性が体感で崩れていない)。
※ 既存 DB の特徴量は初回検索時に自動で再計算される(変種欠落= stale)。初回のみ計算時間が延びる。

**golden 準備時の注意(2026-07-06 実測)**: 回転ツールの多く(EXIF タグ方式)はピクセルを回さず
EXIF Orientation タグのみ書き換えるため、テストにならない(ピクセル不変= hash/pHash 不変)。
テスト用にはピクセル実回転の複製を使う(orientation_fixture_06_rot90.jpg / orientation_fixture_06_mirror.jpg を生成済み —
System.Drawing RotateFlip・EXIF タグ除去済み)。この過程でスコープ外所見
**「EXIF Orientation が全デコード経路で非反映(スマホ撮影画像が横倒し表示)」**を発見 —
ECO-048 とは独立の欠陥様式として 51-cheat-log へ R3 記録(起票要否は maintainer 判断)。

## 8. クローズ(2026-07-06 golden 合格)

- **maintainer 実機確認**: ①90° 回転複製=閾値 70 で検出 ②左右反転複製=検出(整理トレイ既定 80 でも)
  ③png→jpg 変換複製= 78 点で検出。maintainer 所見「直感では 90 以上のはず」→ 実測切り分け: 知覚的実距離は
  2(=96 点・full-decode 実測)で直感が正、-18 点は scaled-decode の JPEG 早期縮小/PNG 全解像度一発縮小
  という**経路系統誤差**(51 R3 #2 へ分離)④無関係画像の混入なし。
- **再発防止(CP 観点明記)**: CP-PHASH-016(S-40 変種契約)・CP-SIM-017(回転/鏡像 E2E・ペア対称性・
  変種欠落 stale・後方互換)・**CP-UI-G9(golden 入力は必ずピクセル実変換の複製で作る=EXIF タグ回転が
  検査を無効化した実績・確認閾値は仕様既定 70)**。
- **M4 同期**: M-PHASH-020(PHashOrientations 追加・orientations 契約)・M-SIMSEARCH-021(DIM 拡張・
  ペア距離規則・phash_variants/migration 006)・沈黙次元(無効化方式=内容+adapter+変種欠落)。
  spec/E-BOM/41 は fix 時に同期済み。35-dsbom 不要(surface 新設なし)。
- **golden 中の R3 所見 3 件(51-cheat-log 記録済み・起票要否= maintainer 判断)**:
  1. EXIF Orientation が全デコード経路で非反映(スマホ撮影画像が横倒し表示)
  2. 類似しきい値の既定値が三者三様(モーダル 70=仕様どおり/整理トレイ 80/作業タブ 90・CAD 数値未規定)
  3. scaled-decode のフォーマット間系統誤差(異フォーマット複製が約 -18 点。是正= P-09 世代交代を伴う)
- **教訓(一般形)**: **拡張は「凍結面の棚卸し」から** — 固定オラクルが凍結しているのは期待値だけでなく
  **識別子(AdapterId・golden 値)とインターフェース実装面(fake が implements する契約)**も含む。
  正規の無効化機構(adapter 世代交代)ですら凍結面と衝突するなら、等価な効果を持つ別の非破壊面
  (変種欠落= stale 条件の追加・default interface method)で増築する。ECO-045 O-a(入口 1 行折り合い)・
  ECO-046 CHEAT-01(optional 注入)に続く「R6 と両立する拡張」の第 3 例= read-across 系譜。
  副教訓: **golden は入力データの正当性から較正する** — 「見た目が変わったがピクセル不変」(EXIF タグ回転)の
  入力は検査を無効化する。プローブと同様、golden 入力も実測(寸法・EXIF・ハッシュ)で裏取りしてから走らせる。
