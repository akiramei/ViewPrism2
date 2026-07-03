# ECO-031 — 41-fixed-oracle の spec_ref 正規化(散文 38 件 → `パス#節` 形式)

- **type**: 台帳正規化(doc-only)
- **status**: staged(起票のみ)
- **golden**: n/a
- **baseline**: main `30f2ed9`
- **出所**: BomDD-Plm bomdd-lint(v0.2-eco-001-accepted)workspace 遡及(2026-07-03)。S-19 裁定で
  「ViewPrism2 台帳記法(path#節 正規化 — BomDD-Plm 自リポで実施済みと同型)」と裁定済み。

## 背景 / 課題(lint 実測 — R-004 38 所見 @ 41-fixed-oracle.yaml)
固定オラクル行の spec_ref が散文形(`20-spec §2.4 / review_points IMG-003` 等)で、パスとして解決できない。
凍結ゲート(R-030)の規律は「期待値の典拠なきオラクル行は仕様の穴」— spec_ref が機械可読でないと
この検査が働かない。

## 変更内容(処方 — BomDD-Plm 自リポの同型作業を参照)
- spec_ref を `bomdd/20-spec.md#2.4` 形式(`パス#断片` — パス部で実在検査・断片は自由)へ正規化する。
- 複数典拠は配列へ。仕様外の典拠(review_points 等)は `docs/review_points.md#IMG-003` のように実パスで書く。
- BomDD-Plm 自リポでは同型の 19 件を1コミットで解消済み(参考: BomDD-Plm `c87447f` 前後の台帳正規化)。

## impacted_bom(予定)
- 41-fixed-oracle.yaml のみ(オラクル行の id/scenario/期待値は不変 — 典拠欄の記法のみ)。

## verification(予定)
- bomdd-lint 再走行で 41 由来の R-004 38所見の解消。オラクル自体の実行結果(S-01〜S-31)不変。
