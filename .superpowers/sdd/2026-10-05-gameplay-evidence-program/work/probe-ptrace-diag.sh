#!/usr/bin/env bash
# 只读诊断：为什么读 /proc/<pid>/mem 会 ESRCH，以及 VM 上有什么可用工具。
set -u
PID=$(cat /home/neople/game/pid/siroco11.pid)

echo "=== 1. 进程还在吗 ==="
ls -l /proc/$PID/ 2>/dev/null | head -3
cat /proc/$PID/status 2>/dev/null | head -2

echo
echo "=== 2. maps 能读吗（决定 VA 是否可映射）==="
head -3 /proc/$PID/maps 2>&1
echo "  maps 行数: $(wc -l < /proc/$PID/maps 2>/dev/null)"

echo
echo "=== 3. 直接 dd 读 mem（看真实 errno）==="
dd if=/proc/$PID/mem of=/dev/null bs=1 skip=$((0x940be80)) count=16 2>&1 | tail -2

echo
echo "=== 4. ptrace 相关内核/安全设置 ==="
echo -n "  getenforce: "; getenforce 2>/dev/null || echo "(无 SELinux 工具)"
echo -n "  selinuxfs: "; ls -d /selinux 2>/dev/null || echo "(无 /selinux)"
echo -n "  /proc/sys/kernel/yama: "; cat /proc/sys/kernel/yama/ptrace_scope 2>/dev/null || echo "(内核 2.6 无 yama)"
echo -n "  /proc/sys/kernel/randomize_va_space: "; cat /proc/sys/kernel/randomize_va_space 2>/dev/null

echo
echo "=== 5. 进程的 ptrace 状态（TracerPid 非 0 说明已被别人 ptrace）==="
grep -E '^(TracerPid|Uid|Gid|State):' /proc/$PID/status 2>/dev/null | sed 's/^/  /'

echo
echo "=== 6. 可用工具 ==="
for t in gdb gcore gcc cc g++ python python2 strace ltrace readelf objdump nm; do
  p=$(command -v $t 2>/dev/null); echo "  $t: ${p:-（无）}"
done

echo
echo "=== 7. 我们是谁（权限）==="
id
