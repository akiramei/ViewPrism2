# ECO-143 — 機械受入の結果を Control Plan の行ごとに集計し、承認の場に添える(試行)

- 種別: 工程拡張(検査器/台帳 — 機械受入の出力。製品コード src は不変)
- status: **staged**(2026-10-01 起票)
- baseline: main `e8edbbb`
- 出典: maintainer 裁定 2026-10-01(方法論リポ BomDD で M-BOM / Control Plan の再設計を議論した際、
  「方法論の文書を先に改訂する(A)」ではなく「ViewPrism2 で試してから文書にする(B)」を選択)
- 優先度: 中(製品の挙動は変えない。承認の判断材料を増やす試行)

## §1 要求

テスト実行の結果を **Control Plan(33)の行ごと**に区分 — **合格・違反・測定不能・未実行**(未実行は「人の承認で検査」と
「検査なし」に分ける — §4.1)— で集計し、
gate② の承認(golden n/a の ECO では accept 依頼)に添える。**機械では止めない**(読むのは承認者)。

背景(事実・2026-10-01 実測):

- 受入を決めているのは gate② の maintainer 承認([AGENTS.md](../AGENTS.md) human gate 節)。直近の受入 3 件
  (ECO-142・141・065)が根拠に挙げたのは、テストの件数(例「974/974」)・R8 レビュー・lint の所見で、
  **CP の行ごとの結果は根拠に出てこない**(register の該当エントリ)。
- `validate_bom.py` は 33 を構文と重複キーだけ検査し、意味の検査対象に読み込まない
  ([validate_bom.py:137](validate_bom.py) `MAIN_LOADED`)。
- `/eco-accept` は CP を**出力として**更新する(手順 1= 観点の追記)が、判定の**入力として**読む経路はない。
- 一方、テストは CP への対応をすでに持つ: `[Trait("cp","CP-…")]` が 59 種、33 の 65 行中 **57 行**に対応する
  (M-HARNESS-015 の interface_contract `traits` の規定どおり)。

**impact-prospective-01(手順 2b)**: 非適格・理由= 変更要求の原文が maintainer の言葉ではなく、AI の提案に対する
選択(「B」)であり、R を maintainer 原文で固定できない。機会あり・非起動として記録する。

## §2 工程診断

| 工程 | 判定 | 根拠 |
|---|---|---|
| CAD(ViewPrismUI) | 該当なし | UI・視覚の変更を含まない |
| BOM(32/33) | 健全・ただし沈黙 | M-HARNESS-015 は trait の付与を規定するが、結果の集計と利用を規定しない |
| 実装(src) | 該当なし | 製品コードは変更しない |
| **検査器/台帳** | **本 ECO の対象** | 機械受入の出力が件数だけで、CP の行の状態を出さない。CP は受入の出力にはなるが入力にならない |

## §3 切り分け済みの事実

確定(2026-10-01 実測):

1. **正規経路で行ごとの結果が取れる**: `dotnet test tests/ViewPrism2.Tests` に MTP 引数 `--report-xunit` を与えると、
   xUnit v2+ XML の各 `<test>` に `<trait name="cp" value="CP-…"/>` が載る(`--filter-trait cp=CP-UTIL-005` で 13 件・trait 13 件)。
   出力先は scratch(作業ツリー外)で、作業ツリーは無変更。
2. **呼び出し側の引数は既定引数を丸ごと置き換える**: csproj の `TestingPlatformCommandLineArguments` は
   `Condition="'$(TestingPlatformCommandLineArguments)' == ''"` で宣言されている。呼び出し側が `-p:` で渡すと、
   ECO-081 の HangDump 引数(fail-closed 契約)が**黙って外れる**(上の実測 1 はこの状態で走った — 13 件・1 秒で終了したため実害なし)。
   → 報告の引数は**呼び出し側で渡さず、csproj の既定引数に足す**(§4.3)。
3. **trait を持たない行は 8 行**: CP-UI-G3(retired・ECO-023)/ CP-UI-G5・G7・G10(depth G・承認者 maintainer)/
   CP-STARTUP-028・CP-REPAIR-AUTOALL-023・CP-REPAIR-CARD-021(depth unit)/ CP-PENDING-AUTO-035(depth unit+G)。
   **行の無い ID が 2 つ**: CP-VIEWER-DIMCACHE・CP-VIEWER-IMPROVE。

未検証:

- 全件実行時の XML の大きさと実行時間への影響。
- trait の無い unit 行 4 行が、trait なしの別テストで検査されているか(本 ECO は判定しない — 表に「未実行」と出るだけ)。
- Oracle(`tests/ViewPrism2.Oracle`)の trait は `oracle=S-NN`(41-fixed-oracle)で CP に結びつかない — 本 ECO の対象外。

## §4 是正方針(案・gate① で 4.2 を裁定)

### 4.1 集計の規則(4 区分)

方法論側(BomDD ECO-089・BomDD-Plm plm-diag/2)の区分「合格 / 違反 / 測定不能 / 適用外」に対応させる。
**意図した違いが 1 つある**: 「その行を測るテストが存在しない」は、方法論側には対応する区分が無い(lint は定義の欠落を
別の規則で出す)。これを測定不能や適用外へ畳まず、**未実行(検査なし)**として分ける — 処置が違うため
(測定不能= 計器はあるが測れなかった → 計器を直す / 検査なし= 計器が無い → 検査を足す)。

| 区分 | 条件(CP の行ごと) | 方法論側との対応 | 処置の向き |
|---|---|---|---|
| 違反 | その ID の trait を持つテストに Fail が 1 件以上 | RED | 製品(または期待値)を直す |
| 合格 | その ID の trait を持つテストが 1 件以上 Pass し、Fail が 0(Skip があれば件数を併記) | PASS | — |
| 測定不能 | その ID の trait を持つテストはあるが、全件が Skip / 未実行 | MEASUREMENT_FAILURE | 計測(テスト・環境)を直す |
| 未実行(人の承認で検査) | trait を持つテストが無く、行の depth が `G` だけ | NOT_APPLICABLE(自動の測定について) | 不要(gate② が検査する) |
| 未実行(検査なし) | trait を持つテストが無く、行の depth が unit / L1〜L3 を含む(`unit+G` も含む) | 対応なし(意図した違い) | 検査を足すか、行を見直す |

- **実行単位の測定不能**: 結果ファイルが無い/読めない/テスト総数 0 のときは、行ごとの判定をせず全体を測定不能とし、終了コード 2。
- **retired の行**は集計から外し、別欄に列挙する。**台帳に無い ID**(trait にあり 33 に行が無い)も別欄に列挙する。
- 表は判定ではない。終了コードは 0(表を作れた)/ 2(作れない)だけで、違反があっても 0。

### 4.2 出し方(gate① 裁定点)

| | A: 添えるだけ(推奨) | B: 添える+測定不能で停止 |
|---|---|---|
| 内容 | `/eco-fix` の停止点(golden 基準の提示・accept 依頼)に表を添え、`/eco-accept` は register の注記に 4 区分の件数を残す | A に加え、測定不能の行が 1 行でもあれば `/eco-fix` は停止点に進まない(機械受入の 5 点目) |
| 得 | 承認の判断材料が増えるだけで、既存の流れを変えない。試行の効果(表が承認に使われるか)を素直に測れる | 「測れなかった」を合格に流さないことを機械で保証する |
| 失 | 表を読まなければ従来と同じ | 違反はすでに「全緑」で止まるので、追加で止めるのは測定不能だけ。その価値が未測のまま門を増やす |

### 4.3 実装(案)

1. `tests/ViewPrism2.Tests/ViewPrism2.Tests.csproj`: 既定の `TestingPlatformCommandLineArguments` に
   `--report-xunit --report-xunit-filename cp-results.xml` と、出力先を構成(Debug/Release)に依らず固定する
   `--results-directory`(プロジェクト直下の TestResults/ — gitignore 済み)を**追加**する(HangDump 引数は保持。
   HangDump の出力もこの TestResults/ へ移る — CLAUDE.md の「TestResults へ保存」の記述とは矛盾しない)。
   正規の実行コマンドは変えない。
2. `bomdd/cp_results.py`(新規): XML と 33 から §4.1 の表(markdown)を出す。入力の既定値は 1 の固定パス、引数で上書き可。
   表の先頭に**実行のテスト総数**を必ず出す(フィルタ付きの部分実行が全件実行に見えないように)。
   `--selftest` で合成 XML による陽性対照(5 区分・retired・台帳外 ID・実行単位の測定不能)を持つ(validate_bom.py の `--selftest` と同型)。
3. 手順書: `.claude/skills/eco-fix/SKILL.md` 手順 3 と停止点、`.claude/skills/eco-accept/SKILL.md` 手順 2、
   [CLAUDE.md](../CLAUDE.md) 機械受入節に各 1〜数行。
4. `bomdd/32-mbom.yaml` M-HARNESS-015 の interface_contract に、結果の出力(cp-results.xml → cp_results.py)を 1 行。

### 4.4 試行の評価(事前登録・実装前に固定)

- **初回の表**: 件数からは見えなかった行が何行出るか。予測= 未実行(人の承認で検査)3 行(CP-UI-G5・G7・G10)・
  未実行(検査なし)4 行(CP-STARTUP-028・CP-REPAIR-AUTOALL-023・CP-REPAIR-CARD-021・CP-PENDING-AUTO-035)・
  retired 1 行(CP-UI-G3)・台帳外 ID 2 つ。予測と違えば理由を実施記録に書く。
- **次の 3 件の ECO の承認**: ①表が承認の文面・処置で言及されたか ②表を見て処置(テスト追加・trait 是正・行の退役・
  「人の承認で検査」の明記)が起きたか。0/3 なら「表は作れるが承認に使われない」と記録する(効果なしも結果)。
- **宣言済みの限界**: 承認者(maintainer)はこの評価基準を知っている。「表が承認に使われたか」は測られる本人が観測される形で、
  使用が過大に出うる。②(処置が起きたか)を主の指標とし、①は参考に留める。
- 評価の結果は方法論リポ(BomDD)の M-BOM / CP 再設計へ返す(昇格判断は方法論側)。

## §5 影響 BOM

- tests: `ViewPrism2.Tests.csproj`(既定引数 1 か所)。テストコードは変更しない。
- bomdd: `cp_results.py`(新規)・`32-mbom.yaml`(M-HARNESS-015 の契約 1 行)。
- 手順書: `.claude/skills/eco-fix/SKILL.md`・`.claude/skills/eco-accept/SKILL.md`・`CLAUDE.md`。
- src: 変更なし。CAD: 該当なし。固定オラクル: 変更しない(R6)。

## §6 残ゲート

1. **gate①**: §4.2 の A / B。
2. `/eco-fix`: 実装 → `--selftest` → 機械受入 4 点 → 初回の表(§4.4 の予測と突合)。
   **R5 対象外(拡張)を宣言**: 欠陥是正ではないため「是正前に不合格となる回帰テスト」は無い。代わりに `--selftest` の
   既知の不良(違反・測定不能・検査なし・実行単位の測定不能・台帳外 ID)が、それぞれ期待の区分に出ることを陽性対照とする。
   R8 は src 非接触だが、新規スクリプトと csproj に独立レビューを任意で実施する。
3. **gate②**: golden n/a を提案(視覚・挙動の変更なし。機械受入+初回の表がクローズ条件)。
4. `/eco-accept` → 試行の評価(§4.4)は次の 3 件の ECO の承認後に追記。
