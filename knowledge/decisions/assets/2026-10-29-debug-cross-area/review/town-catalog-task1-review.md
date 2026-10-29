# Task1 town catalog/planner review — 5aa3cc2

Read-only review of `5aa3cc2f8dd69af95e277ddb184bf932e24e3e48`, base `413c556598e64227a75b64353054124eaf36002f`, in `D:/codex_worktree/debug-town-navigation/DFLegacy`.
Scope: `tools/debug/town_navigation.py`, `tools/debug/test_town_navigation.py`, comparison with existing `PvfArchiveReader.cs` and previously extracted original native movement. No source/index/HEAD edits, live control, process reads/writes, deployment, or new agents.

## Important findings (all P2; fixes requested before integration)

### 1. Right/bottom closed bounds disagree with native legal coordinates

Location: `tools/debug/town_navigation.py:56..57`, also `_visible` at `99..106`.

`contains` and the segment clipping include `x+width` / `y+height`. Original `776C60`, invoked by `479EF0`, uses `< right`, `< bottom`. For one rectangle `(0,0,20,20)`, `waypoints((1,0),(10,10),(20,10))` returns `((20,10),)` although native rejects that destination. `(20,2)->(20,18)` along the entirely illegal right edge is also accepted. Apply the half-open semantics to endpoint and path validation, including degenerate horizontal/vertical legs; merely making portal centres interior does not protect user goals or simplified legs.

### 2. Direct visibility bypasses corner-disconnection policy

Location: `tools/debug/town_navigation.py:166..167` (with the same shortcut at `196`).

Rectangles `[(0,0,20,20),(20,20,20,20)]`, start `(10,10)`, target `(30,30)`: `_intersection` correctly returns `None`, but `_visible` returns true, so the planner returns only the target before connectivity is checked. Native independently probes axes: at `(19,19)` neither `(20,19)` nor `(19,20)` is legal, so it cannot enter the second rectangle. This is not repaired solely by making `contains` half-open: the mathematical diagonal can still cross through `(20,20)`, whereas native axis steps cannot. Connectivity/native-leg validation must remain authoritative over simplification.

### 3. An arbitrary visible straight segment is not the native motion trajectory

Location: `tools/debug/town_navigation.py:192..199`, and initial shortcut `166..167`.

Rectangles `[(0,0,100,100),(90,20,110,20),(180,20,220,80)]`, start `(10,10)`, target `(310,60)`: the mathematical straight segment stays entirely inside the **half-open** union, and the current planner returns only `((310,60),)`. Original `4795F0` instead selects signed x/y speeds (diagonal factors 186 and 122), not the line's required slope. At that diagonal ratio it reaches target y=60 when x≈86.23, still inside the initial chamber. Y becomes idle; X then hits the chamber boundary at x=99, because `(100,60)` lies outside the connecting corridor. It cannot travel back to y=20..39 without an intermediate destination. Thus line-of-sight simplification removes a native-essential turn independently of the first two boundary defects.

Native evidence: `4795F0..479892` axis-directed final-goal steering, `99D6F4=186.0`, `99D6EC=122.0`; `4C9FA9 -> 4C8C00`, `4C8EBC -> owner.v+94`, `4C8F32` returns the blocked axis to its last legal coordinate. These are static source/PE deductions; no live controller experiment was performed. A conservative full start/end AABB-in-union condition is a reasonable sufficient criterion, or retain legs proved within a shared rectangle. New tests should cover both the early direct-return and later turn-removal paths.

## Checks and non-findings

- `python -B -m unittest tools.debug.test_town_navigation -v`: **11 tests passed**, including installed original Script.pvf SHA `288d58aace8ed4724a0a94aaa4cd0957b1d52c633771132ede2f96363c1b506f`.
- Real catalog: **29 areas, 68 portals**. Independently checked every portal point against half-open source portal and walkable rectangles; all are valid. Checked every rectangle-centre-to-portal route: **207 returned paths, 0 no_path, 0 illegal returned waypoints**. These endpoint checks do not prove each native leg traversable, which is why finding 3 remains material.
- Encryption matches the existing C# operation exactly: each little-endian dword XOR key, then rotate right by six; the same tree/body padding and CP949 directory convention are used. Bounds are checked before decoding body spans. Python deliberately rejects duplicates/unsafe paths/oversized entries more strictly than the C# reader, whose dictionary allows last entry to win and which ignores identifier uniqueness; this is fail-closed, not an unsafe compatibility defect on the installed archive.
- `route` skips unknown target areas and rejects unknown requested start/goal. It excludes `dungeon_gate` as an intermediate target while allowing an explicit dungeon-gate goal. The focused regression and actual forward/reverse multi-town routes passed. No unrelated routing policy change is requested here.
- Portal intersection midpoint selection requires at least 2x2 overlap and produces an interior integer point in the installed archive. No independently reproduced invalid portal point was found.

## Minor suggestion, not a blocking finding

`_block` at `270..277` compares opening/closing counts but not global tag order. For example, a stray closing tag before a valid block plus a stray opening tag afterward is accepted. A token-order check would make malformed catalog diagnostics stricter. This has no demonstrated effect on the installed PVF and is separate from the three reproduced movement failures.

## Evidence and status

`town-catalog-task1-review-evidence.json` binds the reviewed source commit/file hashes, native factor bytes, exact three reproductions, and test counts. Original instruction bytes/call chain are retained in `native-town-movement-disassembly.txt` and bound by `native-town-navigation-evidence.json` in this same recon directory.

Parent and implementation agent were given the reproductions. This initial commit is **not ready for integration** until the three P2 findings are fixed and independently rechecked. Further revised commits will be reviewed as deltas; this report preserves the original finding evidence.
