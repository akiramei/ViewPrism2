score:
  reference_items:
    - id: R-001
      verdict: covered
      evidence: "candidate/bom-diff.patch: 30-ebom.yaml @@ -801,13 +805,15 E-UI-PENDING-049"
      note: "既存の高信頼対象・曖昧除外契約を変更せず、surface の superseded 化だけを追加している。"
    - id: R-002
      verdict: covered
      evidence: "candidate/bom-diff.patch: 30-ebom.yaml @@ -801,13 +805,15 E-UI-PENDING-049"
      note: "既存の原子一括 relink、ID・タグ保持、missing 解消契約を壊していない。"
    - id: R-003
      verdict: covered
      evidence: "candidate/bom-diff.patch: 33-control-plan.yaml @@ -783,8 +783,8 CP-PENDING-AUTO-035"
      note: "歴史 CP を保持し、削除 fixture と後継 fixture を明示している。"
    - id: R-004
      verdict: covered
      evidence: "candidate/bom-diff.patch: 32-mbom.yaml @@ -493,22 +495,24 M-RELINK-025"
      note: "旧 AcceptHighConfidenceAsync/AdjudicatePendingBatchAsync を復活させず、現行 relink API を記録している。"
    - id: R-005
      verdict: covered
      evidence: "candidate/bom-diff.patch: 10-requirements.yaml・20-spec.md に該当 hunk なし"
      note: "改善前から反映済みの単一面・単一入口契約を変更していない。"
    - id: R-006
      verdict: covered
      evidence: "candidate/bom-diff.patch: 30-ebom.yaml @@ -845,7 +851,7 E-UI-INTEGRITY-050"
      note: "既存の三分類・T4/T13 混在原子適用契約を維持している。"
    - id: R-007
      verdict: covered
      evidence: "candidate/bom-diff.patch: 10-requirements.yaml・20-spec.md に該当 hunk なし"
      note: "改善前から反映済みの on-demand SHA-256 と baseline fallback を変更していない。"
    - id: R-008
      verdict: covered
      evidence: "candidate/bom-diff.patch: 30-ebom.yaml @@ -763,6 +766,7 E-UI-REPAIR-039; @@ -801,13 +805,15 E-UI-PENDING-049"
      note: "両旧 surface の退役と、トラッシュ・旧 RelinkWindow の存続を明示している。"
    - id: R-009
      verdict: covered
      evidence: "candidate/bom-diff.patch: 32-mbom.yaml @@ -734,14 +739,16 M-UI-PENDING-054"
      note: "旧 artifact・入口の削除と M-UI-INTEGRITY-055 への置換を明示している。"
    - id: R-010
      verdict: partial
      evidence: "candidate/bom-diff.patch: 32-mbom.yaml @@ -532,17 +536,18 M-UI-REPAIR-027"
      note: "旧修復面の退役、後継 M unit、トラッシュ存続は明示したが、M-UI-REPAIR-027 から CP-INTEGRITY-036 への移管参照がない。"
    - id: R-011
      verdict: covered
      evidence: "candidate/bom-diff.patch: 30-ebom.yaml @@ -103,6 +103,9 E-RELINK-007"
      note: "一本化済みの選別・確定契約を維持し、consumer edge を補っている。"
    - id: R-012
      verdict: partial
      evidence: "candidate/bom-diff.patch: 32-mbom.yaml @@ -493,22 +495,24 M-RELINK-025"
      note: "IRelinkService、AutoRepairPair、RelinkSelection と主要 API は写像したが、RelinkService.IsHighConfidence の明示がない。"
    - id: R-013
      verdict: covered
      evidence: "candidate/bom-diff.patch: 32-mbom.yaml @@ -493,22 +495,24 M-RELINK-025"
      note: "CountAutoRepairableAsync を撤去し、GetAutoRepairablePairsAsync と統合面 Load の応答性契約を保持している。"
    - id: R-014
      verdict: covered
      evidence: "candidate/bom-diff.patch: 32-mbom.yaml @@ -750,13 +757,17 M-UI-INTEGRITY-055"
      note: "統合面クラスタを列挙し、RelinkService と ImageRepository は所有 unit への明示的な参照で接続している。"
    - id: R-015
      verdict: covered
      evidence: "candidate/bom-diff.patch: 32-mbom.yaml @@ -750,13 +757,17 M-UI-INTEGRITY-055"
      note: "Classify、LoadAsync、ApplyAutomaticAsync、hash 再利用、混在 batch、個別操作を記録している。"
    - id: R-016
      verdict: covered
      evidence: "candidate/bom-diff.patch: 32-mbom.yaml @@ -119,13 +118,16 M-DB-007"
      note: "三つの限定 read-model API と pending∪missing 境界を明示している。"
    - id: R-017
      verdict: partial
      evidence: "candidate/bom-diff.patch: 32-mbom.yaml @@ -119,13 +118,16 M-DB-007"
      note: "両 batch API、単一 transaction、stale rollback は記録したが、SQL の件数不一致 rollback 条件を明示していない。"
    - id: R-018
      verdict: covered
      evidence: "candidate/bom-diff.patch: 32-mbom.yaml @@ -91,10 +91,10 M-SCAN-005; @@ -119,13 +118,16 M-DB-007"
      note: "migration 011、複合 index、モデル列、旧 hash 保全フラグを写像している。"
    - id: R-019
      verdict: covered
      evidence: "candidate/bom-diff.patch: 32-mbom.yaml @@ -102,8 +102,7 M-SCAN-005"
      note: "UpdateMetaAndPend の二経路だけが保全フラグを立てることを明示し、判定ロジックを変更していない。"
    - id: R-020
      verdict: covered
      evidence: "candidate/bom-diff.patch: 32-mbom.yaml @@ -91,10 +91,10 M-SCAN-005; @@ -750,13 +757,17 M-UI-INTEGRITY-055"
      note: "hash provider を裁定面へ帰属させ、scan 判定から分離している。"
    - id: R-021
      verdict: covered
      evidence: "candidate/bom-diff.patch: 32-mbom.yaml @@ -734,14 +739,16 M-UI-PENDING-054"
      note: "PendingReviewService を個別 T13/T14/T15 の存続サービスとして記録している。"
    - id: R-022
      verdict: covered
      evidence: "candidate/bom-diff.patch: 32-mbom.yaml @@ -750,13 +757,17 M-UI-INTEGRITY-055"
      note: "WindowService、事象件数、統合入口、裁定後 refresh を写像している。"
    - id: R-023
      verdict: covered
      evidence: "candidate/bom-diff.patch: 32-mbom.yaml @@ -532,17 +536,18 M-UI-REPAIR-027; @@ -750,13 +757,17 M-UI-INTEGRITY-055"
      note: "旧 repair キーの撤去と統合面 ja/en 資産の帰属を記録している。"
    - id: R-024
      verdict: covered
      evidence: "candidate/bom-diff.patch: 33-control-plan.yaml @@ -802,8 +802,8 CP-INTEGRITY-036"
      note: "既存の統合 CP ベクタを維持し、役割別 fixture を明確化している。"
    - id: R-025
      verdict: covered
      evidence: "candidate/bom-diff.patch: 33-control-plan.yaml @@ -802,8 +802,8 CP-INTEGRITY-036"
      note: "CpRelinkUnifiedBatchTests.cs とその選別・原子 batch の役割を fixture に追加している。"
    - id: R-026
      verdict: partial
      evidence: "candidate/bom-diff.patch: 32-mbom.yaml @@ -493,22 +495,24 M-RELINK-025"
      note: "M-RELINK-025 には CP-INTEGRITY-036 を追加したが、M-DB-007 と M-SCAN-005 の acceptance_refs は未更新。"
    - id: R-027
      verdict: covered
      evidence: "candidate/bom-diff.patch: 33-control-plan.yaml @@ -783,8 +783,8 CP-PENDING-AUTO-035; @@ -834,11 +834,11 CP-REPAIR-AUTOALL-023/CP-REPAIR-CARD-021"
      note: "旧 CP を削除せず、削除 fixture と後継検査実体を記録している。"
    - id: R-028
      verdict: covered
      evidence: "candidate/bom-diff.patch: 33-control-plan.yaml @@ -802,8 +802,8 CP-INTEGRITY-036"
      note: "既存 test_vectors を変更せず、GF-140-01〜03 の再発防止契約を保持している。"
    - id: R-029
      verdict: covered
      evidence: "candidate/bom-diff.patch: 30-ebom.yaml @@ -763,6 +766,7 E-UI-REPAIR-039; 32-mbom.yaml @@ -532,17 +536,18 M-UI-REPAIR-027"
      note: "旧 RelinkWindow/RelinkViewModel を統合対象外の存続 artifact と明示している。"
    - id: R-030
      verdict: covered
      evidence: "candidate/bom-diff.patch: 32-mbom.yaml @@ -750,13 +757,17 M-UI-INTEGRITY-055"
      note: "ConfirmDialog/ConfirmListAsync を統合面の CMP-011 再利用として束ねている。"
    - id: R-031
      verdict: covered
      evidence: "candidate/bom-diff.patch: 33-control-plan.yaml @@ -802,8 +802,8 CP-INTEGRITY-036"
      note: "既存 CP 実装として扱い、新規設計品目を追加していない。"
    - id: R-032
      verdict: partial
      evidence: "candidate/bom-diff.patch: 32-mbom.yaml @@ -493,22 +495,24 M-RELINK-025; @@ -532,17 +536,18 M-UI-REPAIR-027; @@ -734,14 +739,16 M-UI-PENDING-054"
      note: "旧 Window/VM、専用 tests、CountAutoRepairableAsync の退役と存続 API は記録したが、App.axaml の RepairIcon 撤去を記録していない。"
  candidate_edits:
    - hunk: "30-ebom.yaml @@ -92,7 +92,7 E-HASH-006"
      verdict: grounded
      checked: ["eco/ECO-140.md:80-85", "code/src/ViewPrism2.Infrastructure/Scanning/IntegrityReviewFileHashProvider.cs:10-24"]
    - hunk: "30-ebom.yaml @@ -103,6 +103,9 E-RELINK-007"
      verdict: grounded
      checked: ["eco/ECO-140.md:88-90", "eco/ECO-140.md:134-159", "code/src/ViewPrism2.Core/Services/Repair/IntegrityReviewService.cs:78-108"]
    - hunk: "30-ebom.yaml @@ -320,7 +323,7 E-CRITERIA-037"
      verdict: grounded
      checked: ["eco/ECO-140.md:86-87", "code/src/ViewPrism2.App/ViewModels/IntegrityReviewViewModel.cs:635-670"]
    - hunk: "30-ebom.yaml @@ -711,7 +714,7 E-DESIGN-028"
      verdict: grounded
      checked: ["eco/ECO-140.md:119-124", "eco/ECO-140.diff:987-1773", "code/src/ViewPrism2.App/Views/IntegrityReviewWindow.axaml:1"]
    - hunk: "30-ebom.yaml @@ -763,6 +766,7 E-UI-REPAIR-039"
      verdict: grounded
      checked: ["eco/ECO-140.md:78-90", "eco/ECO-140.md:232-240", "eco/ECO-140.diff:2362-2804", "code/src/ViewPrism2.App/Views/RelinkWindow.axaml:1"]
    - hunk: "30-ebom.yaml @@ -801,13 +805,15 E-UI-PENDING-049"
      verdict: grounded
      checked: ["eco/ECO-139.md:176-193", "eco/ECO-140.md:173-180", "eco/ECO-140.diff:1774-2267"]
    - hunk: "30-ebom.yaml @@ -845,7 +851,7 E-UI-INTEGRITY-050"
      verdict: grounded
      checked: ["eco/ECO-140.md:80-85", "code/src/ViewPrism2.Core/Services/Repair/IntegrityReviewService.cs:305-308", "code/src/ViewPrism2.Infrastructure/Scanning/IntegrityReviewFileHashProvider.cs:12-24"]
    - hunk: "32-mbom.yaml @@ -91,10 +91,10 M-SCAN-005 artifact ownership"
      verdict: grounded
      checked: ["eco/ECO-140.md:83-85", "eco/ECO-140.md:143-154", "eco/ECO-140.md:202-207"]
    - hunk: "32-mbom.yaml @@ -102,8 +102,7 M-SCAN-005 interface_contract"
      verdict: grounded
      checked: ["eco/ECO-140.diff:5255-5303", "code/src/ViewPrism2.Infrastructure/Scanning/ScanService.cs:312-320", "code/src/ViewPrism2.Infrastructure/Scanning/ScanService.cs:608-616"]
    - hunk: "32-mbom.yaml @@ -119,13 +118,16 M-DB-007"
      verdict: grounded
      checked: ["eco/ECO-140.md:181-187", "eco/ECO-140.diff:4036-4109", "eco/ECO-140.diff:4764-4980", "code/src/ViewPrism2.Infrastructure/Database/ImageRepository.cs:292-342", "code/src/ViewPrism2.Infrastructure/Database/ImageRepository.cs:430-626"]
    - hunk: "32-mbom.yaml @@ -493,22 +495,24 M-RELINK-025"
      verdict: grounded
      checked: ["eco/ECO-140.md:88-90", "eco/ECO-140.md:134-159", "eco/ECO-140.md:238-240", "code/src/ViewPrism2.Core/Services/Repair/IntegrityReviewService.cs:80-108", "code/src/ViewPrism2.Infrastructure/Scanning/RelinkService.cs:38-116", "code/src/ViewPrism2.Infrastructure/Scanning/RelinkService.cs:350-385"]
    - hunk: "32-mbom.yaml @@ -532,17 +536,18 M-UI-REPAIR-027"
      verdict: grounded
      checked: ["eco/ECO-140.md:78-90", "eco/ECO-140.md:232-240", "eco/ECO-140.diff:2362-2804", "code/src/ViewPrism2.App/Views/RelinkWindow.axaml:1", "code/src/ViewPrism2.App/ViewModels/ImageTabTrashViewModel.cs:1"]
    - hunk: "32-mbom.yaml @@ -734,14 +739,16 M-UI-PENDING-054"
      verdict: grounded
      checked: ["eco/ECO-140.md:173-180", "eco/ECO-140.diff:1774-2267", "code/src/ViewPrism2.Core/Services/Repair/PendingReviewService.cs:19-58"]
    - hunk: "32-mbom.yaml @@ -750,13 +757,17 M-UI-INTEGRITY-055"
      verdict: grounded
      checked: ["eco/ECO-140.md:119-124", "eco/ECO-140.md:169-187", "eco/ECO-140.diff:987-1773", "code/src/ViewPrism2.Core/Services/Repair/IntegrityReviewService.cs:145-378", "code/src/ViewPrism2.App/ViewModels/IntegrityReviewViewModel.cs:277-336", "code/src/ViewPrism2.App/Services/IWindowService.cs:57-87"]
    - hunk: "33-control-plan.yaml @@ -783,8 +783,8 CP-PENDING-AUTO-035"
      verdict: grounded
      checked: ["eco/ECO-140.md:232-248", "eco/ECO-140.diff:6714-7391", "eco/ECO-140.diff:8939-9299", "code/tests/ViewPrism2.Tests/CpRelinkUnifiedBatchTests.cs:15-244"]
    - hunk: "33-control-plan.yaml @@ -802,8 +802,8 CP-INTEGRITY-036"
      verdict: grounded
      checked: ["eco/ECO-140.md:232-248", "eco/ECO-140.diff:7424-7727", "code/tests/ViewPrism2.Tests/CpRelinkUnifiedBatchTests.cs:33-244"]
    - hunk: "33-control-plan.yaml @@ -834,11 +834,11 CP-REPAIR-AUTOALL-023"
      verdict: grounded
      checked: ["eco/ECO-140.md:232-240", "eco/ECO-140.diff:7896-8455", "code/tests/ViewPrism2.Tests/CpIntegrityReviewTests.cs:1007", "code/tests/ViewPrism2.Tests/CpRelinkUnifiedBatchTests.cs:89-244"]
    - hunk: "33-control-plan.yaml @@ -834,11 +834,11 CP-REPAIR-CARD-021"
      verdict: grounded
      checked: ["eco/ECO-140.md:232-240", "eco/ECO-140.diff:7896-8455", "code/tests/ViewPrism2.Tests/CpIntegrityReviewTests.cs:1", "code/tests/ViewPrism2.Tests/GfIntegrityReviewVisualParityTests.cs:1"]
  totals:
    covered: 27
    partial: 5
    missing: 0
    broken: 0
    grounded: 18
    ungrounded: 0
    contradicting: 0
    design_overwrite: 0