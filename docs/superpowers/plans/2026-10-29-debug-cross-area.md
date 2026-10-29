# Debug Cross-Area Navigation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans or superpowers:subagent-driven-development to implement task-by-task. Checkboxes track evidence, not intent.

**Goal:** A single debug destination automatically traverses configured Town areas and reaches its final coordinate.

**Architecture:** A bounded read-only PVF catalog supplies area portals and rectangular walkable geometry. A Python operation controller owns the existing per-PID navigation lock and issues fresh original-client destination requests, validating each native acceptance and expected context transition. Existing native movement and wire remain unchanged.

**Tech Stack:** Python standard library, pytest, existing debug.dll DLNV/v1, original ACT1/PVF.

**Spec:** docs/superpowers/specs/2026-10-29-debug-cross-area.md

## Global Constraints

- No OS input, packet injection, coordinate/state seeding, original-file edits or new service protocol.
- PID/start/UID/user/actor/context stay pinned. Only planned Town/area/map/generation transitions may rebind a leg.
- Preserve existing same-area API and its lock, freshness and replay checks.
- Native acceptance and final physical position are separate requirements. UI/manual cancellation must prevent later legs.
- Area graph excludes dungeon intermediates; geometry is read from the actual client PVF and bounded before use.

## Review Focus

- A delayed or transient invalid snapshot during expected map loading must not trigger a new command or a false arrival.
- Another controller or minimap destination must stop the route; a later leg must not overwrite it.
- A UID reused after character selection must not inherit a prior route.
- Concave/disconnected rectangles and portals outside walkable intersections must fail or yield valid internal legs.
- An expired/blocked route must not be relabeled successful because some earlier portal was crossed.

### Task 1: Read-only catalog and geometry

**Files:** tools/debug/town_navigation.py; tools/debug/test_town_navigation.py

**Interfaces:** load_catalog(path) -> Catalog; Catalog.area(key) -> Area; Catalog.route(start_key, goal_key) -> tuple[Portal,...]; Catalog.waypoints(key,start_xy,target_xy) -> tuple[(x,y),...]. Rect/Area/Portal are immutable data; Portal.source/target are (town,area), Portal.point lies inside a portal/walkable intersection. Errors carry stable code strings.

- [x] Tests first: encrypted PVF fixture parsing/bounds, actual tags, rectangle connectivity, U-shaped detours, no-path, endpoint and portal legality, invalid/unknown areas, dungeon intermediary exclusion.
- [x] Observe RED, implement bounded parsing and planning, obtain GREEN; actual PVF regression records SHA and routes. Actual float-idle thin join and whole-leg blocked-portal regressions added after diagnostics.
- [x] Independent review and scoped commits; geometry SHA D6C10815, no remaining P1/P2 in reviewed scope.

### Task 2: Single-operation route lifecycle

**Files:** tools/debug/client_route.py; tools/debug/test_client_route.py; tools/debug/README.md

- [x] Tests first with transport snapshots/receipts: accepted-before-arrived, expected generation transition, transient loading, wrong area/identity, manual destination override, controller lock and timeout.
- [x] Implement a file session holding the same native navigation lock for the entire route, plus a pure route runner and CLI accepting final town/area/x/y and actual PVF.
- [x] Keep old navigation API unchanged; final152 pytest debug tests/134 subtests GREEN; independent source review CFE6B98B no remaining P1/P2. Fresh-send200ms budget and explicit pre-dispatch expiry renewal added from native evidence; accepted/unknown requests never renewed. Full-repository pytest:450 passed,182 subtests,6 known baseline failures (three ledger12-vs23 assertions; three Windows excluded-port2311 bindings), retained separately. Scoped consumer commit8ba4b88 frozen before final native acceptance.

### Task 3: Native acceptance and handoff

- [x] Single existing native destination automatically crossed1/0→1/1; old consumer returnedcontext_changed (RED).
- [x] One final goal command per run: A/B multi-area forward and reverse, final position/direction, receipt/context history and request count. Four final natural runs cross4 portals each,13 internal native requests, zero manual travel inputs. Direction-key interruption stops continuation; controlled consumer write delay produces a real expired rejection, one fresh-ID renewal and final arrival.
- [x] Preserve five original native screenshots and public summary; U06 scoped to two towns/five areas. See knowledge/decisions/2026-10-29-debug-cross-area-verification.md.
- [x] Stop only exact journal-owned processes; archive V phase, restore2 normal configs/8 input files, verify15 protected inputs,50 normal entry files with approved native lineage,4+4 DLLs and6 frozen consumer files. U04 remains the next restoration/acceptance task, not marked complete by this delivery.
