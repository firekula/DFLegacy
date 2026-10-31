#!/usr/bin/env python3
r"""dwarf-enum.py —— 从 32 位 ELF 的 `.debug_info` 里抽枚举的**名字与取值**。

## 为什么要自己写

容器/宿主都没有 pyelftools（`python -c "import elftools"` 失败），而 `df_game_r.bin` 带完整 DWARF。
官方自己的枚举定义是**最强的一手证据**：它同时给出枚举名与 `DW_AT_const_value`，
不需要猜 `.rodata` 里的字符串顺序，也不需要反汇编初始化器。

只实现需要的最小集合：CU 头、 abbrev 表、`DW_TAG_enumeration_type` / `DW_TAG_enumerator`。
"""
from __future__ import annotations

import argparse
import struct
from pathlib import Path

# --- DWARF 常量（只列用到的） ---
DW_TAG_enumerator = 0x28
DW_TAG_enumeration_type = 0x04
DW_AT_name = 0x03
DW_AT_const_value = 0x1C
DW_AT_byte_size = 0x0B
DW_AT_type = 0x49
DW_FORM_addr = 0x01
DW_FORM_block2 = 0x03
DW_FORM_block4 = 0x04
DW_FORM_data2 = 0x05
DW_FORM_data4 = 0x06
DW_FORM_data8 = 0x07
DW_FORM_string = 0x08
DW_FORM_block = 0x09
DW_FORM_block1 = 0x0A
DW_FORM_data1 = 0x0B
DW_FORM_flag = 0x0C
DW_FORM_sdata = 0x0D
DW_FORM_strp = 0x0E
DW_FORM_udata = 0x0F
DW_FORM_ref_addr = 0x10
DW_FORM_ref1 = 0x11
DW_FORM_ref2 = 0x12
DW_FORM_ref4 = 0x13
DW_FORM_ref8 = 0x14
DW_FORM_ref_udata = 0x15
DW_FORM_indirect = 0x16
DW_FORM_sec_offset = 0x17
DW_FORM_exprloc = 0x18
DW_FORM_flag_present = 0x19
DW_FORM_strx = 0x1A
DW_FORM_addrx = 0x1B
DW_FORM_ref_sup4 = 0x1C
DW_FORM_strp_sup = 0x1D
DW_FORM_data16 = 0x1E
DW_FORM_line_strp = 0x1F
DW_FORM_ref_sig8 = 0x20
DW_FORM_implicit_const = 0x21
DW_FORM_loclistx = 0x22
DW_FORM_rnglistx = 0x23
DW_FORM_ref_sup8 = 0x24
DW_FORM_strx1 = 0x25
DW_FORM_strx2 = 0x26
DW_FORM_strx3 = 0x27
DW_FORM_strx4 = 0x28
DW_FORM_addrx1 = 0x29
DW_FORM_addrx2 = 0x2A
DW_FORM_addrx3 = 0x2B
DW_FORM_addrx4 = 0x2C

FORM_SIZE = {
    DW_FORM_data1: 1, DW_FORM_flag: 1, DW_FORM_ref1: 1, DW_FORM_addrx1: 1,
    DW_FORM_strx1: 1, DW_FORM_data2: 2, DW_FORM_ref2: 2, DW_FORM_addrx2: 2, DW_FORM_strx2: 2,
    DW_FORM_data4: 4, DW_FORM_ref4: 4, DW_FORM_sec_offset: 4, DW_FORM_strp: 4,
    DW_FORM_addr: 4, DW_FORM_ref_addr: 4, DW_FORM_addrx4: 4, DW_FORM_strx4: 4,
    DW_FORM_data8: 8, DW_FORM_ref8: 8, DW_FORM_ref_sig8: 8,
    DW_FORM_strx3: 3, DW_FORM_addrx3: 3,
}


def sections(path: Path):
    d = path.read_bytes()
    e_shoff = struct.unpack_from("<I", d, 0x20)[0]
    e_shentsize = struct.unpack_from("<H", d, 0x2E)[0]
    e_shnum = struct.unpack_from("<H", d, 0x30)[0]
    e_shstrndx = struct.unpack_from("<H", d, 0x32)[0]
    raw = [struct.unpack_from("<IIIIII", d, e_shoff + i * e_shentsize) for i in range(e_shnum)]
    shstr = raw[e_shstrndx]
    out = {}
    for name, typ, flags, addr, offset, size in raw:
        end = d.find(b"\x00", shstr[4] + name)
        out[d[shstr[4] + name:end].decode("utf-8", "replace")] = (offset, size)
    return d, out


class Reader:
    def __init__(self, d: bytes, off: int):
        self.d = d
        self.off = off

    def u8(self):
        v = self.d[self.off]
        self.off += 1
        return v

    def u16(self):
        v = struct.unpack_from("<H", self.d, self.off)[0]
        self.off += 2
        return v

    def u32(self):
        v = struct.unpack_from("<I", self.d, self.off)[0]
        self.off += 4
        return v

    def u64(self):
        v = struct.unpack_from("<Q", self.d, self.off)[0]
        self.off += 8
        return v

    def bytes(self, n):
        v = self.d[self.off:self.off + n]
        self.off += n
        return v

    def cstr(self):
        end = self.d.find(b"\x00", self.off)
        v = self.d[self.off:end].decode("utf-8", "replace")
        self.off = end + 1
        return v

    def uleb(self):
        r = 0
        s = 0
        while True:
            b = self.u8()
            r |= (b & 0x7F) << s
            if not (b & 0x80):
                return r
            s += 7

    def sleb(self):
        r = 0
        s = 0
        while True:
            b = self.u8()
            r |= (b & 0x7F) << s
            s += 7
            if not (b & 0x80):
                if b & 0x40:
                    r -= 1 << s
                return r


def parse_abbrev(d: bytes, off: int):
    r = Reader(d, off)
    table = {}
    while True:
        code = r.uleb()
        if code == 0:
            break
        tag = r.uleb()
        has_children = r.u8()
        attrs = []
        while True:
            at = r.uleb()
            form = r.uleb()
            ic = None
            if form == DW_FORM_implicit_const:
                ic = r.sleb()
            if at == 0 and form == 0:
                break
            attrs.append((at, form, ic))
        table[code] = (tag, has_children, attrs)
    return table


def read_form(r: Reader, form: int, ic, strsec_off: int, strsec_size: int, dbg: bytes):
    if form == DW_FORM_implicit_const:
        return ic
    if form == DW_FORM_string:
        return r.cstr()
    if form == DW_FORM_strp:
        o = r.u32()
        return dbg[strsec_off + o:dbg.find(b"\x00", strsec_off + o)].decode("utf-8", "replace")
    if form == DW_FORM_udata:
        return r.uleb()
    if form == DW_FORM_sdata:
        return r.sleb()
    if form == DW_FORM_flag_present:
        return True
    if form == DW_FORM_exprloc:
        n = r.uleb()
        return r.bytes(n)
    if form == DW_FORM_block1:
        n = r.u8(); return r.bytes(n)
    if form == DW_FORM_block2:
        n = r.u16(); return r.bytes(n)
    if form == DW_FORM_block4:
        n = r.u32(); return r.bytes(n)
    if form == DW_FORM_block:
        n = r.uleb(); return r.bytes(n)
    if form == DW_FORM_data16:
        return r.bytes(16)
    if form in FORM_SIZE:
        return r.bytes(FORM_SIZE[form])
    raise ValueError(f"unhandled form {form:#x}")


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("bin")
    ap.add_argument("--grep", default="Restrict")
    args = ap.parse_args()

    d, secs = sections(Path(args.bin))
    di_off, di_size = secs[".debug_info"]
    ds_off, ds_size = secs["." + "debug_str"]
    ab_off, ab_size = secs[".debug_abbrev"]

    r = Reader(d, di_off)
    end = di_off + di_size
    cu_count = 0
    enums = []
    while r.off < end:
        cu_start = r.off
        length = r.u32()
        if length == 0 or length == 0xFFFFFFFF:
            break
        cu_end = r.off + length
        r.u16()                     # version
        r.u32()                     # abbrev offset
        r.u8()                      # address size
        ab_off_cu = struct.unpack_from("<I", d, cu_start + 6)[0]
        abbrev = parse_abbrev(d, ab_off + ab_off_cu)
        cu_count += 1
        stack: list[tuple[int, str | None]] = []
        cur_enum: dict | None = None
        while r.off < cu_end:
            code = r.uleb()
            if code == 0:
                if stack:
                    stack.pop()
                if cur_enum and not stack:
                    enums.append(cur_enum)
                    cur_enum = None
                continue
            tag, has_children, attrs = abbrev[code]
            vals = {}
            for at, form, ic in attrs:
                vals[at] = read_form(r, form, ic, ds_off, ds_size, d)
            if has_children:
                stack.append((tag, vals.get(DW_AT_name)))
            if tag == DW_TAG_enumeration_type:
                name = vals.get(DW_AT_name)
                if isinstance(name, str) and args.grep.lower() in name.lower():
                    cur_enum = {"name": name,
                                "byte_size": vals.get(DW_AT_byte_size),
                                "values": []}
                else:
                    cur_enum = None
            elif tag == DW_TAG_enumerator and cur_enum is not None:
                nm = vals.get(DW_AT_name)
                cv = vals.get(DW_AT_const_value)
                if isinstance(cv, bytes):
                    cv = int.from_bytes(cv, "little")
                cur_enum["values"].append((nm, cv))

    print(f"# CUs parsed = {cu_count}; enums matching {args.grep!r} = {len(enums)}")
    for en in enums:
        print(f"\n## enum {en['name']} (byte_size={en['byte_size']}, {len(en['values'])} enumerators)")
        for nm, cv in en["values"]:
            print(f"   {cv!s:>6}  {nm}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
