#!/usr/bin/env python3
"""extract-dispatchers.py — 从解名后的符号表抽出全量派发器家族。

四种命名族都要收（Neople 自己不一致）：
  DisPatcher_X::...        （既有 catalog 的主要拼写）
  Dispatcher_X::...        （既有 catalog 也用这个拼写）
  DIspatcher_X::...        （第 4 种拼写。Task 2 实测 12 行：
                            DIspatcher_OneDayLetheTicket::dispatch_sig(CUser*, PacketBuf&)
                            计划骨架的三条 pattern 都匹配不到它 —— 见 out/demangle-check.txt 补充段）
  ARAD::Arad_Dispatcher_X_Impl::...
归一化家族名 = 去掉上述前后缀的 X；第 4 列保留“所属 .cpp”与原始拼写以便回溯。

与计划骨架的两处差异（都写在这里，不藏在代码里）：
 A. 只认 type == FUNC 且“参数表之后没有尾随垃圾”的名字来当 process / dispatch_sig。
    骨架用 `"::dispatch_sig(" in name` 判定，于是
      `0x08bd0860  63  OBJECT  DisPatcher_Login::dispatch_sig(CUser*, PacketBuf&)::__PRETTY_FUNCTION__`
    也会被登记成 dispatch_sig，而且骨架是“后出现者覆盖先出现者”（与 process 不同，process 取
    size 最大）⇒ 当 OBJECT 行排在 FUNC 行之后时，dispatch_sig 会指向一个 **字符串对象**，
    Task 5 反汇编它拿到的就不是函数体。这里：非 FUNC 一律不认（实测挡掉 318 行）。
 A2. 同家族多候选时的选取规则**不是 size 最大** —— 实测证明 size 最大会选错重载：
       Dispatcher_SecurityCardAuthCancel::process(CUser*)                          316 B @0x0820a580
       Dispatcher_SecurityCardAuthCancel::process(CUser*, MSG_BASE&, ParamBase&)    10 B @0x08233a9c
     3 参数那个才**是派发器入口签名**（全目录的派发器都用它），1 参数那个是另一个重载。
     既有 catalog（已被上一轮验证过）取的就是 10 B 那个 ⇒ 按 size 取大者会与既有目录冲突（实测 1/84）。
     ⇒ 选取次序：① 参数表等于派发器签名者优先（process 用 (CUser*, MSG_BASE&, ParamBase&)，
     dispatch_sig 用 (CUser*, PacketBuf&)）；② 其次 size 大者；③ 最后地址小者（与行序无关）。
 B. pattern 1 加上 `DIspatcher`（第 4 种拼写）。不加就会漏家族。

file= 归属：**已实测证伪，不作为证据输出**。
  做法曾是“按 symtab 的 STT_FILE 顺序，把符号归给最近一个 FILE 条目”。实测结果：
  家族 422 个，首选文件 422 个都是同一个 `XorRand.cpp` —— 这不可能是一个编译单元。
  进一步实测：dispatcher 行 4159 条，其中 3650 条落在 XorRand.cpp 那个窗口里，说明该区域
  **没有 FILE 标记**（一个 catch-all 窗口），FILE 边界在那里不是编译单元边界；
  只有在 FILE 标记密的区域（如 PacketDispatcher_Impl_1.cpp 窗口，461 条）归属才看着合理。
  ⇒ 本文件的第 4 列只输出 spell=；家族 → .cpp 的映射从本 dump **不可得**，见 Task 7 判据 3。
  （脚本会把上面这两个数在运行时算出来写进表头，保证可重跑。）

用法: extract-dispatchers.py <demangled-syms.tsv> <out.txt>

输出：每行 4 列（制表符分隔）
  family <TAB> process=0xADDR(SIZE) <TAB> dispatch_sig=0xADDR(SIZE) <TAB> spell=<原始拼写 | ...>
第 1–3 列的取值契约与计划一致（Task 4 的 load_families 依赖它）。
"""
import re
import sys
from pathlib import Path

PATTERNS = [
    re.compile(r"(?:^|::)(?:DisPatcher|Dispatcher|DIspatcher|Dispatcer|Dispather|Dispatchr)_"
               r"([A-Za-z0-9_]+?)(?:::|\()"),
    re.compile(r"ARAD::Arad_Dispatcher_([A-Za-z0-9_]+?)_Impl(?:::|\()"),
    # 兜底（Task 4 实测发现，本任务首版漏了这 3 个家族：见 out/opcode-map-residual.txt）：
    #   DispatcherResetRandomOption        （前缀后无下划线）  -> ResetRandomOption
    #   Dispatcer_polic_one_to_one_chat_disable（Dispatcer，少个 h）-> polic_one_to_one_chat_disable
    #   Dispather_RegenerationRandomOption （Dispather，字母换位）  -> RegenerationRandomOption
    # 前两个由 PATTERNS[0] 的拼写扩展覆盖；第三条（无下划线）只能靠本兜底。
    re.compile(r"(?:^|::)(?:Dispatcher|DisPatcher|DIspatcher|Dispatcer|Dispather)"
               r"([A-Z][A-Za-z0-9_]*)(?:::|\()"),
]
FALLBACK = PATTERNS[2]
FAMILY_PATTERNS = PATTERNS[:2]     # 正常图案（family_of 先用它们）
ARAD = PATTERNS[1]

# 派发器入口签名（实测：全目录的派发器入口都用这两个；见 docstring 差异 A2）
WANT_ARGS = {"process": "(CUser*, MSG_BASE&, ParamBase&)",
             "dispatch_sig": "(CUser*, PacketBuf&)"}

# 用来找“含 dispatcher 字样但没被任何 pattern 认领”的残余，防止静默漏家族
HINT = re.compile(r"[Dd][Ii]spatcher_")


def rows(p):
    """按 symtab 原顺序产出 (addr, size, typ, name, cur_file)。"""
    cur_file = "?"
    for ln in Path(p).read_text(encoding="utf-8", errors="replace").splitlines():
        parts = ln.split("\t", 3)
        if len(parts) < 4:
            continue
        addr, size, typ, name = parts
        if typ == "FILE":
            cur_file = name
            continue
        try:
            addr_i, size_i = int(addr, 16), int(size)
        except ValueError:
            continue
        yield addr_i, size_i, typ, name, cur_file


def family_of(name):
    """返回 (family, is_arad, via_fallback)。via_fallback 的条目要在表头单列。"""
    for rx in FAMILY_PATTERNS:
        m = rx.search(name)
        if m:
            return m.group(1), rx is ARAD, False
    m = FALLBACK.search(name)
    if m:
        return m.group(1), False, True
    return None, False, False


def member_of(name, typ):
    """返回 'dispatch_sig' / 'process' / None。"""
    if typ != "FUNC":
        return None
    base = re.sub(r"\s+(?:const|noexcept|override|final)\b.*$", "", name)
    if not base.endswith(")"):
        return None
    q = base.split("(", 1)[0]
    if q.endswith("::dispatch_sig"):
        return "dispatch_sig"
    if q.endswith("::process"):
        return "process"
    return None


def better(cur, cand, want_args):
    """同家族多候选：① 派发器签名优先 ② size 大者 ③ 地址小者（与行序无关）。"""
    def rank(c):
        addr, size, name = c
        args = name[name.index("("):] if "(" in name else ""
        return (0 if args.startswith(want_args) else 1, -size, addr)
    if cur is None:
        return cand
    return cand if rank(cand) < rank(cur) else cur


def main(src, dst):
    fams = {}
    by_file = {}
    diag = {"nonfunc_dropped": [], "trailing_dropped": [], "unmatched": []}
    for addr, size, typ, name, cur_file in rows(src):
        fam, is_arad, via_fb = family_of(name)
        if fam is None:
            if HINT.search(name):
                diag["unmatched"].append(name)
            continue
        rec = fams.setdefault(fam, {"process": None, "dispatch_sig": None, "ncand": {},
                                    "spellings": set(), "arad_only": True, "via_fallback": False})
        rec["spellings"].add(name.split("(")[0])
        if via_fb:
            rec["via_fallback"] = True
        by_file[cur_file] = by_file.get(cur_file, 0) + 1
        if not is_arad:
            rec["arad_only"] = False
        which = member_of(name, typ)
        base = re.sub(r"\s+(?:const|noexcept|override|final)\b.*$", "", name)
        q = base.split("(", 1)[0]
        if which is None:
            # 诊断：本该是成员但被规则挡掉的，绝不静默丢
            if q.endswith("::dispatch_sig") or q.endswith("::process"):
                if typ != "FUNC":
                    diag["nonfunc_dropped"].append(f"{typ}\t{name}")
                elif not base.endswith(")"):
                    diag["trailing_dropped"].append(f"{typ}\t{name}")
            continue
        rec[which] = better(rec[which], (addr, size, name), WANT_ARGS[which])
        rec["ncand"][which] = rec["ncand"].get(which, 0) + 1

    lines = [
        "# df_game_r 全量派发器目录（生成自 out/demangled-syms.tsv）",
        "# 格式: family<TAB>process=0xADDR( SIZE)<TAB>dispatch_sig=0xADDR( SIZE)<TAB>spell=<原始拼写 | ...>",
        "# 拼写族: DisPatcher_ / Dispatcher_ / DIspatcher_ / ARAD::Arad_Dispatcher_X_Impl",
        "# process/dispatch_sig 只取 type==FUNC 且参数表后无尾随垃圾者；同候选取派发器签名优先，其次 size 大（docstring 差异 A/A2）",
    ]
    for fam in sorted(fams):
        r = fams[fam]
        proc = f"0x{r['process'][0]:08x}({r['process'][1]:5d})" if r["process"] else "0x00000000(    0)"
        sig = f"0x{r['dispatch_sig'][0]:08x}({r['dispatch_sig'][1]:5d})" if r["dispatch_sig"] else "0x00000000(    0)"
        spell = " | ".join(sorted(r["spellings"])[:2])
        lines.append(f"{fam}\tprocess={proc}\tdispatch_sig={sig}\tspell={spell}")

    n_sig = sum(1 for r in fams.values() if r["dispatch_sig"])
    n_proc = sum(1 for r in fams.values() if r["process"])
    neither = [f for f, r in fams.items() if not r["dispatch_sig"] and not r["process"]]
    arad_only = [f for f, r in fams.items() if r["arad_only"]]
    multi = {f: r["ncand"] for f, r in fams.items()
             if r["ncand"].get("process", 0) > 1 or r["ncand"].get("dispatch_sig", 0) > 1}

    # FILE 归属统计（用来证明第 4 列为什么只输出 spell=）
    n_disp_rows = sum(by_file.values())
    top_file, top_n = max(by_file.items(), key=lambda kv: kv[1]) if by_file else ("?", 0)
    second = sorted(by_file.items(), key=lambda kv: -kv[1])[1:2]
    stat = [
        f"# 家族数 {len(fams)}；dispatch_sig {n_sig}；process {n_proc}；"
        f"两者皆无 {len(neither)}；仅由 Arad 拼写贡献 {len(arad_only)}",
        "# 两者皆无的家族（失败形状 ③：无地址的条目不是证据）: "
        + (", ".join(sorted(neither)) if neither else "(无)"),
        "# 仅由 Arad 拼写贡献的家族（本次与同名 DisPatcher_ 家族归并，按计划约定）: "
        + (", ".join(sorted(arad_only)) if arad_only else "(无)"),
        "# 有多个 process/dispatch_sig 候选的家族（选取规则见 docstring A2）: "
        + (", ".join(f"{f}:{d}" for f, d in sorted(multi.items())) if multi else "(无)"),
        "# file= 列为何缺席（实测证伪，见 docstring）：dispatcher 行 %d 条，按 STT_FILE 顺序归属后"
        % n_disp_rows,
        "#   最多的一条窗口是 %s（%d 条，%.1f%%）—— 该区域没有 FILE 标记，是 catch-all 窗口而不是"
        % (top_file, top_n, 100.0 * top_n / max(n_disp_rows, 1)),
        "#   编译单元边界；次多 %s。=> 家族→.cpp 的映射从本 dump 不可得，Task 7 判据 3 按此记录。"
        % (f"{second[0][0]}({second[0][1]})" if second else "(无)"),
        "# 未匹配但含 dispatcher 字样的行（防静默漏家族；多为 vtable/typeinfo/guard variable/"
        "global constructors 包装与 Arad_Dispatcher_Manager 本身）: %d" % len(diag["unmatched"]),
        "# 被丢弃的非 FUNC 候选（__PRETTY_FUNCTION__ 字符串对象陷阱）: %d"
        % len(diag["nonfunc_dropped"]),
        "# 靠兜底 pattern（无下划线变体）才认出的家族: "
        + (", ".join(sorted(f for f, r in fams.items() if r["via_fallback"])) or "(无)"),
        "# 首版（commit 819287f）只有 422 个家族，漏了拼写变体 DispatcherResetRandomOption /"
        " Dispatcer_polic_one_to_one_chat_disable / Dispather_RegenerationRandomOption ——"
        " 由 Task 4 的注册表解析发现，本版修正为 %d 个。" % len(fams),
    ]
    lines = lines[:4] + stat + lines[4:]
    Path(dst).write_text("\n".join(lines) + "\n", encoding="utf-8")

    print(f"{len(fams)} families, {n_sig} with dispatch_sig, {n_proc} with process -> {dst}")
    print(f"neither process nor dispatch_sig: {len(neither)} -> {sorted(neither)}")
    print(f"arad-only families: {len(arad_only)} -> {sorted(arad_only)}")
    print(f"fallback-pattern families: "
          f"{sorted(f for f, r in fams.items() if r['via_fallback'])}")
    print(f"[diag] families with multiple candidates: {len(multi)} -> {multi}")
    print(f"[diag] non-FUNC candidates dropped (the __PRETTY_FUNCTION__/OBJECT trap): "
          f"{len(diag['nonfunc_dropped'])}")
    for s in diag["nonfunc_dropped"][:5]:
        print(f"    {s}")
    print(f"[diag] FUNC candidates dropped for trailing junk: {len(diag['trailing_dropped'])}")
    for s in diag["trailing_dropped"][:5]:
        print(f"    {s}")
    print(f"[diag] rows containing dispatcher-ish text but claimed by no pattern: "
          f"{len(diag['unmatched'])}")
    for s in diag["unmatched"][:8]:
        print(f"    {s}")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
