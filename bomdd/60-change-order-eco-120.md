# ECO-120: 検査器の欠陥 — validate_bom が台帳 15 本中 4 本しか読まず、33-control-plan の構文破壊でも 0/0 を返す

- 起票日: 2026-07-20
- 報告者: ECO-116 accept 作業中の実測発見(spawn タスク task_ac7d7247 は着手痕跡なし→本セッションで起票し直し)
- baseline: main `240d645`
- 種別: 検査器是正(validate_bom.py の検査範囲欠落。src/tests 無変更)

## §1 症状(実測済み・2026-07-20)

ECO-116 の accept 作業で `bomdd/33-control-plan.yaml` の CP-UI-G1 行(flow mapping の
二重引用符スカラー)に、誤って内側 `"` を含む文字列を挿入した際:

1. `python bomdd/validate_bom.py` は **0 error / 0 warning** を返した(壊れているのに緑)。
2. PyYAML `safe_load` は同ファイルで**パースエラー**(`while parsing a flow mapping ... expected ',' or '}'`)。
3. **bomdd-lint(BomDD-Plm)だけが検出**: 30-ebom.yaml の CP-* 参照が軒並み
   「CP の定義に解決できません」(R-003 大量)= CP 定義の全滅を正しく捕捉。
4. `grep "control" bomdd/validate_bom.py` = **0 ヒット**(そもそも読んでいない)。

33-control-plan は golden CP(human gate の受入観点)の**正本**であり、壊れたまま
コミットされ得る。検出者が bomdd-lint のみ=**単一障害点**で、しかも pre-commit の
lint は **fail-open**(隣接リポの CLI 不在なら skip)。

## §2 工程診断(R2)

| 工程 | 判定 | 根拠 |
| --- | --- | --- |
| CAD | 非該当 | UI 無関係 |
| BOM(台帳) | 健全 | 台帳自体は正しい(壊したのは一時的な作業ミスで復旧済み) |
| **検査器(validate_bom.py)** | **欠陥確定(§3.1)** | 台帳 15 本中 **4 本しか load しない**。残り 10 本の YAML は構文が壊れていても素通し |

結論: **検査器の検査範囲欠落**。裁定(gate①)不要。

## §3 切り分け済みの事実

### 3.1 確定(起票時悉皆)

`validate_bom.py` の load サイト全数(`load_yaml("...")` grep):
**30-ebom / 32-mbom / 60-change-register / 00-manifest の 4 本のみ**。

未読の YAML 台帳(=構文破壊が素通しになる圏外)10 本:

| 台帳 | 内容 | 壊れた場合の実害 |
| --- | --- | --- |
| **33-control-plan.yaml** | golden CP 正本 | **実測済み**(§1)— 受入観点の全滅を 0/0 で素通し |
| 10-requirements.yaml | REQ 定義正本(bomdd-lint R-003 の解決先= ECO-112) | 要件参照の全滅 |
| 31-kbom.yaml | 実装規約(K-AVALONIA 等) | 規約正本の黙殺 |
| 41-fixed-oracle.yaml | 固定オラクル(R6 の保護対象) | オラクル台帳の黙殺 |
| 50-as-built.yaml | golden 承認記録(AUDIT-303) | 承認証跡の黙殺 |
| 34-routing / 35-design-system-bom / 42-exploratory-probes / 52-metrics / 53-service-bom | 各台帳 | 同上 |

- **既知の対称欠陥との関係**: ECO-053 は「PyYAML の警告なし後勝ち」が register 誤挿入を
  素通した欠陥を**重複キー検査(E13)**で塞いだが、その検査も **load される 4 本にしか効かない**
  = 未読 10 本は重複キーも素通し(同じ穴の別面)。
- **bomdd-lint との役割分担**: 意味検査(参照解決 R-003 等)は bomdd-lint が持つが、
  pre-commit では fail-open(CLI 不在で skip)。**構文健全性の床すら in-repo に無い**のが欠落。

### 3.2 未検証(fix 時に確認)

- 未読 10 本が全て現時点で parse 可能か(fix のプローブが兼ねる)。
- 51-cheat-log.md / 20-spec.md 等の **Markdown 台帳**は構文検査の対象外でよいか
  (YAML と違い「壊れて黙る」様態が無いため対象外見込み・fix 時に宣言)。

## §4 是正方針(案・着手時確定)

**案 A(推奨・構文床の全数化)**: validate_bom に
「**bomdd/*.yaml 全数の parse 健全性検査**(既存 `_DupKeyLoader` を再利用= E13 重複キー検査も
自動的に全数へ拡張)」を追加する。エラーは既存の error 系列(新 ID 例: E20)。

- 意味検査(CP 参照解決等)は追加しない= bomdd-lint との重複を作らない。
  役割分担を docstring に明記(「構文床= validate_bom(fail-closed)/意味= bomdd-lint」)。
- プローブ= 33 を一時的に壊して**赤**(是正前は 0/0=症状の再現)→復旧で緑。
  selftest 様式(`--selftest-lifecycle` 同型)での恒久化は fix 時判断。

**案 B(却下候補)**: 33 だけを load 対象に追加。→ 10-requirements 等に同じ穴が残る
(ECO-117 教訓 1= 掃射なしの単発是正)ため採らない。

## §5 影響 BOM(見込み)

- **検査器**: `bomdd/validate_bom.py`(load ループ追加・数十行)。src/tests 無変更。
- **台帳**: 変更なし見込み(全数 parse が現状で緑なら)。

## §6 残ゲート

- **gate①(裁定)**: 不要(検査器の欠落・案 B 却下は ECO-117 教訓の適用)。
- **gate②(golden)**: **n/a 見込み**(検査器変更・機械証拠検収= 破壊注入で赤/復旧で緑+
  既存 4 点受入不変。ECO-105/111/119 前例)。

## §7 実施記録(2026-07-20 — 是正+機械受入完了)

### 7.1 プローブ(症状再現→赤転→緑転の全系列)

1. **是正前の症状再現**: 33-control-plan の flow mapping を破壊 → 現行 validator **0/0**
   (PyYAML は同ファイルでパースエラー)= §1 の実測を fix 直前に再確認。
2. **是正後の赤転**: 同じ破壊 → **E20 で exit 1**(「YAML 構文床(E20)で停止」・fail-fast)。
3. **復旧で緑転**: 0/0。
4. **E13 拡張の陽性対照**: 未読台帳 31-kbom へ重複キー注入 → **E13 で exit 1** → 復旧で緑
   (ECO-053 の検査が未読 10 本へ拡張された証拠)。
5. **selftest**: E20 陽性対照(合成の壊れた YAML)を追加し `--selftest` OK・
   `--selftest-lifecycle` OK。

### 7.2 是正(案 A)

`bomdd/validate_bom.py` +57 行(+docstring 同期):

- `_yaml_syntax_check`/`sweep_yaml_syntax`= **bomdd/*.yaml 全数**の構文床検査(E20 新設)。
  `_DupKeyLoader` 再利用で **E13 も未読台帳へ自動拡張**(本流 load の 4 本= `MAIN_LOADED` は
  後段 load_yaml が報告するため二重報告なし)。
- **fail-fast**: E20 検出時は意味検査へ進まず exit 1(MAIN_LOADED 破壊時の生 traceback 回避も兼ねる)。
- 役割分担を明記: **構文床= validate_bom(fail-closed)/意味(参照解決)= bomdd-lint**。
- 意味検査は追加していない(bomdd-lint との重複を作らない= §4 案 A どおり)。

### 7.3 機械受入(4 点)

build 0/0(フルビルド警告 0)・Tests **844/844**・Oracle 109+2skip・validate 0/0
(src/tests 無変更= dotnet 系は不変の確認)。

### 7.4 R7 / R8

- **R7= 対象外宣言**(UI 無関係)。
- **R8= 独立レビュー(検査器はゲートそのものにつき実施)**: 所見 1+観測 5。
  - **スコープ内 1 件(是正済み)**: **docstring の検査一覧に E20 が無く、行 8 の終了コード契約
    「2= 実行不能(parse 失敗等)」が新実装と矛盾**。レビューの裏取りで**実装側が正**と確定 —
    pre-commit は rc=2 を fail-open(skip)扱いするため、台帳起因の失敗を 2 にしていたら
    床が破れていた。docstring を同期(E20 追記・E13 適用範囲更新・終了コード契約是正)。
  - **観測(スコープ外・記帳のみ)**: 非 UTF-8 ファイルは生 traceback だが exit 1= fail-closed
    維持/空ファイルは None で床通過(既存挙動)/MAIN_LOADED は手動ミラー(将来の load 追加で
    未更新だと E13 二重報告= うるさい側に破れるため許容)/.yml・サブディレクトリは現物ゼロを
    実測確認/E20 併発時の MAIN E13 は次 run 検出(過渡的・fail-closed 不破)。
  - レビューが白と確認: sweep 網羅性(最上位 14 本全捕捉・BOM なし実測)・finally/dispose の
    例外安全・MAIN_LOADED と load サイト 4 つの完全一致・未読 E13 単独時も exit 1・
    selftest 配線(sweep 二重実行なし)・pre-commit 連携(hook は --selftest も毎コミット実行
    = E20 陽性対照が毎回運転される)・性能(全体 2.7 秒)。
  - **未処置のスコープ内所見= 0**。

## §8 残ゲート

**なし(gate② = n/a)。** 検収は機械証拠(§7.1 の破壊注入 赤/緑 系列+selftest 陽性対照+
受入 4 点不変)で成立。maintainer の accept 指示でクローズ可(ECO-105/111/119 前例)。
