#!/usr/bin/env python3
r"""disasm-func.py —— 反汇编 ELF 里指定 VA 的函数，并给出"它调用了谁"的清单。

## 用途

M0 的 `catalog_dispatchers_full.txt` 给的是**家族 → 地址**。要读行为就得反汇编。
本脚本是通用工具（不只给 LOGIN 用）：
* 按 VA + size 反汇编（capstone x86-32）
* 把 `call` 的目标解析成**符号名**（用 ELF 的符号表）
* 把 `call [mem]`（虚调用）也标出来
* 顺带列出函数里引用的**立即数字符串**（`.rodata` 里的可打印串）

用法:
  disasm-func.py <elf> <va> <size> [--calls-only] [--strings] [--limit N]
"""
from __future__ import annotations

import argparse
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

from importlib.util import module_from_spec, spec_from_file_location   # noqa: E402

_spec = spec_from_file_location("elfrecon", HERE / "elfrecon.py")
m = module_from_spec(_spec)
sys.modules["elfrecon"] = m
_spec.loader.exec_module(m)

from capstone import Cs, CS_ARCH_X86, CS_MODE_32      # noqa: E402
from capstone.x86 import X86_OP_IMM, X86_OP_MEM       # noqa: E402


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("elf")
    ap.add_argument("va", type=lambda x: int(x, 0))
    ap.add_argument("size", type=int)
    ap.add_argument("--calls-only", action="store_true")
    ap.add_argument("--strings", action="store_true")
    ap.add_argument("--limit", type=int, default=0, help="只打印前 N 条（0=全部）")
    a = ap.parse_args()

    elf = m.Elf(a.elf)
    off = elf.va_to_off(a.va)
    if off is None:
        print(f"VA 0x{a.va:x} 不落在任何节内")
        return 2
    blob = elf.data[off:off + a.size]
    syms = sorted((s for s in elf.func_symbols() if s["value"]), key=lambda s: s["value"])

    def name_at(va):
        best = None
        for s in syms:
            if s["value"] <= va and (s["size"] == 0 or va < s["value"] + s["size"]):
                if best is None or s["value"] > best["value"]:
                    best = s
        if best is None:
            return None
        d = best["name"]
        return d if len(d) < 90 else d[:87] + "…"

    def rodata_str(va):
        o = elf.va_to_off(va)
        if o is None:
            return None
        end = elf.data.find(b"\x00", o, o + 200)
        if end < 0 or end == o:
            return None
        raw = elf.data[o:end]
        if all(32 <= c < 127 for c in raw) and len(raw) >= 4:
            return raw.decode("ascii")
        return None

    md = Cs(CS_ARCH_X86, CS_MODE_32)
    md.detail = True
    shown = 0
    calls = []
    strings = []
    for ins in md.disasm(blob, a.va):
        line = None
        if ins.mnemonic == "call":
            tgt = ins.operands[0]
            if tgt.type == X86_OP_IMM:
                nm = name_at(tgt.imm)
                calls.append((ins.address, tgt.imm, nm))
                line = f"0x{ins.address:08x}  call 0x{tgt.imm:08x}  {nm or '?'}"
            else:
                calls.append((ins.address, None, "(indirect)"))
                line = f"0x{ins.address:08x}  call {ins.op_str}   (间接)"
        elif not a.calls_only:
            line = f"0x{ins.address:08x}  {ins.mnemonic:<7} {ins.op_str}"
        if a.strings:
            for op in ins.operands:
                if op.type == X86_OP_IMM:
                    s = rodata_str(op.imm)
                    if s:
                        strings.append((ins.address, op.imm, s))
        if line and not a.calls_only:
            if a.limit and shown >= a.limit:
                break
            print(line)
            shown += 1
        elif line and a.calls_only:
            print(line)

    if a.strings and strings:
        print(f"\n=== 引用到的可打印串（{len(strings)} 条）===")
        seen = set()
        for addr, va, s in strings:
            if s in seen:
                continue
            seen.add(s)
            print(f"  0x{addr:08x} -> 0x{va:08x}  {s[:90]!r}")

    if a.calls_only:
        print(f"\n=== 去重后的被调用者（{len({c[2] for c in calls})} 个）===")
        for nm in sorted({c[2] for c in calls if c[2]}, key=str):
            n = sum(1 for c in calls if c[2] == nm)
            print(f"  {n:>3}x  {nm}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
