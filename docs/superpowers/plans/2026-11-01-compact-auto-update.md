# Compact automatic update implementation plan

> **For agentic workers:** Use superpowers:executing-plans inline. This is the user's explicit small UI-flow change; implement and test without another approval round.

**Goal:** One automatic startup path, a left-anchored native progress window, and verified safe updates.
**Architecture:** Reuse UpdateController, UpdateSession and authenticated UpdateInstaller; let UpdateCoordinator control login visibility and automatic installation. Feed real transaction milestones into the compact window.
**Tech Stack:** .NET 10, WPF, existing C# executable test suite.
**Spec:** ../specs/2026-11-01-compact-auto-update-design.md

## Constraints and review focus

- Keep existing source/package/identity validation and recovery transaction intact.
- No timers pretending to measure progress; no duplicate login/update pages.
- Never write or launch the user's real game as part of update fixtures.
- Check cancellation/close races, recovery consent, occupied files, identical current packages, and failed restart.

## Tasks

- [x] Add progress/startup tests, then implement compact progress and one-window automatic coordination in UpdateCoordinator, MainWindow.Updates and App.
- [x] Add transaction milestone and same-installed-release tests, then add safe progress callbacks and skip identical version downloads.
- [x] Run the launcher suite, authenticated real-process replacement/restart and recovery tests, verify public GitHub transport on an isolated copy, render/inspect compact UI, and build a reviewable local package. Preserve unrelated uncommitted documentation. Do not publish or monitor CI.

## Verification and rulings

- Full launcher suite: 1872 assertions, 0 failures (`C:/DFLCI/compact-update-full-tests.log`).
- Real single-file production launcher was installed/restarted through the authenticated helper; exactly one real login window remained, fixture originals/config survived, pending transaction removed, backup present (`C:/DFLCI/compact-update-20261101/actual-process-20261101-112312/result.json`). Download transport in this case was the deterministic fixture, not GitHub.
- Public HTTPS check: `no_compatible_v2_release`; no actual public install claimed (`C:/DFLCI/compact-update-20261101/public-github-check.json`).
- Native compact screenshot inspected at `C:/DFLCI/compact-update-preview.install.png`.
- Independent review: matching-tag optimization initially failed to persist the monotonic release floor, and internal timeout was silently treated as user cancellation. Three failing assertions reproduced both issues before fixes; follow-up review found them resolved. Full suite above includes fixes.
- The first pixel test rendered a Canvas-offset child into an 8px bitmap. Corrected it to render the control at origin; final test observes left edge and actual half-width fill. No timer-based progress remains.
- The first external process test waited on inherited pipe EOF after the original parent exited. The helper result already showed success; test now waits for the actual parent process and checks the restarted window separately. Final process result above passed.
- Ruling: use the existing linked worktree, preserve unrelated DeepSeek docs, create a local commit and packages only. Publication/CI tracking was not requested for this change.
