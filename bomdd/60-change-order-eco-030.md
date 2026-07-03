# ECO-030 — FMEA 構造表の導入(33 への構造化・FMEA-* 参照 43 件の定義サイト新設)

- **type**: 台帳構造化(doc-only — 散在する FMEA 言及を機械可読な表へ)
- **status**: staged(起票のみ)
- **golden**: n/a
- **baseline**: main `30f2ed9`
- **出所**: BomDD-Plm bomdd-lint(v0.2-eco-001-accepted)workspace 遡及(2026-07-03)。S-19 裁定で
  「ViewPrism2 に FMEA 構造表が無い実態の可視化 — 正当(advisory)」と裁定済み。

## 背景 / 課題(lint 実測 — R-003 warn 43 所見)
30-ebom / 32-mbom の fmea_refs 等が FMEA-001〜FMEA-0xx を参照するが、FMEA の**定義サイトが存在しない**
(FMEA 内容は散文・会話・各 ECO 本文に分散)。ref-v0.3 は `33-control-plan の fmea[].id` を定義サイトとして
規定済み(BomDD-Plm 自リポは同構造で解消した実績あり)。

含意: FMEA-参照は「どの故障モードに対する受入か」のトレースの根 — 定義が無いと故障モード→CP 特性の
逆引き(Phase 7 の影響分析)が本文推定になる。

## 変更内容(処方)
1. 33-control-plan に `fmea:` 構造表を新設: `- {id: FMEA-xxx, failure_mode, effect, detection(→CP 参照), source(初出 ECO/loop)}`。
2. 既存の FMEA-* 言及(参照 43 件が指す ID 集合)を洗い出し、本文に散在する故障モード記述から**記録済みの
   事実だけを転記**する(捏造しない — register と同じ出所規律)。転記元が見つからない ID は「未確認」と
   明記した行を立てる(参照を宙に浮かせない)。

## impacted_bom(予定)
- 33-control-plan(fmea 表新設)。参照側(30/32)は無改変。

## verification(予定)
- bomdd-lint 再走行で FMEA 系 R-003 warn 43所見の解消。fmea[].detection→CP の参照が全解決すること。
