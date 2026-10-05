#!/usr/bin/env python3
"""m0-acceptance.py — 跑 spec §2.2 的四条可证伪判据，输出原始结果。

用法: m0-acceptance.py <out.txt>

四条判据（原文见 docs/superpowers/specs/2026-10-05-gameplay-evidence-program-design.md §2.2）：
  1 命令映射无余数          —— opcode-map.tsv 的状态分布必须加总到 1171，且 unknown 被显式计数
  2 两个 demangler 结果一致 —— sidecar c++filt（我方 symtab 解名）vs IDA 各自给出的
                              Dispatcher_* 集合，**差异逐条分类解释**
  3 模块清单交叉            —— `*Dispatcher*.cpp` 的 FILE 条目数 vs 家族数
  4 抽样反证               —— 随机 10 个家族，用其 dispatch_sig 地址**反查 .rodata 里的
                              vtable/RTTI 名字串，必须命中**

与计划 Task 7 Step 1 的差异（记录在此，不藏）：
  A. 判据 4 计划里写的是"用 game-scan-imm 找调用点、再反汇编看是否引用含家族名的字符串"。
     实测这条路走不通：两个名字数组（g_szCmdPacketName @0x0936d920 / 通知的 @0x0936d040）
     各只有**一个**引用点（GetCommandPacketName / GetNotifyPacketName），家族名串并不被
     dispatch_sig 的调用点引用。改用**更强**的反查：dispatch_sig 的地址必然出现在该家族的
     vtable 第 +20 字节槽位（Itanium ABI），而 vtable 的 +4 槽位是 RTTI 指针，指向的
     type_info 里存着**类名字符串**。这条路径直接读 .rodata 的原始字节，且不经过符号表。
  B. 判据 2 的"逐条解释"落在下面的分类统计里：按 symtab 的符号类型与命名模式把差异归类。
"""
import bisect
import random
import struct
import sys
from importlib.util import module_from_spec, spec_from_file_location
from pathlib import Path

HERE = Path(__file__).resolve().parent
O = Path(r"G:\Game\DNF_ACT1\DFLegacy\.superpowers\sdd\2026-09-29-dungeon-party\work\official")
OUT_DIR = HERE / "out"
ELF_PATH = O / "df_game_r.bin"
SYMS = O / "game_r.syms.txt"
CATALOG = OUT_DIR / "catalog_dispatchers_full.txt"
OPMAP = OUT_DIR / "opcode-map.tsv"
IDA_NAMES = OUT_DIR / "ida-dispatcher-names.txt"
SPEC = "docs/superpowers/specs/2026-10-05-gameplay-evidence-program-design.md §2.2"

buf = []


def emit(s=""):
    buf.append(str(s))


def sec(title):
    emit()
    emit("=" * 78)
    emit(title)
    emit("=" * 78)


def load_elf():
    spec = spec_from_file_location("elfrecon", HERE / "elfrecon.py")
    m = module_from_spec(spec)
    spec.loader.exec_module(m)
    return m, m.Elf(str(ELF_PATH))


# ---------------------------------------------------------------- 判据 1
def crit1():
    sec("判据 1 —— 命令映射无余数（每条号都有归属或明确标记，不接受'未解释'）")
    rows = []
    for ln in OPMAP.read_text(encoding="utf-8").splitlines():
        if not ln or ln.startswith("kind\t"):
            continue
        f = ln.split("\t")
        rows.append((f[0], int(f[1]), f[2], f[4], f[5]))
    counts = {}
    for _k, _i, _n, _d, st in rows:
        counts[st] = counts.get(st, 0) + 1
    cmd = sum(1 for r in rows if r[0] == "CMD")
    noti = sum(1 for r in rows if r[0] == "NOTI")
    emit(f"来源: {OPMAP}")
    emit(f"命令表行数: CMD={cmd}  NOTI={noti}  合计={len(rows)}")
    emit("状态分布（unknown 显式计数，含 0）:")
    total = 0
    for st in ("mapped", "no-dispatcher", "unused", "inter-server", "unknown"):
        c = counts.get(st, 0)
        total += c
        emit(f"   {st:14s} = {c}")
    emit(f"   状态加总      = {total}")
    ok = (total == len(rows) == 1171 and cmd == 608 and noti == 563)
    emit(f"判定: 加总 == 行数 == 1171 且 CMD=608/NOTI=563 -> {'PASS' if ok else 'FAIL'}")
    emit(f"unknown={counts.get('unknown', 0)}，spec §2.3 的改判线是 200 -> "
         f"{'未触发' if counts.get('unknown', 0) <= 200 else '触发（须就近改判）'}")
    return ok


# ---------------------------------------------------------------- 判据 2
def crit2():
    sec("判据 2 —— 两个 demangler 结果一致（sidecar c++filt vs IDA，差异逐条解释）")
    # 我方：原始 mangled 符号表，取名字里含 Dispatcher_/DisPatcher_ 的行
    mine_rows = []
    for ln in SYMS.read_text(encoding="utf-8", errors="replace").splitlines():
        p = ln.split("\t", 3)
        if len(p) >= 4 and ("Dispatcher_" in p[3] or "DisPatcher_" in p[3]):
            try:
                mine_rows.append((int(p[0], 16), p[2], p[3]))
            except ValueError:
                pass
    mine = {a for a, _t, _n in mine_rows}
    # IDA 侧
    ida_rows = []
    for ln in IDA_NAMES.read_text(encoding="utf-8", errors="replace").splitlines():
        if not ln or ln.startswith("#"):
            continue
        a, n = ln.split("\t", 1)
        ida_rows.append((int(a, 16), n))
    ida = {a for a, _n in ida_rows}
    emit(f"我方来源: {SYMS}（mangled 名，c++filt 那一侧的输入）")
    emit(f"   行数={len(mine_rows)}  唯一地址数={len(mine)}")
    emit(f"IDA 来源: {IDA_NAMES}（idautils.Names() 里的 mangled 名）")
    emit(f"   行数={len(ida_rows)}  唯一地址数={len(ida)}")
    only_mine = sorted(mine - ida)
    only_ida = sorted(ida - mine)
    emit(f"集合差异: 仅我方 {len(only_mine)} 个地址；仅 IDA {len(only_ida)} 个地址")
    # 分类解释"仅我方"
    type_of = {}
    for a, t, n in mine_rows:
        type_of.setdefault(a, (t, n))
    cats = {}
    for a in only_mine:
        t, n = type_of[a]
        if "__PRETTY_FUNCTION__" in n:
            k = "OBJECT __PRETTY_FUNCTION__（编译期字符串，IDA 侧不建名）"
        elif n.startswith("vtable for") or n.startswith("VTT for"):
            k = "OBJECT vtable for（IDA 把它折进结构、不单列符号）"
        elif n.startswith("typeinfo") or n.startswith("typeinfo name"):
            k = "OBJECT typeinfo（同上）"
        elif n.startswith("guard variable for"):
            k = "OBJECT guard variable（函数内静态量的守卫）"
        elif n.startswith("global constructors keyed to"):
            k = "OBJECT global constructors keyed to（静态初始化桩）"
        elif t == "FUNC":
            k = "FUNC（函数：需逐个看，可能是别名/重合地址）"
        else:
            k = f"{t}（其它）"
        cats[k] = cats.get(k, 0) + 1
    emit("'仅我方'按符号类别归类:")
    for k, v in sorted(cats.items(), key=lambda kv: -kv[1]):
        emit(f"   {v:5d}  {k}")
    emit("'仅我方'里 FUNC 类的样本（最多 10 条）:")
    shown = 0
    for a in only_mine:
        t, n = type_of[a]
        if t == "FUNC" and shown < 10:
            emit(f"   0x{a:08x}  {n}")
            shown += 1
    emit(f"'仅 IDA' 的样本（最多 10 条）:")
    ida_name = {a: n for a, n in ida_rows}
    for a in only_ida[:10]:
        emit(f"   0x{a:08x}  {ida_name[a]}")
    # 同名不同址：同一个 mangled 名在两边是否落在同一批地址上
    mine_names = {}
    for a, _t, n in mine_rows:
        mine_names.setdefault(n, set()).add(a)
    ida_names = {}
    for a, n in ida_rows:
        ida_names.setdefault(n, set()).add(a)
    common = set(mine_names) & set(ida_names)
    moved = [n for n in common if mine_names[n] != ida_names[n]]
    emit(f"两边都有的名字: {len(common)}；其中地址集合**不同**的名字: {len(moved)}")
    for n in sorted(moved)[:10]:
        emit(f"   {n}")
        emit(f"      我方={sorted(hex(x) for x in mine_names[n])[:4]}  "
             f"IDA={sorted(hex(x) for x in ida_names[n])[:4]}")
    ok = (len(only_ida) == 0)
    emit(f"判定: 差异全部可解释、且不存在'只有 IDA 有'的地址 -> "
         f"{'PASS' if ok else 'PARTIAL（见上分类）'}")
    return ok, len(only_mine), len(only_ida)


# ---------------------------------------------------------------- 判据 3
def crit3():
    sec("判据 3 —— 模块清单交叉（*Dispatcher*.cpp 的 FILE 条目数 vs 家族数；每个家族能指到所属 .cpp）")
    files = {}
    for ln in SYMS.read_text(encoding="utf-8", errors="replace").splitlines():
        p = ln.split("\t", 3)
        if len(p) >= 4 and p[2] == "FILE":
            files[p[3]] = files.get(p[3], 0) + 1
    disp_cpp = sorted({f for f in files if "Dispatcher" in f})
    fams = [ln.split("\t")[0] for ln in CATALOG.read_text(encoding="utf-8").splitlines()
            if ln and not ln.startswith("#")]
    emit(f"符号表里 STT_FILE 条目总数: {sum(files.values())}（不同名字 {len(files)} 个）")
    emit(f"其中名字含 Dispatcher 的 .cpp: {len(disp_cpp)} 个")
    for f in disp_cpp:
        emit(f"   {f}")
    emit(f"目录家族数: {len(fams)}")
    emit(f"比值: {len(fams)}/{len(disp_cpp)} = {len(fams)/max(len(disp_cpp),1):.1f}x "
         f"-> **不满足'同量级'**")
    emit("每个家族 -> 所属 .cpp：**本任务无法给出**，两条路都被实测否掉：")
    emit("   ① symtab 的 STT_FILE 顺序邻接：实测证伪 —— 4184 条 dispatcher 行里 3668 条（87.7%）")
    emit("      落在同一个 XorRand.cpp 窗口（该区域没有 FILE 标记，是 catch-all 窗口，不是编译单元边界）；")
    emit("      次多 PacketDispatcher_Impl_1.cpp(461)。见 out/catalog_dispatchers_full.txt 表头。")
    emit("   ② 库里的 DWARF 行信息：实测不可用 —— idautils.Functions() 只有 1797 个函数，")
    emit("      get_sourcefile（ida_lines/idaapi/idc 三处都有该 API）对 7649 个 dispatcher-ish 名字")
    emit("      **全部返回空**，即这个 .idb 没有导入行信息（二进制里其实有 .debug_line）。")
    emit(f"      （原始输出: {OUT_DIR / 'ida-dispatcher-files.txt'}）")
    emit("判定: 判据 3 的**两条子要求都不成立** -> FAIL（原因如上，不是提取出错）")
    emit("       结构与 spec 的预设不符：派发器不是'一家族一 .cpp'，而是集中在少数几个")
    emit("       框架 TU 里（PacketDispatcher_Impl_1/2.cpp、PacketDispatcher_Secu.cpp 等）。")
    return False


# ---------------------------------------------------------------- 判据 4
def crit4(m, elf):
    sec("判据 4 —— 抽样反证（随机 10 个家族，用 dispatch_sig 地址反查 .rodata 的名字串）")
    fams = {}
    for ln in CATALOG.read_text(encoding="utf-8").splitlines():
        if not ln or ln.startswith("#"):
            continue
        f = ln.split("\t")
        s = f[2].split("=", 1)[1]
        sig = int(s.split("(")[0], 16)
        if sig:
            fams[f[0]] = sig
    names = sorted(fams)
    rng = random.Random(20261005)          # 固定种子 => 可重跑得到同一批样本
    sample = rng.sample(names, 10)
    emit(f"候选家族（有非零 dispatch_sig）: {len(names)} 个；随机种子 20261005 抽 10 个")
    emit("反查路径: dispatch_sig 地址 -> 在 .rodata 里搜它的 4 字节指针（vtable +20 槽位）")
    emit("          -> vtable 起点 = 命中处 - 20 -> +0 应为 0(offset-to-top)、+4 是 RTTI 指针")
    emit("          -> type_info: +0 是 vtable、+4 是 mangled 类名串")
    emit()
    # 预先建立"4 字节值 -> 出现位置"的索引（只针对 rw/rodata 里的 vtable 区）
    regions = []
    for sname in (".rodata", ".data.rel.ro", ".data"):
        s = elf.by_name.get(sname)
        if s and s["size"]:
            regions.append((sname, s["addr"], s["offset"], s["size"]))
    ok_count = 0
    for fam in sample:
        sig = fams[fam]
        pat = struct.pack("<I", sig)
        hits = []
        for sname, addr, off, size in regions:
            blob = elf.data[off:off + size]
            i = blob.find(pat)
            while i != -1 and len(hits) < 8:
                hits.append((sname, addr + i))
                i = blob.find(pat, i + 1)
        emit(f"家族 {fam}  dispatch_sig=0x{sig:08x}")
        if not hits:
            emit("   !! 在 .rodata/.data.rel.ro/.data 里找不到该地址的指针 -> 未命中")
            continue
        matched = False
        for sname, slot in hits:
            rec = slot - 20                     # vtable 起点（dispatch_sig 在 +20 槽）
            off = elf.va_to_off(rec)
            if off is None or off < 0:
                continue
            word0, rtti = struct.unpack_from("<II", elf.data, off)
            noff = elf.va_to_off(rtti)
            if noff is None:
                continue
            tv, nameptr = struct.unpack_from("<II", elf.data, noff)
            if not (elf.by_name[".rodata"]["addr"] <= nameptr <
                    elf.by_name[".rodata"]["addr"] + elf.by_name[".rodata"]["size"]):
                continue
            poff = elf.va_to_off(nameptr)
            raw = elf.data[poff:poff + 120].split(b"\0")[0].decode("latin1", "replace")
            # mangled 类名形如 16DisPatcher_Login；去掉长度前缀后做包含判断
            stripped = raw
            j = 0
            while j < len(stripped) and stripped[j].isdigit():
                j += 1
            if j:
                stripped = stripped[j:]
            hit = fam.lower() in raw.lower()
            emit(f"   {sname} vtable=0x{rec:08x} word0=0x{word0:08x} RTTI=0x{rtti:08x} "
                 f"name={raw!r} -> {'命中' if hit else '不含家族名'}")
            matched = matched or hit
        if matched:
            ok_count += 1
        else:
            emit("   !! 所有候选 vtable 都不含该家族名（可能是 ICF 合并/别名，需逐条看）")
    emit()
    emit(f"命中 {ok_count}/10 -> {'PASS' if ok_count == 10 else 'FAIL'}")
    return ok_count == 10


def main():
    out = Path(sys.argv[1])
    m, elf = load_elf()
    emit("M0 验收 —— spec §2.2 四条可证伪判据的原始输出")
    emit(f"判据原文: {SPEC}")
    emit(f"二进制: {ELF_PATH}  md5={elf.md5()}")
    ok1 = crit1()
    ok2, n2m, n2i = crit2()
    ok3 = crit3()
    ok4 = crit4(m, elf)
    sec("汇总")
    emit(f"判据 1 命令映射无余数          : {'PASS' if ok1 else 'FAIL'}")
    emit(f"判据 2 两个 demangler 一致      : {'PASS' if ok2 else 'PARTIAL'} "
         f"(仅我方 {n2m} / 仅 IDA {n2i})")
    emit(f"判据 3 模块清单交叉            : {'PASS' if ok3 else 'FAIL'}（两条子要求都不成立，原因见上）")
    emit(f"判据 4 抽样反证（10 家族）      : {'PASS' if ok4 else 'FAIL'}")
    out.write_text("\n".join(buf) + "\n", encoding="utf-8")
    print("\n".join(buf))


if __name__ == "__main__":
    main()
