#!/usr/bin/env bash
# 在 VM 上后台抓 10013 的包，等一次完整登录。
# 用法: capture-login.sh start | stop | status
set -u
PCAP=/tmp/dfl/login-10013.pcap
LOG=/tmp/dfl/tcpdump.log
PIDF=/tmp/dfl/tcpdump.pid

case "${1:-status}" in
  start)
    mkdir -p /tmp/dfl
    if [ -f "$PIDF" ] && kill -0 "$(cat $PIDF)" 2>/dev/null; then
      echo "已在抓包 (pid $(cat $PIDF))"; exit 0
    fi
    # -s 0 抓全量；-w 写盘。10013 是游戏面。
    /usr/sbin/tcpdump -i any -s 0 -w "$PCAP" 'tcp port 10013' > "$LOG" 2>&1 &
    echo $! > "$PIDF"
    sleep 2
    echo "抓包已启动 pid=$(cat $PIDF)"
    echo "文件: $PCAP"
    cat "$LOG" | head -5
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
    tail -5 "$LOG" 2>/dev/null
    echo "--- 包数 ---"
    /usr/sbin/tcpdump -r "$PCAP" 2>/dev/null | wc -l
    ;;
  status)
    if [ -f "$PIDF" ] && kill -0 "$(cat $PIDF)" 2>/dev/null; then
      echo "正在抓包 pid=$(cat $PIDF)  大小=$(wc -c < "$PCAP" 2>/dev/null) B"
      /usr/sbin/tcpdump -r "$PCAP" 2>/dev/null | wc -l
    else
      echo "未在抓包"
      ls -la "$PCAP" 2>/dev/null || echo "(无 pcap)"
    fi
    ;;
esac
