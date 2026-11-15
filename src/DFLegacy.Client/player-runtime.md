# Shared player runtime and diagnostics contract

`DFLegacy.Client.dll` and the legacy test IJL build compile the same player runtime. The frame gate at `0x83F992` and its call hook remain active with FPS, debug overlay and automated input disabled. `player_runtime::OnFrame()` starts the worker outside the loader lock, captures client state and advances navigation before any renderer availability/visibility checks. Core owns state publication, navigation hooks, town-route catalog/queues and map feedback. Debug unload cannot stop or restore these hooks.

Standalone mode comes only from the bootstrap handshake. Normal mode never loads debug.dll, never polls debug navigation commands and ignores stale automated-input/network/probe diagnostic settings. Standalone player navigation defaults on; `[Features] EnableTownNavigation=0` is an explicit player opt-out. The legacy IJL path retains its existing `[DebugOverlay] EnableTownNavigation` opt-in.

Sampling is independent of `[DebugOverlay] ExportState`. Test/legacy diagnostic state/navigation exports require that switch. Navigation file commands additionally require the existing `EnableTownNavigation` debug opt-in. Test mode alone does not enable automated input, party control or test-control commands; their existing explicit settings and request/lease validation still apply. With exports disabled, the route worker still loads Script.pvf and consumes result queues. Control/state files retain their configured channel directory; they are not logs.

## Debug ABI v3

`debug_api::Version` is 3. `Host` appends `const player_runtime::SharedState* sharedState`; callers and plugin require exact Host size/version. SharedState v1 requires its exact size/version plus exact Snapshot/Reader sizes and non-null stdcall CopyLatest/FrameTiming callbacks. Reader callbacks and publication belong to the core and outlive the diagnostic DLL. A v2 debug DLL fails initialization; normal mode does not attempt loading it. Optional gameplay/pipeline/native-loop export ABIs and telemetry exports are unchanged.

The debug library compiles pure state decoders plus a bridge, not the state owner or navigation/map/route runtime owners. Party/test/profiling observers read the core's Reader and CopyLatest. The five-section frame profiler composes core capture/navigation durations with DebugRender durations and explicitly excludes intervening renderer setup; the outer frame pipeline still measures the complete host callback. This scope distinction is stated in exported profile text.

## Paths and early RS logging

`client_paths::BuildLogPath(leaf, output, capacity)` is a noexcept wide-character API. It creates client `Logs/DFLegacy`, accepts only a file leaf (no traversal, separators, ADS or DOS device aliases), clears output on failure and never supplies a root fallback. Native direct log writers retain their PID/fixed filenames. Render diagnostics retain `render-diagnostics/<pid>.json` under `Logs/DFLegacy`. Config.ini and auto-input control paths use wide Windows APIs; controls remain in the executable directory.

The standalone import is prepended by Detours. Its lightweight DLL_PROCESS_ATTACH routine patches only the already mapped sibling RS.dll's named KERNEL32!CreateFileA IAT slot, before RS DllMain runs. Only the exact `rs_log.txt` argument is redirected, through CreateFileW to the common log directory. Other names use the saved original function. No worker/module load/wait/game initialization is performed there. The bootstrap fixture imports a separate RS.dll whose DllMain writes both rs_log.txt and unrelated.txt; it verifies the early log location, absence of a root rs_log.txt, and untouched unrelated TXT behavior before executable entry. Heavy runtime work remains outside the loader lock.

The existing original-client Logs/ScreenShot patch remains separate. No migration/archive operations are performed by this native runtime.

## Validation boundaries

The new fixtures use synthetic process/memory state, Unicode executable paths (including non-ACP characters), locked destinations and headless debug attach/shutdown cycles. They never launch game assets or send desktop input. The existing full debug_overlay GUI test requires a separate desktop-authorized run. Real game navigation, map feedback rendering and original RS behavior remain live integration gates for the controller.
