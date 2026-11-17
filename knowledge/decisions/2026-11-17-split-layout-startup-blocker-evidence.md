# 拆分布局启动被拦：错误文本与根因证据（2026-11-17）

范围：`v1.0.15` 分支 `codex/chapter-two-full-starter-upgrade` 的“游戏文件留根目录、启动器进 `client\`”改造。
本条只记录**实测抓到的错误文本与证据**，不改代码、不宣布修法。

## 环境

- 分支提交：`9774104`（工作区另有未提交的布局改造；测量期间除临时插桩外未改动源码）。
- 测试根：`G:\tmp\dfo-v1015-flat15`（21:08 完成的原生迁移根；外层游戏文件 + `client\` 内启动器）。
- 启动器：`client\DFLegacy.Launcher.exe`（x86，389,120 B）、`client\DFLegacy.Launcher.App.exe`（x64，265,728 B，
  SHA-256 `8e9428d9…`）、`client\DFLegacy.Updater.exe`（x64，89,148,616 B，SHA-256 `677b5f4c…`）。
- 原生入口：外层 `DFOLauncher.exe`（x64，441,344 B，SHA-256 `23EAD713…`，21:03 构建，含 `layout.cpp` 根目录判定）。

## 抓取方法

不抑制弹窗：原文案只出现在两个模态框里。用**同一条代码路径的无头入口**取文本：

1. `App` 在 `stage=bootstrap-ui-validation` 的 catch 里 `Program.ReportError`，异常会落成
   `client\Logs\DFLegacy\Incidents\error-*.log`；
2. `BootstrapUpdateClient.RunEntryAsync` 的 catch 同样落成 `stage=bootstrap-auto-repair` incident；
3. 原生入口 `startup_prompt.cpp` 的“提示信息”框只是把 `Run()` 的固定一条文案显示出来，不含扩展成员。

```powershell
# 真实顺序：updater 从 %TEMP% 副本运行（不在安装根内），再托管到 App
$work = Join-Path $env:TEMP ('DFLegacy-bootstrap-probe-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
Copy-Item '<root>\client\DFLegacy.Updater.exe' (Join-Path $work 'DFLegacy.Updater.exe')
$psi = [Diagnostics.ProcessStartInfo]::new((Join-Path $work 'DFLegacy.Updater.exe'))
$psi.WorkingDirectory = $work; $psi.UseShellExecute = $false
$psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true; $psi.CreateNoWindow = $true
'--bootstrap-client','<root>\client','--','--headless','--self-test' | ForEach-Object { $psi.ArgumentList.Add($_) }
[Diagnostics.Process]::Start($psi).WaitForExit(150000)
```

## 抓到的错误文本（原文）

按出现顺序，同一次 boot 尝试里最多落三条：

| 阶段 | 文本 | 触发点 |
| --- | --- | --- |
| `stage=bootstrap-auto-repair` | `System.IO.IOException: Received an unexpected EOF or 0 bytes from the transport stream.` | `ReleaseProvider.FindAsync` ← `UpdateEngine.CheckAsync`（`RepairClient.RunAsync:137`），源 `gitcode / firekula/DFLegacy` |
| `stage=bootstrap-auto-repair` | `System.IO.InvalidDataException: 此更新需要替换正在运行的固定入口。请使用新的完整启动包；当前入口和恢复副本已保留。` | `BootstrapLayout.RequireStableEntry`（:`53`）；目标清单 `v1.0.15` 的 `DFLegacy.Launcher.exe` 与本机入口哈希不同 |
| `stage=bootstrap-ui-validation` | `启动校验失败` + 上面某个异常的 `Message` | `App.xaml.cs` catch；只有走到这一步才会弹这个框 |

GUI 实测（`DFOLauncher.exe` 正常双击路径）：

- 窗口 `验证与修复客户端 / 正在检查客户端资源…` 停留约 86 s → 再约 6 min 无变化；
- 结束后外层出现原生“提示信息”框，`bootstrap.log` 追加 `Verified updater failed.`，
  即 `main.cpp` 把 `exitCode==1` 映射为固定文案
  `更新或自动修复未完成。请查看更新程序提示后重试；本地恢复副本已保留。`
- 该根下**没有** `client\Logs\DFLegacy\launcher.log`：失败路径此前不写 launcher 日志，只剩 incident。

## 对照实验（决定性的那一个）

- 把 `client\.dflegacy\v4\validated.json` 的 `root` 从旧的 `G:\tmp\dfo-v1015-live3` 改成当前
  `G:\tmp\dfo-v1015-flat15\client`（其余不变），同一条无头入口跑完后输出
  `正在完整校验 787/787 …` 并正常结束——**本地已校验收据一致时，boot 预检可以完全离线通过，不需要联网**。
- 反证：这份根的 `validated.json` 的 `root` 指向前一份测试副本，`HasValidatedReceipt` 必然为 false，
  于是每次启动都会落到完整校验 → 联网 CheckAsync → 上面那条入口/网络错误。

## 由此确认的三条（不是猜测，都有对应代码行）

1. **预检把“读不到线上”当硬失败**：`RepairClient.RunAsync` 在 `NetworkDisabled=false` 时只走
   `UpdateEngine.CheckAsync`，没有任何“联网失败 → 回落本地完整校验”的分支；源不可达时启动直接失败。
2. **入口哈希规则和 x64 入口 / x86 入口的现状冲突**：目标清单的固定入口是 x64 启动入口，
   本机安装的是 x86 入口；`RequireStableEntry` 因此拒绝，必须走“已验证的启动包/迁移对”，不能靠原地更新。
3. **`validated.json` 是绝对根**：安装根换名/搬家（这次拆分布局必然换根）后，旧收据必然失配，
   每个换根的副本都会在第一次启动时无条件联网。

## 未验证

- 用户真实机器上的具体现象：本轮只在 `G:\tmp\dfo-v1015-flat15` 复现；真实目录的
  `validated.json` 根是否也失配、源是否可达，未取证。
- `client\Logs\DFLegacy\launcher.log` 在失败路径缺失属既有行为（成功交接才写）；本轮临时插桩已回退，未保留改动。

## 测量期间的临时改动（已还原）

- `App.xaml.cs` catch 里临时加过 `Program.WriteLauncherLog("Bootstrap validation failed: "+ex)`，用于确认失败是否走该分支；
  已 `git checkout --` 还原。
- 测试根临时替换过 `Launcher.App.exe` 与 `Bootstrap.ini`/`recovery.ini`；已还原为
  `LauncherSha256=8e9428d9…`、`UpdaterSha256=677b5f4c…`，`validated.json` 已恢复为原 0 字节状态。
