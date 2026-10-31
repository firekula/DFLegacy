#!/usr/bin/env python3
r"""nail-patch-sections.py —— 用**节表**（而不是 program header）精确分类那 47 处补丁。

## 为什么必须重做

`nail-patch-impact.py` 用 PT_LOAD 分类，得到"47/47 落在代码段" —— 这个结论**不精确**：
该 PT_LOAD（RX，文件偏移 0..0x1320e61）里同时装着 `.text`、`.rodata`、`.strtab`、
`.symtab` 等。实测第一处补丁（0x1d258）的上下文就是 `_ZSt18_Rb_tree_increment`，
那是**符号名字符串**，不是代码。把字符串表叫"代码段"是错的分类。

用节表才能回答真正的问题："哪些补丁改的是可执行代码、哪些只是字符串/数据"。

用法: nail-patch-sections.py <vm.bin> <m0.bin>
"""
from __future__ import annotations

import struct
import sys
from pathlib import Path


def sections(data: bytes):
    assert data[:4] == b"\x7fELF" and data[4] == 1, "只处理 ELF32"
    e_shoff = struct.unpack_from("<I", data, 0x20)[0]
    e_shentsize = struct.unpack_from("<H", data, 0x2E)[0]
    e_shnum = struct.unpack_from("<H", data, 0x30)[0]
    e_shstrndx = struct.unpack_from("<H", data, 0x32)[0]

    def name(idx):
        sh = e_shoff + e_shstrndx * e_shentsize
        strtab_off = struct.unpack_from("<I", data, sh + 0x10)[0]
        end = data.index(b"\x00", strtab_off + idx)
        return data[strtab_off + idx:end].decode("latin1")

    out = []
    for i in range(e_shnum):
        sh = e_shoff + i * e_shentsize
        sh_name = struct.unpack_from("<I", data, sh)[0]
        sh_type = struct.unpack_from("<I", data, sh + 4)[0]
        sh_flags = struct.unpack_from("<I", data, sh + 8)[0]
        sh_addr = struct.unpack_from("<I", data, sh + 12)[0]
        sh_offset = struct.unpack_from("<I", data, sh + 16)[0]
        sh_size = struct.unpack_from("<I", data, sh + 20)[0]
        out.append({
            "idx": i, "name": name(sh_name), "type": sh_type, "flags": sh_flags,
            "addr": sh_addr, "offset": sh_offset, "size": sh_size,
            "exec": bool(sh_flags & 0x4), "alloc": bool(sh_flags & 0x2),
        })
    return out


def main() -> int:
    a = Path(sys.argv[1]).read_bytes()
    b = Path(sys.argv[2]).read_bytes()
    secs = sections(b)

    print("节表（有实体内容的）:")
    for s in secs:
        if s["type"] in (8,) or s["size"] == 0:
            continue
        print(f"  {s['name']:<20} off=0x{s['offset']:08x} size=0x{s['size']:<9x} "
              f"addr=0x{s['addr']:08x} {'EXEC' if s['exec'] else ('ALLOC' if s['alloc'] else '')}")

    d = [i for i in range(len(a)) if a[i] != b[i]]
    runs = []
    s0 = d[0]
    for i in range(1, len(d)):
        if d[i] != d[i - 1] + 1:
            runs.append((s0, d[i - 1]))
            s0 = d[i]
    runs.append((s0, d[-1]))

    def which(off):
        for s in secs:
            if s["type"] == 8 or s["size"] == 0:
                continue
            if s["offset"] <= off < s["offset"] + s["size"]:
                return s
        return None

    print()
    print("=" * 84)
    print("逐段：落在哪个**节**")
    print("=" * 84)
    from collections import Counter
    cnt = Counter()
    for s, e in runs:
        sec = which(s)
        nm = sec["name"] if sec else "（无节）"
        kind = "**可执行代码**" if (sec and sec["exec"]) else (".rodata/字符串" if nm in (".rodata", ".strtab", ".symtab", ".dynstr") else "数据/其它")
        cnt[(nm, kind)] += 1
        ctx = "".join(chr(c) if 32 <= c < 127 else "." for c in b[max(0, s - 6):e + 7])
        print(f"  0x{s:08x}..0x{e:08x}  {nm:<14} {kind:<14} ctx={ctx}")

    print()
    print("=" * 84)
    print("汇总：按节")
    print("=" * 84)
    for (nm, kind), n in cnt.most_common():
        print(f"  {nm:<16} {kind:<16} {n} 段")
    exec_n = sum(n for (nm, k), n in cnt.items() if k == "**可执行代码**")
    print(f"\n  ⇒ 真正改到**可执行代码**的补丁: {exec_n}/{len(runs)} 段")
    print(f"  ⇒ 其余是字符串表/只读数据/符号表 —— 不影响代码语义")
    return 0


if __name__ == "__main__":
    sys.exit(main())
