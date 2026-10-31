#!/usr/bin/env python3
r"""analyze-live-login.py —— 分析刚抓到的**活账号**成功登录，并与 B0 那份对比。

## 为什么做这个对比

B0 抓的那份（`streams-vm/13681-10013.bin`）属于**另一套库**的账号（uid=2），
在这台 VM 上重放会因为"账号不存在"而失败。本次抓到的是**活账号**（firekula /
UID 18000008）的完整登录。两份对比能把"哪些字段是账号相关的"直接看出来。

用法: analyze-live-login.py <live-c2s> <live-s2c> [<b0-c2s> <b0-s2c>]
"""
from __future__ import annotations

import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE / "driver"))

import dnfdrive                        # noqa: E402
from dnfdrive import GAME              # noqa: E402


def describe(frames, limit=12):
    out = []
    for f in frames[:limit]:
        kind = "CMD" if f.cat == 1 else ("NOTI" if f.cat == 0 else f"?{f.cat:02x}")
        out.append(f"{kind}{f.fid}({f.size})")
    return " ".join(out) + (" …" if len(frames) > limit else "")


def load(p: Path):
    blob = p.read_bytes()
    frames, used = dnfdrive.parse_frames(blob, GAME)
    return blob, frames, used


def show(tag: str, p: Path):
    blob, frames, used = load(p)
    print(f"{tag}: {p.name}")
    print(f"  {len(blob)} B, {len(frames)} 帧, 恰好铺满={used == len(blob)}"
          f"{'' if used == len(blob) else f'  **残 {len(blob) - used} B**'}")
    print(f"  签名: {describe(frames)}")
    return blob, frames


def main() -> int:
    live_c = Path(sys.argv[1])
    live_s = Path(sys.argv[2])

    print("=" * 78)
    print("本次抓到的活账号登录")
    print("=" * 78)
    lc, lcf = show("C→S", live_c)
    print()
    ls, lsf = show("S→C", live_s)

    if len(sys.argv) > 4:
        b0_c = Path(sys.argv[3])
        b0_s = Path(sys.argv[4])
        print()
        print("=" * 78)
        print("B0 那份（另一套库的账号）")
        print("=" * 78)
        bc, bcf = show("C→S", b0_c)
        print()
        bs, bsf = show("S→C", b0_s)

        print()
        print("=" * 78)
        print("对比")
        print("=" * 78)
        print(f"  C→S 帧数: 本次 {len(lcf)}  B0 {len(bcf)}")
        print(f"  S→C 帧数: 本次 {len(lsf)}  B0 {len(bsf)}")
        n = min(len(lcf), len(bcf))
        diffs = [(i, lcf[i], bcf[i]) for i in range(n) if lcf[i].size != bcf[i].size]
        print(f"  前 {n} 帧里长度不同的: {len(diffs)} 处")
        for i, a, b in diffs[:8]:
            print(f"    [{i}] 本次 cat={a.cat} id={a.fid} size={a.size}   "
                  f"B0 cat={b.cat} id={b.fid} size={b.size}")

        # 首帧逐字节对比
        print()
        print("  首帧头 10 字节:")
        print(f"    本次: {lcf[0].raw[:GAME.hdr].hex(' ')}")
        print(f"    B0  : {bcf[0].raw[:GAME.hdr].hex(' ')}")
        pa, pb = lcf[0].raw[GAME.hdr:], bcf[0].raw[GAME.hdr:]
        nd = sum(1 for x, y in zip(pa, pb) if x != y)
        print(f"  首帧载荷: 本次 {len(pa)} B / B0 {len(pb)} B；不同字节 {nd}")
        print(f"    相同前缀长度: "
              f"{next((i for i in range(min(len(pa), len(pb))) if pa[i] != pb[i]), min(len(pa), len(pb)))}")

    print()
    print("=" * 78)
    print("本次 C→S 首帧（活账号样本）")
    print("=" * 78)
    print(f"  头: {lcf[0].raw[:GAME.hdr].hex(' ')}")
    print(f"  载荷前 64: {lcf[0].raw[GAME.hdr:GAME.hdr + 64].hex(' ')}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
