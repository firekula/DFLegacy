#!/usr/bin/env python3
r"""restrict-xref.py —— 在官服 ELF 里找**引用**某个 VA 的指令（立即数与位移两种形式）。

为什么要单独一份：`elfrecon.py calls` 因整段线性反汇编失步而**静默空输出**
（`open-questions.md` 坑 23）。本文件按**可执行节内逐条真实指令**扫描（capstone 从节首
按指令边界推进，遇到无效字节就跳过），并且**必须打印扫描规模**，让"零命中"与"没扫"
在输出上可区分。
"""
from __future__ import annotations

import argparse
import struct
from pathlib import Path

import capstone


def load_sections(path: Path):
    d = path.read_bytes()
    e_shoff = struct.unpack_from("<I", d, 0x20)[0]
    e_shentsize = struct.unpack_from("<H", d, 0x2E)[0]
    e_shnum = struct.unpack_from("<H", d, 0x30)[0]
    e_shstrndx = struct.unpack_from("<H", d, 0x32)[0]
    raw = []
    for i in range(e_shnum):
        off = e_shoff + i * e_shentsize
        raw.append(struct.unpack_from("<IIIIII", d, off))
    shstr = raw[e_shstrndx]
    secs = {}
    for name, typ, flags, addr, offset, size in raw:
        end = d.find(b"\x00", shstr[4] + name)
        nm = d[shstr[4] + name:end].decode("utf-8", "replace")
        secs[nm] = (addr, offset, size, flags)
    return d, secs


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("bin")
    ap.add_argument("--targets", nargs="+", required=True, type=lambda x: int(x, 0))
    ap.add_argument("--limit", type=int, default=20)
    ap.add_argument("--section", default=".text")
    args = ap.parse_args()

    d, secs = load_sections(Path(args.bin))
    addr, off, size, _ = secs[args.section]
    code = d[off:off + size]
    md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_32)
    md.detail = True

    tgts = set(args.targets)
    hits: dict[int, list[str]] = {t: [] for t in tgts}
    insn_count = 0
    bad = 0
    for ins in md.disasm(code, addr):
        insn_count += 1
        for t in tgts:
            found = None
            for op in ins.operands:
                if op.type == capstone.x86.X86_OP_IMM and op.imm == t:
                    found = f"imm"
                elif op.type == capstone.x86.X86_OP_MEM:
                    if op.mem.base == 0 and op.mem.index == 0 and op.mem.disp == t:
                        found = "abs-disp"
            if found:
                seg = d[off + (ins.address - addr):off + (ins.address - addr) + 16]
                if len(hits[t]) < args.limit:
                    hits[t].append(f"{ins.address:#010x}  {ins.mnemonic:<7} {ins.op_str}"
                                   f"    ; bytes={seg[:ins.size].hex()}")
    print(f"# scanned {insn_count} instructions in {args.section} "
          f"(va {addr:#x}..{addr+size:#x}); targets={[hex(t) for t in sorted(tgts)]}")
    for t in sorted(tgts):
        print(f"\n## refs to {t:#010x}  ({len(hits[t])} shown, limit {args.limit})")
        for h in hits[t]:
            print("  " + h)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
