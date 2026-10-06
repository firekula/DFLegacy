#!/usr/bin/env pwsh
# wait-and-collect-dbmw.ps1 —— 后台等到下一批 dbmw 流量，然后自动收包 + 分析。
#
# 为什么：dbmw 的周期活动约每 31 分钟一次（实测 15:00:34 / 15:31:34 / 16:01:34 / 16:32:34）。
# 本次抓包用**不限协议**的过滤器，正是为了区分两个竞争假设：
#   A 业务走 UDP（我先前用 tcp 过滤器漏了）  → 会出现非 4202 的帧
#   B 那批查询是 dbmw 的**内部定时刷盘**     → 依然只有 4202
# 手动干等 15 分钟没意义，所以放后台。
param([int]$WaitSeconds = 1020)

$ErrorActionPreference = 'Continue'
$B = ".superpowers/sdd/2026-10-05-gameplay-evidence-program"
$env:PYTHONIOENCODING = 'utf-8'
if (-not $env:VM_PASS) { throw 'VM_PASS must be supplied securely via environment; no fallback.' }

Write-Output "=== 等待 $WaitSeconds 秒（覆盖下一个 ~17:03 的周期批次）==="
Start-Sleep -Seconds $WaitSeconds

Write-Output "=== 收包 ==="
python "$B\work\vmrun.py" "$B\work\capture-login.sh" stop 2>&1 | Select-Object -First 8

Write-Output "=== 取回 ==="
python "$B\work\vmget.py" "/tmp/dfl/login-10013.pcap" "$B\work\captures\dbmw-long.pcap" --binary 2>&1

Write-Output "=== 切流 ==="
python ".superpowers\sdd\2026-10-05-official-login-channel\work\pcap-streams.py" `
  "$B\work\captures\dbmw-long.pcap" "$B\work\captures\streams-dbmw3" 2>&1 | Select-Object -First 20

Write-Output "=== 协议分布（TCP vs UDP）==="
python -c @"
import sys
sys.path.insert(0, r'.superpowers\sdd\2026-10-05-official-login-channel\work')
from importlib.util import module_from_spec, spec_from_file_location
s = spec_from_file_location('ps', r'.superpowers\sdd\2026-10-05-official-login-channel\work\pcap-streams.py')
m = module_from_spec(s); sys.modules['ps']=m; s.loader.exec_module(m)
import struct
from collections import Counter
cnt = Counter()
udp = 0
for ts, lt, pkt in m.read_packets(r'$B\work\captures\dbmw-long.pcap'):
    o = m.l3_offset(lt, pkt)
    proto = pkt[o+9]
    cnt['TCP' if proto == 6 else ('UDP' if proto == 17 else str(proto))] += 1
print('  包协议分布:', dict(cnt))
"@

Write-Output "=== dbmw 帧分析（找非 4202 的帧）==="
foreach ($f in (Get-ChildItem "$B\work\captures\streams-dbmw3" -File -ErrorAction SilentlyContinue)) {
  python "$B\work\dbmw-frames.py" $f.FullName 2>&1 | Select-Object -First 14
}
Write-Output "=== 完成 ==="
