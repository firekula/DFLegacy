#!/usr/bin/env bash
# 只读：找出能连 d_taiwan 的凭据。
#
# 纪律说明：**只试从配置里读到的值**，不做口令爆破/字典尝试。
# 候选来自：/home/neople/*/cfg/*.cfg 的 master_db_acc/master_db_pwd，
#           以及 /root/.mysql_history 里那条可见的 GRANT 语句。
# 上一版脚本里我写了 `-proot` 这种"随便试试"，等于弱口令尝试，已移除。
set -u

HOST=127.0.0.1
SOCK=/opt/lampp/var/mysql/mysql.sock
DBPW_FROM_CFG='20e35501e56fcedbe8b10c1f8bc3595be8b10c1f8bc3595b'

probe() {
  local label="$1"; shift
  local out
  # 注意：必须在这里就地重定向，否则管道只给最后一条命令接 stderr，错误会被吞掉
  out="$(mysql "$@" -e 'select current_user() as who, database() as db;' 2>&1)"
  if echo "$out" | grep -q 'ERROR'; then
    echo "  [失败] $label"
    echo "$out" | grep -m1 'ERROR' | sed 's/^/         /'
  else
    echo "  [成功] $label"
    echo "$out" | grep -v 'Using a password' | sed 's/^/         /'
  fi
}

echo "===== A. 走 socket（会被认成 localhost）====="
probe "root + 配置里的 pwd"        -uroot -p"$DBPW_FROM_CFG" --socket=$SOCK
probe "game + 配置里的 pwd"        -ugame -p"$DBPW_FROM_CFG" --socket=$SOCK

echo
echo "===== B. 走 TCP（会被认成 127.0.0.1）====="
probe "root + 配置里的 pwd"        -h$HOST -P3306 -uroot -p"$DBPW_FROM_CFG"
probe "game + 配置里的 pwd"        -h$HOST -P3306 -ugame -p"$DBPW_FROM_CFG"

echo
echo "===== C. 该口令的形态（是明文还是哈希？）====="
echo -n "  长度: "; echo -n "$DBPW_FROM_CFG" | wc -c
echo -n "  是否纯 hex: "; echo "$DBPW_FROM_CFG" | grep -qE '^[0-9a-f]+$' && echo "是（像哈希，不像明文）" || echo "否"
echo -n "  是否单个 SHA1 (40 hex): "; [ ${#DBPW_FROM_CFG} -eq 40 ] && echo "是" || echo "否"
echo -n "  MySQL 老式 16 hex 口令哈希: "; [ ${#DBPW_FROM_CFG} -eq 16 ] && echo "是" || echo "否"

echo
echo "===== D. 官服配置里所有 db 段（确认没有别的库/账号）====="
grep -rhiE 'master_db_(acc|pwd|name|ip|port)|db_acc|db_pwd|db_name|db_ip' \
  /home/neople/*/cfg/*.cfg 2>/dev/null | sort -u

echo
echo "===== E. XAMPP 自己的 my.cnf 有没有 skip-grant 之类 ====="
grep -iE 'skip-grant|skip_networking|bind-address|old_passwords|datadir' /opt/lampp/etc/my.cnf 2>/dev/null || echo "(无相关项)"
