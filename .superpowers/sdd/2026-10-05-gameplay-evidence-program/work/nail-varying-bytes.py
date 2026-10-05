#!/usr/bin/env python3
r"""nail-varying-bytes.py —— 精确钉出"同帧在不同会话里哪些字节位置会变"。

## 为什么这条最重要

`nail-determinism.py` 发现 `NOTI1(450)` 的 440 B 载荷在**三个不同会话**里
**429 字节相同** —— 载荷基本是**确定性**的，不是会话加密的。
这条一旦坐实，M1 单元②（登录/鉴权）的难度就从"要先攻破会话加密"降到
"读懂一个二进制结构"，量级差一个档。

本脚本把"变/不变"精确到**字节位置**，并把这些位置与**帧头 `[7..9]`** 对齐看：
若"变化的字节"正好落在 `[7..9]` 能解释的位置上，就能把头的语义一并定出来。

## 方法

对每个 `(cat,id,size)` 组内**≥2 个样本**：逐位置统计取值集合大小。
* 取值唯一 ⇒ 该位置**恒定**（对该帧类型）
* 取值数 == 样本数 ⇒ 每次会话都不同
并打印"变化的偏移列表"与"每帧的 hdr[7:10]"，看两者是否对齐。

用法: nail-varying-bytes.py -- <stream.bin...>
"""
from __future__ import annotations

import sys
from collections import defaultdict
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE / "driver"))

import dnfdrive                        # noqa: E402
from dnfdrive import GAME              # noqa: E402


def main() -> int:
    argv = [a for a in sys.argv[1:] if a != "--"]
    paths = [Path(a) for a in argv]

    groups = defaultdict(list)
    for p in paths:
        frames, _ = dnfdrive.parse_frames(p.read_bytes(), GAME)
        for i, f in enumerate(frames):
            groups[(f.cat, f.fid, f.size)].append(
                (f"{p.name}#{i}", f.raw[GAME.hdr:], f.raw[7:10]))

    print("=" * 78)
    print("每个帧类型：恒定位置 vs 变化位置（跨会话）")
    print("=" * 78)

    summary = []
    for key in sorted(groups, key=lambda k: (-len(groups[k]), k)):
        items = groups[key]
        if len(items) < 2:
            continue
        cat, fid, size = key
        kind = "CMD" if cat == 1 else ("NOTI" if cat == 0 else f"?{cat:02x}")
        minlen = min(len(p) for _, p, _ in items)
        varying = [i for i in range(minlen) if len({p[i] for _, p, _ in items}) > 1]
        const = minlen - len(varying)
        print(f"\n[{kind} id={fid} size={size}]  {len(items)} 个样本，载荷 ≥{minlen} B")
        print(f"    恒定位置 {const}/{minlen}   变化位置 {len(varying)}/{minlen}")
        print(f"    变化偏移: {varying[:24]}{' …' if len(varying) > 24 else ''}")
        for tag, _p, h in items:
            print(f"      {tag:<34} hdr[7:10]={h.hex(' ')}")
        # 变化位置是否与 hdr[7:10] 的取值有对应
        hdr_vals = [h for _, _, h in items]
        if len(set(hdr_vals)) == len(hdr_vals):
            print(f"    hdr[7:10] 在 {len(items)} 个样本里**全不同** ⇒ 它是随会话变的")
        else:
            print(f"    hdr[7:10] 取值集合: {[h.hex() for h in dict.fromkeys(hdr_vals)]}")
        summary.append((kind, fid, size, len(items), const, minlen, len(varying)))

    print()
    print("=" * 78)
    print("汇总（按确定性排序：恒定比例高的在前）")
    print("=" * 78)
    print(f"  {'帧类型':<18}{'样本':>5}{'恒定/总':>12}{'变化':>7}")
    for kind, fid, size, n, const, tot, nv in sorted(
            summary, key=lambda r: -(r[4] / r[5] if r[5] else 0)):
        print(f"  {kind}{fid}({size})".ljust(18) + f"{n:>5}{const:>8}/{tot:<4}{nv:>7}")

    print()
    print("判读：恒定比例接近 100% ⇒ 该帧是**确定性**的（不依赖会话密钥）;")
    print("      恒定比例很低    ⇒ 该帧按会话随机化（可能加密或含随机种子）。")
    return 0


if __name__ == "__main__":
    sys.exit(main())
