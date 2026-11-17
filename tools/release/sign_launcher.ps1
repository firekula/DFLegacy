[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)][string[]] $Files,
    [string] $TimestampUrl,
    [switch] $MachineStore,
    [switch] $DebugCertificate
)
$ErrorActionPreference = 'Stop'
if ($DebugCertificate) {
    Write-Warning '调试证书模式：签名不受玩家 Windows 信任，不能降低 SmartScreen/杀软告警，发行说明必须明确标注“调试签名，不受信任”。'
}
if (-not $TimestampUrl) {
    $TimestampUrl = if ($env:DFLEGACY_SIGN_TIMESTAMP) { $env:DFLEGACY_SIGN_TIMESTAMP } else { 'http://timestamp.digicert.com' }
}
$pfx = $env:DFLEGACY_SIGN_PFX
$pfxPassword = $env:DFLEGACY_SIGN_PFX_PASSWORD
$thumbprint = $env:DFLEGACY_SIGN_THUMBPRINT
if (-not $pfx -and -not $thumbprint) {
    throw '需要受信任的 OV/EV 代码签名证书：设置 DFLEGACY_SIGN_PFX 和 DFLEGACY_SIGN_PFX_PASSWORD，或 DFLEGACY_SIGN_THUMBPRINT（可选 -MachineStore）。'
}
if ($pfx) {
    if (-not (Test-Path -LiteralPath $pfx -PathType Leaf)) { throw "代码签名 PFX 不存在：$pfx" }
    if (-not $pfxPassword) { throw '已设置 DFLEGACY_SIGN_PFX，但缺少 DFLEGACY_SIGN_PFX_PASSWORD。' }
}
foreach ($file in $Files) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "待签名文件不存在：$file" }
}
$sdkRoot = (Get-ItemProperty -LiteralPath 'HKLM:/SOFTWARE/Microsoft/Windows Kits/Installed Roots' -ErrorAction SilentlyContinue).KitsRoot10
if (-not $sdkRoot) { throw '找不到 Windows SDK（签名需要 signtool.exe）。' }
$signtool = Get-ChildItem -LiteralPath (Join-Path $sdkRoot 'bin') -Directory |
    Where-Object { $_.Name -match '^\d+\.\d+\.\d+\.\d+$' } |
    Sort-Object { [version]$_.Name } -Descending |
    ForEach-Object { Join-Path $_.FullName 'x64/signtool.exe' } |
    Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
    Select-Object -First 1
if (-not $signtool) { throw '找不到 signtool.exe（Windows SDK 10）。' }
foreach ($file in $Files) {
    if ($pfx) {
        if ($DebugCertificate) {
            & $signtool sign /fd sha256 /f $pfx /p $pfxPassword $file
        } else {
            & $signtool sign /fd sha256 /tr $TimestampUrl /td sha256 /f $pfx /p $pfxPassword $file
        }
    } else {
        $signArgs = @('sign', '/fd', 'sha256')
        if (-not $DebugCertificate) { $signArgs += @('/tr', $TimestampUrl, '/td', 'sha256') }
        $signArgs += @('/sha1', $thumbprint)
        if ($MachineStore) { $signArgs += '/sm' }
        $signArgs += $file
        & $signtool @signArgs
    }
    if ($LASTEXITCODE -ne 0) { throw "签名失败：$file（signtool sign 退出码 $LASTEXITCODE）" }
    if (-not $DebugCertificate) {
        & $signtool verify /pa /all $file
        if ($LASTEXITCODE -ne 0) { throw "签名验证失败：$file（需要受信任的证书链）" }
    }
    $signature = Get-AuthenticodeSignature -LiteralPath $file
    if (-not $signature.SignerCertificate) { throw "签名缺失：$file" }
    if ($DebugCertificate) {
        Write-Output "debug-signature $file status=$($signature.Status) subject=$($signature.SignerCertificate.Subject)"
    }
    if ($signature.SignerCertificate.Subject -notmatch 'FireKula') {
        Write-Warning "签名主体不是 FireKula：$($signature.SignerCertificate.Subject)"
    }
    $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
    Write-Output "signed $file sha256=$hash"
}
