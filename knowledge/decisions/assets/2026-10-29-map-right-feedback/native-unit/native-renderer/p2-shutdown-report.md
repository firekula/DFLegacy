# P2 shutdown drain check follow-up

This addendum supersedes any interpretation of the earlier report as arbitrary concurrent FreeLibrary safety. It addresses the independent review's late-observer interleaving, while preserving the original source review and all earlier RED/GREEN artifacts.

## Verified finding and minimal fix

Previously Shutdown checked ActiveCalls/ResourcesPresent before acquiring HookLock, then based its terminal answer only on hook ownership. A final ObserveFrame could enter after the preliminary zero, restore the hook, release HookLock and remain in Publish/Activity teardown while the worker returnedtrue.

The terminal released expression now rechecks all four states together while holding HookLock: DrawHook.installed,HookPublished,ResourcesPresent,ActiveCalls. A callback or resource appearing between the preliminary check and lock therefore makes Shutdown false even if the final observer already restored the hook. The preliminary check remains only an early rejection.

The production patch does not move code restoration or native resource destruction onto the worker; both remain in the bound game-thread ObserveFrame. Existing deferred two-observation texture cleanup is unchanged.

## Deterministic regression

A null-by-default detail::BeforeShutdownLock seam executes after the preliminary checks and before HookLock. The white-box regression supplies a fake observer interleaving: increment ActiveCalls, acquire HookLock, successfully restore a fake hook, clear HookPublished, release HookLock, and deliberately leave ActiveCalls1 while the worker makes its terminal decision. It is a deterministic semantic interleaving test, not a claim that a real extra game thread was executed. A second case introduces ResourcesPresent1 in that same gap. Both use fake operations or unsupported native gates; no genuine absolute game address is called.

P2-red.cmd compiled with MSVC14.51.36231 x86 /W4 /WX /EHsc /std:c++17 /MT /utf-8 and ran before the lock-internal recheck was added. Exit2, exactly the two new assertions failed. P2-green.cmd compiled the fixed production code with the same flags, no warnings; exit0, all prior native seam tests plus both P2 regressions passed. After the simulated final observer exits or resource ownership is cleared, the corresponding subsequent shutdown succeeds.

P2 builds use new P2-red-obj/P2-green-obj directories and new P2 executables/logs. Earlier green.cmd,green_tests.exe,green-build.log,green.log,native-renderer-manifest.json,report.md and review/source-review.md were not rewritten. Their current hashes are recorded under preserved_artifacts in native-renderer-p2-manifest.json.

## Explicit unload boundary

This is a current logical drain check, not a complete lifecycle entry/exit gate and not a program-counter fence. An outer host must prevent new DebugRender/module entry and establish a quiescent boundary before acting on completion to call FreeLibrary. It must continue game-thread observations while cleanup returnsfalse, then stop further entry at a known safe boundary before unloading. The current module lock and Activity counter cannot make arbitrary concurrent FreeLibrary safe; even counter decrement and machine-level function return are distinct events.

The header now documents this caller requirement next to Shutdown, and its terminal implementation comment describes the same limit. No more-complete closed lifecycle gate or outer host unloading protocol was added in this bounded subtask.

Read-only inspection of src/DFLegacy.Ijl15/src/debug_module.cpp confirmed the current host loads once and stores Module/Draw after successful initialization. Its FreeLibrary(candidate) calls are failure cleanup paths before publishing the module. No normal hot-unload call site exists in that file. That fact limits current exposure; it is not used to claim concurrency safety.

## Scope and remaining acceptance

Only the assigned new renderer .h/.cpp and white-box test .cpp were edited. No other product file, old review/evidence, project, executable/PVF/NPK, client process or live memory was changed. No deployment, native game call or commit was performed. Root should have the independent reviewer recheck the P2 diff and this bounded unload contract before deployment. Native rendering/device/visual acceptance remains root-owned and unchanged by this fix.
