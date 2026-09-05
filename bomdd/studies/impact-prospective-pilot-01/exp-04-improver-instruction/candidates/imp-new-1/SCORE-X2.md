score:
  reference_items:
    - id: R-001
      verdict: covered
      evidence: "candidate/bom-diff.patch: 30-ebom.yaml @@ -800,14 +804,14 E-UI-PENDING-049"
    - id: R-002
      verdict: covered
      evidence: "candidate/bom-diff.patch: 30-ebom.yaml @@ -800,14 +804,14 E-UI-PENDING-049"
    - id: R-003
      verdict: covered
      evidence: "candidate/bom-diff.patch: 33-control-plan.yaml @@ -128,7 +128,7 and @@ -783,8 +783,8 CP-PENDING-AUTO-035"
    - id: R-004
      verdict: covered
      evidence: "candidate/bom-diff.patch: 32-mbom.yaml @@ -497,13 +502,15 M-RELINK-025"
      note: "旧 accept 契約を復活させず、現行 relink API を記録している。"
    - id: R-005
      verdict: covered
      evidence: "candidate/bom-diff.patch: 30-ebom.yaml @@ -510,7 +513,7 and @@ -521,6 +524,7 E-UI-MODE-041"
    - id: R-006
      verdict: covered
      evidence: "candidate/bom-diff.patch: 30-ebom.yaml @@ -845,7 +849,7 E-UI-INTEGRITY-050; 32-mbom.yaml @@ -750,13 +759,19"
    - id: R-007
      verdict: covered
      evidence: "candidate/bom-diff.patch: 32-mbom.yaml @@ -750,13 +759,19 M-UI-INTEGRITY-055"
    - id: R-008
      verdict: covered
      evidence: "candidate/bom-diff.patch: 30-ebom.yaml @@ -756,7 +760,7 and @@ -800,14 +804,14"
    - id: R-009
      verdict: covered
      evidence: "candidate/bom-diff.patch: 32-mbom.yaml @@ -736,10 +744,11 M-UI-PENDING-054"
    - id: R-010
      verdict: partial
      evidence: "candidate/bom-diff.patch: 32-mbom.yaml @@ -533,10 +540,11 M-UI-REPAIR-027"
      note: "旧面の superseded、後継 unit、トラッシュ存続は記録したが、この unit から CP-INTEGRITY-036 への移管参照を追加していない。"
    - id: R-011
      verdict: covered
      evidence: "candidate/bom-diff.patch: 30-ebom.yaml @@ -103,6 +103,9 E-RELINK-007; 32-mbom.yaml @@ -497,13 +502,15"
    - id: R-012
      verdict: partial
      evidence: "candidate/bom-diff.patch: 32-mbom.yaml @@ -497,13 +502,15 M-RELINK-025"
      note: "多くの API は追加したが IsHighConfidence と GetUniquelyRelinkableAsync がなく、artifact には実在しない RelinkBatchPair を記載している。"
    - id: R-013
      verdict: partial
      evidence: "candidate/bom-diff.patch: 32-mbom.yaml @@ -497,13 +502,15 M-RELINK-025"
      note: "CountAutoRepairableAsync の撤去と GetAutoRepairablePairsAsync は反映したが、旧「修復 Load」表現を統合面 LoadAsync の応答性契約へ更新していない。"
    - id: R-014
      verdict: covered
      evidence: "candidate/bom-diff.patch: 32-mbom.yaml @@ -750,13 +759,19 M-UI-INTEGRITY-055"
    - id: R-015
      verdict: covered
      evidence: "candidate/bom-diff.patch: 32-mbom.yaml @@ -750,13 +759,19 M-UI-INTEGRITY-055"
    - id: R-016
      verdict: covered
      evidence: "candidate/bom-diff.patch: 32-mbom.yaml @@ -119,13 +119,18 M-DB-007"
    - id: R-017
      verdict: partial
      evidence: "candidate/bom-diff.patch: 32-mbom.yaml @@ -119,13 +119,18 M-DB-007"
      note: "ApplyIntegrityReviewBatchAsync、混在 transaction、stale rollback は記録したが、repository の ApplyRelinkBatchAsync と更新・削除件数不一致 rollback を明示していない。"
    - id: R-018
      verdict: covered
      evidence: "candidate/bom-diff.patch: 32-mbom.yaml @@ -29,7 +29,7, @@ -39,6 +39,7, @@ -102,8 +103,7, and @@ -119,13 +119,18"
    - id: R-019
      verdict: covered
      evidence: "candidate/bom-diff.patch: 32-mbom.yaml @@ -102,8 +103,7 M-SCAN-005"
    - id: R-020
      verdict: covered
      evidence: "candidate/bom-diff.patch: 32-mbom.yaml @@ -91,10 +92,10 and @@ -750,13 +759,19"
    - id: R-021
      verdict: covered
      evidence: "candidate/bom-diff.patch: 32-mbom.yaml @@ -736,10 +744,11 and @@ -750,13 +759,19"
    - id: R-022
      verdict: covered
      evidence: "candidate/bom-diff.patch: 30-ebom.yaml @@ -510,7 +513,7; 32-mbom.yaml @@ -750,13 +759,19"
    - id: R-023
      verdict: covered
      evidence: "candidate/bom-diff.patch: 32-mbom.yaml @@ -750,13 +759,19 M-UI-INTEGRITY-055"
    - id: R-024
      verdict: covered
      evidence: "candidate/bom-diff.patch: 33-control-plan.yaml @@ -802,8 +802,8 and @@ -816,6 +816,7 CP-INTEGRITY-036"
    - id: R-025
      verdict: covered
      evidence: "candidate/bom-diff.patch: 33-control-plan.yaml @@ -802,8 +802,8 CP-INTEGRITY-036"
    - id: R-026
      verdict: missing
      evidence: "candidate/bom-diff.patch: 32-mbom.yaml @@ -91,10 +92,10, @@ -119,13 +119,18, and @@ -497,13 +502,15"
      note: "3 unit の acceptance_refs はいずれも編集されず、CP-INTEGRITY-036 が追加されていない。"
    - id: R-027
      verdict: covered
      evidence: "candidate/bom-diff.patch: 33-control-plan.yaml @@ -783,8 +783,8 and @@ -834,11 +835,11"
    - id: R-028
      verdict: covered
      evidence: "candidate/bom-diff.patch: 33-control-plan.yaml @@ -816,6 +816,7 CP-INTEGRITY-036.test_vectors"
    - id: R-029
      verdict: covered
      evidence: "candidate/bom-diff.patch: 30-ebom.yaml @@ -756,7 +760,7; 32-mbom.yaml @@ -533,10 +540,11 and @@ -750,13 +759,19"
    - id: R-030
      verdict: covered
      evidence: "candidate/bom-diff.patch: 32-mbom.yaml @@ -750,13 +759,19 M-UI-INTEGRITY-055.confirmation_api"
    - id: R-031
      verdict: covered
      evidence: "candidate/bom-diff.patch: 33-control-plan.yaml @@ -834,11 +835,11 CP-DISPLAY-PARITY-022"
    - id: R-032
      verdict: partial
      evidence: "candidate/bom-diff.patch: 32-mbom.yaml @@ -497,13 +502,15, @@ -533,10 +540,11, and @@ -736,10 +744,11"
      note: "旧 Window/VM、専用 fixture、CountAutoRepairableAsync の撤去と存続面は反映したが、App.axaml の RepairIcon 撤去を旧面退役へ明示的に束ねていない。"

  candidate_edits:
    - hunk: "30-ebom.yaml @@ -92,7 +92,7 E-HASH-006.graph_edges"
      verdict: grounded
      checked: ["candidate/CHANGELOG.md:15", "eco/ECO-140.md:80-89", "code/src/ViewPrism2.Core/Services/Repair/IntegrityReviewService.cs:306"]
    - hunk: "30-ebom.yaml @@ -103,6 +103,9 E-RELINK-007.graph_edges"
      verdict: grounded
      checked: ["candidate/CHANGELOG.md:16", "eco/ECO-140.md:88-89", "code/src/ViewPrism2.Core/Services/Repair/IntegrityReviewService.cs:78-108"]
    - hunk: "30-ebom.yaml @@ -171,7 +174,7 E-DB-010.graph_edges"
      verdict: grounded
      checked: ["candidate/CHANGELOG.md:17", "eco/ECO-140.md:173-187", "code/src/ViewPrism2.Infrastructure/Database/ImageRepository.cs:292-307"]
    - hunk: "30-ebom.yaml @@ -320,7 +323,7 E-CRITERIA-037.graph_edges"
      verdict: grounded
      checked: ["candidate/CHANGELOG.md:18", "eco/ECO-140.md:86-87", "code/src/ViewPrism2.App/ViewModels/IntegrityReviewViewModel.cs:636"]
    - hunk: "30-ebom.yaml @@ -510,7 +513,7 E-UI-MODE-041.depends_on"
      verdict: grounded
      checked: ["candidate/CHANGELOG.md:19", "eco/ECO-140.md:78-90", "eco/ECO-140.md:121-124"]
    - hunk: "30-ebom.yaml @@ -521,6 +524,7 E-UI-MODE-041.invariants"
      verdict: grounded
      checked: ["candidate/CHANGELOG.md:19", "eco/ECO-140.md:90-96", "eco/ECO-140.diff:912-941"]
    - hunk: "30-ebom.yaml @@ -711,7 +715,7 E-DESIGN-028.graph_edges"
      verdict: grounded
      checked: ["candidate/CHANGELOG.md:20", "eco/ECO-140.md:119-124", "eco/ECO-140.diff:2861-3443"]
    - hunk: "30-ebom.yaml @@ -756,7 +760,7 E-UI-REPAIR-039.name"
      verdict: grounded
      checked: ["candidate/CHANGELOG.md:21", "eco/ECO-140.md:90-96", "eco/ECO-140.diff:3822-3994"]
    - hunk: "30-ebom.yaml @@ -800,14 +804,14 E-UI-PENDING-049"
      verdict: grounded
      checked: ["candidate/CHANGELOG.md:22", "eco/ECO-139.md:170-198", "eco/ECO-140.diff:1774-2267"]
    - hunk: "30-ebom.yaml @@ -845,7 +849,7 E-UI-INTEGRITY-050.depends_on"
      verdict: grounded
      checked: ["candidate/CHANGELOG.md:23", "eco/ECO-140.md:116-124", "eco/ECO-140.md:173-187"]
    - hunk: "32-mbom.yaml @@ -29,7 +29,7 M-CORE-001.artifact.path_note"
      verdict: grounded
      checked: ["candidate/CHANGELOG.md:24", "code/src/ViewPrism2.Core/Models/Entities.cs:51", "code/src/ViewPrism2.Core/Models/ScanMutationBatch.cs:9"]
    - hunk: "32-mbom.yaml @@ -39,6 +39,7 M-CORE-001.interface_contract.pending_baseline"
      verdict: grounded
      checked: ["candidate/CHANGELOG.md:24", "eco/ECO-140.md:185-187", "code/src/ViewPrism2.Core/Models/ScanMutationBatch.cs:9"]
    - hunk: "32-mbom.yaml @@ -91,10 +92,10 M-SCAN-005 ownership"
      verdict: grounded
      checked: ["candidate/CHANGELOG.md:25", "eco/ECO-140.md:145-154", "code/src/ViewPrism2.Infrastructure/Scanning/RelinkService.cs:35-116"]
    - hunk: "32-mbom.yaml @@ -102,8 +103,7 M-SCAN-005.interface_contract"
      verdict: grounded
      checked: ["candidate/CHANGELOG.md:25", "eco/ECO-140.md:202-207", "code/src/ViewPrism2.Infrastructure/Scanning/ScanService.cs:320", "code/src/ViewPrism2.Infrastructure/Scanning/ScanService.cs:616"]
    - hunk: "32-mbom.yaml @@ -119,13 +119,18 M-DB-007"
      verdict: grounded
      checked: ["candidate/CHANGELOG.md:26", "code/src/ViewPrism2.Core/Repositories/IImageRepository.cs:44-126", "code/src/ViewPrism2.Infrastructure/Database/ImageRepository.cs:292-365", "code/src/ViewPrism2.Infrastructure/Database/ImageRepository.cs:430-626"]
    - hunk: "32-mbom.yaml @@ -497,13 +502,15 M-RELINK-025"
      verdict: ungrounded
      checked: ["candidate/CHANGELOG.md:27", "code/src/ViewPrism2.Core/Services/Repair/IntegrityReviewService.cs:59-108", "code/src/ViewPrism2.Infrastructure/Scanning/RelinkService.cs:38-116", "code/src/ViewPrism2.Infrastructure/Scanning/RelinkService.cs:350-385"]
      note: "大半の API は実在するが、artifact に記載した RelinkBatchPair は code/eco/bom に実在しない。さらに根拠上の IsHighConfidence と GetUniquelyRelinkableAsync を写像していない。"
    - hunk: "32-mbom.yaml @@ -533,10 +540,11 M-UI-REPAIR-027"
      verdict: grounded
      checked: ["candidate/CHANGELOG.md:28", "eco/ECO-140.md:232-240", "eco/ECO-140.diff:2362-2804", "eco/ECO-140.diff:3822-3994"]
    - hunk: "32-mbom.yaml @@ -736,10 +744,11 M-UI-PENDING-054"
      verdict: grounded
      checked: ["candidate/CHANGELOG.md:29", "eco/ECO-140.diff:1774-2267", "eco/ECO-140.diff:3484-3808", "code/src/ViewPrism2.Core/Services/Repair/PendingReviewService.cs:23-66"]
    - hunk: "32-mbom.yaml @@ -750,13 +759,19 M-UI-INTEGRITY-055"
      verdict: grounded
      checked: ["candidate/CHANGELOG.md:30", "code/src/ViewPrism2.App/Services/IWindowService.cs:30-87", "code/src/ViewPrism2.Core/Services/Repair/IntegrityReviewService.cs:145-351", "code/src/ViewPrism2.App/ViewModels/IntegrityReviewViewModel.cs:277-708"]
    - hunk: "33-control-plan.yaml @@ -128,7 +128,7 CP-SCAN-004"
      verdict: grounded
      checked: ["candidate/CHANGELOG.md:31", "eco/ECO-140.md:232-248", "code/src/ViewPrism2.Infrastructure/Scanning/RelinkService.cs:94-116", "code/src/ViewPrism2.Infrastructure/Scanning/RelinkService.cs:374-385"]
    - hunk: "33-control-plan.yaml @@ -783,8 +783,8 CP-PENDING-AUTO-035"
      verdict: grounded
      checked: ["candidate/CHANGELOG.md:32", "eco/ECO-140.diff:6714-7391", "eco/ECO-140.diff:8939-9299", "code/tests/ViewPrism2.Tests/CpRelinkUnifiedBatchTests.cs:33-244"]
    - hunk: "33-control-plan.yaml @@ -802,8 +802,8 CP-INTEGRITY-036.fixture"
      verdict: grounded
      checked: ["candidate/CHANGELOG.md:33", "eco/ECO-140.diff:7424-7727", "code/tests/ViewPrism2.Tests/CpRelinkUnifiedBatchTests.cs:33-244", "code/tests/ViewPrism2.Tests/CpIntegrityReviewTests.cs:395-968"]
    - hunk: "33-control-plan.yaml @@ -816,6 +816,7 CP-INTEGRITY-036.test_vectors"
      verdict: grounded
      checked: ["candidate/CHANGELOG.md:33", "code/src/ViewPrism2.Infrastructure/Database/ImageRepository.cs:292-365", "code/src/ViewPrism2.Infrastructure/Database/ImageRepository.cs:430-626", "code/tests/ViewPrism2.Tests/CpIntegrityReviewTests.cs:699-968"]
    - hunk: "33-control-plan.yaml @@ -834,11 +835,11 CP-REPAIR-AUTOALL-023/CP-DISPLAY-PARITY-022/CP-REPAIR-CARD-021"
      verdict: grounded
      checked: ["candidate/CHANGELOG.md:34-35", "eco/ECO-140.md:232-240", "eco/ECO-140.diff:7896-8400", "code/src/ViewPrism2.App/ViewModels/RelinkViewModel.cs:19-76"]
    - hunk: "34-routing.yaml @@ -135,6 +135,7 ROUTING-V4REPAIR-001"
      verdict: contradicting
      checked: ["REFERENCE.yaml:410-414", "bom/34-routing.yaml:135-177"]
      note: "must_not_change が過去 routing の保持を明示している。"
    - hunk: "34-routing.yaml @@ -167,7 +168,7 ROUTE4-SURFACE.output"
      verdict: contradicting
      checked: ["REFERENCE.yaml:410-414", "bom/34-routing.yaml:167-177"]
      note: "ECO-140 が再裁定していない歴史的製造 output を現行 artifact 表現へ上書きしている。"

  totals:
    covered: 26
    partial: 5
    missing: 1
    broken: 0
    grounded: 23
    ungrounded: 1
    contradicting: 2
    design_overwrite: 0