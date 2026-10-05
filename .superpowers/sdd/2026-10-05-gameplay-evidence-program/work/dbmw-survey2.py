#!/usr/bin/env python3
r"""dbmw-survey2.py —— 先 demangle 再摸结构（上一版没 demangle，聚类全错，留痕）。

## 上一版的错

`dbmw-survey.py` 直接对 **mangled** 名字做 `::` 聚类与关键词扫描，结果：
* 聚类把 `_Z41__static_initialization_and_destruction_0ii` 当成"类名"（它根本不是）
* 关键词扫描 0 命中 —— mangled 名字里当然搜不到 `Query`/`Packet`

教训与 M0 的 Task 2 一样：**先 demangle，再谈结构**。

## 做法

1. 从 ELF 抽函数符号（addr/size/name）
2. 用 sidecar `dfl-recon` 的 `c++filt` 批量 demangle（与 `demangle-syms.py` 同路径）
3. 在 demangled 名字上做聚类与关键词扫描

用法: dbmw-survey2.py <elf> [<关键词>]
"""
from __future__ import annotations

import re
import subprocess
import sys
from collections import Counter
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

from importlib.util import module_from_spec, spec_from_file_location   # noqa: E402

_spec = spec_from_file_location("elfrecon", HERE / "elfrecon.py")
m = module_from_spec(_spec)
sys.modules["elfrecon"] = m
_spec.loader.exec_module(m)

CXXFILT = ["docker", "exec", "-i", "dfl-recon", "c++filt"]


def demangle(names: list[str]) -> list[str]:
    proc = subprocess.run(CXXFILT, input="\n".join(names).encode(), capture_output=True)
    if proc.returncode != 0:
        raise SystemExit(f"c++filt 失败: {proc.stderr[:300]!r}")
    out = proc.stdout.decode("utf-8", "replace").splitlines()
    if len(out) != len(names):
        raise SystemExit(f"行数不一致 {len(out)} vs {len(names)}")
    return out


def main() -> int:
    elf_path = sys.argv[1]
    kw = sys.argv[2] if len(sys.argv) > 2 else None
    elf = m.Elf(elf_path)
    syms = elf.func_symbols()
    names = [s["name"] for s in syms]
    print(f"函数符号 {len(names)} 个；正在 demangle…")
    dem = demangle(names)
    for s, d in zip(syms, dem):
        s["dem"] = d
    print("完成\n")

    if kw:
        hits = [(s, d) for s, d in zip(syms, dem) if kw.lower() in d.lower()]
        print(f"=== demangled 名含 {kw!r} 的符号：{len(hits)} 个（按 size 排序，前 50）===")
        for s, d in sorted(hits, key=lambda x: -x[0]["size"])[:50]:
            print(f"  0x{s['value']:08x}({s['size']:>6})  {d}")
        return 0

    print("=== 按类名（demangled 的 `::` 前两段）聚类 ===")
    c = Counter()
    for d in dem:
        d2 = re.sub(r"\(.*\)$", "", d)
        parts = re.split(r"::", d2)
        key = "::".join(parts[:2]) if len(parts) > 1 else parts[0]
        c[key] += 1
    for k, v in c.most_common(40):
        print(f"  {v:>5}  {k}")

    print()
    print("=== 关键词扫描（demangled 上）===")
    for pat in ["Packet", "Query", "Dispatch", "Handler", "Recv", "Send",
                "Load", "Config", "Table", "Process", "DB", "Mysql", "Session"]:
        hits = [d for d in dem if pat in d]
        if hits:
            print(f"\n  [{pat}] {len(hits)} 个，前 6：")
            for d in hits[:6]:
                print(f"     {d[:120]}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
