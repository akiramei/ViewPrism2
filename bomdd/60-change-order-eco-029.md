# ECO-029 — trace-map/台帳の複合記法・変種品番の正規化(参照値は単一 ID+注釈は note へ)

> **適用記録(2026-07-03)**: 処方を次のとおり確定して適用した。
> - **1対多は配列で表す**(ref-v0.6 で uiIr[]/uiBom[] 配列エッジを併記 — 単純配列化は旧セレクタが黙って
>   スキップし「検査が消える」ため、スキーマ側の配列エッジが必須と判明)。trace-map 3ファイル 76 フィールドを
>   機械変換(` / ` 分割・短縮連番 `0061/0062/0063` 展開・unmodeled/designIntent は uiIrNote へ 24 件)。
> - **変種品番は IR に derived 定義**(S-19 処方の第1選択): 0006A/B/C は ECO-016 の品目3分割に伴う設計分割で
>   あり観測ではない — image-tab ui-ir に `source: [derived]`+derivedFrom 付きで正規配置(「UI-BOM は新規
>   採番しない」原則の回復)。
> - 30-ebom の K+パス合成値・42 の散文合成値は ID+note へ分離。
> - 付随の真正欠陥2件を是正: ui-trace-map の ebomItemRef への K-REGEX 混在(→kbomRef へ分離)/
>   work-tab ui-ir の作業タブモック参照が実取込先と違うパス(画像タブ/→作業タブ/)のまま ingestStatus=pending
>   だった(→実在確認の上 ingested へ同期)。
> - 検証: workspace lint **error 81→0**・warn 87→85(残85は全て ECO-032(b) の注釈付きパス債務)。

- **type**: 台帳正規化(doc-only・記法債務の返済)
- **status**: applied(2026-07-03)
- **golden**: n/a
- **baseline**: main `30f2ed9`
- **出所**: BomDD-Plm bomdd-lint(v0.2-eco-001-accepted)workspace 遡及(2026-07-03)。S-19 裁定で
  「ViewPrism2 台帳記法」クラスタと裁定済み(実装欠陥ではない)。

## 背景 / 課題(lint 実測 — R-003 計 82 所見)
参照フィールドに「ID+散文注釈」や「複数 ID の合成」を書く慣行が、機械可読トレースを壊している:

| 場所 | 件数 | 例 |
|---|---|---|
| ui-trace-map.json(uiIr/ebomItemRef 欄) | 77 | `TMP-UI-CMP-0012 / unmodeled`・`E-UI-BROWSE-022(注釈...)` |
| ui/image-tab/ui-bom.json | 3 | `TMP-UI-REG-0006A/B/C`(変種品番 — IR に 0006 しか定義がない) |
| 30-ebom.yaml | 1 | `K-AVALONIA + ViewPrismUI:資料/...(ECO-025 β...)`(K 参照+パス+注釈の合成値) |
| 42-exploratory-probes.yaml | 1 | `P-07 carryover(...)/ capability-discipline.md §A/B / 20-spec §2.1`(散文合成) |

## 変更内容(処方 — S-19 裁定の処方どおり)
1. **参照欄は単一 ID**。注釈・unmodeled 宣言・出所説明は隣接する `note` フィールドへ移す
   (JSON/YAML とも additionalProperties 自由 — ref-v0 は note を縛らない)。
2. **変種品番は定義側に実体を立てる**: TMP-UI-REG-0006A/B/C を ui-ir.json に定義する(変種が実在する設計なら)
   か、ui-bom 側を 0006 へ寄せて変種語彙を note に落とす(どちらかを裁定)。
3. 複数対象を1欄に書きたい場合は配列にする(セレクタは `[]` を辿れる)。

## impacted_bom(予定)
- ui/*/ui-trace-map.json・ui/image-tab/ui-bom.json(または ui-ir.json)・30-ebom 1箇所・42 1箇所。意味変更なし(記法のみ)。

## verification(予定)
- bomdd-lint 再走行で当該 R-003 82所見の解消。トレースマトリクス(plm-view)の当該行が「注釈汚れ」なしで引けること。
