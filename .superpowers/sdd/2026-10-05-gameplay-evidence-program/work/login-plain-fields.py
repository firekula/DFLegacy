#!/usr/bin/env python3
r"""login-plain-fields.py —— 把 `login-plain-dump.json` 里的 400 B 明文按字段切开。

为什么需要：我第一版把 `[373]/[377]` 两个字段**当普通 u32** 读成了
`0x00019302 / 0x0022cf10` 这种"看不出意义"的值，并据此在报告里写了
"与既有报告的 28000201/30000401 不同"。实际上这两个字段是 **packed BCD**
（既有多轮报告写作 `28000201` / `30000401`，而字节是 `28 00 02 01` / `30 00 04 01`），
**不是**普通整数 —— 那正是"读法不同"。本文件把两种读法并排打出来，避免再写错。
"""
from __future__ import annotations

import json
import struct
import sys
from pathlib import Path


def bcd_u32(b: bytes) -> str:
    """按 packed BCD 读（每个 nibble 是一位十进制数），给出人类写法。"""
    digits = []
    for x in b:
        hi, lo = x >> 4, x & 0xF
        if hi > 9 or lo > 9:
            return "<not BCD>"
        digits.append(str(hi))
        digits.append(str(lo))
    return "".join(digits)


def main() -> int:
    src = Path(sys.argv[1] if len(sys.argv) > 1 else
               "out/login-plain-dump.json")
    rows = json.loads(src.read_text(encoding="utf-8"))
    for r in rows:
        ph = r.get("plain_hex")
        if not ph:
            print(f"{r['login_file']}: (no plaintext; selfcheck {r['selfcheck']})")
            continue
        p = bytes.fromhex(ph)
        print(f"\n=== {r['login_file']}  len={len(p)} key={r['key_sha256'][:8]} ===")
        for name, off, n in (("patch", 0, 4), ("token_length", 4, 4),
                             ("string1_length", 352, 4), ("string2_length", 356, 4),
                             ("flag", 360, 1), ("mouse_length", 361, 4),
                             ("mouse", 365, 8),
                             ("DataManager@373", 373, 4), ("version@377", 377, 4),
                             ("server_group", 381, 2), ("local_ip", 383, 4),
                             ("loginout_state", 387, 1)):
            seg = p[off:off + n]
            u32 = struct.unpack_from("<I", seg, 0)[0] if n == 4 else None
            u16 = struct.unpack_from("<H", seg, 0)[0] if n == 2 else None
            bcd = bcd_u32(seg) if n == 4 else ""
            print(f"  [{off:3d}] {name:18s} bytes={seg.hex():16s} "
                  f"u32={'-' if u32 is None else u32:<12} u16={'-' if u16 is None else u16:<6} BCD={bcd}")
        print(f"  [388] tail12={p[388:400].hex()}  all_zero={p[388:400] == bytes(12)}"
              f"  last_nonzero={max((i for i, x in enumerate(p) if x), default=-1)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
