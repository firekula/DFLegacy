# Native Town Route Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans or superpowers:subagent-driven-development to execute task by task.

**Goal:** N-map right click selects one final Town target and the client DLL autonomously reaches it across areas.

**Architecture:** Read-only bounded C++ PVF catalog/rectangle planner; main-thread route controller using original native destination leaves; right-button map hit reader; worker-only catalog/command/status IO. Preserve old DLNV and Python route consumer.

**Tech Stack:** C++17/Win32, MSBuild, existing native unit test executable, Python consumer/tests and native IJL acceptance.

**Spec:** docs/superpowers/specs/2026-10-29-native-town-route-design.md

## Constraints and review focus

- No external OS input, business packet injection, direct state seeding, original file mutation or rewriting frozen evidence.
- No IO/waiting lock on game thread; only proved native leaves set destinations.
- Test raw/rounded birth-door reentry, thin joins and whole-leg foreign portals.
- Test right-release root capture versus child/NPC/menu/hidden cached map and rear world handling.
- Test controller takeover/manual stop/unknown actor or expected map transitions; requested never equals arrived.

### Task 1: Native catalog and geometry (independent)

Files: new src/DFLegacy.Debug/src/town_route_catalog.h/.cpp and tests/town_route_catalog_tests.cpp. Root owns vcxproj/test entry integration.

Interface namespace dfl::town_route: Key{int town,area}; Point{double x,y}; Waypoint{int x,y}; Rect{x,y,width,height,Contains(double,double)}; Portal{Key source,target;Rect rect;Waypoint point}; Area{Key key;string mapPath,kind;vector<Rect> walkable;vector<Portal> portals}; Catalog{vector<Area> areas;string pvfSha256; Find(Key) const->const Area*; Route(Key,Key,vector<Portal>&,string&error) const->bool; Waypoints(Key,Point,Waypoint,const vector<Portal>&blocked,vector<Waypoint>&,string&error) const->bool}; LoadCatalog(const wchar_t*,Catalog&,string&) noexcept->bool; constexpr PortalBlockMargin=4.

- [x] RED tests encrypted synthetic archive bounds/tags, graph, half-open geometry, old float-stop counterexample, foreign-door subtraction, real PVF29/68 and Python-equivalent route/waypoints.
- [x] Implement read-only parser/planner; GREEN, standalone native build/run, independent review and scoped commit.

### Task 2: Map right-button target reader (independent)

Files: client_navigation.h/.cpp and client_navigation_tests.cpp. Preserve ReadCanvasTarget left-release API. Add CanvasGoal{int town,area,x,y}; ReadRouteCanvasTarget(reader,snapshot,minimap,CanvasGoal&,eventBefore) noexcept. Right release is captured after original8F6C30 at8F6FAF (event0x10→0, CFD202=0, B55188 exactroot); initialFD9-at79D242 assumption was disproved by native RED; not left fields; goal may be any unique displayed region. Native static bytes/provenance go into new evidence namespace.

- [x] Prove original fields/ABIs and ground/background exclusion from original PE.
- [x] RED tests right versus left release, other displayed region, root/child/NPC/overlap/hidden/plane shift protection; GREEN and scoped review.

### Task 3: Native route lifecycle and integration (root)

Files: new town_route_controller.h/.cpp/tests; client_navigation_runtime.cpp; DLL/test vcxproj and test entry.

- [x] Tests first: expected-map continuation, whole route final coordinates, missing/stale/changed identity, manual stop/new target, birth escape/no progress/timeouts, catalog readiness and legacy arbitration.
- [x] Implement pure state machine over catalog/snapshots and callbacks; worker-load catalog, main-thread valid goal ingress and bounded progression through original destination operations.
- [x] Add independent96-byteDLRT wire/status worker IO and Python client_native_route.py consumer; wrong origin/expiry/replay rejects without taking over active valid goal.
- [x] Wire right-map goals; retain left behavior and original UI/capture. Observe target status through exported data, no screenshot polling loop.
- [x] Native full suite and Python debug/full-repo checks; report existing ledger/Windows-port baseline failures by name. Independent source review and frozen DLL provenance.

### Task 4: Native dual-client acceptance and delivery

- [x] New isolated X runtime, exact PID/path/start journal and original/protected baselines; no mutation of previous frozen namespaces.
- [x] Both clients use native N-map/right click for same/cross area forward/reverse; final position/context/status and native original screenshots. No external Python route controller needed for positive UI runs.
- [x] Verify right click on NPC/controls/outside does not choose goal; direction keys/manual target takeover and closing/reopening map do not replay stale goal.
- [x] Preserve original images/receipts/tests and scoped coverage; restore owned configs/input files, normal DLL provenance and verify protected inputs; commit delivery.

Delivery evidence: knowledge/decisions/2026-10-29-native-town-route-verification.md. Source f6a9eb9; frozen DLL5E5. Full repository Python retains6 documented baseline failures.
