# Runtime independent final source verdict

All reported P2 findings are CLOSED. No remaining P1/P2 in the requested runtime/adapter/IO/source-list scope. Source is ready for root to freeze. `final-source-manifest.json` pins the exact reviewed working-tree bytes and HEAD before root's integration/freeze commit; it does not pretend uncommitted bytes have an earlier commit identity.

Native preflight: the route path calls production SetLiveAndActivate, which checks issue1000ms/source750ms age and Live before set/activate. NativePointAndDestination also checks Live before absolute point entry. Independent navigation callback-count tests confirm stale/invalid live contexts call neither destination callback. No absolute original game routine was invoked by review tests.

Delayed-first-step intent: controller b933875 adds Start/new-area ingress baselines, checks destination/known auto/auto value/new or changed nonidle direction before the200ms send gate, tightens natural idle transitions, and clears the ingress only at emitted native action. New-area entry records that map's baseline and returnsNone before the next Tick. The original3case probe rebuilt against fixed source now producesInterrupted1/SetDestination0 for all3cases. Added300ms cancellation/nonrevival/noSteps and transition tests passed with the actual PVF fixture.

Worker delivery/backpressure: a result stays in its SPSC slot until BOTH per-ID and latest Save succeed. The exclusive .tmp lock/unlock retry and Pending terminal-versus-progress production whitebox passed. Pending/Deferred retain terminal decisions; Begin reserves2ring slots before route replacement. Final stopForBackpressure checks Pending, Deferred AND queued>=64both at frame entry and after Inbox processing before Tick. New independent production OnFrame retests:

- starting63slots, invalid Inbox fills64: `apply_calls=0 controller_active=0`;
- starting64full: `apply_calls=0 controller_active=0`; the Inbox may remain unconsumed until capacity returns.

See midframe-63-green.log, midframe-64-green.log and independent-retests.json. These retain the original complete floor fixture and substitute only Capture/read/Apply callbacks; there are no native calls or filesystem actions in the retested OnFrame path. Build is Win32 C++17 /W4 /WX,0errors/warnings. The initial RED snapshots/logs remain unchanged in parent review directories.

The earlier fixed-delta focused runner independently passed navigation/controller/adapter/IO groups with the original Script.pvf env and isolated TEMP/TMP (`../independent-tests-real-pvf.log`). The final queue-only delta was then source-reviewed and rebuilt/retested separately, rather than repeating a large suite. Two preliminary fixture-environment attempts (missing controller PVF env and inherited TEMP setup exit1) remain preserved and were not counted as green actual-PVF evidence.

Other integration conclusions from the initial review still hold: worker-only archive/file IO; main uses only nonblocking lock attempts and bounded capture/plan/native leaves; Catalog publication is a short immutable shared_ptr handoff; host joins the worker before shutdown reset; exact origin/floor checks remain; old poll runs first and the original right-release world consumer excludes the map root's capture ID. Build lists compile the runtime normally in DLL and via the whitebox IO test TU in tests, without duplicate runtime objects. No Catalog/UI internal scope was reopened.

This is a SOURCE verdict. It does not certify a not-yet-frozen DLL, deployment, native right-click travel, all Towns, or live matrix acceptance. No product source, index, configuration, deployment or live process was changed by the independent reviewer.
