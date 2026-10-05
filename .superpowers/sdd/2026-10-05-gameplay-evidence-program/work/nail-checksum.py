#!/usr/bin/env python3
r"""nail-checksum.py —— 钉 `[7..9]` 是不是校验和，以及是哪种。

## 已知（前两个脚本的实测）

* S→C：`hdr[7] == payload[1]` **180/180**（两条独立抓包）。⇒ `hdr[7]` 是 `payload[1]` 的副本。
* C→S：该关系不成立（0/42）。
* 载荷逐字节位置**全高熵**（取值种类≈帧数）⇒ 载荷整体被混淆/加密，没有明文小头。
* `hdr[8]`/`hdr[9]` 与 `payload[2]`/`payload[3]`/帧长/帧序号**都无关**。

## 本脚本试什么

既然 `hdr[7]` 等于 `payload[1]`，那么"校验和存在载荷第 2 字节、同时被抄进头里"是个
自然的假设。试一批候选校验算法，覆盖 `payload[2:]`（或其它区间），看哪一个能**在
S→C 的全部帧上**复现出 `payload[1]`：

  sum8 / sum16 / xor8 / adler32(低/高字节/模256) / crc32(各字节) /
  取反 / +1 / 以及 3 字节整体 `hdr[7:10]` 作为 24 位值与候选值比对

判据是**可证伪的**：某算法若在 90/90 帧上成立，就是它（或与它等价）；
若全部 <90/90，则假设被否证，把最好成绩也一并记录（避免"差不多"被当成"就是"）。

项目背景：符号表里有 `Encryption::Adler32`（spec §1 提到），所以 adler32 优先级最高。

用法: nail-checksum.py -- <stream.bin...>
"""
from __future__ import annotations

import sys
import zlib
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE / "driver"))

import dnfdrive                        # noqa: E402
from dnfdrive import GAME              # noqa: E402


def candidates(payload: bytes) -> dict:
    """对一段字节算出一堆候选校验值。返回 {名字: 值(0..255)}。"""
    body = payload[2:] if len(payload) > 2 else b""
    out = {}
    out["sum8(body)&0xff"] = sum(body) & 0xFF
    out["sum16(body)&0xff"] = (sum(body) & 0xFFFF) & 0xFF
    out["xor8(body)"] = 0
    x = 0
    for b in body:
        x ^= b
    out["xor8(body)"] = x
    a = zlib.adler32(body) & 0xFFFFFFFF
    out["adler32(body)&0xff"] = a & 0xFF
    out["adler32(body)>>8&0xff"] = (a >> 8) & 0xFF
    out["adler32(body)>>16&0xff"] = (a >> 16) & 0xFF
    out["adler32(body)>>24&0xff"] = (a >> 24) & 0xFF
    c = zlib.crc32(body) & 0xFFFFFFFF
    out["crc32(body)&0xff"] = c & 0xFF
    out["crc32(body)>>8&0xff"] = (c >> 8) & 0xFF
    out["crc32(body)>>16&0xff"] = (c >> 16) & 0xFF
    out["crc32(body)>>24&0xff"] = (c >> 24) & 0xFF
    # 全载荷（含前 2 字节）也算一遍
    out["adler32(payload)&0xff"] = zlib.adler32(payload) & 0xFF
    out["crc32(payload)&0xff"] = zlib.crc32(payload) & 0xFF
    out["sum8(payload)&0xff"] = sum(payload) & 0xFF
    return out


def main() -> int:
    argv = [a for a in sys.argv[1:] if a != "--"]
    streams = [Path(a) for a in argv]

    for sp in streams:
        frames, _ = dnfdrive.parse_frames(sp.read_bytes(), GAME)
        direction = "S→C" if sp.name.startswith("10013-") else "C→S"
        usable = [f for f in frames if len(f.raw) - GAME.hdr >= 3]
        print("=" * 78)
        print(f"[{direction}] {sp.name}: {len(frames)} 帧（{len(usable)} 帧可测）")
        print("=" * 78)
        print(f"  关系核对：hdr[7] == payload[1]  "
              f"{sum(1 for f in usable if f.raw[7] == f.raw[GAME.hdr + 1])}/{len(usable)}")
        print()

        # 对每个候选算法统计命中率
        tally: dict[str, int] = {}
        for f in usable:
            p = f.raw[GAME.hdr:]
            target = p[1]                       # = hdr[7]
            for name, val in candidates(p).items():
                if val == target:
                    tally[name] = tally.get(name, 0) + 1
        n = len(usable)
        print("  候选校验和 命中 payload[1] 的次数：")
        for name, cnt in sorted(tally.items(), key=lambda kv: -kv[1]):
            pct = cnt / n * 100 if n else 0
            flag = "  <<< 全中" if cnt == n and n else ""
            print(f"     {name:<28} {cnt:>4}/{n}  ({pct:5.1f}%){flag}")
        if not tally:
            print("     （没有任何候选命中过一次）")
        print()

    return 0


if __name__ == "__main__":
    sys.exit(main())
