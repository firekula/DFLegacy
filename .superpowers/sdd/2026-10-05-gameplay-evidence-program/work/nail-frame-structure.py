#!/usr/bin/env python3
r"""nail-frame-structure.py —— 钉住游戏面帧头里两个未确认项。

## 钉什么

1. **`[7..9]` 那 3 字节到底是什么**（B0/dnfdrive 里标 `[未验证]`）。
   重放输出里目视发现 `hdr[7] == payload[1]` 在 S→C 方向似乎**每一帧都成立**。
   目视不算证据 —— 这里对**全部帧**做统计，给出成立/不成立的计数。

2. **令牌在不在帧里**：拿**两把**公钥（VM 游戏服 `BEF69F43…`、登录服 PHP 那把
   `E21F97…` 推出来的公钥）分别扫每个 256 B 滑窗，看有没有裸 PKCS#1 type-1 块。
   两把都扫不到 ⇒ 令牌被变换过（加密/编码），"原样重放"是唯一可行路径。

## 为什么这两条重要

* `hdr[7..9]` 若真与载荷有关，那它**不是**纯校验和 —— 我们之前（凭日志名
  `check_sum error`）把它定性为校验和就是**过度解读**，需要修正。
* 令牌若不在帧里，M1 单元②就必须先弄清变换方式，否则无法为任意账号构造登录帧。

用法: nail-frame-structure.py <pubkey...> --  <stream.bin...>
"""
from __future__ import annotations

import sys
from collections import Counter
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE / "driver"))

import dnfdrive                        # noqa: E402
from dnfdrive import CHANNEL, GAME     # noqa: E402

from cryptography.hazmat.primitives import serialization   # noqa: E402


def scan_token(payload: bytes, nums, limit=8):
    """找能被该公钥解成 type-1 的 256 B 滑窗。"""
    hits = []
    for off in range(0, max(0, len(payload) - 255) + 1):
        blk = pow(int.from_bytes(payload[off:off + 256], "big"),
                  nums.e, nums.n).to_bytes(256, "big")
        if blk[0] == 0x00 and blk[1] == 0x01:
            try:
                if blk.index(b"\x00", 2) - 2 >= 8:
                    hits.append((off, blk[blk.index(b"\x00", 2) + 1:]))
                    if len(hits) >= limit:
                        break
            except ValueError:
                pass
    return hits


def main() -> int:
    argv = sys.argv[1:]
    if "--" not in argv:
        raise SystemExit(__doc__)
    cut = argv.index("--")
    pub_paths = [Path(p) for p in argv[:cut]]
    streams = [Path(p) for p in argv[cut + 1:]]

    keys = {}
    for p in pub_paths:
        n = serialization.load_pem_public_key(p.read_bytes()).public_numbers()
        keys[p.name] = n
        print(f"公钥 {p.name}: n={format(n.n, 'X')[:32]}…")

    print()
    print("=" * 78)
    print("钉 1：hdr[7] 与 payload 的关系（对**全部**帧统计，不靠目视）")
    print("=" * 78)

    relations = {
        "hdr[7] == payload[0]": 0,
        "hdr[7] == payload[1]": 0,
        "hdr[8] == payload[2]": 0,
        "hdr[9] == payload[3]": 0,
        "hdr[7:10] 出现在 payload 前 16 B 里": 0,
        "hdr[7:10] 出现在 payload 任意位置": 0,
    }
    total = 0
    per_stream = {}

    for sp in streams:
        blob = sp.read_bytes()
        frames, used = dnfdrive.parse_frames(blob, GAME)
        cnt = Counter()
        for f in frames:
            h = f.raw[7:10]
            p = f.raw[GAME.hdr:]
            if len(p) < 4:
                continue
            total += 1
            if h[0] == p[0]:
                cnt["hdr[7] == payload[0]"] += 1
            if h[0] == p[1]:
                cnt["hdr[7] == payload[1]"] += 1
            if h[1] == p[2]:
                cnt["hdr[8] == payload[2]"] += 1
            if h[2] == p[3]:
                cnt["hdr[9] == payload[3]"] += 1
            if h in p[:16]:
                cnt["hdr[7:10] 出现在 payload 前 16 B 里"] += 1
            if h in p:
                cnt["hdr[7:10] 出现在 payload 任意位置"] += 1
        for k, v in cnt.items():
            relations[k] += v
        per_stream[sp.name] = (len(frames), dict(cnt))

    for name, (nf, cnt) in per_stream.items():
        print(f"\n  {name}: {nf} 帧")
        for k in relations:
            print(f"     {k:<44} {cnt.get(k, 0):>4}/{nf}")
    print(f"\n  合计 {total} 帧：")
    for k, v in relations.items():
        pct = v / total * 100 if total else 0
        print(f"     {k:<44} {v:>4}/{total}  ({pct:5.1f}%)")

    print()
    print("=" * 78)
    print("钉 2：令牌在不在帧里（两把公钥分别扫 256 B 滑窗）")
    print("=" * 78)
    for sp in streams:
        blob = sp.read_bytes()
        frames, _ = dnfdrive.parse_frames(blob, GAME)
        if not frames:
            continue
        # 只扫 C→S 首帧（登录帧）—— 令牌只可能在那里
        payload = frames[0].raw[GAME.hdr:]
        print(f"\n  {sp.name} 首帧载荷 {len(payload)} B")
        for kname, nums in keys.items():
            hits = scan_token(payload, nums)
            if hits:
                for off, msg in hits:
                    print(f"     [{kname}] *** 命中 @载荷偏移 {off}  "
                          f"被签内容 {len(msg)} B: {msg.hex(' ')}")
            else:
                print(f"     [{kname}] 无命中（{max(0, len(payload) - 255)} 个滑窗）")

    return 0


if __name__ == "__main__":
    sys.exit(main())
