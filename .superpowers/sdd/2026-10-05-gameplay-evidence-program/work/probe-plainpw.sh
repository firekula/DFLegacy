#!/usr/bin/env bash
# 只读：找那个**明文**口令属于哪个账号/哪个库，并试连。
set -u
SOCK=/opt/lampp/var/mysql/mysql.sock

echo "===== 1. 明文口令出现在哪个配置文件、配的什么账号 ====="
grep -rln 'uu5!\^%jg' /home/neople/*/cfg/ 2>/dev/null | while read -r f; do
  echo "--- $f ---"
  grep -nE 'db_|acc|user|pwd|name|ip|port' "$f" | head -20
  echo
done

echo "===== 2. 那个文件的完整内容（短的话）====="
for f in $(grep -rln 'uu5!\^%jg' /home/neople/*/cfg/ 2>/dev/null); do
  echo "--- $f ($(wc -l < "$f") 行) ---"
  cat "$f"
  echo
done

echo "===== 3. 用这个明文口令试连（只试配置里出现的账号名）====="
for ACC in game root; do
  out="$(mysql -u"$ACC" -p'REDACTED_VM_DB_PASSWORD' --socket=$SOCK -e \
        'select current_user() as who; show databases;' 2>&1)"
  if echo "$out" | grep -q ERROR; then
    echo "  [失败] $ACC / REDACTED_VM_DB_PASSWORD -> $(echo "$out" | grep -m1 ERROR)"
  else
    echo "  [成功] $ACC / REDACTED_VM_DB_PASSWORD"
    echo "$out" | grep -v 'Using a password' | sed 's/^/         /'
  fi
done
