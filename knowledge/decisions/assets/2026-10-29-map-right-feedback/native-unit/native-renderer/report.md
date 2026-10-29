# Native map feedback renderer: implementation and offline verification

Only these three new files were edited by this subtask:
- src/DFLegacy.Debug/src/client_map_feedback_runtime.h
- src/DFLegacy.Debug/src/client_map_feedback_runtime.cpp
- src/DFLegacy.Debug/tests/map_click_feedback_runtime_tests.cpp

No existing product/project/runtime files were edited. No commit, process control, client input, live game function call or game-memory write was performed by this subtask. The root owns integration and any live validation. Frozen native-contract outputs were only read.

## Public API

Namespace dfl::client_map_feedback, all noexcept:
- Initialize(client_state::Reader reader,const wchar_t* directory)
- AcceptGoal(const client_state::Snapshot&,const client_navigation::CanvasGoal&)
- ObserveFrame(const client_state::Snapshot&,bool navigationHooksReady)
- PumpWorker()
- bool Shutdown()

Global test entry is int RunMapClickFeedbackRuntimeTests(). Tests white-box include runtime.cpp; the DLL compiles runtime.cpp normally. Test target must not independently compile runtime.cpp a second time.

## Runtime behavior

Independent third hook at8F6FB6 expects E8C5FAFFFF, calls original8F6A80 thiscall with no stack parameters exactly once before feedback and preserves full32bit EAX. Installation occurs after navigationHooksReady and all pinned native entry signatures pass. Appending rendering occurs only when self equals the currently owned NMap root. Loss of navigation readiness disables feedback. Current cache holds only binding/sequence/capture tick/Town validity, no large Snapshot copy.

Binding uses exact pid/processStartedFileTime/uid/userToken/TownContext/minimap/plane identity. Actor/map/generation changes are intentionally absent. Hidden root, invalid/nonTown/stale SDK capture, identity changes and clock rollback clear feedback. Goal plane-local point subtracts current plane view origin before native frame drawing; private image offset(-22,-22) centers44x44 source shape at the click. Actual anchor appearance remains a live acceptance item.

Source descriptor requires exactly one self marker A8480C,type2,matchinguid; animation+8C58h descriptor; source IImage A8EFE4,type14,44x44,zero frame offsets; source image pos(-22,-23),effect2,angle0,mirror0. Local descriptor copy substitutes private1555 IImage,gray/alpha curve and both address-protected scales using (floatBits XOR[BEF7D4])+actual field address. Shared marker/animation/image data is not written.

Private texture bypasses native tracking factory:91857A(90h) allocation,8E16A0(this,14,graphics+203C,graphics+2040) ctor,8E1730(this,44,44),8D3380 lock, private mapped+68pitch/+6Cbits,88bytes per row,8D33C0 unlock. All create/lock/unlock/draw/destroy leaves validate supported image,bound game thread,pinned native signatures and relevant pointer/vtable identities and catch SEH. Failed transactions clean owned allocations or retain ownership if cleanup itself fails. Texture lives across clicks/expiry/map close; renderer/device/helper changes stop drawing and request deferred retirement.

82C160 cdecl(surface,x,y,flip0,const descriptor*) performs original native draw. GetRS19/20 via8E1920 before drawing; always restore both via8E18F0 after success or a native leaf's caught SEH. No shader/new Begin/End/texture stage rewrite is introduced.

## Shutdown and frame-boundary discipline

NativePatch itself requires the bound game thread. Shutdown never patches code or invokes native resource destruction. It atomically requests stop and returnsfalse while a third-hook/resource owner or callback remains. Root must retain navigationEnabled and its two hooks after false so ObserveFrame continues.

Once stopping,no new feedback draw is allowed. A texture that has ever been submitted is not deleted at the first stop ObserveFrame because that callback may still be the tail of the same Sprite interval. At the second stop ObserveFrame it can be destroyed on the game thread; never-drawn failures can clean immediately. Normal graphics-change retirement also requires two observations with draws stopped. After resources are released, ObserveFrame restores the third CALL on that game thread. A failed restore retains the installed bytes/ownership and retries on later observations. A later Shutdown returns true only when ownership is gone. Activity count is an extra in-flight guard, not a proof that cross-thread CALL patching is safe.

The two-observation boundary follows the root's provided integration contract; it is not represented as a discovered engine Sprite End fence. If ObserveFrame stops arriving, shutdown remains false and unload is blocked.

## Diagnostics

Only PumpWorker serializes or writes dfdebug-map-feedback-<pid>.json. Publication on the game thread uses a nonblocking Try SRW lock; export occurs every200ms or revision change, full temporary JSON plus MoveFileEx(REPLACE_EXISTING), nofsync/FlushFileBuffers/write-through. Output distinguishes local tick_ms from actual SDK capture_tick_ms andcapture_sequence; it does not synthesize SDK freshness. It includes trigger_serial,active,two ring states,plane-local target,draw_count,texture_creations,hook/cleanup status andfixed reason.

Draw count measures completed native frame-draw calls (no caught SEH), not a GPU-present/readback guarantee. Original82C160 is void and underlying driver/helper HRESULT is not exposed by that API. A completed native call alone cannot prove visible pixels.

## RED/GREEN evidence

Initial skeleton compiled underMSVC14.51.36231 x86 /W4 /WX /EHsc /std:c++17 /MT /utf-8, then failed22 expected runtime assertions. See red.cmd/red-build.log/red.log. No compile/link error was claimed as RED.

Final production helpers/runtime and whitebox tests compiled with those flags, no warnings, GREEN exit0. See green.cmd/green-build.log/green.log. Covered:
- original-first once andfull32bit return preservation;
- local private descriptor,protected scale encoding/source immutability;
- native-success,false-return andreal synthetic SEH leaf paths restore both original render states;
- both restoration attempts even when one reportsfailure;
- allocation/ctor/create/lock/unlock failure cleanup,raw-versus-constructed ownership;
- private row pitch,padding,invalid mapping/address overflow;
- failed install does not publish ownership;failed restore retains ownership;
- wrong-thread Shutdown/Observe andNativePatch never patch;later game-thread fake restore fails,retains andthen retries successfully;
- first stoppingObserve does not delete drawn texture,second does before unload;
- unique self marker/template checks,duplicate marker andwrong dimensions rejected;
- unrelated UI roots do not append drawing;
- unsupported image rejects every absolute native leaf before call;
- pending resources andin-flight callbacks block unload.

The test harness never executes genuine absolute game addresses. Only fake memory/operations and unsupported native gates are exercised. Root's separately reported read-only live-before-texture.json confirms actual source marker/descriptor/graphics identities match the pinned offline contract; it does not exercise this new renderer.

## Unverified / acceptance limits

- First actual native allocation/lock/draw/deletion anddriver/device-reset behavior remain live acceptance work.
- Graphics identities are checked before every native leaf; external same-pointer device-lost states can still result in silent native void-draw failure. Visual acceptance is required.
- The third-hook bridge ABI is established from original PE and x86 compiler conventions; tests exercise wrapper semantics,not a real patched CALL.
- Map click center,distinct two200ms-offset native-style yellow copies,cleanup overclose/scene changes andunchanged original blue player effect need root's live observations.
- Two observations provide the explicitly requested delayed cleanup rule; no unproven helper flush entry is called.

native-renderer-manifest.json records final source SHA256 and original PE fingerprint. No test PASS is represented as live rendering PASS.
