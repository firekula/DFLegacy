#!/usr/bin/env python3
r"""nail-binary-diff.py —— 钉"私服的 df_game_r 相对官服 build 改了哪 103 字节"。

## 为什么这条值得钉

* M0 的全部静态结论（425 家族派发器、1171 条 opcode 映射、7273 条 handler）都是在
  `$O\df_game_r.bin` 上做的。VM 跑的是**同尺寸、同 build、差 103 字节**的另一份。
  ⇒ 只要这 103 字节**不落在** M0 分析到的地址上/不改变表结构，M0 的结论对 VM 同样成立。
  这是"能不能把 M0 的静态证据用到 VM 上"的**前置判据**。
* 补丁内容本身也说明"私服相对官服改了什么" —— 重写的目标是**官方架构**，
  所以这些补丁属于"**非官方**"部分，要知道它们是什么、别照抄。

## 做什么

把 47 个差异区间逐个打印：偏移、两边字节、以及两侧的可见 ASCII 上下文。
短区间（几字节）多半是常量/立即数；较长区间（17 B、7 B）多半是字符串。

用法: nail-binary-diff.py <A.bin> <B.bin>
"""
from __future__ import annotations

import sys
from pathlib import Path


def ascii_of(b: bytes) -> str:
    return "".join(chr(c) if 32 <= c < 127 else "." for c in b)


def main() -> int:
    a = Path(sys.argv[1]).read_bytes()
    b = Path(sys.argv[2]).read_bytes()
    if len(a) != len(b):
        print(f"长度不同: {len(a)} vs {len(b)} —— 不是同一 build，后续结论不适用")
        return 1

    d = [i for i in range(len(a)) if a[i] != b[i]]
    runs = []
    s = d[0]
    for i in range(1, len(d)):
        if d[i] != d[i - 1] + 1:
            runs.append((s, d[i - 1]))
            s = d[i]
    runs.append((s, d[-1]))

    print(f"A = {sys.argv[1]}")
    print(f"B = {sys.argv[2]}")
    print(f"长度相同 ({len(a)} B)；不同 {len(d)} 字节，{len(runs)} 段")
    print()
    print("逐段（两侧各带 16 B 上下文；ASCII 里 `.` = 不可打印）")
    print("=" * 100)

    for s, e in runs:
        ctx0 = max(0, s - 16)
        ctx1 = min(len(a), e + 17)
        print(f"\n偏移 0x{s:08x}..0x{e:08x}  ({e - s + 1} B)")
        print(f"  A: {a[s:e + 1].hex(' ')}")
        print(f"  B: {b[s:e + 1].hex(' ')}")
        print(f"  A ctx: {ascii_of(a[ctx0:ctx1])}")
        print(f"  B ctx: {ascii_of(b[ctx0:ctx1])}")
        # 若差异区间的字节看起来是可打印 ASCII，直接给出来
        if all(32 <= c < 127 for c in a[s:e + 1]) or all(32 <= c < 127 for c in b[s:e + 1]):
            print(f"  ** 字符串差异 **  A={a[s:e + 1]!r}  B={b[s:e + 1]!r}")

    print()
    print("=" * 100)
    print("汇总：可打印字符串型差异（重写时这些属于『非官方』，不要照抄）")
    print("=" * 100)
    n_str = 0
    for s, e in runs:
        pa = all(32 <= c < 127 for c in a[s:e + 1])
        pb = all(32 <= c < 127 for c in b[s:e + 1])
        if pa or pb:
            n_str += 1
            print(f"  0x{s:08x}: A={a[s:e + 1]!r}  B={b[s:e + 1]!r}")
    print(f"\n  字符串型差异 {n_str} 处，其余 {len(runs) - n_str} 处是二进制常量/立即数。")
    return 0


if __name__ == "__main__":
    sys.exit(main())
