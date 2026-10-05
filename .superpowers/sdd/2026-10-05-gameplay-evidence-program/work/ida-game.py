# ida-game.py —— 在 df_game_r.bin.idb 上取数（供 .recon 的 ida-game 工具调用）
#
# 参数约定与 .recon/run-ida.sh 一致：ARGV[1] = outfile，ARGV[2..] = args。
# 与客户端那份 ida_tool.py 的区别只有一个：**目标库不同**（官服游戏服 df_game_r.bin.idb，
# 不是 DNF.exe.idb）。库由 .recon/run-ida.sh --idb 转成 DNF_IDB= 环境变量交给 run_ida.sh，
# 这里不关心，也不该另写一份包装。
#
# 分支：
#   smoke  —— 自检：报告实际加载的 IDB 路径、符号总数、名字里含 Dispatcher 的符号数
#   其它    —— 打印 unknown args 并照常落盘（便于定位参数转发问题）
import idaapi
import idautils
import idc

OUT = idc.ARGV[1]
ARGS = idc.ARGV[2:]
lines = []


def emit(s):
    lines.append(str(s))


def smoke():
    idb = idaapi.get_path(idaapi.PATH_TYPE_IDB)
    emit(f"IDB = {idb}")
    names = list(idautils.Names())
    emit(f"symbols = {len(names)}")
    hits = [(ea, nm) for ea, nm in names if "Dispatcher_" in nm or "DisPatcher_" in nm]
    emit(f"dispatcher-named symbols = {len(hits)}")
    for ea, nm in hits[:3]:
        emit(f"  0x{ea:08x}  {nm}")


if ARGS and ARGS[0] == "smoke":
    smoke()
else:
    emit(f"unknown args: {ARGS}")

with open(OUT, "w", encoding="utf-8") as f:
    f.write("\n".join(lines) + "\n")
idc.qexit(0)
