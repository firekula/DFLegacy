#!/usr/bin/env python3
r"""vm-peek5-parse.py —— 解析 VM 只读区域转储，重建 `ServiceRestrictManager` 的策略表。

输入：`peek5-ptrace` 产出的文本（`<va> <hex dword>` 每行一条，区间由 `REGION` 分隔）。

做法（**不做任何猜测式遍历**）：
  1. 把转储拼成 `va -> dword` 字典；
  2. 在**转储到的字节范围**内找 `_Rb_tree` 的 `_M_header` 自指结构：
     `hdr+4 == hdr+8 == hdr+0xc == hdr.parent` 且 `hdr.parent.parent == hdr`
     （libstdc++ 空树/根节点的 header 满足 `_M_parent` 指向 root，root 的 parent 指回 header）；
  3. 从 root 起用**迭代上限 + 访问集合**走完全树（defensive，绝不无限循环）；
  4. 对每个节点的 value（策略对象指针）读 `getCode()`(+8) 与 `vector<short>`(+0xc/+0x10)，
     打印 `vec[33]`（LOGIN 的判据）。
"""
from __future__ import annotations

import argparse
import re
from pathlib import Path

HEX = re.compile(r"^([0-9a-f]{8}) ([0-9a-f]{8})$")


def load(path: Path):
    mem: dict[int, int] = {}
    regions = []
    cur = None
    for line in path.read_text(encoding="utf-8", errors="replace").splitlines():
        if line.startswith("REGION "):
            _, va, ln = line.split()
            cur = (int(va, 16), int(ln))
            regions.append(cur)
            continue
        if line.startswith("REGION_END") or line.startswith("UNREADABLE_AT") or line.startswith("GIVE_UP"):
            continue
        m = HEX.match(line.strip())
        if m:
            mem[int(m.group(1), 16)] = int(m.group(2), 16)
    return mem, regions


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("dump")
    ap.add_argument("--code", type=int, default=33, help="要查的 restrict_code（LOGIN 用 0x21=33）")
    args = ap.parse_args()

    mem, regions = load(Path(args.dump))
    print(f"# regions={len(regions)} words={len(mem)}")
    for va, ln in regions:
        got = sum(1 for a in mem if va <= a < va + ln)
        print(f"#   region {va:#010x}+{ln} words_present={got}")

    def w(va):
        return mem.get(va & 0xFFFFFFFF)

    def words(va, n):
        return [w(va + 4 * i) for i in range(n)]

    # --- 1) 找 _Rb_tree header 自指结构 ---
    print("\n## 候选 _Rb_tree header（自指 + parent.parent 回指）")
    headers = []
    for va in sorted(mem):
        p, l, r = w(va + 4), w(va + 8), w(va + 0xc)
        if p is None or l is None or r is None:
            continue
        if p == va and l == va and r == va:
            headers.append(va)
        elif p is not None and p != va and l == va and r == va and w(p + 4) == va:
            headers.append(va)
    for h in headers:
        print(f"  header={h:#010x} parent(root)={w(h+4):#010x} left={w(h+8):#010x} "
              f"right={w(h+0xc):#010x} colour={w(h+0x10)}")

    # --- 2) 找 manager 对象：以 header 为 +? 成员的对象 ---
    print("\n## manager 对象（含 header 指针的 dword）")
    mgrs = []
    for va in sorted(mem):
        for off in (0, 4, 8, 0xc, 0x10, 0x14, 0x18, 0x1c, 0x20):
            t = w(va + off)
            if t in headers:
                mgrs.append((va, off))
                print(f"  obj={va:#010x} +{off:#04x} -> header {t:#010x}  "
                      f"obj[+0]={w(va):#010x} obj[+4]={w(va+4):#010x} obj[+8]={w(va+8):#010x} "
                      f"obj[+0xc]={w(va+0xc):#010x} obj[+0x10]={w(va+0x10):#010x}")
    # --- 3) 遍历每棵树 ---
    for h in headers:
        root = w(h + 4)
        print(f"\n## 走树 header={h:#010x} root={root if root is None else hex(root)}")
        seen = set()
        stack = [root] if root and root != h else []
        order = []
        while stack:
            cur = stack.pop()
            if cur in seen or cur == h or cur is None or len(seen) > 64:
                continue
            seen.add(cur)
            l, r = w(cur + 4), w(cur + 8)
            order.append(cur)
            if r:
                stack.append(r)
            if l:
                stack.append(l)
        print(f"  nodes={len(order)}")
        rows = []
        for n in order:
            l, r, par, col = w(n + 4), w(n + 8), w(n), w(n + 0xc)
            key, val = w(n + 0x10), w(n + 0x14)
            rows.append((key, n, val, col, l, r, par))
        for key, n, val, col, l, r, par in sorted(rows):
            print(f"  node={n:#010x} key(category)={key} value(policy*)={val if val is None else hex(val)} "
                  f"colour={col} left={l if l is None else hex(l)} right={r if r is None else hex(r)}")
            if val:
                gc = w(val + 8)
                vb, ve = w(val + 0xc), w(val + 0x10)
                print(f"     policy: getCode(+8)={gc} vecBegin={vb if vb is None else hex(vb)} "
                      f"vecEnd={ve if ve is None else hex(ve)}")
                if vb is not None and ve is not None and ve > vb:
                    n_items = (ve - vb) // 2
                    print(f"     vec_count={n_items}")
                    vals = [w(vb + 2 * i) & 0xFFFF if w(vb + 2 * i) is not None else None
                            for i in range(min(n_items, 40))]
                    print(f"     vec={vals}")
                    tgt = w(vb + 2 * args.code)
                    print(f"     vec[{args.code}] = {None if tgt is None else tgt & 0xFFFF}"
                          f"   (None=该地址不在本次转储范围内)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
