# Source review — N地图右键黄色双扩散

Date: 2026-10-29 (Asia/Shanghai)
Reviewer: independent map_feedback_review agent, requesting-code-review workflow.
Base: 4825b825. Review target: current uncommitted source; exact SHA256 values are in reviewed-hashes.json.

## Verdict

**P1: none. P2: one shutdown concurrency issue below.** Source review is otherwise aligned with the spec/plan. This is not a live-rendering PASS or a completed device-transition acceptance.

## Strengths and checked behavior

- Right-only ingress: client_navigation.cpp:148/237 and client_navigation_runtime.cpp:214/225 restrict dispatch to native right release16 after original traversal; original left traversal stays intact. Canvas metadata is published only after existing capture/menu/plane/root rechecks. AcceptGoal follows successful BeginCanvasGoal.
- map_click_feedback.cpp:7/48/60 implements exactly two finite 1750ms curves, offset200ms and total1950ms, replacement, visibility/identity/time cancellation. Stable user/context/minimap/plane binding deliberately permits same-character normal actor/map changes.
- Original1936 source pixels in map_click_ring_pixels.inc match native-contract/native-player-ring-index7.argb1555 byte-for-byte (3872bytes). Yellow conversion preserves A1 and maximum RGB5 intensity. No runtime NPK/PVF/resource file write is present.
- Native x86 boundaries match the read-only contract: malloc91857A cdecl(90h), ctor8E16A0 thiscall(this,14,device,sprite) RET0C, create8E1730 RET8, lock/unlock8D3380/8D33C0, deleting destructor8E1750 RET4, raw free919D7D, FrameDraw82C160 cdecl5args. Direct allocation/ctor avoids the shared factory tracking chain. All27 exported function binaries matched their manifest SHA256; inspected ctor/create/lock/unlock/delete/frame draw/render-state assembly agrees with the typedefs.
- BuildTexture:60 retains failed-cleanup ownership, pairs successful lock with unlock, respects row pitch, suppresses partial publication. Original source marker/animation/texture/template are read only. DrawScoped:82 copies descriptor locally, substitutes a private texture and click-centered offsets, and reencodes both protected scales using their final local field addresses. Both RS19/20 restoration attempts execute after successful draw, false result, or SEH caught at the native leaf.
- DrawBridge:410 calls the original tree draw first exactly once and preserves full32bit EAX; AfterDraw:367 permits appended rendering only for the current N-map root. No extra Sprite Begin/End is introduced.
- Third CALL installation requires Nav Installed&&HitInstalled from navigation_runtime.cpp:383 and checked signatures. NativePatch:412 independently checks the bound game thread; failed restore retains hook ownership. Shutdown false preserves Nav.Enabled because navigation_runtime.cpp:400 returns before disabling it. Drawn texture cleanup waits for the second stop Observe; undrawn transaction failures can clean immediately.
- Publish:180 only tries a publication lock; no wait/file IO occurs in added frame/draw/accept paths. JSON assembly and file writes are in PumpWorker:517. Device/system/surface/helper identity mismatches stop drawing and request deferred retirement.
- Projects include the new production sources and white-box test translation units without separately linking runtime.cpp into the test executable twice. --map-feedback registration is present.

## P2 — Shutdown can return true while an ObserveFrame callback is active

File: src/DFLegacy.Debug/src/client_map_feedback_runtime.cpp:556–559, interacting with :475 and :487–492.

The worker reads ActiveCalls before acquiring HookLock, but game-thread ObserveFrame can begin between that read and the hook-ownership check. A concrete allowed interleaving is:

1. worker Shutdown:556 reads ActiveCalls=0, ResourcesPresent=0 (e.g. hook installed but no texture was created);
2. game thread enters ObserveFrame:475, increments ActiveCalls and follows StopRequested cleanup;
3. ObserveFrame:488–491 restores the third CALL, clears HookPublished, and releases HookLock;
4. worker Shutdown:557–559 acquires the lock, observes both ownership flags clear and returns true;
5. ObserveFrame still executes Publish at492 and its Activity destructor/function return in this DLL.

The caller can therefore receive approval to unload while the cleanup callback still executes DLL code. The current in-flight unit assertion sets a nonzero counter before Shutdown; it does not cover this transition. The same stale-check shape matters if resource cleanup clears ResourcesPresent between the checks.

Coordinate the terminal shutdown decision with callback entry/exit and the caller's frame/unload gate; at minimum the terminal hook-state decision must not rely on an earlier ActiveCalls snapshot. Recheck active/resource state after ownership inspection and ensure the host cannot enter a new frame callback between the final drain check and unload. Add a deterministic interleaving test for the final stop observer. This finding concerns the shutdown/unload contract; no unload was attempted during this review. The current IJL host source does not itself provide a hot-unload call site, so it does not establish that the dangerous interleaving has occurred live.

## Evidence and limits

- Read supplied integrated-map-feedback-v2.log and integrated-navigation-v2.log: reported all passing/0failures. Also read independent native-renderer report/tests and pure tests. I did not execute GUI/native tests or MSBuild concurrently with the root's full Native run.
- Current built debug.dll SHA256 independently read as B202E22FC01720587C73D80F3899A7882DAEC4983BD65187951B51535E51EF2F.
- Read-only live-before-texture.json supports the recorded original source dimensions/vtables/effect2/graphics template only. It does not exercise the new private renderer.
- No process/service queries, input injection, deployment, game-memory operations, save/raw-netdump reads, original asset mutation, checkout/index/HEAD changes, or child agents were used. Only this review directory was written.
- First genuine patched CALL/bridge/native allocation/lock/draw/destruction, visible click anchor, two visible pulses, unchanged original blue effect, map-close/replacement/cross-area behavior still require root-owned live acceptance.
- **Device identity change, same-pointer device-loss/reset, and resulting private resource retirement/rebuild have not been exercised live.** Pointer gates and seam tests cannot establish native helper flushing/device lifetime correctness. Two Observe calls implement the requested delay but are not independently proven native Sprite End fences.
- Native x87 channel conversion can differ from C++ truncation by at most one channel unit as already disclosed in the contract; exact bit-for-bit native rounding was not claimed.

## Declined to judge

- U07/backlog/monster-position synchronization: explicitly subsequent work and outside this diff.
- Original archive/PVF extraction tooling and full engine renderer internals: treated as supplied read-only contract inputs; this review validated exported bytes/hashes and API boundaries, not a second reverse-engineering project.

## Assessment

Ready for final live acceptance only after the P2 shutdown contract is resolved or the root establishes and documents an existing caller-level frame/unload gate that makes the interleaving impossible. No other actionable P1/P2 defect was found in the reviewed scope.
