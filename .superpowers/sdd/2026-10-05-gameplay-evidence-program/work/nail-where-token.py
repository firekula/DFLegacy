#!/usr/bin/env python3
r"""nail-where-token.py —— 在登录帧里定位令牌：搜"好认的字节模式"。

## 思路

登录令牌的被签内容是 46 B = `uid(4,BE) | 01×32 | 55 91 45 10 01 04 03 03 01 01`
（`logincon.php` L177 逐字确认过）。其中有两段**极好认的模式**：

  * 32 个连续 `0x01`  —— 这么长的同值串在随机数据里几乎不可能出现
  * 10 字节尾巴 `55 91 45 10 01 04 03 03 01 01`

如果登录帧里放的是**明文消息**（而不是签名），这两段就该直接出现。
如果放的是**RSA 签名**，那它们被加密了、搜不到 —— 但那就应该能被公钥解出
（已试过两把公钥，都没有）。

所以本脚本的结果是二选一的**可证伪判据**：
  * 搜到 `01×32` 或尾巴 ⇒ 帧里是**明文消息**（哪怕没有签名）⇒ 我们可自造
  * 都搜不到           ⇒ 帧里的令牌被变换过（或不在这一帧）

顺带：把 `uid` 的大端 4 字节在载荷里搜一遍（账号相关的区域应该包含它，
哪怕被混淆过，也可能有未混淆的副本）。

用法: nail-where-token.py <frame-stream.bin> <uid> [<frame-stream2.bin> <uid2>]
"""
from __future__ import annotations

import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE / "driver"))

import dnfdrive                        # noqa: E402
from dnfdrive import GAME              # noqa: E402

TRAILER = bytes.fromhex("55 91 45 10 01 04 03 03 01 01")
ONES32 = b"\x01" * 32


def find_all(hay: bytes, needle: bytes):
    out, i = [], hay.find(needle)
    while i >= 0:
        out.append(i)
        i = hay.find(needle, i + 1)
    return out


def main() -> int:
    args = sys.argv[1:]
    pairs = [(Path(args[i]), int(args[i + 1])) for i in range(0, len(args) - 1, 2)]

    for path, uid in pairs:
        blob = path.read_bytes()
        frames, used = dnfdrive.parse_frames(blob, GAME)
        if not frames:
            print(f"{path.name}: 无帧")
            continue
        p = frames[0].raw[GAME.hdr:]
        print("=" * 78)
        print(f"{path.name}  首帧载荷 {len(p)} B   uid={uid} ({hex(uid)})")
        print("=" * 78)

        # 1) 好认的模式
        for name, needle in (("32×0x01", ONES32), ("10 B 尾巴", TRAILER)):
            hits = find_all(p, needle)
            print(f"  搜 {name:<10} -> {hits if hits else '未出现'}")
            if hits:
                o = hits[0]
                ctx = p[max(0, o - 8):o + len(needle) + 8]
                print(f"       上下文: {ctx.hex(' ')}")

        # 2) uid 大端 4 字节
        for endian in ("big", "little"):
            nb = uid.to_bytes(4, endian)
            hits = find_all(p, nb)
            print(f"  uid {endian}-endian ({nb.hex(' ')}) -> {hits if hits else '未出现'}")

        # 3) 最长同值串（找别的"填充"迹象）
        best = (0, 0, 0)      # (len, byte, off)
        i = 0
        while i < len(p):
            j = i
            while j + 1 < len(p) and p[j + 1] == p[i]:
                j += 1
            if j - i + 1 > best[0]:
                best = (j - i + 1, p[i], i)
            i = j + 1
        print(f"  最长同值串: {best[0]} 个 {best[1]:#04x} @ 偏移 {best[2]}")

        # 4) 打印结构化切片（按上一轮发现的恒定偏移 1,2,76,200）
        print(f"  切片（偏移: 字节）:")
        for o in (0, 1, 2, 3, 76, 77, 200, 201):
            if o < len(p):
                print(f"     [{o:>3}] {p[o]:#04x}")
        print(f"  载荷[:80]: {p[:80].hex(' ')}")
        print()

    print("判读：搜到 32×0x01 或 10 B 尾巴 ⇒ 帧里是**明文消息**（我们可自造）；")
    print("      都没有 ⇒ 令牌被变换过，需要先弄清变换。")
    return 0


if __name__ == "__main__":
    sys.exit(main())
