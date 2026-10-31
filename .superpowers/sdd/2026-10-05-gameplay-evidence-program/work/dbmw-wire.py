#!/usr/bin/env python3
r"""dbmw-wire.py —— 从抓包找 dbmw 的线格式，并验证"头由两个 u16 构造"的静态假设。

## 静态侧已给的线索

```
PacketHeader::PacketHeader(unsigned short, unsigned short)
CTcpServer::makePacketHeader(unsigned short, unsigned short)
CPacketDecoder::MsgDecode(PacketHeader*)
```
⇒ 头的**前两字段各 2 字节**。日志里的 packet id（4202/6072/10218/1043/2511…）全 < 65536。

## 本脚本做什么

不预设布局，把两条流按字节打出来，并**扫描**可能的切帧方式：

* 假设 A：`[0..1] = u16 LE id`，`[2..3] = u16 LE size`（或反过来）
  判据：**按 size 切帧能不能恰好铺满**，且 id 落在日志观测到的集合里
* 假设 B：`[2..3] = id`，`[0..1] = size`（大端/小端各试）
* 对每种切法报告：帧数、每帧的 id 序列、是否铺满

日志里出现过的 id（跨三个实例）作为**白名单**用于交叉验证：
  1043 1505 1655 4000 4200 4202 4152 7103 6072 9024 10218
  2511 2513 3111 3145 4010 4017 6085 10209

用法: dbmw-wire.py <stream.bin...>
"""
from __future__ import annotations

import struct
import sys
from pathlib import Path

KNOWN_IDS = {1043, 1505, 1655, 4000, 4200, 4202, 4152, 7103, 6072, 9024, 10218,
             2511, 2513, 3111, 3145, 4010, 4017, 6085, 10209}


def dump_hex(b: bytes, width: int = 16) -> None:
    for i in range(0, len(b), width):
        chunk = b[i:i + width]
        asc = "".join(chr(c) if 32 <= c < 127 else "." for c in chunk)
        print(f"    {i:04x}  {chunk.hex(' '):<{width*3}}  {asc}")


def try_layout(b: bytes, id_off: int, size_off: int, endian: str, size_incl_hdr: bool,
               hdr_len: int):
    """按 size 字段切帧；返回 (帧列表, 消费字节, id 列表)。"""
    frames, off, ids = [], 0, []
    fmt = "<H" if endian == "le" else ">H"
    while off + hdr_len <= len(b):
        size = struct.unpack_from(fmt, b, off + size_off)[0]
        total = size if size_incl_hdr else size + hdr_len
        if total < hdr_len or off + total > len(b):
            break
        fid = struct.unpack_from(fmt, b, off + id_off)[0]
        frames.append(b[off:off + total])
        ids.append(fid)
        off += total
    return frames, off, ids


def main() -> int:
    for a in sys.argv[1:]:
        p = Path(a)
        b = p.read_bytes()
        print("=" * 78)
        print(f"{p.name}  {len(b)} B")
        print("=" * 78)
        dump_hex(b)
        print()

        # 扫描四种布局（id/size 两种次序 × 大小端），头长 4（两个 u16）
        print("  按『两个 u16 头（4 字节）』扫四种布局，看哪种能恰好铺满：")
        best = []
        for id_off, size_off, tag in ((0, 2, "id@0 size@2"), (2, 0, "size@0 id@2")):
            for endian in ("le", "be"):
                for incl in (True, False):
                    fr, used, ids = try_layout(b, id_off, size_off, endian, incl, 4)
                    if not fr:
                        continue
                    exact = used == len(b)
                    hits = sum(1 for i in ids if i in KNOWN_IDS)
                    best.append((exact, hits, len(fr), tag, endian, incl, ids[:8], used))
        for exact, hits, n, tag, endian, incl, ids, used in sorted(best, reverse=True)[:6]:
            print(f"    {tag:<14} {endian} size{'含头' if incl else '不含头'}: "
                  f"{n} 帧, 消费 {used}/{len(b)} 铺满={exact}, "
                  f"id 命中已知集合 {hits}/{n}")
            print(f"        前 8 个 id: {ids}")

        # 也打印前几帧的"两个 u16"候选值，供人眼判断
        print("\n  前 16 字节的 u16 解读（LE / BE）：")
        for i in range(0, min(16, len(b) - 1), 2):
            le = struct.unpack_from("<H", b, i)[0]
            be = struct.unpack_from(">H", b, i)[0]
            mark = ""
            if le in KNOWN_IDS:
                mark += f"   <<< LE={le} 是已知 packet id"
            if be in KNOWN_IDS:
                mark += f"   <<< BE={be} 是已知 packet id"
            print(f"    [{i:>2}] LE={le:<6} BE={be:<6}{mark}")
        print()
    return 0


if __name__ == "__main__":
    sys.exit(main())
