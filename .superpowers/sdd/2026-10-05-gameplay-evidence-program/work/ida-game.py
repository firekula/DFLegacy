# ida-game.py —— 在 df_game_r.bin.idb 上取数（供 .recon 的 ida-game 工具调用）
#
# 参数约定与 .recon/run-ida.sh 一致：ARGV[1] = outfile，ARGV[2..] = args。
# 与客户端那份 ida_tool.py 的区别只有一个：**目标库不同**（官服游戏服 df_game_r.bin.idb，
# 不是 DNF.exe.idb）。库由 .recon/run-ida.sh --idb 转成 DNF_IDB= 环境变量交给 run_ida.sh，
# 这里不关心，也不该另写一份包装。
#
# 分支：
#   smoke             —— 自检：报告实际加载的 IDB 路径、符号总数、名字里含 Dispatcher 的符号数
#   dispatcher-names  —— 所有名字里含 Dispatcher_/DisPatcher_ 的命名地址（ea<TAB>mangled 名）
#                        供 Task 7 判据 2（两个 demangler 的集合差异）
#   dispatcher-files  —— 每个"名字含 ispatch"的函数：ea<TAB>名<TAB>所属源文件
#                        供 Task 7 判据 3（家族 -> .cpp 映射；来自库里的 DWARF 行信息，
#                        而不是 symtab 的 STT_FILE 邻接 —— 后者已被实测证伪）
#   其它               —— 打印 unknown args 并照常落盘（便于定位参数转发问题）
import idaapi
import idautils
import idc

try:
    import ida_lines
except ImportError:
    ida_lines = None

OUT = idc.ARGV[1]
ARGS = idc.ARGV[2:]
lines = []


def emit(s):
    lines.append(str(s))


def sourcefile(ea):
    """取某个地址所属源文件。不同 IDA 版本把行信息放在不同模块，按可用性依次试。"""
    for mod in (ida_lines, idaapi, idc):
        if mod is None:
            continue
        f = getattr(mod, "get_sourcefile", None)
        if f is None:
            continue
        try:
            v = f(ea)
            if v:
                return v
        except Exception:
            continue
    return ""


def smoke():
    idb = idaapi.get_path(idaapi.PATH_TYPE_IDB)
    emit(f"IDB = {idb}")
    names = list(idautils.Names())
    emit(f"symbols = {len(names)}")
    hits = [(ea, nm) for ea, nm in names if "Dispatcher_" in nm or "DisPatcher_" in nm]
    emit(f"dispatcher-named symbols = {len(hits)}")
    for ea, nm in hits[:3]:
        emit(f"  0x{ea:08x}  {nm}")


def dispatcher_names():
    idb = idaapi.get_path(idaapi.PATH_TYPE_IDB)
    emit(f"# IDB = {idb}")
    hits = [(ea, nm) for ea, nm in idautils.Names()
            if "Dispatcher_" in nm or "DisPatcher_" in nm]
    hits.sort()
    emit(f"# count = {len(hits)}")
    for ea, nm in hits:
        emit(f"0x{ea:08x}\t{nm}")


def dispatcher_files():
    idb = idaapi.get_path(idaapi.PATH_TYPE_IDB)
    emit(f"# IDB = {idb}")
    # 诊断：这个库到底有没有"函数"。（实测：idautils.Functions() 为空 —— 库里的名字来自
    # 符号表，但没有函数定义，所以只能走 Names()。）
    funcs = list(idautils.Functions())
    emit(f"# idautils.Functions() = {len(funcs)}")
    for label, mod in (("ida_lines", ida_lines), ("idaapi", idaapi), ("idc", idc)):
        emit(f"# get_sourcefile in {label}: "
             f"{'yes' if (mod is not None and getattr(mod, 'get_sourcefile', None)) else 'no'}")
    names = [(ea, nm) for ea, nm in idautils.Names() if "ispatch" in nm]
    emit(f"# dispatcher-ish names = {len(names)}")
    got = 0
    for ea, nm in names:
        src = sourcefile(ea)
        if src:
            got += 1
        emit(f"0x{ea:08x}\t{nm}\t{src}")
    emit(f"# with source file = {got}")


if ARGS and ARGS[0] == "smoke":
    smoke()
elif ARGS and ARGS[0] == "dispatcher-names":
    dispatcher_names()
elif ARGS and ARGS[0] == "dispatcher-files":
    dispatcher_files()
else:
    emit(f"unknown args: {ARGS}")

with open(OUT, "w", encoding="utf-8") as f:
    f.write("\n".join(lines) + "\n")
idc.qexit(0)
