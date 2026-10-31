#!/usr/bin/env python3
r"""nail-patch-impact.py —— 钉"这 103 字节补丁是否影响 M0 的静态结论"。

## 为什么这条是重写前必须确认的

M0 的全部静态证据（425 家族、1171 条 opcode 映射、7273 条 handler、判据 2/3/4）
都是在 `$O\df_game_r.bin` 上做的。而**实际运行的**是 VM 上那份（同尺寸、差 103 字节）。

若这 103 字节落在派发器代码或那些表上 ⇒ M0 的地址级结论在 VM 上**可能不成立**，
后续单元引用 M0 地址时必须逐个复核。
若都落在别处（符号表字符串、无关代码、配置常量）⇒ M0 的结论对 VM **直接适用**。

## 做什么

1. 把补丁的**文件偏移**换算成 **VA**（用 ELF 的 program header / section 映射）。
2. 逐个补丁，检查它是否落在 `catalog_dispatchers_full.txt` 里任何家族的
   `[process, process+size)` 或 `[dispatch_sig, dispatch_sig+size)` 区间内。
3. 报告落在代码节内的补丁数量，以及是否有任何一条命中派发器区间。

用法: nail-patch-impact.py <vm.bin> <m0.bin> <catalog_dispatchers_full.txt>
"""
from __future__ import annotations

import re
import struct
import sys
from pathlib import Path


def parse_phdrs(data: bytes):
    """返回 [(offset, vaddr, filesz, flags)]（只取 PT_LOAD）。"""
    assert data[:4] == b"\x7fELF"
    is64 = data[4] == 2
    if is64:
        raise SystemExit("本脚本只处理 ELF32（df_game_r 是 32 位）")
    e_phoff = struct.unpack_from("<I", data, 0x1C)[0]
    e_phentsize = struct.unpack_from("<H", data, 0x2A)[0]
    e_phnum = struct.unpack_from("<H", data, 0x2C)[0]
    out = []
    for i in range(e_phnum):
        off = e_phoff + i * e_phentsize
        p_type = struct.unpack_from("<I", data, off)[0]
        if p_type != 1:            # PT_LOAD
            continue
        p_offset = struct.unpack_from("<I", data, off + 4)[0]
        p_vaddr = struct.unpack_from("<I", data, off + 8)[0]
        p_filesz = struct.unpack_from("<I", data, off + 16)[0]
        p_flags = struct.unpack_from("<I", data, off + 24)[0]
        out.append((p_offset, p_vaddr, p_filesz, p_flags))
    return out


def off_to_va(phdrs, off: int):
    for p_off, p_va, p_fsz, _fl in phdrs:
        if p_off <= off < p_off + p_fsz:
            return p_va + (off - p_off)
    return None


def load_families(path: Path):
    fams = []
    rx = re.compile(r"process=0x([0-9a-f]+)\(\s*(\d+)\).*dispatch_sig=0x([0-9a-f]+)\(\s*(\d+)\)")
    for ln in path.read_text(encoding="utf-8").splitlines():
        if not ln or ln.startswith("#"):
            continue
        m = rx.search(ln)
        if not m:
            continue
        fam = ln.split("\t")[0]
        fams.append((fam, int(m.group(1), 16), int(m.group(2)),
                     int(m.group(3), 16), int(m.group(4))))
    return fams


def main() -> int:
    a = Path(sys.argv[1]).read_bytes()
    b = Path(sys.argv[2]).read_bytes()
    cat = Path(sys.argv[3])

    phdrs = parse_phdrs(b)
    print("PT_LOAD 段（文件偏移 -> VA）:")
    for p_off, p_va, p_fsz, p_fl in phdrs:
        kind = "RX" if p_fl & 1 else ("W" if p_fl & 2 else "R")
        print(f"  0x{p_off:08x} -> 0x{p_va:08x}  size=0x{p_fsz:x}  {kind}")

    d = [i for i in range(len(a)) if a[i] != b[i]]
    runs = []
    s = d[0]
    for i in range(1, len(d)):
        if d[i] != d[i - 1] + 1:
            runs.append((s, d[i - 1]))
            s = d[i]
    runs.append((s, d[-1]))

    fams = load_families(cat)
    print(f"\n派发器家族 {len(fams)} 个（来自 catalog_dispatchers_full.txt）")
    print(f"补丁 {len(runs)} 段\n")

    print("逐段：文件偏移 -> VA -> 落在哪 / 是否命中派发器")
    print("=" * 84)
    in_code = 0
    hits = []
    for s, e in runs:
        va = off_to_va(phdrs, s)
        where = "（不在任何 PT_LOAD 内）"
        if va is not None:
            for p_off, p_va, p_fsz, p_fl in phdrs:
                if p_va <= va < p_va + p_fsz:
                    where = "代码段(RX)" if p_fl & 1 else ("数据段(W)" if p_fl & 2 else "只读段(R)")
                    if p_fl & 1:
                        in_code += 1
                    break
        hit = [f for f in fams
               if (f[1] <= (va or 0) < f[1] + f[2]) or (f[3] <= (va or 0) < f[3] + f[4])]
        mark = ""
        if hit:
            mark = "  *** 命中派发器: " + ", ".join(h[0] for h in hit)
            hits.append((s, hit))
        print(f"  0x{s:08x}..0x{e:08x}  VA={('0x%08x' % va) if va else '—':<12} "
              f"{where:<14}{mark}")

    print()
    print("=" * 84)
    print("结论")
    print("=" * 84)
    print(f"  补丁落在**代码段**的: {in_code}/{len(runs)}")
    print(f"  命中派发器区间（process 或 dispatch_sig）的: {len(hits)}")
    if hits:
        for s, h in hits:
            print(f"     0x{s:08x} -> {[x[0] for x in h]}")
        print("  ⇒ **必须逐个复核**受影响的家族，不能直接引用 M0 地址")
    else:
        print("  ⇒ 没有任何补丁落在派发器区间 ⇒ **M0 的派发器/表结论对 VM 直接适用**")
    return 0


if __name__ == "__main__":
    sys.exit(main())
