#!/usr/bin/env bash
# 只读：dbmw 侦察 —— 谁连它、它长什么样、日志里说了什么。
set -u

echo "########## 1. 现在谁连着三个 dbmw 端口 ##########"
for p in 20203 20303 20403; do
  echo "=== 端口 $p ==="
  netstat -antp 2>/dev/null | grep -E ":$p\b" || echo "  （无连接）"
done

echo
echo "########## 2. dbmw 二进制 ##########"
for d in dbmw_mnt dbmw_guild dbmw_stat; do
  f=/home/neople/$d/df_dbmw_r
  [ -f "$f" ] || continue
  echo "=== $d ==="
  ls -l "$f" | awk '{print "  size="$5"  "$6" "$7" "$8}'
  file "$f" 2>/dev/null | sed 's/^/  /'
  md5sum "$f" 2>/dev/null | sed 's/^/  md5 /'
done

echo
echo "########## 3. 三个 dbmw 用的是同一个二进制吗（比对 md5）##########"
md5sum /home/neople/dbmw_*/df_dbmw_r 2>/dev/null

echo
echo "########## 4. dbmw 的日志（最近在做什么）##########"
for d in dbmw_mnt dbmw_guild dbmw_stat; do
  echo "=== $d ==="
  ls -la /home/neople/$d/log/ 2>/dev/null | tail -6
  for f in /home/neople/$d/log/*.log; do
    [ -f "$f" ] || continue
    echo "  --- $(basename "$f") 末 8 行 ---"
    tail -8 "$f" | sed 's/^/    /'
  done
done

echo
echo "########## 5. dbmw 的 table/ 目录（它有表定义！）##########"
for d in dbmw_mnt dbmw_guild dbmw_stat; do
  echo "=== $d/table ==="
  ls -la /home/neople/$d/table/ 2>/dev/null | sed 's/^/  /'
  for f in /home/neople/$d/table/*; do
    [ -f "$f" ] || continue
    echo "  --- $(basename "$f")（前 30 行）---"
    head -30 "$f" | sed 's/^/    /'
  done
done

echo
echo "########## 6. game 有没有直接连 3306（判断直连 vs 走 dbmw）##########"
netstat -antp 2>/dev/null | grep ':3306' | head -20
