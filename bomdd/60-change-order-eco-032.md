# ECO-032 — 注釈付きパスの段階的正規化(87 件・低優先)+35 のプレースホルダ残 1 件(真正・即時)

- **type**: 欠陥是正 1 件(即時)+台帳正規化(段階的・低優先)
- **status**: applied — (a) 2026-07-03 適用 / (b) 2026-07-03 一括正規化完了(workspace R-004 warn 74→**0**。register notes に内訳)
- **golden**: n/a
- **baseline**: main `30f2ed9`
- **出所**: BomDD-Plm bomdd-lint(v0.2-eco-001-accepted)workspace 遡及(2026-07-03)。S-19 裁定で
  「台帳スタイル債務(段階的正規化・低優先)。うちプレースホルダ残 1 件は真正欠陥」と裁定済み。

## 背景 / 課題(lint 実測 — R-004 warn 87 所見)

### (a) 真正欠陥 — 即時是正(1 件)
`35-design-system-bom.yaml:72` に **`<HTML mock / screenshot / Figma ref>` のプレースホルダが残置**。
テンプレート起こし時の埋め忘れで、当該 design part の CAD 出所トレースが不在。実出所を特定して埋める
(不明なら「未確認」+理由を明記 — 空欄の偽装をしない)。

### (b) スタイル債務 — 段階的正規化(86 件・低優先)
パス欄への注釈合成が実在検査を壊している:

| 場所 | 件数 | 典型例 |
|---|---|---|
| 33-control-plan.yaml(fixture 欄) | 46 | `tests/ViewPrism2.Tests(Trait cp=CP-XXX)` — パス+検索条件の合成 |
| 32-mbom.yaml(artifact.path 等) | 24 | `src/ViewPrism2.App/Views(Models/, Common/)` — 親+列挙の合成 |
| 30-ebom.yaml | 8 | 同型 |
| 42-exploratory-probes.yaml | 7 | 散文値 |
| ui/work-tab/ui-ir.json | 1 | meta.sources の注釈付き値 |

## 変更内容(処方)
1. **(a) を先行して単独コミット**(真正欠陥と債務を混ぜない)。
2. (b) はパス部と注釈を分離: パス欄は実在するパス(または配列)、条件・列挙・説明は `note` へ。
   触るファイルの ECO(機能変更)に**便乗して段階的に**進めてよい(独立の一括正規化は低優先 —
   diff ノイズが機能 ECO の 63 監査を汚すため、一括でやる場合は doc-only コミットを分離する)。
3. 完了判定は「R-004 warn の残数が単調減少していること」でよい(ゼロ到達を本 ECO の完了条件にしない)。

## impacted_bom(予定)
- (a) 35-design-system-bom 1 行。(b) 33/32/30/42/ui の該当欄(意味変更なし)。

## verification(予定)
- (a) bomdd-lint 再走行で当該 1 所見の解消。(b) 残数の推移を 52-metrics 側で追跡(段階完了)。
