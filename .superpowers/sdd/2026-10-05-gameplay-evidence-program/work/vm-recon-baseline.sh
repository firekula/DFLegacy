#!/usr/bin/env bash
# vm-recon-baseline.sh —— VM 侧**只读**基线采集（一条命令跑完，输出给宿主解析）。
# 纪律：只读；不 kill、不写入游戏配置/DB；不改任何系统文件。
set -u
echo "=== T0 ==="
date '+%Y-%m-%d %H:%M:%S %Z (%z)'; echo "epoch=$(date +%s)"
echo "=== uname ==="
uname -a
echo "=== 进程（df_*）==="
ps -eo pid,ppid,etime,comm,args 2>/dev/null | grep -E 'df_[a-z_]*_r' | grep -v grep
echo "=== 监听端口 ==="
( netstat -lntp 2>/dev/null || ss -lntp 2>/dev/null ) | head -60
echo "=== 游戏服二进制 ==="
ls -l /home/neople/game/df_game_r 2>/dev/null
md5sum /home/neople/game/df_game_r 2>/dev/null
sha256sum /home/neople/game/df_game_r 2>/dev/null
echo "=== game cfg ==="
ls -l /home/neople/game/cfg/ 2>/dev/null
for f in /home/neople/game/cfg/*.cfg; do echo "--- $f"; cat "$f" 2>/dev/null; done
echo "=== 日志目录 ==="
ls -l /home/neople/game/log/ 2>/dev/null | head -20
echo "=== MySQL socket 与 pid ==="
ls -l /opt/lampp/var/mysql/mysql.sock 2>/dev/null
ls -l /opt/lampp/var/mysql/*.pid 2>/dev/null
echo "=== python/其它工具可用性 ==="
for c in python python2 mysql tcpdump gdb objdump readelf nm strings gcore perl; do
  printf '%-10s ' "$c"; command -v "$c" 2>/dev/null || echo MISSING
done
echo "=== /proc/sys/kernel/yama/ptrace_scope（决定能不能只读 attach）==="
cat /proc/sys/kernel/yama/ptrace_scope 2>/dev/null || echo "no yama"
echo "=== DONE ==="
