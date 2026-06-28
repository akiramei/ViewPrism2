# Visual Gap Analysis — <画面/機能名>

> 製造品(実機)を CAD(HTML mock + UI-IR + UI-BOM + Design System BOM)と視覚突合する製造検査。  
> pixel-exact 検査ではなく、仕様化された構造・情報・デザインシステム部品・状態・操作 affordance の一致を見る。

## 0. 入力

- CAD mock:
- UI-IR:
- UI-BOM:
- UI trace map:
- Design System BOM:
- E-BOM / K-BOM:
- Control Plan:
- 製造品 build / As-Built:
- 比較方法: screenshot / DOM / accessibility tree / manual review / other
- CAD screenshots: <M1 / M2 / M3 等>
- 実機 screenshots:
- golden-in-the-loop iteration: <N回目>
- 承認者:

## 1. severity 定義

| Severity | 名称 | 判定 |
|---|---|---|
| S1 | 欠落 / 構造 | CAD に存在する情報・構造・状態・操作が実機に無い。最優先 blocker |
| S2 | 設計言語 | Card / CTA / Chip / Badge / IconButton などの design system 部品が欠落し、UI が素の panel/text に退化している |
| S3 | 磨き込み | 仕様化済みの構造と design system はあるが、余白・色味・視覚密度・細部が許容差外 |
| S4 | 許容差内 | CAD と意味・構造が一致し、差分は合意済み許容差内 |

## 2. 検査サマリ

- 判定: pass / fail
- S1 件数:
- S2 件数:
- S3 件数:
- 最大原因:
- 是正方針: spec update / E-BOM update / K-BOM update / Design System BOM update / Control Plan update / manufacturing rework / out-of-scope
- 推奨製造順: S1(情報欠落/構造) -> Design System BOM 部品構築 -> 各 surface 適用 -> S3 細部

## 3. gap table

| ID | Severity | CAD 期待 | 実機観測 | gap | 推定根本原因 | 影響 BOM | 是正 |
|---|---|---|---|---|---|---|---|
| VG-001 | S1 | <例 条件は green/amber/mono chip で表示> | <例 muted 素テキスト> | <型と条件の意味が視覚から消える> | design_system_part_missing | E-DESIGN-TYPE-CHIP-001 / K-DESIGN-CHIP-SEMANTICS-001 | 35/30/31/33 を同期して再製造 |
| VG-002 | S1 | <例 パレット候補値/範囲表示あり> | <例 完全欠落> | <選択可能値と範囲制約が見えない> | display_contract_gap | DC-<SCREEN>-001 / CP-<NAME>-DISPLAY-001 | 表示契約と Control Plan へ追加 |
| VG-003 | S2 | <例 CTA は primary gradient button> | <例 素 button> | <主要操作の優先度が消える> | design_system_part_missing | E-DESIGN-CTA-BUTTON-001 | design system 部品を製造対象化 |

## 4. design system coverage audit

| UI-BOM item | 必要な design parts | 実機で確認 | 状態 | 備考 |
|---|---|---|---|---|
|  | E-DESIGN-CARD-001 | yes/no | covered/partial/missing/out-of-scope |  |
|  | E-DESIGN-TYPE-CHIP-001 | yes/no | covered/partial/missing/out-of-scope |  |
|  | E-DESIGN-CTA-BUTTON-001 | yes/no | covered/partial/missing/out-of-scope |  |

## 5. CAD vs 実機の構造突合

| CAD node / uiId | tempPartNo | 実機 selector / node | 一致 | 差分 |
|---|---|---|---|---|
|  |  |  | yes/no |  |

## 6. 欠落表示要素

| display element | CAD ref | 実機 | severity | Control Plan 行 |
|---|---|---|---|---|
|  |  | missing/present | S1/S2/S3 |  |

## 7. 是正オーダー候補

| CAPA / ECO candidate | 理由 | 改訂対象 |
|---|---|---|
| CAPA-UI-DESIGN-SYSTEM-001 | design system 部品が E/K-BOM に無く、複数 surface が素実装に退化 | 35 / 30 / 31 / 33 / 40 |
| CAPA-UI-DISPLAY-CONTRACT-001 | CAD で見えている値・範囲・件数・凡例が display contract に落ちていない | 20 / 30 / 33 / 41 |

## 8. 結論

- 製造品は CAD に対して合格か:
- 再製造前に改訂が必要な BOM:
- 固定オラクルへ昇格する項目:
- 探索のまま残す項目:
- out-of-scope にする項目と理由:
