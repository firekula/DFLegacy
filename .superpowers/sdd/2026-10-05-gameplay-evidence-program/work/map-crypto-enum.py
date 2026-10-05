#!/usr/bin/env python3
r"""map-crypto-enum.py —— 把 CreateCryptoGraph 的跳转表配上"每个分支实例化哪个密码类"。

做法：读跳转表得到 14 个**基本块起始地址**；再在工厂函数体内找出所有
`call <构造函数>`（`...C2Ev`/`...C1Ev`）；每个跳转目标归属**其后最近的那个构造函数**。

用法: map-crypto-enum.py <elf> <factory_va> <factory_size> <table_va> <count>
"""
from __future__ import annotations

import struct
import subprocess
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
from capstone.x86 import X86_OP_IMM                   # noqa: E402


def main() -> int:
    elf = m.Elf(sys.argv[1])
    fva, fsize, tva, n = (int(sys.argv[2], 0), int(sys.argv[3]),
                          int(sys.argv[4], 0), int(sys.argv[5]))

    syms = [x for x in elf.func_symbols() if x["value"]]
    names = [x["name"] for x in syms]
    p = subprocess.run(["docker", "exec", "-i", "dfl-recon", "c++filt"],
                       input="\n".join(names).encode(), capture_output=True)
    dem = p.stdout.decode("utf-8", "replace").splitlines()
    # ⚠️ 要**同时**留 mangled 与 demangled：构造函数标记 `C1E`/`C2E` 只在 mangled 里，
    #    在 demangled 里是 `X::X()`，所以只查 demangled 会一个都认不出（上一版就错在这）。
    byva = {}
    for x, d in zip(syms, dem):
        byva.setdefault(x["value"], (x["name"], d))

    tab_off = elf.va_to_off(tva)
    targets = [struct.unpack_from("<I", elf.data, tab_off + 4 * i)[0] for i in range(n)]

    foff = elf.va_to_off(fva)
    md = Cs(CS_ARCH_X86, CS_MODE_32)
    md.detail = True
    ctors = []
    for ins in md.disasm(elf.data[foff:foff + fsize], fva):
        if ins.mnemonic == "call" and ins.operands and ins.operands[0].type == X86_OP_IMM:
            t = ins.operands[0].imm
            mang, dm = byva.get(t, ("", ""))
            if "C1E" in mang or "C2E" in mang:      # 构造函数（mangled 标记）
                ctors.append((ins.address, t, dm or mang))

    print(f"工厂 0x{fva:08x}({fsize} B)  跳转表 0x{tva:08x}({n} 项)")
    print(f"工厂内构造函数调用 {len(ctors)} 个\n")
    print(f"  {'枚举值':>6}  {'跳转目标':<12} 实例化的类")
    mapped = []
    for i, tgt in enumerate(targets):
        # 归属：>= 目标地址 的最近一个构造函数调用
        cand = [c for c in ctors if c[0] >= tgt]
        nm = cand[0][2] if cand else "（目标之后没有构造函数调用——可能是 default）"
        bare = nm.split("(")[0]
        mapped.append((i, tgt, bare))
        print(f"  {i:>6}  0x{tgt:08x}  {bare[:80]}")

    print()
    print("=== 汇总（枚举值 -> 密码类）===")
    for i, tgt, bare in mapped:
        print(f"  {i:>2} -> {bare}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
