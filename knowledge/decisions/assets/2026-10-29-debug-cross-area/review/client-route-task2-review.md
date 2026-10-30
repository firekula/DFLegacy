> Final source verdict: all reproduced P1/P2 findings are closed in client_route SHA B3DE0E76. See client-route-task2-final-review.md for exact hashes, direct test counts and retained runtime limitations. The earlier sections below preserve historical review evidence.

# Task2 initial independent review — uncommitted files over 9c46b915

Scope: `D:/codex_worktree/party-chat-gold/DFLegacy/tools/debug/client_route.py` and `test_client_route.py`. Initial reviewed hashes are bound in `client-route-task2-review-evidence.json`. Read-only source review and offline Python tests only; no edits to product/index/HEAD, live client/server operation, process access, deployment or new agents.

**Initial verdict: three reproduced P2 issues need correction before integration/live route execution.** Parent has accepted all three findings and is adding RED/GREEN fixes. This report preserves the original uncommitted implementation, not a later revision's verdict.

## P2: Continuation can overwrite a manual destination set between internal legs

`client_route.py:188..196` recaptures state before sending the next request, but only checks area/generation/map. Ownership/destination/auto-move checks occur inside the previous pending-leg loop (`203..242`) and do not protect the boundary after it returns. Reproduction using the existing test `Transport`: a same-area catalog returns `((50,20),(120,35))`; first request reaches `(50,20)`, then the next fresh capture is x=55/y=20, `destination=(75,99)`, moving=true, with unchanged identity/context. Current code sends `(120,35)`, overwrites the user's new destination, and reports `ok=True`. Observed sends: `[(0,50,20),(0,120,35)]`.

Before each continuation send, recheck the prior command ownership and native result source plus observed destination/direction/auto-move changes. In particular, a manual stop within the arrival tolerance must not become authority to restart another internal leg. Natural portal reset must be handled as its separate proven-transition case.

## P2: The last fresh position is not the origin used for the Task1 safety proof

`client_route.py:266..275` computes the full point list from one snapshot. `leg()` captures again at line 191, but sends the old planned target without revalidating/replanning from that actual origin. This is already observable on the first leg, independent of between-leg ownership.

Use the actual Task1 Catalog with rectangles `[(0,0,100,100),(90,20,110,20),(180,20,220,80)]`, final goal `(310,60)`. Initial capture `(190,60)` safely plans a direct leg. Make the next fresh capture `(99,60)` (same UID/user/actor/context/map/area/generation). Current code sends `(310,60)` from `(99,60)` although `_box_covered(actual,target)` is false; original native steering stalls there and the route times out. Replanning from the fresh source would return `((95,30),(190,30),(310,60))`.

The fresh actual float position used immediately before send must drive or revalidate that leg. A prior integer waypoint or a position merely within the three-pixel arrival tolerance cannot substitute for the origin assumed by Task1's geometric proof.

## P2: A new expected area/generation with the old map is treated as a complete transition

`client_route.py:225..229` checks expected town/area and strictly increasing generation, but not that the map has changed. A structurally valid snapshot with area 1/gen 2 and old map token in both me/town passes and causes the next final-coordinate request to be sent. Reproduction uses the existing successful transport, replacing all new-area map tokens with the source map token: observed sends `[(0,90,20),(1,120,35)]`, `ok=True`.

This is material under the requested map/generation transition contract. The native state reader's `finish()` increments generation when area changes even if map is unchanged, and TownState only proves stable context/actor map references; it does not prove the map definition belongs to the reported area. Therefore a receipt bound to the old actor/map cannot alone establish that the expected new map has loaded. Parent chose conservative `map_transition_unproven` rejection. If actual pointer reuse is later observed, support should use that evidence rather than assuming it now.

## Passing checks and supported behavior

- Existing focused `python -B -m unittest tools.debug.test_client_route -v`: **17 passed**.
- Full `python -B -m unittest discover -s tools/debug -p 'test_*.py'`: **111 passed**. The existing success does not cover the above between-leg/fresh-origin/partial-transition boundaries.
- `_FileSession` holds the existing per-PID native lock for the entire `run_route`, rechecks process start before publication/reads, uses the unchanged 96-byte DLNV wire, exact binary command ownership, response bindings and a request issue-time floor.
- UID/user/actor/context remain pinned throughout; sequence/generation regressions, unrelated areas, rejection, non-context interruption, and missing native acceptance fail closed in the covered paths.
- Final town/area and goal coordinates are preflighted against the catalog before the first request. Client PVF resolution is based on the read-only process image directory, with optional supplied archive SHA equality; no manual waypoint list is exposed by the public CLI.

## Limits and follow-up

No live loading sequence or pointer reuse behavior was inferred from synthetic fixtures. The loading allowlist is intentionally narrow; whether actual area changes expose an `invalid_capture`, map-first, area-first or another transient shape remains for separate native acceptance. This report does not certify the Task2 controller after parent edits. Re-review will bind the revised source hashes, re-run the three regressions and relevant tests, and add a final verdict.

## Second review — source 6A5A4841 (21 focused tests pass)

The initial three fixes are present: fresh-origin single-leg planning, pre-send previous command/destination checks, and `map_transition_unproven` for an expected area retaining the old map. Two remaining continuations were independently reproduced:

- **P2, manual stop within arrival tolerance:** intermediate target `(50,20)`, observed `(48,20)`, same destination `(50,20)`, directions idle, auto_move=false. `leg()` declares the point reached before its auto-stop check; `reach()` then only checks a True-to-False edge, but the saved prior snapshot was already false. It sends `(120,35)` and reports success. A successful native request set auto_move=1; native arrival itself does not clear that flag. Explicit observed same-area stop must not be swallowed by the three-pixel tolerance.
- **P2, rejected prior receipt ignored:** the first receipt is requested and the next pre-send recheck returns same-bound `rejected/live_context_unavailable`; `reach()` only examines interrupted status, so still sends the following target and reports success. A negative newly observed native receipt must remain authoritative before continuation.

These are pending parent RED/GREEN fixes; this second revision was not approved as final. Exact inputs/results and source hashes are retained in the evidence JSON.

## Third review — source AA2FF920 (26 focused tests pass)

The remaining two P2s and context-changed wait bound are fixed: same-area accepted movement requires auto_move=true before arrival tolerance, prior rejected receipts stop continuation, and context_changed starts the five-second portal wait timer.

The new automatic rearm introduces a **P2 at lines 52..61 and 344**: only candidate endpoints are screened against other portals, while the path returned by `catalog.waypoints` and replanned by `reach` can cross one. With floor `(0,0,100,100)`, selected gate `(30,80,40,20)`, start `(50,90)`, selected point `(50,89)`, and foreign gate `(40,80,20,5)` to `(1,8)`, `_portal_rearm_points` returns `((50,76),)`. Every waypoint endpoint is outside the foreign gate, but the original vertical movement necessarily passes `(50,84)` inside it. Safe alternate `(26,90)` is reachable without entering the foreign gate. This reproduces even though the foreign gate does not contain the initial position; it is not the normal reverse-entry spawn case.

Foreign-gate restrictions must protect each full native leg and survive fresh replanning. Merely checking returned waypoints is insufficient. The same issue exists in principle for ordinary selected-gate approach or same-area final legs: an area graph excluding dungeon-gate intermediate nodes does not prove a physical leg avoids the unselected gate rectangles. Preserve an explicit initial-containing-gate exit case, because normal new-area spawn may be inside a reverse portal; do not accidentally forbid leaving it. No live failure/foreign transition was induced by the reviewer.

Third revision remains pending correction of the reproduced rearm issue; no final source approval was issued.
