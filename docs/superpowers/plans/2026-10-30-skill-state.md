# Skill State Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans for root implementation; independent static research only, one fresh-context final review. Root owns live clients.

**Goal:** Complete U05 cooldown/usability data with original ACT1 semantics and bounded evidence.

**Architecture:** Extend current v2 Skill records and capability flags; guarded read-only native capture and strict consumer support, preserving unknown independently from ready/false.

**Tech Stack:** C++ Win32, Python standard library; existing C# isolated server unchanged unless demonstrated need.

**Spec:** `docs/superpowers/specs/2026-10-30-skill-state-design.md`.

## Constraints and review focus

- No mutations during observation, no clock-domain guesses, no fake zero/default readiness.
- Paused/frozen time, expired/equality boundary, signed/overflow durations and truncated lists must have explicit behavior.
- A usable skill must satisfy actual native gates, not simply cooldown=0/MP sufficient; unsupported predicates remain unknown.
- Skill/actor replacement during capture invalidates mixed data; every field must retain PID/start/generation freshness.
- v1/town and capability-disabled fields remain unavailable; API query/wait must respect per-field support.

## Tasks

- [x] Freeze original cooldown/timebase and usability contracts from PE and independent callers.
- [x] Native tests RED for real cooldown boundaries, unavailable/unknown gates and identity replacement; implement guarded Skill capture/JSON; strict Release relevant suites GREEN.
- [x] Python tests RED for v2 capabilities and known zero/false/null, v1/town/invalid values; update query/wait/summary and run related suite GREEN.
- [x] Isolated live ready→cast→cooldown/unavailable→ready, plus an independently blocked condition supported by native contract. Use debug snapshots/native input; retain key original images. No old evidence writes.
- [x] Independent review, full applicable regressions, close owned processes/restore configs, normal/frozen hashes, publish exact scope and update U05 docs/changelog, commit.

Completed: C46/D4 cooldown cycles, context-only block and native death false; original selected-id mistake documented, six review P2 fixed with RED regressions. Native0fail, Python235/315; full541/363 with6existing failures. Test runtime closed and restored; U11 deferred.
