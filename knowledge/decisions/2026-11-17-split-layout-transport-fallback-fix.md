# 拆分布局启动被拦：错误文本、根因与 A 项修复（2026-11-17）

范围：`v1.0.15` 分支 `codex/chapter-two-full-starter-upgrade` 的“游戏文件留根目录、启动器进 `client\`”改造。
本文接 [2026-11-17-split-layout-startup-blocker-evidence.md](2026-11-17-split-layout-startup-blocker-evidence.md)：
先记录实测错误文本，再记录 A 项（源不可达回落本地校验）的 RED→GREEN 与真机验证；B/C 项仍待决策。

## 基线

- 源码：`9774104ffa31979a3a738f9eb17aa83632d96c6e`，worktree 另有未提交的布局改造（未在本轮改动）。
- 测试根：`G:\tmp\dfo-v1015-flat15`（拆分布局；外层游戏文件 + `client\` 启动器）。
- SDK：.NET `10.0.402`；原生预编译 `artifacts/native/ijl15/Release/{DFLegacy.Bootstrap32.exe,DFLegacy.Client.dll,ijl15.dll}` 可用。
- 本机限制：Restart Manager 不可用（`RmStartSession` 返回 29）。`RepairClientTests` / `TransactionTests` 等依赖它的用例在本机跑不通，属环境限制，不是回归。

## 实测错误文本（原文）

| 阶段 | 文本 |
| --- | --- |
| `stage=bootstrap-auto-repair` | `System.IO.IOException: Received an unexpected EOF or 0 bytes from the transport stream.` ← `ReleaseProvider.FindAsync` ← `UpdateEngine.CheckAsync`（`RepairClient.RunAsync:137`） |
| `stage=bootstrap-auto-repair` | `System.IO.InvalidDataException: 此更新需要替换正在运行的固定入口。请使用新的完整启动包；当前入口和恢复副本已保留。` ← `BootstrapLayout.RequireStableEntry:53` |
| 原生框（`startup_prompt.cpp`） | `更新或自动修复未完成。请查看更新程序提示后重试；本地恢复副本已保留。`（`bootstrap.log`: `Verified updater failed.`） |
| `App.xaml.cs` | `启动校验失败` + 上述异常的 `Message` |

GUI 实测：`验证与修复客户端 / 正在检查客户端资源…` 先停约 86 s，随后回落原生提示框。

## 根因（A 项对应第一条）

1. **`UpdateEngine.CheckAsync` 的“源不可达”判定不完整。** 旧过滤器
   `ex is HttpRequestException or TimeoutException or OperationCanceledException`（`UpdateEngine.cs:23`）
   不包含 TLS 层抛出的 `IOException`。结果是：源不可达本应降级为 `IsOffline` 警告，却直接冒泡成启动失败。
   `CheckAsync` 后续本来就有“无目标 + 有可信旧安装 → 本地完整校验并允许启动”的分支（`UpdateEngine.cs:31-40`），
   只是被这条类型判断挡住。
2. 入口哈希规则与 x64/x86 入口现状冲突（B 项，见下文）。
3. `validated.json` 存绝对安装根，换根必然失配，导致每次都走完整校验腿（C 项）。

## A 项修复

**产品改动**（1 行 + 注释）：

```csharp
// src/DFLegacy.UpdateCore/UpdateEngine.cs:23
catch (Exception ex) when (ex is HttpRequestException or TimeoutException or IOException or OperationCanceledException)
```

语义：`provider.FindAsync` 期间的 `IOException` 归为“源不可达”。已有安装可完整校验时以 `IsOffline=true` 交接并提示
“未确认线上最新版本”；文件损坏或无可信安装时仍按原逻辑失败，不放过损坏安装。

**测试（TDD，先红后绿）**

- `tests/DFLegacy.UpdateCore.Tests/EngineTests.cs`：`MemoryProvider.TransportUnavailable` 抛
  `IOException("Received an unexpected EOF …")`，断言 `CanLaunch && IsOffline && Target is null`。
  旧过滤器下该用例在 `CheckAsync:21` 直接抛出 → RED；补 `IOException` 后 GREEN。
- `src/DFLegacy.Launcher/tests/BootstrapUpdateTests.cs::RunTransportFallbackAsync`：不经过 `RepairClient` 的自洽夹具
  （`InstallTransaction.Apply` + `BootstrapLayout.ValidateInstalledAsync` 建立完整收据），删掉 `validated.json`
  强制走完整校验腿，再以 `--headless` 走 `BootstrapUpdateClient.RunAsync`。撤掉修复时在
  `RepairClient.cs:137` 抛出 `IOException` → RED；补回后 GREEN。
- 运行入口：
  - `dotnet run --project tests/DFLegacy.UpdateCore.Tests -c Release -- --engine`（新增隔离入口，绕开本机不可用的 Restart Manager 用例）
  - `DFLegacy.Launcher.Tests.exe --bootstrap-transport-tests`（新增独立测试组）
  - 构建测试工程需要 `-p:BuildNativeAssets=false "-p:Ijl15PrebuiltPath=artifacts/native/ijl15/Release/ijl15.dll"`

**验证结果**：`Engine: OK`；`Bootstrap transport tests: 2 assertions, 0 failures`。

## 真机复验（同一测试根，重建 app/updater 后）

重建 `role=app`/`role=updater`（同一构建配方，`Version=1.0.15`、`SourceRevisionId=9774104…`），把
`DFLegacy.Bootstrap.ini`/`recovery.ini` 的 App/Updater 哈希与 `bootstrap/<updaterHash>.exe` 同步到新产物，然后正常双击外层 `DFOLauncher.exe`：

- TLS `IOException` **不再出现**；预检到达线上清单 `v1.0.15`；
- 新的失败点是 `RequireStableEntry`（B 项），即“目标清单的固定入口与本机 x86 入口不同”，
  跟 A 项无关；
- 说明：本次修复让“模型内可解析的源不可达”不再阻断启动；源可达但入口规则冲突仍会按设计拒绝。

## 现场恢复

- `G:\tmp\dfo-v1015-flat15`：App/Updater 恢复为 `8e9428d9…`/`677b5f4c…`，`Bootstrap.ini`/`recovery.ini`
  恢复原哈希，`validated.json` 恢复 0 字节，删除临时 `<newUpdaterHash>.exe`、备份目录与构建产物。
- 进程：无残留 `DFLegacy*` / `DFOLauncher*`。
- worktree：临时改过的 `App.xaml.cs` 已还原；本轮只保留 A 项产品改动与对应测试。

## 仍未解决（下一步需要决策）

- **B（入口/发布）**：目标清单的固定入口与现有安装的入口（x64 启动入口 vs x86 入口）不同，
  `RequireStableEntry` 会持续拒绝。必须决定：保持入口哈希不变发下一次启动包，或走已验证的
  `v1.0.15-bridge → v1.0.15` 迁移对。
- **C（收据健壮性）**：`validated.json` 的绝对根在换根后失配，导致每次启动都走完整校验腿。A 项让这条腿在离线时
  不再阻断启动，但“换根后仍复用已校验收据”仍未做。
- **环境限制**：Restart Manager 不可用，`RepairClientTests`/`TransactionTests` 等组未能在本机执行；
  本波只对改动契约相关的 `Engine` 与 `Bootstrap transport` 两组给出真实结果。
