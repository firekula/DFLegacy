# 新对话提示词 · S0 一条命令验收（真机收尾）

> **怎么用**：在新会话里把下面 `=== 提示词开始 ===` 到 `=== 提示词结束 ===` 之间的内容**整段粘贴**，
> 或者只说一句：**"读 `docs/superpowers/handoff/2026-10-15-s0-one-command-finish-prompt.md`，按里面的提示词执行"**。

---

=== 提示词开始 ===

## 你的身份与唯一任务

你是 **DFLegacy**（DNF 赛里斯服 ACT1 客户端本地模拟服务端）项目的执行者。工作区 `G:\Game\DNF_ACT1\DFLegacy`。

**唯一任务**：完成 **S0 —— 本机多客户端验收平台** 的最后一段：真机一次 run + 全分支终审 + 交付。

**背景**：S0 的实现已经全部完成并提交（18 笔提交，全部在 `tools/acceptance/`，产品代码零改动），自动化测试 **84 passed**，并且已用**桩二进制**端到端跑通"一条命令"链路。**缺的只有真机那一次**（闭源客户端 UI 需要真人点）。

## 第一步（不许跳过）

1. `recon_status`（本项目规矩：新会话先查接入状态；报 error 就先加载 `recon-onboarding` 技能）。
2. 读账本（**唯一权威进度记录**，包含本轮全部事实、裁决与事故）：
   `G:\Game\DNF_ACT1\DFLegacy\.superpowers\sdd\2026-10-07-local-multiclient-acceptance-s0\progress.md`
3. 读补缺计划（一条命令的设计依据）：
   `.superpowers/sdd/2026-10-07-local-multiclient-acceptance-s0/one-command-amendment.md`
   以及骨架 `.superpowers/sdd/2026-10-07-local-multiclient-acceptance-s0/b2b-cli-skeleton.md`
4. 核对起点：`git log --oneline -5`，最新应为 **`da5d652`**（Task 6 准备）。若不同，先看账本末段发生了什么，不要按记忆执行。

## 已完成的提交清单（自 `e1acf5a` 起，全部只在 `tools/acceptance/`）

```
716d9b7  环境前置验证（pytest/yaml 可用性）
a560e36  数据契约 model.py
17d784d  Task 0 修复（conftest tmp 目录防复用 + README 证据措辞）
866478f  runner（run 目录/状态隔离/端口预检/进程编排）
2701ca1  collector（netdump/日志归集 + 时钟偏移）
81d04eb  verdict（三分判定/归一化摘要/防恒真判定）
99cb8d8  Task 2 修复（stop_all 断言改为快照验证）
6a855ea  四个场景声明 + CLI 干跑链路
a260327  真机 netdump 时间格式/时区修复（无方括号可解析 + 本地时间→UTC）
6df725e  Task 5 修复 collect 参数与干跑防启动断言
8cde33a  actor_pid（launcher.log PID 归因，不猜不泄密）
a840748  Runner 编排接口（env/日志、按 actor 命令与真实 client_pid、逆序回收）
5688dec  Runner 修复（Popen 失败关日志句柄 + 钉住逆序回收顺序）
f0699c4  orchestrate 编排辅助函数
854d509  start_actors before_start callback + record_run_config
ebcb983  一条命令编排（起服务端与双客户端、run 私有存档、真实 PID 归集、三分判定）
fd1aec3  修复编排顺序不确定性与 server 日志测试竞态
51c87eb  一条命令校验 --actors 与场景 actor 数一致
da5d652  Task 6 准备（场景抓包 hex 证据、测试样本同步、一条命令 RUNBOOK）
```

## 已核实的硬事实（不要重新怀疑，除非有反证）

| 事实 | 值 / 依据 |
|---|---|
| 一条命令入口 | `python -m acceptance.cli run --scenario town-two-clients --actors 2 --actor-account A=<账号1>,B=<账号2> --client-dir G:\Game\DNF_ACT1\DF --runs-dir ..\runs`（cwd=`tools`） |
| 服务端配置读取 | `src/DFLegacy.Server/Program.cs:17-27`：`server.json` 取自 exe 目录；**环境变量在之后注册并优先生效** |
| 存档隔离机制 | `JsonGameStore.cs:626` 的 `DataPath` 可指向绝对路径 ⇒ CLI 注入 `DFLegacy__DataPath=<run>/server/state.json`，**活存档不被 run 写** |
| 真实 PID 归因 | Launcher `Services/LaunchFlow.cs:39-43` 启动 DNF.exe 后写 `Started DFLegacy client process <pid>`；`Program.cs:437-443` 追加到 exe 同目录 `launcher.log` |
| netdump 位置与格式 | 在 `G:\Game\DNF_ACT1\DF`（DNF.exe 同目录），`netdump-<pid>.log`，行首**无方括号**、时间为**本地时间**（`net_dump.cpp:232-245` 用 `GetLocalTime`） |
| 场景 hex 证据 | `c2s` = `000B2B0000000000000001`，`s2c` = `000C2F0000000000000001`，在 6/6 份既有 netdump 样本中出现（`[抓包]`） |
| 双账号前置 | `DF/town-test/launcher.json` **当前只有 1 个账号**；需要用户自行补第二个，**密码绝不在对话/日志/manifest 中出现** |
| 端口 | TCP 7001 与 TCP/UDP 2311；跑前须确认未被占 |

## 你要做的事（按顺序）

### A. 真机运行（需要用户配合，这是 S0 的验收本体）

1. **前置核对**（只读）：`Config.ini` 有 `DumpNetwork=1`；`ijl15.dll` 含 netdump；端口未被占；账号数 ≥ 2。
   - 若账号不足 2 个：**停下来请用户在本机补配置**，不要在对话里索取或写入任何密码。
2. 让用户按 `tools/acceptance/RUNBOOK.md` 操作：跑上面那条命令，在提示下完成 A/B 的 UI（登录→选角→进城→A 靠近 B）。
3. **跑完立刻留档**：`runs/<runId>/` 全部内容 + 命令 stdout。
4. 若撞上 party15 崩溃（队员收到自己 CMD 73 后约 15 秒被 RST）：**只记录现象，不要修**（所有者已明确不阻塞）。

### B. 验收判据（spec §5.3，缺一不可）

1. **两次一致**：对同一次 run 再 `judge` 一次 ⇒ `status` 与 `counts` 一致。
2. **能变红**：改掉一个 `[存在]` 期望的 `pattern` 为不存在串 ⇒ 出现 `fail` 且 `status == "red"`；**改回**后回到非红。
3. **自解释**：只看 `runs/<runId>/` 能回答"跑的是什么／谁参与／T0 依据／哪几项要人看"。

### C. 把归一化判定提升进 git

`tools/acceptance/verdicts/town-two-clients.json`（**这一步才进 git**；`runs/` 不进 git）。
**不得**用 dry-run 的 digest 冒充真机判定。

### D. 全分支终审

派一个**全新上下文**的审查者做一次全分支审查（范围 `e1acf5a..HEAD`），重点：
- 硬约束合规：不改产品代码、`runs/` 不进 git、`server.json` 未被 JSON 序列化重写、密码不外泄；
- 三分判定的防恒真性质；`[语义]` 项为 `needs-human` 而非失败；
- 已记录的 deferred minors 是否需要现在修（见账本）。

### E. 交付给所有者（用中文，简洁）

1. **一笔 commit 清单**（含本次真机 run 产生的新提交）；
2. **`town-two-clients.json` 的实际内容**（三分计数，不是"通过"二字）；
3. **你做的每一个 Ruling**（你替所有者做的决定 + 做错会付出什么代价）；
4. 明确说明：哪些是 `[真机]` 证据，哪些仍是 `[未验证]`。

## 硬约束（违反即失败，逐字遵守）

- **禁止人工编号**：`runId` 全自动（`<UTC时间戳>-<git短哈希>-<场景id>`）。
- **判定三分**：`pass`/`needs-human`/`missing-evidence`（外加 `fail` 用于变红）。**不得**只输出一个绿勾。`[语义]` 档**不等于失败**。
- **每个场景至少一条能失败的期望**，否则判定无效（防恒真）。
- **`runs/` 不进 git；`tools/acceptance/verdicts/` 进 git**。
- **`state.json` 每 run 隔离**：绝不写 `DF/town-test/data/state.json`。
- **`server.json` 只能文件复制 + 定向修改**，禁止 JSON 序列化重写（含 `"//"` 注释键）。
- **不改产品代码**：只动 `tools/acceptance/`；不动 `src/`、产品 `tests/`、`STATUS.md`。
- 证据六类 `[静态]/[抓包]/[真机]/[回放]/[推断]/[未验证]` + `[作废]`；判定档 `[存在]/[字节]/[语义]` 是**另一套**，不混用。被推翻的结论**不删原文**，标 `[作废]` 指向推翻它的证据。
- 提交**必须带 pathspec**；`.recon/`、`docs/`、`.superpowers/` 被 gitignore ⇒ 新增要 `git add -f`，且 **`-f` 必须写在 pathspec 之前**。**绝不 `git add -A`**；**逐个文件列 pathspec**（目录 pathspec 会带进 `__pycache__`）。
- Python 读文本**显式 `encoding="utf-8"`**；跑前设 `$env:PYTHONIOENCODING='utf-8'`。
- 落盘一律 `>` 重定向；**不要** `python x.py | Tee-Object out.txt | Select-Object -First N`（会截断落盘文件）。
- **还原实验只能用备份文件**，不要对含未提交修复的文件跑 `git checkout --`（本轮已因此丢过一次 Important 修复）。

## 怎么执行

- 用 **subagent-driven-development** 技能：真机运行与交付由你（控制者）直接做；全分支终审派**一个**新上下文审查者。
- **给审查者的派发要短**：本轮实测，长清单派发会让子智能体陷入重复自问自答并耗尽上下文（rev-a/rev-b/rev-c 都发生过）。把任务压成 3–5 个明确问答点。
- 团队人数上限 **8**；到顶时**复用**在场成员，不要硬建新成员。
- 子智能体上下文耗尽是常见故障：一旦发现它停在分析阶段未交付，**接手或改派**，不要把任务无限留在"进行中"。

=== 提示词结束 ===

---

## 附：这份提示词的来源

- 设计与原计划：`docs/superpowers/specs/2026-10-07-local-multiclient-acceptance-s0-design.md`（SHA256 前 16 位 `4F707655F90AA1DB`）、
  `docs/superpowers/plans/2026-10-07-local-multiclient-acceptance-s0.md`（`F9E9B1ACCAC74C75`）。
- 一条命令的补缺依据与骨架见 SDD 工作区 `.superpowers/sdd/2026-10-07-local-multiclient-acceptance-s0/one-command-amendment.md` 与 `b2b-cli-skeleton.md`。
- 端到端桩验证脚本与输出同在 SDD 工作区：`e2e_one_command_fake_binaries.py`、`e2e-one-command.txt`
  （桩二进制下产出 `pass 3 / needs-human 1 / missing 0 / fail 0`，活存档未被改写）。
- 所有裁决、事故与 deferred minors 都在该工作区的 `progress.md`，**以它为准**。
