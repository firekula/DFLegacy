#!/usr/bin/env bash
# 只读：补齐剩余服务的端口/角色，并理清 game <-> channel <-> bridge <-> dbmw 的依赖方向。
set -u

echo "########## 1. 剩余服务的 cfg 全量（端口 + 对端）##########"
for svc in point coserver statics stun community guild monitor manager relay; do
  d=/home/neople/$svc
  [ -d "$d" ] || continue
  echo "=== $svc ==="
  for f in "$d"/cfg/*; do
    [ -f "$f" ] || continue
    echo "  --- $(basename "$f") ---"
    grep -iE 'port|_ip|server|type|no *=' "$f" 2>/dev/null | head -18 | sed 's/^/    /'
  done
done

echo
echo "########## 2. game 的 siroco11.cfg 里"我依赖谁"（对端清单）##########"
grep -iE '_ip|_port' /home/neople/game/cfg/siroco11.cfg 2>/dev/null | sed 's/^/  /'

echo
echo "########## 3. channel.cfg 全量（它怎么找到 game / bridge）##########"
cat /home/neople/channel/cfg/channel.cfg 2>/dev/null | sed 's/^/  /'

echo
echo "########## 4. bridge.cfg 全量 ##########"
cat /home/neople/bridge/cfg/bridge.cfg 2>/dev/null | sed 's/^/  /'

echo
echo "########## 5. monitor 的服务器组表（谁注册到监控 = 官方进程清单）##########"
cat /home/neople/monitor/cfg/mnt_siroco.cfg 2>/dev/null | sed 's/^/  /'
