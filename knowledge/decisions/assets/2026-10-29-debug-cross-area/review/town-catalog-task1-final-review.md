# Task1 final independent review — c9d5d002

**Verdict: no remaining Critical/Important source finding. Ready for integration and the separately planned live acceptance.** This is a source/offline-test verdict, not a declaration that cross-area client acceptance has passed.

Reviewed source `c9d5d002b73b99f58250afbb322ecd67a399e95a`, following original Task1 `5aa3cc2f8dd69af95e277ddb184bf932e24e3e48`, base `413c556598e64227a75b64353054124eaf36002f`. Workspace `D:/codex_worktree/debug-town-navigation/DFLegacy` was clean before and after independent checks. Scope remained only `tools/debug/town_navigation.py`, tests and read-only comparisons with the existing C# PVF reader/original PE. No product/index/HEAD edit, live client/server action, process access, deployment, or subagent was performed.

## Resolution of original P2 findings

1. **Native half-open membership — resolved.** `town_navigation.py:59..60` now uses `< right`, `< bottom`. `_box_covered` at line 110 separately checks every x boundary column and intervening slab, and its merged half-open y intervals must extend **strictly beyond** the closed box top (line 134). This correctly rejects the right/bottom endpoint and degenerate-line counterexamples; no epsilon makes an excluded edge legal.
2. **Corner-only shortcut — resolved.** Both the initial direct leg (line 188) and route simplification (line 218) require full closed AABB coverage. Two rectangles that meet only at a corner cannot satisfy that test. `_bridge` at line 95 rejects such corners and generates ordered, integer points on both sides of a legal shared edge; reconstruction preserves their direction. Full and partial horizontal/vertical shared-edge cases pass in both directions.
3. **Arbitrary straight-line/native trajectory mismatch — resolved.** Simplification now proves the whole start/end AABB is in the half-open union, covering any monotone per-axis native trajectory rather than only one arbitrary-slope line. The former single-target trap now returns `((95,30),(190,30),(310,60))`, with each leg within a proven covered box and reaching its endpoint under the original 186:122 integer-pixel steering model.

The earlier source findings and their exact coordinates remain preserved in `town-catalog-task1-review.md`; implementation RED evidence `.tmp/town-navigation-validation/native-geometry-red.log` was read and confirms three failures before the fix.

## Independent verification

- `python -B -m unittest tools.debug.test_town_navigation -v`: **14/14 passed**, exit 0; includes installed real PVF regression and all three reproduced defects.
- `python -B -m unittest discover -s tools/debug -p 'test_*.py'`: **94/94 passed**.
- Independent Cartesian partition oracle, deterministic seed **20261029**: **12,000 random boxes**, including negative coordinates, half-integer starts, degenerate horizontal/vertical/same-point boxes, and rectangle boundaries; **0 differences** versus `_box_covered`. The oracle partitions both x and y at every boundary and tests both boundary values and all intervening slab midpoints, instead of reproducing the production y-interval merge.
- Random planner check: **2,078 returned routes, 2,565 legs**. Every leg's full closed AABB passed that independent oracle; every leg reached its target under the previously extracted native axis-steering model.
- Original reproductions now produce `outside_walkable`, `no_path`, and the three-turn-point route above, respectively.
- Original archive compatibility remains covered: installed PVF SHA `288d58aace8ed4724a0a94aaa4cd0957b1d52c633771132ede2f96363c1b506f`, 29 areas/68 portals. XOR then ROR6 decoding/padding matches the C# reader; stricter duplicate/path/bounds rejection is conservative. Unknown areas and dungeon-gate intermediate exclusion remain unchanged.

## Non-blocking suggestion retained

`_block` now at line 292 still compares tag counts without a global token-order check, so a stray closer before a valid block plus a stray opener after it can be accepted. No effect on the installed PVF was demonstrated, and it does not reopen the three movement findings. It is an optional malformed-catalog diagnostics improvement.

## Limits

The AABB criterion is deliberately sufficient and may retain more turns than an optimal route. Offline integer steering models validate the planning assumption, not every original runtime mode, frame schedule, transport or real area-transition lifecycle. Task2 executor locking/transition handling and actual cross-area client acceptance were not reviewed in this Task1 verdict. Newly generated one-pixel bridge points must remain semantically meaningful to the executor; arrival tolerance must not be used to discard necessary waypoints without the same safety proof.

Hashes, original counterexamples and final check counts are bound in `town-catalog-task1-review-evidence.json`. Original native instruction bytes remain in `native-town-movement-disassembly.txt`, with original PE/source hashes in `native-town-navigation-evidence.json`.
