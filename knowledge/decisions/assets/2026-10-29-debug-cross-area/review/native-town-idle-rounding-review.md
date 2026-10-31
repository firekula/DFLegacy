# Native town idle coordinate rounding

Read-only disk PE analysis on 2026-10-29. Original EXE `G:/Game/DNF_ACT1/DF/DNF.exe`, independently hashed SHA256 `6b4850beaf67ec61adb04ae17f119f16278dd05e97b8ffca871a9bdb547de0d5`.

`4795F0` has **no three-pixel native stop threshold**. Its x/y direction decisions compare integer coordinate getters to the final integer destination. Equal values produce direction 4 (idle); strict crossing also permits its existing overshoot snap path.

Getter chain, supported ordinary no-active-provider Town actor:

- x `v+84=407460` and y `v+88=407470` call `v+80=471610` with axis 0/1.
- `471610` reads actor controller `+E0` and jumps to `4C9BF0`.
- `4C9C1B` obtains the vector float component and `4C9C21` calls `433940`.
- `433940` compares the float to zero (`A92C84=0.0`), adds `B0DF4C=0.5` for nonnegative values or subtracts 0.5 for negatives, then jumps to `91B708` (x87 conversion with correction to truncation toward zero). This is nearest integer, half values away from zero.
- `4796EC..479719` and `47979D..4797C7` compare those integer x/y values with actor `+23C/+240`; equality selects idle. No 3.0-distance comparison occurs in this direction update.

For a positive target integer n, raw values approximately in `[n-0.5,n+0.5)` map to n. For a negative target n the half-boundary inclusion is reversed because ties round away from zero; zero has open endpoints `(-0.5,0.5)`. The input components are float32; exact half cases follow the above native arithmetic.

The reported live raw position `(782.58197,195.941757)` maps to `(783,196)`, so native idle at target `(783,196)` is expected. In the PVF's one-pixel overlap y=196..197, raw y=195.94 remains in the incoming rectangle while already rounding to 196. A fresh raw-coordinate planner can therefore keep returning the same boundary point, which native will consider reached without moving farther into the next rectangle.

A larger internal target margin, such as the SDK's three-pixel observed-arrival tolerance or a four-pixel bridge depth, is a conservative design choice, **not an original native constant**. It must keep the possible terminal raw box in the intended outgoing region and retain full native-leg AABB/foreign-portal safety. Do not alter the user's final coordinate or write actor positions to mask this boundary behavior. Thin rectangles or short shared edges without room for the chosen margin need explicit handling or conservative refusal.

The controller's active-provider branch (`4C9C01..4C9C07`) delegates coordinate reading to another object's v+28; no claim is made about that mode. The debug state reader currently rejects active-provider coordinates for the supported ordinary path.

Exact bytes, the zero/half constants, direct half-constant references and the full steering/getter/conversion functions are saved in `native-town-idle-rounding-disassembly.txt`. No process access or live action was performed by this review.
