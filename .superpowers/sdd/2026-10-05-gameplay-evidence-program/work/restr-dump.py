#!/usr/bin/env python3
r"""restr-dump.py —— 导出官服 `ServiceRestrictManager` 策略类的 vtable / getCode / isRestricted。

只读。输出给「category=1 的 restrict_value=1 到底是允许还是拒绝」这个问题当依据。
"""
from __future__ import annotations

import argparse
import struct
from pathlib import Path

import capstone

SEC = None
DATA = None


def load(path: Path):
    global SEC, DATA
    d = path.read_bytes()
    e_shoff = struct.unpack_from("<I", d, 0x20)[0]
    e_shentsize = struct.unpack_from("<H", d, 0x2E)[0]
    e_shnum = struct.unpack_from("<H", d, 0x30)[0]
    e_shstrndx = struct.unpack_from("<H", d, 0x32)[0]
    raw = [struct.unpack_from("<IIIIII", d, e_shoff + i * e_shentsize) for i in range(e_shnum)]
    shstr = raw[e_shstrndx]
    SEC = {}
    for name, typ, flags, addr, offset, size in raw:
        end = d.find(b"\x00", shstr[4] + name)
        nm = d[shstr[4] + name:end].decode("utf-8", "replace")
        SEC[nm] = (addr, offset, size)
    DATA = d
    return d


def va2off(va):
    for nm in (".text", ".rodata", ".data", ".bss", ".data.rel.ro", ".init_array"):
        if nm not in SEC:
            continue
        a, o, s = SEC[nm]
        if a <= va < a + s:
            return o + (va - a)
    return None


def u32(va):
    o = va2off(va)
    return None if o is None else struct.unpack_from("<I", DATA, o)[0]


def syms(path: Path):
    d = DATA
    e_shoff = struct.unpack_from("<I", d, 0x20)[0]
    e_shentsize = struct.unpack_from("<H", d, 0x2E)[0]
    e_shnum = struct.unpack_from("<H", d, 0x30)[0]
    raw = [struct.unpack_from("<IIIIII", d, e_shoff + i * e_shentsize) for i in range(e_shnum)]
    strtab = None
    symtab = None
    for name, typ, flags, addr, offset, size in raw:
        if typ == 3:
            strtab = (offset, size)
        if typ == 2:
            symtab = (offset, size)
    out = {}
    if not symtab or not strtab:
        return out
    for k in range(symtab[1] // 16):
        so = symtab[0] + k * 16
        nm, val, sz, info, other, shndx = struct.unpack_from("<IIIBBH", d, so)
        end = d.find(b"\x00", strtab[0] + nm)
        name = d[strtab[0] + nm:end].decode("utf-8", "replace")
        if name:
            out.setdefault(name, (val, sz))
    return out


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("bin")
    ap.add_argument("--vtables", nargs="*", default=[])
    ap.add_argument("--funcs", nargs="*", default=[])
    ap.add_argument("--len", type=int, default=160)
    ap.add_argument("--grep", default=None)
    args = ap.parse_args()

    load(Path(args.bin))
    S = syms(Path(args.bin))
    md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_32)

    if args.grep:
        print(f"## symbols matching {args.grep!r}")
        for n, (v, s) in sorted(S.items(), key=lambda kv: kv[1][0]):
            if args.grep in n:
                print(f"{v:#010x} size={s:<6} {n}")
        return 0

    for tgt in args.vtables:
        va = int(tgt, 0)
        print(f"\n## vtable @ {va:#010x}")
        for i in range(16):
            p = u32(va + i * 4)
            if p is None:
                break
            nm = next((n for n, (v, s) in S.items() if v == p and s), None)
            print(f"  [{i}] {p:#010x} {nm or ''}")

    for tgt in args.funcs:
        va = int(tgt, 0)
        print(f"\n## func @ {va:#010x} (first {args.len} bytes)")
        o = va2off(va)
        for ins in md.disasm(DATA[o:o + args.len], va):
            print(f"{ins.address:#010x}  {ins.mnemonic:<9} {ins.op_str}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
