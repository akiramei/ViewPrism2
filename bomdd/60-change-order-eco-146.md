# ECO-146 — Who decides derived-layer choices: gate 1 covers the ruling layer only; the AI decides, records and reports M-BOM, Control Plan and Routing choices (method: BomDD ECO-106, distributed by BomDD ECO-107) (implemented)

- Type: process (change-management R4, the eco-fix skill, the gate lines of AGENTS.md and CLAUDE.md). No change to src, tests, BOM files (10/20/30/31/32/33) or fixed oracles.
- status: **implemented** (filed 2026-10-13 `d20ad6d` → fix the same day; golden n/a waits for the maintainer).
- baseline: main `883cb08`
- Source: method repository BomDD ECO-106 (verified 2026-10-13) and its distribution ECO-107; the maintainer's manufacture ruling "A" of 2026-10-13: ViewPrism2 and ViewTube now, TimetableAdv later.
- Records are English-primary from 2026-10-12 (the maintainer's ruling under ViewTube ECO-VT-253). The rule text stays in Japanese, like the text around it.
- Priority: medium (no product behaviour changes).

## §1 Request

Make the human gates name the layer.
- gate 1 is a ruling on the **ruling layer**: CAD (ViewPrismUI), requirements 10, specification 20, E-BOM 30 and K-BOM 31.
- Choices in the **derived layer** are decided and recorded by the AI, and reported as a decision at the next stop: M-BOM 32, Control Plan 33, Routing 34, the fix option, the order and plan of manufacture and inspection, and how to repair its own output.
- So is R3's scope cut (record a separate problem in 51-cheat-log, or file it apart).
- The maintainer is asked only in these cases:
  - when the ruling layer would have to change;
  - for one of the three kinds that BomDD playbook §4.5 returns (a new feature, a new promise, something not derivable);
  - when a limit set in the ECO would be exceeded;
  - to mix a separate problem into the current ECO.

**Basis (BomDD ECO-106 §0).**
- Of 62 ruling stops measured in ViewPrism2's Claude sessions (2026-07-13 to 08-03), 21 could have been decided by the AI.
- The two derived-layer choices asked as gate 1 that the method repository measured came from here:
  - ECO-095: a fix option, "表示時解決" or "シード名の変更";
  - ECO-141: the scope of a maintenance update and the criterion for removing a workaround.
- Both followed R4's wording "gate①=裁定(方針・要求・候補の裁定 — 選択肢から選ぶ)", which names no layer.

## §2 Process diagnosis

| Process | Verdict | Basis |
|---|---|---|
| CAD (ViewPrismUI) | not involved | no UI or visual change |
| Requirements, specification, E-BOM, K-BOM | sound; unchanged | the rule is about who decides, not about the product |
| M-BOM, Control Plan | unchanged | — |
| **Process documents** | **the human gate names no layer (this ECO)** | `bomdd/change-management.md:29-33` R4, `AGENTS.md:37`, `CLAUDE.md:16-18`; `.claude/skills/eco-fix/SKILL.md:27` step 2 does not say who chooses |
| Implementation (src) | unchanged | — |

## §3 Change (frozen at filing)

1. `bomdd/change-management.md` R4:
   - qualify gate 1 as the ruling layer;
   - add the derived-layer sentence, R3's scope cut and the return cases (the text of BomDD ECO-106, with the paths of this repository: `../BomDD/method/bomdd-playbook-v1.md` §4.5).
2. `AGENTS.md:37` and `CLAUDE.md:16`: one qualifier each, pointing to R4.
3. `.claude/skills/eco-fix/SKILL.md` step 2:
   - the manufacturer chooses among the §4 options;
   - it writes the chosen and rejected options and the reason into the step-4 record, and reports the decision at the stop;
   - only in R4's return cases does it stop without touching code.

   The ECO-038 example and the ECO-080 cross-cutting check stay.

**Not changed:**
- the stop after filing and the explicit-command rule (`AGENTS.md:43-44`; maintainer ruling A in BomDD);
- golden (gate 2);
- R3, R6, R7 and R8;
- the other skills;
- src, tests and BOM files.

## §4 Options

One option; no fix options to choose among. The text is BomDD ECO-106's, fitted to this repository's R4 (gate 1 and gate 2 naming, ViewPrismUI as the CAD).

## §5 Impact

- **BOM:** none.
- **Oracle:** the existing fixed oracle rows are unchanged (R6).
- **golden:** n/a (no visual or behaviour change). The closing conditions are mechanical acceptance (4 points) and the grep in §6.

## §6 Acceptance (frozen at filing)

- **V1**: condition = grep, 0 → 1:
  - R4 has "導出層(M-BOM・Control Plan・Routing)の選択も AI の作業" and "R3 の二択から AI が選ぶ";
  - eco-fix has "是正の裁定(製造者が決める・R4)";
  - `AGENTS.md` and `CLAUDE.md` each have "change-management.md R4".
- **V2**: condition = no other hunks in the four files; the frontmatter of eco-fix parses.
- **V3**: condition = mechanical acceptance, 4 points: build 0 errors, Tests all green, Oracle green (known skips), validate_bom 0-0.
- **V4**: condition = BomDD EXP-20261013-01's window opens in this repository at the fix commit (recorded on the BomDD side).

## §7 Remaining gates

- gate 2: the maintainer accepts golden n/a (accept).

## §8 Record of the fix (2026-10-13)

**What was changed** (12 lines inserted, 4 changed; `git diff --stat`):
- `bomdd/change-management.md` R4: the gate 1 line now names the ruling layer, and 5 lines were added (derived-layer choices, R3's scope cut, the return cases);
- `.claude/skills/eco-fix/SKILL.md` step 2: the heading and the chooser changed, and 3 lines were added (record and report; the return cases);
- `AGENTS.md:37` and `CLAUDE.md:16`: one qualifier each.

**The fix option.** There was one option and nothing to choose (section 4). The text is BomDD ECO-106's, fitted to this R4: gate 1 and gate 2 are named, ViewPrismUI is the CAD, and the playbook path is `../BomDD`.

**V1: PASS.** Every phrase counted 0 at HEAD `d20ad6d` and 1 after the fix:
- R4: "導出層(M-BOM・Control Plan・Routing)の選択も AI の作業" and "R3 の二択から AI が選ぶ";
- eco-fix: "是正の裁定(製造者が決める・R4)";
- `AGENTS.md` and `CLAUDE.md`: "change-management.md R4", once each.

**V2: PASS.**
- **No other hunks.** R4 has 2, eco-fix 2, `AGENTS.md` 1 and `CLAUDE.md` 1.
- **Line endings are unchanged.** The four files stay CRLF throughout: CR count equal to line count.

**V3: PASS (mechanical acceptance, 4 points).**
- build: 0 warnings, 0 errors;
- Tests: 977 of 977 passed;
- Oracle: 109 passed, 4 skipped (known);
- validate_bom: 0 errors, 0 warnings.

**The CP-row table** (`cp_results.py`; results file sha256 `ffc95056b462`; total 977) is unchanged from ECO-145: violation 0, unmeasurable 0, not run (no inspection) 4, not run (human approval) 3, pass 57.

**R8.** Not applicable: documents only, src and tests untouched (eco-fix 3.7).

**V4.** The window of BomDD EXP-20261013-01 opens at this fix commit (recorded on the BomDD side).
