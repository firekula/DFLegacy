# 采集 DNF.exe 客户端线程栈（只读进程内存，不改客户端）。
#
# 这是客户端侧取证入口：客户端崩溃/卡死时，唯一能落盘的证据是崩溃前的线程栈。
# 采样器本体是 .superpowers/sdd/2026-09-29-dungeon-party/work/watch_clients.py
# （通过 wow64_stack.py 用 Wow64GetThreadContext 读 32 位线程上下文；64 位宿主
# 直接 GetThreadContext 会拿到 Eip=0 的假上下文）。
#
# 用法（cwd = 项目根）：
#   pwsh -NoProfile -File .recon/collect-dnf-stacks.ps1 -Seconds 60
#   pwsh -NoProfile -File .recon/collect-dnf-stacks.ps1 -Seconds 30 -Pid 1234
#
# 产物：.recon/captures/stacks_<pid>.txt
#   content = 每个采样时刻的 mem/IO 计数 + 每线程 tid/eip/esp/ebp + 帧链
#   随后用 analyze_hang.py 找"冻结"调用链、用 scan_exception.py 找 EXCEPTION_RECORD。
#
# 约束：
#   * 需要客户端正在运行；没有 DNF.exe 时直接失败（退出码 2），不空等到超时。
#   * -Seconds 有上限（默认 60），避免无人值守时无限采样。
#   * 只读；不挂调试器、不写客户端进程、不装驱动。
param(
    [Parameter(Mandatory = $true)][int]$Seconds,
    # ⚠️ 这个参数**不能**叫 -Pid：$PID 是 PowerShell 的只读自动变量（当前 shell 的进程 id），
    # 而变量名大小写不敏感 → param() 绑定阶段就抛"无法覆盖变量 Pid"，脚本一行都跑不到。
    # 这不是"里面别用 $Pid 就行"的问题，是名字本身不能用。故取名 -TargetPid。
    [int]$TargetPid = 0,
    [int]$Exclude = 0,
    [int]$Interval = 2
)

$ErrorActionPreference = 'Stop'

# 输出用 UTF-8：否则中文在默认代码页的控制台里变成乱码（本模式的探测输出就是按原样回显的）。
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$OutputEncoding = [System.Text.UTF8Encoding]::new($false)

$work = Join-Path $PSScriptRoot '..\.superpowers\sdd\2026-09-29-dungeon-party\work'
$work = (Resolve-Path -LiteralPath $work).Path
$outDir = Join-Path $PSScriptRoot 'captures'

if ($Seconds -lt 1 -or $Seconds -gt 300) { throw "-Seconds 必须在 1..300（拿到 $Seconds）" }

# 前置条件不满足时用 exit 2（而不是 Write-Error）：Write-Error 会让 pwsh 以 1 退出，
# 调用方就分不清"没有客户端在跑"（预期情形）和"脚本本身坏了"。
$target = 0
if ($TargetPid -le 0) {
    $rows = @(Get-Process -Name 'DNF' -ErrorAction SilentlyContinue | Where-Object { $_.Id -ne $Exclude })
    if ($rows.Count -eq 0) {
        Write-Host 'PRECONDITION: 没有正在运行的 DNF.exe —— 先启动客户端再采样（本工具不负责启动客户端）。'
        exit 2
    }
    if ($rows.Count -gt 1) {
        Write-Host ("PRECONDITION: 发现多个 DNF.exe " + (($rows | ForEach-Object { $_.Id }) -join ', ') + " —— 请用 -TargetPid 指定要采的那个。")
        exit 2
    }
    $target = $rows[0].Id
} else {
    $proc = Get-Process -Id $TargetPid -ErrorAction SilentlyContinue
    if (-not $proc) { Write-Host "PRECONDITION: PID $TargetPid 不存在。"; exit 2 }
    if ($proc.ProcessName -ne 'DNF') { Write-Host "PRECONDITION: PID $TargetPid 是 $($proc.ProcessName)，不是 DNF.exe。"; exit 2 }
    $target = $TargetPid
}

New-Item -ItemType Directory -Force -Path $outDir | Out-Null
Write-Host "sampling pid $target for $Seconds s (interval ${Interval}s) -> $outDir"

# watch_clients.py 支持 --pid 过滤，避免它在采样期间把别的 DNF.exe 也写进来。
$minutes = [math]::Round($Seconds / 60.0, 4)
& python (Join-Path $work 'watch_clients.py') --pid $target $minutes $outDir $Interval
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$produced = Get-ChildItem -LiteralPath $outDir -Filter "stacks_$target.txt" -ErrorAction SilentlyContinue
if (-not $produced) {
    Write-Host "FAILED: 采样结束但没有产物 $outDir\stacks_$target.txt"
    exit 3
}
Write-Host ("collected {0} bytes -> {1}" -f $produced.Length, $produced.FullName)
