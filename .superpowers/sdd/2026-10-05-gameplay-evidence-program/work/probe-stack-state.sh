#!/usr/bin/env bash
# 只读：确认各进程二进制就位，并看 game 上次为什么退出。
set -u

echo "===== 1. 各进程目录里的可执行文件 ====="
for d in game channel dbmw_mnt dbmw_guild dbmw_stat monitor manager bridge relay \
         stun auction point guild statics coserver community; do
  if [ -d "/home/neople/$d" ]; then
    exe=$(find "/home/neople/$d" -maxdepth 1 -type f -perm -u+x 2>/dev/null | head -3 | tr '\n' ' ')
    echo "  $d: ${exe:-（无可执行文件）}"
  else
    echo "  $d: （目录不存在）"
  fi
done

echo
echo "===== 2. game 日志目录 ====="
ls -la /home/neople/game/log/siroco11/ 2>/dev/null

echo
echo "===== 3. 上次退出原因（日志尾部）====="
for f in /home/neople/game/log/siroco11/*; do
  [ -f "$f" ] || continue
  echo "--- $(basename "$f") ($(wc -l < "$f") 行) ---"
  tail -25 "$f"
  echo
done

echo
echo "===== 4. pid 文件内容（看进程号，确认已死）====="
for p in /home/neople/game/pid/*.pid; do
  [ -f "$p" ] || continue
  pid=$(cat "$p" 2>/dev/null)
  alive=$(ps -p "$pid" -o comm= 2>/dev/null)
  echo "  $(basename "$p") = $pid  $([ -n "$alive" ] && echo "存活: $alive" || echo "（已死）")"
done
