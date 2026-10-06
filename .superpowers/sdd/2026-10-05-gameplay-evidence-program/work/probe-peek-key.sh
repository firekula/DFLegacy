#!/usr/bin/env bash
# 编译 peek-mem.c 并读目标进程的密钥相关内存。
#
# ⚠️ 这是**对活靶子的扰动性操作**：PTRACE_ATTACH 会让 df_game_r 短暂停止（毫秒级），
#    读完立刻 PTRACE_DETACH。**不写内存、不改寄存器、不注入代码**。
#    按活靶纪律必须显式记录在批次清单里。
set -u

PID=$(cat /home/neople/game/pid/siroco11.pid)
echo "目标 pid = $PID"

echo
echo "=== 编译 ==="
gcc -O2 -o /tmp/dfl/peek-mem /tmp/dfl/peek-mem.c 2>&1 && echo "  编译成功" || { echo "  编译失败"; exit 1; }

echo
echo "=== 1. 读 0x940be80 处 128 字节（InitCipherKeyString 用的 16 字节结构体数组）==="
/tmp/dfl/peek-mem "$PID" 940be80 128

echo
echo "=== 2. 顺着 [0x940be80] 的指针，读该对象前 0x60 字节 ==="
PTR=$(/tmp/dfl/peek-mem "$PID" 940be80 4 | awk '/^  940be80/ {print $2}')
echo "  [0x940be80] = 0x$PTR"
if [ -n "$PTR" ] && [ "$PTR" != "00000000" ]; then
  /tmp/dfl/peek-mem "$PID" "$PTR" 96
  # Cipher+0x10 疑似密钥缓冲
  OFF=$(printf '%x' $((0x$PTR + 0x10)))
  echo
  echo "=== 3. 该对象 +0x10 起 512 字节（疑似密钥缓冲）==="
  /tmp/dfl/peek-mem "$PID" "$OFF" 512
fi

echo
echo "=== 4. 对照：另两个 16 字节结构体（index 1、2）==="
/tmp/dfl/peek-mem "$PID" 940be90 32
