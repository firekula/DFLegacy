#!/usr/bin/env python3
"""elfrecon.py — a dependency-light ELF32 static recon helper.

Why this exists: the container has no readelf/objdump/strings/gdb, and these
three programs (df_coserver_r / df_manager_r / df_channel_r) are stripped, so
symbols cannot name anything. The one thing that survives stripping is the
string table, and in this codebase almost every interesting string is a *log
line* (see topology.md: `[!] Server environment(channel) script loading :`).
So the workhorse is: find a string -> find the code that references its
address -> that instruction's enclosing function is the anchor.

Subcommands:
  info   <elf>                       ELF header + section table + symbol counts
  strings <elf> [--min N] [--grep RE]  strings with their virtual addresses
  xref   <elf> --va 0xADDR [--window N]  find code referencing a VA
  xrefs  <elf> --grep RE [--min N]   same, for every string matching RE
  calls  <elf> --va 0xADDR           direct call/jmp targets inside a function
"""
import argparse
import re
import struct
import sys
from pathlib import Path

try:
    from capstone import Cs, CS_ARCH_X86, CS_MODE_32, CS_OP_IMM, CS_OP_MEM
except ImportError:  # pragma: no cover
    print("ERROR: capstone not installed (pip install capstone)", file=sys.stderr)
    raise

PT_LOAD, SHT_SYMTAB, SHT_DYNSYM, SHT_STRTAB, SHT_NOBITS = 1, 2, 11, 3, 8
STT_FUNC = 2


class Elf:
    def __init__(self, path):
        self.path = Path(path)
        self.data = self.path.read_bytes()
        if self.data[:4] != b"\x7fELF":
            raise ValueError("not an ELF file")
        if self.data[4] != 1:
            raise ValueError("not ELF32 (this project's targets are i386)")
        (self.e_type, self.e_machine, _ver, self.e_entry, self.e_phoff, self.e_shoff,
         self.e_flags, _ehsize, self.e_phentsize, self.e_phnum, self.e_shentsize,
         self.e_shnum, self.e_shstrndx) = struct.unpack_from("<HHIIIIIHHHHHH", self.data, 16)

        self.sections = []
        for i in range(self.e_shnum):
            off = self.e_shoff + i * self.e_shentsize
            name, stype, flags, addr, offset, size, link, info, align, entsize = \
                struct.unpack_from("<IIIIIIIIII", self.data, off)
            self.sections.append(dict(name_off=name, type=stype, flags=flags, addr=addr,
                                      offset=offset, size=size, link=link, info=info,
                                      align=align, entsize=entsize, index=i))

        shstr = self.sections[self.e_shstrndx]
        base = shstr["offset"]
        for s in self.sections:
            end = self.data.index(b"\0", base + s["name_off"])
            s["name"] = self.data[base + s["name_off"]:end].decode("latin1")
        self.by_name = {s["name"]: s for s in self.sections}

        self.segments = []
        for i in range(self.e_phnum):
            off = self.e_phoff + i * self.e_phentsize
            p_type, p_offset, p_vaddr, p_paddr, p_filesz, p_memsz, p_flags, p_align = \
                struct.unpack_from("<IIIIIIII", self.data, off)
            if p_type == PT_LOAD:
                self.segments.append(dict(offset=p_offset, vaddr=p_vaddr,
                                          filesz=p_filesz, memsz=p_memsz, flags=p_flags))

        self.symbols = self._read_symbols()
        self._md = None

    def _read_symbols(self):
        out = []
        for s in self.sections:
            if s["type"] not in (SHT_SYMTAB, SHT_DYNSYM) or s["entsize"] == 0:
                continue
            strtab = self.sections[s["link"]]
            for i in range(s["size"] // s["entsize"]):
                off = s["offset"] + i * s["entsize"]
                name, value, size, info, other, shndx = struct.unpack_from("<IIIBBH", self.data, off)
                if name == 0:
                    continue
                end = self.data.index(b"\0", strtab["offset"] + name)
                nm = self.data[strtab["offset"] + name:end].decode("latin1")
                out.append(dict(name=nm, value=value, size=size,
                                type=info & 0xF, bind=info >> 4, shndx=shndx,
                                table=s["name"]))
        return out

    def func_symbols(self):
        return [s for s in self.symbols if s["type"] == STT_FUNC]

    # ---- address <-> file offset -------------------------------------------
    def va_to_off(self, va):
        for seg in self.segments:
            if seg["vaddr"] <= va < seg["vaddr"] + seg["filesz"]:
                return seg["offset"] + (va - seg["vaddr"])
        return None

    def off_to_va(self, off):
        for seg in self.segments:
            if seg["offset"] <= off < seg["offset"] + seg["filesz"]:
                return seg["vaddr"] + (off - seg["offset"])
        return None

    def code_ranges(self):
        """Executable segments as (vaddr, bytes)."""
        out = []
        for seg in self.segments:
            if seg["flags"] & 0x1:
                out.append((seg["vaddr"], self.data[seg["offset"]:seg["offset"] + seg["filesz"]]))
        return out

    def executable_intervals(self):
        return [(s["addr"], s["addr"] + s["size"]) for s in self.sections
                if s["flags"] & 0x4 and s["type"] != SHT_NOBITS]

    def find_strings(self, min_len=4):
        """(va, bytes) for NUL-terminated printable runs in loaded sections."""
        out = []
        for seg in self.segments:
            blob = self.data[seg["offset"]:seg["offset"] + seg["filesz"]]
            for m in re.finditer(rb"[\x20-\x7e]{%d,}" % min_len, blob):
                out.append((seg["vaddr"] + m.start(), m.group()))
        return out

    def md5(self):
        if self._md is None:
            import hashlib
            self._md = hashlib.md5(self.data).hexdigest()
        return self._md


def disasm(elf, va_start, size, detail=True):
    md = Cs(CS_ARCH_X86, CS_MODE_32)
    md.detail = detail
    off = elf.va_to_off(va_start)
    if off is None:
        return []
    return list(md.disasm(elf.data[off:off + size], va_start))


def cmd_info(elf, args):
    tname = {1: "REL", 2: "EXEC", 3: "DYN", 4: "CORE"}.get(elf.e_type, elf.e_type)
    print(f"file        : {elf.path}")
    print(f"md5         : {elf.md5()}")
    print(f"size        : {len(elf.data)} bytes")
    print(f"type        : {tname}   machine={elf.e_machine} (3=i386)  entry=0x{elf.e_entry:x}")
    print(f"sections    : {elf.e_shnum}   program headers: {elf.e_phnum}")
    print()
    print(f"{'idx':>3} {'name':<22} {'type':>5} {'addr':>10} {'offset':>9} {'size':>9} flags")
    for s in elf.sections:
        print(f"{s['index']:>3} {s['name']:<22} {s['type']:>5} 0x{s['addr']:08x} {s['offset']:>9} {s['size']:>9} "
              f"{'W' if s['flags'] & 1 else '-'}{'A' if s['flags'] & 2 else '-'}{'X' if s['flags'] & 4 else '-'}")
    print()
    funcs = elf.func_symbols()
    print(f"symbols     : {len(elf.symbols)} total, {len(funcs)} FUNC")
    tables = sorted({s['table'] for s in elf.symbols})
    print(f"sym tables  : {tables or 'NONE (fully stripped)'}")
    if not funcs:
        print("=> no FUNC symbols: every function must be located via string/const anchors.")


def cmd_strings(elf, args):
    pat = re.compile(args.grep.encode()) if args.grep else None
    n = 0
    for va, raw in elf.find_strings(args.min):
        if pat and not pat.search(raw):
            continue
        n += 1
        print(f"0x{va:08x}  {raw.decode('latin1')}")
    print(f"--- {n} strings", file=sys.stderr)


def _refs_to(elf, target, lo=None, hi=None):
    """Instructions whose operand mentions `target` (immediate or displacement)."""
    hits = []
    for vaddr, blob in elf.code_ranges():
        if lo is not None and not (lo <= vaddr < hi):
            continue
        for ins in disasm(elf, vaddr, len(blob)):
            found = False
            for op in ins.operands:
                if op.type == CS_OP_IMM and op.imm == target:
                    found = True
                elif op.type == CS_OP_MEM and op.mem.disp == target:
                    found = True
            if found:
                hits.append(ins)
    return hits


def _enclosing_symbol(elf, va):
    best = None
    for s in elf.func_symbols():
        if s["value"] <= va and (s["size"] == 0 or va < s["value"] + s["size"]):
            if best is None or s["value"] > best["value"]:
                best = s
    return best


def _backtrack_prologue(elf, va, limit=0x2000):
    """Walk back to the nearest plausible function start (int3/nop padding + push ebp)."""
    start = va
    for s, e in elf.executable_intervals():
        if s <= va < e:
            lo = max(s, va - limit)
            break
    else:
        return va
    md = Cs(CS_ARCH_X86, CS_MODE_32)
    md.detail = False
    off = elf.va_to_off(lo)
    insns = list(md.disasm(elf.data[off:off + (va - lo)], lo))
    candidates = [lo]
    for i in range(1, len(insns)):
        prev, cur = insns[i - 1], insns[i]
        if prev.address + prev.size != cur.address:
            candidates.append(cur.address)
        elif prev.mnemonic in ("int3", "nop", "ret"):
            candidates.append(cur.address)
    return candidates[-1]


def cmd_xref(elf, args):
    hits = _refs_to(elf, args.va)
    print(f"references to 0x{args.va:x}: {len(hits)}")
    seen = set()
    for ins in hits:
        sym = _enclosing_symbol(elf, ins.address)
        fn = sym["name"] if sym else f"sub_{_backtrack_prologue(elf, ins.address):x}"
        key = (fn, ins.address)
        if key in seen:
            continue
        seen.add(key)
        print(f"  {ins.address:08x}  {ins.mnemonic:<8} {ins.op_str:<40} in {fn}")


def cmd_xrefs(elf, args):
    pat = re.compile(args.grep.encode())
    for va, raw in elf.find_strings(args.min):
        if not pat.search(raw):
            continue
        hits = _refs_to(elf, va)
        print(f"\n### 0x{va:08x} {raw.decode('latin1')!r}  ({len(hits)} refs)")
        for ins in hits:
            sym = _enclosing_symbol(elf, ins.address)
            fn = sym["name"] if sym else f"sub_{_backtrack_prologue(elf, ins.address):x}"
            print(f"    {ins.address:08x}  {ins.mnemonic:<8} {ins.op_str:<40} in {fn}")


def cmd_syms(elf, args):
    pat = re.compile(args.grep) if args.grep else None
    funcs = elf.func_symbols() if args.funcs else elf.symbols
    funcs = sorted(funcs, key=lambda s: s["value"])
    n = 0
    for s in funcs:
        if pat and not pat.search(s["name"]):
            continue
        n += 1
        print(f"0x{s['value']:08x} {s['size']:>7} {s['table']:<8} {s['name']}")
    print(f"--- {n} symbols shown", file=sys.stderr)


def cmd_dwarf_units(elf, args):
    """List DWARF .debug_info compilation units by scraping their DW_AT_name.

    Independent of .symtab: if both structures name the same functions, the
    'not stripped' conclusion does not rest on a single table.
    """
    sec = elf.by_name.get(".debug_info")
    if not sec:
        print("no .debug_info (this build has no DWARF)")
        return
    blob = elf.data[sec["offset"]:sec["offset"] + sec["size"]]
    # CU headers: unit_length(4) version(2) abbrev_off(4) addr_size(1)
    off, n = 0, 0
    while off + 11 <= len(blob):
        (length,) = struct.unpack_from("<I", blob, off)
        if length == 0 or length == 0xFFFFFFFF:
            break
        version, abbrev_off, addr_size = struct.unpack_from("<HIB", blob, off + 4)
        n += 1
        print(f"CU {n}: off=0x{off:x} len={length} version={version} "
              f"abbrev_off=0x{abbrev_off:x} addr_size={addr_size}")
        off += 4 + length
    print(f"--- {n} compilation units")


def cmd_calls(elf, args):
    insns = []
    for vaddr, blob in elf.code_ranges():
        if vaddr <= args.va < vaddr + len(blob):
            insns = disasm(elf, vaddr, len(blob))
            break
    inside = False
    for ins in insns:
        if ins.address == args.va:
            inside = True
        if not inside:
            continue
        print(f"  {ins.address:08x}  {ins.mnemonic:<8} {ins.op_str}")
        if ins.mnemonic == "ret":
            break


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)

    p = sub.add_parser("info"); p.add_argument("elf"); p.set_defaults(fn=cmd_info)
    p = sub.add_parser("strings"); p.add_argument("elf")
    p.add_argument("--min", type=int, default=6); p.add_argument("--grep", default=None)
    p.set_defaults(fn=cmd_strings)
    p = sub.add_parser("xref"); p.add_argument("elf")
    p.add_argument("--va", type=lambda x: int(x, 0), required=True); p.set_defaults(fn=cmd_xref)
    p = sub.add_parser("xrefs"); p.add_argument("elf")
    p.add_argument("--grep", required=True); p.add_argument("--min", type=int, default=6)
    p.set_defaults(fn=cmd_xrefs)
    p = sub.add_parser("calls"); p.add_argument("elf")
    p.add_argument("--va", type=lambda x: int(x, 0), required=True); p.set_defaults(fn=cmd_calls)
    p = sub.add_parser("syms"); p.add_argument("elf")
    p.add_argument("--grep", default=None); p.add_argument("--funcs", action="store_true")
    p.set_defaults(fn=cmd_syms)
    p = sub.add_parser("dwarf-units"); p.add_argument("elf"); p.set_defaults(fn=cmd_dwarf_units)

    args = ap.parse_args()
    args.fn(Elf(args.elf), args)


if __name__ == "__main__":
    main()
