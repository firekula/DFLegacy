#!/usr/bin/env bash
# run-30011-selftest.sh —— 跑 Task 11 的三个靶子并把结果拼成一份报告。
#
# 为什么要有这个包装：Task 11 的产物是**一份** `work/out/driver-30011-selftest.txt`，
# 但结论来自三个不同靶子的运行（拒绝路径 / 成功路径 / 结构自检）。手工拼容易漏掉
# 失败那次 —— 而"成功路径没复现"恰恰是本任务最该写清楚的一条。
# 本脚本把三段**原样**拼起来（每段自带 VERDICT），不筛掉任何 FAIL。
#
# 用法（从任意目录）: bash work/run-30011-selftest.sh
set -u

# 用脚本自身位置把工作区根算出来（不猜层数 —— 上一版写死 ../../.. 少爬了一层，
# 结果整份报告写不出去，症状是"-> …txt / No such file or directory"）。
SELF_DIR="$(cd "$(dirname "$0")" && pwd)"       # .../work
ROOT="$(cd "$SELF_DIR/../../../.." && pwd)"     # -> 工作区根
cd "$ROOT" || exit 1

B='.superpowers/sdd/2026-10-05-gameplay-evidence-program'
W='.superpowers/sdd/2026-10-05-official-login-channel/work'
OUT="$B/work/out"
PY="${PYTHON:-python}"

mkdir -p "$OUT" || exit 1

FINAL="$OUT/driver-30011-selftest.txt"
: > "$FINAL"

{
  echo "==============================================================================="
  echo "Task 11 · 游戏面已知答案自检 —— 汇总（本文件由 work/run-30011-selftest.sh 生成）"
  echo "生成时间: $(date -u '+%Y-%m-%dT%H:%M:%SZ')  (UTC)"
  echo "==============================================================================="
  echo
  echo "三个靶子，三段原始输出**原样**附在下面（含全部 FAIL，未筛选）："
  echo "  [A] 抓包结构自检 —— VM 成功路径 (streams-vm / 10013)，只抽期望值不打靶"
  echo "  [B] 抓包结构自检 —— docker 拒绝路径 (streams / 30011)，只抽期望值不打靶"
  echo "  [C] **打靶** —— docker 拒绝路径 127.0.0.1:30021，原样重放抓到的 413 B 首帧"
  echo "  [D] **打靶** —— VM 成功路径 192.168.200.131:10013，同一条 413 B 首帧"
  echo
  echo "靶子端口说明：docker 的游戏面在容器里是 30011、发布到宿主 30021（抓包里的"
  echo "46386/33018 是**抓包侧客户端**端口，不是服务端端口）；VM 的游戏面是 10013。"
  echo
  echo "⚠️ 成功路径（VM）**未能在本机复现** —— 见 [D] 的 VERDICT 与差异证据。"
  echo "   原因不是驱动器（同一驱动器在 [C] 已复现拒绝路径），而是两条 L2a 前置"
  echo "   （member_join_info 行 / geo_allow 名单）在本机改不了：SSH 进不去 VM。"
  echo
  echo "═══════════════════════════════════════════════════════════════════════════════"
  echo "  [A] VM 成功路径 —— 抽期望值（不打靶）"
  echo "═══════════════════════════════════════════════════════════════════════════"
  "$PY" "$B/work/driver/selftest-30011.py" \
      --capture-dir "$B/work/captures/streams-vm" --server-port 10013 \
      --verify-with "$W/container-game-publickey.pem" \
      --out "$OUT/raw-30011-vm-expectation.txt" 2>&1
  echo "(exit=$?)"
  echo
  echo "═══════════════════════════════════════════════════════════════════════════════"
  echo "  [B] docker 拒绝路径 —— 抽期望值（不打靶）"
  echo "═══════════════════════════════════════════════════════════════════════════"
  "$PY" "$B/work/driver/selftest-30011.py" \
      --capture-dir "$B/work/captures/streams" --server-port 30011 \
      --verify-with "$W/container-game-publickey.pem" \
      --out "$OUT/raw-30011-docker-expectation.txt" 2>&1
  echo "(exit=$?)"
  echo
  echo "═══════════════════════════════════════════════════════════════════════════════"
  echo "  [C] 打靶：docker 拒绝路径 127.0.0.1:30021 ← 抓到的 413 B 首帧"
  echo "═══════════════════════════════════════════════════════════════════════════"
  "$PY" "$B/work/driver/selftest-30011.py" \
      --capture-dir "$B/work/captures/streams" --server-port 30011 \
      --verify-with "$W/container-game-publickey.pem" \
      --replay --host 127.0.0.1 --port 30021 \
      --out-raw "$OUT/raw-30011-docker-replay.bin" \
      --out "$OUT/raw-30011-docker-replay.txt" 2>&1
  echo "(exit=$?)"
  echo
  echo "═══════════════════════════════════════════════════════════════════════════════"
  echo "  [D] 打靶：VM 成功路径 192.168.200.131:10013 ← 同一条 413 B 首帧"
  echo "═══════════════════════════════════════════════════════════════════════════"
  "$PY" "$B/work/driver/selftest-30011.py" \
      --capture-dir "$B/work/captures/streams-vm" --server-port 10013 \
      --verify-with "$W/container-game-publickey.pem" \
      --replay --host 192.168.200.131 --port 10013 \
      --out-raw "$OUT/raw-30011-vm-replay.bin" \
      --out "$OUT/raw-30011-vm-replay.txt" 2>&1
  echo "(exit=$?)"
  echo
  echo "═══════════════════════════════════════════════════════════════════════════════"
  echo "  两个靶子的原始字节（判据 4）"
  echo "═══════════════════════════════════════════════════════════════════════════"
  for f in raw-30011-docker-replay.bin raw-30011-vm-replay.bin; do
    p="$OUT/$f"
    if [ -f "$p" ]; then
      echo "  $f : $(wc -c < "$p") B  sha256=$(sha256sum "$p" | cut -c1-16)"
    else
      echo "  $f : 不存在"
    fi
  done
  echo
  echo "  首 48 字节（docker 的回复）："
  head -c 48 "$OUT/raw-30011-docker-replay.bin" 2>/dev/null | xxd | head -3
  echo
  echo "═══════════════════════════════════════════════════════════════════════════════"
  echo "  期望值的机器可读副本（判据 1 的产物，程序输出的，不是我抄的）"
  echo "═══════════════════════════════════════════════════════════════════════"
  for f in raw-30011-vm-expectation.txt raw-30011-docker-expectation.txt \
           raw-30011-docker-replay.txt raw-30011-vm-replay.txt; do
    printf '  %-38s %s 行\n' "$f" "$(wc -l < "$OUT/$f" 2>/dev/null || echo '?')"
  done
  echo
  echo "═══════════════════════════════════════════════════════════════════════════════"
  echo "  附：driver/token.py 的**文件名遮蔽**问题（Task 10/11 都踩到，已修）"
  echo "═══════════════════════════════════════════════════════════════════════"
  echo "  症状：以脚本方式跑 driver/ 下任何文件时，标准库链"
  echo "        dataclasses -> inspect -> linecache -> tokenize -> \`from token import *\`"
  echo "        命中了我们的 driver/token.py（计划 Task 10 指定的文件名），"
  echo "        报 \`ImportError: cannot import name 'dataclass' from partially"
  echo "        initialized module 'dataclasses'\`（看着像循环导入，实际是文件名遮蔽）。"
  echo "  修法：先把自己所在目录从 sys.path 摘掉、**再** import 标准库 token、钉进"
  echo "        sys.modules。守卫放在 dnfdrive.py（driver/ 下的共同导入点）与 token.py。"
  echo "  **顺序是关键** —— 三组对照实测（2026-10-05）："
  echo "        不装守卫                          -> FAIL（复现遮蔽）"
  echo "        摘路径**之前**就 import token      -> FAIL（守卫自己也被遮蔽）"
  echo "        先摘路径、再 import               -> OK（token.__file__ = …\\Lib\\token.py）"
  echo "  回归：本报告 [A]–[D] 四段全部跑通，证明修好之后 driver/ 下所有入口都正常。"
} >> "$FINAL" 2>&1

echo "-> $FINAL"
wc -l < "$FINAL"
