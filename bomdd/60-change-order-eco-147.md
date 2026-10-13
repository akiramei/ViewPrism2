# ECO-147 — Default dispositions for limits and for self-review findings: R8 stops after 2 reviews, sorts findings into substance, evidence and record, and shows what is left at gate 2 as known defects (method: BomDD ECO-108, distributed by BomDD ECO-109) (applied)

- Type: process (change-management R4, R5, R8 and scenario 3.3; the eco-fix and eco-accept skills). No change to src, tests, BOM files (10/20/30/31/32/33) or fixed oracles.
- status: **applied** (2026-10-13: the maintainer accepted golden n/a with known defects N1 and N2, answer "A". Filed `f697a82`, fix `b63897a`).
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
- in final rounds under the default, 17% of record-layer findings went to correction (5 of 29); in the other rounds, 79% (49 of 62);
- an unbounded review ran 13 rounds (ViewTube ECO-VT-184).

ViewPrism2's R8 has no bound and no layers.

## §2 Process diagnosis

| Process | Verdict | Basis |
|---|---|---|
| CAD (ViewPrismUI) | not involved | no UI or visual change |
| Requirements, specification, E-BOM, K-BOM | sound; unchanged | the rule is about how review findings end |
| M-BOM, Control Plan | unchanged | — |
| **Process documents** | **R8 and the limit have no default disposition (this ECO)** | `bomdd/change-management.md:38` R4 limit, `:53-60` R8 ("未処置のスコープ内所見 0 になるまで golden に出さない"), `:108` scenario 3.3; `.claude/skills/eco-fix/SKILL.md:31-32` step 2 (extension ruling), `:58-66` 3.7, `:78-87` stop; `.claude/skills/eco-accept/SKILL.md` steps 2 and 7 |
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

## §8 Record of the fix (2026-10-13)

**What was changed** (`git diff --stat` against `f697a82`):
- `bomdd/change-management.md`:
  - R4: the limit default, and that R7 wins when a copy error remains;
  - R5: R8 findings' probes follow R8;
  - R8: layers, the 2-review bound, the Codex second-opinion rule, the R7 exception and its return path, departures, and the restart;
  - scenario 3.3.
- `.claude/skills/eco-fix/SKILL.md`:
  - the frontmatter description;
  - step 1 (the probe exception);
  - step 2 (the limit default);
  - 3.5 (the R7-exception return path);
  - 3.7 (layers and bound, each round's diff recorded);
  - the stop section (what is carried; a refused known defect becomes a GF-*).
- `.claude/skills/eco-accept/SKILL.md`:
  - the precondition (accepted known defects are not findings);
  - step 2 (accepted known defects recorded, including on the n/a path);
  - a new step 3.5 (cheat-log in the accept commit; a separate filing after step 5);
  - step 7 (destinations).

**Departures from §3, recorded for V2.**
- The eco-fix frontmatter description was not among §3's places. Review round 1 found it still stating the old rule (VP147-F1, blocking), and it was rewritten.
- eco-fix 3.5 and the eco-accept precondition were added by the review's repairs (F6, F8).
- The order file itself changed for F9.

**The fix option.** Choices were made by the manufacturer (R4), as follows:
- **The text.** BomDD ECO-108's text is fitted to this repository: gate 2 naming, golden n/a's accept request, validate_bom, and the Codex second opinion.
- **Equipment independence.** ViewPrism2's R8 has no equipment-independence tier, so the template's "equipment-independent inspection carries the 2" became "Codex is part of its round and adds no round" (F2).
- **Not carried over:**
  - the stop word `NONE` (ViewPrism2 has no operator-layer vocabulary);
  - the last-round REJECT. A Codex finding takes the same layers, so a last-round REJECT ends among the known defects.

**V1: PASS** (`vp147_count.py`, f697a82 against the tree):
- **New phrases, 0 → 1:**
  - R4: "上限に達したときの既定の処置";
  - R8: "**見直しは 2 回まで**" and "**記録**(それ以外";
  - eco-fix: "R8 の層ごとの既定の処置" and "縮めた主張の範囲を明示する";
  - eco-accept: "受け入れた既知の欠陥の処置(R8)".
- **Old phrases, 1 → 0:**
  - "未処置のスコープ内所見 0 になるまで" (change-management and eco-fix);
  - "上限の超過は延長の裁定を求める" (eco-fix).
- **Widened by the review (F1):** "スコープ内所見 0" now appears nowhere outside orders, the register and the excluded `.claude/worktrees`.

**V2: PASS.**
- Hunks are at §3's places plus the departures above.
- Line endings are unchanged: change-management.md is CRLF throughout (203 of 203 lines), and the two skills and the order are LF.
- The frontmatter of eco-fix and eco-accept parses.

**V3: PASS (mechanical acceptance, 4 points, on the final text).** build 0 warnings 0 errors; Tests 977 of 977; Oracle 109 passed, 4 skipped (known); validate_bom 0-0. The CP-row table is unchanged from ECO-146: violation 0, unmeasurable 0, not run (no inspection) 4, not run (human approval) 3, pass 57. The same 4 points were also green before the review's text repairs.

**V4: independent review.** A fresh-context subagent of the same model family was used, which is context independence. The record: this section.
- **Round 1: FAIL.**
  - **F1, blocking.** The eco-fix frontmatter still stated "所見 0".
  - **F2 to F6, material:**
    - F2: the template's equipment tier, REJECT and `NONE` were dropped without a record;
    - F3: Codex's round was open to choice;
    - F4: eco-accept 3.5 filed separately before the accept commit;
    - F5: the R4 limit default against R7;
    - F6: no return path for the R7-exception repair.
  - **F7 to F9, minor:** the golden n/a brackets; destinations and the n/a slot; the order's facts.
  - All nine were repaired.
- **Round 2 (the last under the new R8): PASS.** 9 of 9 were resolved. There were two new minor findings, and under the new R8 they are **not repaired: they are carried as known defects**:
  - **VP147-N1.** A Codex result that arrives after its round has closed (repair started) has no stated round.
  - **VP147-N2.**
    - Wording: R4's "上限到達の報告で止まる" (an R7 copy error left at the ECO's limit) reads against R8's "上限到達で作業は止まらない", which is the review count.
    - Status: neither says whether a green diff with an R7 copy error gets a fix commit and the register note "golden 待ち".

  **The claim is narrowed accordingly:** this ECO does not claim to settle a late Codex result or the register state of a limit stop with a copy error left. Both are shown at the accept request. Under eco-accept 3.5, if the maintainer accepts them, they take R3's two choices after acceptance.

**Template defects found here (R3).** F4, F5, F6 and F8 are also present in the methodology template (BomDD ECO-108); N2's status gap is too. They are reported to the methodology repository, not repaired there from this ECO.

**V5.** BomDD EXP-20260828-03's window opens at this fix commit (recorded on the BomDD side).

## §9 Closure (2026-10-13, applied)

**gate 2.** The maintainer accepted golden n/a with the answer "A" (2026-10-13). The question named two things:
- the two known defects, VP147-N1 and VP147-N2;
- the next step: repair the methodology template first (BomDD ECO-110), then distribute to ViewTube.

The acceptance rests on:
- the mechanical acceptance (4 points, §8);
- the grep (V1, V2);
- the independent review (round 2 PASS).

**Accepted known defects (eco-accept 3.5, R3).** N1 and N2 are recorded in `bomdd/51-cheat-log.md` in this accept commit. Their repair goes to BomDD ECO-110 and a later distribution.

**CP characteristic.** None: a process-document change with no product behaviour.

**Lesson.** A grep for the old rule's exact sentence missed the same rule in a shorter form, in the skill's frontmatter. The frontmatter is the text shown in the skill list, which is read before the body. When a rule is replaced, search for its idea in short forms ("所見 0"), not only for its sentence. Also, applying a new rule to its own ECO tests the rule: the 2-review bound ended this ECO's review with two minor findings carried, as designed.
- Read-across: BomDD ECO-108's template has the defects F4, F5, F6 and F8 found here (ECO-110).

