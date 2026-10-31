#!/usr/bin/env bash
# 只读：查那两条 L2a 前置条件的现状 + 令牌 uid 与 m_id 的对应关系。
# 凭据来源：/home/neople/bridge/cfg/bridge.cfg 的 db_id/db_pwd（明文），非猜测。
# 全部 SELECT。
set -u
MYSQL="mysql -ugame -p'REDACTED_VM_DB_PASSWORD' --socket=/opt/lampp/var/mysql/mysql.sock --table --default-character-set=utf8"

echo "===== 1. accounts：用户名 -> m_id 映射（令牌里的 uid 是哪个？）====="
$MYSQL -e "select m_id, accountname, nickname from d_taiwan.accounts order by m_id limit 25;" 2>&1 | grep -v 'Using a password'

echo
echo "===== 2. member_join_info 现状（前置条件 1：有行就会 PK 冲突）====="
$MYSQL -e "select * from d_taiwan.member_join_info;" 2>&1 | grep -v 'Using a password'

echo
echo "===== 3. geo_allow 现状（前置条件 2：IP 白名单）====="
$MYSQL -e "select * from d_taiwan.geo_allow;" 2>&1 | grep -v 'Using a password'

echo
echo "===== 4. 探针账号实际能进的各个库里的账号表（找 uid=2/3 对应谁）====="
$MYSQL -e "show tables from taiwan_login;" 2>&1 | grep -v 'Using a password'
