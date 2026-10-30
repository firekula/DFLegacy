# U07 / monster movement: next bounded diagnosis

2026-10-30, independent read-only static research. No production/test edits, builds, runtime operations, input, process reads or writes to sealed evidence directories. Only this new `.tmp/movement-contract-20261030` directory was written. Native byte verification:15 prior monster functions and5 prior official Town functions independently rehashed from source PE/ELF and matched;6 new native ranges recorded. `manifest.json` binds source hashes and ranges. `fixtures.json` contains10 proposed cases, not executed product tests or live results.

## What remains open

| Case | Established | Still missing |
|---|---|---|
| Town six-window small residuals | Four effective last reports reached the peer unchanged;25/41/7px residuals match original stop dead zone | Actual receive branch events; not a reason to change original50px threshold |
| Earlier Town247.596/202 discrepancies | Historical large observations remain listed | Full scene/area/actor identity, actual last report, receiver target and received/application timing |
| Town10/s hard-drop candidate | Present source drops update11 and later in one1000ms window; no tail flush | A genuine native burst above budget ending with an uncorrected terminal state; actual running config |
| Old fallback monster UID3 | Sustained653–736px separation;67 exact B_TX/A_RX GP4 matches | ActualStage fields in that old capture, native object/room/association gate execution, motion/action stream |
| Fresh historical M1 UID1 |285pairs, max121.370; Stage(2,1), same declared-associated UID2 and matching targets at max gap | The exact per-message apply path, provider/GP42 inner actions, typed solver fields and effects |

These cases are separate. No proven production correction follows from the existing captures. U11 death/ground residue remains outside this assignment and last in queue.

## Town source: real hard drop, currently unproven natural trigger

`TownSessionDirectory.cs:394–429` `UpdatePositionAsync` validates an active registered ticket, saves X/Y/direction, then calls `TryConsumePositionBudget`; failure immediately returns. Only admitted calls construct NOTI22 and send to other members of the same town/area/instance. Sender is never echoed. `:1078–1100` uses UTC wall time and a1000ms fixed window; default `ServerOptions.cs:185` is10, <=0 disables the limit. No pending position, movement-value cache or timer flush exists for this budget. Merely waiting until the next second does not send the cached final coordinates.

`EntranceService.cs:13582–13643` parses CMD37 body `sequence:u16, x:i16, y:i16, direction:u8, movement:u16` (>=9bytes), updates session cached fields and calls the Town directory. `GameProtocolEngine.CreateUserPosition` changes sequence to subjectUID and preserves the seven gameplay bytes. This chain is not a monster GP4 handler.

Current `TownSyncSmokeTests.cs:1261–1320` explicitly expects only two of three updates for configured limit2, expects a later input after advancing the clock to reset the budget, and checks0 means unlimited. It **does not** require final state delivery after input stops. This is source-level evidence of a structural last-update-loss possibility, not a regression that currently proves the user's native case. The recorded old snapshots already show four last messages delivered, so that window does not reproduce it.

### New native cadence evidence lowers its immediate priority

Original ACT1 PE SHA256 `6b4850beaf67ec61adb04ae17f119f16278dd05e97b8ffca871a9bdb547de0d5`:

* `0x89F496` loads Town local actor `[B75A18]`. Context guards at `89F4A4..89F4BC` require object+98=0, object+8C nonnull, child+20!=1. `89F4BE..89F4E9` queries timer(object+10), compares it to last(object+94)+1000, and only when **strictly greater** calls actor.v+17C at `89F4DC`; then updates last. Normal nonwrapping elapsed1000 is ineligible,1001 eligible.
* Bound Town vtable `9988DC+17C` contains `479A40`. This sender compares current integerX/Y to actor+254/+258 and returns without sending when both unchanged (`479A4B..479A69`). It emits CMD37 and caches the sent coordinates at `479B3E..479B59`.
* This establishes roughly1/s **for this periodic path**, not an exhaustive global send-rate ceiling. Other virtual+17C calls exist, and their receiver types differ; a vtable slot alone is not a function identity. No alternate Town burst source was proven in this bounded trace. There are no direct relative calls to479A40.

Consequently, first measure real CMD37 frequency. Do not flood fabricated packets and label that a natural-client reproduction. Existing native route commands change the normal destination; they do not establish an emission rate above10/s.

The official later-build sample `df_game_r.bin` SHA256 `8859c99d8290dc5fb58151444138cf2697b2bfff12d47d6159596a03f20b5261` was independently rechecked against the public function hashes. `DisPatcher_SetPos::process@081C98C8`, `GameWorld::move_position@086C5706` and the related read/check/set functions retain received fields and use original state/ghost/area/visibility gates; these complete selected functions contain no corresponding10/s limiter. This does not prove the whole official server has no other scheduling constraints.

### Conditional correction choices, not a fix recommendation yet

Only after a real above-budget missing terminal update is identified: either use the existing <=0 configuration/no-throttle behavior as a controlled comparison, or make a bounded latest-update coalescer flush at the next allowed deadline. The latter must preserve **movementValue as well as X/Y/direction**, revalidate ticket/session/area/instance on send, and discard pending data on leave/migration/disconnect; current cached fields alone are insufficient. A stop-direction-only bypass is not a general solution because the native sender may suppress a later unchanged-coordinate stop message. Do not change50px dead zone or echo movement to the sender.

## Monster position authority in current source

`DungeonInstance` owns room layout/spawn records, member/death/progression/drop state. `GameDungeonMonster` is an immutable record with spawnX/Y, not an observed continuously updated motion state. `DungeonInstance.TestControl.cs` exports monsterUID/id/alive; it does not project live monsterX/Y. The inspected gameplay ingress is relaying the client application stream, not integrating monster coordinates into that record.

* `GameplayDatagramService.cs:561–618` selects registered same gameplay scope targets and sends the original datagram bytes unchanged.
* `FallbackRelayService` reads complete native relay frames, binds account/session scope, queues a delivery and revalidates current route before write. `NativeFallbackRelayProtocol.CreateDelivery` copies the frame and replaces only outer opcode with3; MT/application payload is unchanged. The bounded32 queue uses Wait for TCP; UDP `TryWrite` can drop on a full queue. This is a transport boundary worth counting, not evidence that it caused the old sustained separation.
* `GameplayDatagramService.FallbackRelay.cs` scopes dungeon routing by party plus the same instance object/revision; Town10/s code is absent from this chain.

Thus the source does not support “server authoritative monster current coordinates need refreshing” as a correction. Native association, client simulation and receive gate matter; do not promote the party leader or overwrite coordinates to force agreement.

### Exact native receive contract rechecked

GP4 application body begins `u16 objectKind, u16 objectUID, u16 playerKind, u16 playerUID, u8 roomX, u8 roomY, u32 flags`. Parser helper is `tools/acceptance/act1_gameplay_evidence.py:parse_gp4_identity`; the remaining motion payload depends on flags and is not wholly interpreted by that helper.

`78EE00` jump table `79213C[4]` → `78F12D`:

1. At `78F18E..78F1A4`, require activeStage=`engine+200010` and map=`Stage+B0`.
2. `78F1AA..78F1BD` calls map lookup `4C5AA0(objectKind,objectUID)`; absent object rejects.
3. `78F1C3..78F1E3` compares incoming room bytes to **Stage+B8/+BC as DWORDs**; either mismatch rejects.
4. `78F1E9..78F219` calls object.v+138. A null associated object **passes** this gate. If nonnull, its v+34 kind and v+38 UID must equal the declared player fields; mismatch rejects.
5. `78F21F..78F22C` calls object.v+40 for motion controller and then `4C9270(controller,flags)`.

For the previously bound monster type vtableA0BE04, v+138=`47F480`, which returns actor+1864. This is a local pointer, not a cross-process owner ID. `473620` compares the returned association to `[B75A14]` and requires local actor nonnull. `4AE790` writes timer(actor+8)+2000 to actor+1B04 **on every invocation before checking whether association changes**; do not label that field an ownership-only two-second protection interval.

GP6 case `78F512..78F63F` reads object kind/UID/selected-playerUID (threeu16s), looks up the user and user+110 Dungeon actor, requires current map membership, looks up the object, verifies/casts the applicable native object kind, then calls v+2D8 at78F627. Native room/object/association can diverge even after bytes arrive.

`4C9270` flag0x10 path writes the supplied current/target vector data directly; ordinary paths use motion state/time scalars and interpolation. This function mutates motion and consumes the native packet reader; **never call it as an observer**. GP42 dispatch at78EE3D enters the external provider; outerCRC validity does not expose innerGP5/6 or establish their absence. Existing strict decoder and timing tools should remain the parser of record; no searching opaque bytes for plausible headers.

## What the current debugCLI can and cannot establish

Current `client_state.h/cpp/json` and `tools/debug/client_state.py` supply fresh200ms scene/generation, Stage/map tokens, local and member actor/user/map bindings, local/peer position, and enemyUID/id/HP/dead/active/current position/action/queued_action/raw authority_token. No native getters execute during capture; getter addresses are checked as layout signatures.

Enemy enumeration comes from the current map object vector, kind529, with layout signatures v+34=4073D0 and v+330=652A20. The raw+1864 value is currently read without an additional v+138=47F480 signature, so treat it as raw diagnostic data unless the concrete type is separately validated. Raw pointer values must not be compared for equality between processes. A matching local players[].actor_token in the same snapshot is a useful association hint, not general authority proof.

Missing for the old precise gate/solver diagnosis: enemy actor/controller token and repeated binding, actualStage roomX/Y, validated association kind/UID/map, current-vs-target motion vectors, provider pointer/active flag, motion raw scalar tail, network-consumer flag and per-message dispatch/branch events. Snapshot generation changes on scene/Stage/map/local-actor/context identity; it does not itself give a monster incarnation token when UID is reused. CLI currently prints a compact summary (enemy count), not all enemy rows: import validated `load_snapshot` once in a persistent collector and store the complete returned snapshot. Select enemies by UID+map/generation **each sample**, not a fixed array index.

Coordinates currently derive from actor+E0 controller, reject active provider(controller+78 nonnull and byte+7C!=0), then current vector pointer `[controller]` or inlinecontroller+8. Supported getters are4CAC70 (secure floats+35/+3D/+45) and4736A0 (plain floats+4/+8/+C). A future read-only target projection can inspect `[controller+4]` or inlinecontroller+3D, as proven by4C9270, using the same explicitly checked vector layouts, finite/bounded values and repeated actor/controller/vector binding. Treat unsupported/provider paths asnull. Do not assign names or units to raw+28..+48 tail values without more proof, and do not call solver/provider methods.

These are proposed bounded observation extensions, **not implemented fields or approval to broaden arbitrary memory inspection**. In particular, a read-only state projection still cannot prove the exact per-packet branch executed; that would require a separately designed passive native event trace.

## Highest-value live plan for root, using current interfaces

1. **Bind once.** Preserve the four current clients/build/config identities; use two living clientsA/B for the first reproduction, leaveC/D unmodified. Record PID/startFILETIME/UID, run+room_generation, scene/Stage/map tokens and native layout. Do not carry historical map/UID identities into the new run. Use existing party CLI only if needed; verify both rosters. Avoid teleport, forced monster kill or player kill during motion measurement.
2. **Town falsification first, bounded20–30s.** Same sharedTown/area, away from door/NPC boundaries. Record complete validated snapshots and filtered actualCMD37/NOTI22 wire while A moves naturally for several seconds and stops for3–5s; repeatB. Existing DLRT/current-area destination CLI is suitable for one ordinary target, with direction-key interruption as a second trial. Count per-sender1000ms windows from actual messages; capture actual configuration. If never>10 and all last messages match, stop pursuing this budget as that trial's cause. If >10, require exact terminal gameplay-field absence at the peer, no area change, no later correction, and stable large displacement before assigning cause. Do not expect exactfloat equality: stop corrections under50px are native behavior.
3. **Monster fresh paired window, bounded30–60s.** Enter the same ordinary room naturally; identify one ordinary living UID visible at both clients, and keep run/room generations stable. A/B alternately walk without attacking for8–10s, then pause; avoid deaths/clear/transition. Existing debugCLI supplies current positions, alive/action and raw association hints, so it can find a persistent difference cheaply. Snapshot on each new publication; retain everysequence and collection/capture timestamps, reject stale/truncated data, never count repeated reads as new samples. Current200ms sampling is not an atomic pair. Record capture skew and only quantify precise pair gaps under a declared strict skew bound; otherwise report coarser sustained divergence/inconclusive timing.
4. **Stop on the first sustained large difference.** Suggested trigger is >100px in several fresh qualified pairs over at least3s, not a single moving sample. Save the surrounding±5s full snapshots and filtered strict transport/application provenance; take one original screenshot per client after stopping measurement. Measure target/current only if the extra validated fields exist; the current CLI cannot fill them. Count decoded/opaqueGP42 separately; keep duplicate send candidates, fragmented bounds and source socket timestamps. Logs are not dispatch/application times.
5. **Classify before changing code.** Missing matchedGP4 → transport/route/queue investigation; receivedGP4 with room/association mismatch → gate/context investigation; current and target differ despite matched targets → native solver/action/provider investigation; unsupportedGP42/action stream → explicitunknown. A received packet plus current-position divergence alone does not select any one fix. If no divergence within the bounded trial, retain “not reproduced,” not“fixed.”

No broad configuration experiments or repeated screenshots are required for this first pass. A same-client, same-room matched normal/fallback comparison can follow only after a reproducible window exists; old different-map/seed normal captures cannot isolate transport causality.

## Meaningful regressions once a cause is established

* Town terminal update: budget exceeded, then **no further input**; after the allowed deadline the witness must receive the latest wholeX/Y/direction/movement tuple, never self echo. Existing rate-count test alone is insufficient. Add leave/migration/new-session invalidation and latest-wins coalescing cases if that design is chosen; unlimited mode must preserve every update.
* Native stop semantic oracle:49px preserves the current axis;50/51 accepts received axis; mixed axes remain independent. This protects original behavior, not a new snap-to-equal target.
* Diagnostic joins: sameUID across room generations cannot join; duplicate identical sends remain ambiguous; null association passesGP4 identity gate, mismatched association rejects; unsupportedGP42 cannot imply missingGP6; current/target/provider unknown staysnull. Existing decoder split/concat/truncation cases remain required.
* A future monster correction needs an actually failing trace-specific regression tied to the proven gate/solver/relay defect, then a repeat of the same matched-room live scenario. There is no evidence-supported current monster coordinate/authority patch to recommend.
