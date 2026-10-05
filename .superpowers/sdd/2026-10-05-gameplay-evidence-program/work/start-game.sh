#!/usr/bin/env bash
# 起**只读靶子**所需的游戏服（siroco11 = 10013），不动其它进程。
# 若已在跑就不重复起。启动后验证 10013 是否监听。
set -u

echo "===== 0. 起之前：现在谁在跑 ====="
ps -eo pid,etime,comm | grep -E 'df_|gunnersvr|zergsvr|secagent' | grep -v grep

echo
echo "===== 1. 10013 现在是否已在监听 ====="
if netstat -lntp 2>/dev/null | grep -q ':10013'; then
  echo "  已在监听，不重复启动"
  netstat -lntp 2>/dev/null | grep ':10013'
  exit 0
fi
echo "  未监听，准备启动"

echo
echo "===== 2. 启动 game (siroco11) ====="
cd /home/neople/game || exit 1
chmod 777 * 2>/dev/null
sleep 1
./df_game_r siroco11 start &
sleep 6

echo
echo "===== 3. 起之后：进程与端口 ====="
ps -eo pid,etime,comm | grep df_game_r | grep -v grep || echo "  （df_game_r 不在进程表里）"
netstat -lntp 2>/dev/null | grep -E ':10013|:11013' || echo "  （10013/11013 未监听）"

echo
echo "===== 4. 日志尾部（看有没有起来/报什么错）====="
tail -20 /home/neople/game/log/siroco11/Log20261005.log 2>/dev/null
echo "--- .error ---"
tail -12 /home/neople/game/log/siroco11/Log20261005.error 2>/dev/null
