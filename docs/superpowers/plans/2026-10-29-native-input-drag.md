# Native auto-input drag implementation plan

> **For agentic workers:** Use superpowers:executing-plans and TDD; existing party_api_review performs final independent review.

**Goal:** Add native left-button drag for bag-to-ground acceptance without OS input, external injection or driver edits.

**Architecture:** Extend the existing JSON/Step contract with drag and destination coordinates. Keep the current NativeMouseInput path, carrying held mouse-button masks. A three-stage timeline emits source move/down, destination move, destination up on separate input samples; lifecycle cancellation releases held synthetic inputs before reset.

**Tech stack:** C++17, existing MSBuild Win32 Release native tests and DLL.

**Spec:** Root's explicit drag subtask from3b2c399 and approval of MK_LBUTTON plus16ms minimum stage spacing. Nominal drag times are t, t+dur/2 and t+dur; late/zero-duration plans may finish later to preserve those sampling gaps.

## Constraints and review focus

- Edit only this isolated worktree's IJL auto_input implementation, native test wiring/tests and related explanation.
- No live game/server/client, deployment, root V driver changes, OS input APIs or original sample mutation.
- Existing click/key/text/move/rclick behavior stays compatible; destination fields belong only to drag, which requires source and destination coordinates.
- Preserve pure transactional parsing; malformed/rearm/Restore must not leave native mouse buttons latched.
- Cover multiple Pump calls in one sample, late/zero-duration catch-up, tick wrap, destination release and held MK_LBUTTON on move.

### Task1: Contract and timeline RED/GREEN

- [x] Add native drag tests and focused runner, with both MSBuild/CMake test wiring.
- [x] Observe missing drag, incorrect release and lifecycle RED before implementation.
- [x] Add kDrag/Step destination, parser restrictions, held mouse mask, staged timeline and cancellation release.
- [x] Run focused tests GREEN, then the full native compatibility executable.

### Task2: Release and handoff

- [x] Build Win32 Release DLL/tests with /W4 /WX, retain logs and SHA256 of own artifacts.
- [x] Explain nominal versus delayed timing, schema, lifecycle behavior and unverified live limits.
- [x] Commit initial scoped changes and ask existing party_api_review for independent review.
- [x] Reproduce and fix unreadable-file cancellation and both directions of delayed mouse sequencing.
- [ ] Freeze the reviewed follow-up commit/artifacts and return final SHA/artifact paths to root.

## Ledger

- Native baseline Release build exit0; full baseline compatibility test result is retained in .tmp/native-input-drag-validation.
- Ruling: use16ms minimum between drag stages, approved by root because Pump can run multiple times per rendered frame. This is an input sampling gap, not a proof of a particular renderer's frame rate.
- Ruling: keep ParsePlan pure and compatibility for existing operation fields; reject x2/y2 on non-drag, require x/y/x2/y2 on new drag, reject drag's irrelevant vk/chars.
- TDD: drag-red.log17 expected failures; drag-final.log0 failures; native-full-final.log confirms full compatibility passed. Strict release-rebuild.log0 warnings/errors.
- Ruling: lifecycle cancellation preserves a nested timeline guard while releasing held inputs; malformed replacement files cancel, missing files do not. Existing click remains a same-coordinate gesture.
- Review P2: an exclusive own-test-file handle reproduced unreadable-open cancellation2fail in unreadable-red.log. Only file/path-not-found now preserve the old plan; focused/full unreadable-green logs pass.
- Root-approved review boundary: nominally ordered drag and other mouse steps must remain ordered after a late pump, retaining16ms after the prior final mouse sample. Keyboard/text remain parallel; ordinary click→click semantics stay unchanged. Order is nominal t then array index.
- TDD: sequence-red.log20fail covers drag→click/rclick/drag/move; prefix-red.log10fail covers click/rclick/move→drag. Corresponding focused/full GREEN logs pass, including same-tick/+15/+16 boundaries and held target move. No live input, driver changes or deployment occurred.
