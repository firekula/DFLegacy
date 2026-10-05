#!/usr/bin/env python3
"""game-scan-imm.py — 在 ELF32 的可执行节里找“立即数/位移 == 目标地址”的指令。

与 extra-table-scan.py 同一套扫描方式（按节，不按段）——按段扫会失步且静默返回空。

与计划骨架的差异（additive，契约不变）：每条命中后附 `in <所属函数>`，
  用 FUNC 符号表 + bisect 求，避免逐条线扫符号表。命中归属对 Step 3/4 判读是必需的。
  归属不到就写 `in sub_<地址>`（函数起点未知）。

用法: game-scan-imm.py <elf> --targets 0xVA [0xVA ...] [--limit N]
"""
import argparse
import bisect
import sys
from importlib.util import module_from_spec, spec_from_file_location
from pathlib import Path

from capstone import Cs, CS_ARCH_X86, CS_MODE_32
from capstone.x86 import X86_OP_IMM, X86_OP_MEM

HERE = Path(__file__).resolve().parent
ELFREcon = HERE / "elfrecon.py"   # Task 4 已把它从 $W 复制到本目录


def func_index(elf):
    """(starts, funcs) —— 供 bisect 查“某地址落在哪个 FUNC 里”。"""
    funcs = sorted((s for s in elf.func_symbols() if s["value"] and s["size"]),
                   key=lambda s: s["value"])
    return [s["value"] for s in funcs], funcs


def enclosing(starts, funcs, va):
    i = bisect.bisect_right(starts, va) - 1
    if i >= 0:
        s = funcs[i]
        if s["value"] <= va < s["value"] + s["size"]:
            return s["name"]
    return None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("elf")
    ap.add_argument("--targets", nargs="+", type=lambda x: int(x, 0), required=True)
    ap.add_argument("--limit", type=int, default=8)
    a = ap.parse_args()

    spec = spec_from_file_location("elfrecon", ELFREcon)
    m = module_from_spec(spec); spec.loader.exec_module(m)
    elf = m.Elf(a.elf)
    starts, funcs = func_index(elf)
    md = Cs(CS_ARCH_X86, CS_MODE_32); md.detail = True
    hits = {t: [] for t in a.targets}
    for sec in elf.sections:
        if not (sec["flags"] & 0x4) or sec["type"] == 8 or sec["size"] == 0:
            continue
        blob = elf.data[sec["offset"]:sec["offset"] + sec["size"]]
        for ins in md.disasm(blob, sec["addr"]):
            for op in ins.operands:
                v = op.imm if op.type == X86_OP_IMM else (op.mem.disp if op.type == X86_OP_MEM else None)
                if v in hits:
                    fn = enclosing(starts, funcs, ins.address) or f"sub_{ins.address:x}"
                    hits[v].append((ins.address, f"{ins.mnemonic} {ins.op_str}", fn))
    for t in a.targets:
        print(f"== 0x{t:x}: {len(hits[t])} hit(s)")
        for addr, txt, fn in hits[t][:a.limit]:
            print(f"   0x{addr:x}  {txt}   in {fn}")


if __name__ == "__main__":
    sys.exit(main())
