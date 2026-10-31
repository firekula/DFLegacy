# Source review P2 follow-up

Date: 2026-10-29 (Asia/Shanghai).
Independent reviewer: map_feedback_review. Original source-review.md and reviewed-hashes.json are preserved.

## Verdict

**Original P2 resolved. Current P1: none; P2: none within the stated host lifecycle contract.** This approves the bounded source correction for continued integration/live acceptance. It does not establish live renderer/device acceptance or arbitrary concurrent DLL-unload safety.

## Independently checked correction

client_map_feedback_runtime.cpp:555–567 still requests stop and makes only an early rejection from the preliminary counts. After acquiring HookLock, the terminal expression now checks DrawHook.installed, HookPublished, ResourcesPresent and ActiveCalls together. In the exact original reported sequence—preliminary ActiveCalls0; final observer enters, restores CALL, releases HookLock but remains in Publish/Activity teardown—the new terminal ActiveCalls check sees1 and Shutdown returns false. After the observer leaves and ownership is clear, a later call may report drain. Resource ownership appearing in the preliminary-check/lock gap is likewise checked again. Code patching and texture destruction remain on the bound game thread; no worker-side native cleanup was added.

The BeforeShutdownLock seam at136 is internal, initialized null and reset null by Initialize:452. No production lifecycle assignment enables it. Tests:67–76 model the precise semantic interleaving through real RestoreHook with a fake patch operation, leaving ActiveCalls1 after hook restoration until the test explicitly drains it. Tests:262–283 exercise both callback and resource late-publication cases and subsequent successful drain. They do not pretend that a genuine concurrent engine thread or native game address was invoked.

The supplied P2-red.log contains exactly the two new assertion failures; P2-green.log contains the aggregate PASS including P2 drain interleaving. Build logs contain source compilation/link generation and no warning/error. The author manifest records RED exit2, GREEN exit0 and x86 /W4 /WX. I independently recomputed all3 assigned source hashes and all new/preserved artifact hashes from native-renderer-p2-manifest.json: zero mismatches. No build or test executable was launched by this reviewer, to avoid interfering with the root's integration run.

## Host boundary is explicit and matches the inspected current caller

client_map_feedback_runtime.h:11–15 explicitly states that true reports current hook/resource/callback drain, while the host must prevent new DebugRender entry and establish a quiescent boundary before FreeLibrary. Implementation:562–564 states the same boundary. This correctly avoids treating the lock or a counter decrement as a machine-level return/unload fence. A caller that opens fresh callbacks after the final check still needs its own lifecycle gate; the revised API does not promise that unsupported behavior is safe.

Read-only reinspection of src/DFLegacy.Ijl15/src/debug_module.cpp:136–150 confirms both FreeLibrary(candidate) sites are initialization-failure cleanup before Module/Draw publication. A successful load caches Module and Draw; Render subsequently calls the cached Draw pointer. There is no normal hot-unload/DebugShutdown caller in this host file. The current host therefore does not silently depend on a stronger hot-unload guarantee than the revised interface provides. Any future unload implementation must satisfy the documented entry/quiescence precondition.

## Scope and preserved evidence

Comparison against the original reviewed-hashes.json found changes in only client_map_feedback_runtime.h, client_map_feedback_runtime.cpp and tests/map_click_feedback_runtime_tests.cpp. The other14 previously reviewed source/spec/plan files match the original hashes. No project/ingress/pixel/timeline/draw/ABI behavior outside this correction was changed. Original source-review.md matched the author's preserved-artifact hash. Both original review files were read, and neither was rewritten.

Only the requested follow-up review Markdown and new hash manifest are written by this reviewer. No product edits, GUI/tests/MSBuild, process/service queries, deployment, live input/memory calls, save/raw-netdump reads or child agents occurred. New integrated build identity is deliberately left to the root's pending rebuild; the original B202 DLL hash is not represented as the P2-fixed build.

## Remaining acceptance and declined scope

- Live native rendering, actual patched bridge/texture allocation-lock-draw-destruction, visible two-pulse/anchor checks and unchanged original blue marker remain root-owned acceptance work.
- Device identity changes and same-pointer device-loss/reset have not been live tested. Two Observe calls remain the explicitly requested cleanup delay, not a separately proven Sprite End fence.
- Full host hot-unload implementation is outside this bounded correction. Its necessary precondition is now documented; existing IJL host has no such implementation.
- U07/backlog/monster-position work remains outside this review.

No additional actionable P1/P2 finding was identified in the follow-up scope.
