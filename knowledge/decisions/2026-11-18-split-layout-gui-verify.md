# 拆分布局 GUI 真机验证（无人值守，2026-11-17 夜间）

## 夹具

`G:\tmp\dfo-v1014-gui-fixed` → 运行后改名 `G:\tmp\dfo-v1014-gui-fixed-renamed`：

- 底子：官方 v1.0.14 干净发行包（787 受管理文件），替换为本工作区 `5463dee` 的
  `DFLegacy.Launcher.App.exe`（`854962e8…`）与 `DFLegacy.Updater.exe`（`5fdd596b…`），
  入口 `DFLegacy.Launcher.exe` 保持官方原样。
- 安装收据：`tag=v1.0.14-r3`、`sequence=25`（高于线上 bridge 的 24，用来触发“线上更旧 → 保留本地”路径），
  分卷 48 MB，清单与本机文件逐一匹配；`DFOLauncher.exe` 为当前工作区原生入口。
- 起始 flat；无 `Logs`、无 `validated.json`。

## 结果 1：迁移 + 本地校验 + 登录页

- 无人值守确认迁移框：向 stub 的可见窗口（class `D…`）投 `WM_COMMAND(IDOK)`，迁移完成（外层变 `DFOLauncher.exe` + `client\`）。
- 预检：线上 `v1.0.15-bridge`（seq 24）**早于**本地 seq 25 → 走“保留本地版本”路径，
  完整校验 787 文件后交接；update incident `result=verified`。
- `client\Logs\DFLegacy\launcher.log`：`23:36:23 Bootstrap verified UI handoff; offline=False.`
- 登录窗可见且为经典第二章皮肤，标题 `地下城与勇士 · 第二章`，右下角版本 `v1.0.14`
  （见 [login-v1014.png](assets/2026-11-18-split-layout-gui-verify/login-v1014.png)）。

## 结果 2：C1 换根复用（决定性）

- 关闭进程，把整个安装目录改名为 `dfo-v1014-gui-fixed-renamed`（文件时间不变），再次双击外层 `DFOLauncher.exe`。
- **12.3 秒**出现同一个登录窗；`launcher.log`：`23:36:58 Bootstrap verified UI handoff; offline=True.`
  —— 走了 `TryValidateCompleted` 的已完成收据快路径，尽管 `validated.json` 里记的根还是旧路径。
  改动前该路径会因根不等而失配，转去联网校验（修复前的复现里就是这条腿在耗时间/被拦）。
- 登录后更新检查在 `23:37:02` 重写了 `validated.json`（根变成新路径）——这是
  `UpdateEngine.VerifyInstalled` 的正常记录行为，不是 C1 的写入（C1 只读）。

## 结果 3：B 的 GUI 路径仍被发布状态挡住（如实记录）

- 另造 `dfo-v1014-bridge-fixed`：把本地收据标成 `v1.0.15-bridge`/seq 24（`StartupEntryMigration.IsBridge` 为真），
  期望预检取到线上 `v1.0.15` 目标并触发“延迟更新”。
- 实际失败：`System.IO.InvalidDataException: 线上清单的同序号内容被替换，已停止更新。`
  （`UpdateEngine.cs:56`）——线上 `v1.0.15-bridge` 也是 seq 24，与本地伪造的 seq 24 同序号但清单字节不同。
  该检查是防同序号替换的硬规则，先于 `RequireStableEntryOrDefer`。
- 结论：在 `v1.0.15` 正式目标（或与本地字节一致的 bridge 清单）发布前，B 的 GUI 快乐路径无法在真机上构造；
  B 的行为由自动化集成用例覆盖（`--bootstrap-entry-deferral-tests`，3 断言）。

## 未验证

- S1 的弹窗（需要 RM 在该进程失败的真实场景；测试宿主 `RmStartSession` 返回 29）。
- B 的 GUI 快乐路径（原因见上）。

## 无人值守操作记录

- 迁移确认框无法用 UIA（自绘），用 `PostMessageW(WM_COMMAND, IDOK)` 到 class `D…` 的可见窗口完成；
  脚本：`.tmp/v1014-clean/run-fixture.ps1`。
- 截图：`powershell.exe` + `PrintWindow`（`.tmp/v1014-clean/capture-window.ps1`），cua 会话不可用。
