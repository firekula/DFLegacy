# Task 5 续跑提示词（Recon 0.2.1）

> 用途：供下一次获得执行授权的会话粘贴。本文件生成时 Task 5 未开始；本次整理不执行下面的实现指令。

---

在 `G:/Game/DNF_ACT1/DFLegacy` 继续 S2，只实施 Task 5：`DFLegacy.Channel` 新 exe / I2。本波完成后交接，不提前执行 Task 6–11。

先调用 `recon_status`；无 error 后加载 `recon-implementation-handoff` 与证据纪律。检查申报工具覆盖：全量 smoke 有 `smoke-tests`，IPC 分组要单独命令。若宿主没有 Recon 工具，说明入口缺失，复用本机 Recon 包的配置校验能力或直接读取配置，不伪造工具调用。

先读：

- `docs/superpowers/handoff/2026-10-21-task-5-handoff.md`；
- `docs/superpowers/plans/2026-10-19-s2-multiprocess-split-first-slice.md` 的 Global Constraints、Task 5；
- `.superpowers/sdd/2026-10-19-s2-multiprocess-split-first-slice/progress.md` 最末 Recon 0.2.1 当前表；
- handoff 列出的共享接口/配置来源与 spec §6 I2、§7。按标题/符号定位，先不读整份远期计划。

先 `git status --short` / `git rev-parse HEAD`，对照 handoff 中代码基线 `fdde655` 与本机快照。核心源码若没变可沿用契约；相关输入、配置或补丁变了才刷新相应证据。历史 smoke/pid/回放不是当前提交的新通过。快照缺失时按现有来源重建当前指纹，无法重建的历史结果继续标历史，不编造通过。
若本机 ledger 不可用，以 handoff 当前表为恢复起点，核验对应源码后重建本波 ledger；不要因历史工件缺失而重读整份远期计划。

实施约束：

1. C5 契约均以修订版计划的版本 2 为准。Channel 仅引用 Protocol/InterProcess，Server 服务、Runtime sink 和 ScriptFileSystem 仅供参考；禁止 store/state.json。
2. 读同源配置并在 JSON 后应用环境覆盖；镜像角色所需键的缺省/校验。宿主显式传 MaximumPacketLength、RejectBadCrc32、noDelay:true；不要新增 NoDelay 配置键。
3. 广告地址先按 GameplayDatagram.ResolveAdvertisedHost 的 Host 回退，再用已提交 EntranceDialHost.Resolve。脚本 PVF 优先、缺 PVF 回退 SkillScriptPath；缺脚本 warn，不能继续照抄旧单参数示例。
4. 自建 ChannelSessionSink，保留 entrance:{port} 标签及独立收/发/会话计数。测试工程补 Channel 引用，smoke Program 仅加分组调用。
5. 串行管理启动/停止，把 Start 和 Run 都纳入 finally 清理，覆盖第二端口绑定失败的释放。共享 Start token 未生效、Start/Stop 窗口与未观察会话取消仍是 open，不能声称完整并发安全。
6. 精简表驱动测试核验配置透传、地址和脚本回退；共用一个真实进程/托管夹具验证两种拓扑、各入口的协议路由，以及同一客户端的脚本/元数据内容对比。读包/退出设超时，不为每条断言重复启动服务。
7. 每个会话密钥随机。I2 比较下载脚本与解码内容并检查帧结构；不得据此标最终 Tier 2 原始帧对比通过。真机和最终归一化方法留给 D5-REAL / D5-RAW-COMPARE，但本波 I2 的实际内容对比不能延后到 Task 11 才验。

共享 slnx、smoke Program、Server/Protocol/netdump 有他人改动；先保存/核对相关补丁，按已有授权选择隔离，不整仓 stash 或覆盖别人文件。新增源码写前确认变更清单。这里只授权当前 Task 5，不顺带清理死代码或修改未涉及原生工程。

收尾门槛（实施后才执行）：

```powershell
dotnet build tests/DFLegacy.SmokeTests/DFLegacy.SmokeTests.csproj -c Release
dotnet run --project tests/DFLegacy.SmokeTests -c Release --no-build -- --s2-ipc
dotnet run --project tests/DFLegacy.SmokeTests -c Release --no-build
```

构建覆盖受影响 managed 引用，零警告零错误。全量也可走 `smoke-tests` 申报工具；IPC 单独记录。对变动先跑相关组，收尾全量一次；不要重复用原生 slnx 的 dotnet CLI 失败充当门槛。
若缺测试资产，优先用隔离配置/夹具；需要真 PVF 时两模式共享 `<repo>/DNF2008/Script.pvf`。只清理自己创建的进程/临时配置，不能终止现场既有服务或客户端。

审查 finding 先分来源；计划问题先修计划再派修复。更新 ledger 当前表的稳定 ID，不沿用旧 deferred 原文；resolved 移出当前 deferred，accepted 需已有接受决定，阻塞门槛未解决时 I2 保持未验。
Task 7/8 的生命周期/统计和 Task 11 对比方法都为 needs-refresh，下一波再核验。

本波验收实际通过后，逐文件/逐 hunk 提交本任务与权威计划修订，文档路径使用 `git add -f -- <明确文件路径>` 并核对 index，提交信息中文，不自动推送；核对他人的改动仍在。写 handoff，记录代码/补丁/配置版本、命令、产物位置和证明范围，再换会话。设施不足时交接 open 和具体取数步骤，不宣布 I2 已完成。
