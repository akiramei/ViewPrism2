# Change Order — ECO-049(staged): EXIF Orientation の表示系適用 — V1 裁定済み沈黙次元(defer)の解除(スマホ撮影画像の横倒し表示)

> maintainer 実機所見(2026-07-06・ECO-048 golden 準備中に発見・51-cheat-log R3 記録から昇格起票)。
> **欠陥是正ではない**(§2 — 現行挙動は V1 裁定済み沈黙次元「適用しない・後続ループ候補」どおり)。

## 1. 症状(maintainer 実機・2026-07-06)

- EXIF Orientation=6(90° 回転指示)を持つ JPEG(orientation_fixture_06.jpg・スマホ撮影/タグ方式回転ツールの出力)が、
  サムネイル・ビューアとも**横倒しで表示**される。他ツール(Windows フォト等)では正立表示。
- 実測: ピクセル 1194×834・EXIF Orientation=6。ViewPrism2 の全デコード経路は EXIF を適用しない。
- 実害: スマホ撮影画像は Orientation≠1 が一般的で、コレクションに取り込むと横倒しが常態化する。

## 2. 工程診断 — 欠陥ではなく「裁定済み defer の解除」(上流=仕様の沈黙次元表から)

| 工程 | 判定 | 根拠 |
|---|---|---|
| 仕様(20-spec §4 沈黙次元表) | **裁定済み defer** | L974「画像の EXIF 回転(Orientation)/ specified / **V1 では適用しない(原典も未適用)。後続ループ候補**」 |
| K-BOM(31-kbom) | 宣言整合 | L36 K-SKIA「EXIF Orientation は適用しない(仕様 §4 で宣言済み。SKCodec の origin を無視してよい)」 |
| CAD(ViewPrismUI) | 規定なし | 画像の向きに関する記述なし(grep 実測)。「画像を正しく表示する」の暗黙下位事項 |
| 実装 | **仕様に完全適合(逸脱なし)** | §3 — 全デコード経路が宣言どおり EXIF 非適用 |

- 結論: **V1 裁定の解除(要求新設)から入る機能拡張**。ECO-048 と同型(混入コミット・潜伏の概念は非該当)。
- **裁定前提への疑義(未検証)**: V1 裁定の根拠「原典も未適用」は「原典コードに EXIF 処理なし
  (spec L1131: exifreader 依存はあるが利用コードなし)」の意。しかし原典は Electron であり、
  Chromium の `<img>` は既定(`image-orientation: from-image`)で **EXIF を自動適用する** —
  つまり**原典の表示挙動は正立だった可能性が高い**(ブラウザ任せの暗黙機能)。真なら本件は
  「コード無し=挙動無し」の誤同一視による**移植等価性の隠れ回帰**であり、defer 解除の妥当性を補強する。
  確認は原典リバース(設計者側で可)。

## 3. 切り分け済みの事実(確定と未検証を分離)

確定(コード読解・実測):

- EXIF 非適用のデコード面は 4 系統:
  1. **サムネ生成**: ThumbnailService.Generate(ThumbnailService.cs:106 `SKBitmap.Decode` — origin 不読)。
     生成物(キャッシュ PNG/JPEG)自体が横倒し → ThumbnailImage(App)は生成物をそのまま表示。
  2. **ビューア**: ViewerImage.cs:214 / ViewerWindow.axaml.cs:284 の `new Bitmap(path)` =
     **Avalonia Bitmap の原本直読**(Avalonia は EXIF orientation を適用しない)。
  3. **寸法メタ**: ThumbnailService.GetDimensionsAsync(:57 `codec.Info.Width/Height`)= 生ピクセル寸法。
     Orientation 5〜8 では実効寸法(W/H 入替)と食い違う。
  4. **pHash 入力**(PHashImageReader 系)— ただし ECO-048 の 8 変種により回転・鏡像は検出可能で、
     EXIF 違いのみの複製はピクセル同一=距離 0。**pHash 面の実害は既に解消済み**。
- **サムネキャッシュのキーは MD5(小文字絶対パス)のみ**(ThumbnailService.cs:70)で内容・生成規則の
  世代を含まない+「存在すれば再生成しない」(REQ-040)。→ EXIF 適用を実装しても**既存キャッシュが
  恒久的に旧(横倒し)サムネを返し続ける**ため、キャッシュ世代移行の設計が必須(pHash P-09 のサムネ版)。
- SKCodec は `EncodedOrigin` で EXIF orientation(8 方位= D4)を公開する — 適用は SkiaSharp の
  既存 API で完結し、変換族は ECO-048 PHashOrientations と同じ 8 方位(新規依存なし)。
- SimilarPic ExifOrientation.cs は System.Drawing ベースのため**直接移植は不可**(ADR-0002/クロスプラットフォーム)。
  借りるのは「8 方位→変換」のマップ知見のみで、実装は SKCodec.EncodedOrigin ベースの新規。

未検証(疑い):

- 原典(Electron)の表示挙動が EXIF 適用(正立)だったか(§2 の疑義 — 原典リバースで確認可能)。
- 固定オラクル・CP のサムネ検査に EXIF 付きフィクスチャが存在するか(恐らく無し=既存行影響なしの見込み。
  着手時に実測)。
- 詳細パネルの解像度表示が GetDimensionsAsync をどこで使うか(REQ-043 は ECO-023 で撤回済み・
  コメントの参照は残置)— 案 B の影響範囲確定は着手時。

## 4. 是正方針(案 — gate① 裁定で確定)

| 案 | 内容 | diff 規模 | golden 影響 |
|---|---|---|---|
| **A: 表示系のみ(推奨)** | ①サムネ生成に EXIF 適用(SKCodec.EncodedOrigin→正立変換)+**キャッシュ世代移行**(キー or ディレクトリに世代を導入・旧キャッシュは孤児化) ②ビューアの原本直読を正立デコード経路へ差し替え(Infrastructure に正立ローダ新設・Avalonia Bitmap into memory)。pHash・SHA-256・スキャンは不変(P-09 非発動) | 中 | 中(EXIF 画像の正立化=見た目が変わるのは対象画像のみ) |
| **B: A+実効寸法** | GetDimensionsAsync が Orientation 5〜8 で W/H を入替えて返す(詳細パネルの解像度が実効値に) | A+小 | A と同 |
| **C: A/B+pHash 入力正規化** | **非推奨**: P-09 世代交代(ThisBuildGolden 再凍結・全特徴量再計算)を伴うが、ECO-048 変種により利得ほぼ無し(EXIF 違い複製は距離 0・回転済み複製も変種が吸収) | 大 | A と同+初回検索の全再計算 |
| **D: 見送り** | V1 裁定(適用しない)を維持 | 0 | なし |

- 推奨: **B**(A+実効寸法)。A を選ぶ理由(横倒し解消)が寸法表示の食い違いにも同根で当てはまり、
  追加 diff が小さい。C は明確に切り離す(類似検索は ECO-048 で既に向き耐性あり)。
- いずれの案でも: 新 REQ 採番(REQ-085 見込み)+ spec §4 沈黙次元表の更新+ K-SKIA(31-kbom L36)の
  宣言変更+41 新規行(EXIF 適用の決定性=同一 orientation 入力→同一出力)。既存オラクル行は不変(R6)。

## 5. 影響 BOM(案 B 前提・着手時確定)

- M-THUMB-008(ThumbnailService — 生成+寸法+キャッシュ世代)/ E-THUMB-020。
- ビューア表示: M-VIEWERCORE-017 / M-UI-019 系(ViewerImage/ViewerWindow の読込経路)+ E-UI-VIEWER-024。
- 台帳: 10-requirements(新 REQ-085)・20-spec §4 沈黙次元表+表示節・31-kbom K-SKIA・
  41-fixed-oracle(新規行)・33(CP-THUMB 系観点+golden CP)。
- 不変: pHash 系(E-PHASH-031/E-SIMSEARCH-032)・DB スキーマ(migration 不要 — サムネキャッシュはファイル)。

## 6. 残ゲート

1. ~~**gate①(裁定・human)**: 案 A/B/C/D の選択~~ → **受領: 案 B**(maintainer 2026-07-06。V1 裁定の解除を兼ねる)
2. ~~裁定後: /eco-fix eco-049 — プローブ先行 → 是正 → 機械受入~~ → 完了(§7)
3. **golden(maintainer 実機)**: §7 末尾の基準。
4. クローズ時: CP 観点明記+register applied+教訓。

## 7. 実施記録(2026-07-06 — 案 B 実装・機械受入完了・golden 待ち)

- **gate① 裁定**: 案 B(表示系の EXIF 適用=サムネ+ビューア+キャッシュ世代移行+実効寸法。
  pHash 入力は非適用のまま= P-09 非発動)。
- **プローブ先行(R5・実測裏取り)**: CpThumb049ExifTests — EXIF Orientation=6 の JPEG フィクスチャを
  テスト側で合成(SkiaSharp エンコード後、SOI 直後へ APP1(Exif/TIFF/IFD0: 0x0112=6)を手書き挿入 —
  独立実装)し是正前に実行: **サムネ正立=不合格(横倒しのまま)・実効寸法=不合格(生ピクセル寸法)**。
  対照(EXIF なし)+前提較正(SKCodec.EncodedOrigin=RightTop 読取)=合格 → 真因を実測固定。
- **実装(上流→下流)**:
  - 台帳: REQ-085 新設・spec §2.5 追補(EXIF 表示系適用+キャッシュ名 `-v2` 世代)+§4 沈黙次元表更新・
    31-kbom K-SKIA 宣言差替(v4.1)・S-41 追加(41)・E-BOM(E-THUMB-020/E-UI-VIEWER-024)宣言補完。
  - Infrastructure: **ExifOrientationTransform** 新設(D4 添字置換・変換表は ToUprightBgra に一本化・
    実効寸法)+ **OrientedImageLoader** 新設(TopLeft は null=従来直読の高速経路・正立時のみ
    BGRA ピクセル列を返す — SkiaSharp は Infrastructure に閉じる=ADR-0002)。
    ThumbnailService= codec 経由デコードで origin 取得→**縮小後の小画像に正立化適用**(一様スケールと
    D4 は可換=仕様 §2.5)+キャッシュ名 `-v2` 世代サフィックス+ GetDimensionsAsync 実効寸法。
  - App: **OrientedBitmaps**(正立ピクセル→ WriteableBitmap・TopLeft は従来 `new Bitmap(path)`)を
    ViewerImage:214 / ViewerWindow:284 の 2 箇所(原本直読の全数)へ配線。例外面は従来と同一
    (呼び出し側 catch 節無変更)。
- **機械受入(4 点全緑)**: build 0 error/0 warning・**Tests 565/565**(562+3: 世代移行=旧キャッシュ
  非参照・正立ローダ内容検査・TopLeft null)— **プローブ 2 件は合格に転化**・**Oracle 107+2skip**
  (104+S-41 3 件: 8 方位×独立実装 exact・実効寸法・E2E。凍結オラクル回帰ゼロ= S-10 含む)・
  validate_bom 0 error/0 warning。
- diff: Infrastructure 3(+2 新設)・App 3(+1 新設)・Tests 1 新設・Oracle 1 新設(S-41)・bomdd 6。

### golden 合格基準(gate② — maintainer 実機)

1. **EXIF=6 の実画像(orientation_fixture_06.jpg)が正立表示になる**: コレクション再スキャン後(または初回表示で
   新世代サムネが自動生成される — 旧サムネキャッシュは参照されない)、①一覧のサムネイルが正立
   ②ビューア(クリックで開くフルサイズ)が正立。
2. 詳細パネルの解像度が**実効寸法(834×1194)**になる(元は 1194×834+Orientation=6)。
3. 回帰: EXIF なしの既存画像のサムネ・ビューア表示が従来どおり(向き・画質の劣化なし)。
   ※ 全画像のサムネが世代移行で再生成されるため、初回表示のみ生成時間がかかる。
4. 回帰: ビューアの見開き・スクロール・タグ制御モードの表示が正常(読込経路の差し替えが
   全モードに波及するため — ECO-037 教訓の裏面検査)。
