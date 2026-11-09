# Bootstrap Automatic Repair Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Independent native and packaging tasks may use dispatching-parallel-agents after their shared contract is fixed. Steps use checkbox syntax for tracking.

**Goal:** Both architectures recover a missing/damaged launcher or interrupted update when the existing icon is opened.

**Architecture:** Stable native entry -> independent temporary V4 updater -> validated managed UI. Preserve existing updater semantics and keep a verified local recovery engine.

**Tech Stack:** C++20/Win32/BCrypt/static CRT; .NET 10 WPF and existing UpdateCore; Python 3.12 release tools.

**Spec:** `docs/superpowers/specs/2026-11-08-bootstrap-auto-repair-design.md`

## Global Constraints

- Root stays `DFLegacy.Launcher.exe`; UI `DFLegacy.Launcher.App.exe`; updater `DFLegacy.Updater.exe`; descriptor `DFLegacy.Bootstrap.ini`.
- Only Gitea `gitea-test` / `firekula/DFLegacy` / `http://firekula.site:3000`; new tags .3/.4, sequences 3/4; no formal GitHub or online service changes.
- x86/x64 independent; protected configs, original client, other worktrees and `ACT1_LOCAL_ACCEPTANCE.md` remain untouched.
- Descriptor exact keys/grammar, recovery selection, process flags and locks follow the spec verbatim.
- New builds and large fixtures on C: or G:, not nearly-full D:. Use existing native patch artifacts unchanged.

## Review Focus

1. Interrupted replacement of the updater and descriptor must leave a runnable older recovery engine (native + process acceptance).
2. Wrong-source legacy manifest must be rejected before replacing the fixed entry (managed guard test).
3. Unicode/root spaces and quoting must not change target or reveal arguments (native process test).
4. Double-click while updater/UI owns its lock must not create concurrent installs or kill processes (native/managed lock tests).
5. Corrupt cached receipt/config or rollback data must block launch with evidence, never silently reset accounts (managed/core regression + process acceptance).

## Task 1: Native stable entry

**Files:** new `src/DFLegacy.Startup/{CMakeLists.txt,bootstrap.h,bootstrap.cpp,main.cpp,tests/*,build-native.ps1}`; only this directory owned by native worker.
**Interfaces:** descriptor and process contract from spec; output per architecture `DFLegacy.Launcher.exe`; test runner and process fixture executable. Consumes existing icon via resource file reference; no Launcher/Core edits.
- [x] Write native policy/process regression tests, capture failure before implementation.
- [x] Implement strict descriptor/hash/architecture selection, safe temporary execution, recovery caching/atomic selection, locks, cancellation/fallback and bounded error log.
- [x] Build/test Win32 and x64 on C:, run realistic process tests, commit only owned files.
- [x] Independent native review; fix important findings before live packaging.

## Task 2: Managed updater and UI roles

**Files:** new `src/DFLegacy.Launcher/Updates/BootstrapUpdateClient.cs` and `BootstrapLayout.cs`; modify App.xaml.cs, MainWindow.Updates.cs, RepairClient.cs; tests `BootstrapUpdateTests.cs` plus Program dispatch.
**Interfaces:** `BootstrapLayout` descriptor validation and required-file guard; `BootstrapUpdateClient` recognizes `--bootstrap-client <root> -- ...`, invokes repair from external temp, validates final full receipt, starts UI with `--bootstrap-verified -- ...`. Reuse old repair with optional manifest validation callback before any target installation. Bootstrap UI holds app.lock and opens directly after verification; normal monolithic mode unchanged.
- [x] Write failing grammar/layout/source guard/dispatch/offline/UI-handoff tests using real fixture files and fake process-launch boundary only.
- [x] Implement independent updater path, progress, safe UI handoff, UI lock/direct-open redirect, manual recheck routing.
- [x] Run focused and full launcher/core regression, commit; independent review and fix.

## Task 3: Bootstrap packaging

**Files:** new `tools/release/bootstrap_package.py`, `tools/release/tests/test_bootstrap_package.py`; optional preserving opt-in helper specific to V4 bootstrap receipt. Do not change legacy package policy.
**Interfaces:** generate strict descriptor from actual UI/updater hashes and architecture; stage root files + V4 manifest/receipt + generated recovery engine using explicit built inputs, not user client tree. Shared engine build/publish from update_v4.py. Four starters carry correct immutable source identity and preserved configs.
- [x] Write failing tests for architecture mismatch, missing layout, legacy allowlist isolation, preservation, duplicate/source mismatch, no secrets or cached user files.
- [x] Implement builder/checker and stable entry assertion between update versions.
- [x] Run release regressions, commit and independent review.

## Task 4: Real artifacts and Gitea acceptance

**Files:** controlled scripts/evidence under `.tmp/bootstrap-auto-repair-20261108` and `artifacts/bootstrap-auto-repair-20261108`; standalone C# interruption harness referencing production Core, isolated roots on G:.
- [x] Build actual managed x86/x64 once per .3/.4 version, native per architecture; record source commits and hashes.
- [x] Generate four .3 bundles/starters, locally verify; publish .3 draft, all resources, manifests last, anonymous verification.
- [x] Test .2 migration and ordinary .3 startup from fixed shortcut; each architecture delete/corrupt UI and corrupt/delete primary updater. Record UI process/image identity and final hashes, close only owned test UI.
- [x] Generate/publish .4 with newer managed engine and resource; interrupt production transaction worker after write checkpoint, terminate worker, open original shortcut and verify recovery/upgrade/UI without manual repair. Verify old recovery engine retained until new one succeeds.
- [x] Test healthy offline vs corrupt offline and failures preserve files; collect incident records and source-original hash proof.
- [x] Review integrated result, update README/CHANGELOG/V4 guide/report, commit locally. No shared branch push, no production release.

## Execution ruling

User has explicitly approved the approach and asked implementation/testing; proceed without another design/plan approval. Current linked worktree on `codex/client-update-v4-diagnostics` is the authorized isolation. Root integrates the managed lifecycle and acceptance; a native worker and a packaging worker may work in their disjoint directories under the exact shared spec. No agent may edit another owner's files or publish.

## Acceptance update — 2026-11-08

Bootstrap Tasks 1–4 are complete. Evidence and limitations are recorded in `artifacts/bootstrap-auto-repair-20261108/REPORT.md`. After the user handled the firewall prompt, live gameplay exposed the solo dungeon type-2 chat envelope; commit `57f4550` fixes the narrow routing gate. The user confirmed gather, actual pickup preserved quantity, and an actual final-room move was rejected before normal settlement return. Client door animation/repeated move requests and the un-reproduced original crash remain explicit limitations; full gameplay coverage is not inferred from the bootstrap checklist. Formal GitHub and online services remain unchanged.
