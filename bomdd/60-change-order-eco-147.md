# ECO-147 — Default dispositions for limits and for self-review findings: R8 stops after 2 reviews, sorts findings into substance, evidence and record, and shows what is left at gate 2 as known defects (method: BomDD ECO-108, distributed by BomDD ECO-109) (staged)

- Type: process (change-management R4, R5, R8 and scenario 3.3; the eco-fix and eco-accept skills). No change to src, tests, BOM files (10/20/30/31/32/33) or fixed oracles.
- status: **staged** (2026-10-13).
- baseline: main `d8735ff`
- Source: method repository BomDD ECO-108 (verified 2026-10-13) and its distribution ECO-109. The maintainer's manufacture ruling was "A" (2026-10-13): both products now, ViewPrism2 first.
- Records are English-primary. The rule text stays in Japanese, like the text around it.
- Priority: medium (no product behaviour changes).

## §1 Request

Give the self-review (R8) and the ECO limit a default disposition, so that review findings end in a known number of rounds instead of "until in-scope findings are 0":
- **Limit default.** Reaching a limit set in the ECO does not stop the work to ask for an extension. The confirmed part is presented as a candidate for acceptance, the rest is recorded as known defects and known inaccuracies, the claim is narrowed, and this is reported at the next stop. converge's own round limit stays a ruling point.
- **R8 layers.** In-scope findings are sorted into three layers:
  - substance → R5 repair;
  - evidence → accepted if readable, otherwise `UNKNOWN`;
  - record → a known inaccuracy, with two exceptions: an overstated claim is narrowed, and a record that turns a machine check red is corrected.
- **R8 bound.** At most 2 reviews. Substance findings left at the last review are shown at gate 2 as known defects, with the claim narrowed. R7 copy errors are repaired, not carried. Departures from the default are decided by the human at the next stop.
- **eco-fix and eco-accept** follow: the stop lists what is carried, and accepted known defects are recorded and then take R3's two choices.

**Basis (BomDD ECO-108 §0).** BomDD playbook §8.5 holds these defaults, measured in ViewTube:
- under the default, 17% of record-layer findings went to correction (5 of 29); without it, 79% (49 of 62);
- an unbounded review ran 13 rounds (ViewTube ECO-VT-184).

ViewPrism2's R8 has no bound and no layers.

## §2 Process diagnosis

| Process | Verdict | Basis |
|---|---|---|
| CAD (ViewPrismUI) | not involved | no UI or visual change |
| Requirements, specification, E-BOM, K-BOM | sound; unchanged | the rule is about how review findings end |
| M-BOM, Control Plan | unchanged | — |
| **Process documents** | **R8 and the limit have no default disposition (this ECO)** | `bomdd/change-management.md:38` R4 limit, `:53-60` R8 ("未処置のスコープ内所見 0 になるまで golden に出さない"), `:108` scenario 3.3; `.claude/skills/eco-fix/SKILL.md:31-32` step 2 (extension ruling), `:58-66` 3.7, `:76-86` stop; `.claude/skills/eco-accept/SKILL.md` steps 2 and 7 |
| Implementation (src) | unchanged | — |

## §3 Change (frozen at filing)

1. **`bomdd/change-management.md`:**
   - R4: the limit default.
   - R5: R8 findings' probes follow R8.
   - R8: the layers and the bound replace the "until 0" sentence.
   - Scenario 3.3 (`:108`): the R8 step reads as the bound.
2. **`.claude/skills/eco-fix/SKILL.md`:**
   - step 1: the R8 probe exception;
   - step 2: the limit default replaces the extension ruling;
   - 3.7: follows R8;
   - the stop section: what is carried, and a refused known defect becomes a GF-*.
3. **`.claude/skills/eco-accept/SKILL.md`:**
   - step 2: accepted known defects go into the approval record;
   - a new step 3.5: their R3 disposition after acceptance;
   - step 7: they are re-listed with their destination.

**ViewPrism2's own points:**
- The Codex second opinion that R8 allows is counted inside the round it joins; it does not add rounds.
- R7 keeps "転写漏れ 0 になるまで golden に出さない"; R8's exception is how the two meet.

**Not changed:** R3, R6, R7, gate 1, gate 2, the stop after filing, the other skills, src, tests and BOM files.

## §4 Options

The text is BomDD ECO-108's, fitted to this repository: gate 2 naming, golden n/a's accept request, validate_bom as the machine check, and the Codex second opinion. The wording is the manufacturer's (R4).

## §5 Impact

- **BOM:** none.
- **Oracle:** existing fixed oracle rows unchanged (R6).
- **golden:** n/a (no visual or behaviour change).
- The closing conditions are mechanical acceptance (4 points), the grep in §6, and the independent review that BomDD ECO-109 V3 asks for.

## §6 Acceptance (frozen at filing)

- **V1**: condition = grep:
  - 0 → 1 for the new clauses:
    - R4 "上限に達したときの既定の処置";
    - R8 "**見直しは 2 回まで**" and "**記録**(それ以外";
    - eco-fix "R8 の層ごとの既定の処置" and "縮めた主張の範囲を明示する";
    - eco-accept "受け入れた既知の欠陥の処置(R8)";
  - 1 → 0 for "未処置のスコープ内所見 0 になるまで" in change-management.md and eco-fix.
- **V2**: condition = no hunks outside the places in §3; line endings unchanged per file; the frontmatter of eco-fix and eco-accept parses.
- **V3**: condition = mechanical acceptance, 4 points: build 0 errors, Tests all green, Oracle green (known skips), validate_bom 0-0.
- **V4**: condition = a fresh-context review of the diff attacks:
  - the meeting of R7 and R8;
  - the Codex second opinion;
  - scenario 3.3;
  - the golden n/a path.

  Its findings are disposed under the new R8 itself.
- **V5**: condition = BomDD EXP-20260828-03's window opens in this repository at the fix commit (recorded on the BomDD side).

## §7 Remaining gates

- gate 2: the maintainer accepts golden n/a (accept).
