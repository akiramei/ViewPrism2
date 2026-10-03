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

### 4.2' gate① 裁定(maintainer・2026-10-03)— **1:A 2:OK**

- ① = **A**(SkiaSharp は交換しない部品= 宣言された結合。版 exact・更新は DEG として再検査)/ ②③ = 叩き台のまま確定。
- **承認済み E/S 版の固定(同日・`decide(eco-144)` commit・tag `bom-v4.1`)**: REQ-104(①)・REQ-105(②)・REQ-106(③)を 10-requirements に追加(`classification_hint: maintainability`・受入の深さと落ちたときの振り分けを rationale に明記)/
  E-THUMB-020 の `requirement_refs` に REQ-104〜106・invariants に REQ の対応 / SB-THUMB-020 に `service_requirement_refs: [REQ-104, REQ-105, REQ-106]`・`replacement_policy: declared-coupling`(candidate 欄)/
  00-manifest `bom_version: v4.1`。src・tests・33・41 は本 commit で不変(導出は fix で)。
- 人へ戻した判断の記録(BomDD 事前登録 M1): 1 件目= ① の文言(種別: 新しい保守の約束)。②③ は確認のみ(戻した判断に数えない)。

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

## §7 実施記録(/eco-fix・2026-10-03)

**役割**: 約束の確定= maintainer(gate① 1:A 2:OK)/ 導出(33 の vector・53 の方針欄・M の対応確認)= 統括 AI(BomDD 側 producer・claude-fable-5-1)/ 検査の製造= 工場 Codex(gpt-5.6-sol・ブリーフと報告は
BomDD `bomdd/reports/eco-094-upstream-sbom-trial/factory-brief-eco-144.md`・`factory-report-eco-144.md`)/ 受理(機械受入・リハーサル・R8)= 統括 AI。

**diff**(fix commit): `bomdd/33-control-plan.yaml`(CP-THUMB-007: characteristic・tolerance・fixture・oracle に約束 3 件の対応、既存 vector 4 本に【REQ・振り分け】の付記、新規 vector 1 本「版の一致」)・
`tests/ViewPrism2.Tests/CpThumb144VersionPinTests.cs`(新規 130 行・3 Fact・trait cp=CP-THUMB-007)・本 order・register。**src・既存テスト・41・csproj は無変更**(R6)。
承認済み E/S 版(10・30・53・manifest)は decide commit 9f32ef4(tag bom-v4.1)で先に固定済み。

**導出の記録**(BomDD `derivation.md`): 人へ戻した判断= 1 件(① の文言・新しい保守の約束)・機械的派生 0。導出 D1〜D6(M unit の新設なし・CP 行は既存行に vector を足す形・53 に `replacement_policy`)。

**工場の報告**: BLOCKED(sandbox の NuGet 接続拒否 NU1301 でフィルタ実行が走らず)— ただし build 0 error / 0 warning・生成済みアセンブリでの同一クラス検査 3/3 合格・`git status` は製造物 1 本+受理側の 33 のみ。
ずる報告(慣習で補完した判断・全件): 抽出正規表現の具体形 / 3 Fact への分割 / メソッド名 / 定数・Versions record・共通抽出ヘルパの構成 / 失敗メッセージの文言 / 読み取り例外を IOException・UnauthorizedAccessException に限定。
受理側の判定: いずれもブリーフが「慣習で埋めてよい」範囲(実装の形)— 契約(3 ファイル・同一・exact・測定系復旧の分離・期待値リテラルを書かない)は満たしている(読解+下記の実測)。

**R5(プローブ先行)**: 本 ECO は欠陥是正ではなく検査の追加(拡張)— R5 対象外を宣言し、代替= リハーサル(下記)で「赤になるべき入力で赤になる」ことを実測した。

**機械受入(4 点)**(受理側・2026-10-03): `dotnet build` 0 エラー / 0 警告 / `dotnet test tests/ViewPrism2.Tests` **977/977**(974+新規 3・skip 0)/ `dotnet test tests/ViewPrism2.Oracle` 109 合格+skip 4(ECO-143 と同数・無接触)/
`python bomdd/validate_bom.py` 0-0。

**CP 行ごとの表(eco-fix 3.1)**: 結果ファイル cp-results.xml(sha256 7533cb8e40cb)・実行の素性= Debug・終了 2026-10-03T19:40:01+09:00・総数 977(dotnet test の合計と一致)。
**区分: 違反 0 / 測定不能 0 / 未実行(検査なし)4 / 未実行(人の承認で検査)3 / 合格 57**(ECO-143 の初回と同じ区分・件数。CP-THUMB-007 は合格・18 テスト〔15+3〕)。別欄: retired 1・台帳に無い ID 2(同前)。

**振り分けのリハーサル(M3・BomDD `rehearsal.md`)**: ①53 の版だけ 3.119.5 にして全件実行 → 違反 1(`CP-THUMB-007 | L2 | 18(17/1/0)`)→ 製品修正へ(文言どおり)= 成立 ②新規 3 Fact を Skip にして全件実行 → 区分は違反 0・測定不能 0・**CP-THUMB-007 は合格一覧に `(18)` のまま**(skip 3 は実行単位の行にだけ出る)= **行の区分では捕まらない**。
M3= **1/2**。原因= CP 行と vector の粒度の差(1 行= 複数の約束・複数の vector・測定不能は「行の全テスト Skip」の定義)。製品・cp_results の欠陥ではなく、導出が既存行に vector を足す形(D2)を選んだ帰結。対処候補は方法論側の評価(EXP-20261003-01)へ。
一時変更は実行後に復元(`git status` 前後同一)。

**追跡表(M2・BomDD `trace.md`)**: REQ-104/105/106 → E-THUMB-020 → M-THUMB-008 → CP-THUMB-007(vector)→ テスト(trait)→ 実行証拠(上記の表)= **5/5 到達**。欄の不足= CP 行→vector→テストの対応は文言でしか結べない(vector に ID が無い・trait は行単位)/ 53 の上流参照欄・10 の保守性種別は本 ECO で新設(candidate)。
M4(参照方向)= E → M 0 件・REQ → M 0 件(成立)。

**R8 セルフレビュー**(fix を書いた文脈と別の fresh context・読み取りのみ・実測つき): **blocking 0・non-blocking 3・info 5**。処置:
1. [non-blocking・設計] 「測定系復旧」の振り分けはテストの失敗メッセージ先頭でのみ行われ、cp_results は Pass/Fail/Skip の件数しか読まないため、治具の不能も行では「違反」に計上される(fail-closed・`Assert.Skip` は混在行で fail-open)→ **現状維持(fail-closed)**+33 の vector にその旨を明記(スコープ内・文書)。リハーサル M3 の所見と同根(行と vector の粒度差)。
2. [non-blocking] `RepoRoot()` が try の外で、sln 不発見の `DirectoryNotFoundException`(IOException 派生)が「測定系復旧」接頭辞なしで素通り → **是正**(try 内へ移動・スコープ内・tests 2 行)。
3. [non-blocking] 32 の正規表現 `SkiaSharp\b` は将来 `package: SkiaSharp.NativeAssets.*` 行が上に来ると誤一致 → **是正**(`SkiaSharp\s*,` でカンマ固定・スコープ内・tests 1 行)。
4〜8. [info] 53 の正規表現は flow 形式・先頭 `{}` 前提(外れると測定系復旧= 安全側)/ ブロック正規表現は L200〜214 で正しく終端・CRLF でも同一 / `Assert.Fail` 後の `return` は到達性解析上必要 / Fact 1 の空検査は重複(害なし)/ 読む対象は 3 ファイルのみ・書き込みなし。
レビューの根拠(検査官の実測): .NET 正規表現で 3 パターンを実ファイルに適用し各 1 件= 3.119.4・リハーサル中(53 が一時的に 3.119.5)の実行で `Assert.Equal` 不一致= 製品修正経路で落ち測定系復旧と分離されていることを確認。
是正 2・3 の後に機械受入 4 点と表を**再実行**(結果は下記)。未処置のスコープ内所見= **0**。

**機械受入の再実行(R8 是正後・2026-10-03)**: `dotnet build` 0 エラー / 0 警告 / `dotnet test tests/ViewPrism2.Tests` **977/977**(skip 0)/ Oracle は是正前の実行(109+skip 4・tests のみの変更で無接触)/ `python bomdd/validate_bom.py` 0-0。
CP 行ごとの表(本 fix の正本): cp-results.xml **sha256 793554f877aa**・素性 Debug・終了 2026-10-03T19:44:54+09:00・総数 977(dotnet test の合計と一致)・**区分: 違反 0 / 測定不能 0 / 未実行(検査なし)4 / 未実行(人の承認で検査)3 / 合格 57**・CP-THUMB-007 合格(18)・別欄 retired 1・台帳に無い ID 2。

## §6 残ゲート

- ~~gate①(maintainer)~~: 2026-10-03 裁定 1:A 2:OK(§4.2'・decide 9f32ef4・tag bom-v4.1)。
- ~~fix(/eco-fix)~~: 2026-10-03 実施(§7)— 導出・検査の製造(工場)・機械受入 4 点・CP 行ごとの表・リハーサル・R8・追跡表。
- **gate②(maintainer)**: 視覚・挙動の変更なし= **golden n/a の受理**(ECO-143 と同型)を依頼する。クローズ条件= 機械受入 4 点+CP 行ごとの表+追跡表(BomDD `trace.md`)の**意味の審査欄**(REQ-104〜106 の vector が約束を検査しているか: 合 / 否 / 条件付き)の記入。
  承認の場で表のどの行に触れたか(触れなければ「言及なし」)を register 注記に残す(ECO-143 §4.4 の 1 件目)。
