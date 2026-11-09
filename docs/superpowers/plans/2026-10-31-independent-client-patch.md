# Independent Client Patch Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship an independently loaded client patch with complete ordinary gameplay features, optional debug packages and organized client logs.

**Architecture:** A verified x86 helper starts an owned game process and loads DFLegacy.Client.dll before the game entry point, with initialization outside the loader lock. One native runtime owns player features in both normal/test modes. The launcher, Manager exporter and schema 2 updater share a fixed installed package manifest; original game binaries remain intact.

**Tech Stack:** Existing C++17/MSVC Win32/CMake, Microsoft Detours MIT source pinned at 1dbd4ea281d73d478aa8d09a8b83fbd265f715f7, .NET 10 WPF/ASP.NET, Python release tools, Gitea Actions.

**Spec:** `docs/superpowers/specs/2026-10-31-independent-client-patch-design.md` (approved).

## Global Constraints

- 支持已验证的同版本兼容展开客户端；保持其 EXE、原版 DLL、PVF、NPK 不变。原始旧壳的现代 Windows 兼容修复不在本次内。
- 普通包与测试包使用同一份核心；关闭调试导出不能停止寻路，不能重复安装 hook。
- 普通包不含、不自动加载 debug.dll；测试控制不因选择测试包而自动启用。
- 旧用户手动迁移一次；不发布新 schema 1 桥接附件；此后 V2 普通／测试包自动更新。
- 原版恢复是用户可见的一次性迁移，不能根据备份文件名猜测原版。
- 新日志进入客户端 Logs；已有文件只按明确归属归档，不能盲目搬迁 JSON/TXT、事务和控制目录。
- 单文件登录器；辅助 EXE 内嵌，根目录无额外引导 EXE；保留玩家配置。
- GitHub 源码只能沿脱敏公开历史同步，禁止推送私有 Gitea 历史。
- Do not touch real F:/ or G:/ game files, running servers, desktop input, runners, or remote repositories in implementation tasks. Controller owns authorized integration, live acceptance and publication. Scratch/builds use C:/DFLCI/standalone-client-20261031 where practical; never recursive-clean unrelated paths.

## Fixed Cross-task Contracts

- New native output filenames: `DFLegacy.Client.dll`, `DFLegacy.Bootstrap32.exe`, `debug.dll`, alongside legacy test-only `ijl15.dll` under `artifacts/native/ijl15/<Configuration>/`.
- Build properties consumed by launcher: `ClientPatchPrebuiltPath` and `ClientBootstrapPrebuiltPath`, defaulting to the paths above. Existing `Ijl15PrebuiltPath` remains for legacy regression fixtures only.
- Installed manifest name `client-package.json`; UTF-8 strict JSON camelCase: `{ "schemaVersion":2, "version":"...", "commit":"40 lowercase hex", "flavor":"normal"|"test", "files":[{"path":"DFLegacy.Launcher.exe"|"DFLegacy.Client.dll"|"debug.dll", "size":positiveLong, "sha256":"64 lowercase hex"}] }`. Exactly launcher+patch for normal, plus debug for test. No extra binary paths. Manifest itself is controlled metadata, not recursively hashed in files.
- Update ZIP contains the exact binary list plus `update-manifest.json` with the same schema. Installer derives/persists `client-package.json` from validated manifest and journals/backups/restores it in the same transaction as binaries.
- Asset names: `DFLegacy-client-update-v2-win-x64.zip`, `DFLegacy-client-update-v2-test-win-x64.zip`. Full migration tools: `DFLegacy-client-tools-win-x64.zip`, `DFLegacy-client-tools-test-win-x64.zip`, include installed manifest/checksums/instructions but no Config.ini/launcher.json/game binaries. Manager exports can still carry generated private-server connection settings.
- Native helper launch contract: `DFLegacy.Bootstrap32.exe --client <absoluteExe> --patch <absoluteDll> --sha256 <expectedPatchHash> --mode normal|test --nonce <32hex> -- <oneLauncherDataArgument>`. Native internal child handshake is task 1's responsibility, documented in `src/DFLegacy.Client/bootstrap-protocol.md`.
- Helper stdout is one bounded UTF-8 JSON line: success `{ "schemaVersion":1,"event":"ready","nonce":"...","pid":positiveInt,"createdFileTime":"decimalUInt64","patchPath":"absoluteLoadedModulePath" }`; failure `{ "schemaVersion":1,"event":"error","nonce":"...","code":"stableCode","message":"safeExplanation" }`. No credentials or full command line in output/logs. Parent confirms ownership, file identity, native success then writes `commit <nonce>\n` to stdin. Helper terminates its retained child on EOF, mismatched acknowledgement, cancellation or 15-second uncommitted timeout; valid commit releases ownership and helper exits 0. Launcher must observe exit0 after acknowledgement. Injectable DLL initialization timeout is 10 seconds; helper must not resume past initialization failure.
- Native mode is passed from helper via protected per-launch handshake, not inferred from debug.dll presence or stale Config.ini. Existing direct IJL test mode remains backwards compatible; new production path always specifies normal/test.
- Common native log API produced in task2: `bool dfl::client_paths::BuildLogPath(const wchar_t* leaf, wchar_t* output, std::size_t capacity) noexcept`, creates client `Logs/DFLegacy` and refuses non-leaf input. Managed equivalent is task3 `ClientLogPaths`.

## Review Focus

- Unicode/spaces in actual client and user profile paths must not load an unintended DLL (task1 native fixture; task3 managed integration).
- A misleading `ijl15.original.dll` containing an old patch must never be restored as original (task3 migration test).
- Leftover old debug.dll or stale test metadata must not enable diagnostics or mix versions (tasks2–4 tests).
- Power loss between metadata and binary replacement must restore the whole version/flavor (task4 transaction tests).
- Locked logs and unrelated same-directory JSON/TXT must survive migration without moving control files (task2 path policy, task3 archival tests).

## Task 1: Independent native module and owned-process bootstrap

**Files:** Create `src/DFLegacy.Client/{bootstrap_main.cpp,bootstrap_protocol.h,bootstrap_runtime.cpp,bootstrap_runtime.h,bootstrap-protocol.md,client_entry.cpp,client.def,tests/bootstrap_fixture.cpp,tests/bootstrap_tests.cpp}`; vendor pinned `third_party/detours/` source/license/provenance (no cloned .git or binaries). Modify `src/DFLegacy.Ijl15/{CMakeLists.txt,build-native.ps1,src/dnf_patches.cpp}` and create `src/DFLegacy.Ijl15/src/ijl15_entry.cpp` to retain legacy DllMain separately.

**Interfaces:** Produces fixed native filenames, CLI/stdio contract above; provides `dfl::client_bootstrap::Mode { Normal, Test }`, `Mode GetMode() noexcept`, `bool IsStandalone() noexcept` in `bootstrap_runtime.h`. Legacy callers return IsStandalone=false. Shared gameplay code remains buildable for both targets without IJL codec exports in standalone. Bootstrap dependencies are statically linked.

- [x] Add failing native fixture tests `bootstrap_initializes_before_entry_outside_loader_lock`, `bootstrap_failure_never_runs_entry`, `bootstrap_rejects_hash_architecture_and_unknown_image`, `bootstrap_unicode_path_preserves_files`, `bootstrap_parent_eof_owns_cleanup`, `bootstrap_ack_releases_only_owned_child`. Assertions include helper JSON/nonce, timestamps, controlled PID cleanup and unchanged hashes; a test DLL/EXE isolates ordering without real game assets. Production code must not gain an arbitrary-process attach or test-bypass flag.
- [x] Run new CTest filter and record expected RED failures before implementation.
- [x] Implement minimal Detours loading and entry gate, strict argument bounds, file validation, child-only lifecycle and the exact helper protocol. Use a fresh private child handshake; document its structure. Keep heavy patch setup outside loader lock and before game entry. Unknown/patched bytes fail closed with owned child teardown. Keep original IJL imports untouched.
- [x] Integrate pinned Detours into CMake, static CRT, existing /W4 /WX and reproducible build settings; output new DLL/helper, preserve legacy target and all existing test targets. Add optional build-directory argument to native script if needed, with a checked path and no automatic deletion outside owned artifacts.
- [x] Run `src/DFLegacy.Ijl15/build-native.ps1 -Configuration Release -RunTests` with process CMAKE_EXE set to `D:/DFLegacyCI/tools/cmake/bin/cmake.exe`, including new fixtures, and record commands/counts/log paths. Do not run real client or desktop tests. Commit scoped native/bootstrap changes and report exact interface decisions.

## Task 2: Shared ordinary gameplay runtime and native log paths

**Files:** Create `src/DFLegacy.Client/{player_runtime.cpp,player_runtime.h,client_paths.cpp,client_paths.h,tests/player_runtime_tests.cpp,tests/client_paths_tests.cpp}`. Modify native CMake; `src/DFLegacy.Debug/{include/debug_api.h,src/debug_overlay.cpp,src/client_state.cpp,src/client_navigation_runtime.cpp,src/client_town_route_runtime.cpp,src/client_map_feedback_runtime.cpp}`. Modify `src/DFLegacy.Ijl15/src/{performance_overlay.cpp,debug_module.cpp,ijl15_config.cpp,output_paths.cpp,d3d8_hooks.cpp,channel_reentry.cpp,ground_item_removal.cpp,mage_skill_contact.cpp,party_combat_detail.cpp,priest_heal_target.cpp,stage_follow.cpp,summon_owner_death.cpp,net_dump.cpp,auto_input.cpp}` and related focused tests/headers as needed.

**Interfaces:** Consumes task1 mode API. Produces `dfl::player_runtime::Initialize() noexcept`, `OnFrame() noexcept`, `Shutdown() noexcept` and one state/navigation ownership implementation; debug ABI may increment with explicit size/version checks and tests. Produces `client_paths::BuildLogPath` above. Public player/runtime diagnostics must not depend on Debug DLL globals; legacy test-only IJL links the same runtime.

- [x] Add RED fixtures `ordinary_navigation_without_debug_or_export_state`, `test_mode_does_not_duplicate_hooks`, `normal_mode_ignores_leftover_debug`, `test_mode_keeps_input_control_opt_in`, and path tests for Unicode, rejected traversal, directory creation, failed writes and exact original log names.
- [x] Run the focused failures and record them.
- [x] Move player state sampling, town navigation/routes and map feedback to the one core runtime. Decouple disk export from sampling; eliminate Debug duplicate OnFrame/worker/hook ownership. Keep test-only file commands gated; do not accidentally disable normal right-click while disabling command polling. Preserve all existing profiling/test observability through versioned access to shared runtime.
- [x] Route direct native log writers and render diagnostics through shared wide-char paths; identify rs_log.txt's actual writer before redirecting it. Keep original client Logs hook, screenshots and control/state channels distinct. No fallback root logs on write failure. Fix native config resolution for Unicode paths touched by independent launch.
- [x] Run full native and relevant existing Python acceptance/contract suites with built binaries; report inherited failures separately and fix regressions. Commit and report runtime/debug ABI changes for task3.

## Task 3: Launcher integration, visible migration and managed logging

**Files:** Create `src/DFLegacy.Launcher/{ClientPackageManifest.cs,StandaloneClientInstaller.cs,ClientBootstrapLauncher.cs,ClientIjlMigration.cs,ClientLogPaths.cs}` and matching tests under `src/DFLegacy.Launcher/tests/`. Modify launcher csproj, `ClientProcessLauncher.cs`, `Services/LaunchFlow.cs`, `ClientBootstrapInspector.cs`, `Program.cs`, `ClientStartupMonitor.cs`, `ClientRenderDiagnostic.cs`, migration dialog/view model using existing original-style dialog assets; change old replacement installer call sites without deleting legacy test fixtures gratuitously.

**Interfaces:** `ClientPackageManifest.LoadAndValidate(string directory)` returns typed immutable manifest; must validate exact schema/flavor/names/hashes/bounds and no duplicate JSON keys. `ClientBootstrapLauncher.StartAsync(string clientPath,string launcherData,ClientPackageManifest manifest,CancellationToken)` returns retained `Process` after ready/commit/exit0 validation. `StandaloneClientInstaller` verifies/extracts the embedded helper under `.dflegacy/runtime/<identity>/`. Native patch remains explicit root file. `ClientIjlMigration.Inspect` is read-only, `RestoreConfirmed` requires an explicit UI confirmation and transactional rollback. `ClientLogPaths.GetDirectory(string? clientPath)` implements the spec's fallback only before a valid client is known.

- [x] Add RED tests for malformed/mixed manifests, helper errors/EOF/timeout/PID reuse, wide paths, no original file writes, rejection of known packed EXE, and mapping bootstrap failure to readable original-style messages.
- [x] Add RED migration tests using actual known original IJL hash `334aa12f7dee453d1c6cb1b661a3bb3494d3e4cc9c2ff3f9002064c78404e43a`, known patch samples/fixtures and misleading backup filenames. Cancel/no-valid-backup must make zero writes. Unknown DLL must never be overwritten; never distribute original IJL bytes.
- [x] Add RED log/archive tests: exact owned patterns, same-name collisions, locked files, reparse links, unrelated configs, old diagnostic reading and unwritable target behavior. Move old logs only when owned and safe.
- [x] Implement the new startup path, preserving service/preflight/relay/display behavior and credentials privacy. Parent holds file identities through initialization and acknowledges only validated ready output. Remove production IJL replacement. If original IJL needs restoration, present candidate and consequence, backup current patch and restore only after UI acceptance; keep game entry blocked on cancellation.
- [x] Embed helper in published singlefile; include patch/installed manifest in test fixtures. Use existing classic styling for migration/errors. Route managed logs and diagnostic readers to new paths, preserving legacy read compatibility.
- [x] Run focused tests during changes; run full `src/DFLegacy.Launcher/tests` once before commit with native prebuilt properties. Include process fixture tests without desktop automation. Commit.

## Task 4: Flavor-aware V2 updates and manifest transaction

**Files:** Modify `src/DFLegacy.Launcher/Updates/{GitHubUpdateClient.cs,UpdateSafety.cs,UpdatePackage.cs,UpdateArtifacts.cs,UpdateInstaller.cs,UpdateSession.cs}` plus `UpdateTransaction.cs`, `UpdateModels.cs`, `UpdateController.cs`, `UpdateCoordinator.cs`, `UpdateHelper.cs`, and `tests/Update*Tests.cs`; use task3 manifest model rather than duplicate parsing policy.

**Interfaces:** GitHub client selects fixed V2 asset from installed manifest flavor. Update manifest matches the fixed contract; installer writes validated installed manifest alongside exact binary set and treats it as journaled transaction metadata. Public updates never read arbitrary repositories or send credentials. Existing UI/helper state machine and consent protections remain.

- [x] Add RED tests `normal_update_rejects_test_or_legacy`, `test_update_requires_same_version_debug`, `schema2_metadata_rollback_at_each_boundary`, `manual_migration_preserves_player_configuration`, `retained_debug_is_not_loaded_by_normal_mode`; test missing/hash-mismatched metadata and cancellation/restart paths.
- [x] Run focused tests to show RED.
- [x] Implement flavor selection, V2 strict extraction and installed metadata transaction; adapt fixed-file checks/cleanup/rollback to exact normal/test sets without widening paths. Keep protected player config and game assets out of the update transaction; never downgrade to V1.
- [x] Run complete update engine/UI-boundary/helper fixture suites and full launcher tests, preserving previously fixed Enter/decline consent behavior. Commit.

## Task 5: Manager checkbox, release packages and CI wiring

**Files:** Modify `src/DFLegacy.Manager/{ClientPackageExporter.cs,ManagerApplication.cs,wwwroot/index.html,wwwroot/manager.js}`, `tests/DFLegacy.Manager.Tests/Program.cs`, `tools/release/{release.py,github_publish.py,tests/test_release.py,tests/test_github_publish.py,tests/test_ci.py,tests/test_bundle.py}`, `.gitea/workflows/release.yaml`, relevant deployment docs/README and build-assets project wiring.

**Interfaces:** `ExportAsync(string kind,DeploymentConfig config,bool testBuild=false,CancellationToken cancellationToken=default)` (adjust named calls to avoid cancellation ambiguity). `/api/export/client` accepts optional JSON bool `testBuild`, default false; old aliases return normal. client-assets bundle carries `patch/DFLegacy.Client.dll`, optional `patch/debug.dll`, default INI and launcher with embedded helper, all same source version. Release builders produce fixed installed/update manifests and V2/tool asset names; no new old-name V1 ZIP.

- [x] Add RED Manager tests for defaults, test mode, invalid bool, missing optional debug (ordinary works/test fails), mixed version/hash, privacy, preserved root layout and no ijl15. Add release tests for four new ZIP assets, exact manifests/modes, no player config in GitHub tools, paired debug version and absence of legacy update asset.
- [x] Run focused tests and record RED.
- [x] Implement checkbox in existing download card with text from spec; ensure export request captures the user's chosen bool. Package only flavor's required files after immutable-release validation. Update README to clearly explain one-time manual migration, packed limitation and local-only server overlay data.
- [x] Update native/client-asset build CLI and CI arguments; build/package/test both flavors on Windows, consume verified assets for Linux/macOS server bundles, publish schema2/tools to GitHub only after existing same-source gates. Maintain public source snapshot mapping and no secret-bearing config/history changes. Update archive inventory validation rather than hardcoded wrong asset counts.
- [x] Run Manager tests and `python -m unittest discover -s tools/release/tests -v`; validate fixture packages with actual launcher parser. Commit.

## Task 6: Integrated acceptance and delivery evidence

**Files:** Add focused acceptance fixtures/scripts under `tools/acceptance/` for standalone install/start/update and log preservation; update relevant existing native acceptance fixtures for shared ownership. Record results in `docs/superpowers/reports/2026-10-31-independent-client-patch-acceptance.md` and mark plan checkboxes only against evidence.

**Interfaces:** Consumes all previous contracts and real built binaries; never changes policy to make tests pass. Controller handles real clients, review, merging, remote publication, runner management and sanitized GitHub source export.

- [ ] Add missing end-to-end fixture assertions for original bytes unchanged, ordinary navigation without debug, test same-ABI loading, stale DLL rejection, manual migration and V2 update/rollback preserving account/config/resources. Confirm required fixture failures against prior behavior where meaningful.
- [ ] Run native build/CTest plus legacy regression tests, full Windows launcher tests, smoke/Manager/release tests and required acceptance suites. Use owned scratch paths. Review unresolved failure logs instead of repeating broad successful suites without changes.
- [ ] Controller: obtain final independent whole-branch review; fix findings via owning implementers and verify. Perform isolated live startup/real gameplay acceptance once desktop operation is explicitly resumed, never treat 15-second spike as gameplay pass. If desktop stop remains, ask only for that operational continuation while completing all background checks.
- [ ] Controller: preserve user's dirty main checkout, integrate authorized tested branch without force, push Gitea, monitor exact-source CI all RIDs/Docker, verify downloads and public updater packages. Export sanitized current tree atop public GitHub history and verify mapping; never push Gitea private history to GitHub.
- [ ] Controller: final public package roundtrip, relevant real-client acceptance, running service preservation and runner-state ownership checks; write truthful acceptance report and publish completion only when required gates pass. Do not clean another plan's fixtures/ledger. Commit final documentation with [skip ci] as appropriate.

## Self-review / Execution Ruling

All approved spec sections map to tasks1–6; native helpers precede managed consumers, mode and metadata use the one fixed contract, release tools come after consumers. Old-shell support is explicitly rejected, not silently substituted. UI redesign is outside scope except existing-style migration dialog and one Manager checkbox. No plan-approval pause: user explicitly authorized implementing, testing and publishing this reviewed design; task execution method is a routine implementation choice under that instruction. Use serial SDD task gates, not parallel implementers.

## Task 7: Empty ordinary new-character defaults (added user bug fix)

**Authority/order:** User explicitly requested new characters without the title, 50,000 gold or consumables. Execute after task2's review gate, before final task6 acceptance/publication; task numbers remain stable. No new design approval gate.

**Files:** `src/DFLegacy.Server/{JsonGameStore.cs,EntranceService.cs}`, focused `tests/DFLegacy.SmokeTests/NewCharacterDefaultsSmokeTests.cs`, registration/existing assertions in `Program.cs`, and only affected test fixtures that depended on implicit free possessions.

**Evidence:** `CharacterRecord` has a 50,000 Gold optional default; `CreateCharacterLocked` omits Gold and Inventory. First selection uses `character.Inventory ?? CreateStarterInventory(accountUid)`; ordinary starter list grants title26043 and item8x100. Explicit test-account presets are a separate intentional testing facility. Account Cash is shared, so creating a character must not erase an existing account balance.

- [x] Add RED coverage for both ordinary character creation overloads, persistence/reload, and first selection/inventory initialization: zero Gold, no equipment/title/consumable/main/avatar/creature/warehouse gifts. A manually created new character on a test account must also start empty; keep the explicitly generated test preset stocked. Cover existing funded characters/items and existing shared account Cash preserved. A newly registered ordinary account starts without the legacy free account Cash; test-account preset balances remain explicit.
- [x] Set explicit empty/zero values at ordinary creation boundaries, avoiding broad changes to deserialization defaults of old records. Remove ordinary fallback gifts while preserving explicit test-preset setup. Existing saved possessions are never cleared or migrated away. If tests need money/items, seed their own fixture rather than retain production gifts.
- [x] Run new focused tests RED then GREEN, affected fixture suites and complete server smoke suite once after fixes. Commit only scoped source/tests and report counts/evidence.

## Task 8: Keep the selected backpack tab (added user bug fix)

**Authority/order:** User explicitly requested Sort and close/reopen to retain the last chosen main backpack tab. Execute serially after task7, before final task6 acceptance/publication. This belongs to ordinary native features, available without debug.dll.

**Files:** Focused native module/tests under `src/DFLegacy.Ijl15/`, shared CMake/project wiring and patch application as needed; a bounded acceptance fixture under `tools/acceptance/`. Do not alter inventory authority, item slots, sort results or protocol semantics just to hide the UI symptom.

**Read-only recon:** `C:/DFLCI/inventory-tabs-20261031/` contains output from an owned copy of the compatible client's IDA database. `noti13raw.txt` shows NOTI13 full refresh at0x41D01C clearing the model, then `push 1` at0x41D1FB passed to `sub_45E280` at0x41D212. `tabset-callers.txt` shows that flag invokes auto-selection `sub_7AAAF0` for main inventory. `tabraw.txt` shows it writes window+0x3C according to the last inserted slot, explaining material-tab jumps after server CMD20 sends a sorted NOTI13 list. Main-window initialization0x7AA740 defaults tab0 once; direct tab click writes at0x7AAFAA and drag-related selection at0x7AAE45 are distinct. `tab-writes.txt` inventories accesses; opening/closing path still needs bounded confirmation, not an assumed separate reset. Raw decompilation and client binaries stay private.

- [x] Finish the narrow call-chain check for show/hide and any hidden-window refresh affecting tab memory. Establish whether suppressing full-refresh auto-selection alone covers both reported cases; prefer the smallest proven change. Do not disable manual tab clicks or useful drag behavior.
- [x] Add a failing native executable fixture exercising the actual compatible x86 refresh/call contract with deterministic fake inventory/UI state. Cover all four tabs, populated mixed categories ending in Materials/Quest, empty list, repeated sort, close/reopen with intervening refresh, first-open equipment default, manual switch still effective, and unaffected other inventory spaces. Validate supported instruction bytes; unknown/already modified sites must be safe and not partially patched.
- [x] Implement the proven scoped native correction in the common core so normal and test modes behave identically. Keep original files unchanged on disk. Verify fixture RED/GREEN plus existing native regression suite; record exact site/signature and evidence. Runtime integration must not require diagnostic exports or auto input in normal mode.
- [x] Controller later performs real-client sort/reopen acceptance once physical UI operation resumes; fixture results must not be described as that manual/gameplay check. Commit scoped correction/tests.

**Reviewed result:** NOTI13-only suppression left hidden NOTI14 insertions and stack increases able to change the tab. The final correction guards the 13-byte selector tail at `0x7AAB4D` and suppresses only the three-byte automatic tab store at `0x7AAB52`; manual/drag writes and first-open initialization remain intact. Automatic synchronization retains the chosen tab whether visible or hidden. The additional partial-update fixture failed 36 checks before the correction and passes afterward; final coverage executes 234 insertion calls, 32 amount-update calls and 273 native selector lookups. Initial native regression passed 35/35; the scoped correction passed 2/2 focused tests and all three acceptance scenarios. Physical UI operation has now resumed; live-game acceptance remains pending the new launcher.
