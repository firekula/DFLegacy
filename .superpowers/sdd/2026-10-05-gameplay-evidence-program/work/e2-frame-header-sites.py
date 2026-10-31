#!/usr/bin/env python3
r"""e2-frame-header-sites.py —— 找"把 classification=1 / msg_no=1 写进 13 B 游戏面帧头"的代码。

## 为什么这是对的方向

已确认（见 `e2-login-sender-round1.md`）：
  * LOGIN **不走** `BeginCommand(1)`（234 个发射点里没有 cmd=1）；
  * 游戏面帧头 13 B 的布局（`driver/dnfdrive.py` 的 `GAME` 模型）：
    `[0]classification(1=CMD/0=NOTI) [1]msg_no [2]garvage [3..6]sLength [7..10]checksum [11..12]sequence`。

⇒ 登录包的发送处一定会把 `[0]=1` 与 `[1]=1` 写进同一块缓冲。
本脚本按**字节写指令**的模式找候选：
  `mov byte ptr [<reg>], 1` 紧跟 `mov byte ptr [<reg> + 1], 1`（或相反的次序），
以及 `mov word ptr [<reg>], 0x0101`（一次性写两个字节）。

## 纪律
候选 ≠ 结论：命中后必须读上下文确认它是"构造帧头"而不是别的结构。
"""
from __future__ import annotations

import argparse
import re
from pathlib import Path

# mov byte ptr [eax], 1  /  mov byte ptr [eax + 1], 1  /  mov byte ptr [ecx + 1], 1 …
P_B0 = re.compile(r"^byte ptr \[([a-z0-9]+)(?:\s*\+\s*(0x[0-9a-f]+|\d+))?\]$", re.I)
P_W0 = re.compile(r"^word ptr \[([a-z0-9]+)(?:\s*\+\s*(0x[0-9a-f]+|\d+))?\]$", re.I)


def load(path: Path):
    ins = []
    with path.open(encoding="utf-8", errors="replace") as f:
        for line in f:
            p = line.rstrip("\r\n").split("\t")
            if len(p) == 3:
                ins.append((int(p[0], 16), p[1], p[2]))
    return ins


def off_of(expr: str) -> int | None:
    m = P_B0.match(expr.strip())
    if not m:
        return None
    return int(m.group(2), 0) if m.group(2) else 0


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("insns")
    ap.add_argument("--out", default=None)
    ap.add_argument("--context", type=int, default=8)
    args = ap.parse_args()

    ins = load(Path(args.insns))
    print(f"# instructions={len(ins)}")

    # 1) 一次写两个字节：mov word ptr [reg], 0x101
    word_hits = []
    for i, (a, m, o) in enumerate(ins):
        if m == "mov" and re.match(r"^word ptr \[", o, re.I) and re.search(r",\s*(0x101|257)$", o, re.I):
            word_hits.append((a, i, o))
    print(f"\n# `mov word ptr [reg], 0x101` 命中 = {len(word_hits)}")

    # 2) 相邻两条 `mov byte ptr [reg(+0/+1)], 1`
    byte_hits = []
    for i in range(len(ins) - 1):
        a1, m1, o1 = ins[i]
        a2, m2, o2 = ins[i + 1]
        if m1 != "mov" or m2 != "mov":
            continue
        if not re.search(r",\s*1$", o1) or not re.search(r",\s*1$", o2):
            continue
        l1 = o1.split(",")[0].strip()
        l2 = o2.split(",")[0].strip()
        off1, off2 = off_of(l1), off_of(l2)
        if off1 is None or off2 is None:
            continue
        r1 = P_B0.match(l1).group(1)
        r2 = P_B0.match(l2).group(1)
        if r1 == r2 and {off1, off2} == {0, 1}:
            byte_hits.append((a1, i, f"{o1}  ;;  {o2}"))
    print(f"# 相邻 `mov byte [r+0],1` / `mov byte [r+1],1` 命中 = {len(byte_hits)}")

    out = []
    for label, hits in (("WORD 0x0101", word_hits), ("BYTE pair", byte_hits)):
        out.append(f"\n########## {label}: {len(hits)} ##########")
        for a, i, o in hits:
            out.append(f"\n=== {a:#010x}  {o}")
            for k in range(max(0, i - args.context), min(len(ins), i + args.context + 2)):
                aa, mm, oo = ins[k]
                mark = "   <<<" if k in (i, i + 1) else ""
                out.append(f"  {aa:#010x}  {mm:<8} {oo}{mark}")
    text = "\n".join(out)
    print(text[:26000])
    if args.out:
        Path(args.out).write_text(text, encoding="utf-8")
        print(f"\n# 明细 -> {args.out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
