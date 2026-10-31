#!/usr/bin/env python3
r"""client-begincommand-scan.py —— 枚举客户端 `BeginCommand`（`0x8B2650`）的全部发射点及其命令号。

背景（项目既有方法）：`party-protocol-recon.md` 记「全二进制 234 个 `0x8B2650` 调用点」，
每个点前面 `push <id>` 就是命令号。本文件把这 234 个点连同**最近的 push 立即数**列出来，
用于定位 `CMD 1` 的**出站构造处**（E2 的目标）。

判据与限制（必须写在结论里）：
  * 只认"与 `call 0x8B2650` 之间没有被别的 `call`/`ret`/`jmp` 打断"的最近一条 `push imm`；
  * 因此它是**候选**，不是证明；要确认必须读到写载荷的代码。
  * `push 1` 也可能是别的用途（例如 `BeginCommand(1)` 之后还没写字段），所以下文同时打印
    调用点前后各若干条指令，供人工判定。
"""
from __future__ import annotations

import argparse
import struct
from collections import Counter
from pathlib import Path

import capstone

BEGIN_CMD = 0x8B2650


def pe_sections(path: Path):
    """极简 PE32 解析：返回 [(name, va, vsize, raw, rawsize)]。"""
    d = path.read_bytes()
    assert d[:2] == b"MZ", "not PE"
    e_lfanew = struct.unpack_from("<I", d, 0x3C)[0]
    assert d[e_lfanew:e_lfanew + 4] == b"PE\0\0", "no PE sig"
    coff = e_lfanew + 4
    nsec, = struct.unpack_from("<H", d, coff + 2)
    opt_size, = struct.unpack_from("<H", d, coff + 16)
    opt = coff + 20
    magic, = struct.unpack_from("<H", d, opt)
    pe32plus = (magic == 0x20B)
    image_base = struct.unpack_from("<Q" if pe32plus else "<I", d, opt + 24)[0]
    sec_off = opt + opt_size
    out = []
    for i in range(nsec):
        o = sec_off + i * 40
        name = d[o:o + 8].rstrip(b"\x00").decode("ascii", "replace")
        vsize, va, rawsize, raw = struct.unpack_from("<IIII", d, o + 8)
        out.append((name, image_base + va, vsize, raw, rawsize))
    return d, image_base, out


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("exe")
    ap.add_argument("--want", type=int, default=1, help="要筛的命令号（默认 1）")
    ap.add_argument("--context", type=int, default=8)
    ap.add_argument("--out", default=None)
    args = ap.parse_args()

    d, image_base, secs = pe_sections(Path(args.exe))
    print(f"# image_base={image_base:#x} sections={[s[0] for s in secs]}")
    md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_32)
    lines = []
    counts = Counter()
    total = 0

    for name, va, vsize, raw, rawsize in secs:
        if not (0x60000020 & 0x20000000) and name not in (".text",):
            # 只扫 .text（代码）
            if name != ".text":
                continue
        code = d[raw:raw + rawsize]
        insns = list(md.disasm(code, va))
        for i, ins in enumerate(insns):
            if ins.mnemonic != "call":
                continue
            try:
                tgt = int(ins.op_str, 16)
            except ValueError:
                continue
            if tgt != BEGIN_CMD:
                continue
            total += 1
            # 往回找最近的 push imm（不被 call/ret/jmp 打断）
            cmd = None
            for j in range(i - 1, max(i - 1 - 12, -1), -1):
                p = insns[j]
                if p.mnemonic in ("call", "ret", "jmp", "leave"):
                    break
                if p.mnemonic == "push":
                    try:
                        cmd = int(p.op_str, 16)
                    except ValueError:
                        cmd = None
                    break
            counts[cmd] += 1
            if cmd == args.want or (args.want < 0):
                lines.append(f"\n=== BeginCommand @ {ins.address:#010x}  cmd={cmd} ===")
                for k in range(max(i - args.context, 0), min(i + 3, len(insns))):
                    q = insns[k]
                    mark = " <<<" if k == i else ""
                    lines.append(f"  {q.address:#010x}  {q.mnemonic:<7} {q.op_str}{mark}")

    print(f"# total BeginCommand call sites = {total}")
    print("# 命令号直方图（None = 最近一条 push 不是立即数/找不到）:")
    for cmd, n in sorted(counts.items(), key=lambda kv: (-kv[1], kv[0] if kv[0] is not None else -1)):
        print(f"#   cmd={cmd}: {n}")
    text = "\n".join(lines)
    print(text[:20000])
    if args.out:
        Path(args.out).write_text(text, encoding="utf-8")
        print(f"\n# 明细已写入 {args.out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
