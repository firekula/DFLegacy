#!/usr/bin/env python3
r"""nail-determinism.py —— 钉"同一帧在不同会话里是否确定性"。

## 为什么这是关键

我手里有**三份同名帧** `NOTI1(450)`（同 opcode、同长度）：
  1. 所有者本人登录时抓到的（`streams-live/10013-2343.bin` 第 1 帧）
  2. 我**成功**重放时的回复（`live-replay-alone.bin` 第 1 帧）
  3. 我**被踢**那次重放的回复（`live-replay-first-frame.bin` 第 1 帧）

它们来自三个**不同会话**（不同 socket、不同时刻）。所以：

* 若三份载荷**相同或高度相似** ⇒ 载荷是**确定性**的（不依赖会话密钥）⇒ 可离线构造。
* 若三份**完全不同** ⇒ 载荷按会话随机化（加密）⇒ 必须拿到会话密钥才能构造。

这条判据直接决定 M1 单元②的难度，而且**不需要再打靶**（数据已在盘上）。

顺带把两个方向的所有同 (cat,id,size) 帧做交叉对比，看有没有"哪些字段是会话相关的"。

用法: nail-determinism.py -- <stream.bin...>
"""
from __future__ import annotations

import sys
from collections import defaultdict
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE / "driver"))

import dnfdrive                        # noqa: E402
from dnfdrive import GAME              # noqa: E402


def common_prefix(a: bytes, b: bytes) -> int:
    n = min(len(a), len(b))
    for i in range(n):
        if a[i] != b[i]:
            return i
    return n


def main() -> int:
    argv = [a for a in sys.argv[1:] if a != "--"]
    paths = [Path(a) for a in argv]

    print("=" * 78)
    print("按 (cat,id,size) 归类，比较不同会话里的同名帧")
    print("=" * 78)

    groups = defaultdict(list)          # (cat,id,size) -> [(来源, 载荷)]
    for p in paths:
        frames, _ = dnfdrive.parse_frames(p.read_bytes(), GAME)
        for i, f in enumerate(frames):
            groups[(f.cat, f.fid, f.size)].append((f"{p.name}#{i}", f.raw[GAME.hdr:]))

    for key in sorted(groups, key=lambda k: (-len(groups[k]), k)):
        items = groups[key]
        if len(items) < 2:
            continue
        cat, fid, size = key
        kind = "CMD" if cat == 1 else ("NOTI" if cat == 0 else f"?{cat:02x}")
        print(f"\n[{kind} id={fid} size={size}]  出现 {len(items)} 次")
        for tag, pay in items:
            print(f"    {tag:<34} {len(pay):>5} B  {pay[:20].hex(' ')}")
        # 两两对比
        base_tag, base = items[0]
        for tag, pay in items[1:]:
            cp = common_prefix(base, pay)
            same = sum(1 for x, y in zip(base, pay) if x == y)
            print(f"    {base_tag} vs {tag}: 公共前缀 {cp} B；"
                  f"相同字节 {same}/{min(len(base), len(pay))}"
                  f"{'  <<< 完全相同' if cp == len(base) == len(pay) else ''}")

    print()
    print("=" * 78)
    print("结论")
    print("=" * 78)
    for key, items in groups.items():
        if len(items) >= 2:
            base = items[0][1]
            sims = [common_prefix(base, p) for _, p in items[1:]]
            if all(s == 0 for s in sims):
                print(f"  {key}: 所有副本**第 1 字节就不同** ⇒ 载荷按会话随机化")
            elif all(s >= len(base) for s in sims):
                print(f"  {key}: 所有副本**逐字节相同** ⇒ 确定性载荷")
            else:
                print(f"  {key}: 部分相同（公共前缀 {sims}）⇒ 部分确定性")
    return 0


if __name__ == "__main__":
    sys.exit(main())
