# Isolated Chapter Two test control

This directory is deliberately absent from the ordinary C2 CMake link. Only the separately generated DO-NOT-SHIP host includes `Install`, and it additionally requires the dedicated process-name prefix plus explicit output-root and run-nonce environment variables. Semantic actions: reposition the local living player within the current native dungeon, or clear one explicitly identified, live, locally controlled monster. No arbitrary-address interface, persisted character edit, reward grant, synthetic clear, cross-map positioning, or timer change exists here.

The game-frame entry checks original C2 method signatures, current native scene, local actor and own-user binding, protected actor/global identity, stage/map agreement, native health/dead state, exact current map membership and target authority. Each request binds the exact process creation time, run nonce, recent snapshot, actor/map/stage and monotonically increasing request identity. A worker handles file I/O; the original native setter is called on the game thread. Actual death, peer synchronization and settlement still need their own live evidence.

The C1 own-user identity field cannot be copied into C2: real C2 observation showed it as zero while both native actor identity sources agreed. C2 identity is instead checked against the bounded, doubly linked player registry's key-to-own-user relation. The first native preflight refused to act because of this mismatch; a revised original-image fixture reproduced 11 failures before the correction and passed 27 checks afterward. That refusal is not a card test failure or a successful native kill.

Reproducible local preparation is retained under `dist/chapter-two-cards-matrix-20261110`: original-image death-contract project (17 checks, dependencies explicitly substituted), policy project (26 checks), private native-control-host project (runtime guard fixture, 27 checks), build logs and the failed native preflight. The private host retains the required TLS delta of eight bytes. See its REPORT.md for current live acceptance status; offline checks are not rendering/death acceptance.

The companion `tools/debug/chapter_two_test_control.py` only reads the fixed private output directory, verifies the dedicated host's process path/start time, and submits the bounded semantic request. UI operation and screenshots continue through Computer Use.

Version 2 binds the same identity/freshness fields and adds signed16 coordinates. Version1 is refused. Positioning checks the native position provider, both protected coordinate stores, local controller owner and stage/map publication dependencies before moving either axis. It calls C2's original axis setter/getter and complete position publisher with the same arguments as original reposition callers; the publisher updates its original compression baseline. These entries and offsets differ from C1. A partially applied operation is reported separately and is never automatically repeated.

The worker exports bounded same-map player positions and monster states. The CLI selects the target's actual current owner from the explicitly supplied processes. With peers, it requires all same-room party members and checks a newer observation from every process, retaining each process/actor/map/stage identity. A command acknowledgment alone cannot produce `synchronized`. Death/drop/reward persistence still requires independent service/store evidence.

Examples (replace placeholders with freshly verified private process IDs):

```powershell
python tools/debug/chapter_two_test_control.py --pid <A> status
python tools/debug/chapter_two_test_control.py --pid <A> teleport --x 700 --y 240 --peers <B> <C> <D>
python tools/debug/chapter_two_test_control.py --pid <A> kill --uid 0 --peers <B> <C> <D>
python tools/debug/chapter_two_test_control.py --pid <A> walk --x 474 --y 340
```

`kill` accepts one explicit native monster identity, including zero. A successful native call can still return `partial` if a peer changes room or fails to confirm; inspect retained observations before deciding the next action. Do not use this output alone as a card/rendering pass.

Position extension checks: policy39/0, original-image runtime47/0, companion11/0, Python discovery108/0. The native fixture executes original axis setter/getter and full publisher, substituting isolated initialization/output dependencies; it is not a live rendering acceptance. Builds and RED/diagnostic/GREEN logs are retained in the matrix artifact directory. Live acceptance must name the newly restarted host; an older running process does not acquire a rebuilt control entry.

Latest extension checks: policy43/0, original-image runtime52/0, companion16/0, Python discovery113/0. The added town `walk` preparation action uses the separate C2 town actor/user/area binding and already adapted original destination validator/setter and auto-walk activation. Its destination must belong to current floor or original portal geometry; native validation still decides acceptance. Only `native_navigation_started` is reported; position arrival/map transition needs subsequent observation. Dungeon teleport/monster actions remain forbidden in town, and town navigation remains forbidden in dungeons. Town HP/death fields are unknown and exported as null. This avoids using input-key walking to bootstrap a fresh test client.

Native monster identities include zero; this is distinct from the nonzero player identity requirement. Live observation found a zero-ID Goblin left after the first private host handled positive IDs. The explicit zero-target policy and original-image selection fixtures reproduced the omission, then passed 27 and 28 checks after correction. The Python client also retries only transient snapshot-file reads during atomic replacement; it never repeats an action because of an unobserved acknowledgment. Deployment/acceptance must identify the exact private host build, since a source correction does not change an already running process.
