# 新对话提示词 · S2 第一刀实现续跑（从 Task 4 开始）

> **怎么用**：新会话里只说一句：
> **"读 `docs/superpowers/handoff/2026-10-19-s2-impl-continue-prompt.md`，按里面的提示词执行"**。
>
> 本文件只负责**把实现续起来**，不含设计结论——设计与计划都在下列权威文件里。
> 写作时间 2026-10-19；写它时 Task 1–3 已完成并各自过审。

---

=== 提示词开始 ===

## 你的身份与唯一任务

你是 **DFLegacy**（DNF 赛里斯服 ACT1 客户端本地模拟服务端，`G:\Game\DNF_ACT1\DFLegacy`，.NET 10）项目的**执行者**。

**唯一任务**：用 `subagent-driven-development` 技能，**继续执行 S2 第一刀的实现计划**，从 **Task 4** 开始往下推。

设计已定稿、计划已批准、前 3 个任务已落地。**不要再重新设计，不要重写计划**；遇到计划与现实冲突时按本文件 §7 的裁定方式处理并记账。

## 第一步（不许跳过）

1. `recon_status` —— 本项目规矩：新会话先查接入状态；报 error 就先加载 `recon-onboarding`，不报错再往下。
2. 加载 `subagent-driven-development` 技能。
3. 读这四份（按顺序）：
   - `docs/superpowers/specs/2026-10-19-multiprocess-split-s2-design.md`（**设计权威**，所有者已批准）
   - `docs/superpowers/plans/2026-10-19-s2-multiprocess-split-first-slice.md`（**实现计划**，11 个任务）
   - `.superpowers/sdd/2026-10-19-s2-multiprocess-split-first-slice/progress.md`（**断点 / 全部裁定 / deferred 清单**）
   - `.superpowers/sdd/2026-10-19-s2-multiprocess-split-first-slice/task-3-review.md` 与 `task-3-rereview4.md`（已定案的质量红线长什么样）
4. 取当前任务的行号：`Select-String -Path <plan> -Pattern '^## Task \d+'`。
   **不要照抄旧行号**——计划在 Task 3 之后被改过两次，行号已经漂移。

## 当前进度（写此文件时）

| 任务 | 状态 | 提交 |
|---|---|---|
| Task 1 `DFLegacy.InterProcess` 帧壳 + 4 种载荷 | ✅ complete（过审 + 2 轮修复） | `68f2949` → `6a72a48` |
| Task 2 端口分族 + `Processes` 配置段 | ✅ complete（过审 + 2 轮修复 + 复审 clean） | `2aee1d9` → `82c491c` |
| Task 3 `EntranceHandshakeServer` 握手宿主 | ✅ complete（过审 + 4 轮修复 + 复审 clean） | `5a50b9b` → `c6189a6` |
| **Task 4 接入 server，I1 收口** | ⬜ **未开始 —— 你的起点** | — |
| Task 5–11 | ⬜ 未开始 | — |

**阶段性目标 I1**（所有者指定）：握手族脱离 `EntranceService`，改由 `EntranceHandshakeServer` 托管（= Task 3 + Task 4）。
I1 验收三条：①现有 smoke 全绿零回归；②单体模式下 `2311/7001/8080` 仍全部监听且**同属一个 pid**；③客户端握手字节流不变（Tier 2 第一半证据）。

## Task 4 的三条前置（已定稿，别用计划旧版）

1. `RuntimeEntranceSink.Received` **必须**调 `runtime.CountPacket(session)`，不要手写 `Interlocked.Increment` —— 否则 `/api/status.TotalPackets` 会丢掉全部握手包（`RuntimeState.cs:71-75`）。
2. `EntranceDialHost.IsDialable` 必须**逐字镜像** `src/DFLegacy.Server/EntranceService.cs:17680-17689`（主机名可拨、只排除 `0.0.0.0`/`::`、**不排除广播**）。改回计划的旧版就会悄悄改掉客户端拨号目标，直接违反验收③。
3. Task 3 留的 deferred 要在 Task 4 兑现：sink 的 `service` 标签要带本地端口（`entrance:2311` / `entrance:8080`），否则 admin `/api/sessions` 标签会退化。

## 硬约束（逐字遵守）

- **不改客户端面协议**：`src/DFLegacy.Protocol/PacketFrame.cs` 一行不动；11 字节头/同构模型不动。
- **落盘单写者**：`DFLegacy.Channel` 进程内不得出现 `JsonGameStore`、不得写 `state.json`、**不得引用 `DFLegacy.Server`**。
- **链路帧固定参数**：`category=0x7b`、`recordCount=1`、`includeCrc32=false`、`size` 含 11 字节头 ⇒ 建帧只能走 `PacketFrame.Create(id, payload, type: 0x7b, recordCount: 1, includeCrc32: false)`。
- **报文号**：只有 `NOTICE_CHANNEL_SERVER = 4` 复用官方 ID；其余一律 `>= 128`（128/129/130）。
- **端口归属**：`EntrancePorts = {Entrance.Port} ∪ AdditionalPorts \ {Channel.Port}` 归 channel 进程；`{Channel.Port}` 归 server 进程；链路默认 `7004`。
- **提交**：逐文件列 pathspec，**绝不 `git add -A`**；commit message 中文；一个任务一次提交。
- **`docs/` 被 `.gitignore:15` 挡住** ⇒ 新增文档要 `git add -f <path>`（`-f` 写在 pathspec 之前）。
- **外部证据上限**：Docker 当前不可用（`docker ps` 连不上 npipe、Docker Desktop 进程不存在）⇒ 任何官服取证都只能写 `[未验证]` + 取数计划，**不要**把它当已定事实。

## ⚠️ 工作区现状（最容易踩的坑）

1. **工作区里混着别人未提交的改动**：至少 `src/DFLegacy.Server/Program.cs`、`tests/DFLegacy.SmokeTests/Program.cs`、`DFLegacy.Emulator.slnx`（Ijl15.Tests 条目）、`src/DFLegacy.Protocol/GameDatagramProtocol.cs`、`src/DFLegacy.Ijl15/**` 等。
   **绝不能让它们卷进我们的提交**。已验证可行的做法：
   ```powershell
   git stash push -m "s2-preserve" -- <你要改的那几个共享文件>
   # 编辑 → 提交（逐文件 pathspec）→
   git stash pop        # 还回去；冲突就报 BLOCKED，别用 ours/theirs 硬来
   ```
   收尾必须验证：`git stash list` 为空、那些文件重新显示 ` M`、`git show --stat HEAD` 只含本任务文件。
2. **团队名额上限 8**：新成员建不了时，`send_message` 复用已空闲的成员（实现者/复审者分开用）。
3. **我的 dispatch 工具没有 model 参数**：只能在 prompt 里声明期望档位，别假装设过。

## 怎么干活（照 Task 1–3 的成熟循环，不要另创）

每个任务：
1. 记 BASE（`git rev-parse --short HEAD`）。
2. 派**实现者**（fresh 上下文）：给它「计划里该任务的行区间」+ 全局硬约束 + 报告文件路径；要求它**逐字照做**、逐文件 pathspec 提交、回 4 行（STATUS/commit/测试一行/关注点）。
3. **你自己复跑**测试（不要只信报告）：`dotnet run --project tests/DFLegacy.SmokeTests -- --s2-ipc`，期望 `S2 IPC smoke: 0 failures` 且**不挂起**。
4. 生成审查包（`git diff -U10 BASE..HEAD` 写进一个文件），派**审查者**（fresh）：给需求行区间 + 报告 + diff + 硬约束；要求两个判定（`SPEC: ✅/❌`、`QUALITY: Approved/Issues`）+ Critical/Important/Minor + `⚠️ Cannot verify`。
5. 有 Critical/Important ⇒ **修复轮**（1–3 轮复用原实现者；4–5 轮换更强者）+ **限定复审**（只判那几条 finding + 有没有新破坏）。Minor 写进 ledger 的 deferred，不进循环。
6. 每步都往 `progress.md` 追加：`Task N: ...`、`Task N: Ruling: <决定> — <理由> — <错了的代价>`、`Task N: complete (commits a..b, review clean)`。

**上下文预算紧张时**：在干净边界（一个任务过审）停下，把断点写进 `progress.md`（下一步该派谁、要修哪条 finding），再向所有者交付。不要开一半任务就断。

## 质量红线（这几轮真踩出来的，别再犯）

- **断言必须能说出"什么实现改动会让它红"**。说不出就是恒真：`check(true, …)`、回显刚赋的值、只断言"没抛"、被上层逻辑蕴含的长度断言，都算。
- **人工数 hex 极不可靠**：本轮在 49 字节期望帧上双方各错一半。**用工具数字节**（例如把字面量拼起来数长度），不要目测。
- **测试不得挂起**：任何"等对端回包"的地方都要有 `CancelAfter` 超时，让回归**具名失败**而不是把套件吊死。
- **"静默失败"是本项目最忌**：例如 `catch (SocketException)` 无 filter 会吞掉真实 accept 错误、让进程 exit 0 无人知晓。错误处理要么具名失败，要么 `warn + continue`（先例：`src/DFLegacy.Relay.Core/RelayHost.cs:48-52`）。
- **不要为了过审写假断言**；做不到就说"这条只能靠 code review 兜"，并写进 deferred。

## 交付

- 每任务：报告文件 + ledger 行；不要只口头汇报。
- 阶段性交付给所有者时：**你把哪些裁定替他做了**（每条带"错了的代价"）+ 未闭合的 deferred + 断点。
- 全部任务完成后才进 `requesting-code-review` 的整支审查。

=== 提示词结束 ===

---

## 附：本提示词的证据来源

- 进度与提交：`.superpowers/sdd/2026-10-19-s2-multiprocess-split-first-slice/progress.md`（Task 1–3 的全部 ledger 行）
- 质量红线：同目录 `task-1-review.md`、`task-2-rereview.md`、`task-3-review.md`、`task-3-rereview3.md`、`task-3-rereview4.md`
- 设计与计划：`docs/superpowers/specs/2026-10-19-multiprocess-split-s2-design.md`、`docs/superpowers/plans/2026-10-19-s2-multiprocess-split-first-slice.md`
- 环境事实：本轮实测 `docker ps` → `failed to connect to the docker API at npipe:////./pipe/dockerDesktopLinuxEngine`；`Get-Process 'Docker Desktop'` 不存在
- 工作区混入的他人改动：`git status --porcelain` 输出（本轮多次观察）
