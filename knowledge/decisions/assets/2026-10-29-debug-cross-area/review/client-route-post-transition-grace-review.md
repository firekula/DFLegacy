# Post-transition capture grace: narrow independent review

**Source verdict: no new P1/P2 finding.** The small delta is approved for the independently planned runtime acceptance. This does not certify all loading sequences or all town routes.

Base: `62b78483ffecf81a4fb4ec95f89a88991f07a681`.

- `client_route.py` SHA256: `051B5A6BCDF41287A1AD337D86A5B1A174291062E87CC9D2348FB2EB7A61376D`.
- `test_client_route.py` SHA256: `6AC0225ED78C70B8B02C9821A85E3F71437F388DD5C2C2705F785B5E56C76BCB`.

The five-second grace is granted only when the pending native leg was accepted and the snapshot proves the expected new area, increasing generation and different map. It applies to the first continuation capture after that proven transition. During the wait, every pass still checks ownership of the old binary request and revalidates its native receipt; a negative receipt or supersession aborts. Only `stale`, `invalid_capture` and `unreadable_json` can retry. Identity, UI, sequence and unrelated context errors remain immediate failures. No request is sent from an unavailable capture. The first valid capture clears the grace before the regular fresh context, movement/ownership and geometry checks proceed. Same-area staleness does not gain this retry policy.

Direct independent verification:

- `python -B -m unittest tools.debug.test_client_route -q`: **35 tests passed**.
- Same-area stale capture after one ordinary internal leg: immediate `stale`, no waiting and only the original command.
- Proven-transition stale retry followed by binary ownership loss: `superseded` after 0.04 seconds, only the original command.
- Proven-transition stale retry followed by a replaced actor binding: immediate `identity_changed` at 0.04 seconds, only the original command.
- Both new successful-pause and five-second bounded-failure regressions passed in the focused suite.

The review was read-only except these requested evidence/report files. No client/server control, process access, configuration change, source/index/HEAD edit, deployment or new agent was performed. Existing 62b7848 core and geometry review conclusions remain unchanged. Runtime proof files and their success/failure classification are owned by the parent; this review does not relabel them.

The frozen hashes and exact direct verification results are appended as `post_transition_grace_review` in `client-route-task2-review-evidence.json`. The broader previous source review remains in `client-route-task2-final-review.md`.
