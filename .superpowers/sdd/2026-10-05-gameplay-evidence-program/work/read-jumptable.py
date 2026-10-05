#!/usr/bin/env python3
r"""read-jumptable.py —— 读 switch 跳转表，把枚举值映射到目标地址，并给目标起名。

用法: read-jumptable.py <elf> <table_va> <count>
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


def main() -> int:
    elf = m.Elf(sys.argv[1])
    table_va = int(sys.argv[2], 0)
    n = int(sys.argv[3])

    syms = [x for x in elf.func_symbols() if x["value"]]
    names = [x["name"] for x in syms]
    p = subprocess.run(["docker", "exec", "-i", "dfl-recon", "c++filt"],
                       input="\n".join(names).encode(), capture_output=True)
    dem = p.stdout.decode("utf-8", "replace").splitlines()
    byva = {}
    for x, d in zip(syms, dem):
        byva.setdefault(x["value"], d)

    off = elf.va_to_off(table_va)
    print(f"跳转表 VA 0x{table_va:08x} -> 文件偏移 {'0x%x' % off if off is not None else '无'}")
    if off is None:
        return 2
    raw = elf.data[off:off + 4 * n]
    print(f"表项 {n} 个\n")
    print(f"  {'值':>4}  {'目标 VA':<12} 目标函数（demangle 后）")
    for i in range(n):
        tgt = struct.unpack_from("<I", raw, i * 4)[0]
        nm = byva.get(tgt, "")
        print(f"  {i:>4}  0x{tgt:08x}  {nm[:88]}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
