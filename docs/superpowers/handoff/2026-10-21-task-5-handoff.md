# Task 5 Handoff · Recon 0.2.1 恢复核验版

> **[已被取代]** Task 5 已于 2026-10-21 实施并验收；当前视图见同目录
> `2026-10-21-task-5-complete-handoff.md`。本文件保留为实施前的恢复核验记录（其中「Task 5 未开始」
> 等陈述是当时事实，不再有效）。

> 更新：2026-10-21。Task 5 **未开始**；本轮只修改计划、交接和 ledger。
> 下一次经用户授权执行时，使用同目录 `2026-10-21-task-5-continue-prompt.md`。按标题/符号定位，不沿用旧行号。

## 1. 基线与证据版本

- 仓库：`G:/Game/DNF_ACT1/DFLegacy`；分支 `main`；代码基线 `fdde65582a94f8a2011e4097257734ceb39af599`。
- Task 4：`0e38cf2` + `dfbf02e`；历史计划修订 `57d8a05` / `ce43078`；旧交接提交 `fdde655`。
- 当前工作区仍有他人的修改。本轮前快照：`.superpowers/sdd/2026-10-19-s2-multiprocess-split-first-slice/task-5-resume-review/baseline.json`、同目录 `worktree.patch` 和 `untracked/`；补丁 SHA256 `bb27c539369b476738b89960bd31ec24748e52316930a85593748b35bca95eb4`。包含 22 个保护文件指纹；快照供核验，不要直接覆盖当前工作区。
- `server.json`、核心接口、spec、测试源码指纹及 PVF 指纹均在 baseline.json；PVF 为 `<repo>/DNF2008/Script.pvf`，SHA256 `bc397d1463f1f0f749bbd169400b640d0c8a5d1f6e6e97cfda797099f995ac27`。
- 本轮文档修订将单独提交；用 `git log -1 -- docs/superpowers/handoff/2026-10-21-task-5-handoff.md` 查文档提交。后续 HEAD 变化先看是否仅文档变化，按受影响来源决定复核范围。
- 历史 Task 4 全量 smoke / IPC / managed build / pid 83272 结果只归属于当时的混合树；完整工作区指纹未留存，**不是本轮新通过**。本轮核心接口与当前 HEAD 无差异，可沿用其源码契约，不扩大历史运行证据范围。

## 2. 当前产物和结论

| 项 | 当前状态 | 证明范围 |
|---|---|---|
| Task 1–4 实现 | 已提交，历史审查/修复见 ledger | 本轮未重做独立审查或运行验收 |
| 入口 CRC/长度/NoDelay | 宿主实现已补回；Server 已传 CRC/长度，NoDelay=true | `EntranceHandshakeServer.cs:11,132-150`；Task 5 仍需验证自身透传 |
| Stop 的集合竞态 | 已修该竞态，无确定性覆盖 | 不代表 Start/Stop 整体并发安全，也不代表已等待所有会话任务 |
| Task 4 回放 | 录制协议 6/3 的部分内容期望 + 生成请求，协议 10 使用 4096B 合成脚本 | 直接 new 宿主；未覆盖服务接线、原脚本重新下载或全部原始线上字节 |
| Task 5 / I2 | 尚未实现与验收 | 本轮修正了文档契约，不将文档更新记为实现完成 |
| 完整 Tier 2 / Tier 3 | 未验证 | 留给 Task 11/I5；I2 的脚本对比也必须在本波实际完成 |

## 3. 下一波必须使用的契约

权威文件：`docs/superpowers/plans/2026-10-19-s2-multiprocess-split-first-slice.md` 的 Global Constraints + Task 5；C5 契约版本均为 2。

1. **可直接引用**：Protocol 内宿主、内容工厂、端口分族、拨号策略、sink 接口、PVF reader；InterProcess 库。**仅参考**：Server 内 `EntranceHandshakeService` / `RuntimeEntranceSink` / `ScriptFileSystem`。Channel 禁止引用 Server、store 或写 state.json。
2. 透传 `MaximumPacketLength`、`RejectBadCrc32`；`noDelay:true` 是既有常量。新窄配置读取同源 JSON，并在其后应用环境覆盖；撤下旧仅覆盖两键和 `Read(JsonElement)` 示例。
3. 广告地址先沿用 GameplayDatagram 的 Host 回退，再用已提交的 `EntranceDialHost.Resolve`；旧 IsDialableHost 仅工作区来源已归档，当前权威不依赖它仍存在。
4. 脚本来源镜像 Server：PVF 优先，缺 PVF 回退 SkillScriptPath 目录，相对路径以 AppContext.BaseDirectory 解析；缺虚拟文件由调用方 warn。旧把 ScriptPvfPath 当目录的单参数示例已撤下。
5. 启动/取消串行管理，Start/Run 都纳入 finally 清理。不要把共享宿主 Start token 当成已生效，或沿用旧 CancellationToken.None 永久运行示例。
6. Files 已补 `tests/DFLegacy.SmokeTests/DFLegacy.SmokeTests.csproj` 的 Channel 引用。接线测试须触达真实服务/进程，单测 Split 或检查三个端口同 pid 不足。
7. I2 对比同一测试客户端在两模式下载的脚本和频道元数据；每会话随机密钥按本会话解码，保留帧结构检查。该证据不等于最终 Tier 2 原始帧全同，随机差异方法见 D5-RAW-COMPARE。
8. Task 6–11 都标 `needs-refresh`；Task 7/8 尤其复核生命周期和收包统计，不能把 Received+Sent 直接当 Server 的 TotalPackets。

## 4. 遗留项当前视图

单一来源：`.superpowers/sdd/2026-10-19-s2-multiprocess-split-first-slice/progress.md` 最末“Recon 0.2.1 恢复核验 / 当前状态”。本表从该段提取；更新状态时同步提取视图，历史过程留在 ledger。
已解决、移出 deferred：D5-READ, D5-LABEL, D5-STOP, D5-DIAL, D5-GROUP。它们分别对应宿主读参数、会话标签、Stop 集合竞态、拨号来源归属和双测试组 gate 的文档遗漏；后续实现/更广范围仍按本表验收。

| ID（均 open） | 负责人 | 当前范围 | 触发条件 |
|---|---|---|---|
| D5-STOP-TEST | 后续生命周期维护 | 上述 Stop 修复无确定性竞态测试；历史多次绿不能替代确定性覆盖 | 生命周期调整或再次观测竞态时补同步/故障注入验证 |
| D5-START | Task 5 | Start token 未用、Start/Add 与 Stop 有窗口、多端口失败不回滚；Task 5 需串行宿主生命周期并用 finally 收尾 | Task 5 启动失败/取消验收前；共享 API 更广并发保证另行处理 |
| D5-CANCEL | Task 5 / 后续共享宿主维护 | 会话任务未跟踪，取消期 OCE 不在会话 catch 内；Run 退出不证明所有会话任务均已观察并回收 | Task 5 取消用例若出现故障先处理；全会话等待在扩展生命周期时复核 |
| D5-SENT-ORDER | 测试维护 | 已有约 1 秒轮询及 volatile 计数读取；仍无显式完成同步，不能写成确定性 happens-before 覆盖 | 复现 flake 或变更该断言时采用显式同步 |
| D5-ACCEPT-TEST | 共享宿主测试维护 | 真实 accept 错误已 warn+continue；该路径缺确定性错误注入测试，旧无牙指覆盖缺口 | 修改 accept 异常路径或出现线上失败时补注入 seam |
| D5-WIRING | Task 5 | 已有 Split/直接宿主测试与历史 pid 检查，未覆盖两模式真实服务接线 | Task 5 / I2 完成前必须实际验证每个角色端口及协议路由 |
| D5-DEAD | 后续清理 | 旧入口分支与 LoadContent 在现行绑定下不可达；本波不顺带清理 | 专门清理波或重新引入调用前复核 |
| D5-REAL | Task 11 / I5 | 改造后原客户端双档 town-two-clients 与全链路真机未跑；netdump 来源未提交且本轮未验证二进制 | I5 前固定构建/输入版本后取数；可用固化工作区构建，不把必须提交他人改动当先决条件 |
| D5-RAW-COMPARE | Task 11 / I5 | 随机会话密钥导致原始密文不同；最终 Tier 2 的例外或归一化尚未固化，不把 I2 解码内容对比扩大为 Tier 2 通过 | 最终 Tier 2 前对照批准规范明确方法，需改门槛时记录已有授权或取得决定 |

这些项没有被本轮风险接受。允许开始已明确的实现，I2 完成前必须取得接线、脚本对比、非默认参数透传与必要启动清理的实际证据。

## 5. 续跑读集与验证

1. 本文件与同目录 continue-prompt；计划 Global Constraints、Task 5（按 `^## Task 5:` 定位）。
2. `src/DFLegacy.Protocol/EntranceHandshakeServer.cs`、`EntranceContentFactory.cs`、`EntranceDialHost.cs`、`EntrancePortFamilies.cs`、`EntranceSessionSink.cs`、`PvfArchiveReader.cs`。
3. Server 的 `EntranceHandshakeService.cs`、`ServerOptions.cs`、`ProcessOptions.cs`、`ScriptFileSystem` 路径/来源段、`Program.cs` 配置段；两份 smoke csproj/Program 与 `EntranceSplitSmokeTests` 对应测试。
4. spec §6 I2、§7 的验收边界，以及 ledger 最末当前表。只有契约涉及的差异才扩读；不通读远期完整代码。

实施后必跑：受影响 managed build、Release `--s2-ipc`、Release 全量 smoke；命令与最少测试矩阵见 Task 5 Step 1/3。全量申报工具 `smoke-tests` 不含 IPC；两份结果都要记录。
本轮宿主没有 DSH 原生 Recon 工具入口，复用本机 Recon 包的 loadConfig/validateConfig 校验项目配置：8 个申报工具，零问题；它们覆盖实验/游戏回归，本轮源码与文档核验不需要调用。另执行源码/差异、文档一致性与交接摘要检查；没有启动游戏进程或跑游戏测试。原生全解 build 不作本波 gate。

## 6. 保护工作区与提交状态

- 快照记录了本轮前全部可见 dirty/untracked 文件。重点共享 slnx、smoke Program，另有 Server/Protocol/netdump 修改；执行前重新比较，不能恢复整份旧文件覆盖后来的工作。
- 历史允许就地执行的裁定见 ledger；新波自行核验隔离可行性及已有授权，避免整仓 stash。共享文件只纳入本任务 hunk，逐文件提交。
- `docs/` 被 ignore：文档使用 `git add -f -- <明确文件路径>`。本轮普通 add 已跟踪路径虽入 index，仍因忽略父目录返回 1，故须核对暂存结果。ledger、快照和摘要属于忽略的本机工件。
- 本轮文档提交只纳入计划、handoff、continue-prompt。未提交源码的归属与指纹继续保留；本轮没有实施 Task 5。
