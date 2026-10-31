# Launcher Resolution and Sharp Scaling Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking. User explicitly approved direct implementation, testing and publication; routine implementation choices do not need another approval.

**Goal:** Save game display settings from the launcher and reproduce the selected sharp-bilinear whole-frame scaling with native D3D8.

**Architecture:** Keep Config.ini authoritative. Separate the preserving INI editor, display model and WPF tab from the per-device D3D8 presentation engine. Use an extra windowed swap chain for physical-size output; keep the game's original 640×480 backbuffer intact.

**Tech Stack:** .NET10/WPF; x86 C++17/Win32/D3D8 fixed function; CMake/MSVC; Gitea Actions.

**Spec:** docs/superpowers/specs/2026-10-31-launcher-resolution-sharp-scaling-design.md (approved in chat after commit bc8cf99).

## Global Constraints

- Internal 640×480; presets 640×480,800×600,960×720,1024×768,1280×960,1600×1200; no widescreen or live resizing.
- PresentationFilter=sharp|point|linear|legacy; missing/invalid on existing files stays legacy; new defaults sharp. Preserve legacy ScaleFilter and unrelated INI bytes.
- No per-frame CPU readback in production, no new proxy DLL/runtime, no system display/security changes.
- Restart applies saved settings; preserve running clients/services, original assets and main's3 dirty files/163 status records.
- User selected sharp-bilinear. Algorithm: nearest integer ceil scale, then bilinear to physical-size output with no runtime restretch.

## Review Focus

- Conflicting client paths or external edits while a dialog is open must never write a different installation or clobber changes (Task1).
- BOM/duplicate section differences between managed and native INI readers must not silently save ineffective settings (Tasks1/2).
- D3D resource/device ownership must not self-cycle or free an executing cloned vtable (Task3).
- An already active render target/state block/stream or failed Reset must not corrupt the following frame (Tasks2/3).
- An enhancement fallback must be observable and must preserve device-lost HRESULTs (Tasks2/4).

### Task 1: Preserving display configuration and WPF settings

**Files:** Create src/DFLegacy.Launcher/IniConfiguration.cs, ClientDisplayConfiguration.cs, tests/DisplayConfigurationTests.cs. Modify ClientServiceConfiguration.cs, Views/ConfigWindow.xaml and .xaml.cs, tests/Program.cs.

**Interfaces:** IniConfiguration.Load(path) captures bytes/encoding and supports Read(section,key), With(updates), SaveExpected(path,expectedBytes,outputBytes). ClientDisplayConfiguration.Load(clientPath) returns a snapshot with Width,Height,PresentationFilter,OriginalBytes,ConfigPath; Save(snapshot,width,height,mode) validates presets, compares bytes, preserves unknown content and atomically replaces. UI writes only the display snapshot; connection save remains LauncherAccountStore.

- [x] Write regressions against real temporary files: three-key update preserves unrelated bytes in ANSI/UTF8/UTF16, duplicate sections read back correctly, missing file creates embedded defaults, locked/stale/UTF32/invalid geometry fail without writes; no-op preserves mtime; edited launcher path invalidates display save.
- [x] Run `dotnet run --project src/DFLegacy.Launcher/tests -c Release -p:BuildNativeAssets=false -p:Ijl15PrebuiltPath=<matching native DLL>`; observe missing API and behavior RED.
- [x] Implement the immutable snapshot and preserving editor; keep ClientServiceConfiguration's current strict URL validation and use the shared edit primitive without weakening its byte-preservation tests.
- [x] Add connection/game-display tabs, native preset/mode controls and independent save. Show actual target, dirty-path prerequisite, restart note, old-custom-size state and read/IO errors. Keep all controls within a scrollable bounded work area and keyboard order.
- [x] Run the complete Launcher suite and inspect compile/runtime output; commit configuration and UI deliverable.

### Task 2: D3D8 production presenter and GPU acceptance

**Files:** Create src/DFLegacy.Ijl15/src/d3d8_abi.h, presentation_scaling.h/.cpp, d3d8_presenter.h/.cpp, tests/d3d8_presenter_tests.cpp. Modify CMakeLists.txt and aligned vcxproj/.filters.

**Interfaces:** presentation_scaling::Plan(width,height,mode,caps) yields supported dimensions/integer factor and failure reason. d3d8_presenter::Presenter(device,original device-call table) owns source/intermediate textures; Render(source,target,plan) returns RenderResult(Status,RestoreStatus,Stage). The hook context owns the output swap chain and supplies its surface. Clear() releases GPU resources without owning a lasting device reference. API is shared by real hooks and GPU harness.

- [x] Create failing policy/GPU tests: 800 sharp must use1280×960 intermediate; original backbuffer unchanged; true2×2 pixel blocks at1280; aligned color/checker/edge outputs match CPU reference; state/viewport/render-target/stream restored; unsupported target and lost-device outcomes explicit.
- [x] Build/run the new tests against missing implementation and observe RED. The GPU harness dynamically loads the installed d3d8 runtime and creates a test-owned window/device; it never manipulates the user's game.
- [x] Implement exact ABI declarations, scoped COM helpers and fixed-function passes. Preserve all game state, clamp valid texels/POT padding, cache resources, propagate HRESULTs and release every temporary/captured reference. Use point short paths at integer sizes.
- [x] Test actual GPU output including every preset, POT allocation edges and repeated Clear/rebuild; write private readback images/results only under this plan's validation path. GPU absence is a distinct failure for local acceptance, not a silent pass.
- [x] Run native suites including new GPU test and commit presenter deliverable.

### Task 3: Per-device hook lifecycle and native configuration

**Files:** Modify d3d8_scaler.cpp/.h, ijl15_config.cpp/.h, dnf_patches.cpp, Config.ini; add tests/d3d8_hook_tests.cpp and update native build lists. Share the Task2 presenter.

**Interfaces:** Scale presentation settings in native config map the four ASCII mode names and existing width/height; d3d8_scaler::Install() installs per-factory CreateDevice and per-device Present/Reset/Release hooks with original-call tables. Existing texture-state pump remains, forcing old filtering only in explicit legacy.

- [x] Add failing actual native-reader probes for new defaults/missing/unknown keys/encodings and hook fixtures for two independent devices, reentrancy, resource-held device references, final Release, Reset failure/retry, minimized/unsupported Present and fallback status.
- [x] Run probes/fixtures and observe RED for the missing presenter route and lifecycle behavior.
- [x] Implement per-object context ownership and per-thread recursion guards. Conservatively clear presenter resources before external device Release; do not rely on guessed external COM counts. Free cloned tables only after original final Release; internal presenter resource releases bypass cleanup under the guard.
- [x] Implement Present routing, reset handling and rate-limited per-process render diagnostics (request/effective mode, sizes, reason/HRESULT, time/PID). Preserve lost-device returns and original backbuffer; make fallback visible to the WPF display page.
- [x] Keep old missing-key INIs on legacy, new embedded defaults sharp. Unsupported4:3/caps go to marked original-present fallback without rewriting the file. Retain source instruction/image gates and all existing hooks.
- [x] Run full native and Launcher suites with the newly built matching DLL, actual GPU harness and fake-COM lifecycle tests; commit hook integration.

### Task 4: Actual UI/game acceptance, review, integration and publication

**Files:** Add docs/superpowers/reports/2026-10-31-sharp-scaling-acceptance.md; adjust only release/CI test invocation needed to include appropriate noninteractive native coverage. Evidence remains private.

**Interfaces:** final published x86 DLL and single-file Launcher from Tasks1–3; existing tools/release and Gitea pipeline. Final reviewer receives spec, plan, ledger and full base-to-head diff.

- [x] Run real WPF settings cancel/save/path-change/reload through computer-use; inspect final portable binary. Use exact owned processes and preserve user authentication/security handoffs.
- [x] With verified backup and no conflicting live game, test actual800 sharp,640 and1280 at G:/dnf_local/DNF_2008. Capture real output-mode diagnostics and screenshots; verify mouse hover/click alignment, image edges, restore/AltTab and snapshots. Do not claim gameplay beyond what was actually exercised.
- [x] Measure warmed legacy/sharp presentation CPU cost and frame intervals; report p50/p95/peaks and caching/lifetime behavior. Address regressions before making the mode default; record limitations accurately.
- [x] Run full relevant native/Launcher/managed/Python/release suites; fresh whole-branch review and one RED→GREEN fix pass for all material findings, with green suites afterward.
- [x] Preserve main's exact dirty content/status while fast-forwarding approved source. Temporarily resume runners, push main, monitor CI and resolve actual failures without weakening gates. Independently download all release assets and verify manifests, hashes and private dual-arch OCI.
- [x] Install only the validated binary and chosen display keys after verified backups; compare original426 resource hashes and unrelated config. Restore runner pause, record final acceptance, and push documentation with[skip ci]. Keep the user's current services and resources.

## Self-review and execution rulings

All spec sections map to Tasks1–4. Task1's mode names and authoritative INI feed Task3; Task2's presenter is shared by Task3 and GPU acceptance. Source-preserving configuration, current-path guards, state/lifetime/reset and visible fallback each have explicit tests. GUI-only evidence is distinct from headless CI. User approval already covers implementation, testing and publishing; execute inline without another execution-method confirmation.
