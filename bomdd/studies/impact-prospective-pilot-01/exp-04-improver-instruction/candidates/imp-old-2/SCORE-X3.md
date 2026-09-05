score:
  reference_items:
    - id: R-001
      verdict: covered
      evidence: "candidate/bom-diff.patch: 30-ebom.yaml @@ -800,12 +801,12 E-UI-PENDING-049（invariants は変更なし）"
    - id: R-002
      verdict: covered
      evidence: "candidate/bom-diff.patch: 30-ebom.yaml @@ -800,12 +801,12 E-UI-PENDING-049（原子 relink invariants は変更なし）"
    - id: R-003
      verdict: covered
      evidence: "candidate/bom-diff.patch: 33-control-plan.yaml @@ -784,7 +784,7 CP-PENDING-AUTO-035"
    - id: R-004
      verdict: covered
      evidence: "candidate/bom-diff.patch: 32-mbom.yaml @@ -497,19 +497,20 M-RELINK-025; @@ -735,30 +740,34 M-UI-PENDING-054"
    - id: R-005
      verdict: covered
      evidence: "candidate/bom-diff.patch: 30-ebom.yaml @@ -518,9 +518,10 E-UI-MODE-041（既反映の requirements/spec を壊さず統合入口を記録）"
    - id: R-006
      verdict: covered
      evidence: "candidate/bom-diff.patch: 対象 REQ-102/E-UI-INTEGRITY-050.invariants への変更なし"
    - id: R-007
      verdict: covered
      evidence: "candidate/bom-diff.patch: 対象 REQ-103/spec §2.11.8 への変更なし"
    - id: R-008
      verdict: covered
      evidence: "candidate/bom-diff.patch: 30-ebom.yaml @@ -756,12 +757,12 E-UI-REPAIR-039; @@ -800,12 +801,12 E-UI-PENDING-049"
    - id: R-009
      verdict: covered
      evidence: "candidate/bom-diff.patch: 32-mbom.yaml @@ -735,30 +740,34 M-UI-PENDING-054"
    - id: R-010
      verdict: covered
      evidence: "candidate/bom-diff.patch: 32-mbom.yaml @@ -532,11 +533,14 and @@ -549,7 +553,8 M-UI-REPAIR-027"
    - id: R-011
      verdict: covered
      evidence: "candidate/bom-diff.patch: E-RELINK-007 への変更なし; 32-mbom.yaml @@ -497,19 +497,20 M-RELINK-025 が現行 API を追記"
    - id: R-012
      verdict: partial
      evidence: "candidate/bom-diff.patch: 32-mbom.yaml @@ -497,19 +497,20 M-RELINK-025"
      note: "IRelinkService/AutoRepairPair/RelinkSelection、GetRelinkSelectionAsync、各 commit API は写像したが、IsHighConfidence と GetUniquelyRelinkableAsync を明記していない。"
    - id: R-013
      verdict: partial
      evidence: "candidate/bom-diff.patch: 32-mbom.yaml @@ -497,19 +497,20 M-RELINK-025"
      note: "CountAutoRepairableAsync を撤去し GetAutoRepairablePairsAsync を記録したが、残存 invariant は旧『修復 Load』表記のままで、統合面 LoadAsync の応答性契約への置換が不完全。"
    - id: R-014
      verdict: covered
      evidence: "candidate/bom-diff.patch: 32-mbom.yaml @@ -735,30 +740,34 M-UI-INTEGRITY-055"
    - id: R-015
      verdict: covered
      evidence: "candidate/bom-diff.patch: 32-mbom.yaml @@ -735,30 +740,34 M-UI-INTEGRITY-055 interface_contract.hashcheck"
    - id: R-016
      verdict: covered
      evidence: "candidate/bom-diff.patch: M-DB-007.interface_contract.repositories への変更なし"
    - id: R-017
      verdict: missing
      evidence: "candidate/bom-diff.patch: M-DB-007 の ApplyRelinkBatchAsync/ApplyIntegrityReviewBatchAsync 契約を追加する hunk なし"
    - id: R-018
      verdict: covered
      evidence: "candidate/bom-diff.patch: 32-mbom.yaml @@ -94,7 +94,7 M-SCAN-005（既反映の schema/interface/invariants は変更なし）"
    - id: R-019
      verdict: covered
      evidence: "candidate/bom-diff.patch: 32-mbom.yaml @@ -94,7 +94,7 M-SCAN-005 artifact.path_note"
    - id: R-020
      verdict: covered
      evidence: "candidate/bom-diff.patch: 32-mbom.yaml @@ -94,7 +94,7 M-SCAN-005; @@ -735,30 +740,34 M-UI-INTEGRITY-055"
    - id: R-021
      verdict: covered
      evidence: "candidate/bom-diff.patch: 32-mbom.yaml @@ -735,30 +740,34 M-UI-INTEGRITY-055 artifact.path_note"
    - id: R-022
      verdict: covered
      evidence: "candidate/bom-diff.patch: 32-mbom.yaml @@ -735,30 +740,34 M-UI-INTEGRITY-055; 30-ebom.yaml @@ -518,9 +518,10 E-UI-MODE-041"
    - id: R-023
      verdict: covered
      evidence: "candidate/bom-diff.patch: M-I18N-011 と既反映済み M-UI-INTEGRITY-055 の i18n 帰属への変更なし"
    - id: R-024
      verdict: covered
      evidence: "candidate/bom-diff.patch: 33-control-plan.yaml @@ -802,8 +802,8 CP-INTEGRITY-036"
    - id: R-025
      verdict: covered
      evidence: "candidate/bom-diff.patch: 33-control-plan.yaml @@ -802,8 +802,8 CP-INTEGRITY-036.fixture"
    - id: R-026
      verdict: partial
      evidence: "candidate/bom-diff.patch: 32-mbom.yaml @@ -497,19 +497,20 M-RELINK-025.acceptance_refs"
      note: "M-RELINK-025 には CP-INTEGRITY-036 を追加したが、M-SCAN-005 と M-DB-007 の acceptance_refs は更新していない。"
    - id: R-027
      verdict: covered
      evidence: "candidate/bom-diff.patch: 33-control-plan.yaml @@ -784,7 +784,7 CP-PENDING-AUTO-035; @@ -834,11 +834,11 CP-REPAIR-AUTOALL-023/CP-REPAIR-CARD-021"
    - id: R-028
      verdict: covered
      evidence: "candidate/bom-diff.patch: CP-INTEGRITY-036.test_vectors への変更なし"
    - id: R-029
      verdict: covered
      evidence: "candidate/bom-diff.patch: 30-ebom.yaml @@ -756,12 +757,12 E-UI-REPAIR-039; 32-mbom.yaml @@ -735,30 +740,34 M-UI-INTEGRITY-055"
    - id: R-030
      verdict: covered
      evidence: "candidate/bom-diff.patch: M-UI-INTEGRITY-055.adjudicate/CP-INTEGRITY-036 の CMP-011 契約への変更なし"
    - id: R-031
      verdict: covered
      evidence: "candidate/bom-diff.patch: 33-control-plan.yaml @@ -802,8 +802,8 CP-INTEGRITY-036（既存 CP 帰属を維持）"
    - id: R-032
      verdict: partial
      evidence: "candidate/bom-diff.patch: 32-mbom.yaml @@ -497,19 +497,20 M-RELINK-025; @@ -532,11 +533,14 M-UI-REPAIR-027; @@ -735,30 +740,34 M-UI-PENDING-054; 33-control-plan.yaml @@ -784,7 +784,7 and @@ -834,11 +834,11"
      note: "旧 Window/ViewModel・専用テストと CountAutoRepairableAsync の退役、存続 API/トラッシュの維持は記録したが、App.axaml RepairIcon の撤去を BOM 編集に記録していない。"

  candidate_edits:
    - hunk: "30-ebom.yaml @@ -320,7 +320,7 E-CRITERIA-037.graph_edges.consumers"
      verdict: grounded
      checked: ["eco/ECO-140.md:78-90", "eco/ECO-140.diff:133-150"]
    - hunk: "30-ebom.yaml @@ -394,7 +394,7 E-PACKAGE-047.invariants"
      verdict: grounded
      checked: ["eco/ECO-140.md:78-90", "eco/ECO-140.diff:30-58"]
    - hunk: "30-ebom.yaml @@ -505,12 +505,12 E-UI-MODE-041 requirement_refs/depends_on"
      verdict: grounded
      checked: ["eco/ECO-140.md:98-124", "eco/ECO-140.diff:133-150"]
    - hunk: "30-ebom.yaml @@ -518,9 +518,10 E-UI-MODE-041.invariants"
      verdict: grounded
      checked: ["eco/ECO-140.md:116-124", "eco/ECO-140.diff:30-62"]
    - hunk: "30-ebom.yaml @@ -530,7 +531,7 E-UI-MODE-041.acceptance_refs"
      verdict: grounded
      checked: ["eco/ECO-140.md:230-240", "eco/ECO-140.diff:155-182"]
    - hunk: "30-ebom.yaml @@ -711,7 +712,7 E-DESIGN-028.graph_edges.consumers"
      verdict: grounded
      checked: ["eco/ECO-140.md:119-124", "eco/ECO-140.diff:133-150"]
    - hunk: "30-ebom.yaml @@ -756,12 +757,12 E-UI-REPAIR-039"
      verdict: grounded
      checked: ["eco/ECO-140.md:76-108", "eco/ECO-140.diff:113-128"]
    - hunk: "30-ebom.yaml @@ -800,12 +801,12 E-UI-PENDING-049"
      verdict: grounded
      checked: ["eco/ECO-140.md:76-108", "eco/ECO-140.diff:121-150"]
    - hunk: "32-mbom.yaml @@ -94,7 +94,7 M-SCAN-005.artifact.path_note"
      verdict: grounded
      checked: ["eco/ECO-140.md:83-85", "eco/ECO-140.md:202-207", "code/src/ViewPrism2.Infrastructure/Scanning/IntegrityReviewFileHashProvider.cs:10-24", "code/src/ViewPrism2.Infrastructure/Scanning/RelinkService.cs:38-128"]
    - hunk: "32-mbom.yaml @@ -497,19 +497,20 M-RELINK-025"
      verdict: grounded
      checked: ["eco/ECO-140.md:88-90", "eco/ECO-140.md:238-240", "code/src/ViewPrism2.Core/Services/Repair/IntegrityReviewService.cs:60-108", "code/src/ViewPrism2.Infrastructure/Scanning/RelinkService.cs:84-128", "code/src/ViewPrism2.Infrastructure/Scanning/RelinkService.cs:214-385"]
    - hunk: "32-mbom.yaml @@ -532,11 +533,14 M-UI-REPAIR-027 retirement"
      verdict: grounded
      checked: ["eco/ECO-140.md:78-96", "eco/ECO-140.md:232-240", "eco/ECO-140.diff:2362-2804"]
    - hunk: "32-mbom.yaml @@ -549,7 +553,8 M-UI-REPAIR-027 acceptance/FMEA transfer"
      verdict: grounded
      checked: ["eco/ECO-140.md:230-240", "eco/ECO-140.diff:323-361"]
    - hunk: "32-mbom.yaml @@ -735,30 +740,34 M-UI-PENDING-054 retirement"
      verdict: grounded
      checked: ["eco/ECO-140.md:76-96", "eco/ECO-140.diff:1774-2267", "code/src/ViewPrism2.Core/Services/Repair/PendingReviewService.cs:19-58", "code/tests/ViewPrism2.Tests/CpIntegrityReviewTests.cs:39-58"]
    - hunk: "32-mbom.yaml @@ -735,30 +740,34 M-UI-INTEGRITY-055 observations/contracts"
      verdict: grounded
      checked: ["eco/ECO-140.md:169-184", "code/src/ViewPrism2.Core/Services/Repair/IntegrityReviewService.cs:239-378", "code/src/ViewPrism2.App/Services/WindowService.cs:156-179", "code/tests/ViewPrism2.Tests/CpRelinkUnifiedBatchTests.cs:21-251"]
    - hunk: "32-mbom.yaml @@ -820,9 +829,9 FMEA-031 ownership"
      verdict: grounded
      checked: ["eco/ECO-140.md:232-234", "code/tests/ViewPrism2.Tests/GfIntegrityReviewVisualParityTests.cs:138-189"]
    - hunk: "32-mbom.yaml @@ -820,9 +829,9 FMEA-034 ownership/semantics"
      verdict: grounded
      checked: ["eco/ECO-140.md:48-62", "eco/ECO-140.md:88-90", "code/tests/ViewPrism2.Tests/CpRelinkUnifiedBatchTests.cs:104-212"]
    - hunk: "33-control-plan.yaml @@ -128,7 +128,7 CP-SCAN-004 ECO-139 vector migration"
      verdict: grounded
      checked: ["eco/ECO-139.md:170-206", "eco/ECO-140.md:173-184", "code/tests/ViewPrism2.Tests/CpRelinkUnifiedBatchTests.cs:21-251"]
    - hunk: "33-control-plan.yaml @@ -784,7 +784,7 CP-PENDING-AUTO-035.fixture_note"
      verdict: contradicting
      checked: ["REFERENCE.yaml:432-436", "eco/ECO-140.md:232-234", "code/tests/ViewPrism2.Tests/CpIntegrityReviewTests.cs:39-1153", "code/tests/ViewPrism2.Tests/CpRelinkUnifiedBatchTests.cs:21-251"]
      note: "移管内容自体には実在根拠があるが、must_not_change が CP-PENDING-AUTO-035 を対象としており、候補がそのエントリを編集している。"
    - hunk: "33-control-plan.yaml @@ -802,8 +802,8 CP-INTEGRITY-036.fixture/fixture_note"
      verdict: grounded
      checked: ["eco/ECO-140.md:230-248", "eco/ECO-140.diff:7424-7727", "code/tests/ViewPrism2.Tests/CpRelinkUnifiedBatchTests.cs:10-251"]
    - hunk: "33-control-plan.yaml @@ -834,11 +834,11 CP-REPAIR-AUTOALL-023.fixture_note"
      verdict: contradicting
      checked: ["REFERENCE.yaml:438-442", "eco/ECO-140.md:232-240", "code/tests/ViewPrism2.Tests/CpIntegrityReviewTests.cs:984-1058", "code/tests/ViewPrism2.Tests/CpRelinkUnifiedBatchTests.cs:21-251"]
      note: "削除済み fixture と移管先は確認できるが、must_not_change 対象エントリへの編集に該当する。"
    - hunk: "33-control-plan.yaml @@ -834,11 +834,11 CP-REPAIR-CARD-021.fixture_note"
      verdict: contradicting
      checked: ["REFERENCE.yaml:438-442", "eco/ECO-140.md:232-240", "code/tests/ViewPrism2.Tests/CpIntegrityReviewTests.cs:1016-1058", "code/tests/ViewPrism2.Tests/GfIntegrityReviewVisualParityTests.cs:138-189"]
      note: "候補カード検査の移管先は実在するが、must_not_change 対象エントリへの編集に該当する。"

  totals:
    covered: 27
    partial: 4
    missing: 1
    broken: 0
    grounded: 18
    ungrounded: 0
    contradicting: 3
    design_overwrite: 0