#!/usr/bin/env python3
r"""e2-packet-class.py —— 在客户端里定位"按 id 选包类"的工厂，并列出 id → 构造函数的映射。

动机（E2）：E2 在 `0x573ccb` 一带发现
```
push 0x834 ; call 0x91857a      ; 分配
mov [ebp-4], 0x158              ; ★ 类 id
je  0x575c2a                    ; 分配失败 → 跳到"未知类"路径
... call 0x566b40               ; 该类的构造函数
```
相邻的 `0x156/0x157/0x159/0x15f…` 说明这是一张 **id → 构造函数** 的分发表。
其中 **class id 344 (`0x158`) 的分配大小 = 0x834 = 2100 B**，而游戏面登录首帧的载荷是 400 B
⇒ 如果 `0x158` 是「登录包」的类 id，那 2100 B 的类里就包含了那个 400 B 载荷缓冲
（**这正是 §4.C 要的生产端入口**）。

本脚本：
  1. 在 `.rdata`/`.data` 里找 id 表（连续 dword，能映射到代码段地址）；
  2. 打印每个 id 对应的构造函数；
  3. 打印目标 id 的构造函数入口，供 `ida-dec` 继续反编译。
"""
from __future__ import annotations

import argparse
import struct
from pathlib import Path


def pe(path: Path):
    """用 pefile（工程既有依赖）解析；失败则报错而不是猜。"""
    try:
        import pefile  # type: ignore
    except ImportError:
        raise SystemExit("需要 pefile（本机已装有 capstone/pefile，见 .recon/onboarding.md §3）")
    return pefile.PE(str(path), fast_load=False)


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("exe")
    ap.add_argument("--id-base", type=lambda x: int(x, 0), default=0x156)
    ap.add_argument("--count", type=int, default=24)
    ap.add_argument("--table", type=lambda x: int(x, 0), default=None,
                    help="候选表地址（VA）；不给则自动搜索")
    ap.add_argument("--code-ranges", nargs="*", type=lambda x: int(x, 0), default=None)
    args = ap.parse_args()

    pe_f = pe(Path(args.exe))
    base = pe_f.OPTIONAL_HEADER.ImageBase
    print(f"# image_base={base:#x}  sections:")
    code_ranges = args.code_ranges or []
    for s in pe_f.sections:
        nm = s.Name.rstrip(b"\x00").decode("ascii", "replace")
        va = base + s.VirtualAddress
        print(f"#   {nm:10s} va={va:#010x} vsize={s.Misc_VirtualSize:#x} raw={s.SizeOfRawData:#x} "
              f"char={s.Characteristics:#x}")
        if s.Characteristics & 0x20000000:      # IMAGE_SCN_MEM_EXECUTE
            code_ranges.append((va, va + max(s.Misc_VirtualSize, s.SizeOfRawData)))

    def in_code(v: int) -> bool:
        return any(a <= v < b for a, b in code_ranges)

    def rd(va: int, n: int) -> bytes | None:
        try:
            return pe_f.get_data(va - base, n)
        except Exception:
            return None

    # 自动搜索：找"连续 N 个 dword 大多落在代码段"的窗口
    if args.table is None:
        print("\n# 自动搜索 id 表（连续 dword 指向代码段）...")
        cands = []
        for s in pe_f.sections:
            nm = s.Name.rstrip(b"\x00").decode("ascii", "replace")
            if not (s.Characteristics & 0x40000000):   # 初始化数据段
                continue
            start = base + s.VirtualAddress
            size = min(s.SizeOfRawData, s.Misc_VirtualSize)
            blob = pe_f.get_data(s.VirtualAddress, size)
            if not blob:
                continue
            n = size // 4
            vals = struct.unpack_from("<%dI" % n, blob, 0)
            run = 0
            for i, v in enumerate(vals):
                if in_code(v):
                    run += 1
                    if run >= args.count:
                        cands.append((start + (i - run + 1) * 4, nm))
                        run = 0
                else:
                    run = 0
        for va, nm in cands[:10]:
            print(f"#   候选表 {va:#010x}（在 {nm}）")
        if not cands:
            print("#   没找到候选表")
            return 1
        args.table = cands[0][0]

    print(f"\n## 表 {args.table:#010x}，id {args.id_base:#x}..{args.id_base + args.count - 1:#x}")
    data = rd(args.table, args.count * 4)
    if not data:
        print("读取失败")
        return 1
    for i in range(args.count):
        v = struct.unpack_from("<I", data, i * 4)[0]
        pid = args.id_base + i
        print(f"  id {pid} ({pid:#x}) -> {v:#010x}" + ("  (code)" if in_code(v) else ""))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
