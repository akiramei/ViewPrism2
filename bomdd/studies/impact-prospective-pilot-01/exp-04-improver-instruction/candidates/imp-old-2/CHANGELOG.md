# ECO-139 / ECO-140 BOM 反映点検

## 設計変更の抽出

- ECO-139 は、初版の high-confidence を `pending + origin=new + candidate_link_id` の hash 一致へ限定し、GF-139-01 で自動裁定を T13 accept から「1 missing : 1 new の一意組だけを対象とする原子一括 relink」へ再裁定した。missing 側 image_id/タグを保持し、pending 行と missing を解消する。根拠: `eco/ECO-139.md:56-66,170-185`、`eco/ECO-139.diff:1584-1924`。
- ECO-140 は、状態別の Repair/PendingReview 二面を、pending∪missing を「自動裁定できる / 個別に確認 / 見つからない」へ分類する事象中心の `IntegrityReview` 一面へ統合した。旧 2 入口・旧 2 Window は撤去し、未裁定バッジと T13/T14/T15 の core 意味論は維持する。根拠: `eco/ECO-140.md:76-96,116-124,169-184`、`eco/ECO-140.diff:133-150,1774-1779,2362-2367,3484-3489,3822-3827`。
- relink の hash 一致/一意性選別と、単発 T4・原子バッチ確定は E-RELINK-007 へ一本化した。再出現の hash は裁定時 on-demand で確認し、ScanJudge/ScanStaging の判定へ持ち込まない。根拠: `eco/ECO-140.md:80-90,116-118,143-159`、`eco/ECO-140.diff:74-109,247-254`。
- reappeared 比較は `pending_baseline_hash ?? 記録 hash` を基準とし、UpdateMetaAndPend 前の旧 hash を migration 011 で保全する。根拠: `eco/ECO-140.diff:223-239,247-265,277-303`、`eco/ECO-140.md:185-187,202-207`。
- 旧 PendingReview/Repair のテスト面は `CpIntegrityReviewTests`、`CpRelinkUnifiedBatchTests`、`GfIntegrityReviewVisualParityTests` へ移管された。根拠: `eco/ECO-140.md:230-248,327-331`、`eco/ECO-140.diff:6714-6719,7424,7896-7901,8516,8939-8944`。

## 編集記録

- `bom/30-ebom.yaml` / `E-CRITERIA-037.graph_edges.consumers` / 種別: 陳腐化訂正 / 根拠: `eco/ECO-140.md:87-90,105-106`、`eco/ECO-140.diff:133-150`。旧 E-UI-REPAIR-039 consumer を E-UI-INTEGRITY-050 へ置換。
- `bom/30-ebom.yaml` / `E-PACKAGE-047.invariants` / 種別: 陳腐化訂正 / 根拠: `eco/ECO-140.md:78-90`、`eco/ECO-140.diff:133-150`。未解決画像の出口を旧修復面から統合裁定面へ訂正し、旧写像を superseded と明記。
- `bom/30-ebom.yaml` / `E-UI-MODE-041.requirement_refs・depends_on・invariants・acceptance_refs` / 種別: 契約更新 / 根拠: `eco/ECO-140.md:78-96,100-110,119-124`、`eco/ECO-140.diff:133-150,165-182`。旧 2 入口を統合入口へ置換し、歴史規定へ superseded 表記を追加。
- `bom/30-ebom.yaml` / `E-DESIGN-028.graph_edges.consumers` / 種別: 欠落追加 / 根拠: `eco/ECO-140.md:119-124`、`eco/ECO-140.diff:133-150`。新しい外部設計 surface E-UI-INTEGRITY-050 を consumer に追加。
- `bom/30-ebom.yaml` / `E-UI-REPAIR-039.name・external_source_note` / 種別: 陳腐化訂正 / 根拠: `eco/ECO-140.md:78-90,100-106`、`eco/ECO-140.diff:113-126`。RepairWindow 部分の superseded と後継正典を rationale 側へ明記し、存続するトラッシュ面と区別。
- `bom/30-ebom.yaml` / `E-UI-PENDING-049.name・external_source_note` / 種別: 陳腐化訂正 / 根拠: `eco/ECO-140.md:78-90,100-106`、`eco/ECO-140.diff:121-150`。旧入口/Window の superseded、後継正典、存続する badge/core 意味論を区別。
- `bom/32-mbom.yaml` / `M-SCAN-005.artifact.path_note` / 種別: 陳腐化訂正・観測追記 / 根拠: `eco/ECO-140.md:83-85,116-118,202-207`、`eco/ECO-140.diff:277-290`、`code/src/ViewPrism2.Infrastructure/Scanning/IntegrityReviewFileHashProvider.cs:1`、`code/src/ViewPrism2.Infrastructure/Scanning/RelinkService.cs:17`。RelinkService を M-RELINK-025、裁定時 hash provider を M-UI-INTEGRITY-055 へ写像し、scan 判定所有から分離。
- `bom/32-mbom.yaml` / `M-RELINK-025.artifact・interface_contract・acceptance_refs・fmea_refs` / 種別: 契約更新・観測追記 / 根拠: `eco/ECO-140.md:88-90,116-118,143-159`、`eco/ECO-140.diff:74-109`、`code/src/ViewPrism2.Core/Services/Repair/IntegrityReviewService.cs:78-108`、`code/src/ViewPrism2.Infrastructure/Scanning/RelinkService.cs:84-128,214-299,348-385`。削除済み CountAutoRepairableAsync 契約を除き、一本化後の選別・単発・原子/混在バッチ API を記録。
- `bom/32-mbom.yaml` / `M-UI-REPAIR-027.status・superseded_by・artifact.path_note・interface_contract・acceptance_note・fmea_refs` / 種別: 陳腐化訂正・契約更新 / 根拠: `eco/ECO-140.md:78-90,105-108,173-174`、`eco/ECO-140.diff:2362-2367,3822-3827,3977-3982`。削除済み artifact の事実と M-UI-INTEGRITY-055 への統合先を残し、旧契約を歴史契約化。
- `bom/32-mbom.yaml` / `M-UI-PENDING-054.status・superseded_by・artifact.path_note・interface_contract・acceptance_note` / 種別: 陳腐化訂正・契約更新・観測追記 / 根拠: `eco/ECO-140.md:78-90,105-108,173-174`、`eco/ECO-140.diff:1774-1779,3484-3489,3780-3785`、`code/src/ViewPrism2.Core/Services/Repair/PendingReviewService.cs:13-78`、`code/tests/ViewPrism2.Tests/CpPendingSemanticsTests.cs:1`、`code/tests/ViewPrism2.Tests/CpUiG1PendingGuardTests.cs:1`。旧 Window/VM/入口の削除と統合先、存続する T13〜T15/badge 契約を分離。
- `bom/32-mbom.yaml` / `M-UI-INTEGRITY-055.artifact.path_note・interface_contract.hashcheck・acceptance_note・fmea_refs` / 種別: 観測追記・欠落追加 / 根拠: `eco/ECO-140.md:169-184,230-248`、`eco/ECO-140.diff:307-321`、`code/src/ViewPrism2.App/Services/WindowService.cs:156-179`、`code/src/ViewPrism2.App/ViewModels/ImageTabViewModel.cs:1991-2003`、`code/src/ViewPrism2.Core/Services/Repair/IntegrityReviewService.cs:231-378`、`code/tests/ViewPrism2.Tests/CpRelinkUnifiedBatchTests.cs:21-215`。現行 artifact 群、確認済み hash outcome 再利用、専用 fixture と FMEA 所有を追記。
- `bom/32-mbom.yaml` / `FMEA-031.unit・targeted_by` / 種別: 陳腐化訂正 / 根拠: `eco/ECO-140.md:87-90,105-110,232-237`、`eco/ECO-140.diff:133-150,323-359`。候補カード failure の owner/検査先を統合面へ移管。
- `bom/32-mbom.yaml` / `FMEA-034.unit・failure_mode・targeted_by` / 種別: 契約更新 / 根拠: `eco/ECO-140.md:48-62,88-96`、`eco/ECO-140.diff:74-109,165-182`。曖昧除外と単発/原子バッチの異なる失敗意味論を E-RELINK-007/M-RELINK-025 所有へ移管。
- `bom/33-control-plan.yaml` / `CP-SCAN-004 ECO-139 ベクタ` / 種別: 陳腐化訂正・観測追記 / 根拠: `eco/ECO-140.md:173-184,230-240`、`eco/ECO-140.diff:6714-6719,7424`、`code/tests/ViewPrism2.Tests/CpRelinkUnifiedBatchTests.cs:21-215`、`code/tests/ViewPrism2.Tests/CpIntegrityReviewTests.cs:80-133,885-966`。旧 RelinkHighConfidenceAsync/CpPendingAutoAdjudicationTests を superseded とし、現行 fixture へ写像。
- `bom/33-control-plan.yaml` / `CP-PENDING-AUTO-035.fixture_note` / 種別: 陳腐化訂正・観測追記 / 根拠: `eco/ECO-140.diff:6714-6719,8516,8939-8944`、`code/tests/ViewPrism2.Tests/CpIntegrityReviewTests.cs:39-1153`、`code/tests/ViewPrism2.Tests/CpRelinkUnifiedBatchTests.cs:21-215`、`code/tests/ViewPrism2.Tests/GfIntegrityReviewVisualParityTests.cs:33-253`。削除済み fixture と移管先を明示。
- `bom/33-control-plan.yaml` / `CP-INTEGRITY-036.fixture・fixture_note` / 種別: 欠落追加・観測追記 / 根拠: `eco/ECO-140.md:230-248`、`eco/ECO-140.diff:7424-7727`、`code/tests/ViewPrism2.Tests/CpRelinkUnifiedBatchTests.cs:21-215`。一本化 relink の専用 fixture を追加。
- `bom/33-control-plan.yaml` / `CP-REPAIR-AUTOALL-023.fixture_note` / 種別: 陳腐化訂正・観測追記 / 根拠: `eco/ECO-140.md:232-240`、`eco/ECO-140.diff:7896-7901`、`code/tests/ViewPrism2.Tests/CpIntegrityReviewTests.cs:984-1058`、`code/tests/ViewPrism2.Tests/CpRelinkUnifiedBatchTests.cs:21-215`。削除済み歴史 fixture と現行検査先を明示。
- `bom/33-control-plan.yaml` / `CP-REPAIR-CARD-021.fixture_note` / 種別: 陳腐化訂正・観測追記 / 根拠: `eco/ECO-140.md:232-240`、`eco/ECO-140.diff:7896-7901,8516-8938`、`code/tests/ViewPrism2.Tests/CpIntegrityReviewTests.cs:1016-1058`、`code/tests/ViewPrism2.Tests/GfIntegrityReviewVisualParityTests.cs:138-191`。削除済み歴史 fixture と統合面の候補カード検査先を明示。

## 未編集・要裁定

- `bom/34-routing.yaml` / `ROUTING-V4REPAIR-001・ROUTE4-SURFACE` / 未編集。これは V4 製造当時の入力/成果を記録する歴史 route で、ECO-140 order の影響 BOM (`eco/ECO-140.md:98-112`) に routing 改訂指示がなく、`eco/ECO-140.diff` に 34-routing hunk もない。M-UI-REPAIR-027 を M-UI-INTEGRITY-055 へ置換すると過去 route の意味を変えるため、ECO-140 の追補 route を新設すべきかは別裁定とした。
- `bom/33-control-plan.yaml` / `CP-UI-G1` 内の ECO-129 歴史節 / 未編集。旧 `GfPendingReviewVisualParityTests` 名は当時の受入記録として保持し、現行検査面の superseded/移管は CP-PENDING-AUTO-035 と CP-INTEGRITY-036 に明記した。累積 golden 記録そのものを書き換える根拠は ECO order にない。

## 検証方針

- 制約に従い、git・ネットワーク・ビルド・テスト・アプリ実行は使用していない。検証は BOM の ID/参照、artifact/fixture の現存性、ECO diff の追加・削除 hunk、現行コードの宣言位置の静的突合だけで行う。
