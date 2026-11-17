# B 项：入口变更从“致命”降级为“延迟更新”（2026-11-17）

上一波（[receipt reanchor](2026-11-17-split-layout-receipt-reanchor.md)）处理了 C；本波处理 B：
线上清单要求替换固定入口时，不再阻断启动。

## 旧行为

启动预检把 `BootstrapLayout.RequireStableEntry` 作为 `validateManifest` 传给 `RepairClient.RunAsync`
（`BootstrapUpdateClient.cs:62`）。只要线上清单的 `DFLegacy.Launcher.exe` 哈希与本机入口不同，就抛
`InvalidDataException: 此更新需要替换正在运行的固定入口…`，启动被拦（真机复验里 22:10 那次就是它）。
这条规则本身是安全规则：更新器绝不能替换正在运行的入口。

## 新行为

- 预检路径改用 `RequireStableEntryOrDefer`：清单校验仍走 `RequireManifest`（畸形清单继续硬失败），
  入口不稳时抛**专用的** `UpdateDeferredException`，不再抛通用 `InvalidDataException`。
- `BootstrapUpdateClient.RunAsync` 捕获它：记录 launcher 日志
  `Update deferred: release <v> requires a new startup package; keeping the installed entry.`，
  然后跑 `BootstrapLayout.ValidateInstalledAsync`：
  - 本地内容完整 → 正常交接登录页，`RepairOutcome` 返回
    「线上版本 <v> 需要新的启动包；已保留本地安装并完成本地校验。」；
  - 本地内容损坏 → 本地校验的异常照常抛出，绝不因为“延迟更新”放过坏安装。
- `RepairClient` 的 update incident 把这种情况记为 `deferred`（不是 `failed`），避免误报。
- `RequireStableEntry` 保留原语义给其它调用点：`FullStarterMigration.Run.cs:141`、
  `StartupEntryMigration.cs:61`、`V4UpdateHelper.cs:76/79/175`、测试
  `BootstrapUpdateTests.cs:156`（“changed native entry is rejected before repair”）——它们仍要求入口稳定。

## 证据（先红后绿）

`src/DFLegacy.Launcher/tests/BootstrapUpdateTests.cs::RunEntryDeferralAsync`
（自洽夹具：提交 sequence 3 的完整安装 → 线上目标是 sequence 4、且 `DFLegacy.Launcher.exe` 不同）：

- 改动前：`RequireStableEntry` 抛异常，用例在 `BootstrapUpdateClient.cs:62` 直接崩 → 红；
- 改动后：`3 assertions, 0 failures`，且断言：
  - 交接发生一次，`outcome.Message` 含“启动包”，本机入口字节未变；
  - 收据版本仍是 `v0.1.8-giteatest.3`，无 `pending` 事务；
  - 把 `DFLegacy.Client.dll` 改坏后再跑：不发生交接（延迟更新不放过坏安装）。

回归：`--bootstrap-transport-tests` 仍 `2 assertions, 0 failures`；`--engine` 仍 `Engine: OK`。

命令：

```powershell
DFLegacy.Launcher.Tests.exe --bootstrap-entry-deferral-tests
DFLegacy.Launcher.Tests.exe --bootstrap-transport-tests
dotnet run --project tests/DFLegacy.UpdateCore.Tests -c Release -- --engine
```

## 真机复验（部分，2026-11-17 22:33）

用 `74b9c04` 工作区重建 app/updater，替换到测试根 `G:\tmp\dfo-v1015-flat15`（描述符与 recovery 缓存同步），正常双击外层 `DFOLauncher.exe`：

- worker 日志：`Update deferred: release v1.0.15 requires a new startup package; keeping the installed entry.`
- update incident：`stage=independent-repair result=deferred`；
- 随后本地 `BootstrapLayout.ValidateInstalledAsync` 拒绝交接：
  `受管理文件或配置未通过完整校验，请修复后启动。` —— 因为本次替换的 App/Updater 字节与 `installed.json`
  里的旧哈希不一致，本地内容**确实**不完整。这正是第二道护栏（延迟更新不放过坏本地）在工作，不是回归。
- 因此“延迟更新 + 本地完整 → 进登录页”的成功路径**没有**在这次复验里跑通：要跑通需要一份与新二进制一致的收据
  （重写 `installed.json` 的清单字节），属测试夹具工作。现场已还原（App `8e9428d9…`、Updater `677b5f4c…`、
  描述符原哈希、`validated.json` 0 字节、删除临时缓存副本）。

## 未做

- 用户可见的“等待新启动包”提示目前只在 launcher 日志与 `RepairOutcome.Message` 里；登录页/更新检查 UI 未改
  （按“避免添加过多内容”的最小落点）。若要在界面上显示，需要决定放在登录页状态还是更新检查结果。
- 环境限制：依赖 Restart Manager 的测试组仍无法在该 WPF 测试宿主下运行（`RmStartSession` 29）。
- S1（占用提示）未做。
