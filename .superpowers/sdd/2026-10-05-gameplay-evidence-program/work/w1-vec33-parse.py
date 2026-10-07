#!/usr/bin/env python3
r"""w1-vec33-parse.py —— 离线解出 W1 要的 `AuthMobileRestrictPolicy::vec[33]`。

输入：`peek5-ptrace` 的区域转储（`<va> <hex>` 行 + `REGION` 分隔）。

**不做递归图遍历**（那正是上一轮把 VM 进程按住的原因）：本文件只做
"在已转储的字里查表 + 有限深度解引用"，每一步都要求目标地址**也在转储范围内**，
否则报 `OUT_OF_DUMP` 而不是继续追。

步骤：
  1. 在转储里找 `std::map` 的 `_Rb_tree` 头：`hdr+4`(=parent/root) 指向一个节点，
     该节点 `+0` 又指回 `hdr`；且 `hdr+8 == hdr+0xc == hdr`（left/right 自指）。
  2. 对每个候选头，找"谁的 map 成员=这个头"（即 `map = X`，header = X+4）。
  3. 从 root 收集节点（顺序、带访问集合、上限 32），读 `key=[node+0x10]`、`val=[node+0x14]`。
  4. 对每个 val（策略对象）读 `getCode=[val+8]`、`vecBegin=[val+0xc]`、`vecEnd=[val+0x10]`，
     打印 `vec[33]` 与整段向量。
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


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("dump")
    ap.add_argument("--code", type=int, default=33)
    args = ap.parse_args()

    mem, regions = load(Path(args.dump))
    print(f"# regions={len(regions)} words={len(mem)}")
    lo = min(a for a, _ in regions)
    hi = max(a + l for a, l in regions)
    print(f"# dump range {lo:#010x}..{hi:#010x}")

    def w(va):
        return mem.get(va & 0xFFFFFFFF)

    # 1) _Rb_tree 头候选
    headers = []
    for va in sorted(mem):
        p, l, r = w(va + 4), w(va + 8), w(va + 0xc)
        if None in (p, l, r):
            continue
        if l == va and r == va and p not in (0, va) and w(p) == va:
            headers.append(va)
    print(f"\n## _Rb_tree header 候选：{len(headers)}")
    for h in headers:
        print(f"  header={h:#010x} parent(root)={w(h+4):#010x} left={w(h+8):#010x} right={w(h+0xc):#010x}")

    # 2) 谁把 header 当 map 成员（map = header-4，node_count 在 map+0）
    print("\n## 含该 header 的对象（obj 使 header == obj+off+4）")
    found_objs = []
    for va in sorted(mem):
        for off in (0, 4, 8, 0xc, 0x10, 0x14, 0x18, 0x1c, 0x20, 0x24):
            t = w(va + off)
            if t in headers:
                found_objs.append((va, off, t))
                print(f"  obj={va:#010x} map_at_offset={off:#04x} header={t:#010x} "
                      f"node_count={w(va+off-4) if off >= 4 else '<map at obj+0，count 在 obj（下面单独看）>'}"
                      f"  | obj[0..3]={[w(va+i*4) for i in range(4)]}")
    if not found_objs:
        print("  （无：说明本次转储没覆盖到 manager 对象）")

    # 3) 每个 root 起走树（有限、带 seen）
    for h in headers:
        root = w(h + 4)
        print(f"\n## 走树 header={h:#010x} root={root:#010x}")
        seen, stack, nodes = set(), [root], []
        while stack and len(nodes) < 32:
            cur = stack.pop()
            if cur in seen or cur == h or cur is None or cur == 0:
                continue
            seen.add(cur)
            l, r, key, val = w(cur + 4), w(cur + 8), w(cur + 0x10), w(cur + 0x14)
            nodes.append((key, cur, val))
            for nb in (r, l):
                if nb and nb != h and nb not in seen:
                    stack.append(nb)
        for key, n, val in sorted(nodes, key=lambda t: (t[0] is None, t[0])):
            print(f"  node={n:#010x} key(category)={key} value(policy*)={val if val is None else hex(val)}")
            if val is None:
                print("     -> policy OUT_OF_DUMP（该地址不在本次转储范围内）")
                continue
            gc, vb, ve = w(val + 8), w(val + 0xc), w(val + 0x10)
            print(f"     policy getCode(+8)={gc} vecBegin={vb if vb is None else hex(vb)} "
                  f"vecEnd={ve if ve is None else hex(ve)}")
            if vb is None or ve is None or ve <= vb:
                continue
            cnt = (ve - vb) // 2
            vals = []
            for i in range(min(cnt, 40)):
                x = w(vb + 2 * i)
                vals.append(None if x is None else x & 0xFFFF)
            print(f"     vec_count={cnt}")
            print(f"     vec={vals}")
            tv = w(vb + 2 * args.code)
            print(f"     >>> vec[{args.code}] = " +
                  ("OUT_OF_DUMP" if tv is None else str(tv & 0xFFFF)) +
                  ("   ⇒ isApply(%d) = %s" % (args.code, "TRUE(受限)" if (tv is not None and (tv & 0xFFFF) == 1) else "false(放行)")))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
