# Test Control Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Independent server/native components use focused parallel workers; root owns CLI, integration and every live operation.

**Goal:** Add explicitly enabled test CLI/native controls, verify them, then resume four-client loot acceptance using server commits and persistence as primary evidence.

**Architecture:** Existing local mailboxes carry bounded commands. Server publishes authoritative snapshots/results and short-lived run leases; debug.dll executes restricted native actions on the game thread. Python orchestrates exact role/process/run bindings and reports server and client observations separately.

**Tech Stack:** C#/.NET, C++17 Win32, Python3 standard library, existing smoke/native/pytest runners.

**Spec:** `docs/superpowers/specs/2026-10-29-test-control-design.md` (user approved scope and architecture in chat).

## Global Constraints

- Existing isolated worktree `D:/codex_worktree/party-chat-gold/DFLegacy`, branch `codex/channel-return-next`; no second worktree.
- Defaults disabled; no normal save/resource edits, arbitrary address write API, injected gameplay packets or implicit screenshots.
- Preserve native item/ground identity and normal server death/grant/persistence paths.
- Full command and lease layouts are fixed by the spec; result acknowledgments never imply committed business success.
- Root exclusively controls live processes. New private runtime/evidence namespace `.tmp/test-control-20261029` under main checkout; old frozen namespaces remain unchanged.

## Review Focus

1. Crash/restart or room change between validation and mutation: revalidate actual current context before execution.
2. One client missing a ground actor or failing an operation: report partial sync, never whole-party success.
3. Random winner lacking weight/capacity: final rejection preserves ground and yields no committed grant event.
4. Repeated requests/death notifications: consume once and preserve exactly-once business guards.
5. Test flags off or lease stale: no mutation or additional hot-loop IO.

### Task 1: Server control and authoritative results

**Files:** `src/DFLegacy.Server/ServerOptions.cs`, `DungeonDebugOverlayService.cs`, `DungeonInstance` focused partial/helper files, minimal `EntranceService.cs` instrumentation; `tests/DFLegacy.SmokeTests/TestControlSmokeTests.cs`, runner registration.

**Interfaces:** Publishes `dfl-server-test-control/v1`, DTTL leases, committed/rejected loot outcomes; consumes `dfl-server-test-command/v1` ground layouts with matching nonce/run/room generation.

- [x] Write/observe failing tests for disabled/stale/replayed/invalid/pending layout and post-persistence journal behavior.
- [x] Implement atomic layout validation/update, bounded snapshots and IO service; retain original grant/death flow.
- [x] Run focused smoke and relevant loot/pickup regressions; record fresh logs and exact schema.

### Task 2: Native test actions

**Files:** `src/DFLegacy.Debug/src/client_test_control*`, corresponding native tests, project files, `debug_overlay.cpp` registration.

**Interfaces:** Validates DLTC112 requests and DTTL40 leases; publishes `dfl-client-test-control/v1`. Worker does IO, game thread executes verified native setters/death functions.

- [x] Write/observe RED validation and at-most-once execution tests using fake memory/callbacks.
- [x] Verify ACT1 native function bytes/calling conventions; implement position, enemy death and ground relocation with current identity checks.
- [x] Build and run native suite; report unavailable native behavior explicitly rather than guessing success.

### Task 3: Unified CLI and consumer tests

**Files:** `tools/debug/test_control.py`, `tests/test_debug_test_control.py`, usage documentation.

**Interfaces:** `state`, `player teleport`, `monster kill`, `drop isolate`, `loot result`; explicit directory and role/UID/PID bindings. Server outcomes and client observations separate JSON fields and failure codes.

- [x] RED tests for schemas/freshness/identity/packing/rejection/partial sync and authoritative commit rules.
- [x] Implement consumers, native transport, bounded waits and CLI orchestration without screenshot calls.
- [x] Run focused then relevant/full Python suite and managed smoke suite; integrate schemas with both components.

### Task 4: Live tool verification and resumed four-client loot

**Files:** New private run/evidence tools and bounded published `knowledge/decisions` report/assets; backlog update.

- [x] Preserve normal inputs, deploy only owned isolated runtime/configs, login existing four roles and bind exact process/run identities.
- [x] Verify CLI teleport and peer sync, designated enemy death with normal server drops, isolated ground layout and default-off behavior.
- [x] Verify white/blue/purple/pink and epic cancel/direct/roll with capacity preflight and exact committed item instance/slot deltas. Capture only key UI and dice images.
- [x] Fresh independent review and required checks; stop owned run, verify original hashes, publish bounded evidence and commit changes. Record gaps rather than closing unverified rows.

User's later skillbar persistence issue is recorded last in backlog and deliberately excluded from this implementation group.

## Execution result

Implemented and independently reviewed. Live scope, seven original images, exact positive/negative outcomes, existing Python failures and desktop UI timing limits are recorded in `knowledge/decisions/2026-10-29-test-control-four-client-loot-verification.md`. The run is stopped, test configs restored and normal inputs verified.
