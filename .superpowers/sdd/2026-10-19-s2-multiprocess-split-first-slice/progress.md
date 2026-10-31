# SDD ledger — plan: docs/superpowers/plans/2026-10-19-s2-multiprocess-split-first-slice.md

Spec: docs/superpowers/specs/2026-10-19-multiprocess-split-s2-design.md（所有者已批准）
Base commit when execution started: 593600c (branch: main)

## Rulings（开工前）

Ruling: 在 `main` 上就地执行，不建 worktree — 理由：①`/docs` 被 `.gitignore:15` 挡住，
spec/plan/ledger 都在被忽略的目录里，worktree 会看不到它们；②`DNF2008/`、`data/`、`dist/` 等运行期
资产也未入库，worktree 里跑不了真机/验收；③每个任务自带一次提交，可逐个 revert。代价若错：
main 上多出可直接回滚的提交，回滚成本 = `git revert` 若干次。

Ruling: 任务简报用「计划文件的行区间」而不是复制一份 brief 文件 — 理由：计划本身已把每个任务的
全部代码写全，复制一份只会产生两处真相；代价若错：子代理可能读到相邻任务的内容（用精确行区间规避）。

Ruling: 我的 dispatch 工具没有 model 参数（`spawn_teammate` 只接受 name/description/prompt/context），
因此「显式指定模型」这条降级为「在 prompt 里声明期望档位」。代价若错：某些任务可能用了不必要的高档模型。

## 预检冲突扫描

### 跨任务共享文件 / 接口

| 两端 | 一方产出 → 另一方消费 | 结论 |
|---|---|---|
| T1 → T6/T7/T8 | `RegisterPayload`/`RegisterAckPayload`/`StatusPayload` 与其 `Encode/TryDecode` | 一致；T8 已改为消费 `LastRemoteStatus` |
| T1 → 全链 | `InterProcessLink.Create(type=0x7b, recordCount=1, no crc)` | 一致；T6 断言 `TotalLength==49`、T7 断言端到端 |
| T2 → T5 | `server.json` 的 `Processes` 段 | T5 的窄模型读 `Processes.Channel.LinkPort`，默认 7004；顺序 OK |
| T3 → T4/T5 | `EntranceHandshakeServer` / `IEntranceSessionSink` / `EntranceDialHost` | 一致 |
| T4 → T5 | `EntranceContentFactory`（T4 创建，T3 的 Files 未列） | **发现：T4 的 Files 头里漏列该文件** → 已由 dispatch 明确写出 |
| T5 → 冒烟工程 | T5 的测试需要引用 `DFLegacy.Channel` | **发现：T5 的 Files 未列 `tests/DFLegacy.SmokeTests.csproj`** → 由 dispatch 明确补齐 |
| T1/T2/T3/T5/T6/T8 → `tests/.../Program.cs` | 同一 `--s2-ipc` 组里各插一行 | 一致；要求「插入，不重写」 |
| T2/T4/T8 → `src/DFLegacy.Server/Program.cs` | `Validate()` / hosted service / 链路 + `/api/status` | 顺序加法，无冲突 |
| T1/T5 → `DFLegacy.Emulator.slnx` | 各加一个 `<Project>` | 加法，无冲突 |
| T9 → T7 | `protocol/` 的 S2S 表要与 §5.2 的四条报文一一对应 | 一致（4/128/129/130） |

### 每任务自洽

| 任务 | 自洽性 | 结论 |
|---|---|---|
| T1 | 测试里的期望 hex 与 `Encode()` 是否等长 | **发现：原稿多一个 `00`（50 字节，应为 49）→ 已在计划里修正为 `"01"+"0200"`** |
| T2 | `Split` 的 4 条断言 vs 实现（含 channel 端口不在列表时也返回） | 一致 |
| T3 | 测试断言 `Body.Length==37`（=1+36）vs `CreateKeyReply` 的 36 字节载荷 | 一致 |
| T4 | 端口列表改动 vs 「不碰 `HandleClientAsync`」 | 一致（只改 `ExecuteAsync` 前 10 行） |
| T5 | `ChannelOptions.Read` 读到的键 vs `server.json` 实际键 | 一致（含 T2 新增的 `Processes`） |
| T6 | `Status` 回调 + 双向心跳 vs 测试（对端要回 STATUS 才不判 down） | **发现：原稿单向心跳必误判 → 已改双向并同步 spec** |
| T7 | 服务端 `StatusLoopAsync` 的间隔可配 vs T6 测试用 50ms | 一致（ctor 带 `statusInterval`） |
| T8 | 投影用 `LastRemoteStatus` vs T6 的断言 | 一致 |
| T9 | 行里的 `status/tier` 是否在 `verify.py` 枚举内 | **发现：原稿用了 `[设计]/[字节]`，不在枚举内 → 已改 `mapped`/`T4_fields-read+write`** |
| T10 | `Manifest` 字段与 `load_scenario` 签名 | **发现：原稿是猜的 → 已按 `model.py:91-104,149` 重写** |
| T11 | 手工流程，无代码 | 一致 |

## 进度

（每任务完成后在此追加一行 `Task <N>: complete (...)`）

Task 1: dispatched (BASE d83d5e7, implementer impl-task-1)
Task 1: Ruling: 期望帧 hex 以**实测通过的 49 字节串**为准（`"7B803100000000000000"+"01"+...`）—
  我先前那次"修正"（commit d83d5e7）把 header 字面量当成 10 字节，实际它当时只有 18 hex 字符=9 字节，
  于是删掉 `0001` 的补位后变成 48 字节；实现者把 header 补成 20 字符并保留 `01`（记录数=1），
  实测 `TotalLength==49` 且 hex 逐字节相等。**教训：字节数必须用工具数，不能靠眼看 hex 字符。**
  代价若错：帧多/少一字节，Tier 1 与真机全红。
Task 1: implementer reported DONE_WITH_CONCERNS (commit 68f2949, "S2 IPC smoke: 0 failures",
  主工作区与独立 worktree 各一次)。关注点=上面的 hex 分歧，已裁定为"实现正确、计划已改回"。
Task 1: review (review-task-1): SPEC ✅ / QUALITY Issues — Critical 0, Important 1, Minor 9.
  ⚠️ items I resolved myself: `dotnet build src/DFLegacy.InterProcess` = 0 警告 0 错误；
  `dotnet run --project tests/DFLegacy.SmokeTests -- --s2-ipc` = "S2 IPC smoke: 0 failures", EXIT=0 ⇒ 绿灯属实。
  Important: ACK/STATUS/NOTICE 只验长度、NOTICE 全计划无字节级覆盖（属计划文本缺口）。
Task 1: Ruling: Important 不 deferred —— 帧布局错会一路污染 T6/T7 的 Tier 1，代价高；补"往返 + 字段偏移"断言即可
  （用 BinaryPrimitives 读回，不手算整串 hex，避免再次人工数错）。代价若错：多花一轮修复，收益是帧布局被钉死。
Task 1: minor (deferred): 4 段解码样板重复；AsciiFixed 与 EntranceMetadata.WriteFixedAscii 重复；
  非 ASCII 静默折叠为 '?'；定宽读写边界不对称（16 字符主机可读不可写）；int/uint 不一致；
  缺 [NotNullWhen(true)]；InterProcessLink 不校验 RecordCount 与"长度-报文号"对应。
Task 1: fix round 1/5 dispatched (resume impl-task-1, 只改测试文件)
Task 1: fix round 1/5 (1 addressed, 0 open — 往返+偏移+帧长三条断言；commit 6a72a48)。
  实现者另做了变异验证：临时改错三处偏移 → 精确红 3 条对应断言 → 还原复绿。
  Lead 独立复跑：`dotnet run --project tests/DFLegacy.SmokeTests -- --s2-ipc` = 0 failures / EXIT=0。
Task 1: scoped re-review (rereview-task-1): ADDRESSED 3/3, NEW BREAKAGE none, VERDICT clean。
  残余风险（入 deferred）：三条帧长断言被"11+payload"逻辑蕴含；`RegisterPayload.TryDecode` 仍无直接断言。
Task 1: complete (commits d83d5e7..6a72a48, review clean)

Task 2: dispatched (BASE 6a72a48, implementer impl-task-2)。开工前发现
  `src/DFLegacy.Server/Program.cs` 与 `tests/.../Program.cs` 上有**别人未提交的改动**（另有并行工作），
  指令用 `git stash push -- <两文件>` 隔离 → 编辑 → 提交 → `git stash pop` 还原。
Task 2: implementer reported DONE_WITH_CONCERNS (commit 2aee1d9, "S2 IPC smoke: 0 failures",
  7 文件 +121/−0；stash pop 后两个被保护文件恢复为 ` M`，`git stash list` 空)。
Task 2: review (review-task-2): SPEC ✅ / QUALITY Issues — Critical 0, Important 1, Minor 6。
  Important: 越界 `LinkPort` 校验无可变红断言（计划测试块缺口）。
　⚠️ 7 条复查：我独立处理了其中 4 条（提交范围 7 文件已核、stash 还原已核、`--s2-ipc` 复跑 0 failures、JSON 由审查者用 PowerShell+Python 两套解析比对）。
Task 2: Ruling: 把 2 条 Minor（恒真 `check(true,…)`、保序断言无判别力）**折进同一个修复轮** —
  同一文件、同一类问题（断言不具判别力），单开一轮只增加轮次不增加信息。代价若错：修复 diff 略大，复审面变大。
Task 2: fix round 1/5 (2 addressed, 1 open；commit 6c352da，单文件 +22/−1)。
  实现者做了变异对照：删 LinkPort 判据 / 加 OrderBy → 恰好 3 条新增断言变红。
　再复审 (rereview-task-2)：2/3 ADDRESSED，剩 `:60` 的"回显刚赋的值"仍恒真。
Task 2: fix round 2/5 (1 addressed, 0 open；commit 82c491c，单文件 +5/−3)。
  实现者变异验证：令 `Validate()` 对合法拆分误抛 → 恰好 1 条具名失败。
　再复审 (rereview-task-1 复用，团队名额已满 8)：ADDRESSED 1/1，NEW BREAKAGE none，VERDICT clean。
Task 2: Ruling: 团队名额 8/8 打满后**复用已空闲的复审者**而不是等待或新建。代价若错：复审者上下文里残留 Task 1 的信息，
  可能影响中立性；本次其结论给出了具体变红路径，可核。
Task 2: minor (deferred): `EntrancePortFamilies` 越界 channelPort 守卫零覆盖；
  `Game.EntranceBinding` 的 `false/false` 第三态无消费者（交 T3/T4 裁决）；
  `Processes`/`Channel` 显式 null 时抛 NRE；`LinkPort` 未与既有端口查重。
Task 2: complete (commits 6a72a48..82c491c, review clean)

## 阶段性任务 I1（所有者 2026-10-19 指定）= Task 3 + Task 4
验收：①现有 smoke 全绿零回归；②单体模式下 2311/7001/8080 仍全部监听且同属一个 pid；
③客户端握手字节流不变（Tier 2 第一半证据）。中途不向所有者确认，逐任务走完整 SDD 循环。

Task 3: dispatched (BASE 82c491c, implementer=impl-task-1 复用)
Task 3: implementer DONE_WITH_CONCERNS (commit 5a50b9b, 5 文件 +232/−0, `--s2-ipc` 0 failures / EXIT=0)。
  关注点：计划版 `EntranceDialHost.IsDialable` 与既有 `EntranceService.IsDialableHost`（`EntranceService.cs:17680-17689`）语义不同：
  主机名（计划：不可拨 / 既有：可拨）、广播地址（计划：排除 / 既有：不排除）。
Task 3: Ruling: **以既有实现为准** —— `EntranceDialHost.IsDialable` 必须逐字镜像 `IsDialableHost`。
  理由：Task 4 会把调用点换成它，语义不一致就会悄悄改掉客户端拨号目标，直接违反 I1 验收③（客户端握手字节不变）。
  计划文本已同步改正（原来那句"原样抽出"是我写的未经验证的判断）。代价若错：主机名/广播地址两种部署下客户端拨错目标。
Task 3: fix round 1/5 (commit 88338f2, 2 文件 +26/−6)。实现者变异验证：临时 `return true;` 让三条假值断言各红一次。
  Lead 独立复核：镜像实现与断言均在位，`--s2-ipc` 0 failures / EXIT=0。
Task 3: review (review-task-2): SPEC ✅ / QUALITY Issues — Critical 0, Important 2, Minor 9。
  Important ①：回复读无超时 ⇒"宿主不回包"会让套件**挂起**而非变红。
  Important ②：Stop-without-cancel、未知协议 warn/不回包、Dispose 三条行为零覆盖（代码正确）。
  Minor 里最重要一条 **M-7（计划级 bug）**：`RuntimeEntranceSink.Received` 漏 `runtime.CountPacket` ⇒
  `/api/status.TotalPackets` 会丢掉全部握手包。**计划已修**（改为调用 `runtime.CountPacket(session)`）。
Task 3: Ruling: M-7 在派修复轮**之前**先改计划（否则 Task 4 会照旧版实现，等于用一行代码改坏 admin 计数）。
  代价若错：plan 与实现的唯一真相被短暂分叉，已在同一提交前改正。
Task 3: minor (deferred): NoDelay/MaximumPacketLength/RejectBadCrc32 未镜像；取消期 OCE 未吞；
  `SentCount` 断言有 happens-before 竞态；`StartAsync` token 未用且多端口失败不回滚；`Stop()` 与 `RunAsync` 并发 Clear 竞态；
  `service` 硬编码 "entrance"（T4 会把 `entrance:2311/8080` 标签退化成 `entrance`）；`EntranceDialHost` 注释与实现不符；报告行数写反。
Task 3: fix round 2/5 dispatched (I-1 超时 + I-2 三条覆盖；只改测试文件)
Task 3: fix round 2/5 (commit 1a692a6, 仅测试文件 +126/−14)。6 条新断言逐条变异验证均可红；
  Lead 复核：`--s2-ipc` 0 failures / EXIT=0，**elapsed=7s（无挂起）**，5s 是"无回包"观察窗。
Task 3: **未完成 —— 等修复轮 3**。实现者在本轮发现一个**实现缺陷**（不是测试问题）：
  `Stop()` 中断挂起的 accept 抛的是 `SocketException(OperationAborted)`，而 `AcceptLoopAsync` 只捕
  `OperationCanceledException`/`ObjectDisposedException` ⇒ `RunAsync` 在 Stop 之后**fault**（实测可复现）。
  Ruling: 在 Task 3 内用修复轮 3 补掉（accept catch 里加 `SocketException`），并要求测试断言
  "Stop 后 `RunAsync` **无异常**完成"（当前测试只钉了"run 先完成"，属弱断言）。
  理由：T5/T7 会 `Stop(); await RunAsync`，现在会拿到异常；推迟到 T5 才修等于把缺陷带过两个任务。
  代价若错：多一轮修复；不修的代价是 T5/T7 在关闭时序上踩雷。
  断点：下个会话直接对 impl-task-1 发"修复轮 3"（要改 `src/DFLegacy.Protocol/EntranceHandshakeServer.cs`
  的 accept catch + 测试断言），然后跑限定复审，再把 Task 3 标 complete，之后进 Task 4。
Task 3: fix round 3/5 (commit 8efd56b, 2 文件 +24/−6)：accept catch 补 `SocketException`；测试升级为
  "Stop 后 RunAsync **无异常**完成"（含 2s 超时兜底，不会挂）。实现者变异验证可精确复现 995。
  Lead 复核：`--s2-ipc` 0 failures / EXIT=0 / 7s；恰好 2 文件。
Task 3: 再复审 (rereview-task-2)：ADDRESSED 2/2 但 **VERDICT open** —— 无 filter 的 `catch (SocketException)`
  会把 10054/10055/10024 等**真实 accept 错误**也吞掉并 `return` ⇒ listener 永久停、`RunAsync` 正常返回，
  T4 后台服务/T5 进程会**静默退出**；且现有测试对此全绿。仓库先例 `RelayHost.cs:48-52` 是 warn+continue。
Task 3: Ruling: 静默死亡 > 原缺陷 ⇒ 必须收窄（`when (SocketErrorCode == OperationAborted)` + 其它 SocketException
  打 warn 后 `continue`）。理由：本项目最忌"静默失败"，而这一点**测试抓不住**，只能靠 code review 兜。
  代价若错：真实 accept 错误会不断打 warn 刷日志，但服务不会静默死。
Task 3: fix round 4/5 (commit c6189a6, 仅 EntranceHandshakeServer.cs +9/−5)。
  Lead 复核：`:80` 有 `when (...OperationAborted)`、`:84-89` 为 warn+continue；`--s2-ipc` 0 failures / EXIT=0 / 7s。
  实现者如实记录：删掉收窄退回无 filter 版后 `--s2-ipc` **仍 0 failures** ⇒ 该回归**测试无牙**，只能靠 review；
  已写成 deferred（需先引入 accept 错误注入 seam，不在本刀范围）。
Task 3: fix round 4 的限定复审 = **clean**（ADDRESSED 2/2，NEW BREAKAGE none；确认只有 OperationAborted
  走 return、其余 SocketException 走 warn+continue，且 Stop 断言仍非恒真）。
Task 3: complete (commits 82c491c..c6189a6, review clean)
  deferred（最终整支审查 triage）：NoDelay/MaximumPacketLength/RejectBadCrc32 未镜像；取消期 OCE 未吞；
  `SentCount` 断言 happens-before 竞态；`StartAsync` token 未用且多端口失败不回滚；`Stop()`/`RunAsync` 并发 Clear 竞态；
  `service` 硬编码 "entrance"（T4 会让 `entrance:2311/8080` 标签退化成 `entrance`）；`EntranceDialHost` 注释与实现不符；
  **真实 accept 错误被静默吞掉这条回归测试无牙**（需 accept 错误注入 seam 才能覆盖，不在本刀范围）。

## 历史断点：Task 4（已由后文 Task 4 执行记录取代）
进 Task 4 时**必须**使用计划当前文本（已修两处）：
① `RuntimeEntranceSink.Received` 走 `runtime.CountPacket(session)`；
② `EntranceDialHost.IsDialable` 镜像 `EntranceService.cs:17680-17689`。
另外 Task 3 的 deferred「`service` 硬编码 "entrance"」正好在 Task 4 兑现：要求 sink 带上本地端口
（`entrance:2311` / `entrance:8080`），否则 admin 的 `/api/sessions` 标签会退化。

## Task 4（I1 收口）—— 2026-10-21

Task 4: dispatched (BASE d9a8986, 执行者=Lead 本人；复审=review-task-4 独立 fresh 上下文)
Task 4: 隔离：`git stash push -m s2-task4-preserve -- src/DFLegacy.Server/EntranceService.cs src/DFLegacy.Server/Program.cs tests/DFLegacy.SmokeTests/Program.cs`
  → 编辑 → 逐文件 pathspec 提交 → `git stash pop`（三个文件均 auto-merge 成功，无冲突；`git stash list` 空）。
Task 4: 开工前交了两张表（contract inventory 18 行、plan claim table 8 行）+ 证伪假设一句话 + 3 条 Ruling，所有者全部批准。
Task 4: 关键发现（HEAD vs 工作区）：`EntranceService.ResolveChannelDialHost` / `IsDialableHost` **只存在于工作区**
  （`git grep -n 'ResolveChannelDialHost\|IsDialableHost' HEAD -- src tests` 无命中）；HEAD 的 `LoadContent` 直接把
  `channel.Host` 当拨号目标（HEAD `EntranceService.cs:17643`）。⇒ handoff 里"镜像 EntranceService.cs:17680-17689"
  这条断言的来源是**未提交改动**，不是已提交事实。录制真机基线解出的拨号地址是 192.168.3.177，说明该改动当时已在运行。
Task 4: Ruling: **不动 `EntranceService.LoadContent`**，频道信息一律走已提交的 `EntranceDialHost.Resolve` —
  理由：拆分后入口分支不可达；动它会与工作区里未提交的 hunk 正面冲突；本任务的提交必须能在 HEAD-only 状态下编译。
  代价若错：若那份未提交改动被丢弃，本进程仍用"修正后"的拨号目标（好于 HEAD 行为），与树内其余部分短暂不一致但不会静默变坏。
Task 4: 字节证据（[抓包] + [回放]，两次独立复现）：3 个 run / 5 个 netdump、4 个不同会话密钥
  （8459C3…/4D3CF1…/9452A1…/0F4437…）解出同一明文 —— proto3 = 652B sha256 `8E059F3A…A0EE3493`
  （解密块 656B，末 4B 为 AES 块填充，块 sha256 `59FC373C…7DB08CD`）、proto10 脚本 27824B sha256 `C8174C…886F0`、
  proto6 明文 = flag 0 + `dfls-local` + 零。解码反证：`EncryptCompressed(明文, 录制密钥)` 与录制密文逐字节相同。
Task 4: Ruling: 除计划 Step 1 的端口归属外，**新增三组断言**（sink 记账 / 内容组装镜像 / 录制字节回放），
  并改 Task 3 文件让会话标签带本地端口（`entrance:{localPort}`）。理由：mirror 断言必须可红、I1③ 需要字节证据、
  admin `/api/sessions` 标签不能退化（T3 deferred 要求在 T4 兑现）。代价若错：测试面/复审面变大 —— 已用变异验证把归因钉死。
Task 4: Ruling: `RuntimeEntranceSink` 用 `public`（计划写 `internal`）— 仓库无 `InternalsVisibleTo` 先例，测试需要可见。
  代价若错：公开 API 面略增（无行为影响）。
Task 4: 变异验证（一次只改一处，复原后复绿）：绕开 `CountPacket` → 2 红；工厂拨号地址漂移 → 2 红；
  宿主标签硬编码 → 1 红；工厂去掉缓存 → 1 红；`Split` 不排除 channel 口 → 5 红（含我新加的 3 条）。
  注：第一次"去缓存"变异是**空变异**（`Get()` 顶部 null 快路径仍在），改成同时去掉快路径才红 —— 记录以免后人误以为该断言无牙。
Task 4: 发现并修掉 Task 3 遗留缺陷：`EntranceHandshakeServer.Stop()` 与 `RunAsync` 的 `finally` 并发枚举/清空
  同一个 `List` ⇒ `--s2-ipc` 随机红（"Collection was modified"）。改为「取快照 → 释放锁 → 逐个 Stop」，`Stop()` 幂等。
  5 次连跑 0 failures。**该回归测试无牙**（时序相关，无法确定性复现），只能靠 code review + 反复运行兜 → deferred。
Task 4: 测试：`--s2-ipc` 0 failures（约 11s，不挂起）×5；全量 smoke 走申报工具 `smoke-tests`（Release）→
  `All DFLegacy protocol smoke tests passed.` exit 0 / 81s。
Task 4: 端口真绑定（单体）：2311 / 7001 / 8080 全部 Listen 且同属 pid 83272；日志显示
  `EntranceHandshakeService` 绑 2311+8080、`EntranceService` 绑 7001（验收②）。
  运行需 `DFLegacy__ScriptPvfPath` 覆盖：`server.json:58` 指向本机不存在的 `D:\DFLegacy\DF2008\Script.pvf`，资产实际在 `<repo>\DNF2008\Script.pvf`（`Program.cs:21-25` 支持环境变量覆盖）。
Task 4: Ruling: 全解 build gate 降级为「所有 managed 工程 0 警告 0 错误 + 全量 smoke 绿」。
  理由：原生 vcxproj 在 dotnet CLI 下必失败（`MSB4278` 缺 VCTargetsPath，需要 MSBuild.exe），且 slnx 上有**他人未提交**的
  `DFLegacy.Ijl15.Tests.vcxproj` 条目。代价若错：原生工程的编译问题不在本 gate 暴露（与本任务无关，且原本也没在本任务 gate 里）。
Task 4: 未解释的验证异常（已解释）：全量 smoke 首跑 1 红 `NOTI 11 serializes ... peer endpoint layout` ——
  原因是 stash 窗口内他人的**测试期望**（HEAD，little-endian）与他人的**生产改动**（工作区，network order）不匹配，
  与本任务无关；`git stash pop` 还原后复跑全绿。
Task 4: complete (commits d9a8986..0e38cf2, 一次提交 8 文件 +417/−13)
  deferred：
  - `EntranceHandshakeServer.Stop()` 并发安全无确定性测试覆盖（时序相关），只能靠 review + 反复运行；
  - `IsDialableHost` 仍是"仅工作区"事实：若那份未提交改动被丢弃，`EntranceDialHost.IsDialable` 的镜像对象在 HEAD 里不存在，需重评；
  - 真机重跑 `town-two-clients` 未做（netdump 依赖未提交的 `src/DFLegacy.Ijl15/src/net_dump.cpp`），I1③ 的端到端证据止于「录制真机输入 + 回放」；
  - `EntranceService` 的入口分支（`:15375`）与 `LoadContent()` 拆分后不可达，属死代码，留待后续清理（不在 I1 范围）。

## [作废] 旧 Task 5 接续摘要（保留历史；当前以本文末 Recon 0.2.1 段为准）
进 Task 5 时必须知道的三件事（Task 4 实测）：
① `EntranceHandshakeService` + `RuntimeEntranceSink` + `EntranceContentFactory` 已就位，channel 进程可直接复用；
② 会话标签由宿主拼成 `entrance:{localPort}`，channel 侧的 sink 实现要接受同样形状；
③ `EntranceHandshakeServer.Stop()` 现在是幂等且并发安全的，可以直接在 channel 宿主的 shutdown 路径调用。

Task 4: 复审（review-task-4，independent fresh）：SPEC ✅（①②③ 逐条）／QUALITY Issues — Critical 0，Important 1，Minor 7。
  Important（已自核属实，非转述）：旧**共用**读循环对入口族也执行 `options.RejectBadCrc32`（HEAD `EntranceService.cs:4432`，
  与共用 `CountPacket :4428` 同层）与 `options.MaximumPacketLength`（`:4201`）、`client.NoDelay=true`（`:207`）；
  新宿主三样都漏了。⇒ 开该开关的部署会从"坏 CRC 断会话"变成"继续处理"。属 plan-origin（Task 3 的宿主接口没带这些参数）。
Task 4: Ruling: 把三个读循环参数补进宿主构造（而不是记为已接受差异）— 理由：I1 的门槛是"行为不变"，
  且这三项都是旧共用路径上的既有行为，补回来才是"不变"；shipped 配置下字节无差（MaximumPacketLength 恰等于默认 4 MiB、
  RejectBadCrc32=false），所以风险面只在非默认部署。代价若错：宿主构造多三个参数、复审面变大。
Task 4: fix round 1/3 (commit dfbf02e, 4 文件 +111/−15)：
  ① 宿主增加 `maximumPacketLength`/`rejectBadCrc32`/`noDelay` 并按旧口径使用（CRC 拒绝位置在 `session.Received` 之后、
     与旧循环一致）；② Service 从 options 传入；③ 拆分模式日志改 warn + 列出被忽略的端口（spec §8 R2，复审 Minor）；
     ④ 新增 `RunCrcRejectionAsync`（开/关两个方向 + 长度上限）；⑤ 测试读取器容忍 RST（宿主带未读字节断开会触发
     Connection reset，原 catch 会让套件崩而不是具名失败 —— 这是新增"超长帧"用例暴露出来的）。
  变异验证：CRC 开关硬编码 off → 1 红；硬编码 on → 1 红（双向都可判别）；`maximumPacketLength` 忽略 → 1 红；复原后复绿。
Task 4: 复审 Minor 处置：Minor 2（拆分模式日志）已改 warn 并列出端口；Minor 3（用例措辞与 d9a8986 不符）已改；
  Minor 7（注释行号漂移）已改成 HEAD/工作区双标注；Minor 1（NoDelay/MaxLength）随 Important 一起修掉。
  Minor 4（StartAsync 先 Start 再入锁的残留窗口，当前两个调用点不可达）、Minor 5（`EntranceHandshakeService` 从未被测试实例化，
  端口接线在被"对调"时仍会通过）、Minor 6（新断言只在 `--s2-ipc` 组，全量 smoke 不跑）→ 记 deferred。
Task 4: 最终测试（提交后、stash 已还原的树上）：`--s2-ipc` 0 failures；managed build 0 警告 0 错误；
  全量 smoke（申报工具 `smoke-tests`，Release）`All DFLegacy protocol smoke tests passed.` exit 0 / 82s。
Task 4: complete (commits d9a8986..0e38cf2 + 修复轮 dfbf02e，共 2 提交；计划 57d8a05；handoff 54475e9)
  追加 deferred：入口面读循环三参数补回后仍无"超长帧/坏 CRC 在真机上"的对照；`StartAsync` 残留窗口；
  `EntranceHandshakeService` 的托管接线无自动化覆盖（只靠真绑定 pid 检查 + code review）；新断言不在全量 smoke 分组内。


## Recon 0.2.1 恢复核验 / 当前状态（2026-10-21）

本轮授权仅重检计划、交接与续跑材料；Task 5 未开始。当前状态表取代上文历史 deferred 的当前效力，不删除历史过程。
代码基线 `fdde65582a94f8a2011e4097257734ceb39af599`，工作区补丁与未跟踪源码快照：`.superpowers/sdd/2026-10-19-s2-multiprocess-split-first-slice/task-5-resume-review/baseline.json`、`worktree.patch`、`untracked/`。
补丁 SHA256 `bb27c539369b476738b89960bd31ec24748e52316930a85593748b35bca95eb4`；PVF 指纹见 baseline.json。本轮只核验源码/文档，没有运行游戏侧构建、smoke、绑定或真机。
历史 Task 4 测试结果归属于当时 stash 还原后的混合树；缺当时完整指纹，不冒充本轮通过。核心已提交接口本轮与 HEAD 无差异。
Task 5 计划的 C5-DEP/READ/CONFIG/HOST/SCRIPT/LIFE/SINK/GATE 均更新到版本 2；这表示文档消费契约已更新，不表示未创建的实现已通过。
Task 6–11 均 `needs-refresh`，尤其 Task 7 的生命周期/计数与 Task 11 的原始帧对比方法；远期代码未在本轮逐段审计。

| ID | 当前状态 | 负责人 | 当前结论 | 最新证据/范围 | 处理触发条件 |
|---|---|---|---|---|---|
| D5-READ | `resolved` | Task 4 | 旧宿主漏 NoDelay/CRC/长度已修；Task 5 的透传由 C5-READ 约束 | dfbf02e；EntranceHandshakeServer.cs:132-150；RunCrcRejectionAsync | 已在 Task 4 修复，Task 5 实装后再验消费接线 |
| D5-LABEL | `resolved` | Task 4 | 宿主标签已带本地端口；撤下旧的硬编码入口标签遗留项 | EntranceHandshakeServer.cs:136-138；RunHandshakeReplayAsync | 后续 sink 仍须保留标签 |
| D5-STOP | `resolved` | Task 4 | 已修并发 Stop/Run.finally 对 List 的枚举清空竞态；不等于整个生命周期并发安全 | EntranceHandshakeServer.cs:73-88；dfbf02e 前已在 0e38cf2 修复 | 更广的启动窗口和测试缺口分别见 D5-START / STOP-TEST |
| D5-DIAL | `resolved` | 本轮恢复核验 | 镜像来源归属已澄清：旧 IsDialableHost 仅工作区；Task 5 使用已提交 EntranceDialHost，不能再声称 HEAD 中存在旧符号 | EntranceDialHost.cs:12-39；.superpowers/sdd/2026-10-19-s2-multiprocess-split-first-slice/task-5-resume-review/worktree.patch 内 EntranceService hunk | 来源/配置实际变化时按 C5-HOST 复核 |
| D5-GROUP | `resolved` | 本轮计划修订 | 全量不含 IPC 的事实保留；验收遗漏通过明确列两组命令修正，未修改测试分组 | Task 5 Step 3；tests/DFLegacy.SmokeTests/Program.cs:12 | 每次收尾核对两份结果 |
| D5-STOP-TEST | `open` | 后续生命周期维护 | 上述 Stop 修复无确定性竞态测试；历史多次绿不能替代确定性覆盖 | EntranceHandshakeServer.cs:73；ledger Task 4 随机 Collection was modified 记录 | 生命周期调整或再次观测竞态时补同步/故障注入验证 |
| D5-START | `open` | Task 5 | Start token 未用、Start/Add 与 Stop 有窗口、多端口失败不回滚；Task 5 需串行宿主生命周期并用 finally 收尾 | EntranceHandshakeServer.cs:27-48 | Task 5 启动失败/取消验收前；共享 API 更广并发保证另行处理 |
| D5-CANCEL | `open` | Task 5 / 后续共享宿主维护 | 会话任务未跟踪，取消期 OCE 不在会话 catch 内；Run 退出不证明所有会话任务均已观察并回收 | EntranceHandshakeServer.cs:120,166 | Task 5 取消用例若出现故障先处理；全会话等待在扩展生命周期时复核 |
| D5-SENT-ORDER | `open` | 测试维护 | 已有约 1 秒轮询及 volatile 计数读取；仍无显式完成同步，不能写成确定性 happens-before 覆盖 | EntranceSplitSmokeTests.cs:377-390,483-537 | 复现 flake 或变更该断言时采用显式同步 |
| D5-ACCEPT-TEST | `open` | 共享宿主测试维护 | 真实 accept 错误已 warn+continue；该路径缺确定性错误注入测试，旧无牙指覆盖缺口 | EntranceHandshakeServer.cs:108-117；Task 3 复审历史 | 修改 accept 异常路径或出现线上失败时补注入 seam |
| D5-WIRING | `open` | Task 5 | 已有 Split/直接宿主测试与历史 pid 检查，未覆盖两模式真实服务接线 | EntranceSplitSmokeTests.cs:85,189；未实例化 EntranceHandshakeService | Task 5 / I2 完成前必须实际验证每个角色端口及协议路由 |
| D5-DEAD | `open` | 后续清理 | 旧入口分支与 LoadContent 在现行绑定下不可达；本波不顺带清理 | EntranceService.cs::ExecuteAsync / LoadContent；Task 4 拆分裁定 | 专门清理波或重新引入调用前复核 |
| D5-REAL | `open` | Task 11 / I5 | 改造后原客户端双档 town-two-clients 与全链路真机未跑；netdump 来源未提交且本轮未验证二进制 | ledger Task 4 抓包摘要；.superpowers/sdd/2026-10-19-s2-multiprocess-split-first-slice/task-5-resume-review/untracked/src/DFLegacy.Ijl15/src/net_dump.cpp | I5 前固定构建/输入版本后取数；可用固化工作区构建，不把必须提交他人改动当先决条件 |
| D5-RAW-COMPARE | `open` | Task 11 / I5 | 随机会话密钥导致原始密文不同；最终 Tier 2 的例外或归一化尚未固化，不把 I2 解码内容对比扩大为 Tier 2 通过 | EntranceProtocolEngine.cs:10；spec §7.1 Tier 2 | 最终 Tier 2 前对照批准规范明确方法，需改门槛时记录已有授权或取得决定 |

没有新增 accepted 风险或变更 spec 门槛。open 项允许开始已明确的 Task 5 工作，其中 D5-WIRING、D5-START 及实际暴露的取消问题须在 I2 完成前处理；未完成不能以 deferred 宣称 I2 通过。
本轮 Ruling：撤下旧 Task 5 过期代码块，以已核验的消费契约、精简测试与门槛替代；代价是实施者须按契约完成装配，不再照抄完整示例。
本轮 Ruling：保留 Task 4 已批准的角色依赖边界、工作区拨号策略与 managed build gate；不重新把原生 dotnet CLI 失败混进本波 gate，不因换会话重跑未变实现的全量测试。

## Task 5（I2）：`DFLegacy.Channel` 新 exe —— 2026-10-21 实施与验收

本节取代「Recon 0.2.1 恢复核验」表作为**当前视图**；历史过程保留在上文，未删除。
实施前基线 `330c526`（= `fdde655` 之上的纯文档提交）；工作区快照 `task-5-resume-review/baseline.json` 的
22 条 protected + 17 条 inputSha256 **逐条复核全部一致**，因此 C5-DEP/READ/CONFIG/HOST/SCRIPT/LIFE/SINK
的源码证据可沿用，未刷新契约。
实施前 `server.json` 的 `ScriptPvfPath` 是全机不存在的 `D:\DFLegacy\DF2008\Script.pvf`；本波一律用
`<repo>/DNF2008/Script.pvf`（SHA256 `bc397d14…ac27`，与 baseline.json 一致）做两档共享资产。
`etc/channel_info.etc` 实测存在于该 PVF（27964 字节），所以 I2 的脚本对比不是空跑。

### 提交与索引

- 本波提交：`73f16c0`（12 文件，+2026/-3），逐文件 pathspec。
  `DFLegacy.Emulator.slnx` 与 `tests/DFLegacy.SmokeTests/Program.cs` 含他人 hunk，用「HEAD 版本 + 本任务 1 行」
  构造 index blob（`git hash-object -w` + `git update-index --cacheinfo`）后 `git diff --cached` 核对，
  只暂存了自己的 1 行；提交后 `git status` 仍显示这两个文件的他人改动（Ilj15.Tests 工程、NAT/NOTI 11），
  说明未被带走。`docs/` 用 `git add -f -- <明确路径>`。
- 提交后工作区与提交前一致（我的文件全 clean），他人改动与未跟踪文件数量不变。

### 新增/变更产物

| 文件 | 性质 |
|---|---|
| `src/DFLegacy.Channel/DFLegacy.Channel.csproj` | 新（Web SDK，仅 Protocol + InterProcess；复制 `server.json` 到输出目录） |
| `src/DFLegacy.Channel/ChannelOptions.cs` | 新（窄配置模型 + `Read(IConfigurationSection)` + 镜像 `ProcessOptions.Validate()`） |
| `src/DFLegacy.Channel/ChannelScriptSource.cs` | 新（镜像 `ScriptFileSystem` 的选源四分支） |
| `src/DFLegacy.Channel/ChannelSessionSink.cs` | 新（独立收/发/会话计数，保留 `entrance:{port}` 标签） |
| `src/DFLegacy.Channel/Program.cs` | 新（配置 → 内容 → 宿主；try/finally 串行生命周期） |
| `tests/DFLegacy.SmokeTests/ChannelProcessSmokeTests.cs` | 新（配置表驱动 + 脚本来源 + 两档真进程接线 + I2 内容对比 + 读参数透传 + 拓扑守卫） |
| `tests/DFLegacy.SmokeTests/DFLegacy.SmokeTests.csproj` | 改（1 行 ProjectReference） |
| `tests/DFLegacy.SmokeTests/Program.cs` | 改（`--s2-ipc` 块 1 行） |
| `DFLegacy.Emulator.slnx` | 改（1 行 Channel 工程） |

### 门槛结果（本波实际执行）

| 命令 | 结果 |
|---|---|
| `dotnet build tests/DFLegacy.SmokeTests/DFLegacy.SmokeTests.csproj -c Release` | 成功，**0 警告 0 错误** |
| `dotnet run --project tests/DFLegacy.SmokeTests -c Release --no-build -- --s2-ipc` | `S2 IPC smoke: 0 failures`，12.1s（多轮复跑一致） |
| `dotnet run --project tests/DFLegacy.SmokeTests -c Release --no-build`（全量） | `All DFLegacy protocol smoke tests passed.`，42s，exit 0 |
| 已申报只读工具 `smoke-tests` | 可用，40.3s，exit 0（全量同一条，工具不带 `--no-build`；IPC 必须单独命令） |

**变异对照（证明门槛能变红）**：临时把 Channel 的广告地址解析改成直接用 `Entrance.Host`、并让脚本少 1 字节，
`--s2-ipc` 变红 6 条，且**只有 split（Channel 进程）侧红**、mono（Server 进程）侧仍绿 → 证明断言确实穿过
新 exe。还原后需**强制重建**才恢复绿：`Copy-Item` 还原会把源文件 mtime 改回更早，MSBuild 判定 up-to-date
并继续用变异后的二进制（曾因此出现「源码已还原但门槛仍红」的假失败）。

### 证明范围与边界

- I2 通过：同一测试客户端、同一真 PVF、真实两档进程，按各会话密钥解码/解压后，协议 10 脚本、协议 3 元数据、
  协议 6 缓存决定跨模式与跨入口口**逐字节相等**；随机会话密钥导致的原始密文差异被显式断言并排除。
- 单体：一个真实 `DFLegacy.Server` 进程报告并服务三个端口；拆分：入口口归 `DFLegacy.Channel`、频道口归
  `DFLegacy.Server`，两进程各自日志可证，且 Channel 输出目录无 `DFLegacy.Server.dll`、无 store/state 写入。
- **不覆盖**：Tier 2 原始帧全同（D5-RAW-COMPARE）、真机（D5-REAL）、频道口的正向玩法路由（D5-WIRING 余量）、
  进程级信号驱动的优雅退出（D5-START）、完整并发安全（D5-START/D5-CANCEL）。
- 独立复审：`task-5-review.md`（同目录），结论「I2 证据成立，无必须改生产代码才能提交的问题」；F-1/F-2 经
  Ruling 收窄口径并改写断言文案，F-3/F-4 已修计划文本，F-5/F-6/F-7/F-8 已修测试。

### 当前遗留项（稳定 ID）

`resolved` 移出 deferred：D5-READ、D5-LABEL、D5-STOP、D5-DIAL、D5-GROUP（同上文，证据加本波实测）。
下表为本波之后的 open 项。

| ID（均 open） | 负责人 | 当前范围 | 触发条件 |
|---|---|---|---|
| D5-START | 共享宿主 + 后续生命周期维护 | 调用方一半已完成：串行启动/停止、Start+Run 同处 try、finally 统一 Stop、启动失败非零退出且不留监听。仍 open：`StartAsync` 未使用 token、Start/Add 与 Stop 有窗口、**进程级信号驱动优雅退出零覆盖**（`Console.CancelKeyPress`→Cancel 与 OCE 返回 0 两条分支无用例） | 生命周期调整前补 in-process `StartAsync→Stop` 与真实停止信号用例 |
| D5-CANCEL | 共享宿主维护 | 会话任务未跟踪（`EntranceHandshakeServer.cs:120` 即发即忘），取消期 OCE 不在会话 catch 内；Run 退出不证明会话任务已观察回收 | 扩展生命周期或出现取消期异常时 |
| D5-WIRING | Task 8 / Task 11 | 两模式真实进程的端口归属与握手族路由已实测（含频道口「接得上但握手引擎不服务」）；**频道口的正向玩法协议路由仍只到 code review**（Ruling 1） | Task 8 接入 admin 计数或 Task 11 真机时用真实玩法帧补 |
| D5-CONFIG-PATH | 部署/后续任务（本波未授权改 BUILDING.md） | 两进程运行时读的是各自输出目录的 `server.json` 副本；相对 `ScriptPvfPath`/`SkillScriptPath` 按各自 `AppContext.BaseDirectory` 解析，拆分可能选到不同资产 | 拆分部署前落定绝对路径或两 exe 同目录；改部署文档时 |
| D5-STOP-TEST | 后续生命周期维护 | Stop 修复无确定性竞态测试 | 生命周期调整或再现竞态时 |
| D5-SENT-ORDER | 测试维护 | 会话计数轮询无显式完成同步 | 复现 flake 或改该断言时 |
| D5-ACCEPT-TEST | 共享宿主测试维护 | accept 错误路径缺错误注入 seam | 修改 accept 异常路径时 |
| D5-DEAD | 后续清理波 | `EntranceService` 旧入口分支与 `LoadContent` 在现行绑定下不可达（本波未清理） | 专门清理波 |
| D5-REAL | Task 11 / I5 | 真机未跑 | I5 前固定构建/输入版本 |
| D5-RAW-COMPARE | Task 11 / I5 | 最终 Tier 2 例外/归一化未固化；I2 的内容对比**不等于** Tier 2 通过 | 最终 Tier 2 前 |

没有新增 accepted 风险，未修改 spec 门槛。Task 6–11 仍全部 `needs-refresh`，其中 Task 7/8 的
生命周期与统计口径、Task 11 的对比方法尤其要在各自波开始前重核。

## Task 6（I3 前半）：链路客户端 —— 2026-10-21 实施与验收

本节取代「Task 5（I2）」段作为**当前视图**；Task 5 的结论仍然有效，历史保留。
基线 `73f16c0`（Task 5 提交）；本波提交 `388ed00`（6 文件，+974/−1：两条新源码 + 计划刷新 + 两份交接）。
进入本波时计划 Task 6 标 `needs-refresh`，先做契约刷新再实施 —— 刷新出的 4 处草案缺陷记在计划 Task 6 段的
「刷新裁定」里，草案代码保留作历史。

### 刷新查出的草案缺陷（plan-origin，已在本波修掉）

| # | 草案问题 | 判定依据 | 后果（若不修） |
|---|---|---|---|
| 1 | Step 1 在服务端发 ACK **之前**断言 `client.Connected` | `InterProcessLinkClient` 的 `Connected` 语义 = 已注册成功（spec §5.3：`accepted=0` 不建链） | 该断言必红 |
| 2 | 所有读包都没有超时（`PacketFrame.ReadAsync(stream)` 无 token） | 本仓库既有规则（Task 5 交接 §7、`EntranceSplitSmokeTests.cs:461-481`） | 静默对端会挂住整套 smoke |
| 3 | 测试起 `RunAsync` 后从不 await，`DisposeAsync` 立即 `Cancel`+`Dispose` CTS | 跑动中的读/写循环会碰已释放的 CTS | `ObjectDisposedException` + 未观察异常 |
| 4 | Tier 1 只验了 id/长度，没验固定线上字节，且完全没测「静默 3 个心跳 ⇒ down」 | spec §7.1 第 1/4 项 | I3 的门槛只有一半证据 |

### 实现要点（后续波不要改错）

- `Connected` = 已注册成功（`accepted=1` 且版本一致）；TCP 连上但未 ACK 时仍为 false。
- `LastAck` 被拒绝时也保留（`Accepted=0` 可观测）；`LastRemoteStatus` 只存**对端发来的** STATUS。
- 判死只看收：连续 `MissedStatusLimit` 个间隔一帧都没收到才 down；注册阶段另有 `AckTimeout`
  （= `StatusInterval × MissedStatusLimit`，**下限 2 s**，见 D6-ACK-WINDOW）。
- CTS 归 `RunAsync` 的 `using` 所有、退出时才释放；`DisposeAsync` 只请求停止，不等待 ——
  调用方要等停止必须 await 自己持有的 `RunAsync` 任务。
- 观测属性用 `volatile`/`Interlocked`，不提供更强同步。

### 门槛结果（本波实际执行）

| 命令 | 结果 |
|---|---|
| `dotnet build tests/DFLegacy.SmokeTests/DFLegacy.SmokeTests.csproj -c Release` | 0 警告 0 错误 |
| `dotnet run --project tests/DFLegacy.SmokeTests -c Release --no-build -- --s2-ipc` | `0 failures`，连跑 3 次：14.5s / 15.9s / 12.9s |
| `dotnet run --project tests/DFLegacy.SmokeTests -c Release --no-build`（全量） | `All DFLegacy protocol smoke tests passed.`，43.6s，exit 0 |

**变异对照（证明新断言能变红）**：
1. 把 `_connected = true` 提前到 ACK 之前 → `a TCP connection alone does not mark the link up` 变红。
2. 把读循环的静默窗口放大到 5 分钟 → `3 missed status intervals of complete silence mark the link down` 变红。
（第一次变异脚本用了 CRLF 替换而文件是 LF，静默没生效；用 `diff` 校验替换是否命中后才算数。）

### 证明范围与边界

- 通过：客户端真的把 Tier 1 第 1 项那串字节写到 socket 上（0x7b / 0x80 / size=49 LE / CRC 字段全 0 / 记录数 1）、
  `size = 11 + 38` 自洽、心跳按间隔重复、对端 STATUS 捕获口径、`accepted=0` 不建链且退避重试、
  版本不匹配拒绝、静默 3 个心跳周期判 down 且自动重连。
- **不覆盖**：真实 channel 侧服务端（Task 7）、两进程端到端连通、game 进程接线与 admin 合并（Task 8）、真机（D5-REAL）。
  本波的对端是测试里自建的 `FakeLinkPeer`，只证明客户端行为，不证明与 Task 7 实现互通。

### 当前遗留项（稳定 ID）

Task 5 段的 open 项全部继续有效（D5-START / D5-CANCEL / D5-WIRING / D5-CONFIG-PATH / D5-STOP-TEST /
D5-SENT-ORDER / D5-ACCEPT-TEST / D5-DEAD / D5-REAL / D5-RAW-COMPARE），本波不改变它们的状态。
新增：

| ID（均 open） | 负责人 | 当前范围 | 触发条件 |
|---|---|---|---|
| D6-INTEROP | Task 7 | 客户端只对着自建 `FakeLinkPeer` 验过，与真实 channel 侧服务端的行为互通未验证（报文/ack 时序/重连握手都可能不同） | Task 7 的 `InterProcessLinkServer` 落地后必须跑客户端↔服务端真实两进程用例 |
| D6-ACK-WINDOW | Task 7 | `AckTimeout` 的下限 2 s 是硬编码常量，不是配置键；与 Task 7 服务端的实际注册耗时无关地固定 | Task 7 落地后按真实注册路径复核是否需要暴露为配置 |
| D6-WIRING | Task 8 | 客户端尚未在 game 进程里实例化，`/api/status` 也没有 channel 侧字段；本波只有库 + 测试 | Task 8 接入时必须复用 `LastRemoteStatus` 口径，不能把自发 STATUS 合并进 admin |

Task 7–11 仍全部 `needs-refresh`。

## Task 7（I3 后半）：链路服务端 + 端到端连通 —— 2026-10-21 实施与验收

本节取代「Task 6（I3 前半）」段作为**当前视图**；前两波结论仍有效，历史保留。
基线 `388ed00`（Task 6）；本波提交 `ebbbc53`（7 文件）。
进入本波时计划 Task 7 标 `needs-refresh`；刷新查出草案 10 处与现场不符或会复现已知缺陷，先修计划再实施
（计划 Task 7 段「2026-10-21 刷新裁定」，草案代码保留作历史）。

### 刷新查出的草案缺陷（plan-origin，已在本波修掉）

| # | 草案问题 | 后果（若不修） |
|---|---|---|
| 1 | 接线用不存在的符号（`options.EntranceHost` / `ChannelName` / `ChannelPort` / `LinkPort`） | 编译失败 |
| 2 | 用 `CancellationToken.None` 起 RunAsync 并丢弃返回的 Task | 违反 C5-LIFE/2；进程永不正常退出 |
| 3 | `TotalPackets = ReceivedPackets + SentPackets` | 违反 ledger 已记录口径 |
| 4 | `PeerUp` 是单 bool，每个会话进出都置位 | 重连时旧会话收尾把新会话打回 down |
| 5 | `Stop()` 无锁 `Clear()` 而 `RunAsync` 正在枚举 | 复现 Task 4 修过的 "Collection was modified" |
| 6 | `Stop()` 不关闭已建立会话 | handler 悬挂 |
| 7 | 端到端只验 game→channel 单向 + 固定 `Task.Delay` | 半条证据 + 脆性 |
| 8 | 把用例塞进「两个 listener 停掉之前」 | 该插入点在 Task 6 之后已不存在 |
| 9 | 读包无长度上限 | 对端一个头就换来 4 MiB 缓冲 |
| 10 | NOTICE_CHANNEL_SERVER 未接 | 只用到三种载荷（记 D7-NOTICE） |

### 实现要点

- `PeerUp` = 已注册会话数 > 0（`Interlocked`），不是单布尔位。
- `Stop()` 幂等：门内快照 + 清空，门外停监听 + 关连接。
- 链路帧读上限 4 KiB（`MaximumFrameLength`）。
- channel 侧 STATUS：`TotalPackets = sink.ReceivedPackets`（收包口径）、`TotalConnections = TotalSessions`、
  `LoggedInUsers = 0`。
- 进程生命周期：链路与握手宿主同处一个 try、共用 `lifetime`；`finally` 先 Cancel 再 Stop 两者。

### 门槛结果（本波实际执行）

| 命令 | 结果 |
|---|---|
| 构建 smoke 工程（Release） | 0 警告 0 错误 |
| `--s2-ipc` | `0 failures`；连跑 2 次 13.4s / 13.2s |
| 全量（`--no-build`） | `All DFLegacy protocol smoke tests passed.`，38.6s，exit 0 |

**变异对照**：服务端无条件接受注册 → 版本拒绝断言变红；Channel 链路端口 +1 → 真实进程 e2e 就绪断言变红。
（变异还连带红了 split 模式频道口探针 —— 因为 `LinkPort + 1` 撞上了夹具另一个端口，属副作用，顺带证明端口去重必要。）

### 证明范围与边界

- 通过：I3 的库层门槛 —— Tier 1 第 1/2 项（线上固定字节、size=11+N）、第 3 项（accepted=0 不建链 + 退避重试）、
  第 4 项（静默 3 个心跳判 down 且自动重连），以及**真实两进程端到端**（真 `DFLegacy.Channel` 进程 ↔ 真客户端，
  注册 + 双向 STATUS + ACK 携带配置身份）。
- **不覆盖**：客户端尚未接进 game 进程（Task 8 / I4）、`/api/status` 无 channel 侧字段、服务端侧静默判死、
  真机（D5-REAL）、Tier 2（D5-RAW-COMPARE）。

### 当前遗留项（稳定 ID）

**状态变化**：`D6-INTEROP` → resolved（真实两进程端到端已验）；`D6-ACK-WINDOW` 仍 open（2 s 下限是常量，
但默认配置的真实注册路径已验通）。其余 D5-* 与 D6-WIRING 状态不变。

本波新增：

| ID（均 open） | 负责人 | 当前范围 | 触发条件 |
|---|---|---|---|
| D7-SILENCE | Task 8 | 服务端没有静默判死：对端只要 TCP 不断开，`PeerUp` 恒真（半开连接检测不到）；`LastStatusAt` 可读但无人用 | Task 8 做 admin 存活位时必须先定判定口径 |
| D7-NOTICE | Task 9 / 10 | `NOTICE_CHANNEL_SERVER`（第 4 种载荷）仍未由服务端发出 | S2S 报文表或验收场景需要时 |
| D7-INTERVAL | Task 8 | 服务端 STATUS 间隔硬编码 1 s，与客户端间隔不对齐不会被发现 | Task 8 接链路时决定是否配置化 |

Task 8–11 仍全部 `needs-refresh`。

## Task 8（I4）：game 接入链路 + admin 计数与存活 —— 2026-10-21 实施与验收

本节取代「Task 7（I3 后半）」段作为**当前视图**；前几波结论仍有效，历史保留。
基线 `ebbbc53`（Task 7）；本波提交 `34b9dd1`（10 文件，含两份交接）。
进入本波时计划 Task 8 标 `needs-refresh`；刷新查出草案 4 处缺陷，先修计划再实施
（计划 Task 8 段「2026-10-21 刷新裁定」，草案代码保留作历史）。

### 刷新查出的草案缺陷（plan-origin，已修掉）

| # | 草案问题 | 后果（若不修） |
|---|---|---|
| 1 | `ChannelLinkService` 构造缺 `RuntimeState`，但 `ExecuteAsync` 用了 `runtime.*` | 编译失败 |
| 2 | `Status()` 把 `Sessions().Length` 同时塞给 `ActiveSessions` 和 `LoggedInUsers` | channel 侧把会话数当登录数合并 |
| 3 | `/api/status` 是 Admin 鉴权（fail-closed），草案「手工访问」会 401 | 手工步骤不可作证 |
| 4 | Step 5 是手工验证 | 按仓库纪律不算证据 |

### 实现要点

- `ChannelLinkService : BackgroundService`：拆分模式拨号 channel 进程，单体模式直接返回；`Client` 供
  `/api/status` 读 `Connected`/`LastStatusAt`/`LastRemoteStatus`。
- `StatusProjection.ChannelLink`：断开只报本地值；连通按收包口径把对端 `TotalConnections`/`TotalPackets`
  与 `ActiveSessions` 加回。`LoggedInUsers` 在 game 侧恒 0（RuntimeState 无登录计数）。
- `/api/status` 新增 `channelLink` 字段；既有 `totalConnections`/`totalPackets`/`activeSessions` 等
  保持本地值不动（I4 门槛）。
- Server.csproj 补 InterProcess ProjectReference（草案未列但必要）。

### 门槛结果（本波实际执行）

| 命令 | 结果 |
|---|---|
| 构建 smoke 工程（Release） | 0 警告 0 错误 |
| `--s2-ipc` | `0 failures`，16s |
| 全量（`--no-build`） | `All DFLegacy protocol smoke tests passed.`，41.3s，exit 0 |

**变异对照**：投影合并改成只取本地值 → 「加回」断言变红；ChannelLinkService 链路端口 +1 → 接入链路三断言变红。

**修掉的真实问题**：初版 `channelLink.totalConnections >= 1` 恒红 —— 链路本身不算进任一侧会话计数，
没有客户端连接时两边都 0。改为先做一次「连上就关」的真实入口连接，把 channel 侧 TotalSessions 推到 1 再轮询，
让断言变成真的跨进程计数观测。

### 证明范围与边界

- 通过：game 进程接入链路（真实 Server split + Channel 两进程）、双侧存活对 admin 可见
  （`/api/status.channelLink.connected=true`）、既有字段不回归（纯投影 + 真实 Bearer GET）、
  入口连接后 `channelLink.totalConnections>=1`。
- **不覆盖**：`channelLink` 每字段精确值（D8-STATUS-ENDPOINT）、game 侧 `LoggedInUsers`（D8-LOGGEDIN）、
  服务端侧静默判死（D7-SILENCE）、真机（D5-REAL）、Tier 2（D5-RAW-COMPARE）。

### 当前遗留项（稳定 ID）

**状态变化**：`D6-WIRING` → resolved（客户端已接进 game 进程并真实两进程连通）。
`D7-SILENCE`、`D7-INTERVAL`、`D7-NOTICE` 与 Task 5/6/7 的其余 open 项状态不变。

本波新增：

| ID（均 open） | 负责人 | 当前范围 | 触发条件 |
|---|---|---|---|
| D8-STATUS-ENDPOINT | 后续波 | `channelLink` 只端到端验到 connected=true + totalConnections>=1，其余字段精确值未逐字段断言 | 需精确 admin 口径时补，或决定是否公开 `/api/status` 后加免登录 shape 测试 |
| D8-LOGGEDIN | 后续波 | game 侧 `LoggedInUsers` 恒 0 | 有真实登录口径的波 |

Task 9–11 仍全部 `needs-refresh`。

## Task 9（I5 前半）：protocol S2S 报文表 —— 2026-10-22 实施与验收

本节取代「Task 8（I4）」段作为**当前视图**；前几波结论仍有效，历史保留。
基线 `51f5d83`（计划刷新提交；`git log 34b9dd1..51f5d83 -- protocol/` 为空，protocol/ 两基线间无差异）；
本波提交 `6aeec02`（7 文件，+49/−16，全部在 `protocol/` 内，`git status -- protocol/` 已干净）。
进入本波时计划 Task 9 标 `needs-refresh`；刷新查出草案 1 处与现场实现不符，先修计划再实施
（计划 Task 9 段「2026-10-22 刷新裁定」，草案代码保留作历史）。

### 刷新查出的草案缺陷（plan-origin，已在本波修掉）

| # | 草案问题 | 后果（若不修） |
|---|---|---|
| 1 | 「总数 1171→1175、KIND_BASELINES += s2s」与 `_check_baselines`/`build_manifest` 只读 opcode-map 的现状不符 | S2S 基线无法校验；或污染官方六档/status/mark 统计；`import_map` 下次静默删掉 S2S |

### 实施发现并回填计划的缺陷（plan-origin，Lead 裁定清单漏项）

- 裁定下游清单漏了 `protocol/tests/test_render.py`：其 `_base` 夹具调 `render.render_all`，
  `render.TRACKED` 扩到 6 文件后 `build_manifest` 对缺失受跟踪产物抛 `FileNotFoundError`，且
  `test_manifest_has_no_timestamp_and_keys_sorted` 断言 `len(files)==5`。不修它 pytest 首跑 6 红
  （实现者实测 6 failed/90 passed）。已最小修复（夹具补 s2s-map.tsv + 断言 5→6）并纳入提交；
  计划 Task 9 裁定清单与 handoff §2 已回填。

### 实现要点（后续波不要改错）

- `opcodes_total` 保持 **1171**（官方 CMD+NOTI 口径）；`s2s=4` 是 manifest 独立基线键，与六档/status/ext_mark 互不混算。
- `_check_baselines` 的 s2s 计数读 `s2s-map.tsv`，不走 opcode-map 的 rows 循环。
- S2S 不进 importer 流水线；`_KIND_ORDER` 未动；`BASELINE_OPCODES_TOTAL` 未动。
- `render_index` 不读 s2s-map ⇒ `map/index.md` 字节不变（本波无 diff，属预期，不是遗漏）。
- **D7-NOTICE 范围澄清**：S2S 台账已登记 4 条报文（4/128/129/130）；「服务端是否真发 NOTICE」仍 open，归 Task 11 真机。

### 门槛结果（Lead 独立复跑，非转述）

| 命令 | 结果 |
|---|---|
| `python -m protocol.s1 verify` | `verify: PASS (5/5)`，exit 0 |
| `python -m pytest protocol -q` | `96 passed`，exit 0 |
| manifest 抽查 | `files` 恰 6 项（含 `map/s2s-map.tsv`）、`s2s=4`、`opcodes_total=1171` |

### 当前遗留项（稳定 ID）

Task 5–8 的 open 项全部继续有效，本波不改变任何 D 项状态。
Task 10 实施中（impl-task-10）；Task 11 仍 `needs-refresh`。

## Task 10（I5 中段）：验收平台多进程编排 —— 2026-10-22 实施与验收

本节取代「Task 9（I5 前半）」段作为**当前视图**；Task 9 结论仍有效，历史保留。
基线 `893383f`（Task 9 收尾提交）；本波提交 `98499e9`（10 文件，+291/−34，全部在 `tools/acceptance/` 内）。
进入本波时计划 Task 10 标 `needs-refresh`；刷新查出草案 4 处与现场不符，先修计划再实施
（计划 Task 10 段「2026-10-22 刷新裁定」，草案代码保留作历史）。

### 刷新查出的草案缺陷（plan-origin，已在本波修掉）

| # | 草案问题 | 后果（若不修） |
|---|---|---|
| 1 | `SERVER_SPECS` 端口硬编码（game tcp[7001] udp[2311,7002,7003] / channel tcp[2311,8080]） | 端口来自 server.json，硬编码漂移；disjoint 测试把「2311 game=UDP / channel=TCP」合法共存误判冲突 |
| 2 | disjoint 判据只按端口号 | 同上 |
| 3 | 多进程就绪只等 Entrance（`wait_until_listening` runner.py:534） | game 进程的 Channel.Port 未就绪就起 actor |
| 4 | `judge` 的 `server_dir` 形参从未被读 | 服务端侧期望（`evidence=[静态]`）永远 missing-evidence 或被跳过 |

### 实施回填（Lead 裁定 Files 清单漏 4 个既有测试）

`servers` 重命名 + 门场景加入后，4 个既有测试不交则提交即红，已同波提交：
`test_model.py`（`server=`→`servers=`）、`test_cli_dry_run.py`（场景数 4→5）、
`test_cli_orchestrate.py`（manifest 键 + 日志名 `server-game.log`）、`test_runner.py`（日志名）。

### 实现要点（后续波不要改错）

- `Manifest.servers` 按进程名记（`game`/`channel`），不再被后启动的进程覆写；`deterministic_view` 键同步。
- `SERVER_SPECS`/`server_process_specs` 从 `server.json` 派生：channel = `Entrance.Port + AdditionalPorts +
  Processes.Channel.LinkPort`；game = `Channel.Port + GameplayDatagram(Primary/SecondaryPort) + CharacterDatagram`。
  disjoint 判据按 **(host,port,protocol)**。
- `wait_until_listening` 拆分布局分别等 channel(Entrance) 与 game(Channel.Port)。
- `judge` 真读 `server_dir`：`evidence=[静态]` 的期望从 `server-*.log` 搜 pattern。
- 逐进程日志：`server-game.log`（orchestrate 单服务端路径用 `name="game"`）。

### 门槛结果（Lead 独立复跑，非转述）

| 命令 | 结果 |
|---|---|
| `python -m pytest acceptance/tests -q` | **`3 failed, 118 passed`**，exit 1 —— 3 红见下，**非本次引入** |
| `test_multi_server.py` 单独跑 | `4 passed`（先写红：首跑 4 failed，均为预期缺失） |
| `grep -rn '\.server\b\|\["server"\]\|\x27server\x27' tools/acceptance --include=*.py` | 0 命中（`model.py:77` 的 `"server"` 是 `server_dir` 目录名，非 manifest 字段） |

**3 红的归因（Lead 独立证实，两级证据）**：`test_cli_orchestrate.py` 的 wires/waits/ambiguous_pid 三用例，
根因是 **Windows 排除端口段 2280–2379 覆盖 TCP 2311**，`bind 127.0.0.1:2311` 报 `WinError 10013`
（本机实测 bind 2311→10013、24011→OK；`netsh interface ipv4 show excludedportrange protocol=tcp` 证实）。
**在 base 提交 `893383f` 的独立 worktree 复跑同文件：同样 3 failed** ⇒ 预先存在的环境问题，非 Task 10 回归。
（实现者用 `git stash` 得出同一结论；Lead 因改动已提交无法 stash 复现，改用 worktree 复核。）

### 当前遗留项（稳定 ID）

**本波新增 open**：

| ID | 负责人 | 当前范围 | 触发条件 |
|---|---|---|---|
| D10-ENV-2311 | Task 11 / 环境维护 | Windows 排除端口段 2280–2379 覆盖 TCP 2311（本机动态保留，与是否在跑服务端无关）：`test_cli_orchestrate.py` 三用例恒红；**Task 11 真机服务端绑 2311 同样会 10013** | **Task 11 真机开跑前必须解决**（重启观察换段 / `netsh int ipv4 set dynamic tcp start=49152 num=16384` 收窄动态段 / 管理员保留），解决后复跑 `pytest acceptance/tests -q` 预期 121 passed |

**状态不变**：D5-REAL、D5-RAW-COMPARE（Task 11 真机 + Tier 2）；D7-NOTICE（台账已登记，真发仍 open）；
其余 D5-*/D6-*/D7-*/D8-* 全部保持。

**门槛修订（Ruling，带代价）**：本波「全绿」按 D10-ENV-2311 改读作「118 passed + 3 个已归因环境红」。
代价：若该环境问题被误判为回归，会掩盖真实缺陷——归因证据已两级钉死（worktree 复跑 + netsh），且解决
D10-ENV-2311 后必须复跑到 121 passed 才算恢复全绿。

Task 11 仍 `needs-refresh`（进入前先刷新 Tier 2 例外/归一化 + D5-REAL 取数 + 解决 D10-ENV-2311）。

## Task 11 前置：D10-ENV-2311 解决 + 环境恢复验证（2026-10-22）

- **D10-ENV-2311 → resolved**：用户重启后（winnat 相关排除段释放），`bind 127.0.0.1:2311` 由 10013 恢复 **OK**；
  `netsh interface ipv4 show excludedportrange protocol=tcp` 中 2280–2479 三条排除段已消失（无命中）。
- **环境恢复验证（Lead 独立复跑）**：`python -m pytest acceptance/tests -q` → **121 passed, exit 0**
  （`test_cli_orchestrate.py` 3 用例随端口恢复转绿），与 Task 10 段的「解决后应 121 passed」门槛一致。
- 登记解决方式：重启释放 Windows 动态排除段；winnat 服务仍在 Running（Manual），但 2311 不再被排除。

Task 11 实施中：`impl-task-11-wiring`（orchestrate 双进程接线）进行中。

## Task 11 前置（裁定 2）：orchestrate 双进程接线 —— 2026-10-22 实施与验收

基线 `94f4a49`（D10-ENV-2311 解决）；本波提交 `7096458`（3 文件，+239/−11，全部在 `tools/acceptance/` 内）。
计划 Task 11 段「刷新裁定 2」：Task 10 只给了原语、`orchestrate` 仍单进程，本步把拆分模式双进程接上。

### 实现要点（后续真机 run 不要改错）

- `orchestrate` 判 `_is_split_server(server.json)`：拆分模式先 `start_server(name="channel", exe=channel_exe)`
  再 `start_server(name="game", exe=server_exe)`；两进程共读同一份 `server.json` 副本，逐进程日志
  `server-channel.log` / `server-game.log`。单体模式不变（只起 game）。
- channel exe 默认 `server_exe.parent/"DFLegacy.Channel.exe"`，`--channel-exe` 覆盖（进 `_orchestration_paths`）；
  拆分模式 channel exe 缺失时 rc=2 明确报错。
- 等就绪复用 `wait_until_listening`（其 `_wait_split_processes` 已按 channel→Entrance、game→Channel 各自判活）。
- 新测试用 `bind 0` 分空闲端口 + 占位进程，**绝不 bind 2311**。

### 实施回填（1 处，非越界）

`test_cli_orchestrate.py` 的 `_paths` 补 `channel_exe` 键（1 行）：orchestrate 现在读 `paths["channel_exe"]`，
旧 `_paths` 缺该键会让 4 个既有用例 KeyError 新增红；补键消除。

### 门槛结果（Lead 独立复跑，非转述）

| 命令 | 结果 |
|---|---|
| `python -m pytest acceptance/tests/test_multi_server.py -q` | `6 passed`（TDD：实现前 2 新用例红） |
| `python -m pytest acceptance/tests -q` | **`123 passed`，exit 0**（= 121 环境恢复 + 2 新用例；D10-ENV-2311 已解决故 3 个 2311 用例全绿） |

### 真机部署缺口（runbook 前置，非本步代码问题）

- `DFLegacy.Channel.exe` **未部署到 `DF/town-test/`**（只在 `src/.../bin/Release/net10.0/` 与 `tests/.../bin/Release/net10.0/`）⇒ 真机拆分档要么拷进 town-test、要么 `--channel-exe` 指向 bin 路径。
- 真机在跑的 `DF/town-test/server.json` 是**旧单体配置**（无 `Processes` 段），跑拆分档需先加 `Processes` 段并切 `Game.EntranceBinding=false`。

### 当前遗留项（稳定 ID）

`D5-REAL`、`D5-RAW-COMPARE`（Tier 2/3 本体验证，仍 open）；`D7-NOTICE`（真发仍 open）；其余 D 项状态不变。
`D10-ENV-2311` 已 resolved（上文）。
