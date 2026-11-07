# ACT1 team and relay PvP implementation plan

> **For agentic workers:** Use superpowers:executing-plans inline, task by task. User explicitly approved direct implementation of both original modes; no further design approval gate.

**Goal:** Restore working ACT1 team and relay arena matches, including eight seats, balanced teams, readiness, native turns and durable rewards.
**Architecture:** Keep existing channel isolation and two-phase commerce admission. Extend native wire builders and snapshot match participants by seat/team; commit every participant atomically through the existing idempotent PvP ledger. Follow the exact ACT1 client (MD5 71f643d7b7339ca58d9bac6ded960c8f), not later server packet extensions.
**Tech stack:** C#/.NET10, existing ACT1 native client and smoke harness.
**Spec:** User approval in this chat; wire evidence recorded in docs/notes/pvp-team-relay-contract.md.

## Completion record

Implementation committed as `0a6a422`; protocol/rating, original individual, team/relay and complete server smoke passed. Native acceptance and remaining multi-client/network limits are recorded in `docs/acceptance/2026-11-07-pvp-team-relay.md`. The user subsequently authorized source/document pushes and requested stopping the local test server; the implementation-time local-only constraint below describes the original phase. Test listeners on127.0.0.4 have been stopped. A subsequent user request authorizes sanitized GitHub source synchronization and the v0.1.7 server image; client update attachments remain unpublished.

## Constraints
- Worktree D:/codex_worktree/manager-export-release/DFLegacy only, branch codex/pvp-team-relay, baseline16fa02c.
- Local fixtures only. No publish, push, live changes or real player rewards.
- Preserve original two-player individual behavior, channel isolation, sequence generations, commercial ownership and uncertain-write quarantine.
- No new ports; reuse existing PvP channels and gameplay UDP.

## Task 1 — Native mode/seat wire contract
- [x] Record client evidence for mode0/1/2, team seat1/2 and relay messages. Original later-server relay112/113 is unsupported in this client and must not be sent.
- [x] Add independent hex/range tests for eight seats, readiness, starts, deaths, results and relay notifications; run RED.
- [x] Extend builders/readers, then run protocol tests GREEN.

## Task 2 — Durable multiplayer results
- [x] Add outcome tests for 2v2, 4v4, relay team victory including dead teammates, forfeit/draw, ranking VP and duplicate/restart safety; watch RED.
- [x] Extend settlement snapshot with optional mode, team and seat fields; retain old receipt readability and idempotence. Add team rating/VP calculation using documented original evidence and existing ACT1 protections.
- [x] Run targeted store tests and original individual suite GREEN.

## Task 3 — Room lifecycle and native relay
- [x] Add scenario tests for balanced/unbalanced starts, host-only mode, own team changes, readiness invalidation, sparse seats, capacity, in-match leave/disconnect and host migration, relay deaths/turns/timeouts, stale commands and UDP room isolation; run RED.
- [x] Implement eight seats, frozen participants and teams, native relay flow, map player limits, liveness and result ACK reset.
- [x] Run all PvP and standard smoke tests; build fresh isolated runtime. Restart only owned local test instances and verify modes with native clients. Capture evidence and update acceptance docs.
- [x] One fresh review of complete worktree changes, fix important findings with regression tests. Final delivery records what passed and any remaining native acceptance limits.

## Review focus
Sparse seat lookups; frozen winners when a teammate dies/leaves; same-team kill reports; malformed/oversized mode/seat/results; relay spectator versus active fighter; match generations; missing reports; atomic/idempotent rewards after partial I/O; coexistence with GM VP; accidental later-version packet fields; no Chapter2/online modifications.
