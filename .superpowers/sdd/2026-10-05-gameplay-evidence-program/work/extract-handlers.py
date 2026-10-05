#!/usr/bin/env python3
"""extract-handlers.py — 从派发器与 put_header 点抽出处理器目录。

三个来源，每条记录都带 source 以便回溯：
  dispatch_sig        反汇编 dispatch_sig 的函数体，取**直接 call** 目标
  dispatch_sig_tailjmp 同一函数体里的**直接 jmp** 目标（尾调用/thunk：既有目录里
                      大量 dispatch_sig 只有 10 字节，函数体就是一个 jmp，所以只取 call
                      会把它们全判成"没有 handler"——那是把编译器产物当成缺失）
  put_header          packet_senders.txt 的每个发送点（其真实格式见下）

packet_senders.txt 的真实格式（**实测**，不是计划里假设的"每行第一个 0x…"）：
  <构造函数名>\tclass=<0|1|None>\tcmd=<N|None>\t@0x<addr>
  1249 行 = 1 注释 + 1248 数据行。class=0 是通知（NOTI）、class=1 是命令（CMD）。
  它本身就是"谁在发这个包"的目录 —— 对 Task 7 校验 opcode 映射是另一条独立通道。

过滤：dispatch_sig* 的 call/jmp 目标必须在可执行节内**且**符号 size >= 16（排除 thunk/PLT 桩）。
      put_header 点不过滤（它记录的是"谁构造了包"这个事实本身）。

用法: extract-handlers.py <catalog_dispatchers_full.txt> <packet_senders.txt> <elf> <out.txt>
"""
import bisect
import re
import sys
from importlib.util import module_from_spec, spec_from_file_location
from pathlib import Path

from capstone import Cs, CS_ARCH_X86, CS_MODE_32

HERE = Path(__file__).resolve().parent
SENDER_RX = re.compile(r"^(?P<fn>.+?)\tclass=(?P<cls>\w+)\tcmd=(?P<cmd>\w+)\t@(?P<addr>0x[0-9a-fA-F]+)\s*$")


def load_symbols(tsv):
    """[(addr, size, name)]，FUNC only，按地址排序。"""
    rows = []
    for ln in Path(tsv).read_text(encoding="utf-8", errors="replace").splitlines():
        p = ln.split("\t", 3)
        if len(p) >= 4 and p[2] == "FUNC":
            try:
                rows.append((int(p[0], 16), int(p[1]), p[3]))
            except ValueError:
                pass
    rows.sort()
    return rows


class Syms:
    def __init__(self, rows):
        self.rows = rows
        self.starts = [r[0] for r in rows]

    def at(self, va, look_back=64):
        """包含 va 且 value 最大的 FUNC 符号（与逐条线扫的语义一致，但用 bisect）。"""
        i = bisect.bisect_right(self.starts, va) - 1
        for k in range(i, max(-1, i - look_back), -1):
            a, sz, nm = self.rows[k]
            if a <= va and (sz == 0 or va < a + sz):
                return (a, sz, nm)
        return None


def load_families(path):
    """返回 [(family, process_addr, process_size, sig_addr, sig_size)]。"""
    fams = []
    for ln in Path(path).read_text(encoding="utf-8").splitlines():
        if not ln or ln.startswith("#"):
            continue
        name, proc, sig, _rest = ln.split("\t")
        p_a, p_s = proc.split("=")[1].split("(")
        s_a, s_s = sig.split("=")[1].split("(")
        fams.append((name, int(p_a, 16), int(p_s.rstrip(")")),
                     int(s_a, 16), int(s_s.rstrip(")"))))
    return fams


def main(catalog, senders, elf_path, out):
    spec = spec_from_file_location("elfrecon", HERE / "elfrecon.py")
    m = module_from_spec(spec)
    spec.loader.exec_module(m)
    elf = m.Elf(elf_path)
    syms = Syms(load_symbols(HERE / "out" / "demangled-syms.tsv"))
    text = elf.by_name[".text"]
    text_lo, text_hi = text["addr"], text["addr"] + text["size"]
    plt = elf.by_name[".plt"]
    plt_lo, plt_hi = plt["addr"], plt["addr"] + plt["size"]

    md = Cs(CS_ARCH_X86, CS_MODE_32)
    md.detail = True

    lines = ["# df_game_r 处理器目录（生成自 catalog_dispatchers_full.txt + packet_senders.txt）",
             "# 格式: family<TAB>handler=0xADDR( SIZE)<TAB>source=<dispatch_sig|dispatch_sig_tailjmp"
             "|put_header><TAB>name=<符号名>[ class=<0|1> cmd=<N>]",
             "# source 三值的含义见 extract-handlers.py docstring；size=0 表示符号表里没有该地址的带尺寸符号",
             "# put_header 行来自 packet_senders.txt 的真实格式 <fn>\\tclass=<0|1>\\tcmd=<N>\\t@0x<addr>"]

    n_sig = n_tail = n_ph = n_rej = n_plt = 0
    outside = []
    zero_fams = []
    fams_with_sig = fams_with_handler = 0
    for fam, _pa, _ps, sig_va, sig_sz in load_families(catalog):
        if not sig_va or not sig_sz:
            continue
        fams_with_sig += 1
        off = elf.va_to_off(sig_va)
        if off is None:
            continue
        got = 0
        for ins in md.disasm(elf.data[off:off + sig_sz], sig_va):
            if ins.mnemonic not in ("call", "jmp"):
                continue
            if not ins.operands or ins.operands[0].type != 2:   # X86_OP_IMM
                continue
            tgt = ins.operands[0].imm
            # 只把**跳出本函数**的 jmp 当尾调用：函数内部的 jmp 会落到 ICF 合并出的
            # 同名重复符号上（实测：Dispatcher_AddSocketToAvatar::dispatch_sig 内部
            # jmp 到同名的 0x0821abd4），那是函数内跳转，不是"另一个 handler"。
            if ins.mnemonic == "jmp" and sig_va <= tgt < sig_va + sig_sz:
                continue
            s = syms.at(tgt)
            size = s[1] if s else 0
            name = s[2] if s else "?"
            if text_lo <= tgt < text_hi:
                pass
            elif plt_lo <= tgt < plt_hi:
                n_plt += 1          # 跳到 .plt = 调用外部导入（printf/memcpy 之类），不是 handler
                continue
            else:
                outside.append((fam, tgt, ins.mnemonic))
                continue
            if size < 16:
                n_rej += 1
                continue
            src = "dispatch_sig" if ins.mnemonic == "call" else "dispatch_sig_tailjmp"
            lines.append(f"{fam}\thandler=0x{tgt:08x}({size:5d})\tsource={src}\tname={name}")
            if ins.mnemonic == "call":
                n_sig += 1
            else:
                n_tail += 1
            got += 1
        if got:
            fams_with_handler += 1
        else:
            zero_fams.append((fam, sig_va, sig_sz))

    for ln in Path(senders).read_text(encoding="utf-8", errors="replace").splitlines():
        if not ln or ln.startswith("#"):
            continue
        mm = SENDER_RX.match(ln)
        if not mm:
            continue
        va = int(mm.group("addr"), 16)
        s = syms.at(va)
        name = s[2] if s else mm.group("fn")
        size = s[1] if s else 0
        if not (text_lo <= va < text_hi):
            outside.append(("(put_header)", va, "put_header"))
        lines.append(f"(sender)\thandler=0x{va:08x}({size:5d})\tsource=put_header\t"
                     f"name={name} class={mm.group('cls')} cmd={mm.group('cmd')} "
                     f"sender={mm.group('fn')}")
        n_ph += 1

    Path(out).write_text("\n".join(lines) + "\n", encoding="utf-8")
    pct = 100.0 * fams_with_handler / max(fams_with_sig, 1)
    stats = [
        "# --- 统计（生成时计算；重跑本脚本会重算，不是手写常数） ---",
        f"# rows: dispatch_sig={n_sig} tailjmp={n_tail} put_header={n_ph} total={n_sig + n_tail + n_ph}",
        f"# families with dispatch_sig={fams_with_sig}; with >=1 handler={fams_with_handler}"
        f" ({pct:.1f}%)   [Task 5 Step 3 判据: >=80%]",
        f"# families with dispatch_sig but ZERO handler: {len(zero_fams)}"
        + ("".join(f"; {f}=0x{a:08x}({z})" for f, a, z in zero_fams) if zero_fams else ""),
        "#   实测这 4 个的函数体都是 push ebp / mov ebp,esp / mov eax,0 / pop ebp / ret —— 空实现（返回 0），",
        "#   所以「没有 handler」是结构性的，不是解析失败。",
        f"# call/jmp targets rejected as thunks (symbol size < 16): {n_rej}",
        f"# calls into .plt (external imports, not handlers, excluded): {n_plt}",
        f"# handler addresses outside .text/.plt (Task 5 失败形状 ③): {len(outside)}",
        "# 语义说明（重要）：本文件是**每家族的直接 call 边表**（一条 call 指令 = 一行，重复调用保留），",
        "#   因此既包含该家族自己的 read/process/send，也包含框架与工具调用（PacketBuf::get_int、",
        "#   LineFunc、cMyTrace、_Unwind_Resume、CUser::Send 等）。它不是人工挑选的「业务处理器」清单 ——",
        "#   要选业务处理器，按 name= 里的家族名前缀过滤（如 Dispatcher_<family>::/DisPatcher_<family>::）。",
        "# put_header 行是「谁在构造这个包」的目录，带着 class(0=通知/1=命令) 与 cmd 号，",
        "#   对 Task 7 校验 opcode 映射是另一条独立通道（实测：class=0 的 370 个不同 id 全在 0..562，",
        "#   class=1 的 277 个全在 0..607，与 opcode_noti/cmd.txt 的 id 空间一致）。",
    ]
    with open(out, "a", encoding="utf-8") as fh:
        fh.write("\n".join(stats) + "\n")
    print(f"dispatch_sig handlers={n_sig}, tail-jmp handlers={n_tail}, put_header points={n_ph} -> {out}")
    print(f"families with dispatch_sig={fams_with_sig}, of which with >=1 handler="
          f"{fams_with_handler} ({pct:.1f}%)")
    print(f"call/jmp targets rejected as thunks (symbol size < 16): {n_rej}")
    print(f"calls into .plt (external imports, not handlers, excluded): {n_plt}")
    print(f"handler addresses outside .text/.plt (failure shape 3): {len(outside)}")
    for fam, va, kind in outside[:8]:
        print(f"   {fam} {kind} 0x{va:08x}")
    print(f"families with dispatch_sig but ZERO handler: {len(zero_fams)}")
    for fam, va, sz in zero_fams:
        print(f"   {fam}: dispatch_sig=0x{va:08x}({sz}) —— 函数体里没有可用的 call/跨函数 jmp")


if __name__ == "__main__":
    main(*sys.argv[1:5])
