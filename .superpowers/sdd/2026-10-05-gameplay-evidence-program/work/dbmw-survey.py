#!/usr/bin/env python3
r"""dbmw-survey.py —— 摸 df_dbmw_r 的符号结构（它是与 game 完全不同的代码基）。

## 已知的线索（来自它的运行日志）

```
PacketProcess: id(4202) count(163) / id(6072) / id(9024) / id(10218) / id(4152)   ← 线协议 opcode
QueryCount:    id(20244) / id(20300) / id(20012) / id(20036) / id(20186)          ← DB 查询 id
DBQueryErr:    selectCollectItems (Row_Data Not Exist)                             ← 查询具名
MysqlErr:      inSert into log_query_stat(occ_time,q_id,total,response_time) values(now(),20036,...)
```

⇒ 存在**两套 id 命名空间**：packet id（几千）与 query id（两万级）。
本脚本从符号里找出它们的承接结构。

用法: dbmw-survey.py <elf> [<关键字>]
"""
from __future__ import annotations

import re
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


def main() -> int:
    elf = m.Elf(sys.argv[1])
    kw = sys.argv[2] if len(sys.argv) > 2 else None
    syms = elf.func_symbols()
    names = [s["name"] for s in syms]

    print(f"函数符号 {len(names)} 个")
    print()

    if kw:
        print(f"=== 名字含 {kw!r} 的符号（前 60）===")
        hits = [n for n in names if kw.lower() in n.lower()]
        print(f"命中 {len(hits)} 个")
        for n in hits[:60]:
            print(f"  {n}")
        return 0

    # 按"名字前缀/类名"聚类，看有哪些大类
    print("=== 按类名/命名空间聚类（取前 2 段的出现次数）===")
    c = Counter()
    for n in names:
        n2 = re.sub(r"\(.*\)$", "", n)
        parts = re.split(r"::", n2)
        key = "::".join(parts[:2]) if len(parts) > 1 else parts[0].split("(")[0]
        c[key] += 1
    for k, v in c.most_common(45):
        print(f"  {v:>5}  {k}")

    print()
    print("=== 关键词扫描（找协议/查询/派发的承接结构）===")
    for pat in ["Packet", "packet", "Query", "query", "Dispatch", "dispatch",
                "Handler", "handler", "Process", "Recv", "Send", "Table", "table",
                "Load", "QueryId", "PacketId", "Config"]:
        hits = [n for n in names if pat in n]
        if hits:
            print(f"\n  [{pat}] {len(hits)} 个，前 8：")
            for n in hits[:8]:
                print(f"     {n}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
