#!/usr/bin/env python3
r"""nail-header13.py —— 用官方 cfg 给出的字段布局验证 13 字节头，并攻 checksum/sequence。

## 官方 cfg 的原话（/home/neople/game/cfg/siroco11.cfg L82–89，逐字）

```
header_classification = 0 1     header_msg_no  = 5 1
header_sLength        = 1 4     //header_garvage = 6 1
header_checksum       = 7 4     header_sequence = 11 2
header_load_interval  = 10      header_mangled_length = 13
```

字段集合与宽度：classification(1) + msg_no(1) + garvage(1) + sLength(4)
                  + checksum(4) + sequence(2) = **13**

## 我们的客户端抓包里观测到的（`dnfdrive.GAME` 的 10 字节模型）

`[0]cat [1]id [2]0/1/2 [3..6]size(LE,含头) [7..9]3 字节`
（`[7] == payload[1]` 在 S→C 方向 180/180 —— 一直没解释）

**若真头是 13 字节**，则我们的"`[7..9]` 3 字节"其实是 `checksum[0..2]`，
而 `payload[1]`（= raw[11]）其实是 **`sequence[0]`**。
⇒ 那条 180/180 的关系就变成 **`checksum[0] == sequence[0]`** —— 依旧怪，
但也可能是我们的 10/13 切法本身错了。本脚本把这些都摆到数据面前。

## 判什么

1. **13 字节切法是否也恰好铺满**（size 字段含整帧 ⇒ 无论头多长都应铺满）。
2. **`sequence`（raw[11..12]）是否递增** —— 递增就坐实它是序号。
3. **`checksum`（raw[7..10]）是否等于 4 字节的 adler32/crc32/自加和**（对 `raw[13:]`）。
4. 对照：按 13 字节头重新算 `[7]==raw[11]` 这条关系还成不成立。

用法: nail-header13.py -- <stream.bin...>
"""
from __future__ import annotations

import struct
import sys
import zlib
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE / "driver"))

import dnfdrive                        # noqa: E402
from dnfdrive import GAME              # noqa: E402

HDR13 = 13


def frames13(blob: bytes, hdr: int = HDR13):
    """按"size 在 [3..6]、含整帧"切帧，但头长用 hdr。"""
    out, off = [], 0
    while off + hdr <= len(blob):
        size = struct.unpack_from("<I", blob, off + 3)[0]
        if size < hdr or off + size > len(blob):
            break
        out.append((off, blob[off:off + size]))
        off += size
    return out, off


def u32(b): return struct.unpack("<I", b)[0]
def u16(b): return struct.unpack("<H", b)[0]


def main() -> int:
    argv = [a for a in sys.argv[1:] if a != "--"]
    for p in (Path(a) for a in argv):
        blob = p.read_bytes()
        fr, used = frames13(blob)
        direction = "S→C" if p.name.startswith("10013") else "C→S"
        print("=" * 80)
        print(f"[{direction}] {p.name}  {len(blob)} B  13 字节头切出 {len(fr)} 帧  "
              f"铺满={used == len(blob)}（消费 {used}）")
        print("=" * 80)
        if not fr:
            continue

        # 1) sequence 递增？
        seqs = [u16(f[11:13]) for _o, f in fr]
        print(f"  sequence(raw[11..12] LE) 前 12 个: {seqs[:12]}")
        inc = sum(1 for i in range(1, len(seqs)) if seqs[i] == (seqs[i - 1] + 1) & 0xFFFF)
        print(f"  严格 +1 的比例: {inc}/{len(seqs) - 1}"
              f"  ({(inc / max(1, len(seqs) - 1) * 100):.1f}%)")
        uniq = len(set(seqs))
        print(f"  取值种类: {uniq}/{len(seqs)}"
              f"{'  ⇒ 像递增计数器' if inc > len(seqs) * 0.6 else ''}")

        # 2) checksum 是不是 4 字节校验（对 raw[13:]）
        hits = {"adler32(payload)": 0, "crc32(payload)": 0, "sum32(payload)": 0,
                "adler32(raw[7:] 之后?)": 0, "crc32(整帧前 7B+payload)": 0}
        for _o, f in fr:
            ck = u32(f[7:11])
            pl = f[HDR13:]
            if zlib.adler32(pl) & 0xFFFFFFFF == ck: hits["adler32(payload)"] += 1
            if zlib.crc32(pl) & 0xFFFFFFFF == ck: hits["crc32(payload)"] += 1
            if (sum(pl) & 0xFFFFFFFF) == ck: hits["sum32(payload)"] += 1
            if zlib.crc32(f[:7] + pl) & 0xFFFFFFFF == ck:
                hits["crc32(整帧前 7B+payload)"] += 1
        print(f"  checksum(raw[7..10] LE) 与候选 4 字节校验的吻合次数（共 {len(fr)} 帧）:")
        for k, v in hits.items():
            print(f"     {k:<28} {v}")

        # 3) 旧关系在 13 字节切法下还成立吗
        rel_a = sum(1 for _o, f in fr if len(f) > 11 and f[7] == f[11])
        rel_b = sum(1 for _o, f in fr if len(f) > 13 and f[7] == f[13])
        print(f"  raw[7] == raw[11](=sequence[0]) : {rel_a}/{len(fr)}")
        print(f"  raw[7] == raw[13](=payload[0])  : {rel_b}/{len(fr)}")
        # 空载荷帧不算进 rel_b 的分母（请求帧可能只有头）
        n13 = sum(1 for _o, f in fr if len(f) > 13)
        print(f"    （其中 {n13} 帧有载荷，其余为空载荷帧）")

        # 4) 抽一帧看 13 字节头的全貌
        o, f = fr[0]
        print(f"  首帧头 13 B: {f[:13].hex(' ')}")
        print(f"     classification={f[0]:#04x} msg_no={f[1]:#04x} garvage={f[2]:#04x} "
              f"sLength={u32(f[3:7])} checksum={f[7:11].hex(' ')} sequence={u16(f[11:13])}")
        print(f"     载荷({len(f) - HDR13} B)[:24]: {f[HDR13:HDR13 + 24].hex(' ')}")
        print()
    return 0


if __name__ == "__main__":
    sys.exit(main())
