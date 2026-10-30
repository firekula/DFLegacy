# Town return and debug overhead Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans for root integration and scoped subagent implementation.

**Goal:** Both blank-map buttons work after actual dungeon return, and measured debug overhead is reduced without weakening checks.

**Architecture:** Pure controller initial-area exception; authenticated native hit ingress; measurement before targeted worker/per-frame optimization. Preserve data/economy transactions.

**Tech Stack:** Win32 C++17/MSBuild, Python offline evidence, native IJL UI and DLL debug commands.

**Spec:** docs/superpowers/specs/2026-10-29-town-return-perf.md

## Constraints and review focus

- Old instance/actor/generation, transport and dungeon selector cannot become routes.
- Left/right require their own consumed event and exact current root; NPC/menu/hidden/cached data reject.
- Only initial dungeon_gate can be exited; target/transit restrictions stay.
- Main remains nonblocking; derived IO never changes business-state durability.
- Perf claims need stage/frame/IO evidence; whole-plugin OFF is a combined control only.

### Task1: Preserve acceptance and current reproduction
- [x] Record human U04 acceptance separately; copy live netdump/route/SDK and exact running journal.
- [x] Save initial unsupported_area proof and scope known baseline failures.

### Task2: Controller egress (scoped agent)
Files: town_route_controller.h/.cpp; town_route_controller_tests.cpp.
- [x] RED initial1/2 source, true3portal PVF chain to2/0, initial birth gate, no unsafe target/transit.
- [x] Minimal source-only exception; GREEN and independent review.

### Task3: Map ingress (root)
Files: client_navigation.h/.cpp/runtime.cpp and corresponding navigation tests.
- [x] RED consumed left event8 after native hit with FD8 cleared; independent left capture/current/rechecks; root/marker/menu negatives.
- [x] Shared after-original ingress for both buttons; preserve ABI/original UI and native request guards; GREEN.

### Task4: Perf isolation and bounded optimization
Files determined by measurements, expected client_party_runtime.cpp/client_state.cpp only if justified.
- [x] Record capture-duration and disk/publication counters; compare narrow controls without mislabeling plugin-OFF.
- [x] RED redundant IO/coalescing/freshness behavior before changing it; preserve bounded requests/terminal results.
- [x] Relevant and full suites, source review and frozen artifact.

### Task5: Native return validation and delivery
- [x] Stop/archive only owned old phase after preserving manual state; new private phase from exact closed state.
- [x] Both actual return-gate starts, both buttons, same/cross-area, manual and UI protection; key native screenshots.
- [x] Matched perf observations and honest limits; restore configs/inputs/protected files, audit and commit.

Outcome: source 5f101c9; live four-button checks, gate egress, manual interruption and guard checks passed. Performance sample supports duplicate-result elimination and fresher SDK export only; intermittent hitches remain unassigned. Full Python retains 6 known baseline failures. See knowledge/decisions/2026-10-29-town-return-perf-verification.md.
