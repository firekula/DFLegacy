# Launcher Regions and Auto Update Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the approved original-style server selection, notifications and GitHub client updater with a verified public release.

**Architecture:** Keep WPF/native assets and the existing two client binaries. Separate selection/probes, safe update transport/install, UI integration and release publication; run implementers sequentially because they share launcher integration files.

**Tech Stack:** .NET10 WPF, C#, Python release tooling, Gitea Actions, public GitHub Releases.

**Spec:** `docs/superpowers/specs/2026-10-31-launcher-regions-auto-update-design.md`

## Global Constraints

- Updates replace only `DFLegacy.Launcher.exe` and `ijl15.dll`; preserve all player configuration and game files.
- Use `firekula/DFLegacy`, fixed asset `DFLegacy-client-update-win-x64.zip`, schemaVersion=1; ZIP≤256MiB, expanded≤512MiB.
- Original server state images: default265, hover266, selected264; green selection independent of online status.
- User explicitly approved direct implementation, tests and publication. Do not ask again for plan, merge, push or publication approval.
- Worktree `D:/codex_worktree/deployment-web-release/DFLegacy`, branch `codex/launcher-regions-auto-update`. Never reset/clean another checkout.
- Keep user-requested local server PID38252/config `D:/DFLegacyCI/validation/sharp-scaling-20261031/test-server.json` running. Do not close user game/launcher. CI runners were paused; restore prior paused state after an authorized release run.
- No native product DLL source changes, no permanent extra updater executable, no tokens in output. Only controller publishes or touches runner/service state.
- SDD reports are ignored scratch: write them but never stage/commit them.
- Read/write only this plan's SDD workspace. No implementer/reviewer may spawn agents. Commit only explicitly owned task files; docs may need `git add -f`.
- Test launcher with `dotnet run --project src/DFLegacy.Launcher/tests/DFLegacy.Launcher.Tests.csproj -c Release -p:BuildNativeAssets=false -p:Ijl15PrebuiltPath=D:/codex_worktree/deployment-web-release/DFLegacy/artifacts/native/ijl15/Release/ijl15.dll`.

## Review Focus

- Switching two endpoints during an outstanding probe must not attach stale latency or account API results to the new selection (Task1).
- Unicode/long server names and long multiline errors must fit or scroll while confirmation remains reachable (Tasks1/2).
- Interrupted download, malicious ZIP, reparse directory or changed staged binary must leave installed files byte-identical (Task3).
- Game/DLL or second-file lock, parent identity mismatch and simultaneous updater must preserve or restore both old binaries (Task3/4).
- Fresh legacy installation, already-current hash, GitHub404/timeout/429, prerelease versus stable and interrupted CI rerun must not loop, downgrade or publish inconsistent bytes (Tasks3/4/5).

---

### Task 1: Configurable operator partitions and real selected-server latency

**Files:** Modify `src/DFLegacy.Launcher/Models/LauncherOptions.cs`, `Services/LauncherAccountStore.cs`, `LocalServerSelection.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`, `tests/LocalServerSelectionTests.cs`, `tests/ClassicUiTests.cs`, `tests/Program.cs`; create focused selection/probe files as needed; update native asset generator/provenance and only necessary embedded PNGs.

**Interfaces:** Consumes legacy options/store. Produces `LauncherServerProfile` with Id/Name/Region/Host/Port/AccountServicePort/AccountServiceUrl; `LauncherOptions.Servers` and optional selected identity, preserving legacy fields. `LocalServerSelection` exposes configured choices and selected choice, `Select(id)` and `Confirm(store)` resolve a cloned in-memory target while retaining account/display configuration. Preserve compatibility of existing callers where practical; new UI lives in a partial MainWindow file if it reduces shared-file growth.

- [x] Add meaningful failing tests for no initial highlight, both groups, legacy fallback, independent two-server API endpoints, selecting versus health refresh, stale probe, cancellation, corrupt/changed saved config and unknown-field preservation. Add actual WPF hit-target/selection/long-name/footer rendering tests.
- [x] Run focused launcher checks; capture expected RED before implementation.
- [x] Implement grouping/selection, asynchronous cancellable TCP connection timing and UI from approved preview. Load original311 labels and legend with no unrelated painted checkbox. Four columns per section; overflow scrolls within section. Selected item stays highlighted even during hover/probe changes; confirmation uses the selected profile, preserves typed credentials and does not mutate unrelated saved settings.
- [x] Run full launcher check command; self-review with spec and record GREEN evidence.
- [x] Commit task files and write task report with exact commits, test evidence and interface names for Task4.

### Task 2: Original message panel and readable notifications

**Files:** Create `src/DFLegacy.Launcher/Views/ClassicMessageWindow.xaml` and `.xaml.cs`, `ClassicMessage.cs`; modify current Growl callsites in launcher `.cs`, relevant tests and asset generator/provenance; embed original314 panel and original yes-button images.

**Interfaces:** Consumes ClassicStyles/PixelText and task1 MainWindow. Produces `ClassicMessage.Show(Window? owner, string message)` usable in main and owned dialogs; same underlying UI for info/success/warning/error without generic gray Growl. Constructor/render tests can instantiate without showing blocking dialogs.

- [x] Add failing real WPF tests for native286×170 panel, readable long message scrolling, Enter/Escape and actual full confirmation hit target; pin no generic Growl in normal product paths.
- [x] Observe RED, then implement owner-centered original message panel with accessible labels, pixel text and no rectangular backdrop outside artwork. Preserve existing operation semantics, including error paths.
- [x] Run launcher checks, inspect resource/image layout and self-review.
- [x] Commit and write report; do not change update engine or publication files.

### Task 3: Verified GitHub download and recoverable two-binary installer

**Files:** Create focused `src/DFLegacy.Launcher/Updates/*.cs` and `tests/Update*Tests.cs`; extend test entry registration and `Models/LauncherOptions.cs` for update options; add minimal headless-helper dispatch in `App.xaml.cs` if necessary, coordinating integration interface in report. Do not implement update-page XAML or publication here.

**Interfaces:** `GitHubUpdateClient(HttpClient client, string repository, bool includePrereleases = true)` with `CheckAsync(..., CancellationToken)` and `DownloadAsync(..., IProgress<UpdateProgress>?, CancellationToken)` returning validated `UpdateRelease` / `PreparedUpdate`; `UpdateInstaller` prepares and runs a same-EXE helper handoff, revalidates target/package and reports `UpdateApplyResult`. Pin complete exact callable signatures in report for Task4. Update manifest: schemaVersion=1, version, commit, files array of `{path,sha256,size}`; exactly the two allowed root binaries, manifest entry `update-manifest.json`.

- [x] Write failing tests against bounded HTTP fixtures and real temporary ZIP/files/directories: release selection/stable-preview, 404/429/timeout/cancel, digest/size mismatch, safe archive whitelist/duplicates/traversal/symlink/reparse, already-current hashes, older release ID.
- [x] Observe RED; implement bounded async public GitHub client with only trusted HTTPS GitHub release URLs and no credentials; validate outer digest, internal manifest and all bytes before preparing install.
- [x] Add failing real temporary-file/process tests for parent/target identity, tampered helper plan/staging, simultaneous lock, locked DLL, failed second replace and complete rollback, preservation of Config.ini/launcher.json/DNF/resources, backup integrity and controlled restart.
- [x] Observe RED; implement same-EXE temporary helper, exclusive update lock, parent validation/wait, backups, transaction/rollback and state. Never kill player processes. No arbitrary plan files or extra update targets, no permanent helper.
- [x] Run full launcher tests, self-review security/recovery boundaries and record exact RED/GREEN commands and coverage limits.
- [x] Commit engine/tests and write concise handoff report including exact integration API and fixture process mode.

### Task 4: Startup update flow and original update artwork

**Files:** Create update WPF page/window files and focused controller; modify `App.xaml.cs`, `MainWindow.xaml(.cs)`, MoreServices menu, asset generator/provenance and tests.

**Interfaces:** Consume Task3 report's exact API and Task2 ClassicMessage. Startup asynchronously checks once with5s timeout, also manual “检查更新”. Main update artRT_BITMAP198, compact install artLauncherUpdator132, original progress249. Option default dev feed includes prereleases; support stable-only. `LauncherOptions` update configuration defaults repository to firekula/DFLegacy, preserving old config and headless behavior.

- [x] Add failing tests for startup nonblocking/no releases/error recovery, explicit retry/cancel/back, real byte progress, install-confirm handoff, game file lock and owned helper, update-state suppression of repeated install; test actual WPF progress/error/button layout.
- [x] Observe RED; implement approved two-stage update UI with actual progress/versions, automatic check/download then user-confirmed restart installation. Manual operation remains available after failures. UI thread remains responsive; closed-window callbacks ignored; helper mode dispatch before main UI/headless initialization.
- [x] Run launcher suite and actual UI inspection; use temporary installation fixtures for process handoff/restart. No deployment into user's open client. Record code/asset checks and proof of configuration preservation.
- [x] Commit and write report.

### Task 5: Client update asset and GitHub publication implementation

**Files:** Modify `tools/release/release.py`, `tools/release/ci.py`, `.gitea/workflows/release.yaml`, release tests, `src/DFLegacy.Manager/ClientPackageExporter.cs` and manager tests as needed; create focused GitHub publisher module and tests; add documentation report.

**Interfaces:** Build fixed-name updateZIP from already verified win-x64 launcher+native outputs, manifest matches Task3. Publish same version/commit on publicGitHub with prerelease rule matching Gitea metadata. Existing Gitea fiveRID/service+Docker contracts stay intact. Generated player ZIP carries update options but no release credentials or player account data.

- [x] Add failing release tests for exact update whitelist/hash/schema, excluded player data, stable/dev mapping, same-identity rerun/different-byte refusal and download verification before publish. Add manager export checks for preserved bootstrap/profile fields and enabled public update configuration.
- [x] Observe RED; implement package/publisher and gatedCI integration; optional Github credentials absent must produce explicit CI capability status, never silently claim GitHub publication. Controller configures available authorized credentials without printing secrets.
- [x] Run launcher/manager/release/Python checks appropriate to changed code and the task-scoped independent review; resolve required findings before the controller release gates.

## Controller release acceptance

- [ ] Independent whole-branch review and required fixes before merge/push.
- [ ] Controller pushes/merges using existing authorization while preserving all unrelated main-checkout dirty/untracked state; run actual Windows/Linux CI+fiveRID+Docker gates. Publish verified dedicated update asset to GitHub and check downloaded bytes and release identity.
- [ ] Verify realGitHub download→helper apply→restart in isolated client fixture, backups and unrelated config byte preservation, UI click/layout acceptance. Original DLL source unchanged; verify bundled bytes and applicable native compatibility gates from CI.
- [ ] Restore runners' paused state, keep user server running, finalize report with exact links/versions/tests/limits; mark all completed plan/ledger steps with evidence and commit docs.

## Controller self-review

All spec requirements map to local implementation Tasks1–5 and the explicit controller release acceptance gates. Shared launcher/UI/test entry files are intentionally sequential. Manifest and artifact names fixed across engine/UI/publisher. Current local baseline launcher checks passed at95b6a6d before changes. Publication and live update acceptance belong to the controller; implementer reports alone cannot certify them.
