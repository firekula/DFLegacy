#!/usr/bin/env python3
r"""e2-version-consts.py —— 在客户端里找"版本/DataManager 常量"的**读入点**（文件 vs 立即数）。

动机：E2 里已确认 `20080418`（登录帧的 `DataManager` 字段）在客户端**只被 `cmp` 一次**
⇒ 它不是代码里的写入常量。那么它极可能来自**本地文件**（ini/pvf）或另一处全局。
本脚本：
  1. 扫 `.exe` 与同目录的配置文件里，是否存在这些常量的**文本或二进制**形态；
  2. 列出客户端目录下所有 ini/cfg 的内容（登录相关字段常在 `DFighter.ini`/`Config.ini`）。
"""
from __future__ import annotations

import argparse
import re
from pathlib import Path

DECS = [20080418, 28000201, 30000401]
HEXS = [0x01326722, 0x01AB3FC9, 0x01C9C511]


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--client-dir", default=r"G:\Game\DNF_ACT1\DF")
    ap.add_argument("--exe", default=r"G:\Game\DNF_ACT1\DF\DNF.exe")
    args = ap.parse_args()

    d = Path(args.exe).read_bytes()
    print(f"# exe={args.exe} size={len(d)}")
    for dec, hx in zip(DECS, HEXS):
        le = hx.to_bytes(4, "little")
        ascii_forms = [str(dec).encode(), (b"%08d" % dec), ("%x" % hx).encode(), ("0x%x" % hx).encode()]
        hits = {}
        for name, pat in (("le32", le),) + tuple((f"txt:{p[:12]!r}", p) for p in ascii_forms):
            pos = []
            st = 0
            while True:
                k = d.find(pat, st)
                if k < 0:
                    break
                pos.append(k)
                st = k + 1
                if len(pos) > 6:
                    break
            if pos:
                hits[name] = pos
        print(f"\n## 常量 {dec} ({hx:#x})")
        if not hits:
            print("   二进制里没有（任何形态）")
        for name, pos in hits.items():
            print(f"   {name}: " + ", ".join(f"{p:#x}" for p in pos))

    print("\n## 客户端目录的 ini/cfg/lst 文本")
    cd = Path(args.client_dir)
    for p in sorted(cd.glob("*")):
        if p.suffix.lower() in (".ini", ".cfg", ".lst", ".xml") and p.is_file():
            try:
                txt = p.read_text(encoding="utf-8", errors="replace")
            except Exception as e:
                print(f"--- {p.name}: 读取失败 {e}")
                continue
            keys = [l for l in txt.splitlines()
                    if re.search(r"version|ver|patch|date|data", l, re.I)]
            print(f"\n--- {p.name} ({len(txt)} chars) 相关行 {len(keys)}")
            for l in keys[:20]:
                print(f"    {l.strip()[:120]}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
