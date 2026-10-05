#!/usr/bin/env bash
# 只读：看这次会话（16:4x）dbmw 侧到底处理了什么，判断业务报文有没有发生。
set -u

echo "########## 1. 各 dbmw 的 QueryCount（本次新增的行）##########"
for d in dbmw_mnt dbmw_guild dbmw_stat; do
  echo "=== $d ==="
  tail -12 /home/neople/$d/log/QueryCount*.log 2>/dev/null | sed 's/^/  /'
done

echo
echo "########## 2. PacketProcess（本次的 packet id 统计）##########"
for d in dbmw_mnt dbmw_guild dbmw_stat; do
  echo "=== $d ==="
  tail -14 /home/neople/$d/log/PacketProcess*.log 2>/dev/null | sed 's/^/  /'
done

echo
echo "########## 3. 业务类日志（guild 存档 / statics 上报）##########"
echo "--- dbmw_guild/GuildModify ---"
tail -10 /home/neople/dbmw_guild/log/GuildModify*.log 2>/dev/null | sed 's/^/  /'
echo "--- dbmw_stat/Statistic ---"
tail -8 /home/neople/dbmw_stat/log/Statistic*.log 2>/dev/null | sed 's/^/  /'

echo
echo "########## 4. UDP 日志（业务是否走 UDP）##########"
for d in dbmw_mnt dbmw_guild dbmw_stat; do
  echo "=== $d ==="
  cat /home/neople/$d/log/Udp*.log 2>/dev/null | tail -12 | sed 's/^/  /'
done

echo
echo "########## 5. 当前连接（TCP 与 UDP）##########"
netstat -antup 2>/dev/null | grep -E ':(20203|20303|20403)\b' | sed 's/^/  /'

echo
echo "########## 6. dbmw 的 cfg 里 TCP/UDP 端口怎么写的 ##########"
for d in dbmw_mnt dbmw_guild dbmw_stat; do
  echo "=== $d ==="
  grep -iE 'port|udp|tcp' /home/neople/$d/cfg/*.cfg | sed 's/^/  /'
done
