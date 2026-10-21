# Task 5 完成交接 · `DFLegacy.Channel` 新 exe / I2（2026-10-21）

> **[历史]** I2 的结论仍然有效，但**当前视图**已移到 `2026-10-21-task-6-complete-handoff.md`
> （Task 6 记录基线 `73f16c0` = 本波提交；遗留项当前表也以那份的 §6 为准）。

> 本文件取代 `2026-10-21-task-5-handoff.md`（那份是实施前的恢复核验视图，历史保留不回改）。
> 本波只做 Task 5；Task 6–11 一行未动，全部仍 `needs-refresh`。
> 换会话续跑时：先读本文件 §3–§6，再按标题/符号定位计划里的下一个任务段，不要通读整份远期计划。

## 1. 基线、提交与工作区

- 仓库 `G:/Game/DNF_ACT1/DFLegacy`，分支 `main`。
- 实施前 HEAD `330c526`（= 代码基线 `fdde655` 之上的纯文档提交：计划/handoff/continue-prompt）。
  `git diff fdde655..330c526` 只有这 3 个 docs 文件，**核心源码零差异**，故 C5 契约证据可沿用。
- 实施前工作区快照复核：`task-5-resume-review/baseline.json` 的
  - 22 条 `protected` 指纹 → **全部一致**
  - 17 条 `inputSha256` → **全部一致**
  - `worktree.patch` SHA256 = `bb27c539…5eb4` → **一致**
  ⇒ 本波没有刷新任何 C5 契约证据，沿用的是同一份源码/C 配置。
- 本波提交：用 `git log -1 -- src/DFLegacy.Channel` / `git log -1 -- docs/superpowers/plans/2026-10-19-s2-multiprocess-split-first-slice.md` 查（没有把 hash 写死进本文，避免自引用）。
- 共享文件里**他人的改动仍原样保留**：`DFLegacy.Emulator.slnx` 的 `DFLegacy.Ijl15.Tests.vcxproj` 条目、
  `tests/DFLegacy.SmokeTests/Program.cs` 的 NAT/NOTI 11 断言、`src/DFLegacy.Server/EntranceService.cs` +
  `Program.cs` 的拨号告警改动、`src/DFLegacy.Protocol/*`、`src/DFLegacy.Ijl15/*` 与三份未跟踪原生文件。
  本波只把**自己的 1 行 hunk** 放进 index（见 §5）。

## 2. 本波交付

| 文件 | 内容 |
|---|---|
| `src/DFLegacy.Channel/DFLegacy.Channel.csproj` | Web SDK exe（不建 `WebApplication`/Kestrel），仅引 Protocol + InterProcess；`server.json` 以 `Content` 复制到输出目录 |
| `src/DFLegacy.Channel/ChannelOptions.cs` | 窄配置模型；`Read(IConfigurationSection)`；缺省值逐项镜像 `ServerOptions`/`ProcessOptions`；`Validate()` 同条件同消息 |
| `src/DFLegacy.Channel/ChannelScriptSource.cs` | `etc/channel_info.etc` 选源：PVF 优先 → 目录回退 → null；相对路径按 `AppContext.BaseDirectory`；损坏 PVF 抛 |
| `src/DFLegacy.Channel/ChannelSessionSink.cs` | 独立 `ReceivedPackets`/`SentPackets`/`TotalSessions`/`ActiveSessions`；保留 `entrance:{port}`；Dispose 幂等 |
| `src/DFLegacy.Channel/Program.cs` | 配置 → 拓扑守卫 → 内容 → 宿主；`StartAsync`+`RunAsync` 同处 try，`finally` 唯一 Stop；退出码 0/1/2 |
| `tests/DFLegacy.SmokeTests/ChannelProcessSmokeTests.cs` | 6 组检查：配置映射 / 校验镜像 / 脚本来源 / 两档真进程接线 + I2 / 读参数透传 / 拓扑守卫 |
| `tests/DFLegacy.SmokeTests/DFLegacy.SmokeTests.csproj` | +1 行 ProjectReference |
| `tests/DFLegacy.SmokeTests/Program.cs` | `--s2-ipc` 块 +1 行 |
| `DFLegacy.Emulator.slnx` | +1 行 Channel 工程 |

### 关键实现口径（后续波不要改错）

- 广告地址：`EntranceDialHost.Resolve(GameplayDatagram.ResolveAdvertisedHost(), Entrance.Host, Channel.Host)`
  —— 先做 Host 回退再 Resolve，顺序反了会让缺省 `AdvertisedHost=""` 跳过 Host 回退。
- 读参数：`maximumPacketLength: options.MaximumPacketLength`、`rejectBadCrc32: options.RejectBadCrc32`、
  `noDelay: true`（常量，**不新增 NoDelay 配置键**）。
- 拓扑守卫顺序：`Validate()` → `Channel.Enabled` → `Entrance.Enabled` → 入口口为空 → 解析 `Entrance.Host`，
  全部早于任何 bind。
- `Channel.Enabled=false` / `Entrance.Enabled=false` / 入口口为空 → 只 warn + `return 0`，不绑端口。
- 非法拓扑 → `return 2`；启动/运行失败 → `return 1`；正常取消或停止 → `return 0`。

## 3. 本波 Ruling（每条都写了代价）

1. **频道口「走频道路由」收窄为可观察的部分。** 本波验证：该口由 game 角色监听、**接得上**、握手引擎不在该口
   服务（协议 11 拿不到 12，且探针先要求连接成功，避免"端口没监听"也冒充通过）。正向玩法路由需要构造合法
   玩法帧或走 auth-gated admin 记账，超出本波授权。
   **代价**：`D5-WIRING` 的玩法路由一半仍 open，Task 8/11 必须补；不得宣称该口玩法接线已验证。
2. **「正常停止退出」收窄为「停止后在超时内退出且端口释放」。** 测试的停止是
   `Kill(entireProcessTree)` 后等退出（与本仓库 `tools/acceptance/runner.py:559-572` 的 terminate/kill 惯例一致），
   断言文案已按此改写，不再写 "when asked to stop"。
   **代价**：`Program.cs` 的 `Console.CancelKeyPress`→`Cancel()` 与 OCE→`return 0` 两条分支零覆盖，记入 `D5-START`。
3. **「启动失败也 Stop 已建监听」只由 code review 兜。** 进程一退出 OS 就释放监听，进程外无法区分
   「finally 里 Stop 过」和「进程死了」。测试只断言可观察子集（exit 1 + 首口确实曾 ready + 退出后端口可绑）。
   **代价**：该 `finally` 无自动化覆盖；改生命周期前必须重读 `Program.cs` 的 try/finally 与 `EntranceHandshakeServer.Stop()`（幂等）。
4. **配置同源的真实含义已改正**：两进程运行时读的是**各自输出目录的 `server.json` 副本**（构建时同源）。
   **代价**：相对 `ScriptPvfPath`/`SkillScriptPath` 会按各自 BaseDirectory 解析；拆分部署必须绝对路径或共目录，
   记为 `D5-CONFIG-PATH`，本波未改 `BUILDING.md`（不在授权内）。

## 4. 门槛结果（本波实际执行，均为当日同一棵树）

| # | 命令 | 结果 |
|---|---|---|
| 1 | `dotnet build tests/DFLegacy.SmokeTests/DFLegacy.SmokeTests.csproj -c Release` | 成功，**0 警告 0 错误**，exit 0 |
| 2 | `dotnet run --project tests/DFLegacy.SmokeTests -c Release --no-build -- --s2-ipc` | `S2 IPC smoke: 0 failures`，12.1–12.5s，exit 0（提交后复跑 12.5s） |
| 3 | `dotnet run --project tests/DFLegacy.SmokeTests -c Release --no-build` | `All DFLegacy protocol smoke tests passed.`，42–51s，exit 0（提交后复跑 49.9s） |
| 4 | 已申报只读工具 `smoke-tests`（= 全量，不带 `--no-build`） | 可用，40.3s，exit 0 |

提交后在**提交树**上把 1/2/3 三条原样再跑一遍，结果同上（0 警告 0 错误 / 0 failures / 全通过）。
提交 hash 见 `git log -1 -- src/DFLegacy.Channel`（本机 ledger 也记了一份，避免本文自引用）。

命令 2 与 4/3 是**两组独立门槛**：全量不含 IPC 组，IPC 必须单独跑并单独记录。
本机 `recon_status` 无配置问题；8 个申报工具里只有 `smoke-tests` 覆盖本波回归，**没有申报 IPC 分组工具**。

### 变异对照（证明门槛能变红）

临时改坏 Channel 的两处（广告地址直接用 `Entrance.Host`、脚本少 1 字节）后跑命令 2：**6 条红**，且
**只有 split（Channel 进程）侧红、mono（Server 进程）侧仍绿** —— 证明断言确实穿过新 exe 而不是空转。
还原后必须**强制重建**：`Copy-Item` 还原会把源文件 mtime 改回更早，MSBuild 判定 up-to-date 并继续跑变异
二进制（实测踩到「源码已还原但门槛仍红」）；`Touch` 源文件或删除对应 `obj/` 后重建即可。

### I2 证明范围

- 通过：同一测试客户端 + 同一真 PVF（`<repo>/DNF2008/Script.pvf`，`etc/channel_info.etc` = 27964 字节）+
  真实两档进程，按**各会话**密钥解码/解压后，协议 10 脚本、协议 3 元数据、协议 6 缓存决定跨模式与跨入口口
  逐字节相等；每会话密钥随机导致的原始密文差异被显式断言并排除。
- **不覆盖**：Tier 2 原始帧全同（D5-RAW-COMPARE）、真机（D5-REAL）、频道口正向玩法路由（D5-WIRING）、
  进程级优雅退出与完整并发安全（D5-START/D5-CANCEL）。

## 5. 提交与审查记录

- 提交方式：逐文件 pathspec。`DFLegacy.Emulator.slnx` 与 `tests/DFLegacy.SmokeTests/Program.cs` 含他人 hunk，
  用「HEAD 版本 + 本任务 1 行」构造 index blob（`git hash-object -w` + `git update-index --cacheinfo`）后
  `git diff --cached` 核对，确认**只**暂存自己那一行，他人的改动仍是未暂存状态。
- `docs/` 被 `.gitignore:15` 挡住但相关文件已跟踪 ⇒ 用 `git add -f -- <明确路径>` 并核对 index；
  新增文档同样 `-f`。
- 独立复审：`.superpowers/sdd/2026-10-19-s2-multiprocess-split-first-slice/task-5-review.md`
  （本机工件，未提交）。结论「I2 证据成立，无必须改生产代码才能提交的问题」。
  F-1/F-2 按 Ruling 1/2 收窄口径并改写断言文案；F-3 修了计划与 `ChannelOptions` 注释；
  F-5/F-6/F-7/F-8 已修测试（非空前置、connect/write 兜底、守卫断言"未 bind"、端口分配去重）。
  F-4 按 Ruling 3 记为 review-only。

## 6. 遗留项当前视图

单一来源：`.superpowers/sdd/2026-10-19-s2-multiprocess-split-first-slice/progress.md` 的
「Task 5（I2）实施与验收」段（本机工件）。已 resolved 并移出当前 deferred：D5-READ、D5-LABEL、D5-STOP、
D5-DIAL、D5-GROUP。

| ID（均 open） | 负责人 | 当前范围 | 触发条件 |
|---|---|---|---|
| D5-START | 共享宿主 + 后续生命周期维护 | 调用方一半已完成（串行、finally、启动失败非零退出）；`StartAsync` 未用 token、Start/Stop 窗口、**优雅退出零覆盖**仍 open | 生命周期调整前补 in-process `StartAsync→Stop` 与真实停止信号 |
| D5-CANCEL | 共享宿主维护 | 会话任务即发即忘（`EntranceHandshakeServer.cs:120`），取消期 OCE 不在会话 catch 内 | 扩展生命周期或出现取消期异常 |
| D5-WIRING | Task 8 / Task 11 | 端口归属与握手族路由已实测；**频道口正向玩法路由只到 code review** | Task 8 接 admin 记账或 Task 11 真机时用真实玩法帧补 |
| D5-CONFIG-PATH | 部署 / 后续任务 | 运行时两份 `server.json` 副本；相对脚本路径按各自 BaseDirectory 解析 | 拆分部署前落定绝对路径或共目录；改部署文档时 |
| D5-STOP-TEST | 后续生命周期维护 | Stop 修复无确定性竞态测试 | 生命周期调整或再现竞态 |
| D5-SENT-ORDER | 测试维护 | 会话计数轮询无显式完成同步 | 复现 flake 或改该断言 |
| D5-ACCEPT-TEST | 共享宿主测试维护 | accept 错误路径缺注入 seam | 修改 accept 异常路径 |
| D5-DEAD | 后续清理波 | 旧入口分支与 `LoadContent` 不可达（本波未清理） | 专门清理波 |
| D5-REAL | Task 11 / I5 | 真机未跑 | I5 前固定构建/输入版本 |
| D5-RAW-COMPARE | Task 11 / I5 | 最终 Tier 2 例外/归一化未固化 | 最终 Tier 2 前 |

没有新增 accepted 风险，未改 spec 门槛。**阻塞门槛未解决项**：D5-WIRING 的玩法路由一半仍 open ——
它不阻塞 I2（spec §6 判据是两档握手脚本字节相同，已满足），但阻塞「频道口玩法接线已验证」这类更宽的声明。

## 7. 未解释 / 需警惕的验证异常

1. **还原后门槛仍红（已解释，但容易误判）**：`Copy-Item` 还原源码把 mtime 改回更早，MSBuild 判定
   up-to-date，继续用变异二进制。判据：`Get-Item <src>.LastWriteTime` 早于 `bin/.../<dll>.LastWriteTime` 即中招。
2. **`server.json` 的 `ScriptPvfPath` 在本机无效**（`D:\DFLegacy\DF2008\Script.pvf` 不存在）。夹具全部用
   `DFLegacy__ScriptPvfPath` 覆盖到仓库内 PVF，所以夹具不受影响；但**直接跑 Server/Channel 默认配置会缺少脚本源**，
   这是本机既有配置事实，不是本波引入。
3. 本波没有观察到 flake：`--s2-ipc` 共跑 4 次（0 failures），全量 2 次，均一致。端口由 `bind(0)` 动态分配并
   去重，无固定端口占用；运行前后 `git status` 一致，`%TEMP%` 无 `dflegacy-channel-*` 残留。

## 8. 下一波只读这些

1. 本文件 §2 的实现口径 + §3 的 Ruling + §6 的遗留项。
2. 计划里的下一个任务段（`## Task 6:` 起）——注意 Task 6–11 全部 `needs-refresh`，进入该波前先核验：
   Task 7 的启动/取消接入与统计投影、Task 8 的合并口径（**不要**把 Received+Sent 当 Server 的 TotalPackets）、
   Task 11 的原始帧对比方法（随机会话密钥 ⇒ 必须显式例外清单或归一化）。
3. `src/DFLegacy.Channel/Program.cs`、`ChannelOptions.cs`、`ChannelSessionSink.cs`（Task 6/7 要接链路与统计）。
4. `.superpowers/sdd/2026-10-19-s2-multiprocess-split-first-slice/progress.md` 最末「Task 5（I2）」段（本机）。
