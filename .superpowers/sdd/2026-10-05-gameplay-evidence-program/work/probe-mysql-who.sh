#!/usr/bin/env bash
# 只读：查清 3306 上这个 mysqld 的来历、账号、以及 game 用户的口令哈希形态。
echo "===== 1. mysqld 进程与二进制 ====="
ps -eo pid,cmd | grep -E 'mysqld' | grep -v grep
echo "--- /opt/lampp 下的 bin ---"
ls -la /opt/lampp/bin/mysql* 2>/dev/null
ls -la /opt/lampp/sbin/mysqld 2>/dev/null

echo
echo "===== 2. 库的物理目录（判断装过哪些）====="
ls -la /opt/lampp/var/mysql/ 2>/dev/null | head -30
echo "--- 有没有 d_taiwan 目录 ---"
ls -d /opt/lampp/var/mysql/d_taiwan 2>/dev/null || echo "(/opt/lampp/var/mysql 下没有 d_taiwan)"
ls -d /var/lib/mysql/d_taiwan 2>/dev/null || echo "(/var/lib/mysql 下没有 d_taiwan)"

echo
echo "===== 3. 枚举账号与口令哈希（随便挑一个能进的入口）====="
for CRED in "-uroot" "-uroot -p88888888" "-uroot -p20e35501e56fcedbe8b10c1f8bc3595be8b10c1f8bc3595b"; do
  echo "--- 尝试: mysql $CRED ---"
  mysql $CRED --socket=/opt/lampp/var/mysql/mysql.sock -e \
    'select user,host,password from mysql.user;' 2>&1 | grep -v 'Using a password' | head -12
done
echo "--- 也试 root/root ---"
mysql -uroot -proot --socket=/opt/lampp/var/mysql/mysql.sock -e 'select 1;' 2>&1 | head -3

echo
echo "===== 4. 历史文件里有没有别的口令 ====="
for f in /root/.mysql_history /home/neople/*/.mysql_history; do
  [ -f "$f" ] && { echo "--- $f ---"; grep -iE 'password|identified|grant|connect|-p' "$f" | head -20; }
done
