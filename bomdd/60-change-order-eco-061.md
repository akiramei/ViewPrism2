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

- **gate①(裁定)**: 遷移証拠機構の方式選択 — 案 a(trailer)/ 案 b(tag)/ 案 c(二段階)。
  推奨は着手時に比較表で提示するが、暫定所見: 案 a が既存 prefix 慣行との併存・自己参照回避・
  追加運用ゼロの点で有利。案 b は push 忘れ面、案 c は台帳ノイズが代償。
- **gate②(golden 相当)**: 変異テスト 5 種(受入条件 5)の全検出+既存 60 ECO への遡及検査の
  初回実行結果(除外/移行の明示リスト)を maintainer が確認して applied 化。
