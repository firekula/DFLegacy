#!/usr/bin/env python3
r"""nail-header-scan.py —— 扫"哪个偏移上的 2 字节字段是逐帧递增的序号"。

## 为什么这么扫

`nail-header13.py` 发现：
  * **C→S**：`raw[11..12]`(LE) = `0,1,2,…,20`，**严格 +1，100%** ⇒ 那里是序号
  * **S→C**：同样位置的取值是随机样貌（50455, 4962, …），**0/89 递增**
  * 且 `raw[7] == raw[11]` 在 **S→C 是 90/90**、在 C→S 是 0/21

同一份官方 cfg（`siroco11.cfg` L82–89）说头长 13、`checksum` 在 7 长 4、
`sequence` 在 11 长 2。但两个方向的表现不一致 ⇒ **不能假定两个方向同布局**。

本脚本不假定布局，直接**扫描**：对偏移 0..15 的每个位置，取 2 字节 LE，
统计它在一条流里"逐帧 +1"的比例。哪个偏移是 100%，哪个就是序号。
同时对 4 字节位置扫一遍（有些实现用 4 字节序号）。

这是**结构性判据**，不依赖任何先验假设。

用法: nail-header-scan.py -- <stream.bin...>
"""
from __future__ import annotations

import struct
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE / "driver"))

import dnfdrive                        # noqa: E402
from dnfdrive import GAME              # noqa: E402


def frames_by_size(blob: bytes, hdr_guess: int = 10):
    """用 size（在 [3..6]、含整帧）切帧；头长只影响载荷视图，不影响切帧。"""
    out, off = [], 0
    while off + hdr_guess <= len(blob):
        size = struct.unpack_from("<I", blob, off + 3)[0]
        if size < 8 or off + size > len(blob):
            break
        out.append(blob[off:off + size])
        off += size
    return out, off


def main() -> int:
    argv = [a for a in sys.argv[1:] if a != "--"]
    for p in (Path(a) for a in argv):
        blob = p.read_bytes()
        fr, used = frames_by_size(blob)
        direction = "S→C" if p.name.startswith("10013") else "C→S"
        print("=" * 78)
        print(f"[{direction}] {p.name}  {len(fr)} 帧  铺满={used == len(blob)}")
        print("=" * 78)
        if len(fr) < 3:
            continue

        # 2 字节扫描
        print("  2 字节 LE 字段：逐帧 +1 的比例")
        best2 = []
        for o in range(0, 16):
            vals = []
            ok = True
            for f in fr:
                if o + 2 > len(f):
                    ok = False
                    break
                vals.append(struct.unpack_from("<H", f, o)[0])
            if not ok:
                continue
            inc = sum(1 for i in range(1, len(vals)) if vals[i] == (vals[i - 1] + 1) & 0xFFFF)
            pct = inc / (len(vals) - 1) * 100
            best2.append((pct, o, vals[:6]))
        for pct, o, sample in sorted(best2, reverse=True)[:6]:
            flag = "  <<< 递增序号" if pct > 90 else ""
            print(f"    偏移 {o:>2}: {pct:5.1f}%   前 6 值 {sample}{flag}")

        # 4 字节扫描
        print("  4 字节 LE 字段：逐帧 +1 的比例")
        best4 = []
        for o in range(0, 14):
            vals = []
            ok = True
            for f in fr:
                if o + 4 > len(f):
                    ok = False
                    break
                vals.append(struct.unpack_from("<I", f, o)[0])
            if not ok:
                continue
            inc = sum(1 for i in range(1, len(vals)) if vals[i] == (vals[i - 1] + 1) & 0xFFFFFFFF)
            pct = inc / (len(vals) - 1) * 100
            best4.append((pct, o, vals[:4]))
        for pct, o, sample in sorted(best4, reverse=True)[:4]:
            flag = "  <<< 递增序号(4B)" if pct > 90 else ""
            print(f"    偏移 {o:>2}: {pct:5.1f}%   前 4 值 {sample}{flag}")

        # 再看：每帧"取值恒定"的偏移（常量字段）
        print("  逐帧恒定的偏移（常量字段）:")
        const = []
        for o in range(0, 16):
            vals = {f[o] for f in fr if o < len(f)}
            if len(vals) == 1:
                const.append((o, vals.pop()))
        print(f"    {[(o, hex(v)) for o, v in const]}")
        print()
    return 0


if __name__ == "__main__":
    sys.exit(main())
