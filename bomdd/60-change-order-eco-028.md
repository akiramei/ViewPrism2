# ECO-028 — dangling 受入参照の是正(CP 2件の行不在+撤回済み品目への残存参照)

- **type**: 欠陥是正(doc/BOM 同期 — bom_sync_gap)
- **status**: staged(起票のみ。是正は後段)
- **golden**: n/a(台帳同期のみ・表示/挙動変更なし)
- **baseline**: main `30f2ed9`
- **出所**: BomDD-Plm bomdd-lint(v0.2-eco-001-accepted)による workspace 遡及リント(2026-07-03)。
  S-19 裁定(BomDD-Plm/bomdd/plm-intake/s19-adjudication-2026-07-03.md)で「真正の欠陥(初捕獲)」と裁定済み。
  ECO-001(受理形/per-file)適用後の再走行でも残存=誤検出でないことを再確認済み。

## 背景 / 課題(lint 実測)
R-003(壊れ参照)6所見が受入トレースの穴を指している:

| 参照 | 場所 | 原因 |
|---|---|---|
| CP-WORKSPACE-028 | 30-ebom.yaml:321(E-WORKSPACE-042)・30-ebom.yaml:456・32-mbom.yaml:326 | ECO-021 β系が acceptance_refs で参照したが **33-control-plan に行が起票されていない**(CP の起票漏れ) |
| CP-TRASH-022 | 30-ebom.yaml | 同上(33 は CP-TRASH-020/021 まで) |
| E-UI-DETAIL-023 | 53-service-bom.yaml:45・:56(K-AVALONIA / K-MVVM の affected_parts) | **ECO-023 で撤回済み**の品目への残存参照 = ECO-023 の M4 同期漏れ(lineage 再帰属漏れ) |

含意: acceptance_refs が指す CP が存在しない=「その受入は台帳上検証不能」。撤回品目の残存参照は
Service BOM の劣化イベント逆引き(影響部品絞り込み)を汚染する。

## 変更内容(処方)
1. CP-WORKSPACE-028・CP-TRASH-022 の実体を確認し、**検査が既に Tests に存在するなら 33 に CP 行を追記**
   (characteristic/depth/tolerance/fixture=Trait 参照)。存在しないなら受入の設計から(オラクル・ファースト)。
2. 53-service-bom の affected_parts から E-UI-DETAIL-023 を除去(ECO-023 の撤回判断に台帳を追随)。

## impacted_bom(予定)
- 33-control-plan(CP 2行追記 or 受入設計)/ 53-service-bom(2箇所)/ 30-ebom・32-mbom は参照側につき無改変見込み。

## verification(予定)
- bomdd-lint 再走行で当該 R-003 6所見の解消(他所見の増減なし)。validate_bom 0/0 維持。
