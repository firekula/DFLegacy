#!/usr/bin/env python3
"""map-opcodes.py — 把 CMD/NOTI 号映射到派发器家族。

=========================== 机制（Task 4 Step 4 定出，含地址依据） ===========================
1) 查表函数：`PacketDispatcher::get_dispatcher(int)` @0x085948e2（63 B）
     cmp dword [ebp+0xc], 0x25d ; jle ok ; return 0     -> id > 605 一律无派发器
     cmp dword [ebp+0xc], 0     ; jns ok ; return 0     -> id < 0   一律无派发器
     mov edx, [ebp+0xc] ; mov eax, [ebp+8] ; mov eax, [eax + edx*4]   -> 表[id]
   ⇒ 表 = `PacketDispatcher` 单例的成员数组 `m_table[id]`（4 字节指针，基址 = 对象 + 0），
     长度 606（id 0..605）。`PacketDispatcher::getDispatcher(int)` @0x08231f2e（26 B）只是它的 thunk。
2) 表的填充：`PacketDispatcher::PacketDispatcher()` @0x08590a2e（15890 B）
     先 `mov [eax + edx*4], 0` 清空 id 0..0x25d，再 425 个注册块，每块形如
       mov dword [esp], 4 ; call operator_new(0x08724450) ; call <Ctor>
       mov eax, [ebp+8]   ; mov dword [eax + IMM], edx      （IMM/4 = opcode id）
     （IMM 的形式有 `[eax]` / `[eax + 4]` / `[eax + 8]` / `[eax + 0xNNN]` 四种，都要认；
       漏掉前三种会漏掉 CMD 0/1/2，即 CHECK_CONNECTION / LOGIN / SET_UDP_IP_PORT。）
     **`<Ctor>` 的类名就是派发器家族名**（如 `Dispatcher_BingoQuiz::Dispatcher_BingoQuiz()`）。
3) 因此 CMD 的 id -> 家族来自**编译期注册表**（构造函数的注册块），不是 .rodata 的名字表。
   .rodata 的名字数组（`g_szCmdPacketName` @0x0936d920）只被
   `GetCommandPacketName(ENUM_CMDPACKET)` @0x082a21c5 用于日志取名（唯一引用点，实测）。
4) **NOTI 没有派发器表**：`get_dispatcher` 只按 id 取表（**没有 class 参数**），
   一个 id 索引的表不可能同时服务两套 id 空间；而 `ENUM_NOTIPACKET` 的消费者全是发送/取名侧
   （`CUser::SendNotiPacket(CUser::eSendTarget, ENUM_NOTIPACKET, int)`、
    `ARAD::DISPATCHER::make_noti_packetheader_jpn(PacketGuard&, ENUM_NOTIPACKET, bool)`、
    `GameWorld::send_chat_msg(..., ENUM_NOTIPACKET)`、`GetNotifyPacketName(ENUM_NOTIPACKET)`），
   没有任何 `dispatch*` 函数以 ENUM_NOTIPACKET 为参数。
   ⇒ NOTI 563 条一律 `no-dispatcher`（**出站专用**），证据同上。

=========================== 与计划骨架的差异（都写在这里） ===========================
A. 输入表第三列是 **.rodata 文件偏移**，不是 VA（实测：0xaa39d8 处正是
   b'ENUM_CMDPACKET_CHECK_CONNECTION'，而 .rodata 的 VA 区间是从 0x08ae5840 起，
   二者差 = .rodata 的 offset）。所以本脚本先做 off -> VA 转换再写进 TSV 的 rodata_va 列。
   不转换就会像计划 Step 3 那样对 0xaa39d8 扫 xref 得到 0 命中，被误判成
   “.rodata 地址不被直接引用”而白跑一趟 IDA。
B. 计划骨架的 classify() 只有 (kind,id,name,rodata_va,families) 五个入参，无法表达本机制
   （需要反汇编 ELF）。故新增可选 `--elf`（默认见 DEFAULT_ELF），4 个位置参数不变。
C. 机制自检：脚本会断言 get_dispatcher 里存在 `cmp ..., 0x25d`、构造函数里有清空循环与
   >=400 个注册块 —— 机制前提破了就**报错退出**，不产出看似正常的表。

=========================== 状态定义 ===========================
mapped        该 id 在注册表里，指向一个具名派发器家族
no-dispatcher 结构上可达但没有派发器：NOTI 全类（出站专用）；CMD 里 id<=605 但槽位为 NULL
unused        id 超出查表函数的上界 0x25d=605（END 等哨兵）——**没有**派发器可言
inter-server  服务间派发器（Arad_Dispatcher_*_Impl 的 db_dispatch/inter_dispatch 那一类）
unknown       判不出来。**绝不猜**：判不出就留 unknown 并计数。

用法: map-opcodes.py <opcode_cmd.txt> <opcode_noti.txt> <catalog_dispatchers_full.txt> <out.tsv>
                     [--elf <df_game_r.bin>]
"""
import argparse
import re
import struct
import sys
from importlib.util import module_from_spec, spec_from_file_location
from pathlib import Path

HERE = Path(__file__).resolve().parent
DEFAULT_ELF = (r"G:\Game\DNF_ACT1\DFLegacy\.superpowers\sdd\2026-09-29-dungeon-party"
               r"\work\official\df_game_r.bin")

# 机制锚点（Task 4 Step 4 定出；改二进制必须重新确认）
GET_DISPATCHER = 0x085948E2
GET_DISPATCHER_SIZE = 63
CTOR = 0x08590A2E
CTOR_SIZE = 15890
OPERATOR_NEW = 0x08724450
TABLE_MAX_ID = 0x25D          # 605

STATUSES = {"mapped", "no-dispatcher", "unused", "inter-server", "unknown"}

FAMILY_RX = [
    re.compile(r"(?:^|::)(?:DisPatcher|Dispatcher|DIspatcher|Dispatcer|Dispather|Dispatchr)_"
               r"([A-Za-z0-9_]+?)(?:::|\()"),
    re.compile(r"ARAD::Arad_Dispatcher_([A-Za-z0-9_]+?)_Impl(?:::|\()"),
    # 兜底（实测存在，Task 4 观测到 3 例，报告里单列）：
    #   无下划线         DispatcherResetRandomOption::...        -> ResetRandomOption
    #   前缀拼错         Dispatcer_polic_one_to_one_chat_disable -> polic_one_to_one_chat_disable
    #   前缀字母换位     Dispather_RegenerationRandomOption      -> RegenerationRandomOption
    re.compile(r"(?:^|::)(?:Dispatcher|DisPatcher|DIspatcher|Dispatcer|Dispather)"
               r"([A-Z][A-Za-z0-9_]*)(?:::|\()"),
]
FALLBACK_RX = FAMILY_RX[2]


def load_elf(path):
    spec = spec_from_file_location("elfrecon", HERE / "elfrecon.py")
    m = module_from_spec(spec)
    spec.loader.exec_module(m)
    return m, m.Elf(path)


def family_of_class(ctor_name):
    """'Dispatcher_BingoQuiz::Dispatcher_BingoQuiz()' -> ('BingoQuiz', False)

    返回 (family, 是否走了兜底 pattern)。兜底命中要单列进报告，便于评审门抽查。
    """
    for i, rx in enumerate(FAMILY_RX):
        if i == 2:      # 兜底放到最后单独判断，避免抢在前两个前面
            continue
        m = rx.search(ctor_name)
        if m:
            return m.group(1), False
    m = FALLBACK_RX.search(ctor_name)
    if m:
        return m.group(1), True
    return None, False


def symbol_map(tsv_path):
    """addr -> (size, name) for FUNC symbols, from the committed demangled dump."""
    import bisect
    rows = []
    for ln in Path(tsv_path).read_text(encoding="utf-8", errors="replace").splitlines():
        p = ln.split("\t", 3)
        if len(p) >= 4 and p[2] == "FUNC":
            try:
                rows.append((int(p[0], 16), int(p[1]), p[3]))
            except ValueError:
                pass
    rows.sort()
    starts = [r[0] for r in rows]

    def nm(va):
        i = bisect.bisect_right(starts, va) - 1
        if i >= 0 and rows[i][0] <= va < rows[i][0] + rows[i][1]:
            return rows[i][2]
        return f"sub_{va:x}"
    return nm


STORE_RX = re.compile(r"dword ptr \[eax(?: \+ (\S+))?\], edx$")


def parse_registrations(m, elf, nm):
    """返回 {id: (ctor_addr, ctor_name, family)}，并做机制自检。

    以**存储指令**为锚点，向前回看最近的 `call <Ctor>` 与其前的 `call operator_new`。
    早先以 operator_new 为锚点、向后只找 8 条指令的写法会漏掉两类块：
      - `mov [esp], 8`（对象 8 字节，不是 4）
      - 存储指令落在异常处理 `jmp` 之后（如 Dispatcher_MonstercardBind @id 416、
        Dispatcher_UseVendingMachine @id 215）—— 漏了就会让这两个家族看起来“未注册”。
    """
    ins = list(m.disasm(elf, CTOR, CTOR_SIZE))
    # --- 自检 1: 清空循环的上界必须是 0x25d
    assert any(i.mnemonic == "cmp" and "0x25d" in i.op_str for i in ins), \
        "PacketDispatcher 构造函数里没有 cmp ..., 0x25d：机制前提变了"
    # --- 自检 2: 查表函数里必须有 0x25d 上界比较
    gd = list(m.disasm(elf, GET_DISPATCHER, GET_DISPATCHER_SIZE))
    assert any(i.mnemonic == "cmp" and "0x25d" in i.op_str for i in gd), \
        "get_dispatcher 里没有 0x25d 上界：机制前提变了"

    regs = {}
    misses = []
    for idx, x in enumerate(ins):
        if x.mnemonic != "mov":
            continue
        mm = STORE_RX.search(x.op_str)
        if not mm:
            continue
        idv = 0 if mm.group(1) is None else int(mm.group(1), 0) // 4
        if not (0 <= idv <= TABLE_MAX_ID):
            continue
        # 回看最近的 `call operator_new`，再从它往后取第一个 call = 该块的构造函数。
        # 不能取“最近的 call”：8 字节对象块的异常清理路径里有 `call operator delete`，
        # 取最近 call 会把 delete 当构造函数。
        new_at = None
        for back in range(idx - 1, max(0, idx - 60) - 1, -1):
            y = ins[back]
            if y.mnemonic == "call" and y.op_str == hex(OPERATOR_NEW):
                new_at = back
                break
        ctor = None
        if new_at is not None:
            for fwd in range(new_at + 1, min(new_at + 12, len(ins))):
                if ins[fwd].mnemonic == "call" and ins[fwd].op_str.startswith("0x"):
                    ctor = ins[fwd].op_str
                    break
        if ctor is None:
            misses.append(f"0x{x.address:08x} store {x.op_str} (no operator_new/ctor behind)")
            continue
        cva = int(ctor, 16)
        cname = nm(cva)
        fam, via_fallback = family_of_class(cname)
        regs[idv] = (cva, cname, fam, via_fallback)
    assert len(regs) >= 400, f"注册块只有 {len(regs)} 个：机制前提变了"
    return regs, misses


def load_table(path, elf):
    """返回 [(id, name, va)]，把第三列的 .rodata 文件偏移转成 VA。"""
    rod = elf.by_name[".rodata"]
    rows, bad = [], []
    for ln in Path(path).read_text(encoding="utf-8").splitlines():
        if not ln or ln.startswith("#"):
            continue
        parts = ln.split("\t")
        oid, name, raw = int(parts[0]), parts[1], int(parts[2], 16)
        va = rod["addr"] + (raw - rod["offset"])
        if not (rod["addr"] <= va < rod["addr"] + rod["size"]):
            bad.append((oid, name, raw))
            va = 0
        rows.append((oid, name, va))
    return rows, bad, rod


def str_at(elf, va):
    off = elf.va_to_off(va)
    if off is None:
        return None
    return elf.data[off:off + 96].split(b"\0")[0].decode("latin1", "replace")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("cmd_txt")
    ap.add_argument("noti_txt")
    ap.add_argument("catalog")
    ap.add_argument("out_tsv")
    ap.add_argument("--elf", default=DEFAULT_ELF)
    ap.add_argument("--syms", default=str(HERE / "out" / "demangled-syms.tsv"))
    a = ap.parse_args()

    m, elf = load_elf(a.elf)
    nm = symbol_map(a.syms)
    regs, misses = parse_registrations(m, elf, nm)

    cmd_rows, cmd_bad, rod = load_table(a.cmd_txt, elf)
    noti_rows, noti_bad, _ = load_table(a.noti_txt, elf)

    catalog = set()
    for ln in Path(a.catalog).read_text(encoding="utf-8").splitlines():
        if ln and not ln.startswith("#"):
            catalog.add(ln.split("\t", 1)[0])
    reg_fams = {v[2] for v in regs.values() if v[2]}

    print(f"[mech] get_dispatcher @0x{GET_DISPATCHER:08x} bound id<=0x{TABLE_MAX_ID:x} ({TABLE_MAX_ID})")
    print(f"[mech] registrations parsed = {len(regs)}  (ids {min(regs)}..{max(regs)})  "
          f"distinct families = {len(reg_fams)}")
    print(f"[mech] registration blocks my parser could not read: {len(misses)}")
    for s in misses[:5]:
        print(f"        {s}")
    print(f"[mech] unregistered slots inside 0..{TABLE_MAX_ID}: "
          f"{sorted(set(range(TABLE_MAX_ID + 1)) - set(regs))}")
    print(f"[mech] registered families NOT in catalog: {sorted(reg_fams - catalog)}")
    print(f"[mech] catalog families NOT registered: "
          f"{len(catalog - reg_fams)} -> {sorted(catalog - reg_fams)[:12]}")
    fb = {i: v for i, v in regs.items() if v[3]}
    print(f"[mech] registrations that needed the FALLBACK family pattern: {len(fb)}")
    for i, v in sorted(fb.items()):
        print(f"        id={i:4d}  {v[1]}  -> family '{v[2]}'")
    print(f"[mech] .rodata off->VA: va = 0x{rod['addr']:08x} + (off - 0x{rod['offset']:x})")
    print(f"[mech] input rows whose address is outside .rodata: "
          f"cmd={len(cmd_bad)} noti={len(noti_bad)}")

    lines = ["kind\tid\tname\trodata_va\tdispatcher\tstatus"]
    counts = {}
    notes = []
    for kind, rows in (("CMD", cmd_rows), ("NOTI", noti_rows)):
        for oid, name, va in rows:
            if kind == "CMD":
                if oid > TABLE_MAX_ID:
                    disp, status = "", "unused"
                    notes.append(f"CMD {oid} {name}: id > 0x{TABLE_MAX_ID:x}（查表上界）=> 无派发器")
                elif oid in regs:
                    fam = regs[oid][2]
                    if fam is None:
                        disp, status = "", "unknown"
                        notes.append(f"CMD {oid} {name}: 注册块 ctor 归不出家族名 "
                                     f"({regs[oid][1]}) => unknown（不猜）")
                    else:
                        disp, status = fam, "mapped"
                else:
                    disp, status = "", "no-dispatcher"
            else:
                disp, status = "", "no-dispatcher"
            assert status in STATUSES, f"非法状态 {status}"
            counts[status] = counts.get(status, 0) + 1
            lines.append(f"{kind}\t{oid}\t{name}\t0x{va:08x}\t{disp}\t{status}")

    Path(a.out_tsv).write_text("\n".join(lines) + "\n", encoding="utf-8")
    total = sum(counts.values())
    print(f"total={total} " + " ".join(f"{k}={v}" for k, v in sorted(counts.items())))
    print("\n[note] 逐条说明（可复核）:")
    for s in notes:
        print(f"  {s}")

    # 抽样核对：CMD 号 vs 家族语义（Step 4 的判据，10 条里 >=8 条相符）
    print("\n[sample] CMD id -> 注册家族（用于 Step 4 的 >=8/10 判据）:")
    for oid in (0, 1, 2, 3, 4, 5, 6, 7, 100, 300):
        r = dict((x[0], x) for x in cmd_rows).get(oid)
        reg = regs.get(oid)
        print(f"  CMD {oid:4d} {r[1] if r else '?':24s} -> "
              f"{reg[2] if reg and reg[2] else '(none)':34s} from {reg[1] if reg else '-'}")
    # 名字串自检
    badnames = []
    for kind, rows in (("CMD", cmd_rows), ("NOTI", noti_rows)):
        for oid, name, va in rows:
            s = str_at(elf, va) if va else None
            if s is None or not s.startswith(f"ENUM_{kind}PACKET_{name}"):
                badnames.append((kind, oid, name, s))
    print(f"\n[namecheck] rows whose .rodata string does not start with ENUM_<KIND>PACKET_<name>: "
          f"{len(badnames)}")
    for k, oid, name, s in badnames[:10]:
        print(f"  {k} {oid} {name}: got {s!r}")


if __name__ == "__main__":
    main()
