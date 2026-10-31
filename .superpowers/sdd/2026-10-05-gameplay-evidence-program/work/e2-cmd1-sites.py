#!/usr/bin/env python3
r"""e2-cmd1-sites.py —— 从项目既有的客户端全量反汇编里，找出 `BeginCommand`（`0x8B2650`）
**全部发射点及其命令号**，并单独列出 `cmd == 1`（= LOGIN）的上下文。

为什么用 `insns.txt` 而不是自己解析 PE：客户端是 **VMProtect 加密**的（section 名乱码、有 `.vmp0`），
我自己那份手写 PE 解析读出的 `image_base` 是错的（0x598000）⇒ 扫出 0 个发射点。
项目既有 `friends-protocol-recon/work/insns.txt`（54 MB，2026-09-27，`[静态]` 级既有产物）
就是**权威反汇编**，且它的地址与项目里其它结论（`0x4FE9B0`、`0x8B2650`…）一致 ⇒ 直接用它。

判据与限制：
  * "最近的 `push imm`" = **候选**命令号；`b-senders.py` 用的是同一条规则（本工程既有方法）。
  * 因此本文件给的是**候选集**；要定案"哪一处是 LOGIN 的出站构造"必须读它后面写载荷的代码。
"""
from __future__ import annotations

import argparse
import re
from collections import defaultdict
from pathlib import Path

BEGIN_CMD = 0x8B2650
NUM = re.compile(r"^(0x[0-9a-f]+|-?\d+)$", re.I)


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
    ap.add_argument("--out", default=None)
    ap.add_argument("--window", type=int, default=6)
    ap.add_argument("--context", type=int, default=14)
    args = ap.parse_args()

    ins = load(Path(args.insns))
    print(f"# instructions={len(ins)}  range={ins[0][0]:#x}..{ins[-1][0]:#x}")

    sites = []
    for i, (a, m, o) in enumerate(ins):
        if m != "call" or o != hex(BEGIN_CMD):
            continue
        cmd = None
        for j in range(i - 1, max(0, i - args.window - 1), -1):
            a2, m2, o2 = ins[j]
            if m2 == "push" and NUM.match(o2):
                cmd = int(o2, 0)
                break
            if m2 in ("mov", "call"):
                break
        sites.append((a, cmd, i))

    print(f"# BeginCommand call sites = {len(sites)}")
    hist = defaultdict(int)
    for _, c, _ in sites:
        hist[c] += 1
    print("# 命令号直方图：")
    for c, n in sorted(hist.items(), key=lambda kv: (-kv[1], -1 if kv[0] is None else kv[0])):
        print(f"#   cmd={c}: {n}")

    lines = []
    for a, c, i in sites:
        if c is not None and c != 1:
            continue
        tag = "cmd=1（立即数）" if c == 1 else "cmd=None（命令号不是立即数，可能是变量）"
        lines.append(f"\n=== BeginCommand @ {a:#010x}  [{tag}] ===")
        for k in range(max(0, i - args.context), min(len(ins), i + 6)):
            aa, mm, oo = ins[k]
            mark = "   <<< call BeginCommand" if k == i else ""
            lines.append(f"  {aa:#010x}  {mm:<8} {oo}{mark}")
    text = "\n".join(lines)
    n_imm1 = sum(1 for _, c, _ in sites if c == 1)
    n_none = sum(1 for _, c, _ in sites if c is None)
    print(f"\n# cmd=1（立即数）= {n_imm1} ; cmd=None = {n_none} ; 合计列出 {n_imm1 + n_none}")
    print(text[:40000])
    if args.out:
        Path(args.out).write_text(text, encoding="utf-8")
        print(f"\n# 明细写入 {args.out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
