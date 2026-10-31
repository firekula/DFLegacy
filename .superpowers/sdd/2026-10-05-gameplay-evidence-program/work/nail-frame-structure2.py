#!/usr/bin/env python3
r"""nail-frame-structure2.py —— 继续钉 `[7..9]`：那 3 字节到底怎么算出来的。

## 已知（nail-frame-structure.py 的实测）

* S→C：`hdr[7] == payload[1]` **180/180 成立**（两条独立抓包各 90 帧）。
* C→S：同样关系 **0/42**（完全不成）—— 有方向性。

⇒ 所以 `[7..9]` 不是"对整载荷算的校验和"。本脚本继续问四件事：

  1. **逐字节位置的可打印率/熵**：S→C 载荷是"前几字节明文 + 其余密文"，还是整体随机?
     （这决定 `payload[0..1]` 是不是一个明文小头）
  2. `hdr[8]` / `hdr[9]` 与谁相关：拿它们去和 payload 的每个位置、以及 hdr 内部做比对。
  3. `hdr[7]` 与 `payload[0]` 的关系：是否满足 `hdr[7] = f(payload[0])`（异或/加减常数）。
  4. **帧长是否影响**：`hdr[8..9]` 会不会是帧长或序号的函数。

判定方式全部是"计数 + 反例"，不给"看起来像"的结论。

用法: nail-frame-structure2.py -- <stream.bin...>
"""
from __future__ import annotations

import sys
from collections import Counter
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE / "driver"))

import dnfdrive                        # noqa: E402
from dnfdrive import GAME              # noqa: E402


def pos_stats(frames, npos=16):
    """每个载荷位置上的：可打印数、取值种类数。"""
    ent = [set() for _ in range(npos)]
    pr = [0] * npos
    n = 0
    for f in frames:
        p = f.raw[GAME.hdr:]
        if len(p) < npos:
            continue
        n += 1
        for i in range(npos):
            ent[i].add(p[i])
            if 32 <= p[i] < 127:
                pr[i] += 1
    return n, ent, pr


def main() -> int:
    argv = [a for a in sys.argv[1:] if a != "--"]
    streams = [Path(a) for a in argv]

    allf = {}
    for sp in streams:
        frames, _ = dnfdrive.parse_frames(sp.read_bytes(), GAME)
        allf[sp.name] = frames

    print("=" * 78)
    print("1. 载荷逐字节位置统计（S→C 与 C→S 分开看）")
    print("=" * 78)
    for name, frames in allf.items():
        direction = "S→C" if name.startswith("10013-") else "C→S"
        n, ent, pr = pos_stats(frames)
        print(f"\n  [{direction}] {name}: {n} 帧")
        print("    位置 :  0    1    2    3    4    5    6    7    8    9   10   11   12   13   14   15")
        print("    种类 : " + " ".join(f"{len(ent[i]):>4}" for i in range(16)))
        print("    可打印:" + " ".join(f"{pr[i]:>4}" for i in range(16)))
        # 前几个位置若取值种类远少于帧数 => 低熵（明文/常量）；接近帧数 => 高熵
        print(f"    （帧数 {n}：取值种类≈帧数 = 高熵；远小于 = 低熵/常量/枚举）")

    print()
    print("=" * 78)
    print("2. hdr[8] / hdr[9] 与谁相关（候选关系计数）")
    print("=" * 78)
    checks = [
        ("hdr[8] == hdr[7]", lambda h, p: h[1] == h[0]),
        ("hdr[9] == hdr[7]", lambda h, p: h[2] == h[0]),
        ("hdr[8] == hdr[9]", lambda h, p: h[1] == h[2]),
        ("hdr[8] == payload[2]", lambda h, p: len(p) > 2 and h[1] == p[2]),
        ("hdr[9] == payload[3]", lambda h, p: len(p) > 3 and h[2] == p[3]),
        ("hdr[7]^payload[0] 恒定", None),      # 特殊处理
        ("hdr[7]-payload[0] 恒定", None),
        ("hdr[7]+payload[0] 恒定", None),
    ]
    for name, frames in allf.items():
        direction = "S→C" if name.startswith("10013-") else "C→S"
        cnt = Counter()
        xor_vals, sub_vals, add_vals = Counter(), Counter(), Counter()
        tot = 0
        for f in frames:
            h = f.raw[7:10]
            p = f.raw[GAME.hdr:]
            if len(p) < 4:
                continue
            tot += 1
            for label, fn in checks:
                if fn and fn(h, p):
                    cnt[label] += 1
            xor_vals[h[0] ^ p[0]] += 1
            sub_vals[(h[0] - p[0]) & 0xFF] += 1
            add_vals[(h[0] + p[0]) & 0xFF] += 1
        print(f"\n  [{direction}] {name}: {tot} 帧")
        for label, _ in checks:
            if _ is not None:
                print(f"     {label:<26} {cnt[label]:>4}/{tot}")
        for label, c in (("hdr[7]^payload[0] 取值数", xor_vals),
                         ("hdr[7]-payload[0] 取值数", sub_vals),
                         ("hdr[7]+payload[0] 取值数", add_vals)):
            top = c.most_common(1)[0]
            print(f"     {label:<26} {len(c)} 种；最多 {top[0]:#04x} 出现 {top[1]}/{tot}")

    print()
    print("=" * 78)
    print("3. hdr[8..9] 与帧长/序号的关系")
    print("=" * 78)
    for name, frames in allf.items():
        direction = "S→C" if name.startswith("10013-") else "C→S"
        print(f"\n  [{direction}] {name}")
        eq8 = sum(1 for i, f in enumerate(frames)
                  if (f.size & 0xFF) == f.raw[8])
        eq9 = sum(1 for i, f in enumerate(frames)
                  if ((f.size >> 8) & 0xFF) == f.raw[9])
        idx8 = sum(1 for i, f in enumerate(frames) if (i & 0xFF) == f.raw[8])
        print(f"     hdr[8] == size&0xFF      {eq8}/{len(frames)}")
        print(f"     hdr[9] == (size>>8)&0xFF {eq9}/{len(frames)}")
        print(f"     hdr[8] == 帧序号&0xFF     {idx8}/{len(frames)}")
        print("     前 6 帧 hdr[7:10] + size: " + "  ".join(
            f"{f.raw[7:10].hex()}/{f.size}" for f in frames[:6]))

    return 0


if __name__ == "__main__":
    sys.exit(main())
