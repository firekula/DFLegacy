#!/usr/bin/env python3
r"""dbmw-protocol.py —— 从符号里枚举 dbmw 的**消息词汇表**与 id 映射结构。

## 为什么这个可行（与 game 的区别）

game 那套的难点是"行为"（要读伪码、要推断语义）。dbmw 这套**符号自带语义**：
`CDBManager` 的方法名就是操作名，参数类型名就是包名 ——
例如 `CDBManager::GuildSecede(Packet_DB_Request_Guild_Secede*, ...)`。
⇒ **消息词汇表可以直接从符号表枚举**，不需要先反编译。

## 本脚本产出

1. 全部 `Packet_*` 类型名（去重、按域归类）—— 这就是**线协议的词汇表**
2. 含 id/enum 的类型名（`ENUM_*`），找 id 命名空间
3. `CDBManager` 的方法清单（= 操作清单）
4. `*Instance` / `*Server` 类（进程内的角色类）

用法: dbmw-protocol.py <elf> [<out.txt>]
"""
from __future__ import annotations

import re
import subprocess
import sys
from collections import Counter, defaultdict
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

from importlib.util import module_from_spec, spec_from_file_location   # noqa: E402

_spec = spec_from_file_location("elfrecon", HERE / "elfrecon.py")
m = module_from_spec(_spec)
sys.modules["elfrecon"] = m
_spec.loader.exec_module(m)

CXXFILT = ["docker", "exec", "-i", "dfl-recon", "c++filt"]


def main() -> int:
    elf = m.Elf(sys.argv[1])
    out_path = Path(sys.argv[2]) if len(sys.argv) > 2 else None
    syms = elf.func_symbols()
    names = [s["name"] for s in syms]
    proc = subprocess.run(CXXFILT, input="\n".join(names).encode(), capture_output=True)
    dem = proc.stdout.decode("utf-8", "replace").splitlines()
    lines: list[str] = []

    def emit(s=""):
        lines.append(s)
        print(s)

    emit(f"dbmw 符号分析：{len(names)} 个函数符号")

    # 1) Packet_* 类型
    pkt = Counter()
    for d in dem:
        for mm in re.finditer(r"\b(Packet_[A-Za-z0-9_]+)", d):
            pkt[mm.group(1)] += 1
    emit(f"\n{'='*84}")
    emit(f"1. 包类型（Packet_*）共 {len(pkt)} 种 —— **这就是线协议的词汇表**")
    emit(f"{'='*84}")
    # 按第二段（域）归类
    bydom = defaultdict(list)
    for p, n in pkt.items():
        parts = p.split("_")
        dom = parts[1] if len(parts) > 1 else "?"
        bydom[dom].append((p, n))
    for dom in sorted(bydom, key=lambda k: -len(bydom[k])):
        emit(f"\n  --- 域 {dom}（{len(bydom[dom])} 种）---")
        for p, n in sorted(bydom[dom]):
            emit(f"    {p:<58} 出现 {n}")

    # 2) ENUM_* 类型
    enums = Counter()
    for d in dem:
        for mm in re.finditer(r"\b(ENUM_[A-Za-z0-9_]+)", d):
            enums[mm.group(1)] += 1
    emit(f"\n{'='*84}")
    emit(f"2. 枚举类型（ENUM_*）共 {len(enums)} 种 —— id 命名空间在这里")
    emit(f"{'='*84}")
    for e, n in sorted(enums.items(), key=lambda kv: -kv[1]):
        emit(f"    {e:<48} 出现 {n}")

    # 3) CDBManager 方法
    meth = []
    for d in dem:
        mm = re.match(r"CDBManager::([A-Za-z0-9_~]+)\(", d)
        if mm:
            meth.append((mm.group(1), d))
    uniq = sorted({x[0] for x in meth})
    emit(f"\n{'='*84}")
    emit(f"3. CDBManager 方法（去重 {len(uniq)} 个）—— **这就是操作清单**")
    emit(f"{'='*84}")
    for name in uniq:
        sig = next(d for n, d in meth if n == name)
        emit(f"    {name}")

    # 4) 角色类
    roles = sorted({x for d in dem for x in re.findall(r"\b(C[A-Z][A-Za-z0-9_]*(?:Server|Manager|Instance|Application|Connector))\b", d)})
    emit(f"\n{'='*84}")
    emit(f"4. 进程内角色类（C*Server/Manager/Instance/Application）共 {len(roles)} 个")
    emit(f"{'='*84}")
    for r in roles:
        emit(f"    {r}")

    if out_path:
        out_path.write_text("\n".join(lines) + "\n", encoding="utf-8")
        print(f"\n-> {out_path}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
