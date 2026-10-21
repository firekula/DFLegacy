# Task 6 完成交接 · 链路客户端 + Tier 1 前半（2026-10-21）

> **[历史]** I3 前半的结论仍有效，但**当前视图**已移到 `2026-10-21-task-7-complete-handoff.md`
> （Task 7 起 BASE 为本次提交；遗留项当前表以那份 §6 为准）。

> 当前视图。前一版 `2026-10-21-task-5-complete-handoff.md` 记录 I2，历史保留。
> 本波只做 Task 6；Task 7–11 一行未动，全部仍 `needs-refresh`。
> 换会话续跑：先读本文件 §3–§6，再按标题定位计划的 `## Task 7:` 段，不要通读整份远期计划。

## 1. 基线、提交与工作区

- 仓库 `G:/Game/DNF_ACT1/DFLegacy`，分支 `main`。
- 本波 BASE `73f16c0`（Task 5 提交），HEAD `939b598`（4 文件，+861/−1）。
- 实施前 `recon_status` 无配置问题；申报工具仍只有 `smoke-tests` 覆盖全量回归，**没有 IPC 分组工具**。
- 三份共享文件里他人的改动仍原样保留：`DFLegacy.Emulator.slnx`（Ilj15.Tests 工程）、
  `tests/DFLegacy.SmokeTests/Program.cs`（NAT/NOTI 11）、以及 `src/DFLegacy.Server|Protocol|Ijl15` 的既有改动与未跟踪原生文件。
  本波只把 `Program.cs` 的**自己那一行**放进 index，提交后这两个文件仍显示他人 hunk。

## 2. 本波交付

| 文件 | 内容 |
|---|---|
| `src/DFLegacy.InterProcess/InterProcessLinkClient.cs` | 拨号 → REGISTER → 等 ACK → 周期 STATUS（心跳）→ 静默判死 + 退避重连；`Connected`/`LastAck`/`LastRemoteStatus`/`LastStatusAt` 可读；`PeerUp`/`PeerDown` 事件 |
| `tests/DFLegacy.SmokeTests/InterProcessLinkSmokeTests.cs` | 三组：正常链路（线上字节/心跳/对端 STATUS）、accepted=0 与版本不匹配、静默判死 + 重连 |
| `tests/DFLegacy.SmokeTests/Program.cs` | `--s2-ipc` 块 +1 行 |
| `docs/.../2026-10-19-s2-multiprocess-split-first-slice.md` | Task 6 段写入「2026-10-21 刷新裁定」（4 处草案缺陷 + 各自代价），草案代码保留作历史 |

### 关键实现口径（Task 7/8 不要改错）

- **`Connected` = 已注册成功**，不是 TCP 连上。`accepted=0` 或 `ProtocolVersion` 不一致都不建链，但 `LastAck`
  仍保留（区分「没回 ACK」与「回了但拒绝」）。
- **`LastRemoteStatus` 只存对端发来的 STATUS**。admin 合并必须用它；把自己发的那条合进去会让
  `/api/status` 的 channel 侧字段永远等于 game 侧。
- **判死只看收**：连续 `MissedStatusLimit` 个间隔**一帧都没收到**才 down（单向心跳没有回包可等）。
- `AckTimeout = StatusInterval × MissedStatusLimit`，**下限 2 s**（注册阶段的宽限，见 D6-ACK-WINDOW）。
- **CTS 归 `RunAsync` 的 `using` 所有**，退出时才释放；`DisposeAsync` 只取消、不等待。要等停止就 await
  自己持有的 `RunAsync` 任务 —— 这条与草案相反，改回去会引入「跑动中释放 CTS」。

## 3. 本波 Ruling（每条带代价）

1. **刷新裁定优先于草案代码**：草案 Step 1/Step 3 的 4 处缺陷（ACK 前断 Connected、读包无超时、
   `DisposeAsync` 释放 CTS、Tier 1 只验一半）已在计划里逐条记明并取代，草案代码块保留作历史。
   **代价**：计划文本里同时存在「现行裁定」与「历史草案」，读的人必须先看裁定。
2. **Tier 1 第 1/2 项的编解码精确 hex 不重复测**：`InterProcessCodecSmokeTests.cs:20-29` 已钉住 49 B 的
   固定 hex，本波只补「客户端真的把那些字节写到 socket 上」。**代价**：两处证据要一起读才算完整帧证据。
3. **注册宽限与稳态判死分开**：稳态按 3 个心跳间隔判死（spec §7.1 第 4 项），注册阶段给 2 s 下限。
   **代价**：`AckTimeout` 是一个新常量，Task 7 需要复核（D6-ACK-WINDOW）。

## 4. 门槛结果（本波实际执行，同一棵树）

| # | 命令 | 结果 |
|---|---|---|
| 1 | `dotnet build tests/DFLegacy.SmokeTests/DFLegacy.SmokeTests.csproj -c Release` | 成功，**0 警告 0 错误** |
| 2 | `dotnet run --project tests/DFLegacy.SmokeTests -c Release --no-build -- --s2-ipc` | `S2 IPC smoke: 0 failures`；连跑 3 次 14.5s / 15.9s / 12.9s，exit 0 |
| 3 | `dotnet run --project tests/DFLegacy.SmokeTests -c Release --no-build` | `All DFLegacy protocol smoke tests passed.`，43.6s，exit 0 |

命令 2 与 3 是两组独立门槛（全量不含 IPC 组）。本波 IPC 组耗时从 12s 涨到 ~14s 是新加的三组链路用例。

### 变异对照（证明新门槛能变红，且能定位）

| 变异 | 变红的断言 |
|---|---|
| `_connected = true` 提前到 ACK 之前 | `a TCP connection alone does not mark the link up: Connected stays false until REGISTER_ACK is accepted` |
| 读循环静默窗口放大到 5 分钟 | `3 missed status intervals of complete silence mark the link down with a reason` |

**坑（第二次踩到同类）**：第一次改坏用 `\r\n` 做字符串替换，而文件是 LF，**替换静默没生效**，
跑出来仍绿 —— 是同批第二个变异变红才暴露。结论：改坏后必须先用 `Select-String` 确认改动落在预期行，
再跑门槛；改回后要 `Touch` 源文件或删 `obj/` 强制重建（Task 5 交接 §7 记的 mtime 反向问题）。

## 5. 提交与索引

- 逐文件 pathspec：`InterProcessLinkClient.cs`、`InterProcessLinkSmokeTests.cs`、`Program.cs`（hunk 级）、计划。
- `Program.cs` 含他人 hunk，用「HEAD blob + 本任务 1 行」构造 index（`git hash-object -w` +
  `git update-index --cacheinfo`），`git diff --cached` 核对后提交；提交后该文件仍显示他人改动。
- `docs/` 用 `git add -f -- <明确路径>`。

## 6. 遗留项当前视图

单一来源：`.superpowers/sdd/2026-10-19-s2-multiprocess-split-first-slice/progress.md` 的
「Task 6（I3 前半）」段（本机工件）。Task 5 段列出的 open 项状态不变：

D5-START、D5-CANCEL、D5-WIRING、D5-CONFIG-PATH、D5-STOP-TEST、D5-SENT-ORDER、D5-ACCEPT-TEST、D5-DEAD、
D5-REAL、D5-RAW-COMPARE。本波新增：

| ID（均 open） | 负责人 | 当前范围 | 触发条件 |
|---|---|---|---|
| D6-INTEROP | Task 7 | 客户端只对着测试自建的 `FakeLinkPeer` 验过，与真实 channel 侧服务端的互通未验证 | Task 7 落地后必须跑真实客户端↔服务端两进程用例 |
| D6-ACK-WINDOW | Task 7 | `AckTimeout` 下限 2 s 是硬编码常量，不是配置键 | Task 7 按真实注册耗时复核是否要暴露配置 |
| D6-WIRING | Task 8 | 客户端尚未在 game 进程实例化，`/api/status` 无 channel 侧字段 | Task 8 接线时复用 `LastRemoteStatus` 口径 |

没有新增 accepted 风险，未改 spec 门槛。

## 7. 未解释 / 需警惕的验证异常

1. **本波出现过一次真实偶发失败（已定位、已修）**：`LastAck` 先写、`PeerDown` 后发，测试分两次 poll
   会在极窄窗口读到「LastAck 已是拒绝但 PeerDown 还没进列表」。已合并成一次 poll，并连跑 3 次确认稳定。
   **判据**：再看到 `a rejected REGISTER_ACK is reported through PeerDown` 偶发红，先查这个顺序。
2. **握手阶段的长短要分开看**：把注册宽限设得太短（我最初用 300 ms）会让测试自己的「没有重复帧」等待
   （200–300 ms）先耗尽窗口，症状是刷屏的 `no REGISTER_ACK within the ack window`。这条留给 Task 7 调参时参考。
3. 本波未观察到其它 flake；`--s2-ipc` 净跑 5 次（2 次红是变异对照，3 次绿）。

## 8. 下一波只读这些

1. 本文件 §2 的实现口径、§3 的 Ruling、§6 的遗留项。
2. 计划 `## Task 7:` 段 —— 注意它仍 `needs-refresh`，进入前必须核验：Task 5 的 `DFLegacy.Channel` 装配点
   （`src/DFLegacy.Channel/Program.cs` 现在只起握手宿主，链路服务端要接在这里）、Task 6 的客户端契约
   （`RegisterPayload`/`RegisterAckPayload` 字段、`Connected` 语义、`AckTimeout`）、以及 `Processes.Channel.LinkPort` 的实际值来源。
3. `src/DFLegacy.Channel/Program.cs`、`ChannelOptions.cs`（`Processes.Channel.LinkPort` 已映射，但**尚未使用**）、
   `src/DFLegacy.InterProcess/InterProcessLinkClient.cs`。
4. ledger 最末「Task 6（I3 前半）」段（本机）。

Task 11 的 Tier 2 对比方法、Task 7/8 的生命周期与统计口径继续 `needs-refresh`：**不要把 Received+Sent 当
Server 的 TotalPackets，也不要把自发 STATUS 当对端状态**。
