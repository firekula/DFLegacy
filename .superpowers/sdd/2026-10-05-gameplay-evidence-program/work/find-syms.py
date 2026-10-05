#!/usr/bin/env python3
r"""find-syms.py —— 按关键字列出符号（demangle 后），带地址与大小。

反复需要的小工具：每次想"找含某某的函数"都要重写一遍 python -c，
而 `python -c` 经 PowerShell 传参时引号会被吃掉好几层（本会话已踩多次）。
写成文件就没这问题。

用法: find-syms.py <elf> <关键字> [<关键字2> ...] [--big N]
"""
from __future__ import annotations

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
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    big = 0
    if "--big" in sys.argv:
        big = int(sys.argv[sys.argv.index("--big") + 1])
    elf_path, keys = args[0], args[1:]

    elf = m.Elf(elf_path)
    syms = [x for x in elf.func_symbols() if x["value"]]
    names = [x["name"] for x in syms]
    p = subprocess.run(["docker", "exec", "-i", "dfl-recon", "c++filt"],
                       input="\n".join(names).encode(), capture_output=True)
    dem = p.stdout.decode("utf-8", "replace").splitlines()

    hits = [(x, d) for x, d in zip(syms, dem)
            if any(k.lower() in d.lower() for k in keys)]
    if big:
        hits = [h for h in hits if h[0]["size"] >= big]
    print(f"命中 {len(hits)} 个（关键字 {keys}）")
    for x, d in sorted(hits, key=lambda h: -h[0]["size"]):
        print(f"  0x{x['value']:08x}({x['size']:>6})  {d[:110]}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
