# Offline review of right-pulse-v2 live evidence

Date: 2026-10-29 (Asia/Shanghai). Independent reviewer: map_feedback_review.
Mode: offline inspection only. No process/service queries, GUI control, live input/memory access, product change, old-evidence rewrite, build or test executable was performed. Existing evidence was read, selected original JPGs were viewed and hashes/assertions were checked offline.

## Limited acceptance verdict

**PASS for the recorded A/B scenarios: native left release does not submit a goal, valid right release routes, the new renderer produces visible yellow feedback, rapid replacement starts two finite runtime rings, expiry clears them, and close/reopen clears without replay. No new P1/P2 discrepancy was found in this evidence scope.**

This verdict is limited to the preserved022A deployment and tested points/scenarios. It is not a claim about every map position, all UI interactions, exact GPU frame timing, arbitrary concurrent unload, device-loss/reset or global performance. The first generic-hook F5 rendering attempt remains a genuine live RED. The initial v2 screenshot is a loading transition and is excluded from yellow-pixel proof. The first rapid-click fixture failure at245 is preserved and excluded from the successful two-valid-click claim; the corrected retry is separately identified below.

## Artifact, process identity and source binding

- deployment.json records phase right-pulse-v2, Status running, clientA pid8820/start2026-10-29T10:42:36.5015453Z andB pid8568/start2026-10-29T10:42:37.2443797Z. Parsing the original ISO strings gives exact Windows FILETIMEs134377441565015453 and134377441572443797. Recorded feedback/state identities in A/B left, rapid and close evidence match these pid/start pairs. This is a check of recorded identities, not an independent query that the processes are still running now.
- Actual SHA256 of build-manifest-right-pulse-v2.json is00F9C92D246464AE84DD398C0C24EB0185F4C5ACCAC9702799A26502923AF41E, matching deployment and scenario-proof references. Actual build debug.dll SHA256 is022A81AC3CE59B0BF4662B7712ECC0938FC3AA1665102BABF899C64D78ED6D42. All4 debug-v2-deployment target files independently hash to the same022A value. Scenario proofs consistently reference the bound manifest.
- Initial inspection found inherited SourceFiles incomplete for this change:7 new feedback/core/test files absent and8 modified tracked files retaining historical hashes. That original manifest remains unchanged; its SourceFiles/VerificationLogs are historical baseline, not the current debug source seal.
- The additive build-source-seal-v2.json (SHA25648BFC439EBA6B689263D56EA487837198D9A6510A857CE328429769491048C26) addresses this explicitly and binds to the original00F9 manifest,022A artifact andc39d28246c2e5e923f025926af305e6f66e9e80a commit. I recomputed all122 inventory file hashes: zero mismatches. All41 Debug paths have normalized git blobs equal to c39; all15 previously reviewed Debug paths have exact raw SHA256 equal to reviewed-hashes-specific-followup.json, including all7 added files. The author's3 source-specific renderer hashes also agree. Normalized Git blob checks account for checkout CRLF without relabeling changed bytes as equal raw SHA256.
- build-source-seal-v2-clarification.json correctly limits current source applicability to Debug/Directory.Build.props. Its specific-native-build.log and3 current test log hashes all match; build log reports0warnings/0errors, feedback and navigation logs PASS/0failures, full overlay log0failures. I read these supplied outputs without rerunning them.
- The unchanged0F2CB060C1DBAA09367C40E81B297951B5CC2883278B1B255EE4672258709225 IJL artifact matches its17af17257d8a0c0f72e1c392b9f9450821ee7d7c frozen manifest, whose recorded520B0195… hash also matches. Current IJL source/test inventory is context only and is not claimed as a rebuild of that frozen binary.

## Recorded scenario results

| Check | ClientA | ClientB |
|---|---|---|
| Left-only blank map | trigger0→0, draw0→0; route unchanged, destination474/234 unchanged | trigger0→0, draw0→0; route unchanged, destination474/234 unchanged |
| First/right route | trigger0→1, draw0→234, texture0→1; terminal arrived at town2/area0, goal1431/299 | cross-area terminal arrived at town2/area0, goal1039/372 |
| Rapid valid replacement retry | trigger4→6, target255/220→260/220; draw1398→1884; texture remains1 | trigger1→3, target260/220→255/220; draw202→845; texture remains1 |
| Two ring scheduling | new serial6 sampled with both rings visible and shifted scales | new serial3 sampled with both rings visible and shifted scales |
| Expiry | final inactive, both invisible, cleanup_pendingfalse | final inactive, both invisible, cleanup_pendingfalse |
| Close and reopen | trigger6→7; hidden reason clears active; draw1942 stays fixed; final map visible without replay | trigger3→4; hidden reason clears active; draw917 stays fixed; final map visible without replay |

A-first-right-pulses-proof.json shows first ring at383834953 withscale.5 and second hidden, then both visible at383835187 withscales.684/.5 and later.918/.718. texture_creations1 and draw_count>0 directly contradict the preserved generic-hook draw0 failure. Its route moves from town2/area5 to2/0 and ends with observed-arrival data. The companion first screenshot is the loading screen, so it contributes no yellow visual claim.

B-right-cross-area.json preserves native_route_only and input right-click255/246; area observations are1/1→1/0→2/1→2/0. Terminal status is arrived/final_position_observed, recorded position1039.416138/372.403595 near goal1039/372. This demonstrates the specific recorded cross-area route, not every route.

The rapid retries each contain both accepted serials and point replacement. For the final new serial, the first recorded initial-scale sample to first inactive sample spans approximately2016ms(A) and1953ms(B), consistent with the finite1950ms implementation and publication sampling. These coarse observations do not independently measure an exact1950ms GPU presentation interval. Both contain two-visible-ring samples, then no active rings; texture remains1 across repeated clicks. v2-edges-retry.log reports all4 checks PASS; independent offline assertions on the proof JSON returned0failures.

The close trials record an active new goal before N closes the map, then map_hidden_or_context_unavailable and inactive rings before normal expiry. Remaining samples never increment draw_count or trigger again, and final state explicitly reports minimap visible after reopening. Thus the no-replay conclusion comes from the recorded lifecycle data rather than the pass-log label alone.

## Original image inspection

Viewed7 original-size640×480 evidence JPGs: A/B rapid-v2, A yellow-double-clear, A yellow-canvas, A yellow-nearby, A yellow-stationary and the excluded first/loading screenshot. Every inspected evidence copy exactly matches the SHA256 of its logged native ScreenShot JPG source; no transformed/cropped image was used.

A/B rapid-v2 images show the native town map with a visible yellow annular feedback near the tested255/260 screen positions (y246, plane-local220); the cursor has moved away to600/420. A-yellow-double-clear at254/246 provides another yellow-ring still without cursor overlap. Blue/cyan player indication also remains visible in the viewed map stills. This supports visible yellow output and coexistence in these captured frames; it is not a full animation-regression proof of the original blue effect.

No single screenshot is used to prove two pulses separated by200ms or a complete temporal curve. The two-ring scheduling claim is supported by the runtime samples plus the already reviewed finite timeline implementation; screenshot inspection independently supplies actual visible-yellow evidence. Exact center/pivot geometry is visually plausible at these points but is not a subpixel measurement.

## Preserved failed attempts and limits

v2-edges.log records the original A rapid fixture expecting+2 but observing3→4, with only the260 target accepted; input245/246 is preserved as that failed fixture. The retry uses accepted blank points255/260, records4→6 and has separate JSON/JPG/log evidence. The failure was not overwritten or counted as a successful two-click trial. The source-specific first F5 draw0 attempt likewise remains RED.

Device identity changes, same-pointer device loss/reset, real delayed texture destruction/unload, direct driver-state readback, precision fade/rounding, screenshot-based time-series reconstruction and broad performance were not established by this evidence. They are not silently promoted to PASS. Global performance analysis and a complete frame-by-frame two-circle video are outside the requested bounded acceptance review.

Earlier source-review.md, P2 follow-up, source-specific follow-up and their hashes are preserved. This file and live-evidence-reviewed-hashes.json are the only reviewer writes for this increment.
