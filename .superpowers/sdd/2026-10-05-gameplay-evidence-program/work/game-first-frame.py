#!/usr/bin/env python3
r"""game-first-frame.py —— 游戏面 C 流首帧载荷的**可观测性**检查（Task 11 判据 3 的依据）。

## 它回答什么问题

Task 11 判据 3 说"令牌替换**未验证**"，理由是"首帧里没有能通过容器公钥模幂的 256 B 块"。
但那句话有两种非常不同的解释，**必须区分开**，否则就是把"没找到"写成了"没有"：

  (i)  首帧里**根本没有** 256 B 量级的不透明块  ⇒ 令牌在别的地方或以别的形式存在
  (ii) 首帧里**有**这样一个块，只是它不归容器那把公钥管（VM 自己的密钥 / 已被加密）

做法（三条互相独立的观察）：
  1. 载荷整体的可打印率；
  2. 载荷里有没有 `00 00 00 xx` 这种 uid 大端模式；
  3. 每个 256 B 滑窗做公钥模幂，看有没有落在 PKCS#1 type-1（`00 01 FF…`）上 ——
     并**与随机基线对照**（随机数据的命中期望 ≈ 窗口数 / 65536）。
     只有远高于基线才算"真的找到了"，低于基线就是"没有"，不能说成"扫不到"。

## 实测结论（2026-10-05，两个靶子一致）

| 靶 | 载荷 | 可打印率 | `00 00 00 xx` | type-1 命中 / 随机基线 |
|---|---|---|---|---|
| VM（成功）`13681-10013.bin` | 403 B | 38.0% | 0 个 | 0 / 0.0023 |
| docker（拒绝）`33018-30011.bin` | 403 B | 34.7% | 0 个 | 0 / 0.0023 |

⇒ 两个靶子都是 (i)+(ii) 的中间态：**有** 403 B 的整体不透明块，但**没有任何**证据说明那是
容器公钥体系里的裸令牌。所以判据 3 走计划的**退化分支**（逐字节原样重放 + 显式记录"未验证"）。

用法:
  game-first-frame.py <stream.bin> <pubkey.pem> [--hdr 10] [--blocks 10]
"""
from __future__ import annotations

import argparse
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE / "driver"))

# 先 import dnfdrive（它带"标准库 token 防遮蔽"守卫，见其文件头）
import dnfdrive                        # noqa: E402
from dnfdrive import GAME              # noqa: E402

from cryptography.hazmat.primitives import serialization   # noqa: E402


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description="游戏面首帧载荷可观测性检查")
    ap.add_argument("stream", type=Path, help="C 流文件（如 streams-vm/13681-10013.bin）")
    ap.add_argument("pubkey", type=Path, help="公钥 PEM（用它的 e/n 做模幂）")
    ap.add_argument("--hdr", type=int, default=None,
                    help="帧头长度（默认用 dnfdrive.GAME.hdr）")
    ap.add_argument("--blocks", type=int, default=10, help="打印前 N 个 32 B 分块")
    a = ap.parse_args(argv)

    hdr = GAME.hdr if a.hdr is None else a.hdr
    blob = a.stream.read_bytes()
    frames, used = dnfdrive.parse_frames(blob, GAME)
    if not frames:
        print(f"FAIL: {a.stream} 里切不出任何帧（帧模型不匹配？）")
        return 2
    if used != len(blob):
        print(f"警告: 只切出 {used}/{len(blob)} B（有残帧）")

    first = frames[0]
    payload = first.raw[hdr:]
    nums = serialization.load_pem_public_key(a.pubkey.read_bytes()).public_numbers()

    print(f"流文件   : {a.stream}  ({len(blob)} B, {len(frames)} 帧)")
    print(f"首帧     : cat=0x{first.cat:02x} id={first.fid} size={first.size} "
          f"载荷={len(payload)} B")
    print(f"首帧头   : {first.raw[:hdr].hex(' ')}")
    print(f"载荷[:48]: {payload[:48].hex(' ')}")

    printable = sum(1 for b in payload if 32 <= b < 127)
    print(f"\n[1] 可打印率: {printable}/{len(payload)} = {printable / len(payload):.1%}")
    print("    按 32 B 分块（看有没有'一段明文 + 一段不透明'的分区）:")
    for i in range(0, min(len(payload), 32 * a.blocks), 32):
        chunk = payload[i:i + 32]
        n = sum(1 for b in chunk if 32 <= b < 127)
        print(f"      {i:4d}  {n:2d}/32  {'#' * n:<32} {chunk.hex(' ')[:48]}")

    uid_like = [i for i in range(len(payload) - 3)
                if payload[i] == 0 and payload[i + 1] == 0 and payload[i + 2] == 0
                and 0 < payload[i + 3] < 0x20]
    print(f"\n[2] 形如 `00 00 00 xx`（uid 大端）的位置: "
          f"{uid_like[:20] if uid_like else '无'}"
          f"{' …' if len(uid_like) > 20 else ''}  共 {len(uid_like)} 个")

    wins = [payload[o:o + 256] for o in range(0, max(1, len(payload) - 255))]
    good = []
    for o, w in enumerate(wins):
        blk = pow(int.from_bytes(w, "big"), nums.e, nums.n).to_bytes(256, "big")
        if blk[0] == 0x00 and blk[1] == 0x01:
            try:
                if blk.index(b"\x00", 2) - 2 >= 8:
                    good.append(o)
            except ValueError:
                pass
    baseline = len(wins) / 65536
    print(f"\n[3] 256 B 滑窗数: {len(wins)}")
    print(f"    模幂后落在 type-1 上的窗口: {good if good else '无'}")
    print(f"    随机基线期望: {baseline:.4f} 个")
    if good:
        print(f"    ⇒ **找到候选**（{len(good)} 个，高于基线）—— 需要进一步看填充与语义")
    else:
        print(f"    ⇒ **没有任何窗口落在 type-1 上**（0 vs 基线 {baseline:.4f}）")
        print(f"       ⇒ 说'扫不到'是对的；**不能**由此断言'首帧里没有令牌'，")
        print(f"         只能说'它不是容器这把公钥体系里的裸令牌'")
    return 0


if __name__ == "__main__":
    sys.exit(main())
