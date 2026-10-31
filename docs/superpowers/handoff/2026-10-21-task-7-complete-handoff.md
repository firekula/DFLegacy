# Task 7 完成交接 · 链路服务端 + 端到端连通（2026-10-21）

> **[历史]** I3 后半的结论仍有效，但**当前视图**已移到 `2026-10-21-task-8-complete-handoff.md`
> （Task 8 起 BASE 为本次提交；遗留项当前表以那份 §6 为准）。

> 当前视图。前两版 `2026-10-21-task-5-complete-handoff.md`（I2）与 `2026-10-21-task-6-complete-handoff.md`（I3 前半）
> 记录历史，结论仍有效。
> 本波只做 Task 7。Task 8–11 一行未动，全部仍 `needs-refresh`。
> 换会话续跑：先读本文件 §2–§6，再按标题定位计划的 `## Task 8:` 段。

## 1. 基线、提交与工作区

- 仓库 `G:/Game/DNF_ACT1/DFLegacy`，分支 `main`。
- 本波 BASE `388ed00`（Task 6 提交），HEAD 见 `git log -1 -- src/DFLegacy.InterProcess/InterProcessLinkServer.cs`。
  5 文件，+685/−16。
- 本波**没有**改共享文件：`tests/DFLegacy.SmokeTests/Program.cs` 与 `DFLegacy.Emulator.slnx` 的他人 hunk 全程未动，
  提交后仍显示为未暂存（NAT/NOTI 11 与 Ilj15.Tests 工程）。这一点与本波无关，但别把它们的 diff 算进 Task 7。

## 2. 本波交付

| 文件 | 内容 |
|---|---|
| `src/DFLegacy.InterProcess/InterProcessLinkServer.cs` | channel 侧链路服务端：REGISTER → REGISTER_ACK → 双向 STATUS；`PeerUp`/`LastRegister`/`LastStatus`/`LastStatusAt`；幂等 `Stop()` |
| `src/DFLegacy.Channel/Program.cs` | 链路服务端接入进程生命周期（与握手宿主同一个 try/lifetime/finally） |
| `tests/.../InterProcessLinkSmokeTests.cs` | +2 个场景：in-process 服务端端到端、版本不匹配 accepted=0 |
| `tests/.../ChannelProcessSmokeTests.cs` | +1 个场景：**真实 `DFLegacy.Channel` 进程** ↔ 真 `InterProcessLinkClient` 的端到端；夹具补 `LinkPort` |
| `docs/.../plan` | Task 7 段写入「2026-10-21 刷新裁定」（10 条草案缺陷 + 代价），草案代码保留作历史 |

### 关键实现口径（Task 8/9 不要改错）

- **`PeerUp` = 已注册会话数 > 0**（`Interlocked` 维护），不是单个布尔位 —— 重连时旧会话收尾不会把新会话打回 down。
- **`Stop()` 幂等**：门内取快照 + 清空，门外停监听并关闭已接受连接。它会与 `RunAsync` 的 finally 并发调用，
  直接枚举同一个 List 会复现 Task 4 修过的 "Collection was modified"。
- **链路帧读上限 4 KiB**（`MaximumFrameLength`）：最大 REGISTER 只有 49 B。
- **channel 侧 STATUS 的 `TotalPackets` 用收包口径**（`sink.ReceivedPackets`），**不是** `Received+Sent`；
  `TotalConnections = TotalSessions`、`LoggedInUsers = 0`（channel 进程不登录）。
- **进程生命周期**：链路与握手宿主同处一个 try、共用 `lifetime`；`finally` 先 `Cancel()` 再 `Stop()` 两者 ——
  否则任一方故障退出后另一方的循环还会继续跑。

## 3. 本波 Ruling（每条带代价）

1. **刷新裁定优先于草案**：草案有 10 处与现场不符或会复现已知缺陷（不存在的配置符号、`CancellationToken.None`
   永久运行、`TotalPackets=Received+Sent`、单 bool `PeerUp`、无锁 `Clear()`、`Stop()` 不关会话、单向端到端断言、
   插入点已不存在、读包无上限、NOTICE 未接）。全部记在计划里并取代草案。**代价**：计划文本同时存在现行裁定与历史草案。
2. **真实两进程 e2e 放在 `ChannelProcessSmokeTests`**（草案说放 `InterProcessLinkSmokeTests`）：那里已有进程夹具、
   端口分配与 env 构造。**代价**：Task 7 的证据落在两个测试文件里。
3. **本波不发 `NOTICE_CHANNEL_SERVER`**（草案也没发）。**代价**：四种载荷只用了三种，第 4 种留到 Task 9/10，
   见 D7-NOTICE —— 别把它当成已连通。

## 4. 门槛结果（本波实际执行）

| # | 命令 | 结果 |
|---|---|---|
| 1 | `dotnet build tests/DFLegacy.SmokeTests/DFLegacy.SmokeTests.csproj -c Release` | 成功，**0 警告 0 错误** |
| 2 | `dotnet run --project tests/DFLegacy.SmokeTests -c Release --no-build -- --s2-ipc` | `S2 IPC smoke: 0 failures`；连跑 2 次 13.4s / 13.2s（另有一次变异红、一次还原后绿） |
| 3 | `dotnet run --project tests/DFLegacy.SmokeTests -c Release --no-build` | `All DFLegacy protocol smoke tests passed.`，38.6s，exit 0 |

### 变异对照

| 变异 | 变红的断言 |
|---|---|
| 服务端无条件接受注册（`versionMatches = true`） | `a REGISTER with a newer link protocol version is answered with accepted=0 …` + `the rejection is logged with the reason` |
| Channel 的链路端口改成 `LinkPort + 1` | `channel link role becomes ready within 120s`（真实进程 e2e 就绪） |

变异时还有一个**连带红**（split 模式下频道口的探针失败）—— 原因是 `LinkPort + 1` 恰好撞上了夹具的另一个端口，
属于变异本身的副作用，不是断言缺陷。它顺带证明了端口去重（`FixturePorts.Allocate`）确实是必要的。

## 5. 提交与索引

- 逐文件 pathspec，本波 5 个文件全部是自己的（新增 1、修改 4），**没有**需要 hunk 级拆分的共享文件。
  `tests/DFLegacy.SmokeTests/Program.cs` 本波未改（Task 6 的注册行已在 HEAD 里），它的未暂存改动是别人的。
- `docs/` 用 `git add -f -- <明确路径>`。

## 6. 遗留项当前视图

单一来源：`.superpowers/sdd/2026-10-19-s2-multiprocess-split-first-slice/progress.md` 的「Task 7」段（本机工件）。

**本波改变状态的**：
- `D6-INTEROP` → **resolved**：真实 `DFLegacy.Channel` 进程 ↔ 真客户端两进程端到端已验（注册 + 双向 STATUS + ACK 身份）。
- `D6-ACK-WINDOW` → 仍 open（2 s 下限仍是常量），但默认配置下的真实注册路径已被验通。

**仍 open（Task 5/6 段）**：D5-START、D5-CANCEL、D5-WIRING、D5-CONFIG-PATH、D5-STOP-TEST、D5-SENT-ORDER、
D5-ACCEPT-TEST、D5-DEAD、D5-REAL、D5-RAW-COMPARE、D6-ACK-WINDOW、D6-WIRING（客户端还没接进 game 进程）。

**本波新增**：

| ID（均 open） | 负责人 | 当前范围 | 触发条件 |
|---|---|---|---|
| D7-SILENCE | Task 8 | 链路服务端**没有服务端侧的静默判死**：对端只要 TCP 不断开，`PeerUp` 就一直为真（半开连接检测不到）。`LastStatusAt` 已经可读，但没人用它做判定 | Task 8 做 admin 存活位时必须先决定：用 `LastStatusAt` 超时，还是只信 socket 关闭 |
| D7-NOTICE | Task 9 / 10 | `NOTICE_CHANNEL_SERVER`（第 4 种载荷，官方布局）仍未由服务端发出 | S2S 报文表或验收场景需要它时 |
| D7-INTERVAL | Task 8 | 服务端 STATUS 间隔硬编码 1 s，客户端间隔由调用方给；两侧不对齐不会被发现（只会在「静默判死」时表现为误判） | Task 8 把链路接进 game 进程时一并决定是否配置化 |

## 7. 未解释 / 需警惕的验证异常

1. **变异必须先在源码里确认命中**：本波第一次试写变异时用的是 `\r\n` 替换而文件是 LF，替换静默没生效 —— 这已经是
   同一类坑的第二次（Task 6 交接 §4 记过）。现在的固定做法：改完先 `Select-String` 确认落在预期行，再构建；
   还原后 `Touch` 源文件强制重建，最后用**行为证据**（本波是 e2e 就绪）确认还原真的生效了。
2. 本波未观察到 flake：`--s2-ipc` 净跑 4 次（1 次变异红、1 次还原后绿、2 次复跑绿），全量 1 次。

## 8. 下一波只读这些

1. 本文件 §2 的实现口径、§3 的 Ruling、§6 的遗留项（尤其 **D7-SILENCE** 与 `LastRemoteStatus`/`LastStatus` 的口径）。
2. 计划 `## Task 8:` 段 —— 仍 `needs-refresh`，进入前必须核验：
   - game 侧要 new 的是 `InterProcessLinkClient`，它的 `Register` 该填什么（`GcNo`/`ChannelNo`/`GamePort` 的真实来源），
   - `/api/status` 现有字段（`src/DFLegacy.Server/Program.cs:331-355`）与 I4「既有字段不回归 + 新增 channel 侧总计与在线位」，
   - `Received+Sent ≠ TotalPackets`、`LastRemoteStatus` 是对端口径这两条已记录规则，
   - Server 的 `ProcessOptions.Validate()` 与 `Processes.Game.EntranceBinding` 在拆分模式下对链路客户端启停的影响。
3. `src/DFLegacy.Channel/Program.cs`（链路服务端装配样例）、`src/DFLegacy.InterProcess/InterProcessLinkClient.cs`、
   `src/DFLegacy.Server/Program.cs` 的状态接口段。
4. ledger 最末「Task 7」段（本机）。

Task 11 的 Tier 2 对比方法继续 `needs-refresh`。**I3 已在库层验证通过，但 game 进程还没接客户端（Task 8 / I4）。**
