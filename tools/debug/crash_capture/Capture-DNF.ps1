[CmdletBinding()]
param(
    [ValidateSet('monitor','snapshot')][string]$Mode = 'monitor',
    [int]$GameProcessId = 0
)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
try {
    $captureExe = Join-Path $PSScriptRoot 'DFLegacy.CrashCapture.exe'
    if (-not (Test-Path -LiteralPath $captureExe -PathType Leaf)) {
        throw '请先完整解压诊断包，再运行。'
    }
    $games = @(Get-Process -Name DNF -ErrorAction SilentlyContinue | Where-Object {
        try { $_.Path -and (Split-Path $_.Path -Leaf) -ieq 'DNF.exe' } catch { $false }
    })
    if ($GameProcessId -gt 0) { $games = @($games | Where-Object Id -eq $GameProcessId) }
    if ($games.Count -eq 0) {
        throw '没有找到可读取的 DNF.exe。请先启动游戏并进入城镇，再运行；若游戏以管理员启动，诊断程序也需要以管理员启动。'
    }
    if ($games.Count -gt 1) {
        $games | Select-Object Id,Path | Format-Table -AutoSize | Out-Host
        $selection = Read-Host '运行了多个游戏，请输入要抓取的游戏 PID'
        $chosen = 0
        if (-not [int]::TryParse($selection, [ref]$chosen)) { throw 'PID 必须是上表中的数字。' }
        $games = @($games | Where-Object Id -eq $chosen)
        if ($games.Count -ne 1) { throw '没有选择到对应的游戏进程。' }
    }
    $game = $games[0]
    $clientDirectory = Split-Path $game.Path -Parent
    $session = '{0}-{1}-{2}' -f (Get-Date -Format 'yyyyMMdd-HHmmss-fff'),$game.Id,$Mode
    $reportDirectory = Join-Path (Join-Path $PSScriptRoot 'CrashReports') $session
    New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
    $fileFacts = @()
    foreach ($name in @('DNF.exe','DFLegacy.Client.dll','Script.pvf','client-package.json')) {
        $path = Join-Path $clientDirectory $name
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            $item = Get-Item -LiteralPath $path
            $stream = [IO.File]::OpenRead($path)
            $hasher = [Security.Cryptography.SHA256]::Create()
            try { $hash = [BitConverter]::ToString($hasher.ComputeHash($stream)).Replace('-','') }
            finally { $hasher.Dispose(); $stream.Dispose() }
            $fileFacts += [pscustomobject]@{Name=$name;Bytes=$item.Length;SHA256=$hash}
        }
    }
    [pscustomobject]@{
        CaptureVersion=1; Time=(Get-Date -Format o); Mode=$Mode; ProcessId=$game.Id
        OS=[Environment]::OSVersion.VersionString; ClientDirectory=$clientDirectory; Files=$fileFacts
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $reportDirectory 'client-info.json') -Encoding UTF8
    $manifest = Join-Path $clientDirectory 'client-package.json'
    if (Test-Path -LiteralPath $manifest) { Copy-Item -LiteralPath $manifest -Destination $reportDirectory }
    function Copy-LogTails {
        foreach ($name in @('launcher.log','rs_log.txt')) {
            $source = Join-Path (Join-Path $clientDirectory 'Logs\DFLegacy') $name
            if (Test-Path -LiteralPath $source -PathType Leaf) {
                try { Get-Content -LiteralPath $source -Tail 150 | Set-Content -LiteralPath (Join-Path $reportDirectory $name) -Encoding UTF8 }
                catch { Write-Host "读取 $name 失败，但仍可抓取异常。" }
            }
        }
    }
    Copy-LogTails
    Write-Host "报告目录：$reportDirectory" -ForegroundColor Cyan
    if ($Mode -eq 'monitor') {
        Write-Host '出现 MONITOR READY 后，返回游戏尝试复现。弹出崩溃提示后先保留游戏，等这里显示 dump saved，再按 Q 结束抓取。'
        Write-Host '仅有 dump saved 不代表游戏已崩溃；正常处理的异常也会记录。请在真正复现后结束，并记录发生时间。'
    }
    & $captureExe $Mode $game.Id $reportDirectory
    $captureExit = $LASTEXITCODE
    Copy-LogTails
    if ($captureExit -ne 0) { throw "抓取工具退出码为 $captureExit，请保留上面的错误及报告目录。" }
    Write-Host "已结束。请将 $reportDirectory 整个文件夹压缩后反馈。" -ForegroundColor Green
} catch {
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
