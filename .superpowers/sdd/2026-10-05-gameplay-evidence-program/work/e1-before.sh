#!/usr/bin/env bash
# e1-before.sh —— E1 单变量实验的**前置只读**采集：DB 当前值 + 日志备份 + 进程基线。
# 纪律：全部只读（SELECT / cp / ps / ls）。不改 DB、不重启任何进程。
set -u
TS="$(date '+%Y%m%d-%H%M%S')"
echo "=== E1 BEFORE  T0=$TS $(date '+%Y-%m-%d %H:%M:%S %Z %z') epoch=$(date +%s) ==="
echo "=== 目标行（server_group=3, category=1, restrict_code=33）==="
mysql -ugame -p'REDACTED_VM_DB_PASSWORD' --socket=/opt/lampp/var/mysql/mysql.sock --table \
  --default-character-set=utf8 -e \
  "SELECT server_group,category,restrict_code,restrict_value,mod_date,reg_date FROM d_taiwan.dnf_restrict_state WHERE server_group=3 AND category=1 AND restrict_code IN (1,32,33) ORDER BY restrict_code" 2>/dev/null
echo "=== 全表计数（用于事后证明只有一行被我改过）==="
mysql -ugame -p'REDACTED_VM_DB_PASSWORD' --socket=/opt/lampp/var/mysql/mysql.sock --table \
  --default-character-set=utf8 -e \
  "SELECT COUNT(*) total, SUM(restrict_value='1') as ones, SUM(restrict_value='0') as zeros, SUM(mod_date) as sum_mod FROM d_taiwan.dnf_restrict_state" 2>/dev/null
echo "=== 日志备份（重启会截断，先拷出来）==="
BK="/tmp/e1-backup-$TS"
mkdir -p "$BK"
for d in /home/neople/game/log/siroco11 /home/neople/channel/log /home/neople/bridge/log; do
  if [ -d "$d" ]; then
    mkdir -p "$BK/$(echo "$d" | tr '/' '_')"
    cp -a "$d"/. "$BK/$(echo "$d" | tr '/' '_')/" 2>/dev/null
    echo "backed up $d -> $BK/$(echo "$d" | tr '/' '_')"
  fi
done
echo "BACKUP_DIR=$BK"
ls -l "$BK" | head
echo "=== df_game_r 进程基线 ==="
ps -eo pid,ppid,etime,args | grep -E 'df_game_r|df_channel_r' | grep -v grep
echo "=== 10013 上的活动连接（谁连着游戏面）==="
netstat -tn 2>/dev/null | grep -E ':2719|:10013' || echo "(无 10013 连接)"
echo "=== grep 日志里与 restrict/login 相关的最近行 ==="
grep -hiE 'restrict|LoginChannel|isApply' /home/neople/game/log/siroco11/*.log 2>/dev/null | tail -20 || echo "(无命中)"
for f in $(ls -t /home/neople/game/log/siroco11/*.log 2>/dev/null | head -2); do
  echo "--- $f tail 8"; tail -8 "$f"
done
echo "=== BEFORE DONE ==="
