#!/usr/bin/env python3
r"""scan-restrict-args.py —— 在 `df_game_r.bin` 的可执行节里，扫描"调用
`Singleton<ServiceRestrictManager>::Get()` 之后紧接着压入的 category/code 立即数"。

为什么需要：`isRestricted` 在 LOGIN 之外的**其它**路径上也被调用（`Get()` 有 25 个调用点）。
我先前只看了 LOGIN 那一处就下了结论，而运行期数据与它矛盾 ⇒ 必须把**所有**调用点的实参列出来，
看有没有哪个点是"真正的登录准入检查"，以及我是否把 `category` 与 `code` 的顺序读反了。

判据：`Get()` 的返回地址在 `eax`（cdecl 返回值），紧接着的 `mov [esp+4..], imm` 就是实参。
本脚本不依赖 IDA：按指令边界线性反汇编，只保留 `call 0x81625e6` 之后 12 条以内的
`mov dword ptr [esp+N], imm` / `mov dword ptr [esp+N], reg`。
"""
from __future__ import annotations

import argparse
import struct
from collections import deque
from pathlib import Path

import capstone

GET_VA = 0x081625E6


def sections(path: Path):
    d = path.read_bytes()
    e_shoff = struct.unpack_from("<I", d, 0x20)[0]
    e_shentsize = struct.unpack_from("<H", d, 0x2E)[0]
    e_shnum = struct.unpack_from("<H", d, 0x30)[0]
    e_shstrndx = struct.unpack_from("<H", d, 0x32)[0]
    raw = [struct.unpack_from("<IIIIII", d, e_shoff + i * e_shentsize) for i in range(e_shnum)]
    shstr = raw[e_shstrndx]
    out = {}
    for name, typ, flags, addr, offset, size in raw:
        end = d.find(b"\x00", shstr[4] + name)
        nm = d[shstr[4] + name:end].decode("utf-8", "replace")
        out[nm] = (addr, offset, size)
    return d, out


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("bin")
    ap.add_argument("--window", type=int, default=14)
    args = ap.parse_args()
    d, secs = sections(Path(args.bin))
    addr, off, size = secs[".text"]
    code = d[off:off + size]
    md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_32)

    insns = list(md.disasm(code, addr))
    print(f"# instructions={len(insns)}")
    hits = 0
    for i, ins in enumerate(insns):
        if ins.mnemonic != "call":
            continue
        if ins.op_str.strip() != hex(GET_VA):
            continue
        hits += 1
        seq = []
        for j in range(i + 1, min(i + 1 + args.window, len(insns))):
            nx = insns[j]
            if nx.mnemonic == "mov" and "esp" in nx.op_str:
                seq.append(f"{nx.address:#010x} {nx.mnemonic} {nx.op_str}")
            if nx.mnemonic == "call" and "esp" not in nx.op_str:
                # 下一个 call：说明这一段结束了（isRestricted / 其它）
                seq.append(f"          -> next call {nx.op_str}")
                break
        print(f"\n=== Get() called at {ins.address:#010x} ===")
        for s in seq:
            print("   " + s)
    print(f"\n# total Get() call sites = {hits}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
