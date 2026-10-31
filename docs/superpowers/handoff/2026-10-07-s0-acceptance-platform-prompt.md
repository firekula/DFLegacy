# 新对话提示词 · S0 本机多客户端验收平台

> **怎么用**：在新会话里把下面 `=== 提示词开始 ===` 到 `=== 提示词结束 ===` 之间的内容**整段粘贴**，
> 或者只说一句：**"读 `docs/superpowers/handoff/2026-10-07-s0-acceptance-platform-prompt.md`，按里面的提示词执行"**。
> 两种都行；后者更省 token。

---

=== 提示词开始 ===

## 你的身份与唯一任务

你是 **DFLegacy**（DNF 赛里斯服 ACT1 客户端本地模拟服务端）项目的执行者。工作区 `G:\Game\DNF_ACT1\DFLegacy`。

**唯一任务**：实现 **S0 —— 本机多客户端验收平台**。

**要解决的问题（所有者原话）**：
> "每次更新，在另一台电脑上都需要我自己手动操作和复制日志"

即：现在每次多客户端联机验收都要人起服务端、人起客户端、人决定日志编号、人把日志搬到一起。
S0 把这件事换成**一条命令产出可 diff 的判定**。

**这不是**：完整协议地图、多进程拆分、局域网联机、修 party15 崩溃。那些是 S1–S4，各自独立立项。

## 第一步（不许跳过）

1. `recon_status`（本项目规矩：新会话先查接入状态，报 error 就先用 `recon-onboarding` 技能）。
2. 读设计文档：`docs/superpowers/specs/2026-10-07-local-multiclient-acceptance-s0-design.md`
   （SHA256 前 16 位 `4F707655F90AA1DB`，185 行）
3. 读实现计划：`docs/superpowers/plans/2026-10-07-local-multiclient-acceptance-s0.md`
   （SHA256 前 16 位 `F9E9B1ACCAC74C75`，1372 行）
4. `git log --oneline -3` 确认起点：计划提交是 `f8a9a4c`。**若这两个文件的哈希对不上**，
   说明它们被改过 —— 先看清楚改了什么再动手，不要按记忆执行。

**这份提示词不重复设计内容。** 设计与全部代码都在上面两份文件里；你按计划里的任务逐个执行。

## 为什么是这两份文件（不要重新设计）

- 设计文档里的每个决定都带**证据**（例如"工具都在、缺的是编排层"列了 `net_dump`/`client-stacks`/`.tmp/` 的具体文件与大小）；
- 计划里每个任务都有**逐字可抄的测试与实现代码**、每步的 Run/Expected、以及提交命令；
- 所有者已审过设计并逐条确认（含"判定三分"、"`state.json` 每 run 隔离"、"`runId` 全自动"三条取舍）。

**结论：照计划执行，不要另起设计。** 若你发现计划里有真缺陷（代码写不通、前后矛盾），
按本项目的裁决纪律处理：**改计划要说明理由并记录**，不要静默偏离。

## 硬约束（违反即失败）

取自计划的 Global Constraints，逐字遵守：

- **禁止人工编号**：`runId` 全自动（`<UTC时间戳>-<git短哈希>-<场景id>`）。**不得**再出现
  `server-round6b`、`party44` 这类人工编号。
- **判定三分**：`pass` / `needs-human` / `missing-evidence`。**不得**只输出一个绿勾。
  `[语义]` 档**不等于失败** —— 它是"当前没有 golden bytes 可自动判定"的诚实标注。
- **每个场景必须至少有一条能失败的期望**，否则判定无效（防恒真判定）。
- **`runs/` 不进 git；`tools/acceptance/verdicts/` 进 git**。产物可丢，判定必须留版本控制轨迹。
- **`state.json` 每 run 隔离**：绝不共用 `DF/town-test/data/state.json`（它是活存档，152,435 B，
  实测仍在变）。复制 → 跑 → 归档。
- **`server.json` 只能文件复制 + 定向修改**：它含 `"//"` 注释键，**禁止**用 JSON 序列化重写后覆盖；
  改动必须记进 `manifest.json`。
- **不改产品代码**：只新增 `tools/acceptance/`；**不动** `src/`、产品 `tests/`、`STATUS.md`。
- 证据六类标记 `[静态]`/`[抓包]`/`[真机]`/`[回放]`/`[推断]`/`[未验证]` + `[作废]`；
  判定档 `[存在]/[字节]/[语义]` 是**另一套**，不混用。被推翻的结论**不删原文**，标 `[作废]` 指向推翻它的证据。
- 提交**必须带 pathspec**；`.recon/`、`docs/`、`.superpowers/` 被 gitignore ⇒ 新增要 `git add -f`，
  且 **`-f` 必须写在 pathspec 之前**。**绝不 `git add -A`**。
- Python 读文本**显式 `encoding="utf-8"`**；跑前设 `$env:PYTHONIOENCODING='utf-8'`。
- 落盘一律 `>` 重定向；**不要**用 `python x.py | Tee-Object out.txt | Select-Object -First N`（会截断落盘文件）。
- **不引入新依赖**：Task 0 若发现 `pytest` 或 YAML 解析器缺失，**改选型**（unittest / JSON 场景），
  **不要** `pip install`。

## 怎么执行

用 **subagent-driven-development** 技能：每个任务派一个**全新上下文**的实现者，任务之间做审查
（规格合规 + 代码质量两道裁决），全部完成后做一次全分支终审。

**环境注意（上一轮实测过的坑，别重踩）**：
- 团队人数上限 **8**；到顶时**复用**在场成员（尤其做修复时），不要硬建新成员。
- 若用 worktree 隔离：`.superpowers/` 与 `docs/` **不会**进 worktree（被 gitignore），
  而计划要读的产物可能就在那里 —— 主人明确要求过"先提交，再走 worktree"，所以**先提交再建**。
- `task-brief` / `review-package` 用 `git rev-parse` 求 repo root ⇒ 在 worktree 里 `cd` 之后它们会把产物
  写到 worktree 的 `.superpowers/` 下，于是同一计划出现**两处工作区**。一律用**绝对路径**，
  并只认一个权威工作区。
- 目录型 pathspec 会把 `__pycache__/*.pyc` 带进提交 ⇒ **逐个文件列 pathspec**，提交前后 `git status --porcelain` 核对。

**Task 0 必须先做**：它是环境前置验证，可能改变后面所有任务的技术选型（`pytest` vs `unittest`、
YAML vs JSON 场景）。它的结论要写进 `tools/acceptance/README.md` 并提交。

**Task 1–5 全部可以在不启动任何游戏进程的前提下测完**（用假 actor 命令、用已落盘的 netdump 样本、
用构造的产物目录）。**只有 Task 6 需要真人操作客户端**。

## S0 的完成定义（计划的 Task 6 就是验收）

1. 一条命令产出判定，产物落 `runs/<runId>/`，**归一化判定**落 `tools/acceptance/verdicts/town-two-clients.json`；
2. **同一次 run 判两次，`status` 与 `counts` 一致**；
3. **能变红**：故意改掉一个期望的匹配串 ⇒ 判定必须出现 `fail` 且 `status == "red"`（改回后回到非红）；
4. 只看 `runs/<runId>/` 能回答"跑的是什么、谁参与、T0 是什么、哪几项要人看"。

**完成时交给主人**：一笔 commit 清单、`town-two-clients.json` 的实际内容（三分计数）、
以及**你做的每一个 Ruling**（你替我做的决定 + 做错会付出什么代价）—— 不要只报"通过"。

## 两个必须说清楚的前提

- **Task 6 需要人**：客户端闭源，UI 动作无法自动驱动（"窗口自动化"是独立的大坑，不在 S0）。
  计划里的 `RUNBOOK.md` 就是给真人照做的步骤；**不要把"需要人点几下"当成 S0 失败**。
- **party15 崩溃不在范围**：所有者已明确"不阻塞，先搭流程"。你在 Task 6 真机跑时可能会撞上它
  （队员收到自己的 CMD 73 后约 15 秒被 RST），**记录现象即可，不要顺手去修**。

=== 提示词结束 ===

---

## 附：这份提示词的来源（给"下一个接手的人"）

- 设计与计划均由本项目所有者在本轮对话中**逐节确认**后定稿；
- 三次需求修正在设计文档 §0 留档（我最初两次理解错了方向，不可删）；
- 计划的自查修掉了 3 个缺陷：`_cmd_judge` 是空桩、`run` 命令没有真机路径、
  `[字节]` 档与 `[存在]` 档在实现上无差别（已补测试）。
