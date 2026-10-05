#!/usr/bin/env python3
r"""dbmw-frames.py —— 把 dbmw 单向流切帧并给出 id 直方图 + 非心跳帧的样本。

用已定案的 4 字节头：`u16 LE packet_id | u16 LE size(含头) | payload`。

用法: dbmw-frames.py <stream.bin...>
"""
from __future__ import annotations

import struct
import sys
from collections import Counter
from pathlib import Path

HEADER = 4


def frames(b: bytes):
    out, off = [], 0
    while off + HEADER <= len(b):
        fid, size = struct.unpack_from("<HH", b, off)
        if size < HEADER or off + size > len(b):
            break
        out.append((fid, b[off:off + size]))
        off += size
    return out, off


def main() -> int:
    for a in sys.argv[1:]:
        p = Path(a)
        b = p.read_bytes()
        fr, used = frames(b)
        print("=" * 78)
        print(f"{p.name}  {len(b)} B  {len(fr)} 帧  铺满={used == len(b)}（消费 {used}）")
        print("=" * 78)
        hist = Counter(fid for fid, _ in fr)
        print(f"  packet id 直方图（{len(hist)} 种）:")
        for fid, n in hist.most_common():
            print(f"    id={fid:<6} {n:>5} 次")
        # 非 4202 的样本
        others = [(i, fid, f) for i, (fid, f) in enumerate(fr) if fid != 4202]
        if others:
            print(f"\n  非 4202 的帧（{len(others)} 个），前 10 个:")
            for i, fid, f in others[:10]:
                print(f"    #{i}  id={fid}  size={len(f)}  {f.hex(' ')}")
                asc = "".join(chr(c) if 32 <= c < 127 else "." for c in f[HEADER:])
                print(f"          payload ascii: {asc[:60]}")
        else:
            print("\n  **全是 id=4202（心跳）—— 本次没抓到业务报文**")
        # 4202 的载荷是否恒定
        p4202 = {f[HEADER:] for fid, f in fr if fid == 4202}
        print(f"\n  4202 的载荷形态: {len(p4202)} 种")
        for x in list(p4202)[:4]:
            print(f"    {x.hex(' ')}")
        print()
    return 0


if __name__ == "__main__":
    sys.exit(main())
