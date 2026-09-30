#!/bin/bash
# 无头 IDA 入口（本模式的薄包装）。权威实现在原处：
#   .superpowers/sdd/2026-09-29-dungeon-party/work/run_ida.sh
#
#   ./.recon/run-ida.sh <脚本> <输出文件> <命令> [参数...]
#   例：./.recon/run-ida.sh .../ida_tool.py .recon/out/x.txt dec 0x82E3A0
#
# 这一层只做两件事，逻辑一行都不重复：
#   1) 按**项目根**解析脚本与输出路径，并转成 Windows 形式（G:/...）——
#      MSYS 形式（/g/...）在 Windows 版 IDA 的 Python 里打不开：
#      idat.exe 的 cwd 是项目根，但解释器是原生 Windows 的，没有 MSYS 根目录概念；
#   2) 转交给原件。
#
# 必须用 git-bash 调用（PATH 里的 bash 是 WSL 的，/d/... 会解析失败）。
set -u

IDA_DIR="$(cd "$(dirname "$0")/../.superpowers/sdd/2026-09-29-dungeon-party/work" && pwd)"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"

if [ "$#" -lt 3 ]; then
    echo "用法: $0 <脚本> <输出文件> <命令> [参数...]" >&2
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

to_windows_path() {
    case "$1" in
        /[A-Za-z]/*) printf '%s:/%s\n' "$(printf '%s' "${1:1:1}" | tr 'a-z' 'A-Z')" "${1:3}" ;;
        *)           printf '%s\n' "$1" ;;
    esac
}

mkdir -p "$(dirname "$OUT_ABS")" || { echo "建不出输出目录: $OUT_ABS" >&2; exit 1; }

exec bash "$IDA_DIR/run_ida.sh" "$(to_windows_path "$OUT_ABS")" "$CMD" "$(to_windows_path "$SCRIPT_ABS")"
