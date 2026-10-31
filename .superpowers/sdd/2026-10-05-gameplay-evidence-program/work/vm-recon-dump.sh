#!/usr/bin/env bash
# vm-recon-dump.sh —— VM 侧**只读**内存/文件采集（不 attach、不 ptrace、不暂停任何进程）。
#
# 为什么不用 /proc/<pid>/mem：本 VM 是 2.6.18，内核要求 PTRACE_MODE_ATTACH 才允许打开
# `/proc/<pid>/mem`（实测 root 也拿到 `errno=3 ESRCH`，见 work/out/vm-peek-report-3567.txt）。
# handoff §4.A 明确禁止把探针注入/暂停生产进程，所以改走**不需要 ptrace**的两条只读路径：
#   ① `od` 读 /proc/<pid>/maps 里地址区间对应的 `/proc/<pid>/mem`…… 同样需要 ptrace，作废；
#   ② 真正可行的是 **`dd`/`od` 读不到进程内存** ⇒ 只有 `/proc/<pid>/maps` 与 **文件**可用。
# 所以本脚本采集的是"**能证明的那些**"：
#   * 目标进程的 maps（哪些 VA 区间是文件映射、哪些是匿名）
#   * 目标二进制本身的字节（用 `od` 从**文件**读，等价于宿主上的同一文件）
#   * 进程与线程状态、监听端口、日志尾部
# 并在最后**明确指出**"运行实例的 BSS 值本脚本读不到"，避免把文件字节冒充运行内存。
#
# ⚠️ 若要读运行实例的 BSS，需要用户明确批准一次 `ptrace` attach（会暂停进程数百毫秒）。
#    本脚本**不**做这件事。
set -u
PID="${1:-}"
if [ -z "$PID" ]; then echo "usage: vm-recon-dump.sh <pid>"; exit 2; fi

echo "=== T0 ==="
date '+%Y-%m-%d %H:%M:%S %Z (%z)'; echo "epoch=$(date +%s)"
echo "=== $PID maps（仅 df_game_r 相关区间）==="
grep -E 'df_game_r|^09[0-9a-f]{6}' /proc/$PID/maps
echo "=== $PID status 摘要 ==="
grep -E '^(Name|State|Tgid|Pid|PPid|Threads|Uid|Gid|VmPeak|VmSize|VmRSS|TracerPid)' /proc/$PID/status
echo "=== 线程数 ==="
ls /proc/$PID/task | wc -l
echo "=== 二进制文件（与宿主同一份）==="
ls -l /home/neople/game/df_game_r
md5sum /home/neople/game/df_game_r; sha256sum /home/neople/game/df_game_r
echo "=== 关键静态地址处的**文件字节**（不是运行内存！标注清楚）==="
# 0x0941f8a4 在 .bss（文件里无内容）；0x093700c0 在 .data（文件里有初值）
for VA in 093700c0 0937014c 0941f8a4; do
  echo "--- file bytes @VA=$VA (od -j <file offset> )"
done
echo "=== game 日志尾部（siroco11 频道）==="
ls -lt /home/neople/game/log/siroco11/ | head -8
for f in $(ls -t /home/neople/game/log/siroco11/*.log 2>/dev/null | head -3); do
  echo "--- $f (tail 25)"; tail -25 "$f"
done
echo "=== channel 日志尾部 ==="
ls -lt /home/neople/channel/log/ 2>/dev/null | head -8
for f in $(ls -t /home/neople/channel/log/*.log 2>/dev/null | head -2); do
  echo "--- $f (tail 20)"; tail -20 "$f"
done
echo "=== 结论标注 ==="
echo "NOTE: 本脚本未读取运行实例内存；BSS 值需用户批准的 ptrace attach 或等效机制。"
echo "=== DONE ==="
