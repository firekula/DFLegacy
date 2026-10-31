#!/bin/bash
# 无头 IDA 入口（本模式的薄包装）。权威实现在原处：
#   .superpowers/sdd/2026-09-29-dungeon-party/work/run_ida.sh
#
#   ./.recon/run-ida.sh <脚本> <输出文件> <命令> [参数...]
#   例：./.recon/run-ida.sh .../ida_tool.py .recon/out/x.txt dec 0x82E3A0
#
#   ./.recon/run-ida.sh --idb <库路径> <脚本> <输出文件> <命令> [参数...]
#   例：./.recon/run-ida.sh --idb .superpowers/.../official/df_game_r.bin.idb \
#            .superpowers/.../work/ida-game.py .superpowers/.../out/ida-smoke.txt smoke
#
# 这一层只做两件事，逻辑一行都不重复：
#   1) 按**项目根**解析脚本与输出路径，并转成 Windows 形式（G:/...）——
#      MSYS 形式（/g/...）在 Windows 版 IDA 的 Python 里打不开：
#      idat.exe 的 cwd 是项目根，但解释器是原生 Windows 的，没有 MSYS 根目录概念；
#   2) 转交给原件。
#
# 【2026-10-05 增加 --idb（Task 6）】原件的库选择本来就是 `${DNF_IDB:-<客户端库>}`
#   （run_ida.sh:31），本模式无法在 invoke 里设环境变量，所以在这里把 --idb 的参数
#   **转成 DNF_IDB=** 交给原件 —— 走既有约定，不另造第二套。
#   --idb 是可选的：不带它时行为与本文件历史上的完全一致（默认客户端库）。
#   相对路径按**项目根**解析（与脚本/输出同一套规则），再转 Windows 形式。
#
# 必须用 git-bash 调用（PATH 里的 bash 是 WSL 的，/d/... 会解析失败）。
set -u

IDA_DIR="$(cd "$(dirname "$0")/../.superpowers/sdd/2026-09-29-dungeon-party/work" && pwd)"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"

IDB_OVERRIDE=""
if [ "${1:-}" = "--idb" ]; then
    if [ "$#" -lt 2 ]; then
        echo "--idb 后面要跟库路径" >&2
        exit 64
    fi
    IDB_OVERRIDE="$2"; shift 2
fi

if [ "$#" -lt 3 ]; then
    echo "用法: $0 [--idb <库路径>] <脚本> <输出文件> <命令> [参数...]" >&2
    exit 64
fi

SCRIPT="$1"; shift
OUT="$1"; shift
CMD="$*"

case "$SCRIPT" in
    /*) SCRIPT_ABS="$SCRIPT" ;;
    *)  SCRIPT_ABS="$ROOT/$SCRIPT" ;;
esac
case "$OUT" in
    /*) OUT_ABS="$OUT" ;;
    *)  OUT_ABS="$ROOT/$OUT" ;;
esac
if [ -n "$IDB_OVERRIDE" ]; then
    case "$IDB_OVERRIDE" in
        /*) IDB_ABS="$IDB_OVERRIDE" ;;
        [A-Za-z]:/*) IDB_ABS="$IDB_OVERRIDE" ;;
        *)  IDB_ABS="$ROOT/$IDB_OVERRIDE" ;;
    esac
fi

to_windows_path() {
    case "$1" in
        /[A-Za-z]/*) printf '%s:/%s\n' "$(printf '%s' "${1:1:1}" | tr 'a-z' 'A-Z')" "${1:3}" ;;
        *)           printf '%s\n' "$1" ;;
    esac
}

mkdir -p "$(dirname "$OUT_ABS")" || { echo "建不出输出目录: $OUT_ABS" >&2; exit 1; }

if [ -n "$IDB_OVERRIDE" ]; then
    export DNF_IDB="$(to_windows_path "$IDB_ABS")"
fi

exec bash "$IDA_DIR/run_ida.sh" "$(to_windows_path "$OUT_ABS")" "$CMD" "$(to_windows_path "$SCRIPT_ABS")"

