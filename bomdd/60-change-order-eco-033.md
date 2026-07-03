# ECO-033 — test_vectors 欠落 6 特性への追補(CHEAT-005 予防リントの的中)

- **type**: 受入補強(Control Plan の完全性 — オラクル・ファースト規律の遡及適用)
- **status**: staged(起票のみ)
- **golden**: n/a(CP 台帳と unit 検査の補強のみ)
- **baseline**: main `30f2ed9`
- **出所**: BomDD-Plm bomdd-lint(v0.2-eco-001-accepted)workspace 遡及(2026-07-03)。S-19 裁定で
  「真正(CHEAT-005 予防リント R-014 の的中)」と裁定済み。

## 背景 / 課題(lint 実測 — R-014 6 所見 @ 33-control-plan.yaml)
「完全性が要る特性には test_vectors(境界値・中間値・反例)を必ず列挙する」(playbook §4.4・CHEAT-005 対策)
に対し、次の 6 特性が vectors を欠く:

| 行 | CP |
|---|---|
| 279 | CP-NFR-001 |
| 297 | CP-ROBUST-001 |
| 547 | CP-TRASH-021 |
| 548 | CP-REPAIR-AUTOALL-023 |
| 549 | CP-DISPLAY-PARITY-022 |
| 550 | CP-REPAIR-CARD-021 |

含意: vectors の無い特性は「テスト緑でもサイレント乖離」(MoviePad CHEAT-005 の故障モード)を
構造的に許す。MoviePad では丸め未指定が実バグとして顕在化した実績がある。

## 変更内容(処方)
1. 各特性について境界値・中間値・反例を設計者が列挙し `test_vectors:` に追記する
   (例: CP-TRASH-021 なら「空ゴミ箱で完全削除」「復元対象が既に物理不在」「全選択→復元の件数一致」級)。
2. 列挙した vectors が既存 Tests に**実在するか**を突合し、無いものは unit test を追加する
   (vectors は台帳飾りでなく被覆の宣言 — 書いたら検査と1対1にする)。
3. 追加検査が FAIL を出した場合は §6.4 帰属(是正経路)へ — 直接ソース修正から始めない。

## impacted_bom(予定)
- 33-control-plan(6 特性の test_vectors)+tests/ViewPrism2.Tests(不足分の unit)。

## verification(予定)
- bomdd-lint 再走行で R-014 6所見の解消。Tests 全緑(既存本数を退行させない)。
