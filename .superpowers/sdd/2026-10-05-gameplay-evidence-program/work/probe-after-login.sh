#!/usr/bin/env bash
# 只读：确认刚才那次登录发生了什么（日志 + 端口连接 + DB 痕迹），并检查抓包工具是否可用。
set -u

echo "===== 1. game 服日志尾部（找登录/鉴权/断连）====="
for f in /home/neople/game/log/siroco11/Log20261005.log \
         /home/neople/game/log/siroco11/Log20261005.error \
         /home/neople/game/log/siroco11/Log20261005.cri; do
  [ -f "$f" ] || continue
  echo "--- $(basename "$f")（末 20 行）---"
  tail -20 "$f"
  echo
done

echo "===== 2. 10013 上的连接（还有活跃会话吗）====="
netstat -antp 2>/dev/null | grep -E ':10013' || echo "  （10013 上没有活跃连接）"

echo
echo "===== 3. 抓包工具可用性 ====="
for t in tcpdump tshark ngrep; do
  p=$(command -v $t 2>/dev/null); echo "  $t: ${p:-（无）}"
done

echo
echo "===== 4. 7001 / 10013 上的历史连接 ====="
netstat -ant 2>/dev/null | grep -E ':(7001|10013)\b' | head -12

echo
echo "===== 5. member_join_info 现在的行（登录会写这里）====="
mysql -ugame -p'REDACTED_VM_DB_PASSWORD' --socket=/opt/lampp/var/mysql/mysql.sock --table \
  -e "select m_id, from_unixtime(login_time) as login_at, ip, login_ip, error_type from d_taiwan.member_join_info order by m_id;" 2>&1 | grep -v 'Using a password'
