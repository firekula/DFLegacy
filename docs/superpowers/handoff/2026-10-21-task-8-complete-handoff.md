# Task 8 完成交接 · game 接入链路 + admin 计数与存活（2026-10-21）

> 当前视图。前几版（Task 5/6/7 的 complete-handoff）记录历史，结论仍有效。
> 本波只做 Task 8（I4）。Task 9–11 一行未动，全部仍 `needs-refresh`。
> 换会话续跑：先读本文件 §2–§6，再按标题定位计划的 `## Task 9:` 段。

## 1. 基线、提交与工作区

- 仓库 `G:/Game/DNF_ACT1/DFLegacy`，分支 `main`。
- 本波 BASE `ebbbc53`（Task 7 提交），HEAD 见 `git log -1 -- src/DFLegacy.Server/ChannelLinkService.cs`。
  8 文件，+371/−9。
- 本波**改了两个含他人改动的共享文件**：`src/DFLegacy.Server/Program.cs`（他人有 `WarnAboutChannelDialHost`）与
  `tests/DFLegacy.SmokeTests/Program.cs`（他人有 NAT/NOTI 11）。都用「HEAD blob + 本任务 hunk」构造 index 后
  `git diff --cached` 核对，只暂存自己的 hunk；提交后这两个文件的他人改动仍为未暂存状态。

## 2. 本波交付

| 文件 | 内容 |
|---|---|
| `src/DFLegacy.Server/ChannelLinkService.cs` | `BackgroundService` 托管链路客户端；拆分模式拨号 channel 进程，单体模式直接返回；构造注入 `RuntimeState` |
| `src/DFLegacy.Server/StatusProjection.cs` | `ChannelLink(...)` → `ChannelLinkStatus`；断开只报本地值、连通按收包口径加回双侧总计 |
| `src/DFLegacy.Server/Program.cs` | 注册 `ChannelLinkService`（单例 + 托管）；`/api/status` 加 `channelLink` 字段（既有字段不动） |
| `src/DFLegacy.Server/DFLegacy.Server.csproj` | +1 行 InterProcess ProjectReference |
| `tests/.../StatusProjectionSmokeTests.cs` | 纯投影：断开 / 连通 / connected-但未收到首个 STATUS 三态 |
| `tests/.../ChannelProcessSmokeTests.cs` | 真实两进程 e2e：链路起来、注册入账、admin 登录 GET `/api/status` |
| `tests/.../Program.cs` | `--s2-ipc` 块 +1 行 `StatusProjectionSmokeTests.Run` |
| `docs/.../plan` | Task 8 段「2026-10-21 刷新裁定」（4 条缺陷 + 代价） |

### 关键实现口径（Task 9/10/11 不要改错）

- **`StatusPayload.LoggedInUsers` 在 game 侧恒 0**：`RuntimeState` 不记已登录角色数，别把会话数塞进去。
- **`TotalPackets` 收包口径**：`runtime.TotalPackets`（收包数）与 channel 的 `sink.ReceivedPackets` 都是收包，
  投影里相加才是「双侧总收包」。**不是收+发**。
- **`/api/status.channelLink`**：`connected = Client?.Connected`、`totalConnections = 本地 TotalConnections + 对端 TotalConnections`、
  `activeSessions = 本地 Sessions().Length + 对端 ActiveSessions`。既有字段 `totalConnections/totalPackets/activeSessions`
  **保持本地值不动**（I4 门槛）。
- **链路客户端生命周期**：`ChannelLinkService.ExecuteAsync` 靠 `stoppingToken` 退出；CTS 由 `RunAsync` 的 `using` 持有。
  单体模式（`Processes.Channel.Enabled=false`）直接返回，`Client` 留 null。
- **admin 鉴权**：`/api/status` 是 Admin（`AdminAccessPolicy.cs:92-95` fail-closed），不是 Public。测试里用
  `JsonGameStore` 预种账号 + `Admin.Users=[admin]`（启动时 `ApplyBootstrapAdminsAsync` @Program.cs:182 提权）+ login。

## 3. 本波 Ruling（每条带代价）

1. **刷新裁定优先于草案**：草案 4 处缺陷（`ChannelLinkService` 缺 `RuntimeState`、`LoggedInUsers` 混用会话数、
   `/api/status` 手工访问会被 401 拦、Step 5 手工验证不算证据）。全部记在计划里并取代草案。
   **代价**：计划文本同时存在现行裁定与历史草案。
2. **`/api/status` 端到端验证走 admin 登录**（不做成 Public）：在测试里预种账号 → 起真实进程 → login → Bearer GET。
   **代价**：测试要起两个真实进程 + 种账号，约 16s；且 `Admin.Users` 提权只在启动时发生。
3. **`LoggedInUsers` 暂不暴露**（game 侧报 0）：`RuntimeState` 没有登录角色计数，Task 8 不新增这个口径。
   **代价**：admin 的 `channelLink` 没有「已登录角色数」，留到有真实口径的波再补。

## 4. 门槛结果（本波实际执行）

| # | 命令 | 结果 |
|---|---|---|
| 1 | `dotnet build tests/DFLegacy.SmokeTests/DFLegacy.SmokeTests.csproj -c Release` | 成功，**0 警告 0 错误** |
| 2 | `dotnet run --project tests/DFLegacy.SmokeTests -c Release --no-build -- --s2-ipc` | `S2 IPC smoke: 0 failures`，16s |
| 3 | `dotnet run --project tests/DFLegacy.SmokeTests -c Release --no-build` | `All DFLegacy protocol smoke tests passed.`，41.3s，exit 0 |

### 变异对照

| 变异 | 变红的断言 |
|---|---|
| `StatusProjection.ChannelLink` 合并改成只取本地值（`localPackets`） | `with the link up the channel-side counters are added back into the admin totals` |
| `ChannelLinkService` 链路端口 `+1` | 接入链路三断言：link comes up / channel logs registration / channelLink.connected |

### 修掉的真实问题（非变异）

- **`channelLink.totalConnections >= 1` 最初恒红**：没有客户端连接时两边计数都是 0，`>=1` 是错的。改为先做一次
  「连上就关」的真实入口连接，把 channel 侧 `TotalSessions` 推到 1，再轮询 `/api/status` 直到
  `channelLink.totalConnections >= 1` —— 这样才是一条真的跨进程计数观测，而不是「两边都 0 的巧合」。

## 5. 提交与索引

- 逐文件 pathspec；`src/DFLegacy.Server/Program.cs` 与 `tests/DFLegacy.SmokeTests/Program.cs` 用「HEAD blob + 本任务 hunk」
  构造 index（`git hash-object -w` + `git update-index --cacheinfo`），`git diff --cached` 核对后提交。
- `docs/` 用 `git add -f -- <明确路径>`。

## 6. 遗留项当前视图

单一来源：`.superpowers/sdd/2026-10-19-s2-multiprocess-split-first-slice/progress.md` 的「Task 8」段（本机工件）。

**本波改变状态**：`D6-WIRING`（客户端接进 game 进程）→ **resolved**：`ChannelLinkService` 已在 Server 进程实例化并
真实两进程连通；`D7-SILENCE`（服务端侧静默判死）→ 仍 open（本波没做服务端判死，只用了 `LastStatusAt` 可读性）。
`D7-INTERVAL`（STATUS 间隔不对齐）→ 仍 open（game 侧硬编码 1 s，channel 侧硬编码 1 s，恰好一致但都不是配置键）。

**仍 open**：D5-START、D5-CANCEL、D5-WIRING、D5-CONFIG-PATH、D5-STOP-TEST、D5-SENT-ORDER、D5-ACCEPT-TEST、D5-DEAD、
D5-REAL、D5-RAW-COMPARE、D6-ACK-WINDOW、D7-NOTICE、D7-SILENCE、D7-INTERVAL。

**本波新增**：

| ID（均 open） | 负责人 | 当前范围 | 触发条件 |
|---|---|---|---|
| D8-STATUS-ENDPOINT | 后续波 | `/api/status` 的 `channelLink` 字段端到端只验到「connected=true + totalConnections>=1」；`lastStatusAt`/`activeSessions`/`remoteSessions` 的精确值未逐字段断言（需要造出确定性的双侧会话数） | 需要精确 admin 口径时补；或决定 `/api/status` 是否应公开后加免登录 shape 测试 |
| D8-LOGGEDIN | 后续波 | game 侧 `LoggedInUsers` 恒 0，`RuntimeState` 无登录角色计数 | 有真实登录口径的波 |

## 7. 未解释 / 需警惕的验证异常

1. **本波没有 flake**：`--s2-ipc` 净跑 3 次（1 次红是初版断言错，2 次绿是修后 + 变异还原后），全量 1 次。
2. **`channelLink.totalConnections >= 1` 的教训**：进程链路本身**不算**进任一侧的 `TotalConnections`（链路是独立 socket，
   不进 `RuntimeState`/`ChannelSessionSink`）。别把「链路起来了」当成「有客户端连接」。

## 8. 下一波只读这些

1. 本文件 §2 的实现口径、§3 的 Ruling、§6 的遗留项。
2. 计划 `## Task 9:` 段 —— 仍 `needs-refresh`，进入前必须核验：`protocol/` 的 S2S 报文表现状、四种载荷里
   `NOTICE_CHANNEL_SERVER`（D7-NOTICE）要不要在本波接、`InterProcessMessageIds`/`InterProcessPayloads` 是否要扩展。
3. `src/DFLegacy.InterProcess/InterProcessPayloads.cs`（四种载荷）、`src/DFLegacy.Server/ChannelLinkService.cs`、
   `StatusProjection.cs`。
4. ledger 最末「Task 8」段（本机）。

**I4 已验到**：game 进程接入链路（真实两进程）、双侧存活对 admin 可见（`/api/status.channelLink.connected`）、
既有字段不回归（纯投影 + 真实 GET）。**未验**：`channelLink` 每个字段的精确值（D8-STATUS-ENDPOINT）、
服务端侧静默判死（D7-SILENCE）、真机（D5-REAL）、Tier 2（D5-RAW-COMPARE）。
