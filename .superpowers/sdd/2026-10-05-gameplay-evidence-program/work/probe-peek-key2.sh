#!/usr/bin/env bash
# 验证 0x2311c7a0 是 Cipher 对象，并把 334 字节密钥取全。
set -u
PID=$(cat /home/neople/game/pid/siroco11.pid)
echo "pid = $PID"

echo
echo "=== 1. 对象头（total_key_len + vector 三指针）==="
/tmp/dfl/peek-mem "$PID" 2311c7a0 16

echo
echo "=== 2. vector 的 14 个 ICryptoGraph*（静态分析说链长 14）==="
# begin = 0x2311cbb8
/tmp/dfl/peek-mem "$PID" 2311cbb8 56

echo
echo "=== 3. 密钥缓冲：+0x10 起 334 字节（total_key_len）==="
/tmp/dfl/peek-mem "$PID" 2311c7b0 334
