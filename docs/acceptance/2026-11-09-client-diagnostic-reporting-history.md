# 较早阶段的完整实施与验收记录

以下为截至 `71034f2` 的历史阶段记录，文中的 pending／`.5` 限制是当时状态。最终 `.6` 结果以[当前验收报告](2026-11-09-client-diagnostic-reporting.md)为准。保留原始 RED/GREEN、失败、更正、成本和范围，不把早期快照改写为最终产物。

# Task 4 implementation / packaging / acceptance report

Status: DONE_WITH_CONCERNS — source/integration work ready for Task4 review; final artifact stage REQUIRED AND PENDING after independent task review, root whole-branch review and grouped corrections. Earlier .5 remains an immutable intermediate snapshot, not the final corrected delivery. Worktree `D:/codex_worktree/manager-export-release/DFLegacy`, branch `codex/client-diagnostic-reporting`, task baseline `aa7692237a12072e68dbe378a5352bdf17bf98fc`.

## Implemented

- CI publishes each Diagnostics helper as an independently self-contained single file, alongside the existing single-file launcher; no installed runtime dependency or extraction of host internals. Win32 CrashCapture is copied from the native build. `bootstrap_package` requires both fixed public helper names, checks their PE architecture (Diagnostics matches launcher; capture stays x86), retains fixed-entry byte identity, and permits historical bootstrap receipts lacking the new helpers.
- Legacy client-assets uses exact public selection rather than sweeping publish-directory private siblings. Both architecture helpers are verified as self-contained .NET bundles. New schema-2 output includes both helpers; readers keep old releases readable and reject an incomplete helper pair. Manager chooses the correct architecture asset, emits both helpers and records their installed hashes. Offline packager defaults include both fixed helpers. Existing GitHub publication source consistency checks now group Diagnostics by architecture, as they already group Launcher; no GitHub operation occurred.
- Python V4 selection and managed PathPolicy reject diagnostic/session/capture/queue/events/installation directories, preference/settings/session/closed/drops metadata and derivatives, dumps/logs, including copied nested data. A public executable's filename does not require allowing the private Diagnostics directory.
- AutomaticDiagnostics retains exact Process handles from its own starts. Update coordinator suspends own sessions before file-owner inspection, controller independently suspends before preflight, waits at most 12s against those retained handles, and preserves existing lock checks. Live game identities prevent stopping their native capture lifetime. No process-name/PID search or termination was introduced. Failure/cancellation restores reporting; errors are reported after resuming so the failure itself is retained in the new queue.
- Complete reuses only an already valid same-capability marker, serializes same-instance close calls, and repairs wrong/malformed markers. This avoids redundant atomic replace while a reader holds the close marker. Closing native-game capture is still excluded.
- Bounded first-display-adapter registry reads fill available driver version and capacity. No WMI command, serial identifier, full inventory, or invented actual game display state. Actual OS projection returned driver `32.0.21030.2001`, GPU capacity `17163091968`; gameResolution/windowMode/renderer/adapter/dpi remain unknown. Collector processArchitecture and OS uptime labels are explicit. No claim that PVF hash is automatically uploaded was added.
- README, CHANGELOG, CLIENT_UPDATES_V4, DEPLOYING, BUILDING, design sentence, actual export instructions and illustrated acceptance report were updated. No formal version bump.

## Scope rulings applied

The handoff's self-contained helper ruling supersedes runtime-sharing optimization. x86 helper raw size 69,183,411 bytes; x64 73,652,147 bytes; native capture 212,480 bytes. The larger helper cost is disclosed. V4 still downloads only changed managed files.

Offline missing/corrupt managed helper remains blocked by trusted-manifest integrity; online repairs remain available. Runtime helper failure after a valid installation stays fail-soft. No corrupt helper was executed.

Real LocalAppData already contains user history. Packaged `--repair-client` / `--self-test` would reach RegisterDiagnosticSecrets -> Configure and create real-user registrations, so those were not invoked. Root approved actual non-session packaged entry checks and real repair control path through architecture-specific Launcher.Tests hosts with injected user roots. No production root override was added. Full packaged CLI session E2E remains unverified and is clearly distinguished from fixture/core and real remote repair evidence.

Parent hidden UI server/POST was automatically rejected generic blocked-by-policy before this task. No reroute through another tool/script, no CUA session, no invented screenshot. The report contains a labeled SVG architecture/evidence diagram. Interactive GM walkthrough remains unverified; real own-fixture Kestrel HTTP workflow evidence is retained.

## TDD and focused checks

All commands ran from the worktree; outputs/builds on C and large public fixture/artifacts on G.

Launcher command prefix used throughout:

```powershell
dotnet run --project src/DFLegacy.Launcher/tests -c Release --artifacts-path C:/codex-build/client-diagnostics-20261109 -p:BuildNativeAssets=false -p:ClientPatchPrebuiltPath=C:/codex-build/client-diagnostics-20261109/native/Release/DFLegacy.Client.dll -p:ClientBootstrapPrebuiltPath=C:/codex-build/client-diagnostics-20261109/native/Release/DFLegacy.Bootstrap32.exe -p:Ijl15PrebuiltPath=C:/codex-build/client-diagnostics-20261109/native/Release/ijl15.dll
```

1. `python -m unittest discover -s tools/release/tests -p test_bootstrap_package.py -v`: RED `task4-package-red.log`, 18 tests / 5 failures, including missing-helper requirements, wrong architecture acceptance and `Diagnostics/session.json` admitted. GREEN `task4-package-green.log`, 18/18. Added real four-variant helper inventory and exact legacy private-sibling exclusion tests.
2. Launcher selector `-- --diagnostic-lifetime-tests`: initial scaffold exposed missing APIs (not counted as behavioral RED). With suspension call omitted, behavioral RED `task4-lifetime-behavior-red.log`: 8 assertions / 4 failures, including actual locked preflight, retained helper exit and resumed process/reporting. Restored call; focused GREEN later expanded to 12 assertions / 0 failures (`task4-lifetime-final.log`), using `--artifacts-path .../final-validation` to avoid shared managed build outputs.
3. Deterministic valid locked close-marker test before idempotence: RED `task4-close-idempotency-red.log`, `FAIL: repeated own-session completion never replaces an already valid close marker`, 10 assertions / 1 failure. Final GREEN above additionally checks four concurrent same-session callers and wrong/malformed marker repair.
4. Self-review exposed error projection after shutdown. Earlier RED showed `handoff failure is retained by the resumed diagnostic session` failing; immediate queue observations also raced the independent collector. Final test uses bounded eventual queue assertions, resumes before reporting, and passes the actual failure-stage plus later incident checks. `task4-resume-error-red.log` and `task4-resume-error-behavior-red.log` retained; the latter also hit the old 20s test cleanup bound, so it is not presented as pristine final behavior evidence. Fixture bound was raised to 60s; production 12s/10s limits did not change.
5. `dotnet run --project tests/DFLegacy.Diagnostics.Tests -c Release --artifacts-path ... -- --native-integration-only`: hardware RED `task4-hardware-red.log`, 14 assertions / 1 failure (driver/memory keys absent). Latest full Core GREEN 87/0. Read-only actual sanitized projection is in `task4-hardware-observed.json`.
6. `-- --update-v4-tests`: `task4-v4-green.log`, 44 assertions / 0 failures, including same-version helper repair, offline integrity block and managed-reader private path rejection.

## Final broad verification and honest intermediate failures

| Command / evidence under C:/codex-build/client-diagnostics-20261109 | Result |
| --- | --- |
| Latest production source launcher test executable `final-validation/bin/DFLegacy.Launcher.Tests/release/DFLegacy.Launcher.Tests.exe`, DFL_NATIVE_FIXTURES=repo/artifacts/native/ijl15 and DFL_LEGACY_PATCH_ARCHIVE=repo/artifacts/test-fixture-cache/DFLegacy-client-update-win-x64.zip | `task4-full-Launcher-final-source.log`: 2097 assertions passed |
| `dotnet run --project tests/DFLegacy.Diagnostics.Tests -c Release --artifacts-path ...` | `task4-full-DFLegacy.Diagnostics.Tests-current.log`: 87 assertions, 0 failures; existing Windows file-symbolic-link privilege SKIP |
| `dotnet run --project tests/DFLegacy.UpdateCore.Tests -c Release --artifacts-path ...` | `task4-full-DFLegacy.UpdateCore.Tests.log`: 702 assertions passed |
| `dotnet run --project tests/DFLegacy.Manager.Tests -c Release --artifacts-path ... -- --http` | `task4-manager-final-source.log`: 174 checks, 0 failures |
| `python -m unittest discover -s tools/release/tests -v` | `task4-python-final-source.log`: 150 tests OK, 89.139s, build source metadata b4fa376 |
| `python -u src/DFLegacy.Ijl15/tests/test_native_capture.py .../native/Release/native_incident_tests.exe .../native/Release/native_capture_helper_fixture.exe` | `task4-native-full.log`: all 24 modes PASS including concurrent global budget, delayed recovery twice and skip-provisional |
| Actual repo-relative smoke executable, following previous handoff fixture discovery workaround | `task4-full-DFLegacy.SmokeTests-observed.log`: All DFLegacy protocol smoke tests passed |
| Same smoke executable `--client-diagnostics` | `task4-server-diagnostics-final.log`: 0 failures, real Kestrel listener/role/format/restart/receipt/status/filter/download/delete/quota checks |
| `verify_client_notices.py --native .../native/Release --launcher .../publish/win-x64/launcher/DFLegacy.Launcher.exe --launcher-x86 .../publish/win-x86/launcher/DFLegacy.Launcher.exe --packages .../legacy-utc/packages` | `task4-packaged-notices-final.log`: 0 failures, exact notices, invalid config bypassed, no configuration/log writes, six actual package inventories/hashes |
| Same notices script with x86 launcher and `--launcher-rid win-x86` | `task4-packaged-notices-x86.log`: 0 failures |
| Actual self-contained Diagnostics.exe `--invalid`, x86 and x64 | both exit 2, stdout/stderr empty; parser returns before opening any session |

Initial full release tests found expected changed inventory assertions, a mocked-CI copy setup omission, and the new helper's architecture grouping in github_publish. Those were corrected; full 150 suite passed. Manager test project needed existing V4/Diagnostics references for its linked LauncherOptions/ClientPackageManifest; added both. No fixture assertion was weakened; helper inventory expectations were strengthened.

Initial direct-C full smoke could not parent-walk to MentorGold fixtures. Correct setup: copy C-built outputs to `.tmp/task4-client-diagnostics-smoke/bin/Release/net10.0`, then invoke the DLL with dotnet (no D build). That full run completed with one unrelated skill-slot rename UnauthorizedAccessException. One isolated `--skill-slot-persistence` rerun passed. Root authorized only test failure observability (`exception.ToString`, preserving behavior/assertions), then one full-context rerun; it passed. Original failure `task4-full-DFLegacy.SmokeTests-final.log`, isolated `task4-skill-slot-classification.log`, observed full pass above retained. Its root cause remains unknown; no gameplay/save code was changed.

A separate concrete lifecycle stack was found in rich local test logs: UnauthorizedAccessException -> DiagnosticFiles.Write -> DiagnosticSession.Complete -> SuspendForUpdateAsync on redundant close-marker replacement. That is the idempotence fix above, not an inferred explanation for the unrelated smoke transient.

Architecture-specific self-contained test-host publishing reports IL3000 for pre-existing Assembly.Location usage in other test selectors. The opt-in release selector does not execute those paths. This is test-host-only warning, not claimed pristine output. Physical Windows10 or 32-bit OS acceptance was not performed (actual host build26300 x64 / .NET10).

## Actual artifacts / publication / integration

Production build source commit: **b4fa376e781f3d157e56d206eb6241774a8bd8e4** (`feat(client): package diagnostic helpers and coordinate update lifetime`). Native C outputs come from reviewed Task3 baseline aa7692237a12072e68dbe378a5352bdf17bf98fc; no native implementation changed here.

Commands/scripts (private, no credential literals):

```powershell
& C:/codex-build/client-diagnostics-20261109/task4-build.ps1 -Commit b4fa376e781f3d157e56d206eb6241774a8bd8e4
python C:/codex-build/client-diagnostics-20261109/task4-bundles.py b4fa376e781f3d157e56d206eb6241774a8bd8e4
python -u C:/codex-build/client-diagnostics-20261109/task4-publish-gitea.py publish5
python C:/codex-build/client-diagnostics-20261109/task4-legacy-utc.py
```

`task4-build-bundles.log` verifies four V4 bundles/starters and six legacy ZIPs. Early unpublished legacy dev tag used local-clock text with a Z suffix; it was corrected in a fresh `legacy-utc` output to `dev-20261109T035128Z-b4fa376e781f`. Earlier outputs were preserved, not silently overwritten. One fresh legacy stage rename was denied by transient external Windows file access; rerunning the complete immutable builder in its still-uncreated target succeeded. `task4-legacy-utc-final.log`: six verified packages. No published bytes were changed.

Public release [v0.1.8-giteatest.5](http://firekula.site:3000/firekula/DFLegacy/releases/tag/v0.1.8-giteatest.5), ID3720, prerelease true. Publisher used draft, verified all grouped resources/manifests, then anonymous full/hash/size and Range checks. `task4-gitea-publish.log` ends `published-verified`, variants4. Existing public tag target **aca790d0070da5c6e0d354c649c3355e9b79afc3** is deliberately distinct from actual build source SHA. No source push, main merge, GitHub/GHCR or formal release occurred. Auth used established exact-provider git credential helper -> memory-only ScopedBasic, cleared in short-lived process; no credential/header/stdout was printed or persisted in assets.

Artifact base: `G:/dnf_local/DFLegacy_Diagnostics_Artifacts_20261109`.
- `build/v0.1.8-giteatest.5/{x86,x64}-{normal,test}/{source,bundle}` and `starters/*.zip`.
- `legacy-utc/client-assets`, `legacy-utc/packages` (six verified schema2 archives).
- `handoff.json`: actual build/tag target, variant source/bundle paths, manifests, starter hashes, binary hashes, private symbol availability/provenance. Native Release production PDB was not emitted; managed symbols use existing embedded CI setting. Two retained standalone PDB hashes belong to presenter tests, explicitly not matched production symbols.
- `remote-verification/v0.1.8-giteatest.5.json`: published verified receipt; created.json records initial draft state only.
- `legacy-utc/manager-export-results.json`: actual Manager ExportPairAsync-generated four variants, exact helper SHA256, no runtime data, API8081/GM18081 preserved. Command `dotnet run --project C:/codex-build/client-diagnostics-20261109/manager-export-probe/ManagerExportProbe.csproj -c Release --artifacts-path .../manager-probe-artifacts`; final log `task4-manager-actual-export-final.log`.

Starters: x86 normal243956168 / test244119112 bytes; x64 normal259157268 / test259320218 bytes. Full starter includes duplicate recovery launcher/updater content; these totals are not represented as differential download size.

## Four actual Gitea repair variants

Added opt-in DiagnosticReleaseAcceptanceTests and `--diagnostic-release-acceptance <spec>` in existing test host. It seeds exact `.4` public files/receipt in newly allocated G roots, points all account/diagnostic profiles to isolated loopback, injects its user root and invokes the production RepairClient.RunAsync (not a reimplemented engine). Hosts were separately published self-contained x86/x64 to C (`task4-release-host-build-*.log`). Real provider remains anonymous pinned Gitea.

```powershell
& C:/codex-build/client-diagnostics-20261109/release-test-host/win-x86/DFLegacy.Launcher.Tests.exe --diagnostic-release-acceptance C:/codex-build/client-diagnostics-20261109/task4-release-spec-run02.json
& C:/codex-build/client-diagnostics-20261109/release-test-host/win-x64/DFLegacy.Launcher.Tests.exe --diagnostic-release-acceptance C:/codex-build/client-diagnostics-20261109/task4-release-spec.json
```

Both architecture logs (`task4-real-gitea-x86-final.log`, `task4-real-gitea-x64.log`) end **10 assertions / 0 failures**. Per variant: real .4->.5 upgrade; current-user disabled choice survives copied enabled default; delete Diagnostics and its full/part cache objects, then refetch exactly 69,183,411 bytes x86 or73,652,147 x64; disabled-network missing managed helper blocks; re-enable and final complete manifest verification/no pending transaction passes. Results persist at `G:/dnf_local/DFLegacy_Diagnostics_Release_Test_20261109/run-02/x86-results.json` and `run-01/x64-results.json` with actual manifest and helper hashes.

First x86 run normal passed but test flavor was correctly blocked at actual file-lock preflight by QQPCMgr RTP Service. No termination/bypass. Test harness added bounded wait/retry of the entire real repair call, retaining every lock check, and used new run02 instead of overwriting run01. Full x86 run02 passed. x64 run01 completed both flavors without needing retry.

## Protection checks / limitations

`task4-original-protected-hashes.json`: all7 original protected files match baseline. `git ls-remote origin refs/heads/main` remains aca790d0070da5c6e0d354c649c3355e9b79afc3; GitHub main remains e85dd6aba5d29da63360f1e621568249ec0f4d31. No Chapter2, production service/config/save or registry operation. No registry digest or online save hashes were measured; no such claim is made. Original client was read only for hashes, never launched/modified.

New receiver/helper never uploads raw dumps. Historical original BugTrap transport suppression remains scoped to verified ACT1 hashes; unknown modules and historical combat/CrashSender failure reproduction remain outside proven scope. Actual renderer/display stays unknown. Interactive GM screenshots/full packaged session CLI E2E remain unverified for reasons above. Root final whole-branch review still owns cross-task findings (including optional PVF wire projection gap).

## Files / self-review / commits

Production implementation and root docs: 34 scoped files in b4fa376 (`git show --stat b4fa376`). Includes release.py/bootstrap_package/update_v4/ci/github_publish, Manager exporter/help, offline defaults, managed path policy/helper lifecycle, limited hardware, schema2 parser, regression fixtures and root docs/design amendment. Follow-up acceptance commit adds only the opt-in release test host selector/class and illustrated report/SVG; production `.5` build SHA stays b4fa376.

Self-reviewed default Configure -> controller/coordinator preflight ordering, owned handles, cancellation/resume/error projection, marker capability/malformed handling, live native exclusion, old package compatibility, helper architecture mapping, release private path inventory, config migration and actual variants. `git diff --check` passed; source commit/next acceptance commit checks recorded below. Three unrelated untracked paths were retained (ACT1_LOCAL_ACCEPTANCE.md and two pre-existing __pycache__ trees). No subagents were dispatched.

Final acceptance commit and fresh final-source Python result are appended below after completion.


## Named integrity correction before task review (post .5 snapshot)

Root correctly identified that default Configure starts before managed-file validation. The .5 source b4fa376 only checked presence/NoLinks, so an otherwise valid but wrong-hash executable could reach Process.Start. No .5 bytes were overwritten and no new interim label was published.

Local correction: default helper startup now reads the installed V4 receipt first; V4 presence forbids legacy fallback even if it omits the helper, and malformed receipt fails closed. Otherwise only a valid schema-2 helper entry can authorize it. Helper size and SHA256 are verified before process creation, with the image held FileShare.Read (no write/delete sharing) through Process.Start. Absent metadata, wrong hash/size, malformed metadata or unmanaged helper do not execute. Existing runtime fail-soft session/local recording and trusted online repair remain. Internal test-only directory/creation seams do not add any production option or CLI root override.

Named regression command uses the earlier launcher prefix with `--artifacts-path .../final-validation -- --diagnostic-helper-integrity-tests`. `task4-helper-integrity-red.log`: **11 assertions / 9 failures** before the guard. A real valid executable image was paired with wrong manifest hash while creation was intercepted, so no known mismatched executable was actually executed during RED. Final `task4-helper-integrity-green.log`: **13 assertions / 0 failures**, covering absent trust, legacy wrong hash/size, matched legacy, V4 precedence vs valid legacy, matched V4, missing helper management, malformed V4/legacy. Both matched positive controls actually start a benign own child while the checksum handle denies writes/replacement. No real user diagnostic data is read or mutated by those controls. Post-guard actual lifecycle still 12/0 (`task4-lifetime-post-integrity.log`). Latest full launcher post-guard rerun: `task4-full-Launcher-integrity-final.log` (result appended after completion).

Root explicitly deferred ClientVersion source identity and optional wire PVF hash cross-task review findings to grouped final review corrections; neither was silently claimed as solved here.

## Required final artifact phase — NOT SKIPPED

After Task4 source/integration review, root whole-branch review and all grouped code corrections, root will resume Task4 for ONE final immutable new Gitea test label (inspect availability then), final x86/x64 normal/test bundles, legacy/Manager/offline inclusion/privacy checks, and all four real Gitea repair/update checks from injected test roots. Do not republish intermediate labels repeatedly or overwrite .5. Record final actual build SHA separately from public tag target again.

Final corrected native build MUST preserve optimized Release behavior while generating and privately retaining PDBs precisely matching the project's own production DLLs: PE CodeView/PDB GUID + Age, DLL/PDB SHA256, and exact source/build SHA. They must not enter manifests/client-assets/Gitea. Existing presenter-test PDBs are not substitutes; original DNF lacking symbols is a separate constraint. Current .5 production native PDB absence is a pending requirement, not an accepted permanent waiver. Any necessary native build-config correction is collected with final review fixes, per root ruling.

Known intermediate .5 source snapshot: b4fa376. Acceptance test/illustrated report commit: **78da3df — test(client): verify isolated diagnostic release and document acceptance** (4 files). Current local integrity-fix commit will be recorded below. The report and illustrated acceptance document clearly distinguish this local source from .5's published earlier bytes.


Latest post-integrity source verification: `task4-full-Launcher-integrity-final.log` ends **Launcher configuration tests passed (2110 assertions)**. Focused helper trust guard **13/0** and post-guard owned helper/update lifecycle **12/0**. No production code changed after these checks; later edits only explain snapshot/final-artifact/symbol requirements. `task4-python-final-source.log` fresh run was **150 tests OK in 89.139s**. The final whole-branch build/publish remains explicitly pending root reviews, per workflow ruling.


Local integrity correction commit: **71034f2 — fix(client): verify diagnostic helper before startup** (6 files: guard/seams, actual valid-image regression/child marker selector, V4/BUILDING/illustrated report snapshot-and-final-stage documentation). `git show --check --oneline HEAD` and staged diff checks exit0. Final status contains only the original three unrelated untracked paths. No post-guard intermediate publication occurred. Final artifact phase and private matching production symbol requirement remain required pending review, not silently waived.
