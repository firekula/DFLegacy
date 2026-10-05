#!/usr/bin/env bash
# 只读：游戏服的端口/服务配置 + 登录链（gate）在哪。
set -u

echo "===== 1. game 的 cfg 目录全清单 ====="
ls -la /home/neople/game/cfg/ 2>/dev/null

echo
echo "===== 2. siroco11.cfg 全文（游戏服的端口/服务器组）====="
cat /home/neople/game/cfg/siroco11.cfg 2>/dev/null

echo
echo "===== 3. 各 cfg 里的端口字段汇总 ====="
grep -rhiE 'port|ip' /home/neople/game/cfg/*.cfg 2>/dev/null | sort -u | head -40

echo
echo "===== 4. 登录链：谁提供 5505 / auth（找 gate）====="
for d in /home/neople/*/; do
  b=$(basename "$d")
  # 各进程目录的 cfg 里搜 5505 / auth / login
  hits=$(grep -rliE '5505|api/v1/auth|game-server-ip' "$d" 2>/dev/null | head -3)
  [ -n "$hits" ] && { echo "--- $b ---"; echo "$hits"; }
done

echo
echo "===== 5. server_config.tbl / server_str.xml（服务器组定义）====="
head -40 /home/neople/game/server_str.xml 2>/dev/null
echo "--- server_config.tbl ---"
head -20 /home/neople/dbmw_mnt/table/server_config.tbl 2>/dev/null

echo
echo "===== 6. 有没有 web/gate 目录 ====="
ls -la /home/ 2>/dev/null
ls -la /opt/lampp/htdocs/ 2>/dev/null | head -20
