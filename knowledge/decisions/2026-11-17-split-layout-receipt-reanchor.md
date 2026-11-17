# C 项：校验收据按内容重锚（2026-11-17）

上一波 [transport fallback](2026-11-17-split-layout-transport-fallback-fix.md) 让源不可达不再阻断启动；本波处理 C 项：
`validated.json` 里的绝对安装根在换根/搬家后失配，导致每次启动都全量重算。

## 契约变化

改动前（`InstallationStore.HasValidatedReceipt`）：

```csharp
validated.Schema == 1
&& validated.Root == Root                       // 绝对路径相等
&& validated.ReceiptSha256 == ReceiptHash()
&& validated.Files.SequenceEqual(CriticalFiles(receipt))
```

改动后：**去掉根相等**，只保留收据哈希与全部关键文件（exe/dll/`DFLegacy.Bootstrap.ini`）的
size + 写入时间 + 创建时间比对。`ValidationReceipt.Root` 保留为诊断字段，不再参与判定；
下一次 `RecordValidation`（安装或完整校验后）会自然把根重写成当前值。

理由：搬目录/改名保留所有受管理文件身份；记录本身既未签名也不绑定机器，`root` 只是明文 JSON 的一个字段，
校验它并不增加安全性，却让每次换根都强制全量校验。

## 证据（先红后绿）

`tests/DFLegacy.UpdateCore.Tests/EngineTests.cs`：

- 新建完整安装 → `ValidateInstalledAsync` 记录 → `Directory.Move` 到新路径 → 在新路径 `ValidateInstalledAsync(progress)`。
  - 改动前：`progress.Count > 0`（全量重算）→ 用例红；
  - 改动后：`CanLaunch && progress.Count == 0`（复用记录）→ 绿。
- 安全护栏：移动后篡改 `DFLegacy.Client.dll`，断言 `!CanLaunch && progress.Count > 0`——新路径不会放过被改过的受管理内容。

命令：

```powershell
dotnet run --project tests/DFLegacy.UpdateCore.Tests -c Release -- --engine   # Engine: OK
DFLegacy.Launcher.Tests.exe --bootstrap-transport-tests                        # 2 assertions, 0 failures
```

## 未做与边界

- 仍保持硬失败：`InstallTransaction.HasPending`（半装事务）、收据哈希不符、关键文件身份不符、内容校验失败。
- 未改记录 schema（`schema:1` 不变），旧记录可直接被新代码读取。
- 本机环境限制：依赖 Restart Manager 的测试组（`RepairClientTests`、`TransactionTests`、`FileLockOwnerTests`）
  在该 WPF 测试宿主下 `RmStartSession` 返回 29，未执行；与本改动无关。

## 下一波（已获方向，未实现）

- **B（入口哈希软化为“延迟更新”）**：线上清单需要替换固定入口时，不抛致命错误，改为“本次不更新、保留本地安装、
  本地内容校验通过就进登录页”，并记录“等待新启动包”。不可软化：更新器绝不自行替换正在运行的入口；
  未完成事务与内容损坏仍硬失败。提示落点按最小内容先放 incident + 更新检查结果，不动登录页 UI。
- **S1（占用提示）**：`RepairClient.ReleaseLocksAsync` 先 `FindUnreadableFiles` 探测，无人占用直接返回；
  确有占用但 RM 失败时抛可读提示「请完全退出游戏后重试」。为可测试性需要一个可注入的 finder 委托，
  否则用例会在 RM 可用的机器上失败。
