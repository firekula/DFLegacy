> Latest expiry-recovery delta: sourceCFE6B98B / test73056716 passed independent review with no remaining P1/P2 and45 focused route tests. See client-route-expiry-recovery-final-review.md; older hash tables and narrower reviews below are historical evidence.

> Latest narrow delta: inflight capture-gap source1D2AEC89 / testF2A64774 passed independent review with no new P1/P2 and38 focused route tests. See client-route-inflight-gap-review.md. Earlier core/grace review sections below remain historical evidence.

> Later narrow delta: post-transition grace source051B5A6B / test6AC0225E has also passed independent review with no new P1/P2. See client-route-post-transition-grace-review.md for its exact hashes and35-test gate. The core hash table below preserves the prior62b7848 review.

# Task2 final independent source review

**Verdict: no remaining P1/P2 source finding. All reproduced continuation, geometry-wiring, foreign-gate and birth-reentry issues are closed in the frozen source below.** This is an offline/source verdict; runtime and full town coverage remain separate acceptance work.

Reviewed root working files over tracked HEAD `62b78483ffecf81a4fb4ec95f89a88991f07a681`:

| File | SHA-256 of frozen working bytes |
|---|---|
| `tools/debug/client_route.py` | `B3DE0E766EBB2025FEC560C413DE9FF691777F2A9C500D07F5ECEA6126665E4B` |
| `tools/debug/test_client_route.py` | `96987E819917D36003ACCB5D1DFDAAE578E697B0447E073CBD846389D367D407` |
| `tools/debug/town_navigation.py` | `D6C10815E65D02B795E52D5984FAE2F0A14877F114EBEBD705DC9E7A9BB7877A` |

Task1 geometry is the reviewed `e2f8f3641dbf6626ddf5ca0f42fed896c8f7fdaf` implementation integrated by root as `db06ea7`. Task2 was reviewed as frozen uncommitted files; while this report was being finalized, root committed those same bytes as `62b78483ffecf81a4fb4ec95f89a88991f07a681` (working tree clean). No source/index/HEAD changes, client/server operation, process access, configuration change, deployment or additional agent was performed by the reviewer. Only requested derived review files were written under W/recon.

## Final delta and closure

- Every imminent native send replans from its fresh actual float origin, retaining the same `blocked_portals` constraints. Final requested coordinates remain unchanged. The command file, prior native receipt, destination, movement direction and observed stop state are rechecked before continuation.
- Same-area auto_move=false stops the operation even within the three-pixel arrival tolerance; unknown auto state also fails closed. A newly read rejected prior receipt cannot be ignored. Context-changed receipt waits and portal-at-point waits are both bounded to five seconds.
- An expected transition requires the expected area, a newer generation and a changed map. It may complete on any internal approach leg; only the selected portal's final point specifically requires a transition. Unrelated contexts fail closed.
- All unselected portals are passed into Task1's full-leg geometry restrictions on every fresh plan. Final user goals within a portal's shared four-pixel half-open Near region are rejected before travel. Birth portals are explicitly handled even when the next selected portal differs.
- The last reproduced P2, multi-leg escape returning through a birth portal, is resolved by requiring a **single native leg** in both candidate selection and fresh dispatch. `_monotonic_exit` checks both the raw and native nearest-half-away rounded origin: if already outside the original portal, that origin-to-target closed AABB must avoid the portal. If inside, the single axis-monotone exit may leave it only once. A completed escape must then be observed outside **all** Near regions.
- Near uses the geometry module's exported `PORTAL_BLOCK_MARGIN=4` and the same half-open expansion. Escape nominal candidates use five pixels on all four sides, covering the original half-pixel native idle band. The earlier right/bottom+4 case cannot leave a naturally rounded stop inside the padded blocked region.

## Independent checks on the final hashes

- `python -B -m unittest tools.debug.test_town_navigation tools.debug.test_client_route -q`: **52 tests passed**.
- `python -B -m unittest discover -s tools/debug -p 'test_*.py' -q`: **132 tests passed**. These are the reviewer's direct unittest counts; parent runs using another collection command have separate recorded counts.
- The strong raw-coordinate reentry fixture (birth `(30,30,40,40)`, start `(50,50)`, walkable `[(46,46,29,8),(67,46,8,28),(46,66,29,8),(0,62,54,8)]`) now conservatively returns `portal_rearm_unavailable` before native publication.
- Direct raw/rounded checks reject crossing back through a portal from its padded exterior. All four five-pixel nominal exit directions, each with nine half-pixel-band endpoint combinations, remain outside Near.

The previously completed Task1 review also independently checked 3,500 random rectangle-union-minus-expanded-portal cases against a Cartesian boundary/slab oracle: 1,495,546 membership points, zero differences. This was not repeated for this final Task2 delta; its geometry hash is unchanged.

## Limits retained

No runtime result was produced or certified by this reviewer. Parent-owned diagnostics and future A/B trips must establish actual endpoint/transition behavior. The proof/model covers the supported ordinary Town movement path; it is not a claim about every map, transport/provider mode or all town routes. Thin fragments without the chosen stopping clearance and birth exits requiring unsafe/multiple native legs may conservatively refuse a route. No claim of global route optimality or complete town coverage is made.

The original native integer rounding evidence is retained in `native-town-idle-rounding-review.md` and its bytes/hash files. The full review history and exact original counterexamples remain in `client-route-task2-review.md` / `client-route-task2-review-evidence.json`; this document supersedes their earlier pending verdicts for the frozen hashes above.
