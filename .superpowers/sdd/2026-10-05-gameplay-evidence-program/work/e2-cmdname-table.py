#!/usr/bin/env python3
r"""e2-cmdname-table.py —— 用**命令名表**（`0xB54430`）把"remap 下标"翻译成命令名。

项目既有结论（`friends-protocol-recon.md:17`、`trade-protocol-recon-2.md:46`）：
  * `0xB54430` 是**命令名指针数组**，`表[id]` 给出该 id 的 `ENUM_CMDPACKET_*` 字符串；
  * `0xB54170` 是**通知名**数组（同构）；
  * `trade-protocol-recon-2.md` 实测"命令名表覆盖 0..162（161=ANTIBOT, 162=DPROTO），163..183=CMD_NO_NAME"。

本脚本把它整张 dump 出来（含越界后的 `CMD_NO_NAME` 现象），用于把
`0x00417480` 那张跳转表的**下标**翻译成命令名，从而回答"CMD 1 的 handler 是哪一格"。
"""
from __future__ import annotations

import argparse
import re
import struct
from pathlib import Path

import pefile


def cstr(pe: pefile.PE, base: int, va: int, cap: int = 128) -> str:
    try:
        raw = pe.get_data(va - base, cap)
    except Exception:
        return "<unmapped>"
    end = raw.find(b"\x00")
    if end < 0:
        end = len(raw)
    try:
        return raw[:end].decode("ascii")
    except UnicodeDecodeError:
        return "<non-ascii>"


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("exe")
    ap.add_argument("--table", type=lambda x: int(x, 0), default=0xB54430)
    ap.add_argument("--noti", type=lambda x: int(x, 0), default=0xB54170)
    ap.add_argument("--count", type=int, default=200)
    ap.add_argument("--filter", default=None, help="只打含该子串的名字")
    args = ap.parse_args()

    pe = pefile.PE(args.exe, fast_load=True)
    base = pe.OPTIONAL_HEADER.ImageBase
    print(f"# image_base={base:#x}")

    for label, tbl in (("CMD", args.table), ("NOTI", args.noti)):
        print(f"\n## {label} 名表 @ {tbl:#x}")
        for i in range(args.count):
            try:
                p = struct.unpack("<I", pe.get_data(tbl - base + i * 4, 4))[0]
            except Exception:
                break
            nm = cstr(pe, base, p)
            if args.filter and args.filter.lower() not in nm.lower():
                continue
            flag = ""
            if nm in ("CMD_NO_NAME", "NOTI_NO_NAME") or "NO_NAME" in nm:
                flag = "   <-- 无名（可能是表尾/越界）"
            print(f"  [{i:3d}] {p:#010x}  {nm}{flag}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
