# Expiry recovery final independent review

**Verdict: no remaining P1/P2 in the requested expiry-recovery delta.**

Frozen working source hashes:

- `client_route.py`: `CFE6B98B5BAA347E77DD62B929E2B51F40A0EBC3E2D0A48DDD40B1BDE347AF21`.
- `test_client_route.py`: `730567167A47CF339E2F130469ECCC3DC84E9151071B294B9DBDC897DD480831`.

Each pending send waits up to five seconds for a sample aged no more than200ms, preserving native750ms validation and without publishing while data is unavailable. The initial capture remains fail-closed. Every readable globally valid sample is now checked for source area/generation/map, pinned identity, prior destination/direction/auto state and renewal position stability **before** the200ms criterion can continue waiting. This closes the reproduced300ms manual-override observation that was previously forgotten when a later sample reverted to the old values.

New-ID recovery is restricted to an exact validated `rejected/expired_request` before any observed acceptance, with the original source idle. Its unchanged original source is re-proven on fresh data; position may differ by at most0.5px, destination/auto must remain unchanged, and geometry is replanned. Ownership and receipts are rechecked throughout. A later changed receipt, accepted state, timeout, other rejection or observed manual change stops recovery. Renewal count remains at most three per reach operation; only a successfully published new command records the corresponding expired old ID in `expired_request_renewals`.

The source's native-expiry interpretation is consistent with the existing C++ dispatch ordering: ValidateRequest returns the expiry reason before SetAndActivate, and the old request ID is consumed. No native deadline or native routine was modified by this Python delta. Earlier original native/raw runtime rejection evidence remains parent-owned provenance; this review did not perform a live action or relabel those results.

Independent verification: `python -B -m unittest tools.debug.test_client_route -q` — **45 tests passed**. This covers bounded renewal, no renewal after acceptance/other failure, changed receipt rejection, audit recording only after actual publication, freshness waiting and the aged-but-valid manual cancellation regression. No old geometry oracle or broad new scope was repeated.

The exact previously reproduced state sequence and original outputs are preserved in `client-route-expiry-recovery-evidence.json`; its `final_review` entry binds the approved hashes and focused result. `client-route-expiry-recovery-review.md` preserves the historical pending finding. This source verdict leaves actual final-matrix/native runtime acceptance and all-town coverage to the parent.
