#!/usr/bin/env python3
r"""e2-find-login-sender.py —— E2：在客户端全量反汇编里找 **LOGIN（游戏面 msg_no=1）** 的出站构造处。

## 已知的"指纹"（来自本轮已解出的 400 B 明文，`work/out/login-plain-dump.json`）

游戏面 C→S 首帧 = 13 B 头（`01 01 00 9d 01 00 00 …`）+ 400 B 加密载荷。
明文里有三个可在客户端二进制里当**锚点**的东西：

| 锚点 | 值 | 说明 |
|---|---|---|
| `patch` | `0` | 太常见，无用 |
| `token_length` | **344** = `0x158` | 登录帧里该字段的**上限**很可能以立即数出现 |
| `string1` 上限 | **24** = `0x18` | 静态消费端读出（`get_str(..., 0x18, ...)`) |
| `string2` 上限 | **132** = `0x84` | 同上 |
| `flag` 校验 | `< 3` | 同上 |
| `mouse_length` | **8** | |
| `DataManager` 常量 | `20080418` = `0x01326722` | **BCD**（字节 `22 67 32 01`）；客户端把它写进帧尾 |
| `version` | `28000201` / `30000401` | 部署相关 |

最省力的入口是 **`0x01326722`（DataManager 值）**：它在登录帧里恒为 `22 67 32 01`，
而在同账号两次登录里都是这个值 ⇒ 客户端**很可能以立即数写它**。
本脚本按"立即数/内存位移/字符串"三类锚点全扫，并打印命中点的上下文。

## 纪律

* 命中"立即数"只是**候选**：编译器也可能把它放在数据段。因此同时扫 `.rdata` 式的连续字节。
* 本脚本只读 `insns.txt`（既有产物）与 `DNF.exe`（只读），不运行客户端、不改任何文件。
"""
from __future__ import annotations

import argparse
import re
from pathlib import Path

NUM = re.compile(r"^(0x[0-9a-f]+|-?\d+)$", re.I)


def load(path: Path):
    ins = []
    with path.open(encoding="utf-8", errors="replace") as f:
        for line in f:
            p = line.rstrip("\r\n").split("\t")
            if len(p) == 3:
                ins.append((int(p[0], 16), p[1], p[2]))
    return ins


def show(ins, i, before, after, out):
    for k in range(max(0, i - before), min(len(ins), i + after)):
        a, m, o = ins[k]
        mark = "   <<<" if k == i else ""
        out.append(f"  {a:#010x}  {m:<8} {o}{mark}")


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("insns")
    ap.add_argument("--out", default="out/e2-login-anchors.txt")
    ap.add_argument("--csv", default="G:/Game/DNF_ACT1/DF/DNF.exe")
    args = ap.parse_args()

    ins = load(Path(args.insns))
    print(f"# instructions={len(ins)}")
    out = []

    anchors = {
        "DataManager=20080418 (0x1326722)": 0x01326722,
        "token_length=344 (0x158)": 0x158,
        "string1_cap=24 (0x18)": 0x18,
        "string2_cap=132 (0x84)": 0x84,
    }
    for label, val in anchors.items():
        pat = re.compile(r"(^|,\s*)(0x%x|%d)$" % (val, val), re.I)
        hits = [(a, m, o, i) for i, (a, m, o) in enumerate(ins) if pat.search(o)]
        print(f"\n## 锚点 {label}: {len(hits)} 命中")
        out.append(f"\n########## 锚点 {label} ({len(hits)} 命中) ##########")
        for a, m, o, i in hits[:40]:
            out.append(f"\n-- {a:#010x}  {m} {o}")
            show(ins, i, 6, 6, out)

    # 扫 exe 里的连续字节 22 67 32 01（DataManager 的 BCD 存储形态）
    d = Path(args.csv).read_bytes()
    needle = bytes.fromhex("22673201")
    pos = []
    start = 0
    while True:
        k = d.find(needle, start)
        if k < 0:
            break
        pos.append(k)
        start = k + 1
    print(f"\n## `22 67 32 01` 在 DNF.exe 里: {len(pos)} 处")
    out.append(f"\n########## `22 67 32 01` 字节串 ({len(pos)} 处) ##########")
    for k in pos[:40]:
        ctx = d[max(0, k - 24):k + 24]
        out.append(f"  file+{k:#08x}  ctx={ctx.hex()}")

    Path(args.out).write_text("\n".join(out), encoding="utf-8")
    print(f"# 明细 -> {args.out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
