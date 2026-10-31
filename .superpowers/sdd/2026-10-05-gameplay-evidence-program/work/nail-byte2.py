#!/usr/bin/env python3
r"""nail-byte2.py —— 钉头里 `[2]` 那个字节到底是什么（旧结论说"恒 0"，实测是错的）。

## 旧结论为什么错

`dnfdrive.py` 与 `B0-batch-report.md` 都写 `[2] 恒 0`（语义 [未验证]）。
那是在 B0 的**少数几条流**上看到的。本会话把全部抓包（live/vm/docker）一起统计，
取值集合是 `{0, 1, 2}` ⇒ "恒 0" 是**样本不足导致的错误推广**（正是证据纪律里
"把一个观察推广成一条规则"的反面教材）。

## 本脚本查什么

`[2]` 与 `cat`/`id`/`size`/方向 的相关性：
* 按方向统计取值分布；
* 看它是否等于 `size >> N` 的某个字节（曾猜"size 的高字节被挪到这里"）；
* 看它是否与 `path`（流）相关（同一流内恒定 = 会话/连接级字段）。

用法: nail-byte2.py -- <stream.bin...>
"""
from __future__ import annotations

import sys
from collections import Counter
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE / "driver"))

import dnfdrive                        # noqa: E402
from dnfdrive import GAME              # noqa: E402


def main() -> int:
    argv = [a for a in sys.argv[1:] if a != "--"]
    paths = [Path(a) for a in argv]

    grand = Counter()
    print("=" * 78)
    print("头[2] 的取值：按流")
    print("=" * 78)
    for p in paths:
        blob = p.read_bytes()
        frames, used = dnfdrive.parse_frames(blob, GAME)
        if used != len(blob):
            print(f"  {p.name}: 未铺满，跳过")
            continue
        vals = Counter(f.raw[2] for f in frames)
        direction = "S→C" if "-10013" in p.name and not p.name.startswith("10013") else \
                    ("S→C" if p.name.startswith("10013") else "C→S")
        print(f"  [{direction}] {p.name:<24} {len(frames):>3} 帧  "
              f"取值 {dict(sorted(vals.items()))}")
        grand.update(vals)

    print()
    print(f"  合计取值分布: {dict(sorted(grand.items()))}")
    print()

    print("=" * 78)
    print("头[2] 是否与 size 相关（试 size 的各字节 / 位移）")
    print("=" * 78)
    cands = {
        "[2] == size&0xff": lambda f: f.raw[2] == (f.size & 0xFF),
        "[2] == (size>>8)&0xff": lambda f: f.raw[2] == ((f.size >> 8) & 0xFF),
        "[2] == (size>>16)&0xff": lambda f: f.raw[2] == ((f.size >> 16) & 0xFF),
        "[2] == (size>>24)&0xff": lambda f: f.raw[2] == ((f.size >> 24) & 0xFF),
        "[2] == cat": lambda f: f.raw[2] == f.cat,
    }
    for p in paths:
        blob = p.read_bytes()
        frames, used = dnfdrive.parse_frames(blob, GAME)
        if used != len(blob) or not frames:
            continue
        print(f"\n  {p.name} ({len(frames)} 帧)")
        for name, fn in cands.items():
            n = sum(1 for f in frames if fn(f))
            if n:
                print(f"     {name:<26} {n:>4}/{len(frames)}")
        # 与 (cat,id) 组合的分布
        combo = Counter((f.cat, f.fid, f.raw[2]) for f in frames)
        multi = {k: v for k, v in combo.items() if len([1 for c in combo if c[:2] == k[:2]]) > 1}
        if multi:
            print(f"     同一 (cat,id) 下 [2] 有多个取值 —— 前 5 例:")
            seen = set()
            for (c, i, b), v in sorted(combo.items()):
                if (c, i) in seen:
                    continue
                bs = sorted(bb for cc, ii, bb in combo if (cc, ii) == (c, i))
                if len(bs) > 1:
                    seen.add((c, i))
                    kind = "CMD" if c == 1 else "NOTI"
                    print(f"        {kind}{i}: [2] ∈ {bs}")
                if len(seen) >= 5:
                    break
        else:
            print("     同一 (cat,id) 下 [2] 恒定 ⇒ 它由帧类型决定，不是每帧随机")

    return 0


if __name__ == "__main__":
    sys.exit(main())
