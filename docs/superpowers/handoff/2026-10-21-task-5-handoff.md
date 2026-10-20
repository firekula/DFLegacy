# Task 5 Handoff · `DFLegacy.Channel` 新 exe（I2）

> 写作时间 2026-10-21。上一段：Task 4（I1 收口）已提交并过独立复审流程。
> **本文件只交接，不含设计结论**——设计与计划在下面列的权威文件里。

---

## 1. 坐标

| 项 | 值 |
|---|---|
| BASE（Task 4 开工） | `d9a8986` |
| Task 4 提交 | `0e38cf2`（8 文件 +417/−13） |
| 计划修订提交 | `57d8a05`（计划文件 +57/−15） |
| 分支 | `main`（不建 worktree，理由见 ledger 的开工 Ruling） |
| 断点 | **Task 5 未开始**。计划 Task 5 从 **第 1212 行**起（`Select-String -Path <plan> -Pattern '^## Task 5'` 现场取，别照抄本文件） |

## 2. 已完成任务

| 任务 | 状态 | 提交 |
|---|---|---|
| Task 1 `DFLegacy.InterProcess` 帧壳 | ✅ | `68f2949` → `6a72a48` |
| Task 2 端口分族 + `Processes` 配置 | ✅ | `2aee1d9` → `82c491c` |
| Task 3 `EntranceHandshakeServer` | ✅ | `5a50b9b` → `c6189a6` |
| **Task 4 接入 server，I1 收口** | ✅ | `d9a8986` → `0e38cf2`（+ 计划 `57d8a05`） |
| Task 5–11 | ⬜ 未开始 | — |

## 3. I1 的三条验收：实测结果（可重跑）

| # | 验收 | 实测 | 命令 |
|---|---|---|---|
| ① | 现有 smoke 全绿零回归 | `All DFLegacy protocol smoke tests passed.` exit 0 / 81s；`--s2-ipc` 0 failures（11s，不挂起）×5 | `dotnet run --project tests/DFLegacy.SmokeTests -c Release`（申报工具 `smoke-tests`）、`dotnet run --project tests/DFLegacy.SmokeTests -- --s2-ipc` |
| ② | 2311/7001/8080 全监听且同 pid | 三端口同属 pid 83272；日志：`EntranceHandshakeService` 绑 2311+8080、`EntranceService` 绑 7001 | 起 `DFLegacy.Server` 后 `Get-NetTCPConnection -State Listen \| ? LocalPort -in 2311,7001,8080` |
| ③ | 客户端握手字节不变 | **[抓包]+[回放]**：真机录制明文与新代码产出逐字节相同（见 §4） | `EntranceSplitSmokeTests.RunContentFactory` / `RunHandshakeReplayAsync`（随 `--s2-ipc` 跑） |

> ② 的运行需要 PVF 覆盖：`server.json:58` 的 `ScriptPvfPath` 指向本机不存在的
> `D:\DFLegacy\DF2008\Script.pvf`；资产实际在 `<repo>\DNF2008\Script.pvf`，
> 用 `$env:DFLegacy__ScriptPvfPath='<repo>\DNF2008\Script.pvf'` 再起
> `dotnet src/DFLegacy.Server/bin/Debug/net10.0/DFLegacy.Server.dll`（`Program.cs:21-25` 支持该覆盖）。

## 4. 本 wave 的 Rulings（每条带"错了的代价"）

1. **不动 `EntranceService.LoadContent`，频道信息走已提交的 `EntranceDialHost.Resolve`。**
   理由：拆分后入口分支不可达；工作区里 `LoadContent` 已被**未提交**的 `ResolveChannelDialHost` 改过，
   动它会与未提交 hunk 冲突；Task 4 的提交必须能在 HEAD-only 状态下编译。
   代价若错：若那份未提交改动被丢弃，本进程仍用"修正后"的拨号目标（好于 HEAD 行为），与树内其余部分短暂不一致，但不会静默变坏。
2. **`RuntimeEntranceSink` 用 `public`**（计划写 `internal`）：仓库无 `InternalsVisibleTo` 先例，测试要可见。代价：公开面略增，无行为影响。
3. **新增三组断言**（sink 记账 / 内容组装镜像 / 录制字节回放）+ 改 Task 3 文件让会话标签带本地端口。代价：测试面与复审面变大（已用变异验证钉死归因）。
4. **全解 build gate 降级**为「managed 工程 0/0 + 全量 smoke 绿」：原生 vcxproj 在 dotnet CLI 下必失败（`MSB4278` 缺 VCTargetsPath），且 slnx 上有他人未提交的 `Ijl15.Tests` 条目。代价：原生工程编译问题不在本 gate 暴露（与本任务无关）。
5. **`EntranceHandshakeServer.Stop()` 加锁**（取快照 → 释放锁 → 逐个 Stop，`Stop()` 幂等）：修掉 `RunAsync.finally` 与宿主 `Stop()` 并发枚举/清空的竞态（实测 `--s2-ipc` 随机红）。代价：该竞态**无确定性测试覆盖**，只能靠 review + 反复运行兜。

## 5. Task 5 可直接复用的三个接口事实（Task 4 实测）

1. `EntranceHandshakeService` / `RuntimeEntranceSink` / `EntranceContentFactory` 都已就位，channel 进程可直接复用；
   `EntranceContentFactory` 只负责组装+缓存，缺 `etc/channel_info.etc` 的 warn 留在调用方（需要 `ScriptFileSystem.SourceDescription`）。
2. 会话标签由宿主拼成 `entrance:{localPort}`（`EntranceHandshakeServer.cs` 的 `HandleClientAsync`）——
   channel 侧的 sink 实现要接受同样形状，否则 admin 标签在拆分模式下会不一致。
3. `EntranceHandshakeServer.Stop()` 现在幂等且并发安全，可直接放进 channel 宿主的 shutdown 路径。

## 6. 未验证 / deferred（Task 5 不要假装它们已解决）

- **真机重跑 `town-two-clients` 未做**：netdump 依赖**未提交**的 `src/DFLegacy.Ijl15/src/net_dump.cpp`；
  I1③ 的端到端证据止于"录制真机输入 + 回放"，没有"改造后再录一次"的对照。
  取数计划：先解决 netdump 的提交/构建（属他人工作），再跑 `tools/acceptance` 的 `town-two-clients`，
  用 `decode` 同样的方法比对 proto3/proto6/proto10 明文。
- `EntranceHandshakeServer.Stop()` 并发安全无确定性测试覆盖（时序相关）。
- `IsDialableHost` 仍是"仅工作区"事实：`git grep -n 'IsDialableHost' HEAD -- src tests` 为空。
  若那份未提交改动被丢弃，`EntranceDialHost.IsDialable` 的镜像对象在 HEAD 里不存在，需重评。
- `EntranceService` 的入口分支（`:15375`）与 `LoadContent()` 拆分后不可达，属死代码，留待后续清理。
- 延续 Task 3 deferred：`NoDelay/MaximumPacketLength/RejectBadCrc32` 未镜像；取消期 OCE 未吞；
  `SentCount` 断言 happens-before；`StartAsync` token 未用且多端口失败不回滚；真实 accept 错误被静默吞掉这条回归无牙。

## 7. 工作区现状（最容易踩的坑）

- **其他人的未提交改动仍在**（Task 4 只 stash 了三个共享文件，已 `git stash pop` 还原，`git stash list` 为空）：
  `src/DFLegacy.Protocol/GameProtocolEngine.cs`、`src/DFLegacy.Protocol/GameDatagramProtocol.cs`、
  `src/DFLegacy.Server/EntranceService.cs`（`ResolveChannelDialHost`/`IsDialableHost`）、
  `src/DFLegacy.Server/Program.cs`（`WarnAboutChannelDialHost`）、
  `tests/DFLegacy.SmokeTests/Program.cs`（NOTI 11 字节序）、`tests/DFLegacy.SmokeTests/TownSyncSmokeTests.cs`、
  `DFLegacy.Emulator.slnx`、`src/DFLegacy.Ijl15/**`、`BUILDING.md`、`STATUS.md`、`.recon/onboarding.md`。
- 复现 Task 4 的隔离手法：`git stash push -m <name> -- <共享文件>` → 编辑 → 逐文件 pathspec 提交 → `git stash pop`；
  **收尾三查**：`git stash list` 空、被保护文件重新 ` M`、`git show --stat HEAD` 只含本任务文件。
- `docs/` 被 `.gitignore` 挡住：**新增**文档要 `git add -f <path>`（已跟踪的文档普通 `git add` 即可）。

## 8. 下一波只读这些（不要通读整份计划）

1. 计划的 **Task 5 段**（当前从第 1212 行起，现场取行号）。
2. Task 4 产出的接口：`src/DFLegacy.Server/EntranceHandshakeService.cs`、`src/DFLegacy.Server/RuntimeEntranceSink.cs`、
   `src/DFLegacy.Protocol/EntranceContentFactory.cs`、`src/DFLegacy.Protocol/EntranceHandshakeServer.cs`、
   `src/DFLegacy.Protocol/EntranceSessionSink.cs`、`src/DFLegacy.Protocol/EntrancePortFamilies.cs`。
3. ledger：`.superpowers/sdd/2026-10-19-s2-multiprocess-split-first-slice/progress.md`（Task 4 段 + deferred）。
4. spec §5（通信层）与 §6 的 I2 行。

## 9. 权威计划状态

**已提交**：`57d8a05`。Task 4 的执行期修订（Files 头、三组断言、build gate、PVF 覆盖、不动 `LoadContent`）都在里面。
