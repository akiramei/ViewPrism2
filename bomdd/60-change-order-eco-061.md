# ECO-061 (staged) — ECO ライフサイクル遷移の状態不変条件検査 — validator 検査次元の追加+eco-accept post-condition

## §1 症状(観測 2026-07-11・報告者 maintainer)

ECO-060 の運用中、担当 AI(GPT-5.6 Sol・ハーネス未記録)が自然文の了承を `/eco-fix`・`/eco-accept`
の実行へ昇格させ、**fix コミットが存在しない状態で** register を `applied`・As-Built を承認済みへ
変更した(早期クローズ)。この間 `python bomdd/validate_bom.py` は **0 エラー / 0 警告**であり、
違反は maintainer の指摘によってのみ検出された。最終履歴に不正な applied 状態は残存しなかったが、
これは設計された検出・復旧ではなく、別の逸脱(fix コミット保留)との偶然の相殺による。
一次資料: [reports/incident-eco060-lifecycle-2026-07-11.md](reports/incident-eco060-lifecycle-2026-07-11.md)
(担当 AI 自己分析報告の原本+maintainer 検分)・51-cheat-log 同日記録。

## §2 工程診断(R2)

| 工程 | 判定 | 根拠 |
|---|---|---|
| CAD(ViewPrismUI) | 無関係 | UI 挙動の欠陥ではない |
| BOM(30/32) | 無関係 | 品目宣言・受入観点の欠陥ではない |
| 実装(src/tests) | 無関係 | 製品コードは正常(ECO-060 golden 合格・580/580) |
| **検査器(validate_bom.py)** | **欠陥(検査次元の不在)** | 構造整合のみ検査し、register の status 遷移と git 履歴証拠を突合しない。`applied` へ手編集しても 0/0 で通過(§1 で実測) |
| **工程ハーネス(.claude/skills)** | **欠陥(前提が散文)** | eco-accept の前提「fix コミット済み」が実行可能チェックでなく散文(成熟度ラダー②滞留)。post-condition なし |

帰属: **検査器/台帳の欠陥**(方法論還元済み: BomDD FINDINGS §11.3・playbook §13・silence-checklist §12
「台帳自身も状態機械である」)。「0 エラー表示と実態の乖離が無音で成立する」軸の 3 例目
(BomDD harness ECO-002= 欠測の健全化・ECO-003= 集計 fail-open・本件= 検査次元の不在)。

## §3 切り分け済みの事実

確定(実測):
- validate_bom.py は 60-change-register.yaml の `status` 値と git 履歴(fix/accept コミットの存在・
  順序)をいかなる形でも突合しない(コード読解+§1 の 0/0 通過実測)。
- register の status 語彙は staged(3)/ implemented(20)/ applied(36)/ superseded(1)の 4 値・
  全 60 ECO(2026-07-11 時点)。
- 既存コミット規約は `起票(eco-NNN):` → `decide(eco-NNN):` → `fix(eco-NNN):` → `accept(eco-NNN):`
  の**メッセージ prefix 慣行**であり、機械可読な trailer・tag・証拠フィールドではない。
- accept コミットは自分自身の SHA を同一コミット内の台帳へ記録できない(自己参照制約)。

疑い(未検証):
- 既存 60 ECO のコミット履歴が新検査の要求(prefix の一貫性・fix→accept 順序)を全件満たすか
  — 遡及検査の初回実行で判明する(満たさない場合の扱いは §4 受入条件 6)。
- superseded・doc-only ECO(fix コミットを持たない正当なクローズ形)の扱い — 実装時に
  許可遷移表で明文化する。

## §4 是正方針(案 — gate① 裁定後に確定)

**受入条件(維持者裁定 2026-07-11・7 項)**:

1. 起票(staged)→ implemented → applied の**許可遷移と禁止遷移を明文化**する(superseded・
   doc-only 等の傍系遷移を含む遷移表を register スキーマまたは change-management.md に置く)。
2. コミットメッセージの曖昧検索ではなく、**ECO ID を持つ明示的な trailer、tag、または証拠フィールド**
   を遷移証拠とする。
3. **fix 証拠の実在・対象リポジトリ・祖先関係・遷移順序**を検査する(fix が accept より前にあり、
   対象ブランチの祖先であること)。
4. 台帳を手編集して applied にした場合に validator が **fail する陽性対照**を置く。
5. **変異テスト**に次を含める: fix なし / 順序逆転 / 存在しない SHA / 別系統ブランチ / 状態飛び越し。
6. **既存 ECO(60 件)**については、移行・明示的除外・スキーマ版のいずれかを定義し、
   **黙って適用除外しない**。
7. `/eco-accept` は事前条件だけでなく、**実行後に validator 成功を確認する post-condition** も持つ。

**設計制約 — accept コミットの自己参照回避(gate① で方式を裁定)**:

- **案 a(trailer 方式)**: applied への変更コミット自体に `BomDD-ECO-Accept: ECO-NNN` 形式の
  trailer を持たせ、validator が履歴から遷移コミットを特定する(fix 側も `BomDD-ECO-Fix: ECO-NNN`)。
  台帳に SHA を書かないため自己参照が発生しない。既存の prefix 慣行と併存可能。
- **案 b(tag/証拠イベント方式)**: applied コミット後に `eco-NNN-accepted` 等の tag または
  別の証拠イベントを作り、validator は tag→コミットの対応を検査する。tag の push 忘れが
  新たな無音面になり得る点に注意。
- **案 c(二段階コミット方式)**: 証拠記録(遷移コミット SHA)を後続コミットに分離し、
  「applied(証拠未記録)→ applied(証拠記録済み)」の二段階状態を register 上で明示する。
  台帳が完全な証拠を自蔵する代わりに、遷移が 2 コミットになる。

付随(スコープ内・小): register の ID 単位変更の適用後検証(ECO-060 運用時に ID 限定不足の
パッチが ECO-007 へ誤適用 ×2 — コミット前検出)は、本 ECO の検査が「ID ⇔ 証拠」を突合する
ことで部分的に緩和される。専用治具は rule of three 待ちのまま(51-cheat-log 記録)。

## §5 影響 BOM

- `bomdd/validate_bom.py`(検査次元の追加 — 製品コード src/tests は**不変**)
- `bomdd/60-change-register.yaml`(遷移表/スキーマ版の宣言 — 受入条件 1・6)
- `bomdd/change-management.md` §3.3/§4(許可遷移表の正本)
- `.claude/skills/eco-accept/SKILL.md`(事前条件の実行可能チェック化+post-condition — 受入条件 7)
- `.claude/skills/eco-fix/SKILL.md`(fix 証拠様式の追随 — 裁定方式による)
- 既存固定 Oracle 行・製品 CP: **影響なし予測**(製品挙動に触れない)

## §6 残ゲート

- **gate①(裁定)**: ~~遷移証拠機構の方式選択~~ → **案 a(trailer)で裁定済み(maintainer 2026-07-11)**。
- **gate②(golden 相当)**: 変異テスト 5 種(受入条件 5)の全検出+既存 60 ECO への遡及検査の
  初回実行結果(除外/移行の明示リスト)を maintainer が確認して applied 化。

## §7 実施記録(2026-07-11・fix)

### 7.1 プローブ先行(R5)— 是正前の赤 2 本(実測)

1. `--selftest` に lifecycle 変異の陽性対照を先行追加 → **FAIL・exit 1**
   (「lifecycle 検査が存在しない(E14〜E19 未実装)」— 検査不在の恒久検出として本実装後も残置)。
2. **症状の直接再現**: register の ECO-061 を手編集で `staged→applied` に変異(fix コミットなし)
   → 是正前 validator は **0 error / 0 warning・exit 0 で素通し**(ECO-060 違反様式の再現)。復元済み。

### 7.2 実装(裁定=案 a trailer)

- `validate_bom.py`: **E14〜E19 を新設**。純粋検査層(`lifecycle_evidence_findings`/`lifecycle_edge_findings`
  — git 非依存・selftest で変異可能)と git 抽出層(`collect_trailer_evidence`= HEAD 祖先のみ収集・
  `git_is_ancestor`)を分離。E15〜E17 は**コミット済み HEAD の台帳状態**を基準に履歴証拠と突合
  (遷移進行中は trailer 未出現のため — その面は commit-msg hook が塞ぐ)。E19 は HEAD→作業ツリーの
  遷移エッジ。git 不能・shallow clone は **E14 で fail-closed**(`--no-git` のみ宣言つき明示スキップ)。
- `--commit-msg <file>` モード+`hooks/commit-msg` 新設: 遷移コミット自身への trailer を commit 時点で
  強制(**自己参照制約の解**: 台帳に SHA を書かず、validator が履歴の trailer から遷移コミットを特定)。
- `--selftest-lifecycle` モード: 一時 git リポ 3 本による抽出層の実 DAG 統合検査
  (正常 fix→accept / 別系統ブランチ / 順序逆転)。一時リポ生成を伴うため pre-commit には載せず、
  eco-accept post-condition と手動で回す。
- `60-change-register.yaml`: `lifecycle_evidence` ブロック新設(scheme=trailer-v1・applies_from=ECO-061・
  **legacy 免除の明示** — 移行は履歴書換=ハッシュ再発行となるため行わない)。
- `change-management.md` **§4.1 新設**(許可遷移表の正本)・`eco-fix`/`eco-accept` SKILL.md
  (trailer 手順+前提の実行可能チェック化+**post-condition**)。

### 7.3 受入条件 7 項との対応

| # | 受入条件 | 実装 |
|---|---|---|
| 1 | 許可/禁止遷移の明文化 | change-management.md §4.1(正本)+validate_bom.py ALLOWED_EDGES |
| 2 | 曖昧検索でなく明示的証拠 | git trailer(BomDD-ECO-Fix/Accept)— `%(trailers:key=…)` で機械抽出 |
| 3 | 実在・対象リポ・祖先関係・順序 | E15/E16(HEAD 祖先に実在)・E17(fix が accept の祖先)。収集自体を HEAD 祖先に限定=別系統ブランチ排除 |
| 4 | 手編集 applied の陽性対照 | §7.4 実測(E19・exit 1)+selftest 恒久収載 |
| 5 | 変異テスト 5 種 | fixなし=E15/E16・順序逆転=E17(純粋層+実 DAG)・参照先不在=E18・別系統ブランチ=E15(実 git)・飛び越し/逆行=E19 — `--selftest`+`--selftest-lifecycle` に恒久収載 |
| 6 | 既存 60 件を黙って除外しない | lifecycle_evidence.legacy で免除を宣言(E14 が宣言自体を検査)。遡及初回実行= 0 所見(§7.4) |
| 7 | eco-accept post-condition | SKILL.md 手順 5 新設(accept コミット後に validator 0-0+--selftest-lifecycle OK を確認) |

### 7.4 是正後の機械受入(全緑)

- **陽性対照(受入条件 4)**: §7.1-2 と同一の手編集変異 → **[E19] 禁止遷移 staged→applied を検出・
  exit 1**。復元後 0-0(可逆)。
- `--selftest`: **OK**(lifecycle 変異 8 ケース+正常系 2 ケースを含む)。
- `--selftest-lifecycle`: **OK**(実 git 統合 3 本 — 正常/別系統ブランチ/順序逆転)。
- 既存 60 ECO への遡及検査(本検査の初回実行): **0 error / 0 warning** — legacy 宣言免除が機能し、
  除外は宣言により明示(黙殺なし)。
- `dotnet build`: **0 error** / `ViewPrism2.Tests`: **580/580** / `ViewPrism2.Oracle`: **109 pass+既知 2 skip**
  (製品コード不変 — 影響なし予測どおり)/ `python bomdd/validate_bom.py`: **0-0**。
- 本 fix コミット自体が新規律の**初適用**(staged→implemented 遷移+`BomDD-ECO-Fix: ECO-061` trailer —
  commit-msg hook の in vivo 陽性対照)。
