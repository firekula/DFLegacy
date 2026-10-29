> Resolved: sourceCFE6B98B / test73056716 passed independent expiry-recovery review with no remaining P1/P2 and45 focused tests. See client-route-expiry-recovery-final-review.md. The earlier pending case below is retained as historical reproduction evidence.

# Expiry recovery review — pending cancellation-observation fix

Reviewed source `778C2E8629A6FC9649B05D4DA420B70D0FD31A8E247E3CB6B53110DD157CD0BA`, tests `97148EC0A04EF8BAF992ADDA11DB8CB04491249182CED80BFCFF3A70DA70557D`. Independent focused suite: **44 route tests passed**.

The exact old bound expired-request rejection can authorize a bounded new-ID attempt only before observed acceptance and with unchanged original idle source. The added `receipt_changed` guard correctly prevents contradictory acceptance from being ignored; renewal audit IDs now append only after a new transport.send succeeds. These two narrow fixes are closed.

## P2: A valid but201..750ms sample can observe manual cancellation and then be discarded

In reach(), capture() returns a validated sample, then the new `age >200` branch continues before the existing destination/direction/auto/position continuation checks. Such a sample is too old to leave ample **sending** headroom, but remains within the global750ms validity window and can carry a newer observed manual override. That observation must not disappear if a later frame returns to the original destination/idle position.

Independent reproduction uses complete real96-byte request encoding and validates every native receipt with `nav.validate_response` (not a bare unvalidated receipt mock). Relative to NOW: original idle snapshots occur at0 and1, first request is issued at1; an exact bound expired rejection is recorded at900 and read at1000. Recovery capture sequence3 is from700 (age300ms, later than the original issue), at the original position but destination88,88/direction1,3. The age filter ignores it. Sequence4 is fully fresh at1040, back at the old destination10,20/direction4,4 and original position/auto state. Current source sends a new ID and reports final success: commands2, expired_request_renewals=[1]. The already observed manual change was overridden.

Expected narrow correction: perform cancellation/continuation checks on each readable validated sample before using the200ms criterion to wait for more sending budget; otherwise latch any observed manual override so no renewal may be sent. No stale/invalid sample should authorize sending. Complete metadata and samples are retained in `client-route-expiry-recovery-evidence.json`.

This is within the requested expiry-recovery/no-manual-override delta. No old geometry oracle, live client/server action, process access, source/index/HEAD edit or deployment was performed. The earlier request-expiry raw native proofs and their classification remain owned by the parent. No claim is made that the previous contradictory-terminal receipt sequence normally occurs in native code; the current manual-sample counterexample uses monotonic capture ticks and fully valid response bindings.
