# SourceSpecificBinding incremental source review

Date: 2026-10-29 (Asia/Shanghai). Independent reviewer: map_feedback_review.
Scope: minimap-specific third-hook source/self binding; preserved P2 lifecycle correction; three assigned renderer files. Earlier reviews and hashes remain unchanged.

## Verdict

**P1: none. P2: none in this increment.** The specific callback source and matchingSelf condition now agree with the supplied original x86 instructions. The original P2 remains closed under the documented host lifecycle contract. Fresh genuine GPU rendering, click-anchor and visible two-pulse acceptance remain pending; no live PASS is inferred from this review or the new tests.

The first F5 rendering failure reported by root remains a genuine live RED: accepted/active goals with zero draw_count/texture_creations. Earlier generic8F6FB6 source assumptions are superseded by this correction, not reclassified as successful rendering evidence.

## Callsite and x86 ABI

Independently read the entire exported82byte 881860 callback,949byte 8814A0 draw body and14byte callsite, their disassembly, contract and manifest. Recomputed all source/new-evidence hashes in native-renderer-source-specific-manifest.json: zero mismatches.

- Callback881860 receives owner inECX and child* on stack, retains owner inESI, loads root from[owner+4], compares child to root and checks nonnull/visible before reaching88187C.
- 88187C is MOV ECX,ESI;88187E original bytes are E8 1D FC FF FF. Independently decoding the signed relative displacement from the raw callback gives8814A0. The14byte callsite file matches its exact slice in the full callback. Runtime's16byte signature at88187C is exactly8B CE E8 1D FC FF FF 8B CE E8 06 F3 FF FF 3B 7E, also matched against the full raw callback.
- The complete8814A0 body has no incoming positive EBP stack-argument references and ends in plain C3 RET at881854. A thiscall(owner) with zero stack arguments is consistent with both caller and callee. OriginalDraw uses DrawTargetAddress and the fastcall bridge receives owner inECX with unusedEDX, pushes no incoming callee arguments and requires no callee stack pop. DrawWithOriginal still invokes original first exactly once and preserves full EAX through the after callback.
- Only the5byte CALL is patched.881883 MOV ECX,ESI and881885 CALL880B90 remain after the bridge, so original owner drawing then appended feedback still precede tooltip processing. Other callback branches/final8806D0 remain untouched.
- Signatures pin target entry, callback entry and surrounding callsite before installation; FunctionsVerified caches the completed pre-patch validation, so later leaf checks do not incorrectly reject the module's own replaced CALL bytes. NativePatch and hook displacement/restore consistently use the new DrawCallAddress and OriginalCall. The previous8F6FB6 tree draw is no longer patched by this renderer.

## Self filtering and retained ownership checks

AfterDraw:374–400 now compares self to the freshly read binding.minimapToken:379, matching the ownerECX proven at the new source. It no longer compares owner to panel root. ReadCanvas still separately requires supported/fresh town context, current minimap vtable, visible/enabled root, exact plane, live context/user identity and bounded view origin. The final pre-draw check still compares root, minimap, plane and view against the earlier read. This removes the incompatible generic-root/self assumption without dropping the panel/plane ownership guards.

Other minimap owners/panel-root self cannot append feedback. OriginalDraw still runs for every call reaching the bridge; filtering affects only the appended operation. The specific callback executes after original NMap drawing and before the existing tooltip call, within the original render flow; no extra Begin/End was added.

Texture operations, local descriptor rebasing, state restoration, finite timing/pixels and worker-only diagnostics are unchanged from the reviewed baseline. This source change does not independently establish native helper batching or device-reset behavior.

## P2 remains resolved

Shutdown:560–572 retains the HookLock-internal joint checks of DrawHook.installed,HookPublished,ResourcesPresent,ActiveCalls, as well as the null-by-default/reset BeforeShutdownLock seam. Both P2 late-observer/resource assertions and subsequent-drain assertions remain in the test source. Game-thread-only patch/destruction and delayed cleanup remain intact. Header still explicitly requires stopping new DebugRender entry and a quiescent host boundary before FreeLibrary; no arbitrary concurrent unload guarantee was reintroduced. Prior inspected IJL host has no normal hot-unload path.

## Test evidence and limits

SourceSpecific-red.log contains exactly3 new failed assertions: pinned source bytes/addresses, actual minimap owner reaches protected path, panel root rejected. SourceSpecific-green.log reports the complete native seam suite PASS including retained P2 regressions; the author manifest records RED exit3 and GREEN exit0 under x86 /W4 /WX. Build logs show compilation/code generation with no warning/error. All artifact/source/native-increment hashes match the author manifest.

The matchingSelf regression uses actual AfterDraw with fake memory: owner0x300000 reaches LeafGate and is cancelled specifically as native_signature_unavailable; panel root0x310000 leaves active feedback/reason intact. This proves correct gating past owner selection, while deliberately avoiding genuine absolute native calls. It does not prove the final GPU draw, real private texture creation or click anchor.

Only3 renderer files differ from reviewed-hashes-p2-followup.json; the other14 previously reviewed source/spec/plan files are unchanged. No reviewer test process, GUI, MSBuild, deployment, live process/service query, input/memory call, save/raw-netdump access, product edit or child agent was used. Only this follow-up review and its new hash list are written.

## Declined scope / remaining acceptance

- Fresh real GPU/private allocation-lock-draw/bridge/anchor/two-visible-pulse/unchanged-blue-marker acceptance is root-owned and pending. SourceSpecificBinding logic GREEN does not overwrite the earlier live RED.
- Device identity transitions and same-pointer device-loss/reset remain untested live. The two-Observe delay is still not claimed as a discovered native Sprite End fence.
- Unrelated U07/backlog and original archive extraction tooling are outside this increment.

No additional actionable P1/P2 finding was identified.
