#!/usr/bin/env python3
r"""nail-header13-verify.py —— 把 13 字节头拿到**全部**抓包上验证，并对旧 10 字节模型并存对照。

## 为什么要对全部抓包验

`nail-header13.py` 只在 3 条流上验过。改驱动器之前必须确认：
**13 字节头在全部 10 条真实流上都"恰好铺满"**（切帧靠 size，头长只影响载荷视图，
所以铺满本身不能区分 10 与 13 —— 但如果不铺满，说明我对 size 字段的理解错了）。

更重要的是**区分 10 与 13 的独立判据**：
1. **C→S 的 `[11..12]` 是递增序号**（10 字节模型把这两个字节当作载荷开头）——
   载荷不可能是完美计数器 ⇒ 13 字节胜出。
2. **官方 cfg 白纸黑字写 `header_mangled_length = 13`**。

本脚本把"两个模型各自的铺满情况 + 13 字节模型下 [11..12] 的递增比例"一次列全，
作为"改驱动器"的判据留档。

用法: nail-header13-verify.py -- <stream.bin...>
"""
from __future__ import annotations

import struct
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE / "driver"))

import dnfdrive                        # noqa: E402
from dnfdrive import GAME              # noqa: E402


def tile(blob: bytes, hdr: int):
    """size 在 [3..6]、含整帧地切；头长只用于判断 size 是否 ≥ 头长。"""
    out, off = [], 0
    while off + hdr <= len(blob):
        size = struct.unpack_from("<I", blob, off + 3)[0]
        if size < hdr or off + size > len(blob):
            break
        out.append(blob[off:off + size])
        off += size
    return out, off


def main() -> int:
    argv = [a for a in sys.argv[1:] if a != "--"]
    print(f"{'流':<26}{'方向':<5}{'字节':>8}{'帧':>5}{'10B铺满':>9}{'13B铺满':>9}"
          f"{'C→S序号+1':>11}")
    print("-" * 76)
    all_ok = True
    for a in argv:
        p = Path(a)
        blob = p.read_bytes()
        f10, u10 = tile(blob, 10)
        f13, u13 = tile(blob, 13)
        # 方向用**分类字节**判（cat=1 CMD ⇒ C→S；cat=0 NOTI ⇒ S→C）——
        # 不能用文件名硬编码：`30011-46386.bin` 看着像 C→S，其实是服务端在发。
        d = "?" if not f13 else ("C→S" if f13[0][0] == 1 else "S→C")
        seq = ""
        if d == "C→S" and len(f13) > 2:
            vals = [struct.unpack_from("<H", f, 11)[0] for f in f13 if len(f) >= 13]
            inc = sum(1 for i in range(1, len(vals))
                      if vals[i] == (vals[i - 1] + 1) & 0xFFFF)
            seq = f"{inc}/{len(vals) - 1}"
            if inc != len(vals) - 1:
                all_ok = False
        ok10, ok13 = u10 == len(blob), u13 == len(blob)
        if not ok13:
            all_ok = False
        print(f"{p.name:<26}{d:<5}{len(blob):>8}{len(f13):>5}"
              f"{str(ok10):>9}{str(ok13):>9}{seq:>11}")

    print()
    print("判读：")
    print("  * 两个模型都会'铺满'（切帧靠 size 字段，与头长无关）⇒ 铺满**不能**用来区分。")
    print("  * 区分靠两条独立证据：① C→S 的 [11..12] 是**完美递增序号**（10 字节模型")
    print("    会把它当载荷开头，而载荷不可能是计数器）；② 官方 cfg 写 header_mangled_length = 13。")
    print(f"\n  13 字节模型在全部 {len(argv)} 条流上均铺满: {all_ok}")
    return 0 if all_ok else 1


if __name__ == "__main__":
    sys.exit(main())
