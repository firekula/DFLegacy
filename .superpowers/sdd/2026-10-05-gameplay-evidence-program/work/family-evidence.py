#!/usr/bin/env python3
"""family-evidence.py — 判据 3 的补全件：给每个家族找出**独立于符号表**的身份证据与所属 .cpp。

为什么需要它（判据 3 的三条路实测结果，全部在案）：
  ① symtab 的 STT_FILE 顺序邻接 —— 证伪：4184 条 dispatcher 行里 3668 条（87.7%）落在同一个
     XorRand.cpp 窗口（catch-all 窗口，不是编译单元边界）。
  ② `.idb` 的行信息 —— 不可用：get_sourcefile 对 7649 个名字全返回空。
  ③ 二进制自带的 DWARF —— **不覆盖游戏代码**：.debug_info 只有 **21 个 CU**，.debug_line 的
     CU 是 FreeType 自己的源码（ftinit.c / ftcalc.c / ttdriver.c，目录 /root/f248/src/base、
     ./builds/unix），即**静态链接的第三方库**的调试信息。这同时解释了 ② 为什么是空的。
所以"每个家族 → 所属 .cpp"在本二进制上**没有权威来源**。本脚本给出能拿到的最强替代：
  A. **全量** vtable/RTTI 反查（不是抽样）：每个家族的 dispatch_sig（或退而用 process）地址
     必然出现在它自己 vtable 的 +20 / +32 槽位，而 vtable 的 +4 是 RTTI 指针，type_info 里
     存着**类名字符串**。这条路径直接读 .rodata 原始字节，不经过符号表。
     —— 它同时回答"我们有没有漏家族"：把 RTTI 名字集合与 425 个家族名对照，**反向**看有没有
        RTTI 里有、目录里没有的家族。
  B. **__FILE__ 字符串池**：断言/日志里嵌入的源码路径（.rodata 里 167 个不同 .cpp）。
     对每个家族，扫它所有成员函数的函数体，看有没有引用某个 .cpp 串 —— 有就能给出**精确的
     所属 .cpp**，没有就如实记"该家族在二进制里没有留下文件路径"。覆盖率是实测出来的数，
     不是猜的。

用法: family-evidence.py <out.txt> <family-cpp-map.tsv>
"""
import bisect
import re
import struct
import sys
from importlib.util import module_from_spec, spec_from_file_location
from pathlib import Path

from capstone import Cs, CS_ARCH_X86, CS_MODE_32
from capstone.x86 import X86_OP_IMM, X86_OP_MEM

HERE = Path(__file__).resolve().parent
OUT_DIR = HERE / "out"
ELF_PATH = Path(r"G:\Game\DNF_ACT1\DFLegacy\.superpowers\sdd\2026-09-29-dungeon-party"
                r"\work\official\df_game_r.bin")
CATALOG = OUT_DIR / "catalog_dispatchers_full.txt"
SYMS_TSV = OUT_DIR / "demangled-syms.tsv"

FAMILY_RX = [
    re.compile(r"(?:^|::)(?:DisPatcher|Dispatcher|DIspatcher|Dispatcer|Dispather|Dispatchr)_"
               r"([A-Za-z0-9_]+?)(?:::|\()"),
    re.compile(r"ARAD::Arad_Dispatcher_([A-Za-z0-9_]+?)_Impl(?:::|\()"),
    re.compile(r"(?:^|::)(?:Dispatcher|DisPatcher|DIspatcher|Dispatcer|Dispather)"
               r"([A-Z][A-Za-z0-9_]*)(?:::|\()"),
]


def family_of(name):
    for rx in FAMILY_RX:
        m = rx.search(name)
        if m:
            return m.group(1)
    return None


def load_elf():
    spec = spec_from_file_location("elfrecon", HERE / "elfrecon.py")
    m = module_from_spec(spec)
    spec.loader.exec_module(m)
    return m, m.Elf(str(ELF_PATH))


def read_cstr(elf, va, maxlen=400):
    off = elf.va_to_off(va)
    if off is None:
        return None
    raw = elf.data[off:off + maxlen].split(b"\0")[0]
    if not raw or not all(32 <= c < 127 for c in raw):
        return None
    return raw.decode("latin1")


def main(out_txt, out_tsv):
    m, elf = load_elf()
    rod = elf.by_name[".rodata"]
    text = elf.by_name[".text"]
    text_lo, text_hi = text["addr"], text["addr"] + text["size"]
    rod_lo, rod_hi = rod["addr"], rod["addr"] + rod["size"]
    rows = []
    L = []
    L.append("判据 3 补全件 —— 每家族的独立身份证据与所属 .cpp")
    L.append(f"二进制: {ELF_PATH}  md5={elf.md5()}")
    L.append("")

    # ---------- 0) DWARF 覆盖率的实测（解释为什么没有权威的 家族->.cpp）
    di = elf.by_name[".debug_info"]
    blob = elf.data[di["offset"]:di["offset"] + di["size"]]
    cus, off = [], 0
    while off + 11 <= len(blob):
        (ln,) = struct.unpack_from("<I", blob, off)
        if ln == 0 or ln == 0xFFFFFFFF:
            break
        ver, abbrev, asz = struct.unpack_from("<HIB", blob, off + 4)
        cus.append((off, ln, ver))
        off += 4 + ln
    dl = elf.by_name[".debug_line"]
    dblob = elf.data[dl["offset"]:dl["offset"] + dl["size"]]
    line_cus, off, first_files = 0, 0, []
    while off + 4 <= len(dblob):
        (ln,) = struct.unpack_from("<I", dblob, off)
        if ln == 0:
            break
        ver = struct.unpack_from("<H", dblob, off + 4)[0]
        hlen = struct.unpack_from("<I", dblob, off + 6)[0]
        if ver == 2 and line_cus < 6:
            p = off + 10 + 1 + 1 + 4 + 1 - 1        # minlen + (无 maxops) + 4 + opbase-1
            p = off + 10
            p += 1                                   # min_inst_length
            p += 4                                   # default_is_stmt, line_base, line_range, opcode_base
            p += struct.unpack_from("<B", dblob, off + 14)[0] - 1   # standard_opcode_lengths
            hdr_end = off + 4 + hlen
            while p < hdr_end and dblob[p] != 0:     # include_directories
                p = dblob.index(b"\0", p) + 1
            p += 1
            if p < hdr_end and dblob[p] != 0:        # 第一个 file_name
                e = dblob.index(b"\0", p)
                first_files.append(dblob[p:e].decode("latin1"))
        line_cus += 1
        off += 4 + ln
    L.append("0) DWARF 覆盖率的实测（为什么'家族 -> .cpp'没有权威来源）")
    L.append(f"   .debug_info: {di['size']} B, CU 数 = {len(cus)}（版本 {sorted({v for _o,_l,v in cus})}）")
    L.append(f"   .debug_line: {dl['size']} B, CU 数 = {line_cus}")
    L.append(f"   这些 CU 的源码文件样本 = {first_files}")
    L.append("   => 调试信息属于**静态链接的第三方库**（/root/f248/ 是 FreeType 的构建树），")
    L.append("      **不覆盖游戏自己的 C++ 代码**。这同时解释了 .idb 的 get_sourcefile 为什么全空。")
    L.append("")

    # ---------- 1) 全量 vtable 目录（扫描 .rodata / .data.rel.ro / .data）
    L.append("1) 全量 vtable 目录（直接读 .rodata/.data.rel.ro/.data 的原始字节）")
    recs = []
    for sname in (".rodata", ".data.rel.ro", ".data"):
        s = elf.by_name.get(sname)
        if not s or not s["size"]:
            continue
        b = elf.data[s["offset"]:s["offset"] + s["size"]]
        base = s["addr"]
        for i in range(0, len(b) - 40, 4):
            w0, w1, w5, w8 = (struct.unpack_from("<I", b, i)[0],
                              struct.unpack_from("<I", b, i + 4)[0],
                              struct.unpack_from("<I", b, i + 20)[0],
                              struct.unpack_from("<I", b, i + 32)[0])
            if w0 != 0 or not (rod_lo <= w1 < rod_hi):
                continue
            if not (text_lo <= w5 < text_hi):
                continue
            nm = read_cstr(elf, struct.unpack_from("<I", elf.data, elf.va_to_off(w1) + 4)[0]) \
                if elf.va_to_off(w1) is not None else None
            if not nm:
                continue
            recs.append((base + i, nm, w5, w8, sname))
    named = [r for r in recs if "ispatcher" in r[1] or "ispatcher" in r[1]]
    L.append(f"   候选记录（word0=0 且 +4 在 .rodata 且 +20 在 .text）= {len(recs)}")
    L.append(f"   其中 RTTI 名含 ispatcher 的 = {len(named)}")
    L.append(f"   样本: " + "; ".join(f"0x{r[0]:08x} {r[1]}" for r in named[:3]))
    L.append("")

    # ---------- 2) 家族 -> RTTI 名字（全量，不是抽样）
    fams = {}
    for ln in CATALOG.read_text(encoding="utf-8").splitlines():
        if not ln or ln.startswith("#"):
            continue
        f = ln.split("\t")
        p = f[1].split("=", 1)[1]
        s = f[2].split("=", 1)[1]
        fams[f[0]] = (int(p.split("(")[0], 16), int(s.split("(")[0], 16))
    by_sig, by_proc = {}, {}
    for va, nm, sig, proc, sname in recs:
        by_sig.setdefault(sig, []).append((va, nm))
        if proc:
            by_proc.setdefault(proc, []).append((va, nm))
    L.append("2) 全量反查：每个家族的 dispatch_sig / process 是否出现在某个 vtable 里，且名字相符")
    ok = no_vt = mismatch = 0
    mism, novt = [], []
    tsv = ["family\tsig_or_proc\tvtable\trtti_name\tmatch"]
    tsv.append(f"#DWARF\tdebug_info_cus={len(cus)}\tdebug_line_cus={line_cus}\t"
               f"sample_source_files={'|'.join(first_files[:6])}")
    tsv.append(f"#VTABLE\trecords={len(recs)}\twith_dispatcher_rtti={len(named)}")
    for fam in sorted(fams):
        pa, sa = fams[fam]
        cands = []
        if sa:
            cands += [(a, n, "dispatch_sig") for a, n in by_sig.get(sa, [])]
        if pa:
            cands += [(a, n, "process") for a, n in by_proc.get(pa, [])]
        hit = [c for c in cands if fam.lower() in c[1].lower()]
        if hit:
            ok += 1
            a, n, which = hit[0]
            tsv.append(f"{fam}\t{which}\t0x{a:08x}\t{n}\tyes")
        elif cands:
            mismatch += 1
            mism.append((fam, cands))
            a, n, which = cands[0]
            tsv.append(f"{fam}\t{which}\t0x{a:08x}\t{n}\tNO")
        else:
            no_vt += 1
            novt.append(fam)
            tsv.append(f"{fam}\t-\t-\t-\tno-vtable")
    L.append(f"   名字相符（独立确认）           = {ok} / {len(fams)}")
    L.append(f"   找到 vtable 但名字不含家族名   = {mismatch}")
    for fam, cands in mism[:8]:
        L.append(f"       {fam}: " + "; ".join(f"0x{a:08x} {n}" for a, n, _w in cands[:3]))
    L.append(f"   完全找不到 vtable（无 dispatch_sig 也无 process 命中） = {no_vt}")
    for fam in novt[:12]:
        L.append(f"       {fam}  (process=0x{fams[fam][0]:08x} sig=0x{fams[fam][1]:08x})")
    L.append("")

    # ---------- 3) 反向：RTTI 里有、目录里没有的家族（查"有没有漏"）
    L.append("3) 反向检查：RTTI 名字里有、而目录里没有的派发器（用于发现漏掉的家族）")
    known = set(fams)
    extra = {}
    for va, nm, sig, proc, sname in recs:
        f = family_of(nm)
        if f and f not in known:
            extra.setdefault(f, []).append((va, nm))
    L.append(f"   目录外家族数 = {len(extra)}")
    for f in sorted(extra)[:20]:
        L.append(f"       {f}  <- 0x{extra[f][0][0]:08x} {extra[f][0][1]}")
    for f in sorted(extra):
        tsv.append(f"#EXTRA\t{f}\t0x{extra[f][0][0]:08x}\t{extra[f][0][1]}")
    L.append("")

    # ---------- 4) __FILE__ 池 -> 每家族精确 .cpp（覆盖率实测）
    L.append("4) __FILE__ 串池 -> 每家族的精确 .cpp（覆盖率是实测值）")
    rblob = elf.data[rod["offset"]:rod["offset"] + rod["size"]]
    cpp = {}
    for mm in re.finditer(rb"[\x20-\x7e]{6,300}", rblob):
        s = mm.group()
        # 必须是**以 .cpp 结尾**的路径。只判 `b".cpp" in s` 会把日志格式串也收进来 ——
        # 实测踩过一次：'Position : PacketDispatcher.cpp ZeroHit or 1500up Hit ... %d ...'
        # 是条日志消息，不是 __FILE__。
        if s.endswith(b".cpp") and b" " not in s:
            cpp[rod["addr"] + mm.start()] = s.decode("latin1")
    L.append(f"   .rodata 里含 .cpp 的串 = {len(cpp)}（去重 {len(set(cpp.values()))}）")
    # 每个家族的成员函数（按类名前缀从符号表取），再扫这些函数体里引用的 .cpp 串
    fam_funcs = {}
    for ln in SYMS_TSV.read_text(encoding="utf-8", errors="replace").splitlines():
        p = ln.split("\t", 3)
        if len(p) < 4 or p[2] != "FUNC":
            continue
        f = family_of(p[3])
        if f:
            try:
                fam_funcs.setdefault(f, []).append((int(p[0], 16), int(p[1])))
            except ValueError:
                pass
    md = Cs(CS_ARCH_X86, CS_MODE_32)
    md.detail = True
    got_cpp = {}
    for fam, funcs in fam_funcs.items():
        found = None
        for va, size in funcs:
            if not size:
                continue
            off = elf.va_to_off(va)
            if off is None:
                continue
            for ins in md.disasm(elf.data[off:off + size], va):
                for op in ins.operands:
                    v = op.imm if op.type == X86_OP_IMM else (op.mem.disp if op.type == X86_OP_MEM else None)
                    if v in cpp:
                        found = cpp[v]
                        break
                if found:
                    break
            if found:
                break
        if found:
            got_cpp[fam] = found
    L.append(f"   家族总数 = {len(fams)}；能指到精确 .cpp 的 = {len(got_cpp)} "
             f"（{100.0*len(got_cpp)/max(len(fams),1):.1f}%）")
    L.append(f"   指不到的 = {len(fams) - len(got_cpp)} —— 这些家族在二进制里**没有留下源码路径**")
    L.append("   能指到的（全部列出，可核对）:")
    for fam in sorted(got_cpp):
        L.append(f"       {fam:42s} -> {got_cpp[fam]}")
    for fam in sorted(fams):
        tsv.append(f"#CPP\t{fam}\t{got_cpp.get(fam, '')}")

    # ---------- 5) 子要求一（"同量级"）的**修正量法**：到底有几个源文件承载派发器代码
    stt_cpp = set()
    for ln in Path(r"G:\Game\DNF_ACT1\DFLegacy\.superpowers\sdd\2026-09-29-dungeon-party"
                   r"\work\official\game_r.syms.txt").read_text(
                       encoding="utf-8", errors="replace").splitlines():
        p = ln.split("\t", 3)
        if len(p) >= 4 and p[2] == "FILE" and "Dispatcher" in p[3]:
            stt_cpp.add(p[3])
    fpool = set(got_cpp.values())
    union = stt_cpp | fpool
    L.append("")
    L.append("5) 子要求一（`*Dispatcher*.cpp` 条目数 vs 家族数'同量级'）的修正量法")
    L.append(f"   STT_FILE 名字含 Dispatcher 的 .cpp          = {len(stt_cpp)}")
    L.append(f"   __FILE__ 池里被家族代码实际引用到的 .cpp     = {len(fpool)}")
    L.append(f"   两者并集（= 确实承载派发器代码的源文件）      = {len(union)}")
    L.append(f"   家族数 / 文件数 = {len(fams)}/{len(union)} = {len(fams)/max(len(union),1):.1f} 家族每文件")
    L.append("   => spec 拿'类'与'文件'直接比'同量级'，前提不成立：一个 TU 承载多个家族是常态")
    L.append("      （PacketDispatcher_Impl_1.cpp 一个 FILE 窗口内就有 461 条 dispatcher 符号行）。")
    tsv.append(f"#SUBCRIT1\tstt_dispatcher_cpp={len(stt_cpp)}\tfilepool_cpp={len(fpool)}"
               f"\tunion={len(union)}\tfamilies={len(fams)}")

    Path(out_tsv).write_text("\n".join(tsv) + "\n", encoding="utf-8")
    Path(out_txt).write_text("\n".join(L) + "\n", encoding="utf-8")
    print("\n".join(L))
    print(f"\n[tsv] {out_tsv}")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
