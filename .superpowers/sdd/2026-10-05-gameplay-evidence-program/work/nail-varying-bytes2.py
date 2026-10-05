#!/usr/bin/env python3
r"""nail-varying-bytes2.py —— 修正版：按"帧类型 + 该类型在流内的第 N 次出现"跨会话配对。

## 上一版的 bug（留痕，别重犯）

`nail-varying-bytes.py` 按 `(cat,id,size)` 分组，但**同一类型在一条流里可能出现多次
且内容不同**（`NOTI183(31)` 在 S 流里出现两次，两条内容不一样）。把两次混进一组后，
"位置 0"就出现了两个取值 ⇒ 该位置被判为"变化"，于是**所有帧都被误判成随机**。

实测证据：`NOTI183` 的第 1 次出现跨会话是 **18/21 字节相同**，但按上次的分组法
统计出 `0/21 恒定`。两个数不可能同时对 —— 错的是分组。

正确做法：用 **出现序号** 配对。sessionA 的第 1 个 `NOTI183` ↔ sessionB 的第 1 个，
以此类推。这样比的是"逻辑上同一帧"。

用法: nail-varying-bytes2.py -- <stream.bin...>   （第 1 个文件作为基准会话）
"""
from __future__ import annotations

import sys
from collections import defaultdict
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE / "driver"))

import dnfdrive                        # noqa: E402
from dnfdrive import GAME              # noqa: E402


def index_stream(path: Path):
    """返回 {(cat,id,size,occurrence): (payload, hdr7_10)} 与出现次数统计。"""
    frames, _ = dnfdrive.parse_frames(path.read_bytes(), GAME)
    seen = defaultdict(int)
    out = {}
    for f in frames:
        k = (f.cat, f.fid, f.size)
        n = seen[k]
        seen[k] += 1
        out[(f.cat, f.fid, f.size, n)] = (f.raw[GAME.hdr:], f.raw[7:10])
    return out


def main() -> int:
    argv = [a for a in sys.argv[1:] if a != "--"]
    paths = [Path(a) for a in argv]
    streams = {p.name: index_stream(p) for p in paths}

    print("=" * 78)
    print("按（帧类型 + 出现序号）跨会话配对，看哪些字节恒定")
    print("=" * 78)

    # 找出所有会话共有的键
    common = set.intersection(*(set(v) for v in streams.values()))
    rows = []
    for k in sorted(common, key=lambda x: (x[0], x[1], x[2], x[3])):
        cat, fid, size, occ = k
        samples = [streams[n][k] for n in sorted(streams)]
        minlen = min(len(p) for p, _ in samples)
        if minlen == 0:
            continue
        varying = [i for i in range(minlen) if len({p[i] for p, _ in samples}) > 1]
        const = minlen - len(varying)
        kind = "CMD" if cat == 1 else ("NOTI" if cat == 0 else f"?{cat:02x}")
        hdrs = [h.hex() for _, h in samples]
        rows.append((kind, fid, size, occ, len(samples), const, minlen, varying, hdrs))

    print(f"\n共有 {len(rows)} 个'逻辑同帧'；逐条：\n")
    for kind, fid, size, occ, n, const, tot, varying, hdrs in rows:
        pct = const / tot * 100 if tot else 0
        tag = "确定性" if pct == 100 else ("**全变**" if const == 0 else "部分变")
        print(f"  {kind}{fid}({size}) 第{occ}次  样本{n}  载荷{tot} B  "
              f"恒定 {const}/{tot} ({pct:5.1f}%)  [{tag}]")
        if varying:
            print(f"      变化偏移: {varying[:28]}{' …' if len(varying) > 28 else ''}")
        print(f"      hdr[7:10]: {hdrs}")

    print()
    print("=" * 78)
    print("汇总")
    print("=" * 78)
    det = [r for r in rows if r[5] == r[6]]
    part = [r for r in rows if 0 < r[5] < r[6]]
    allv = [r for r in rows if r[5] == 0]
    print(f"  完全确定性（载荷逐字节恒定）: {len(det)} 个")
    for kind, fid, size, occ, n, c, t, v, h in det:
        print(f"      {kind}{fid}({size}) 第{occ}次   {t} B")
    print(f"\n  部分变化: {len(part)} 个")
    for kind, fid, size, occ, n, c, t, v, h in part:
        print(f"      {kind}{fid}({size}) 第{occ}次   恒定 {c}/{t}；变化偏移 {v[:16]}")
    print(f"\n  完全变化（0 恒定）: {len(allv)} 个")
    for kind, fid, size, occ, n, c, t, v, h in allv:
        print(f"      {kind}{fid}({size}) 第{occ}次   {t} B   hdr[7:10]={h}")

    # hdr[7:10] 的恒定性单独统计
    print()
    hcons = sum(1 for r in rows if len(set(r[8])) == 1)
    print(f"  hdr[7:10] 跨会话恒定的'逻辑同帧': {hcons}/{len(rows)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
