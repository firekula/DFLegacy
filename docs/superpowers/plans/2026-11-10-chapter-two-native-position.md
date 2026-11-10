# Chapter Two native test positioning implementation plan

> **For agentic workers:** Use superpowers:executing-plans. Execute inline in the existing isolated worktree.

**Goal:** Replace repetitive manual travel/combat during C2 acceptance with the C1 semantic test controls, adapted to confirmed C2 native entries.

**Architecture:** Extend the explicitly linked private test host and its companion CLI. Native game-thread actions retain identity/signature guards; worker exports bounded observations. CLI proves peer outcomes separately from command acknowledgments.

**Tech Stack:** x86 C++17/MSVC, Python, original-image fixtures, existing Computer Use for screenshots and ordinary startup/exit UI.

**Spec:** docs/superpowers/specs/2026-11-10-chapter-two-native-position-design.md

## Global Constraints

Only codex/chapter-two, existing isolated worktree and DO-NOT-SHIP runtime. No production/daily/main edits, publishing, arbitrary memory interface, reward/store edits, synthetic clear or timer changes. No further manual movement or combat. Keep evidence namespaces and old failures.

## Review Focus

1. A remote actor or changed map cannot be moved.
2. Missing publication dependencies reject before either axis changes.
3. Acknowledgment after mutation is not peer synchronization.
4. UID zero is a valid monster, never a valid player.
5. A peer changing room or process prevents a synchronized result.

### Task 1: Guarded native positioning

Files: src/DFLegacy.ChapterTwo.Native/test_control/{Policy.h,Policy.cpp,Runtime.cpp,policy_tests.cpp}; private original-image runtime fixture under dist/chapter-two-cards-matrix-20261110/native-control-host.

Interface: request v2 adds int32 x/y and operation2=self-position; existing operation1=single monster. Export position and bounded player/enemy observations.

- [x] Confirm original C2 setters/getters/publication and original callers; retain read-only evidence.
- [x] Add policy and native original-image rejection tests; observe RED for positioning.
- [x] Implement native setter + full original publisher with fresh binding validation, protected position checks and per-frame context.
- [x] Run native checks and existing death contracts, build only private host; verify TLS delta8.

### Task 2: Reusable semantic CLI and outcome checks

Files: tools/debug/chapter_two_test_control.py; tests/test_chapter_two_test_control.py; test_control/README.md.

Interfaces: status, teleport --x/--y, kill --uid, optional explicit peer process list. Every action uses request v2 and guards current process/context. Export distinguishes native call from peer confirmation.

- [x] Add failing tests for request serialization, self-only positioning, target ownership, unknown acknowledgement and changed-peer refusal.
- [x] Implement v2 commands and bounded peer polling without retrying an unknown mutation.
- [x] Run targeted tests plus existing test-control checks and document exact usage.

### Task 3: Isolated native acceptance and checkpoint

Files: fresh DO-NOT-SHIP evidence directory, private host metadata, knowledge continuation and matrix report.

- [x] Preserve/exit old hosts normally; do not mutate a running image.
- [x] Deploy the verified private host only; restart isolated four-client run.
- [x] Use semantic positioning and target clearing to verify native changes, peer positions/deaths and normal door transitions; save screenshots and logs.
- [x] Record actual results and limitations, then resume the existing four-condition cards matrix with the new controls.
