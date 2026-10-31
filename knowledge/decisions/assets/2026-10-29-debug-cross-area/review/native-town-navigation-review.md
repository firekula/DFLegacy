# Debug town navigation: independent static review

Reviewed 2026-10-29, read-only. Source HEAD: `413c556598e64227a75b64353054124eaf36002f` in `D:/codex_worktree/party-chat-gold/DFLegacy`.
Original disk PE: `G:/Game/DNF_ACT1/DF/DNF.exe`, SHA-256 `6b4850beaf67ec61adb04ae17f119f16278dd05e97b8ffca871a9bdb547de0d5` independently recalculated.
No source/index/HEAD changes, process access, client control, deployment, or live acceptance performed. Only these derived evidence files were written in the requested recon namespace.

## Verdict

**Current API implements native direct-goal movement, not a route planner.** For a valid world-space point and the supported Town actor, `479EF0 -> 478F70 -> B5F0A0=1` matches the movement-producing portion of the original ground left-click consumer. The complete valid-click branch contains no additional native path-planning call omitted by this implementation.

`4795F0` continually steers each coordinate toward the saved final destination. Lower native collision handling permits local per-axis sliding/clamping; it does not generate waypoints or select a detour. Therefore some monotone L-shaped passages may work, but a concave/U-shaped corridor requiring movement away from the final target cannot be guaranteed by one destination. Do not interpret a successful single doorway crossing as proof of general cross-area routing or concave-mask pathfinding.

No source defect in the ABI/core equivalence was identified by this focused static review. The feature's existing same-area contract is narrower than arbitrary cross-area destination navigation.

## Original click and current API correspondence

Actor vtable `009988DC` maps `+54 -> 407400`, `+94 -> 479EF0`, `+98 -> 4719F0`, `+154 -> 47A8E0`, `+178 -> 478F70`, `+1A4 -> 4795F0`.

| Original disk PE evidence | Meaning | Current source |
|---|---|---|
| `79CB50..79CC62` | UI/capture/selection and Town-scene/actor gates; obtain actor map | `client_navigation_runtime.cpp:88..125` has its own stricter identity/layout/Town/live guards |
| `79CC65..79CC85` | Read mouse coordinates, convert screen to world via `4C4FB0/4C4FF0` | API already accepts world x/y; minimap projection is performed elsewhere |
| `79CC8A..79CC9E` | `thiscall actor.v+94(x,y,nullptr)`; branch on AL | `client_navigation_runtime.cpp:127..139`: `bool (__thiscall*)(void*,int,int,int*)` at `479EF0` |
| `79CCA2..79CCC4`, only if the clicked endpoint is invalid | `v+98=4719F0` searches nearby endpoints (radius 100, step 3), not a route from the actor | API intentionally rejects invalid endpoints, without this nearest-valid fallback |
| `79CCCA..79CCF6` | For own non-state-5 user, `thiscall actor.v+178(x,y)` | `client_navigation_runtime.cpp:136`: `void (__thiscall*)(void*,int,int)` at `478F70`, followed by destination readback |
| `79CD07` | One-byte store `[00B5F0A0]=1` | `client_navigation_runtime.cpp:142..158`: live recheck, exact destination recheck, same one-time byte store |
| `79CD0E`, `79CD13..79CDE5` | Play `CLICK_MOVE` audio; create/place `Common/Etc/Animation/MoveDestinationMark.ani` | Omitted audio/visual feedback, with no lost routing behavior visible in this branch |

ABI: point checker returns Boolean in AL and `ret 0Ch`; setter is void with `ret 8`. The current declarations match. `478F70` refuses an active busy transport, writes `actor+23C=x` and `actor+240=y`, then immediately invokes `v+1A4=4795F0`. Thus the API preserves the original immediate first steering update as well as later automatic updates.

The audio identification is supported by the original string `A7D540="CLICK_MOVE"`, `433890 -> 90DF30` tag lookup/play path, and that audio manager's strings `AudioTagDatabase`, `Effect`, `Music`, `Ambient`, and `sound_log.txt`. The visual marker pathname is present at `A7D510`.

## Why this is steering and collision, not route finding

1. **Endpoint membership:** `479EF0` follows `actor+E4 -> map+F4 -> area+50 -> definition`, iterates the 16-byte rectangles at `definition+154/+158`, and calls `776C60(x,y,rect)`. `776C60` checks `left <= x < left+width`, `top <= y < top+height`. No actor-origin segment/connectivity search is performed. The API additionally proves this vector is present and structurally valid because the original returns true for some missing-map cases.
2. **Fixed final goal:** `478F70` stores only the final x/y, then calls `4795F0`. `4795F0` compares current x/y to those saved values, selects direction `0/1/4` on x and `2/3/4` on y, handles overshoot by snapping the crossed component to the final value, and writes signed axis velocities through the controller's vector `v+0C`. This function neither reads the rectangle vector nor creates a route/waypoint list. The only position-dependent sign choice points toward the final coordinate.
3. **Automatic continuation:** ordinary actor update `473490` calls movement controller update `4C9F00` at `4734D4`, then Town virtual update `47A8E0` at `4735D3`. Town `47B24C..47B25D` reads `B5F0A0` and, while set, invokes `v+1A4=4795F0` each update. Original keyboard input can clear this flag at `47B303`. The API's one-time activation is sufficient; repeatedly calling the setter is not a missing native requirement.
4. **Local collision behavior:** `4C9F00` calls `4C8C00` separately for axes 0/1/2 (`4C9FA2..4C9FB7`). For normal no-active-provider movement, `4C8C00` computes the axis displacement, walks integer candidate coordinates toward that tentative axis endpoint (`4C8E80..4C8ECA`), and checks each x/y via owner `v+94` at `4C8EBC`. On the first invalid candidate it writes the previous legal value to that axis at `4C8F32..4C8F45`. This can leave the other axis free to move along a wall, but has no alternate waypoint/away-from-goal selection.
5. **Owner/type provenance:** actor initialization `472C20` pushes the actor into controller constructor `473A60` at `472C7E..472C81` and stores the returned controller at actor `+E0` (`472C8D`). Constructor passes that owner to `4CA8A0`; `4CA8A6` saves it at controller `+74`, the owner used by the above point checks. Raw vector vtable `99D38C` maps its `+0C` to `4736E0` (plain axis velocity assignment); secure vector `99FDFC` maps `+0C` to `4CAD10` (encoded axis velocity assignment). These writes are not hidden path planners.

This is stronger than merely failing to spot an A* name: the steering and no-provider collision paths retain a final fixed target and do not choose travel in the opposite direction when a corridor demands it. It is also narrower than claiming all concave maps fail: local sliding can traverse some shapes where both coordinates progress monotonically toward the goal.

## Cross-area and lifecycle limits visible in current source

- `client_navigation.cpp:49` returns `cross_area_unsupported` when request town/area differs from the current snapshot. No foreign-area destination is accepted or transformed into an area route.
- `client_navigation_runtime.cpp:299..318` binds tracking to the original generation/actor/map/town/area. A natural map/area transition ends that request as `interrupted/context_changed` before an `arrived` result; it does not cancel the native movement or issue a next-area destination.
- Tracking also ends for replaced destination, stale capture, native auto-move stopped, or 30-second timeout. It does not re-plan a blocked route or keep forcing the activation byte.
- Therefore a current-area goal lying through a doorway may exercise the original game's normal area-transition machinery. The exact gate trigger, resulting context/position, and state left on the far side remain matters for the parent's independent live proof. They are not statically certified here.

## PVF tag evidence and remaining unknowns

The disk PE contains and parses both tags: `7494B5` references `A70468="[town movable area]"`, reads six integers per row and appends via `74DC30` to the `edi+D4` container; `749563` references `A70450="[virtual movable area]"`, reads four integers per row and appends via `74DCC0` to `edi+E4`. These parser excerpts are retained in the disassembly.

The transformation from those two PVF containers into the runtime definition's `+154` rectangle vector, and a complete topology of the actual test town, were not independently reconstructed in this task. Do not label the two parsed containers interchangeable or claim that all gates/movable masks were proven from this evidence. This limitation does not affect the direct steering/per-axis collision findings, which follow the actual runtime fields and native calls.

Other deliberate limits: no live game/client/server was run, no injected-memory code or runtime vtables were read, and no global claim is made about every movement mode/transport/provider. Findings refer to the exact disk PE and the ordinary supported Town actor/no-active-provider path that the debug state reader accepts.

## Evidence files

- `native-town-movement-disassembly.txt`: original instruction bytes and Capstone x86 decoding for the complete click consumer, setter, steering, endpoint checker/fallback, Town update, normal actor/controller/axis updates, controller owner binding, vector setters, click audio, and PVF tag parser excerpts.
- `native-town-navigation-evidence.json`: source HEAD, hashes of both reviewed navigation source files, original PE SHA-256, disassembly SHA-256, and explicit address ranges.

No unit test was run for this static-only comparison: executing synthetic navigation tests cannot establish the original PE's route-planning behavior, and live testing was assigned separately to the parent.
