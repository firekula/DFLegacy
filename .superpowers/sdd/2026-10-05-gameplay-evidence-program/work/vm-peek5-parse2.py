#!/usr/bin/env python3
"""vm-peek5-parse2.py —— 在只读区域转储里找 `std::map`（_Rb_tree）对象并走树。

为什么不用"猜 header"那版：libstdc++ 的 `_Rb_tree` 布局里 `_M_node_count` 与
`_M_header` 的相对次序就是 `node_count` 在**前**、header 在后（见 `_Rb_tree_impl` 定义），
而 header 里 `_M_parent`(root) 的 parent 指回 header。
本文件的做法是**从 node_count 反向定位**：找值为 1..16 的 dword，检查其后 12 字节是否构成
`_M_header`（parent/left/right），且 `parent.parent == header`；再走这棵树。
"""
from __future__ import annotations

import argparse
import re
from pathlib import Path

LINE = re.compile(r"^([0-9a-f]{8}) ([0-9a-f]{8})$")


def load(path: Path):
    mem: dict[int, int] = {}
    regions = []
    for raw in path.read_text(encoding="utf-8", errors="replace").splitlines():
        s = raw.strip()
        if s.startswith("REGION "):
            _, va, ln = s.split()
            regions.append((int(va, 16), int(ln)))
            continue
        m = LINE.match(s)
        if m:
            mem[int(m.group(1), 16)] = int(m.group(2), 16)
    return mem, regions


def walk(mem, header, limit=64):
    """从 header 走整棵树（用访问集合防环；不做"聪明"的后继推导）。"""
    root = mem.get(header + 4)
    if root is None or root == header:
        return []
    stack, seen, out = [root], set(), []
    while stack and len(seen) < limit:
        cur = stack.pop()
        if cur in seen or cur == header or cur not in mem:
            continue
        seen.add(cur)
        out.append(cur)
        for off in (8, 4):                      # right, left
            nb = mem.get(cur + off)
            if nb and nb != header and nb not in seen:
                stack.append(nb)
    return out


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("dump")
    ap.add_argument("--code", type=int, default=33)
    args = ap.parse_args()

    mem, regions = load(Path(args.dump))
    print(f"# regions={len(regions)} words={len(mem)}")

    cands = []
    for va, w in mem.items():
        if not (1 <= w <= 64):
            continue
        h = va + 4
        p = mem.get(h)
        if not p or p == h:
            continue
        if mem.get(p) != h:                     # parent.parent 必须回指 header
            continue
        cands.append((va, w, h, p))

    print(f"## map 候选（node_count 前后构成 _Rb_tree_impl）：{len(cands)}")
    for ncount_va, n, h, root in cands:
        nodes = walk(mem, h)
        if n != len(nodes) and len(nodes) == 0:
            continue
        print(f"\n=== map @{ncount_va:#010x} node_count={n} header={h:#010x} root={root:#010x} 走到 {len(nodes)} 节点 ===")
        rows = []
        for nd in nodes:
            par, l, r, col = (mem.get(nd + o) for o in (0, 4, 8, 0xc))
            key, val = mem.get(nd + 0x10), mem.get(nd + 0x14)
            rows.append((key, nd, val, col, l, r, par))
        for key, nd, val, col, l, r, par in sorted(rows, key=lambda t: (t[0] is None, t[0])):
            print(f"  node={nd:#010x} key(category)={key} value={val if val is None else hex(val)} colour={col}")
            if val:
                gc = mem.get(val + 8)
                vb, ve = mem.get(val + 0xc), mem.get(val + 0x10)
                print(f"      policy getCode(+8)={gc} vecBegin={None if vb is None else hex(vb)} "
                      f"vecEnd={None if ve is None else hex(ve)}")
                if vb is not None and ve is not None and ve > vb:
                    cnt = (ve - vb) // 2
                    print(f"      vec_count={cnt}")
                    vals = []
                    for i in range(min(cnt, 40)):
                        wv = mem.get(vb + 2 * i)
                        vals.append(None if wv is None else wv & 0xFFFF)
                    print(f"      vec={vals}")
                    tv = mem.get(vb + 2 * args.code)
                    print(f"      >>> vec[{args.code}] = " +
                          ("（不在本次转储范围内）" if tv is None else str(tv & 0xFFFF)))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
