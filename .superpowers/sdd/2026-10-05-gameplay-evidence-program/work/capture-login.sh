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
# ⚠️ **要不要限定 tcp？** 默认**不限定**（`port N` 同时抓 TCP 与 UDP）。
# 踩过的坑：dbmw 的 cfg 里 tcp_port 与 udp_port 是**同一个号**，且它真的同时监听两者；
# 第一次抓 dbmw 时我写了 `tcp port ...`，结果**只看到心跳、业务报文一个没抓到**
# （那批查询确实在抓包窗口内执行了 —— 日志有记录）。若要只要 TCP，设 PROTO=tcp。
PROTO="${PROTO:-}"
PCAP=/tmp/dfl/login-10013.pcap
LOG=/tmp/dfl/tcpdump.log
PIDF=/tmp/dfl/tcpdump.pid

FILTER=""
for p in $PORTS; do
  [ -n "$FILTER" ] && FILTER="$FILTER or "
  FILTER="${FILTER}${PROTO:+$PROTO }port $p"
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
      PID=$(cat $PIDF)
      echo "正在抓包 pid=$PID  大小=$(wc -c < "$PCAP" 2>/dev/null) B"
      # ⚠️ 过滤器要**从真实进程**读，不能重算 —— 启动时可用 $PORTS 覆盖，
      #    status 时若没设 $PORTS 就会打印默认值，造成"过滤器不对"的假象（踩过）。
      REAL=$(ps -eo args 2>/dev/null | grep "^/usr/sbin/tcpdump .* -w $PCAP" | head -1)
      echo "实际过滤器: ${REAL#*$PCAP }"
      echo "包数: $(/usr/sbin/tcpdump -r "$PCAP" 2>/dev/null | wc -l)"
    else
      echo "未在抓包"
      ls -la "$PCAP" 2>/dev/null || echo "(无 pcap)"
    fi
    ;;
esac
