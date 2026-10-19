# 新对话提示词 · S2（多进程拆分）设计对话

> **怎么用**：在新会话里把 `=== 提示词开始 ===` 到 `=== 提示词结束 ===` 之间的内容**整段粘贴**；
> 或只说一句：**"读 `docs/superpowers/handoff/2026-10-18-s2-design-brainstorm-prompt.md`，按里面的提示词执行"**。
>
> 本文件只负责**把设计对话开起来**，不包含 S2 的设计结论——结论要在新对话里由所有者拍板。

---

=== 提示词开始 ===

## 你的身份与唯一任务

你是 **DFLegacy**（DNF 赛里斯服 ACT1 客户端本地模拟服务端）项目的执行者。工作区 `G:\Game\DNF_ACT1\DFLegacy`。

**唯一任务**：用 `brainstorming` 技能，把 **S2（多进程拆分）** 从"路线图一句话"推进成**可批准的设计稿**。
**不要写实现代码**：本对话的终点是一份 `docs/superpowers/specs/YYYY-MM-DD-*s2*-design.md`，经所有者批准后才转 `writing-plans`。

## 第一步（不许跳过）

1. `recon_status`（本项目规矩：新会话先查接入状态；报 error 就先加载 `recon-onboarding`）。
2. 加载 `brainstorming` 技能，按它的**架构级**路径走（分类 → 澄清提问，一次一个 → 2~3 个方案 → 分段设计 → 写 spec → 所有者审阅）。
3. 读这三份，拿到 S2 的原始定义与已有关键证据：
   - `docs/superpowers/specs/2026-10-07-local-multiclient-acceptance-s0-design.md` §8（S2 的一句话定义）
   - `docs/superpowers/sdd/2026-10-05-official-login-channel/RESUME.md`（官服栈现状、可重跑命令、坑）
   - `docs/superpowers/sdd/2026-10-05-official-login-channel/topology.md`（17 个进程 / 端口归属 / 启动方式）

## ⚠️ 开问之前必须先纠正的**前提错误**

S0 路线图对 S2 的通信层描述是：「接口先用**已实证**的通信层（**4 字节头 `u16 id + u16 size`**、TCP 心跳 **id 4202**、业务走 UDP）」。
**这两条在本仓库找不到任何证据支撑，属于不该被继承的说法**：

- 全仓 grep `4202`：**零命中**（含 `.superpowers/sdd/2026-10-05-official-login-channel/**`）。
- 客户端↔入口/频道服的帧头，仓库里**已定案为 11 字节**，不是 4 字节：
  `docs/superpowers/sdd/2026-10-05-official-login-channel/SPEC-login-to-channel.md:475-498`
  —— `[0]category / [1]packet id / [2..5]size(LE, 含头) / [6..9]校验 / [10]ackOk`；
  证据来自 `tagPacketHeader` 的四个访问器 + `onCS_CONNECT` 的 `size-11`/`header+11` + `onSC_GET_GC_INFO` 的 `[eax+0xb]`。
- 我们自己的 `PacketFrame`（10 字节头 + 载荷首字节=记录数）与官方模型**同构**：
  `docs/superpowers/sdd/2026-10-05-official-login-channel/open-questions.md:171-185`
  —— 线上字节完全相同，**不要改它**。
- 另有 `14 字节头` 的旧假设，已标 `[作废]`：`STATIC-FINDINGS.md:299,319,336-337,423-426`。

**所以你开局要做的第一件事**：把这个矛盾**摆到所有者面前**（一句话 + 上面三条引用），并问他：
**S2 的"接口"到底指哪条链路**——①客户端↔进程（已定 11 字节/10 字节模型），
②官方进程↔进程（尚未抓过，可能完全不同），③我们内部模块↔模块（可以自选）？
**在这个问题有答案之前，不要问别的问题。**

## 已确凿的事实（不要重新发现）

| 事实 | 值 / 出处 |
|---|---|
| 官方栈 | 容器 `dnf`（镜像 `llnut/dnf`，bridge，`172.17.0.2`），**17 个进程**，`topology.md` 全表 |
| 关键进程/端口 | `df_channel_r` 7001、`df_bridge_r` 7000、`df_relay_r` 7300、`df_game_r` 30011/30052/3811、`df_manager_r` 40403、`df_coserver_r`、`df_stun_r`、`df_dbmw_r` 20203/20303/20403、`dnf-gate-server` 5505 —— `work/port-map.txt` |
| 端口归属怎么复现 | `docker cp work/port-map.sh dnf:/tmp/ && docker exec dnf sh /tmp/port-map.sh`（镜像内**没有** ss/netstat/lsof/busybox；只能读 `/proc/net/*` + `/proc/<pid>/fd`） |
| 宿主侧互斥（坑 14） | `DFLegacy.Server` 与容器**抢** `TCP 7001` / `UDP 2311`：先起 DFLegacy 则官方 7001 静默绑不上。**做 S2 实验前先明确"这次测的是谁"** |
| 容器内注入 | 只能用 `bash /dev/tcp` + `printf` + `timeout cat`（镜像无 python3）；判据 `timeout` 退出码 124=对端没关、0=关了 |
| 容器日志 | `channel/log/*.log` **重启即截断**（坑 15）：动容器前先拷出来 |
| 已关闭的大问题 | Q-CRC（偏移 6..9 入站不校验）、Q-GATEAPI（网关 3 条路由，`/api/v1/auth` 是 AES-256-GCM）、Q-OFFCHAN（官服 `id=3` 与 2008 同构，多 4 B 是加密补块）、Q-OP12（UDP 侧查 extra 表而该表无写入者 ⇒ UDP 收到即丢） —— `RESUME.md:13-20` |
| 仍开着的 | 其余 8 个 handler 只有字符串级结论；`Q-TOKEN32` 后 16 字节语义；`Q-CLASSBYTE` 的 `0x7c` 解释；网关 42 字节字段语义；STUN `05` 探头应从另一源端口回 + MTU_CHECKER —— `RESUME.md:22-28` |

**环境状态（2026-10-18 实测，与 `RESUME.md` 写就时的 10-05 不同）**：
- `docker` 守护进程**未运行**：`docker ps` 报 `failed to connect to the docker API at npipe:////./pipe/dockerDesktopLinuxEngine`。
  因此容器 `dnf`（官方 17 进程栈）**当前不可用**。
- ⇒ **S2 的设计可以照常做**（设计不需要容器），但任何需要官服栈取证的步骤（尤其是从未抓过的
  "官方进程↔进程"链路）必须先：启动 Docker Desktop → `docker ps -a` 确认 `dnf` 容器存在且能起 →
  按 `RESUME.md:60-78` 的命令重跑。**在这些就绪之前，不要把"官方链路"写成已定事实**，
  最多写成 `[未验证]` 并列出取数计划。
- 另一条 10-05 的环境注意事项仍然有效：宿主 `DFLegacy.Server` 会与容器抢 `TCP 7001` / `UDP 2311`（坑 14），
  做任何"宿主连官服"的实验前先明确这次测的是谁。

## 设计对话要问到的决策点（**一次只问一个**，按此顺序）

1. **接口指哪条链路**（见上"前提错误"）。这是 S2 全部设计的地基。
2. **拆分的动机**：为了"像官服"（对齐 17 进程边界，便于逐进程对照反编译），
   还是为了"工程收益"（隔离崩溃、按场景独立重启、便于并行开发）？
   —— 动机不同，边界划分会完全不同（官服有 `community/auction/guild/point` 等我们没实现的进程）。
3. **"进程"是真 OS 进程还是逻辑模块**：同一个仓库里拆成多个 exe，还是单进程内按接口分模块、
   只把有隔离收益的（如 `server` ↔ `game`）拆成真进程？
4. **谁拥有状态**：`state.json` 现在是单文件单一持有者（`JsonGameStore`，`DataPath` 可配）。
   拆成多进程后，账户/角色/城镇/副本/组队各自归谁？跨进程怎么读写（共享文件 / 内部 RPC / 单一写入者）？
5. **验收方式**：S2 完成后，用什么证明"拆分没破坏行为"？
   （建议候选：复用 S0 的 `tools/acceptance` 矩阵 + 新增"跨进程链路"的 netdump 证据；
   是否需要 `[真机]` 一次双客户端进城？）
6. **第一步最小切片**：S2 里哪一刀最值得先切（例如先把 `game` 会话从 `EntranceService` 里分出去，
   还是先立一条跨进程通信库 + 两个假进程的 e2e）？

## 硬约束（逐字遵守）

- **本对话不写产品代码**：`src/` 零改动；spec 批准后才出计划、才实现。
- 证据六类 `[静态]/[抓包]/[真机]/[回放]/[推断]/[未验证]` + `[作废]`；**被推翻的结论不删原文**，标 `[作废]` 指向推翻它的证据。
- **不继承未经验证的说法**：本文件点出的"4 字节头 / 4202"就是反面教材——要么补证据，要么改口径。
- 设计稿写进 `docs/superpowers/specs/`（`docs/` 被 gitignore ⇒ `git add -f`，且 `-f` 写在 pathspec 之前）。
- 提交逐个文件列 pathspec，**绝不 `git add -A`**。
- 与 S1 的关系：`protocol/`（已交付）是"协议地图 + 两本账"的正式资产；
  若 S2 引入新的跨进程 opcode/帧格式，**它们应当进 `protocol/`**（`python -m protocol.s1 verify` 必须仍然 5/5）。

## 交付格式（本对话结束时应有的东西）

1. 一份设计稿 `docs/superpowers/specs/2026-10-18-...s2-design.md`，含：目标/非目标、进程边界表（每个进程：职责、状态归属、对外接口、与官方哪个进程对应）、
   通信层决定（含证据与 `[未验证]` 标注）、最小切片与验收判据、风险。
2. 给所有者的一页摘要：**你替他做了哪些决定、每个决定若错的代价**。
3. 明确写出**进入实现前还缺什么**（例如"官方进程↔进程那条链路没抓过"）。

=== 提示词结束 ===

---

## 附：本交接的证据来源

- S2 的原始定义：`docs/superpowers/specs/2026-10-07-local-multiclient-acceptance-s0-design.md:238`
- 前提纠错依据：`docs/superpowers/sdd/2026-10-05-official-login-channel/SPEC-login-to-channel.md:475-498`、
  `…/open-questions.md:171-185`、`…/STATIC-FINDINGS.md:299,319,336-337,423-426`；`4202` 全仓 grep 零命中（本轮实测）
- 官服栈状态与坑：`…/RESUME.md:13-28,30-58`；端口归属：`…/work/port-map.txt`；拓扑：`…/topology.md`
- S1 交付物（跨进程知识将来落这里）：`protocol/README.md`、`protocol/map/opcode-map.tsv`
