#!/usr/bin/env python3
r"""xref-fast.py —— 快速找"谁调用了某个地址"（E8/E9 rel32 原始字节扫描）。

## 为什么不用 game-scan-imm.py

那个工具用 capstone **全量反汇编**每个可执行节（约 50 秒/次，M0 时测过）。
找 `call target` 其实只需要找 `E8 <rel32>` 且 `addr+5+rel32 == target` ——
纯字节匹配，毫秒级。本工具也顺手覆盖 `E9`（jmp rel32，尾调用）与 `0F 8x`（条件跳转）。

代价：**不做反汇编校验**，所以可能把"恰好长得像 E8 rel32 的数据"算进来。
因此输出里对每个命中都给出**所在函数**（用符号表判定），方便人工复核。

用法: xref-fast.py <elf> <target_va> [--from-va A --to-va B] [--kind call|jmp|jcc|all]
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
    target = int(sys.argv[2], 0)
    kind = "all"
    if "--kind" in sys.argv:
        kind = sys.argv[sys.argv.index("--kind") + 1]

    syms = sorted((s for s in elf.func_symbols() if s["value"]),
                  key=lambda s: s["value"])
    names = [s["name"] for s in syms]
    p = subprocess.run(["docker", "exec", "-i", "dfl-recon", "c++filt"],
                       input="\n".join(names).encode(), capture_output=True)
    dem = p.stdout.decode("utf-8", "replace").splitlines()
    funcs = [(s["value"], s["size"], d) for s, d in zip(syms, dem)]

    def owner(va):
        best = None
        for v, sz, d in funcs:
            if v <= va and (sz == 0 or va < v + sz):
                if best is None or v > best[0]:
                    best = (v, d)
        return best[1] if best else "?"

    hits = []
    for sec in elf.sections:
        if not (sec["flags"] & 0x4) or sec["type"] == 8 or sec["size"] == 0:
            continue
        base = sec["addr"]
        blob = elf.data[sec["offset"]:sec["offset"] + sec["size"]]
        for i in range(len(blob) - 4):
            op = blob[i]
            size = 0
            if op == 0xE8 and kind in ("call", "all"):
                size = 5
            elif op == 0xE9 and kind in ("jmp", "all"):
                size = 5
            elif op == 0x0F and 0x80 <= blob[i + 1] <= 0x8F and kind in ("jcc", "all"):
                size = 6
                rel = struct.unpack_from("<i", blob, i + 2)[0]
                va = base + i
                if va + size + rel == target:
                    hits.append((va, op, "jcc"))
                continue
            if size == 5:
                rel = struct.unpack_from("<i", blob, i + 1)[0]
                va = base + i
                if va + size + rel == target:
                    hits.append((va, op, "call" if op == 0xE8 else "jmp"))

    print(f"xref -> 0x{target:08x}   共 {len(hits)} 处")
    seen_owner = {}
    for va, op, k in hits:
        o = owner(va)
        seen_owner.setdefault(o, []).append(va)
    print(f"分布在 {len(seen_owner)} 个函数里：\n")
    for o, vas in sorted(seen_owner.items(), key=lambda kv: -len(kv[1])):
        print(f"  {len(vas):>3}x  {o[:96]}")
        print(f"        {', '.join('0x%08x' % v for v in vas[:8])}"
              f"{' …' if len(vas) > 8 else ''}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
