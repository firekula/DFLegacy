#!/usr/bin/env python3
r"""w1-probe-layout.py —— 在区域转储上**按地址打印原始字**，并把"谁指向谁"列出来。

为什么需要：连续三轮我都按"猜的 `_Rb_tree` 布局"去套，结果每一轮都被上一轮的错误假设带偏。
本文件不做任何布局假设 —— 只回答两个事实问题：
  ① `0x094045d4`（`Singleton<ServiceRestrictManager>::Get()` 的返回地址）附近到底长什么样；
  ② 现场那几个可疑对象（`0x1187e9f0`、`0x118a57a8`、`0x11830038`…）各自指向谁、谁指向它们。
"""
from __future__ import annotations

import argparse
import re
from pathlib import Path

LINE = re.compile(r"^([0-9a-f]{8}) ([0-9a-f]{8})$")


def load(path: Path):
    mem: dict[int, int] = {}
    for raw in path.read_text(encoding="utf-8", errors="replace").splitlines():
        m = LINE.match(raw.strip())
        if m:
            mem[int(m.group(1), 16)] = int(m.group(2), 16)
    return mem


def show(mem, base, n, label):
    print(f"\n## {label} @ {base:#010x}")
    for i in range(n):
        va = base + i * 4
        v = mem.get(va)
        extra = ""
        if v is not None and 0x00100000 <= v <= 0x7FFFFFFF:
            target = mem.get(v)
            extra = (f"   -> [{v:#010x}] = " +
                     ("<not in dump>" if target is None else f"{target:#010x}"))
        print(f"  {va:#010x} +{i*4:#04x} = " +
              ("<not in dump>" if v is None else f"{v:#010x}") + extra)


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("dump")
    args = ap.parse_args()
    mem = load(Path(args.dump))
    print(f"# words={len(mem)}")

    show(mem, 0x094045c0, 20, "manager 片区（Get() 返回 0x094045d4）")
    show(mem, 0x1187e9f0, 24, "可疑对象 A")
    show(mem, 0x118a57a8, 16, "可疑对象 B")
    show(mem, 0x11830038, 16, "可疑节点 C")

    # 谁指向 A / B / C
    for tgt in (0x1187e9f0, 0x118a57a8, 0x11830038, 0x094045d4):
        print(f"\n## 谁的值等于 {tgt:#010x}")
        hit = 0
        for va, v in sorted(mem.items()):
            if v == tgt:
                print(f"  {va:#010x} = {tgt:#010x}")
                hit += 1
        if not hit:
            print("  （本次转储内无人指向它）")

    # 找"看起来像 _Rb_tree 头"的地方：连续三字都指向同一地址，或 self-loop
    print("\n## 自指/三连自指的位置（可能是 header 或空树哨兵）")
    for va, v in sorted(mem.items()):
        a, b = mem.get(va + 4), mem.get(va + 8)
        if a is not None and b is not None and a == b and v is not None:
            print(f"  {va:#010x}: [{v:#010x}, {a:#010x}, {b:#010x}]")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
