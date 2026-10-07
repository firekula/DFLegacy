#!/usr/bin/env python3
r"""e2-context.py —— 从 `insns.txt` 里按地址打印一段上下文（E2 的"读代码"步）。

用法:
  python e2-context.py <insns> --at 0x573cdd --before 40 --after 60
  python e2-context.py <insns> --fn-of 0x573cdd      # 反向找函数起点（启发式：最近的 ret/对齐边界）
"""
from __future__ import annotations

import argparse
from pathlib import Path


def load(path: Path):
    ins = []
    with path.open(encoding="utf-8", errors="replace") as f:
        for line in f:
            p = line.rstrip("\r\n").split("\t")
            if len(p) == 3:
                ins.append((int(p[0], 16), p[1], p[2]))
    return ins


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("insns")
    ap.add_argument("--at", type=lambda x: int(x, 0), required=True)
    ap.add_argument("--before", type=int, default=40)
    ap.add_argument("--after", type=int, default=60)
    ap.add_argument("--mark", nargs="*", type=lambda x: int(x, 0), default=[])
    args = ap.parse_args()

    ins = load(Path(args.insns))
    idx = {a: i for i, (a, m, o) in enumerate(ins)}
    if args.at not in idx:
        cands = [a for a in idx if a <= args.at]
        if not cands:
            print("not found")
            return 1
        near = max(cands)
        print(f"; {args.at:#x} 不在指令边界，取最近 {near:#x}")
        args.at = near
    i = idx[args.at]
    for k in range(max(0, i - args.before), min(len(ins), i + args.after)):
        a, m, o = ins[k]
        mark = "   <<<" if a == args.at else ("   <--" if a in args.mark else "")
        print(f"{a:#010x}  {m:<9} {o}{mark}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
