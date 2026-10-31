#!/usr/bin/env python3
r"""e2-frame-fingerprint.py —— 用**登录帧字段上限的组合**在客户端里聚类候选构造点。

## 指纹（全部来自静态消费端与已解出的明文，`login-tail-static.txt` / `login-plain-dump.json`）

登录载荷的字段上限是**一组很少同时出现的数字**：

```
token:    get_str(..., 2049 = 0x801, len)      ← 全局缓冲 memset(0x940a2e0,0,0x801)
string1:  get_str(...,   24 = 0x18 , len)
string2:  get_str(...,  132 = 0x84 , len)
flag:     < 3
mouse:    len == 8
```

⇒ 一个函数里**同时**出现 `0x18`、`0x84`、`8`（且附近有 `3`）就是**强候选**。
本脚本按"滑动窗口内的立即数集合"聚类，窗口以"锚点指令"为起点前后各 N 条。

## 纪律
候选 ≠ 结论：命中后必须读该函数的反汇编/反编译确认它真的在写登录载荷。
"""
from __future__ import annotations

import argparse
import re
from collections import defaultdict
from pathlib import Path

NUM = re.compile(r"(?<![0-9a-fx])(0x[0-9a-f]+|-?\d+)(?![0-9a-f])", re.I)


def load(path: Path):
    ins = []
    with path.open(encoding="utf-8", errors="replace") as f:
        for line in f:
            p = line.rstrip("\r\n").split("\t")
            if len(p) == 3:
                ins.append((int(p[0], 16), p[1], p[2]))
    return ins


def imms(ins, a, b):
    s = set()
    for k in range(a, b):
        for m in NUM.finditer(ins[k][2]):
            try:
                s.add(int(m.group(1), 0))
            except ValueError:
                pass
    return s


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("insns")
    ap.add_argument("--half", type=int, default=60)
    ap.add_argument("--anchor", type=lambda x: int(x, 0), default=0x84)
    args = ap.parse_args()

    ins = load(Path(args.insns))
    print(f"# instructions={len(ins)}  anchor={args.anchor:#x}  half={args.half}")

    # 锚点 = 操作数里带 0x84 的指令
    anchors = [i for i, (a, m, o) in enumerate(ins)
               if re.search(r"(^|,\s*)(0x84|132)$", o, re.I)]
    print(f"# 锚点（含 0x84/132 的指令）= {len(anchors)}")

    want = {0x18, 0x84, 8}
    scored = []
    for i in anchors:
        s = imms(ins, max(0, i - args.half), min(len(ins), i + args.half + 1))
        score = len(want & s)
        if score >= 3:
            scored.append((score, i, s))

    # 合并相邻（同一函数内会重复命中）
    scored.sort()
    merged = []
    for score, i, s in scored:
        if merged and i - merged[-1][1] <= args.half * 2:
            merged[-1] = (max(score, merged[-1][0]), i, s)
        else:
            merged.append((score, i, s))

    print(f"# 三条同时命中的簇 = {len(merged)}")
    for score, i, s in merged[:40]:
        a, m, o = ins[i]
        print(f"\n=== 簇 @ {a:#010x} (score={score}) anchor: {m} {o}")
        print(f"    窗口内立即数: {sorted(hex(x) for x in s if x < 0x100000)}")
        print(f"    含 0x158(344)? {0x158 in s}  含 0x84? {0x84 in s}  含 0x18? {0x18 in s}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
