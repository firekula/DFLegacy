# Player Kill Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans for root implementation; one independent final code review. Root alone owns live operation.

**Goal:** Add self-only native player death CLI and continue U05, keeping U11 last.

**Architecture:** Reuse DLTC112/test lease, authentic native HP setter with verified player virtual functions; observe authoritative member death and fresh self/peer state separately.

**Tech Stack:** C++ Win32, C# .NET10, Python standard-library CLI.

**Spec:** `docs/superpowers/specs/2026-10-30-player-kill-design.md`.

## Constraints and review focus

- No writes to frozen test-control/active-drop namespaces, normal saves or resources; test outputs use generic random smoke directories.
- Self identity, exact process start, run/room/party and fresh lease required; no revive/forced server state shortcut.
- Applied/partial ACK is not death proof; no retransmission after uncertain execution.
- Peer disappearance alone is not proof of death; self HP0/dead and server dead required.
- Already dead, stale source, foreign actor/authority, changed room or party fail closed.

## Tasks

- [x] Freeze original player vtables/HP ABI. Native tests RED: op6 self/zero operands, dead refusal, player-only identity and at-most-once. Implement op6 and run strict Release tests.
- [x] Managed tests RED: actual alive/dead snapshot reflects death/revival and publishing does not mutate instance. Add projection only, run focused/full Release checks.
- [x] Python tests RED: op6 wire/CLI, local+peer+server independent confirmation, changed context/partial/no retries. Implement player_kill and verify related suites.
- [x] Run isolated dual-client native death acceptance from cloned state, key image, server death/peer state. Stop owned processes, restore INIs, verify normal hashes. Independent review, document exact boundaries and commit.
- [x] Continue U05 local MP observation with verified native fields and tests; U11 remains last after U09.

Completed: native self-only op6, party-only high-level confirmation, solo unknown projection, two P2 fixes, four-client member/leader death and duplicate refusal; local MP consumption/recovery and HUD. Full managed and related native/Python passed; full Python6 existing failures retained. Processes stopped, configs restored, normal save unchanged; U11 deferred last.
