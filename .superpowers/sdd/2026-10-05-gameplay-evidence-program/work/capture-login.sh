#!/usr/bin/env bash
# 在 VM 上后台抓包，等一次完整登录。
#
# 过滤：**10013（游戏面）+ 7001（频道）一起抓** —— 单抓 10013 会丢掉登录链的前半段，
# 而"令牌从哪来、经哪条连接进游戏面"正是要靠两段对齐才能回答的问题。
# 端口可用 $PORTS 覆盖（空格分隔，供临时实验用）。
#
# 用法: capture-login.sh start | stop | status
set -u
PORTS="${PORTS:-10013 7001}"
PCAP=/tmp/dfl/login-10013.pcap
LOG=/tmp/dfl/tcpdump.log
PIDF=/tmp/dfl/tcpdump.pid

FILTER=""
for p in $PORTS; do
  [ -n "$FILTER" ] && FILTER="$FILTER or "
  FILTER="${FILTER}tcp port $p"
done

case "${1:-status}" in
  start)
    mkdir -p /tmp/dfl
    if [ -f "$PIDF" ] && kill -0 "$(cat $PIDF)" 2>/dev/null; then
      echo "已在抓包 (pid $(cat $PIDF))；先 stop 再 start 才能换过滤器"
      exit 0
    fi
    rm -f "$PCAP"
    /usr/sbin/tcpdump -i any -s 0 -w "$PCAP" "$FILTER" > "$LOG" 2>&1 &
    echo $! > "$PIDF"
    sleep 2
    echo "抓包已启动 pid=$(cat $PIDF)"
    echo "过滤器: $FILTER"
    echo "文件  : $PCAP"
    head -3 "$LOG"
    ;;
  stop)
    if [ -f "$PIDF" ]; then
      PID=$(cat "$PIDF")
      kill -INT "$PID" 2>/dev/null
      sleep 2
      kill -9 "$PID" 2>/dev/null
      rm -f "$PIDF"
      echo "已停止"
    else
      echo "没有在抓"
    fi
    ls -la "$PCAP" 2>/dev/null
    echo "--- tcpdump 摘要 ---"
    tail -4 "$LOG" 2>/dev/null
    echo "--- 包数 ---"
    /usr/sbin/tcpdump -r "$PCAP" 2>/dev/null | wc -l
    ;;
  status)
    if [ -f "$PIDF" ] && kill -0 "$(cat $PIDF)" 2>/dev/null; then
      echo "正在抓包 pid=$(cat $PIDF)  大小=$(wc -c < "$PCAP" 2>/dev/null) B"
      echo "过滤器: $FILTER"
      echo "包数: $(/usr/sbin/tcpdump -r "$PCAP" 2>/dev/null | wc -l)"
    else
      echo "未在抓包"
      ls -la "$PCAP" 2>/dev/null || echo "(无 pcap)"
    fi
    ;;
esac
