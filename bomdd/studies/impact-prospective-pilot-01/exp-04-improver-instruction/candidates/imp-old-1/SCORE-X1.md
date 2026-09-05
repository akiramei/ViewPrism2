score:
  reference_items:
    - id: R-001
      verdict: covered
      evidence: "candidate/bom-diff.patch: 対象 hunk なし; bom/20-spec.md:1144-1179, bom/30-ebom.yaml:815-816"
    - id: R-002
      verdict: covered
      evidence: "candidate/bom-diff.patch: 対象 hunk なし; bom/20-spec.md:1159-1165, bom/30-ebom.yaml:815-816"
    - id: R-003
      verdict: covered
      evidence: "candidate/bom-diff.patch: 33-control-plan.yaml @@ -128,7 +128,7 および @@ -783,8 +783,8"
      note: "歴史的意味論を残し、撤去済み fixture の後継を記録している。"
    - id: R-004
      verdict: covered
      evidence: "candidate/bom-diff.patch: 32-mbom.yaml @@ -493,22 +491,25; 旧 accept API の復活なし"
    - id: R-005
      verdict: covered
      evidence: "candidate/bom-diff.patch: 30-ebom.yaml @@ -518,9 +518,10 E-UI-MODE-041"
    - id: R-006
      verdict: covered
      evidence: "candidate/bom-diff.patch: 対象契約を変更する hunk なし; bom/30-ebom.yaml:840-857"
    - id: R-007
      verdict: covered
      evidence: "candidate/bom-diff.patch: 対象 hunk なし; bom/10-requirements.yaml:1463-1485, bom/20-spec.md:1204-1210"
    - id: R-008
      verdict: covered
      evidence: "candidate/bom-diff.patch: 30-ebom.yaml @@ -761,7 +762,7 および @@ -805,7 +806,7"
    - id: R-009
      verdict: broken
      evidence: "candidate/bom-diff.patch: 32-mbom.yaml @@ -734,23 +738,26 M-UI-PENDING-054"
      note: "superseded と後継は明記したが、ebom_refs を [E-UI-PENDING-049] から [] にして旧写像を失わせた。"
    - id: R-010
      verdict: partial
      evidence: "candidate/bom-diff.patch: 32-mbom.yaml @@ -531,12 +532,15 M-UI-REPAIR-027"
      note: "旧面の退役、後継、トラッシュ存続は記録したが、移管先 CP-INTEGRITY-036 を acceptance_refs に記録していない。"
    - id: R-011
      verdict: covered
      evidence: "candidate/bom-diff.patch: 32-mbom.yaml @@ -493,22 +491,25 M-RELINK-025"
    - id: R-012
      verdict: partial
      evidence: "candidate/bom-diff.patch: 32-mbom.yaml @@ -493,22 +491,25 M-RELINK-025"
      note: "IRelinkService、選別、単発・batch API は写像したが、AutoRepairPair、RelinkSelection、IsHighConfidence の明示が欠ける。"
    - id: R-013
      verdict: covered
      evidence: "candidate/bom-diff.patch: 32-mbom.yaml @@ -493,22 +491,25 M-RELINK-025.interface_contract"
    - id: R-014
      verdict: covered
      evidence: "candidate/bom-diff.patch: 32-mbom.yaml @@ -734,23 +738,26 M-UI-INTEGRITY-055.artifact.path_note"
    - id: R-015
      verdict: covered
      evidence: "candidate/bom-diff.patch: M-UI-INTEGRITY-055.interface_contract を変更する hunk なし; bom/32-mbom.yaml:756-759"
    - id: R-016
      verdict: covered
      evidence: "candidate/bom-diff.patch: M-DB-007 の対象 hunk なし; bom/32-mbom.yaml:127"
    - id: R-017
      verdict: missing
      evidence: "candidate/bom-diff.patch: M-DB-007.interface_contract の編集 hunk なし"
    - id: R-018
      verdict: covered
      evidence: "candidate/bom-diff.patch: 32-mbom.yaml @@ -1,6 +1,6; 既存 M-DB-007/M-SCAN-005 写像は未破壊"
    - id: R-019
      verdict: covered
      evidence: "candidate/bom-diff.patch: 32-mbom.yaml @@ -91,10 +91,10 および @@ -102,8 +102,6; judge/scan_stage 契約は不変"
    - id: R-020
      verdict: covered
      evidence: "candidate/bom-diff.patch: 32-mbom.yaml @@ -91,10 +91,10 および @@ -734,23 +738,26"
    - id: R-021
      verdict: covered
      evidence: "candidate/bom-diff.patch: 32-mbom.yaml @@ -734,23 +738,26 M-UI-INTEGRITY-055.artifact.path_note"
    - id: R-022
      verdict: covered
      evidence: "candidate/bom-diff.patch: 対象 entry 契約を変更する hunk なし; bom/32-mbom.yaml:753-756"
    - id: R-023
      verdict: covered
      evidence: "candidate/bom-diff.patch: M-I18N-011/M-UI-INTEGRITY-055 i18n 写像の変更 hunk なし"
    - id: R-024
      verdict: covered
      evidence: "candidate/bom-diff.patch: 33-control-plan.yaml @@ -802,7 +802,7 CP-INTEGRITY-036"
    - id: R-025
      verdict: covered
      evidence: "candidate/bom-diff.patch: 33-control-plan.yaml @@ -802,7 +802,7 CP-INTEGRITY-036.fixture"
    - id: R-026
      verdict: partial
      evidence: "candidate/bom-diff.patch: 32-mbom.yaml @@ -493,22 +491,25 M-RELINK-025.acceptance_refs"
      note: "M-RELINK-025 には追加したが、M-DB-007 と M-SCAN-005 の acceptance_refs は未編集。"
    - id: R-027
      verdict: covered
      evidence: "candidate/bom-diff.patch: 33-control-plan.yaml @@ -783,8 +783,8 および @@ -834,11 +834,11"
    - id: R-028
      verdict: covered
      evidence: "candidate/bom-diff.patch: CP-INTEGRITY-036.test_vectors の変更 hunk なし; bom/33-control-plan.yaml:820-822"
    - id: R-029
      verdict: covered
      evidence: "candidate/bom-diff.patch: 30-ebom.yaml @@ -761,7 +762,7 E-UI-REPAIR-039"
    - id: R-030
      verdict: covered
      evidence: "candidate/bom-diff.patch: ConfirmDialog 契約の変更 hunk なし; bom/32-mbom.yaml:758"
    - id: R-031
      verdict: covered
      evidence: "candidate/bom-diff.patch: 33-control-plan.yaml @@ -802,7 +802,7; 既存 CP 品目の新規設計品目化なし"
    - id: R-032
      verdict: partial
      evidence: "candidate/bom-diff.patch: 32-mbom.yaml @@ -493,22 +491,25 および @@ -531,12 +532,15"
      note: "RepairWindow/ViewModel と CountAutoRepairableAsync の退役は記録したが、App.axaml RepairIcon と旧 Pending 専用資産・visual tests の撤去を対象 artifact へ十分に束ねていない。"

  candidate_edits:
    - hunk: "30-ebom.yaml @@ -320,7 +320,7 E-CRITERIA-037.graph_edges.consumers"
      verdict: grounded
      checked: ["eco/ECO-140.md:86-90", "eco/ECO-140.diff:133-153"]
    - hunk: "30-ebom.yaml @@ -394,7 +394,7 E-PACKAGE-047.invariants"
      verdict: grounded
      checked: ["eco/ECO-140.md:78-90", "eco/ECO-140.md:119-123"]
    - hunk: "30-ebom.yaml @@ -510,7 +510,7 E-UI-MODE-041.depends_on"
      verdict: grounded
      checked: ["eco/ECO-140.md:105-108", "eco/ECO-140.md:116-124"]
    - hunk: "30-ebom.yaml @@ -518,9 +518,10 E-UI-MODE-041.invariants"
      verdict: grounded
      checked: ["eco/ECO-140.md:119-124", "eco/ECO-140.md:317-323", "eco/ECO-140.diff:2845-2852"]
    - hunk: "30-ebom.yaml @@ -711,7 +712,7 E-DESIGN-028.graph_edges.consumers"
      verdict: grounded
      checked: ["eco/ECO-140.md:100-108", "eco/ECO-140.md:119-124", "eco/ECO-140.diff:133-153"]
    - hunk: "30-ebom.yaml @@ -761,7 +762,7 E-UI-REPAIR-039.external_source_note"
      verdict: grounded
      checked: ["eco/ECO-140.md:78-96", "eco/ECO-140.diff:113-118"]
    - hunk: "30-ebom.yaml @@ -805,7 +806,7 E-UI-PENDING-049.external_source_note"
      verdict: grounded
      checked: ["eco/ECO-140.md:78-96", "eco/ECO-140.diff:121-128"]
    - hunk: "32-mbom.yaml @@ -1,6 +1,6 bomdd.experiment"
      verdict: grounded
      checked: ["eco/ECO-140.md:173-187", "eco/ECO-140.diff:3995-4025"]
    - hunk: "32-mbom.yaml @@ -91,10 +91,10 M-SCAN-005.ebom_refs/artifact.path_note"
      verdict: grounded
      checked: ["eco/ECO-140.md:132-154", "code/src/ViewPrism2.Infrastructure/Scanning/RelinkService.cs:17", "code/src/ViewPrism2.Infrastructure/Scanning/IntegrityReviewFileHashProvider.cs:10-24"]
    - hunk: "32-mbom.yaml @@ -102,8 +102,6 M-SCAN-005.interface_contract relink API removal"
      verdict: grounded
      checked: ["eco/ECO-140.md:88-90", "code/src/ViewPrism2.Core/Services/Repair/IntegrityReviewService.cs:80-108", "code/src/ViewPrism2.Infrastructure/Scanning/RelinkService.cs:136-385"]
    - hunk: "32-mbom.yaml @@ -493,22 +491,25 M-RELINK-025"
      verdict: grounded
      checked: ["eco/ECO-140.md:132-160", "eco/ECO-140.md:238-240", "code/src/ViewPrism2.Core/Services/Repair/IntegrityReviewService.cs:60-108", "code/src/ViewPrism2.Infrastructure/Scanning/RelinkService.cs:38-385"]
      note: "記載内容自体は実在するが、参照表 R-012 が求める全型・全メソッドの列挙には届かない。"
    - hunk: "32-mbom.yaml @@ -531,12 +532,15 M-UI-REPAIR-027 supersession/path/contract"
      verdict: grounded
      checked: ["eco/ECO-140.md:78-90", "eco/ECO-140.md:232-240", "eco/ECO-140.diff:2362-2804", "eco/ECO-140.diff:3822-3994", "code/src/ViewPrism2.App/Views/RepairWindow.axaml: absent"]
    - hunk: "32-mbom.yaml @@ -531,12 +532,15 M-UI-REPAIR-027.ebom_refs=[]"
      verdict: ungrounded
      checked: ["eco/ECO-140.diff:113-118", "bom/30-ebom.yaml:758-781", "bom/32-mbom.yaml:533-552"]
      note: "根拠は E-UI-REPAIR-039 を superseded 履歴として残しており、歴史 M-BOM から参照を消すことまでは支持しない。"
    - hunk: "32-mbom.yaml @@ -734,23 +738,26 M-UI-PENDING-054 supersession/path/acceptance"
      verdict: grounded
      checked: ["eco/ECO-140.md:78-90", "eco/ECO-140.diff:1774-2267", "eco/ECO-140.diff:3484-3808", "code/src/ViewPrism2.App/Views/PendingReviewWindow.axaml: absent"]
    - hunk: "32-mbom.yaml @@ -734,23 +738,26 M-UI-PENDING-054.ebom_refs=[]"
      verdict: contradicting
      checked: ["REFERENCE.yaml:R-009", "eco/ECO-140.diff:121-128", "bom/30-ebom.yaml:802-839", "bom/32-mbom.yaml:735-747"]
      note: "E-UI-PENDING-049 と旧 artifact の履歴写像を保持する要求に反する。"
    - hunk: "32-mbom.yaml @@ -734,23 +738,26 M-UI-INTEGRITY-055.artifact.path_note"
      verdict: grounded
      checked: ["eco/ECO-140.md:132-160", "code/src/ViewPrism2.App/Services/WindowService.cs:156-179", "code/src/ViewPrism2.App/ViewModels/ImageTabViewModel.cs:1991-2002", "code/src/ViewPrism2.Core/Services/Repair/IntegrityReviewService.cs:231-378"]
    - hunk: "33-control-plan.yaml @@ -128,7 +128,7 CP-SCAN-004 ECO-139 vector transfer"
      verdict: grounded
      checked: ["eco/ECO-140.md:173-184", "eco/ECO-140.md:232-240", "code/tests/ViewPrism2.Tests/CpRelinkUnifiedBatchTests.cs:45-244"]
    - hunk: "33-control-plan.yaml @@ -783,8 +783,8 CP-PENDING-AUTO-035.fixture"
      verdict: grounded
      checked: ["eco/ECO-140.md:232-248", "eco/ECO-140.diff:6714-7391", "eco/ECO-140.diff:8939-9299", "code/tests/ViewPrism2.Tests/GfPendingReviewVisualParityTests.cs: absent"]
    - hunk: "33-control-plan.yaml @@ -802,7 +802,7 CP-INTEGRITY-036.fixture"
      verdict: grounded
      checked: ["eco/ECO-140.diff:7424-7727", "code/tests/ViewPrism2.Tests/CpRelinkUnifiedBatchTests.cs:14-244"]
    - hunk: "33-control-plan.yaml @@ -834,11 +834,11 CP-REPAIR-AUTOALL-023.fixture"
      verdict: grounded
      checked: ["eco/ECO-140.md:232-240", "eco/ECO-140.diff:7896-8455", "code/tests/ViewPrism2.Tests/CpUiRepairViewModelTests.cs: absent"]
    - hunk: "33-control-plan.yaml @@ -834,11 +834,11 CP-REPAIR-CARD-021.fixture"
      verdict: grounded
      checked: ["eco/ECO-140.md:232-240", "eco/ECO-140.diff:7896-8455", "code/tests/ViewPrism2.Tests/CpUiRepairViewModelTests.cs: absent"]

  totals:
    covered: 26
    partial: 4
    missing: 1
    broken: 1
    grounded: 19
    ungrounded: 1
    contradicting: 1
    design_overwrite: 0