# ECO-144 — 保守上の約束を上流(要求台帳)で裁定し、承認済み E/S 版からサムネイル部品の検査を導出する(方法論 BomDD ECO-094 の試行)(staged)

- 種別: 工程拡張(要求台帳・E-BOM・Service BOM・Control Plan の対応付け。製品コード src は変更しない予定 — gate① で ① に B を選ぶ場合のみ別 ECO)
- status: **staged**(2026-10-03 起票 → gate① 裁定待ち)
- baseline: main `dc722a9`
- 出典: 方法論リポ BomDD ECO-094(外部レビュー 2026-10-02 論点 1・2・5 → maintainer 裁定「1:A」= 目標モデルを 1 機能で試行 → 2026-10-03「対象は A」= E-THUMB-020)。
  事前登録= `../BomDD/bomdd/reports/eco-094-upstream-sbom-trial/preregistration.md`(commit 5d90ba4・本起票より前)。
- 優先度: 中(製品の挙動は変えない。上流の約束と下流の検査の結線を試す)

## §1 要求

対象 1 機能= **E-THUMB-020 サムネイル生成**について、**保守上の約束**を maintainer の言葉で要求台帳(10)に固定し、その承認済みの E/S 版から
M-BOM / Control Plan の検査を導出して、要求 → E → M → CP → テスト → 実行証拠(cp_results の行)の追跡を 1 表にする。

約束の叩き台(gate① で文言を確定する — §4.1/§4.2):

| # | 約束(叩き台) | 現状との関係 |
|---|---|---|
| ① | 画像処理部品(SkiaSharp)は**交換しない部品**として宣言する(宣言された結合)。版は exact ピン(3.119.4)で固定し、版の更新は劣化イベント(53 DEG)として扱い、CP-THUMB-007 と pHash 系 CP を再検査してから採用する | 現状の宣言と一致(§3 ①〜③)。叩き台の初版「同等の部品へ交換できる」は現状と食い違ったため置き換え(§4.1 の裁定点) |
| ② | 画像処理部品の交換・更新後も、キャッシュ済みサムネイルは再生成なしで使い続けられる。生成規則が変わるときは世代サフィックスで共存し、旧世代は参照しない | CP-THUMB-007 の vector「キャッシュヒット: 再生成なし」「キャッシュ世代移行 -v2」が既に検査(ECO-049 で下流に決まった約束の上流化) |
| ③ | 壊れた画像・読めない画像があっても、スキャンと一覧は停止しない(その画像だけ null) | CP-THUMB-007 の vector「壊れた jpg → null・例外なし」(FMEA-012)が既に検査 |

**impact-prospective-01(手順 2b)**: 非適格・理由= 変更要求の原文が maintainer の言葉ではなく、AI の提案(候補 A/B)に対する選択(「A」)であり、
R を maintainer 原文で固定できない(ECO-143 と同型)。機会あり・非起動として記録する。

## §2 工程診断

| 工程 | 判定 | 根拠 |
|---|---|---|
| CAD(ViewPrismUI) | 該当なし | UI・視覚の変更を含まない |
| **要求台帳(10)・Service BOM(53)** | **沈黙(本 ECO の対象)** | 保守上の約束(交換の方針・継続・許容する損失)が要求として書かれていない。53 は `reinspect_on_change` と `replacement_decision_vocabulary` を持つが「何を約束するか」は持たない。-v2 の約束は ECO-049(欠陥是正)の中で決まった |
| BOM(30/32/33) | 健全・対応付けの欄なし | E-THUMB-020 は REQ-040/085 を参照し CP-THUMB-007 を持つ。33 の vector は約束 ②③ を既に検査するが、どの約束の検査かは書かれていない |
| 実装(src) | 変更しない(① が A の場合) | ① が B(交換可能性を目標に抽象境界を入れる)なら機能追加= 別 ECO |
| 検査器/台帳 | 欄の追加(candidate) | 53 の item に `service_requirement_refs`(上流の約束への参照)を 1 欄足す |

## §3 切り分け済みの事実(2026-10-03・統括 AI の実測・HEAD dc722a9)

確定:

1. **SkiaSharp は調達台帳で `substitutable: false`**(32-mbom procurement L776・ADR-0004)。
2. **SkiaSharp 固有識別子の層別出現**(`SkiaSharp|SK[A-Z]…` の grep・bin/obj 除外):

   | 層 | 出現 |
   |---|---|
   | src/ViewPrism2.Core | 4 ファイル(IFilePresenceProbe・IPHashImageReader・PerceptualHash・SpreadHeightCalculator) |
   | src/ViewPrism2.Infrastructure | 7 ファイル(ThumbnailService・OrientedImageLoader・PHashImageReader ほか) |
   | src/ViewPrism2.App | 3 ファイル |
   | tests | 24 ファイル |
   | 10-requirements | 5 / 20-spec 5 / 30-ebom 1 / 31-kbom 11 / 32-mbom 7 / 33-control-plan 1 / **41-fixed-oracle 0** |

   契約層(10/20/30)に出現し、オラクル(41)には出ない。20-spec §2.10 は「SkiaSharp は版差で出力が変わり得るため**版を exact ピン**(53)」「hash_adapter= decode 経路/SkiaSharp 版の世代識別子(P-09)」を宣言済み
   → 方法論 s-bom-template の交換クラスでは **oracle-coupled(宣言された結合)**。叩き台 ①「交換できる」は現状の宣言と食い違う。
3. **-v2 の約束は下流で決まった**: E-THUMB-020 invariants「since ECO-049(REQ-085) … キャッシュファイル名は生成規則の世代サフィックス付き(-v2)= 旧世代を参照しない」。REQ-040/085 にその文は無い。
4. **CP-THUMB-007 の vector は ②③ を既に検査**(キャッシュヒット再生成なし / -v2 世代移行 / 壊れた jpg → null)。テストは `tests/ViewPrism2.Tests/CpThumb007Tests.cs`・`CpThumb049ExifTests.cs`(trait cp=CP-THUMB-007)。
5. **E → M の参照は 0 件**(30-ebom の E-THUMB-020 に M-* の参照なし。M-THUMB-008 → E-THUMB-020・CP-THUMB-007 の向き)— BomDD 事前登録 M4 の起票時点の値。
6. **validate_bom.py は 10-requirements と 53 を意味的に検査しない**(REQ・service_bom の参照を持たない)— 新しい `classification_hint: maintainability` と 53 の新欄は検査器を壊さない(構文のみ)。

疑い(未検証):

- ① を A にした場合に導出される「版 exact ピンの検査」は、既存のテストに相当する行があるか未確認(csproj の版と 32 procurement の一致を見る検査は見当たらない)。導出時(手順 3)に決める。

スコープ外所見(R3・記録のみ): `bomdd/00-manifest.yaml` の `eco_range: ECO-001 .. ECO-016`・`open_eco: [ECO-007]` は現状(ECO-143 まで applied)と乖離している。本 ECO では触れない。

## §4 是正方針(案・gate① で 4.1 を裁定・4.2 を確認)

### 4.1 gate① 裁定点 — 約束 ① の文言

- **A(推奨)**: 「SkiaSharp は交換しない部品(宣言された結合)。版は exact ピン・更新は劣化イベントとして CP-THUMB-007 と pHash 系 CP を再検査してから採用」。
  現状の宣言(§3 ①②・20-spec §2.10)と一致し、製品コードを変えない。試行の目的(約束の上流化と導出の追跡)に閉じる。
- **B**: 「同等の寸法・形式を出す部品へ交換できる」を目標として維持し、抽象境界(画像処理のインターフェース)を導入する。= 機能追加(Core 4・Infrastructure 7 ファイルの改修)で、本試行の範囲外・別 ECO。

### 4.2 確認点 — 約束 ②③ の文言(現状の検査と整合・文言の確認のみ)

②③ は §1 の叩き台のまま REQ 化する。MODIFY があれば文言を差し替える。

### 4.3 実装(案・① が A の場合・/eco-fix で実施)

1. **REQ 追加**(10-requirements・REQ-104〜106・`classification_hint: maintainability`・rationale は根拠精度 G1〔受入の深さと許容差〕まで): ① ② ③。
2. **E/S の結線**: E-THUMB-020 の `requirement_refs` に REQ-104〜106 を追加。SB-THUMB-020 に `service_requirement_refs: [REQ-104, REQ-105, REQ-106]`(candidate 欄)と、① の宣言(`replacement_policy: declared-coupling`〔宣言された結合〕)を追加。
3. **設計リリース**: 00-manifest の `bom_version` を v4.0 → **v4.1** にし、tag `bom-v4.1` を gate① 裁定の記録 commit(`decide(eco-144):`)に付ける= 承認済み E/S 版。
4. **導出(統括 AI)**: CP-THUMB-007 の characteristic と test_vectors に REQ-104〜106 との対応を書く。① の検査行(版 exact ピン= csproj の SkiaSharp 版と 32 procurement の一致・更新時の再検査経路)は、
   既存の検査に相当するものが無ければ **新規 vector 1 本+テスト 1 本**(tests のみ・src 不変)。落ちたときの振り分け(製品修正 / 測定系復旧)を各 vector に書く。
   人へ戻す判断は「新しい機能・新しい保守の約束・導出不能」の 3 種だけ(BomDD 事前登録 M1)。
5. **機械受入(4 点)+ CP 行ごとの表**(eco-fix 手順 3/3.1)。
6. **追跡表**: REQ-104〜106 → E-THUMB-020 → M-THUMB-008 → CP-THUMB-007(vector)→ テスト名(trait)→ cp_results の行(実行の素性・総数)を 1 表に(BomDD 側 reports/eco-094-upstream-sbom-trial/trace.md)。
   構造(辺の実在)は機械・期待値の意味(約束を検査しているか)は maintainer の審査欄。
7. **リハーサル(M3)**: 導出した vector 1 本を意図的に赤(製品側の違反)と測定不能(Skip)にして cp_results の区分と承認依頼の文面を観測(一時変更・commit しない)。
   欠測・未知 CP ID を含む依頼のリハーサル(方法論 EXP-20261002-01 の補助注記)も同時に行う。

### 4.4 試行の評価(事前登録= BomDD preregistration.md・本 ECO では再定義しない)

M1 人へ戻った判断(種別)/ M2 追跡の 5 辺 / M3 振り分け 2/2 / M4 E→M 参照 0。加えて本 ECO は ECO-143 §4.4「次の 3 件の承認」の 1 件目— 承認の場で CP 行ごとの表に触れたか・処置が起きたかを register 注記に残す。

## §5 影響 BOM(案)

| 成果物 | 変更 |
|---|---|
| 10-requirements.yaml | REQ-104〜106 追加(maintainability) |
| 30-ebom.yaml | E-THUMB-020 requirement_refs 追加 |
| 53-service-bom.yaml | SB-THUMB-020 に service_requirement_refs・replacement_policy(candidate 欄) |
| 33-control-plan.yaml | CP-THUMB-007 characteristic/test_vectors に REQ 対応・① の vector(新規の場合) |
| 00-manifest.yaml | bom_version v4.1(+tag bom-v4.1) |
| tests | ① の検査が新規なら 1 テスト追加(trait cp=CP-THUMB-007) |
| src | **0**(① が A の場合) |
| 既存固定オラクル(41) | 変更しない(R6) |

## §6 残ゲート

- **gate①(maintainer)**: 4.1 の ①(A / B)の裁定と 4.2 の ②③ の文言確認。
- fix(/eco-fix): 4.3 の 1〜7 → 機械受入 4 点+CP 行ごとの表 → R8 セルフレビュー(tests に触れる場合)→ 停止。
- gate②: 視覚・挙動の変更なし= golden n/a 候補(ECO-143 と同型)。承認の場で表に触れたかを記録。
