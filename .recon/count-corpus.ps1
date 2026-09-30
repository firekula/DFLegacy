# 语料与检索键的实测计数（可复现）。口径写死在这里，corpus 名单**从配置读**，不内联。
#
#   pwsh -NoProfile -File .recon/count-corpus.ps1 [-SkipGitCheck]
#
# 依赖（脚本头必须声明，2026-09-30）：**node**（本机 D:\Programs\nvm\nodejs\node.exe，
# PATH 上可用）+ **yaml 模块**。PowerShell 没有内建 YAML 解析；这里走 node 而不是装
# powershell-yaml 模块，因为 node 已经是本模式在用的解释器（`{node}` 是内建占位符），
# 不引入新的安装状态。
#
# yaml 模块用**绝对路径 require**（node 从 cwd 解析，临时脚本放 %TEMP% 找不到它）。
# 解析顺序：$env:RECON_YAML_MODULE → recon 包自带的那份（yaml@2.9.1）。
# **找不到就报错退出**（不静默、不退回内联列表）—— 见下方"为什么不内联"。
# 若要换机器，设 RECON_YAML_MODULE 指向该机的 yaml 包目录即可。
#
# 为什么不内联 corpus 列表（2026-09-30 决定）：两个真相源记同一件事，失败模式是
# "corpus 改了、脚本没改 → 脚本成功、打印一个数字、而那个数字不再对应真实输入" ——
# 与 run_ida.sh 那个 ${@:3} 同类（exit 0、看起来正常、结果是错的）。
#
# 口径定义
# --------
# 1) glob 来自 .recon/recon.config.yml 的 corpus:（**唯一真相源**，本脚本不再内联）。
# 2) `**/` 允许匹配**零层**目录，因此 `a/**/*.md` 也含 a/ 下的直接子文件。
#    注意：PowerShell 的 -Include 不吃路径 glob，必须自己按段展开 —— 这是踩过的坑。
# 3) **重复条目去重**：重叠 glob 会命中同一文件多次，按规范化后的**绝对路径**去重，
#    并把同时命中多条 glob 的文件单独列出（.superpowers/sdd/*.md 曾被
#    .superpowers/sdd/**/*.md 完全包含，重复计了 19 个文件）。
# 4) 文件按**路径字典序**拼接，分隔符 "`n"（跨文件的正则命中因此是确定的）。
# 5) 两个规模口径都报，别混用：
#      CHARS = 解码后字符串长度（正则跑在字符串上，**检索键计数以此为准**）
#      BYTES = 文件字节数之和（磁盘量）。中文是多字节，两者差别很大
#      （例：STATUS.md 88,521 字节 = 46,092 字符）。
# 6) 另报"被 git 忽略的文件数"（git check-ignore）：这些知识资产不在版本控制里，删了就没。
#
# 硬失败（不许打印 0 当结果）：YAML 解析失败 / corpus 段缺失或为空 / glob 命中 0 个文件。
param(
    [switch]$SkipGitCheck
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$configPath = Join-Path $PSScriptRoot 'recon.config.yml'

# ---------------------------------------------------------------- 读 corpus（唯一真相源）
if (-not (Test-Path -LiteralPath $configPath)) {
    Write-Error "读不到 $configPath —— corpus 名单的唯一真相源缺失，拒绝出数字。"
    exit 3
}

if (-not (Get-Command node -ErrorAction SilentlyContinue)) {
    Write-Error "PATH 上没有 node —— 无法解析 recon.config.yml，拒绝出数字（见脚本头依赖说明）。"
    exit 3
}

$yamlCandidates = @()
if ($env:RECON_YAML_MODULE) { $yamlCandidates += $env:RECON_YAML_MODULE }
$yamlCandidates += 'D:\Project\DeepseekHarness\HarckerMode\packages\recon\node_modules\yaml'
$yamlModule = $yamlCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $yamlModule) {
    Write-Error ("找不到 yaml 模块。试过：`n  " + ($yamlCandidates -join "`n  ") +
        "`n设 `$env:RECON_YAML_MODULE 指向 yaml 包目录后重试；本脚本不会退回内联列表（那正是要消除的静默错数）。")
    exit 3
}
Write-Host "yaml 模块：$yamlModule"

$js = @'
const fs = require('fs');
const YAML = require(process.argv[3]);
const cfg = YAML.parse(fs.readFileSync(process.argv[2], 'utf8'));
const corpus = cfg && cfg.corpus;
if (!Array.isArray(corpus) || corpus.length === 0) {
  console.error('corpus 段缺失或为空');
  process.exit(4);
}
process.stdout.write(JSON.stringify(corpus));
'@
$jsPath = Join-Path $env:TEMP ("recon-corpus-{0}.js" -f ([guid]::NewGuid().ToString('N')))
Set-Content -LiteralPath $jsPath -Value $js -Encoding utf8
try {
    $raw = & node $jsPath $configPath $yamlModule 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Error "解析 $configPath 的 corpus: 失败（node 退出码 $LASTEXITCODE）：$raw"
        exit 3
    }
} finally {
    Remove-Item -LiteralPath $jsPath -ErrorAction SilentlyContinue
}

$globs = @($raw | ConvertFrom-Json)
if ($globs.Count -eq 0) { Write-Error "corpus 段为空，拒绝出数字。"; exit 3 }
Write-Host ("corpus globs 来自 recon.config.yml：{0} 条" -f $globs.Count)

# ---------------------------------------------------------------- glob 展开（见口径 2）
function Expand-CorpusGlob {
    param([string]$Root, [string]$Glob)
    $segs = $Glob -split '/'
    $dirs = @($Root)
    for ($i = 0; $i -lt $segs.Count - 1; $i++) {
        $seg = $segs[$i]
        $next = New-Object System.Collections.Generic.List[string]
        foreach ($d in $dirs) {
            if ($seg -eq '**') {
                [void]$next.Add($d)   # ** 匹配零层 → 当前目录自身也留下
                Get-ChildItem -LiteralPath $d -Directory -Force -Recurse -ErrorAction SilentlyContinue |
                    ForEach-Object { [void]$next.Add($_.FullName) }
            } else {
                Get-ChildItem -LiteralPath $d -Directory -Force -ErrorAction SilentlyContinue |
                    Where-Object { $_.Name -like $seg } |
                    ForEach-Object { [void]$next.Add($_.FullName) }
            }
        }
        $dirs = @($next | Sort-Object -Unique)
    }
    $leaf = $segs[-1]
    $out = New-Object System.Collections.Generic.List[string]
    foreach ($d in $dirs) {
        Get-ChildItem -LiteralPath $d -File -Force -Filter $leaf -ErrorAction SilentlyContinue |
            ForEach-Object { [void]$out.Add($_.FullName) }
    }
    $out
}

$set = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$perGlob = [ordered]@{}
$contrib = @{}
foreach ($g in $globs) {
    $found = @(Expand-CorpusGlob -Root $root -Glob $g)
    $perGlob[$g] = $found.Count
    foreach ($f in $found) {
        [void]$set.Add($f)
        if (-not $contrib.ContainsKey($f)) { $contrib[$f] = New-Object System.Collections.Generic.List[string] }
        if (-not $contrib[$f].Contains($g)) { [void]$contrib[$f].Add($g) }
    }
}

$files = @($set | Sort-Object)
if ($files.Count -eq 0) {
    Write-Error "corpus 的 $($globs.Count) 条 glob 一条都没命中文件 —— 拒绝把 0 当结果打印（多半是 glob 写错或路径基准不对）。"
    exit 3
}

# 单条 glob 命中 0 个也是错误：拼错的 glob 会**静默地什么都不贡献**，
# 总数看起来只少一点、脚本 exit 0 —— 正是"数字不再对应真实输入"那类失败。
# 允许空 corpus 条目就地写出，让配置作者自己决定。
$emptyGlobs = @($perGlob.Keys | Where-Object { $perGlob[$_] -eq 0 })
if ($emptyGlobs.Count -gt 0) {
    Write-Error ("这些 corpus glob 命中 0 个文件（拼写错误？路径基准不对？）：`n  " +
        ($emptyGlobs -join "`n  ") + "`n删掉它，或改对；不要把 0 当结果。")
    exit 3
}

Write-Host "== 语料 =="
foreach ($g in $perGlob.Keys) { Write-Host ("  {0,-72} {1,5}" -f $g, $perGlob[$g]) }
$naive = ($perGlob.Values | Measure-Object -Sum).Sum
Write-Host ("  朴素求和={0}  去重后={1}  重复计数={2}" -f $naive, $files.Count, ($naive - $files.Count))
$dupes = @($files | Where-Object { $contrib[$_].Count -gt 1 })
if ($dupes.Count -gt 0) {
    Write-Host "  ⚠️ 被多条 glob 同时命中的文件（任何天真索引器都会重复计数）："
    foreach ($d in $dupes) { Write-Host ("     {0}  <- {1}" -f (Split-Path $d -Leaf), ($contrib[$d] -join ' + ')) }
}

# ---------------------------------------------------------------- 读文件（见口径 4/5）
$parts = New-Object System.Collections.Generic.List[string]
$bytes = 0
$unreadable = 0
foreach ($f in $files) {
    try {
        [void]$parts.Add((Get-Content -LiteralPath $f -Raw -Encoding utf8))
        $bytes += (Get-Item -LiteralPath $f).Length
    } catch { $unreadable++; Write-Warning "读不了: $f" }
}
$text = $parts -join "`n"
Write-Host ("FILES={0}  CHARS={1}  BYTES={2}  UNREADABLE={3}" -f $files.Count, $text.Length, $bytes, $unreadable)

# ---------------------------------------------------------------- 检索键（与 keys.yml 的 patterns 一致）
$kinds = [ordered]@{
    'cmd'             = '\bCMD\s*(\d+)\b'
    'noti'            = '\bNOTI\s*(\d+)\b'
    'protocol'        = 'protocol\s+(\d+)'
    'client_function' = '\bsub_([0-9A-Fa-f]{6,8})\b'
    'server_symbol_D' = '\b(Dis[Pp]atcher_[A-Za-z0-9_]+)\b'
    'server_symbol_C' = '\b(C[A-Z][A-Za-z0-9]*::[A-Za-z0-9_]+)\b'
    'address'         = '\b(0x[0-9A-Fa-f]{6,8})\b'
    'field_offset'    = '\+\s*(0x[0-9A-Fa-f]{1,4})\b'
    'resource'        = '\b([a-z][a-z0-9_]*/[a-z][a-z0-9_]*/[a-z0-9_]+\.[a-z0-9]+)\b'
}

Write-Host ""
Write-Host "== 检索键 =="
Write-Host ("  {0,-18} {1,8} {2,8}" -f 'kind', 'hits', 'uniq')
foreach ($k in $kinds.Keys) {
    $ms = [regex]::Matches($text, $kinds[$k])
    $uniq = @($ms | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
    Write-Host ("  {0,-18} {1,8} {2,8}" -f $k, $ms.Count, $uniq.Count)
}

# ---------------------------------------------------------------- 版本控制（见口径 6）
if (-not $SkipGitCheck) {
    Write-Host ""
    if (Get-Command git -ErrorAction SilentlyContinue) {
        Push-Location $root
        try {
            $ignored = 0
            foreach ($f in $files) {
                $rel = $f.Substring($root.Length).TrimStart('\', '/') -replace '\\', '/'
                & git check-ignore -q -- $rel 2>$null
                if ($LASTEXITCODE -eq 0) { $ignored++ }
            }
            Write-Host ("== 版本控制 == 语料中 {0} 个文件被 git 忽略（不在版本控制里）" -f $ignored)
        } finally { Pop-Location }
    } else {
        Write-Host "== 版本控制 == 跳过（PATH 上没有 git）"
    }
}
