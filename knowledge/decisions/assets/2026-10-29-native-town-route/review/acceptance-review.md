# Native town route v2 — independent archived acceptance review

Date: 2026-10-29. Reviewer: active_drop_rules. Result: **no P1/P2 finding in the reviewed evidence**.

This review covers the four A/B cross-area N-map right-click cases, two same-area cases, A manual-interruption/takeover/protection cases, B's single-command DLRT API case, original screenshot witnesses, and the archived deployment/restoration chain. It does not certify every catalog area or every interruption permutation.

## Method and provenance

The reviewer read archived JSON/JSONL, raw input/API bytes, harness source and original screenshots; recomputed hashes; and ran a private read-only audit with **2,375 assertions, zero failures**. The audit uses pure SDK schema/response validators. It neither executes the input harnesses nor queries/controls processes, calls native movement, builds, deploys, or edits product source. Supplementary restoration checks and all six image comparisons are recorded in `acceptance-audit-results.json`.

The frozen running journal is `running-v2-deployment.json`, SHA256
`59C95948675A1AD0752D4CEB5BD3F7DAC14B15F3A6419D936DE031067D410017`.
The build-manifest hash is
`A34115D8AE0812ACD82AD57C2A4133AD4BEE570E956C837B84C66A3688D1F5D5`,
binding native Debug source `f6a9eb93cfff8678d3b99ad0fe7a5a92871b551f`,
Debug DLL `5E5FF3F218E738C9C4AAEFACBB496B800A44BD5D44BA88E45BB00AFFB401836B`,
and IJL DLL `0F2CB060C1DBAA09367C40E81B297951B5CC2883278B1B255EE4672258709225`.
Artifact hashes, evidence proof bindings, and the recorded loaded-module PID/path/hash entries agree.

A is PID 58960, process-start FILETIME 134377232355326353, UID 1, executable under `G:/Game/DNF_ACT1/DF`. B is PID 76432, process-start FILETIME 134377232363334670, UID 2, executable under `G:/Game/DNF_ACT1/Client_test2`. User/actor/context tokens remain pinned within each case. These are recorded process-lifetime identities; this reviewer did not enumerate current processes.

## Positive UI cases

All six files below are under `evidence-native-ui-v2/`, with corresponding `-input.json`, input diagnostics and `-route-updates.jsonl`.

| Case | Request | Native terminal steps | Elapsed ms | SDK captured after terminal, ms |
| --- | ---: | ---: | ---: | ---: |
| A-full-forward | 4 | 14 | 30,672 | 157 |
| A-full-reverse | 2 | 14 | 30,625 | 125 |
| B-full-forward | 2 | 14 | 32,454 | 359 |
| B-full-reverse | 3 | 14 | 30,422 | 282 |
| A-same-area | 3 | 1 | 2,922 | 187 |
| B-same-area | 1 | 1 | 2,531 | 390 |

Forward cases visit `1/1 → 1/0 → 2/1 → 2/0 → 2/2`, ending at requested `2/2 (1734,263)`. Reverse cases visit the opposite sequence and end at `1/1 (575,297)`. A same-area ends at `1/1 (351,278)`; B same-area ends at `1/1 (575,297)`.

For each case:

- The route JSONL matches the stored history row for row; request, origin, source `minimap`, goal, identity and PVF binding remain consistent. Tick/step progression is monotonic.
- Native terminal is `arrived/final_position_observed`. A later, complete, supported Town SDK sample has the same lifetime identity, expected area, coordinates within the declared 3px arrival tolerance and idle directions.
- The input payload contains exactly one `rclick` at t=500ms, duration 180ms. Its recomputed plan hash matches the receipt, the receipt reports success/own-plan-armed, and exactly one own-PID one-step plan arm appears in the input diagnostic.
- Independent inverse projection through the recorded N-map cache maps each right-click coordinate to the terminal goal.
- Area/map transitions and each exporter's generation progression agree. Native route and SDK generation counters come from separate capture histories and are not assumed numerically identical.
- The SDK loader's reviewed execution path enforces 750ms freshness when sampling. Archived captured/export times and terminal ordering were checked; archive age is not treated as a current live freshness test.

Progress exports coalesce intermediate states: not every internal step ID is present. Thus the evidence supports the terminal counter of 14 and the observed five-area physical journey; it is not fourteen separate command receipts.

`goaltest_v3.py` launches one native right-click through `control.py`, then observes native route and SDK exports. It does not dispatch waypoints. `h.py` contains an unused legacy `move` helper calling the debug API; that helper is not called on these UI acceptance paths. The active call chain writes the IJL input plan and validates its PID/arm/completion receipts; it uses no OS input, packet injection or process-memory writes. Native PrintScreen is used for the screenshot witnesses.

The earlier `A-cross-forward.json` (map not open/no request) and `A-first-right-route-diagnostic.json` (observer PermissionError/incomplete history) remain diagnostic records and are excluded from the six successful cases.

## Manual, takeover and protection

`A-native-manual-interruption.json` establishes an active request 5. NPC right-click preserves that goal. After N-map close, a later native tick still reports moving; a native Left key held 220ms then yields `interrupted/native_auto_move_stopped`. Position is unchanged and directions remain idle over the subsequent 2,250ms, with unchanged route bytes.

`A-valid-target-takeover.json` establishes active request 7, then valid new request 8. Request 8 arrives at its new goal; after 2,100ms, position and route bytes remain stable, without continuation of request 7. Natural-arrival `auto=true` does not contradict idle directions or the terminal arrival.

The following archived protections meet their asserted scope:

- `A-control-right-no-goal.json`
- `A-outside-projection-no-goal.json`
- `A-region-menu-right-no-goal.json`
- `A-npc-menu-right-no-goal.json`
- `A-hidden-map-right-no-route.json`
- `A-reopened-map-no-replay.json`

Route objects remain unchanged and the producer's exact-byte checks are recorded. The two menu-state records include native visible=true witnesses. The hidden-map case proves no new **map route**, while preserving original ground-click behavior; it does not claim no native world movement. Reopening the map produces no replay. `manual_native.py` and `protection_native.py` use IJL inputs and observation on these paths.

These interruption/protection cases were exercised on A; this report does not extend them to unrecorded B permutations.

## B single-command API case

`B-v2-native-api-cross.json` records **26,843ms / 14 steps** to `2/2 (1200,280)`. The copied `B-v2-api-*` raw request is exactly 96 bytes with DLRT magic and request ID `1793250289134858600`, bound to B's PID/start/UID/user/actor/context and initial area. SHA256:
`33B4C6EB341B3C645DBA5519A54FE0C0B8ACDCD3A7D5E7A85E1B8E149963AF18`.

The per-request result equals the recorded native terminal, source `debug`, status `arrived`. The pure consumer response validator accepts it with all identity/request/goal/time guards. A fresh SDK capture is 31ms after terminal at `(1200.17053,280.26474)`, idle. The archive records one final command and no Python waypoint controller; the previously audited consumer execution writes once and never resends. This API run is separate from the four minimap UI cross-area cases.

## Screenshot and restoration checks

All six native JPG witnesses were visually inspected and hash-matched against their original client `ScreenShot` sources and corresponding input records: A/B Hendon exterior, A/B Seria room, and A/B open N-map. PID/path and SDK-area associations agree. The images are context witnesses; precise final arrival is established by Native terminal plus the later SDK sample.

The stopped/closed journal chain agrees with `closeout-restoration.json`. Independent file rehashing verified:

- 2 normal configs and 8 restored input/interface states, including intended absent files.
- 4 Debug targets at accepted 5E5 hash and 4 IJL targets at 0F2 hash.
- 15 protected inputs and their original backup bindings.
- 50 normal files, all 146 files from the previous W frozen manifest, and 6 archived closeout files.

The closed deployment hash is `0EF9D5D5F94115E7BE64720F5F7B9E94B8D4475DFD72327CEB1F45C0FA9C82EE`;
the closed manifest hash is `F2965243B6214920CA9D9A5D9D2F16E758898016A2196961BDE81CEDEB8106D1`.
`NoNativeProcesses=true` is the owner's recorded closeout assertion, not a new process enumeration by this reviewer.

## Review artifacts

- `acceptance-audit.py`, `acceptance-audit.log`, `acceptance-audit-results.json`: independent archived audit and supplemental hash results.
- `consumer-review.md`: late-terminal age exemption, retained identity/goal/time/SDK guards and no-resend source/test review.
- `hit-review.md`: right-release ingress, ABI/original behavior, rollback/unload ownership and bounded export-test review.

Within the stated cases and evidence chain, no blocking source/evidence discrepancy remains. Coverage does not include every one of the 29 catalog areas, all menu/manual-event permutations, or U04 active-discard live acceptance.

