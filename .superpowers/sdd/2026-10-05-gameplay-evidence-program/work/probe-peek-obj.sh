#!/usr/bin/env bash
# 顺着指针读 Cipher 对象及其密钥缓冲。
set -u
PID=$(cat /home/neople/game/pid/siroco11.pid)
echo "pid = $PID"

echo
echo "=== 1. 0x2311c7a0 处 96 字节（疑似 Cipher 对象）==="
/tmp/dfl/peek-mem "$PID" 2311c7a0 96

echo
echo "=== 2. +0x10 起 512 字节（静态分析指向：Cipher+0x10 被 memset 0x801 ⇒ 疑似密钥缓冲）==="
/tmp/dfl/peek-mem "$PID" 2311c7b0 512
