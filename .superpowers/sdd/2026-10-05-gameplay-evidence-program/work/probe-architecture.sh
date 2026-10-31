#!/usr/bin/env bash
# 只读：把官服**进程/端口/配置/互相依赖**一次摸全。这是"重写怎么切分"的事实基础。
set -u

echo "########## A. 当前在跑的进程（按二进制名归并）##########"
ps -eo pid,etime,cmd --sort=pid 2>/dev/null | grep -E 'df_|gunnersvr|zergsvr|secagent|mysqld|httpd' | grep -v grep

echo
echo "########## B. 每个进程的监听端口 ##########"
netstat -lntup 2>/dev/null | grep -E 'df_|gunner|zerg|secagent|mysqld|httpd' | awk '{print $1, $4, $7}' | sort -u

echo
echo "########## C. 各服务的 cfg：监听端口 + 依赖的对端 ##########"
for d in /home/neople/*/; do
  b=$(basename "$d")
  cfgs=$(ls "$d"cfg/*.cfg "$d"cfg/*.xml 2>/dev/null | head -3)
  [ -z "$cfgs" ] && continue
  echo "=== $b ==="
  for f in $cfgs; do
    echo "  --- $(basename "$f") ---"
    grep -iE 'port|ip|_acc|_pwd|_name|type|no *=' "$f" 2>/dev/null | head -22 | sed 's/^/    /'
  done
done

echo
echo "########## D. 目录结构（每个服务的文件构成）##########"
for d in /home/neople/*/; do
  b=$(basename "$d")
  exe=$(find "$d" -maxdepth 1 -type f -perm -u+x 2>/dev/null | head -1 | xargs -r basename)
  n_cfg=$(ls "$d"cfg 2>/dev/null | wc -l)
  n_tbl=$(ls "$d"table 2>/dev/null | wc -l)
  has_log=$([ -d "$d"log ] && echo yes || echo no)
  has_pid=$([ -d "$d"pid ] && echo yes || echo no)
  printf "  %-14s exe=%-14s cfg=%s table=%s log=%s pid=%s\n" "$b" "${exe:-—}" "$n_cfg" "$n_tbl" "$has_log" "$has_pid"
done
