#!/usr/bin/env python3
r"""nail-loginframe.py —— 决定性实验：同一账号两次登录，C→S 登录帧是否确定性？

## 为什么这是最后一条未控住的变量

`work/out/frame-structure-nailed.md` §4 证明：**S→C 载荷基本确定性**（跨三会话 429/440、
18/21 字节恒定）。但那是**服务端发的**帧。**客户端发的登录帧**只有一份样本，
所以"能不能离线构造登录帧"一直悬着 —— 而这恰好决定 M1 单元②的难度。

现在有两份**同一账号（firekula）**的完整登录抓包：
  A = `streams-live/2343-10013.bin`（2026-10-05 14:43 那次）
  B = `streams-live2/14805-10013.bin`（2026-10-05 14:59 那次）

## 判什么

逐帧、逐字节比 A 与 B：
  * 若首帧**逐字节相同**（或只差极少字节）⇒ 登录帧**确定性** ⇒ 可离线构造 ⇒ M1 大幅简化
  * 若首帧**大片不同** ⇒ 帧里有会话随机量 ⇒ 必须走完整登录链

同时按"帧类型 + 出现序号"配对，报告每一对的变化字节数，
这样"哪些帧是账号相关、哪些是会话相关"一次看清。

用法: nail-loginframe.py <A.bin> <B.bin>
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
    frames, used = dnfdrive.parse_frames(path.read_bytes(), GAME)
    seen = defaultdict(int)
    out = {}
    for f in frames:
        k = (f.cat, f.fid, f.size)
        n = seen[k]
        seen[k] += 1
        out[(f.cat, f.fid, f.size, n)] = (f.raw[GAME.hdr:], f.raw[:GAME.hdr])
    return frames, out, used


def main() -> int:
    a_p, b_p = (Path(x) for x in sys.argv[1:3])
    fa, ia, ua = index_stream(a_p)
    fb, ib, ub = index_stream(b_p)

    print("=" * 78)
    print("同一账号两次登录 —— C→S 流对比")
    print("=" * 78)
    print(f"  A: {a_p.name}  {a_p.stat().st_size} B  {len(fa)} 帧  铺满={ua == a_p.stat().st_size}")
    print(f"  B: {b_p.name}  {b_p.stat().st_size} B  {len(fb)} 帧  铺满={ub == b_p.stat().st_size}")

    print()
    print("  逐帧长度：")
    print("    idx  A(cat,id,size)        B(cat,id,size)")
    for i in range(max(len(fa), len(fb))):
        x = f"{fa[i].cat},{fa[i].fid},{fa[i].size}" if i < len(fa) else "—"
        y = f"{fb[i].cat},{fb[i].fid},{fb[i].size}" if i < len(fb) else "—"
        mark = "" if x == y else "   <<< 不同"
        print(f"    {i:>3}  {x:<20} {y:<20}{mark}")

    print()
    print("=" * 78)
    print("首帧（LOGIN 帧）逐字节对比 —— 决定性的那一条")
    print("=" * 78)
    if not fa or not fb:
        print("  有一边没有帧")
        return 1
    A, B = fa[0], fb[0]
    print(f"  A 头: {A.raw[:GAME.hdr].hex(' ')}   size={A.size}  载荷={len(A.raw) - GAME.hdr} B")
    print(f"  B 头: {B.raw[:GAME.hdr].hex(' ')}   size={B.size}  载荷={len(B.raw) - GAME.hdr} B")
    pa, pb = A.raw[GAME.hdr:], B.raw[GAME.hdr:]
    n = min(len(pa), len(pb))
    diff = [i for i in range(n) if pa[i] != pb[i]]
    same = n - len(diff)
    print(f"  载荷长度: A {len(pa)} / B {len(pb)}")
    print(f"  相同字节: {same}/{n} ({same / n * 100:.1f}%)   不同字节: {len(diff)}")
    print(f"  公共前缀: {next((i for i in range(n) if pa[i] != pb[i]), n)} B")
    if diff:
        print(f"  不同偏移（前 40）: {diff[:40]}{' …' if len(diff) > 40 else ''}")
        print(f"  不同偏移区间数: ", end="")
        # 连续区间
        runs, s = [], diff[0]
        for i in range(1, len(diff)):
            if diff[i] != diff[i - 1] + 1:
                runs.append((s, diff[i - 1]))
                s = diff[i]
        runs.append((s, diff[-1]))
        print(f"{len(runs)}  区间: {runs[:12]}{' …' if len(runs) > 12 else ''}")
    print()
    print("  A 载荷[:64]: " + pa[:64].hex(" "))
    print("  B 载荷[:64]: " + pb[:64].hex(" "))

    print()
    print("=" * 78)
    print("按（帧类型+出现序号）配对：每对的差异")
    print("=" * 78)
    common = sorted(set(ia) & set(ib), key=lambda k: (k[0], k[1], k[2], k[3]))
    for k in common:
        cat, fid, size, occ = k
        pa2, ha = ia[k]
        pb2, hb = ib[k]
        m = min(len(pa2), len(pb2))
        d = sum(1 for i in range(m) if pa2[i] != pb2[i])
        kind = "CMD" if cat == 1 else "NOTI"
        tag = "**逐字节相同**" if d == 0 else f"差 {d} B"
        print(f"  {kind}{fid}({size}) 第{occ}次  载荷{m} B  {tag}"
              f"   hdr A={ha[7:10].hex()} B={hb[7:10].hex()}")
    only_a = sorted(set(ia) - set(ib))
    only_b = sorted(set(ib) - set(ia))
    if only_a or only_b:
        print(f"\n  只在 A 里: {only_a}")
        print(f"  只在 B 里: {only_b}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
