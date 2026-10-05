#!/usr/bin/env python3
r"""read-va.py —— 读 ELF 里某个 VA 处的字节，并尽量解释成字符串/结构。

用法: read-va.py <elf> <va> [<长度>]
"""
from __future__ import annotations

import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

from importlib.util import module_from_spec, spec_from_file_location   # noqa: E402

_spec = spec_from_file_location("elfrecon", HERE / "elfrecon.py")
m = module_from_spec(_spec)
sys.modules["elfrecon"] = m
_spec.loader.exec_module(m)


def main() -> int:
    elf = m.Elf(sys.argv[1])
    va = int(sys.argv[2], 0)
    n = int(sys.argv[3]) if len(sys.argv) > 3 else 256

    off = elf.va_to_off(va)
    print(f"VA 0x{va:08x} -> 文件偏移 {('0x%x' % off) if off is not None else '（不在节内）'}")
    if off is None:
        return 2
    blob = elf.data[off:off + n]
    print(f"\n十六进制（{len(blob)} B）:")
    for i in range(0, len(blob), 16):
        chunk = blob[i:i + 16]
        asc = "".join(chr(c) if 32 <= c < 127 else "." for c in chunk)
        print(f"  {i:04x}  {chunk.hex(' '):<48}  {asc}")

    # 试着当 C 字符串
    end = blob.find(b"\x00")
    if end > 0:
        s = blob[:end]
        printable = sum(1 for c in s if 32 <= c < 127)
        print(f"\n首个 NUL 之前 {end} B，可打印 {printable}/{end}")
        if printable == end:
            print("  当字符串看:")
            for line in s.decode("ascii").splitlines()[:12]:
                print(f"    {line}")

    # 试着当 PEM
    if b"-----BEGIN" in blob:
        print("\n  ** 含 PEM 头 **")
    # 试着当指针数组
    import struct
    if len(blob) >= 8:
        print("\n  前 16 个 dword（看是不是指针表）:")
        for i in range(0, min(len(blob) - 3, 64), 4):
            v = struct.unpack_from("<I", blob, i)[0]
            note = ""
            if 0x08000000 <= v < 0x0a000000:
                note = "  <- 像代码/数据段内的地址"
            print(f"    [{i:>2}] 0x{v:08x}{note}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
