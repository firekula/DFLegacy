#!/usr/bin/env python3
r"""restrict-policy-static.py —— 只读静态分析：RestrictCategory 枚举名与各策略类 getCode()。

## 目的（handoff §4.B）

钉住「category / restrict_code 的业务名称」与 `isRestricted` 的**值语义**，
从而把 VM DB 行值（category=1 code=1..33 value=1；category=3 code=1 value=0）
翻译成业务含义。**不连接 VM、不写任何东西。**

## 输入

`df_game_r.bin`（官服二进制，符号完整；来源 `.superpowers/sdd/2026-09-29-dungeon-party/work/official/`）。

## 方法（两条独立路径）

1. **枚举名表**：`RestrictCategory::enumStringRestrict`（OBJECT @0x093700a0，16 B = 两个 8 B 表）
   由 `_GLOBAL__I__ZN16RestrictCategory12enumToStringENS_4EnumE`（@0x0816ed12）初始化。
   反汇编该静态构造器，读出「索引 -> 字符串地址」的成对常量。
2. **策略类 code**：`ServiceRestrictManager::initialize` 里对每个子类调 `getCode()`；
   子类 `XXRestrictPolicy::getCode()` 都是 `mov eax, imm; ret`，直接读立即数。
   与路径 1 交叉后即可给出 `code -> 名称`。

两条路径独立 ⇒ 一致才算 `[静态]` 级。
"""
from __future__ import annotations

import argparse
import struct
from pathlib import Path

import capstone

SECTIONS = ("text", "rodata", "data", "bss")


class Elf:
    def __init__(self, path: Path):
        self.d = path.read_bytes()
        d = self.d
        assert d[:4] == b"\x7fELF", "not ELF"
        e_shoff = struct.unpack_from("<I", d, 0x20)[0]
        e_shentsize = struct.unpack_from("<H", d, 0x2E)[0]
        e_shnum = struct.unpack_from("<H", d, 0x30)[0]
        e_shstrndx = struct.unpack_from("<H", d, 0x32)[0]
        raw = []
        for i in range(e_shnum):
            off = e_shoff + i * e_shentsize
            name, typ, flags, addr, offset, size = struct.unpack_from("<IIIIII", d, off)
            raw.append((name, typ, flags, addr, offset, size))
        shstr = raw[e_shstrndx]
        self.secs = {}
        for name, typ, flags, addr, offset, size in raw:
            end = d.find(b"\x00", shstr[4] + name)
            nm = d[shstr[4] + name:end].decode("utf-8", "replace")
            self.secs[nm] = dict(addr=addr, off=offset, size=size)
        self.load = [(self.secs[n]["addr"], self.secs[n]["off"], self.secs[n]["size"])
                     for n in (".text", ".rodata", ".data", ".bss") if n in self.secs]

    def va2off(self, va: int):
        for addr, off, size in self.load:
            if addr <= va < addr + size:
                return off + (va - addr)
        return None

    def cstr(self, va: int, cap: int = 200) -> str | None:
        off = self.va2off(va)
        if off is None:
            return None
        end = self.d.find(b"\x00", off, off + cap)
        if end < 0:
            return None
        raw = self.d[off:end]
        if not raw or any(b < 9 or (13 < b < 32) or b > 126 for b in raw):
            return None
        return raw.decode("ascii")

    def u32(self, va: int):
        off = self.va2off(va)
        return None if off is None else struct.unpack_from("<I", self.d, off)[0]

    def disasm(self, va: int, size: int):
        md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_32)
        off = self.va2off(va)
        code = self.d[off:off + size]
        return list(md.disasm(code, va))


def find_sym(path: Path, needle: str):
    """从 symtab 里找地址（避免依赖外部 nm）。"""
    d = path.read_bytes()
    e_shoff = struct.unpack_from("<I", d, 0x20)[0]
    e_shentsize = struct.unpack_from("<H", d, 0x2E)[0]
    e_shnum = struct.unpack_from("<H", d, 0x30)[0]
    found = []
    for i in range(e_shnum):
        off = e_shoff + i * e_shentsize
        _, typ, _, addr, offset, size = struct.unpack_from("<IIIIII", d, off)
        if typ == 2 and size and addr == 0:      # SHT_SYMTAB has addr 0
            strtab = None
            for j in range(e_shnum):
                o2 = e_shoff + j * e_shentsize
                _, t2, _, _, off2, sz2 = struct.unpack_from("<IIIIII", d, o2)
                if t2 == 3:
                    strtab = (off2, sz2)
            if not strtab:
                continue
            cnt = size // 16
            for k in range(cnt):
                so = offset + k * 16
                nm, val, sz, info, other, shndx = struct.unpack_from("<IIIBBH", d, so)
                end = d.find(b"\x00", strtab[0] + nm)
                name = d[strtab[0] + nm:end].decode("utf-8", "replace")
                if needle in name:
                    found.append((name, val, sz))
    return found


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("bin")
    ap.add_argument("--grep", default="RestrictPolicy")
    ap.add_argument("--disasm", type=lambda x: int(x, 0), default=None)
    ap.add_argument("--len", type=int, default=400)
    args = ap.parse_args()

    path = Path(args.bin)
    e = Elf(path)
    print(f"# {path.name} size={len(e.d)}")
    for n in (".text", ".rodata", ".data", ".bss"):
        s = e.secs[n]
        print(f"# {n:9s} va={s['addr']:#010x} off={s['off']:#010x} size={s['size']}")

    print(f"\n## symbols matching {args.grep!r}")
    for name, val, sz in sorted(find_sym(path, args.grep), key=lambda t: t[1]):
        print(f"{val:#010x}  size={sz:<5} {name}")

    print("\n## enumStringRestrict container @0x093700a0 (two 8-byte std::map-like objects)")
    for i in range(2):
        p = e.u32(0x093700a0 + i * 4)
        print(f"  slot[{i}] = {p if p is None else hex(p)}")
        if p is None:
            continue
        for j in range(40):
            q = e.u32(p + j * 4)
            if q is None:
                print(f"    [+{j*4:#04x}] <unmapped>")
                break
            s = e.cstr(q)
            print(f"    [+{j*4:#04x}] {q:#010x} {s!r}" if s else f"    [+{j*4:#04x}] {q:#010x} <not cstr>")

    if args.disasm:
        print(f"\n## disasm {args.disasm:#x} len={args.len}")
        for ins in e.disasm(args.disasm, args.len):
            print(f"{ins.address:#010x}  {ins.mnemonic:<8} {ins.op_str}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
