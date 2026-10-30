# Owned inflight capture gap: narrow independent review

**Verdict: no new P1/P2 source finding.** Reviewed only the requested two-line production delta and its three new regressions.

- `client_route.py` SHA256 `1D2AEC89BEBBB1E262E0BCC5880BD124BA3CB6CAF34D0331F33B622A73F31396`.
- `test_client_route.py` SHA256 `F2A6477432AEF42EC5EEA495719E8AA4A009C96115B92F0D539CEE216B19633C`.

A still-owned, published native leg may now wait through `stale`, `invalid_capture` or `unreadable_json` even when it is a same-area leg. A valid capture resets the continuous-gap timer; an uninterrupted missing-data interval remains limited to five seconds and the whole operation's deadline. The wait contains no send/reissue path. Every pass still verifies the original binary request and native receipt before reading another snapshot. A negative receipt, lost ownership or identifiable identity/context error is not silently converted into success. Fresh data must still pass the existing identity, area/map/generation, destination and auto-move checks; native acceptance remains required for completion.

The initial capture and ordinary same-area capture between separate native legs have not gained this retry policy. The previously reviewed, separately scoped post-transition first-continuation grace remains unchanged.

Independent command: `python -B -m unittest tools.debug.test_client_route -q` — **38 tests passed**. The three new cases verify one-command recovery from a transient same-area gap, one-command failure after a continuous approximately five-second gap, and immediate `identity_changed` when a readable post-gap snapshot replaces the actor.

No old geometry oracle was repeated. No source/index/HEAD edit, live client/server operation, process access, configuration change or deployment was performed. This is a narrow source/offline-test approval; parent-owned runtime proofs retain their independent classification and no full-town/loading-sequence coverage is claimed.

Exact hashes/results are appended as `inflight_gap_review` in `client-route-task2-review-evidence.json`, now its latest review entry.
