# S1：占用提示（Restart Manager 失败时）(2026-11-17)

## 问题

`RepairClient.ReleaseLocksAsync` 直接调用 `FileLockOwners.Find`（Restart Manager）。RM 失败时
原始 `Win32Exception` 一路上抛，启动前预检只显示原生固定文案“更新或自动修复未完成…”，
用户拿不到可执行的信息。

## 改动

- `ReleaseLocksAsync` 把 RM 的失败（`Win32Exception` / `IOException` / `InvalidOperationException`）
  包成 [`ClientFilesOccupiedException`](.tmp/upgrade-wt/src/DFLegacy.Launcher/Updates/FileLockOwners.cs)：
  「无法自动识别占用客户端文件的程序。请完全退出游戏（DNF.exe 与 DFLegacy.Launcher）后重试。」
  RM 正常时行为不变（点名 → 确认 → `CloseMainWindow`/`Kill`）。
- `BootstrapUpdateClient.RunEntryAsync` 为该类型单独 catch：记 incident（`bootstrap-files-occupied`），
  非 headless 时弹 `ClassicMessage` 显示上述文本，返回 1。理由：文件仍被占用时换一个 updater 也解决不了，
  所以这条可执行提示应当直接给玩家，而不是只进日志。
- 为可测试性，`ReleaseLocksAsync` 增加可选 `find` 委托（默认 `FileLockOwners.Find`），测试注入会抛
  `Win32Exception(29)` 的 finder。否则用例在 RM 可用的机器上会误判。

## 证据（先红后绿）

`BootstrapUpdateTests.RunOccupancyAsync`（`--bootstrap-occupancy-tests`）：

- 无占用 → finder 返回空，正常返回（不为“没人占用”弹框、不关进程）；
- finder 抛 `Win32Exception(29)` → 抛 `ClientFilesOccupiedException`，消息含「请完全退出游戏」。
  改动前：直接抛 `Win32Exception`，用例崩溃（`RepairClient.cs:182`，已观察）。

回归：`--bootstrap-entry-deferral-tests`（3/0）、`--bootstrap-transport-tests`（2/0）、`--engine`（OK）。

## 边界

- 只包“识别占用者”这一步；真正的文件写入失败仍按原有 sharing-violation 路径报错。
- `FindUnreadableFiles` 未接入本流程：它只判断“能否以共享只读打开”，与“能否替换”不完全等价，
  接入会改变现有“点名并关闭”的行为，留给后续评估。
- 环境限制：依赖 Restart Manager 的完整测试组仍无法在该 WPF 测试宿主运行（`RmStartSession` 29）。
