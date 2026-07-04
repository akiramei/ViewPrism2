# Change Order — ECO-035(M-BOM 写像被覆ギャップの是正)

> scale-01 遡及採点([studies/scale-01-impact-retrospective.md](studies/scale-01-impact-retrospective.md) §2.1)が
> 検出した「どの unit の artifact.path にも属さないファイル 8 件」の台帳是正。**宣言のみ・実装変更なし**。

## 1. 検出と帰属
| 未所有ファイル | 真因 | 是正 |
|---|---|---|
| tests/ViewPrism2.Oracle/*(7 files) | オラクル治具の所有 unit 不在 — 「治具は製品と同格」(M-HARNESS-015 の原則)が固定オラクル側に未適用 | **M-ORACLE-030** 新設(path= tests/ViewPrism2.Oracle) |
| src/ViewPrism2.Infrastructure/ViewPrism2.Infrastructure.csproj | プロジェクト級 unit の不在 — Core(M-CORE-001)/App(M-UI-013)にはある非対称 | **M-INFRA-031** 新設(path= src/ViewPrism2.Infrastructure。配下専門 unit が最長一致で優先) |

## 2. 影響分析(61 §1.4 ハブ台帳 regime 下の初 ECO)
- ハブ台帳(52 hub_ledger: M-UI-016/M-UI-013/M-VIEWSVC-012)— 本 ECO は 32-mbom のみ・src 変更なし
  → 3 unit とも影響なし(根拠: doc-only)。
- 影響 = 32-mbom への unit 2 件追加宣言のみ。verifies は宣言しない(オラクルの検査対象・期待値の正典は
  41-fixed-oracle の採点行 — unit 側に転記すると二重定義=捏造リスク)。

## 3. 検証(2026-07-04)
- workspace lint(BomDD-Plm bomdd-lint): error/warn 0 維持(新 unit の R-005 孤立 info は想定内)。
- scale-01 治具の再帰属: 写像未所有 8 → 0。
