# Native route consumer delayed-terminal independent review

Reviewed the uncommitted consumer/test delta on root HEAD `76db1cb807303853b4c48bf67e8272540b4c4ba2`. No P1/P2 findings remain in this consumer change.

Reviewed source hashes:

- `tools/debug/client_native_route.py`: `F751F989413412C8974F94B69D43F12D60A70E38FEA8328C9978835948DAA543`
- `tools/debug/test_client_native_route.py`: `318EF90A806574DFECFD01E3704F5A813EB9CB6BC88CF95D42928B75ABC1D1F7`

## Contract retained

Only `arrived`, `rejected`, and `interrupted` are exempt from the 1000ms maximum response age. The allowed-status check occurs before the exemption. Every response still needs an integer event tick satisfying `issued_tick <= event_tick <= now`, exact schema/PID/process-start/UID/request ID/debug source, original generation and actor/map/user/context tokens, and exact final town/area/x/y. All seven progress statuses still reject at age1001ms and accept at age1000ms.

An exempt arrival is an eligible native event, not sufficient evidence of completed execution. `execute()` still independently calls the real SDK snapshot loader with its default750ms age bound, validates the Town schema and PID/process start, pins UID/user/actor/context, requires the SDK capture strictly after the terminal event, and checks final Town/area, position within3px, and idle directions. Failure terminals preserve their actual reason and do not claim arrival.

The `execute()` function text is unchanged from the preserved RED source, confirmed by AST-selected source comparison as well as the Git diff. It writes one command before its bounded loop, checks the same command bytes and process start on each iteration, and never resends while awaiting publication or SDK proof. The existing default150s deadline and maximum300s configured bound remain. Publication retries do not rewrite the native event timestamp.

## Independent verification

- Production unit suite:13 methods,0fail/0error (`consumer-unit-tests.log`).
- Original delayed-terminal probe:4 methods,0fail/0error (`consumer-original-probe.log`).
- Additional independent probe:94 checks,0fail, including17 production `execute()` cases with memory transport and the **real** `state.load_snapshot`, schema, PID/start, freshness and consumer validators (`consumer-independent-probe.py`, `consumer-independent-results.json`).
- Direct boundary matrix covers all terminal statuses at1000/1001/1240/149000ms, all progress age boundaries, and altered schema/PID/start/request/UID/source/generation/tokens/goal/pre-issued/future timestamps.
- Actual SDK loader rejects stale, future and invalid captures. Equal-event-tick SDK evidence waits and times out. Wrong current position/direction/UID/user/actor/context, process replacement, command supersession and wrong native PID fail closed. All17 execute cases write exactly one command. Stale progress and unavailable SDK reach the deterministic2s deadline without resend.
- All11 entries in `X/controller/native-consumer/fix-verification.json` match their recorded SHA256, including original RED source/tests/logs, final test/probe logs and both current source files. `consumer-provenance.json` records the independent comparisons.

## Scope

This is source and offline consumer verification with deterministic clocks and memory transport. It does not reproduce an actual filesystem publication stall, run a150s wall-clock wait, certify Native event production, or constitute UI/live acceptance. Process identity calls were mocked; no client, server, OS input, real mailbox, deployment or Native build was operated. Python was run with bytecode writes disabled. Only this new review directory was written; existing evidence and repository source were left intact.
