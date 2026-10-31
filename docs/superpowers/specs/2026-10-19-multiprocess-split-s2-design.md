# S2 设计稿 · 多进程拆分（第一刀：入口握手族）

> **状态**：待所有者批准。批准后才转 `writing-plans`，本轮**未改 `src/` 一行**。
> **来源**：`docs/superpowers/handoff/2026-10-18-s2-design-brainstorm-prompt.md`（该文件只负责开启设计对话，不含结论）。
> **决策记录（滚动日志，含被推翻的中间判断）**：`.recon/out/2026-10-18-s2-decisions.md`（未入版本控制）。
> **本稿的两份勘察输入**（未入版本控制，关键事实已在正文内联到一手来源）：
> `.recon/out/2026-10-18-s2-state-inventory.md`、`.recon/out/2026-10-18-s2-acceptance-capability.md`。

---

## 0. 结论速览

1. **S2 的"接口"= 官方进程↔进程那条链路**（所有者 D1）。它**不是没抓过**：仓库里已有 206 包的进程间抓包、帧头与客户端面同构、3 条报文语义已解、`[0]` category 是每链路一个值。缺的是**建链/登录握手、opcode 全表、UDP 行为**。
2. **第一刀只切一个边界**：把**入口握手族（TCP 2311 与 8080）**从 `DFLegacy.Server` 拆成新进程 `DFLegacy.Channel`（我们的 `df_channel_r`）。它是全仓**唯一零共享状态、零存档依赖**的边界。
3. **落盘权威不动**：`state.json` 仍只由 game 进程写（D4）。第一刀**没有任何落盘 RPC**。
4. **链路造型**：复用现有 `PacketFrame`（与官方 11 字节头线上同构）+ 新库承载注册/心跳；报文 ID 只有 `4` 复用官方，其余 128/129/130 是我们自己的号段。
5. **验收三段**：链路帧断言 → 单体/拆分逐字节对比 → 真机一次（D5）。
6. **本文不批准任何实现**；§9 列了进入实现前仍缺的东西，其中最重要的一条是「官方进程间链路的建链报文从没抓过」。

---

## 1. 前提纠错（本稿**不继承**的说法）

### 1.1 被否决的前提：4 字节头 / 心跳 `4202` / 业务走 UDP

S0 路线图对 S2 的通信层描述是「接口先用**已实证**的通信层（4 字节头 `u16 id + u16 size`、TCP 心跳 id 4202、业务走 UDP）」
（`docs/superpowers/specs/2026-10-07-local-multiclient-acceptance-s0-design.md:238`）。

**这三条在本仓库找不到任何证据支撑，本稿不继承**：

| 说法 | 状态 | 依据 |
|---|---|---|
| 4 字节头 `u16 id + u16 size` | **否决** | 与已定案的 11 字节头模型矛盾（见 §1.3）。而且本仓库观测到的**每一种**帧头都不是 4 字节：TCP 面 11 B（`PacketFrame.cs:10-13`）；角色选择 UDP 面 11 B（`LegacyDatagramProtocol.cs:14`，且头与体分开发）；MT UDP 帧头 **9 B**（`GameDatagramProtocol.cs:45,183-192`：`[0]`类型 + `[1..4]`序号 u32 + `[5..6]`应用长度 u16 + `[7]`队伍号 + `[8]`标志）；应用层头 **7 B**（`GameDatagramProtocol.cs:44,123-138`：`[0]`mode + `[1..2]`protocolId u16 + `[3..6]`CRC32）。**u16 id 与 u16 size 分属两个不同的头，没有哪个头是 `id+size` 4 字节** |
| TCP 心跳 id `4202` | **否决** | 本轮**自己重跑**全仓 `grep 4202`：**零命中**（含 `.superpowers/sdd/2026-10-05-official-login-channel/**`） |
| 「接口先用已实证的通信层」 | **措辞不成立** | 那三条正是**没有**实证的部分；已实证的是 11 字节头（客户端面）与进程间帧的同构性 |

### 1.2 更正交接文件自身的一处过度纠正：官方进程间链路**抓过**

交接文件第 65 行说「官方进程↔进程**尚未抓过**，可能完全不同」。这句话不准确，本稿更正：

| 已确定的事实 | 证据 |
|---|---|
| 进程间抓包**已完成**（206 包，在容器 netns 内用共享 netns 的 sidecar 抓的） | `docs/superpowers/sdd/2026-10-05-official-login-channel/dual-evidence.md:64-66`、`…/topology.md:144-147` |
| 进程间帧与客户端面**同构**（ID@[1]、size@[2..5]、11 字节头，载荷 = header+11） | `…/STATIC-FINDINGS.md:306-321,443-444`（另有参考实现 packetlog 作独立旁证，`…:442-444`） |
| `[0]` category 是**每链路一个值**：channel↔bridge `0x7b`、dbmw↔对端 `0x6a`、game→manager `0x6d`、官服→客户端 `0x7c`、2008 客户端→服务端 `0x00` | `…/SPEC-login-to-channel.md:451`、`…/dual-evidence.md:251-264`；两种读法（每链路协商值 / 分类常量）**都未被证伪**，登记为 Q-CLASSBYTE |
| 已解语义 3 条：`5`=CHECK_SCRIPT_VERSION（载荷=ASCII 版本号 `"59"`）、`4`=NOTICE_CHANNEL_SERVER（组号+ASCII IP+端口）、`2`=UPDATE_CHANNEL_INFO（gc_no / channel_no / max_user_num / IP） | `…/dual-evidence.md:78-122`（其中 §2.1 的日志值逐字落在抓包字节里） |
| **未解**：`[6..9]`=`0b`/`0d`、`[10]`=`09`/`03` 由谁写、接收方读不读 | `…/dual-evidence.md:137-149` |

**仍然成立的那半句**：官方**建链/登录**报文（例如 `game → manager` 的 `OnTcpServerLogin(TYPE:3, sock:11)`，`…/topology.md:354` 只有日志、**没有帧**）、**opcode 全表**、**UDP 侧行为**都没抓过。所以 §9 把它列为实现前缺口。

### 1.3 已定案、本稿不动的前提

| 事实 | 证据 |
|---|---|
| 客户端面帧头 = **11 字节**：`[0]category / [1]packet id / [2..5]size(LE，含头) / [6..9]未观测到非 0 / [10]ackOk` | `…/SPEC-login-to-channel.md:475-498`、`…/STATIC-FINDINGS.md:339-371`（`tagPacketHeader` 六个访问器逐条反汇编） |
| 本项目 `PacketFrame`（10 字节头 + 载荷首字节=记录数）与官方模型**线上字节同构**，**不要改** | `…/open-questions.md:169-176`、`…/SPEC-login-to-channel.md:495-498` |
| `[6..9]` **没有** checksum 访问器、二进制里**没有** `Crc` 符号；偏移 6..9 入站不校验 | `…/STATIC-FINDINGS.md:557-563`、`…/SPEC-login-to-channel.md:465-473` |
| `header_*` 那组 cfg 键是**死配置**（没有任何程序读它），帧布局是硬编码 | `…/STATIC-FINDINGS.md:373-413`（已用"两侧都搜 + 正向对照"复测） |
| 「14 字节头」假设 | **`[作废]`**，被 §1.3 第 1 行推翻；原文保留在 `…/STATIC-FINDINGS.md:299,319,336-337,423-426` |

### 1.4 环境事实（本轮实测，决定了本稿的证据上限）

- **Docker 不可用**：`docker ps` → `failed to connect to the docker API at npipe:////./pipe/dockerDesktopLinuxEngine`；`Get-Process 'Docker Desktop'` → 不存在。
  ⇒ 官方 17 进程栈现在拿不到；本稿中凡涉及官服栈新取证的，一律 `[未验证]` + 取数计划（§9）。
- 宿主 `DFLegacy.Server` 与容器抢 `TCP 7001` / `UDP 2311`（`…/topology.md:129-142`，坑 14）。做任何"宿主连官服"实验前先明确**这次测的是谁**。

---

## 2. 决策记录（所有者已拍板）

| # | 决策点 | 选择 | 未选及理由 |
|---|---|---|---|
| **D1** | S2 的"接口"指哪条链路 | **官方进程↔进程**（单选） | 未选 ①客户端↔进程（已定案、S2 不改它）与 ③内部模块↔模块（放弃"逐进程对照反编译"） |
| **D2** | 该链路的保真档位 | **C 两段走**：帧壳用已实证形状，报文语义现在自选；官方逐对报文取证列后补 | 未选 A 全字节对齐（Docker 不可用 ⇒ 写不出可批准的定稿）；未选 B 完全自选（丢掉将来对齐的接口形状） |
| **D3** | "进程"是真 OS 进程还是逻辑模块 | **B 递进**：先只把一刀做成真进程，其余先留，后续逐刀搬 | 未选 A 一次切齐 8 进程（状态/P2P/验收平台同时爆炸）；未选 C 纯逻辑模块（崩溃隔离收益归零） |
| **D4** | 谁拥有状态 | **A 保留单写者**：落盘权威仍只一个进程，其余只持内存态 | 未选 B 按领域分片写（作废统一写事务与幂等提交）；未选 C 共享文件加锁（整份重写会互覆盖） |
| **D5** | 验收方式 | **A 三段门槛**：链路帧断言 + S0 矩阵回归 + 真机一次 | 未选 B 只靠矩阵+真机（定位不到跳）；未选 C 只做链路（端到端断裂） |
| **D6** | 第一刀切哪里 | **入口握手族（2311/8080）独立成进程 = 我们的 `df_channel_r`** | 未选 A 只立链路不搬业务（无隔离收益）；未选 C 先切 game/副本（耦合最重，见 §4.4） |
| **形状** | 第一刀的实现形状 | **A 独立 exe `DFLegacy.Channel` + 新链路库** | 未选 B 单 exe 双角色（`Program.cs` 长出角色开关）；未选 C in-proc 双 transport（无法满足 Tier 1 的真 socket 字节断言） |

**D4 的依据**（这是"不能动"的两个资产）：
`JsonGameStore._gate` 是**唯一**的 store 状态锁，写事务是"候选快照 + quarantine"，且明确"锁内不跨 await"
（`docs/superpowers/plans/2026-10-02-store-write-transaction-boundary.md:7,17`）；
副本奖励的幂等提交明确写着"会话局部函数**无法**安全承担多会话共享奖励的提交"
（`docs/superpowers/specs/2026-10-02-dungeon-reward-idempotent-commit-design.md:17`）。

---

## 3. 目标 / 非目标

### 3.1 目标（本刀交付）

1. **入口握手族**从 `DFLegacy.Server` 拆成真进程 `DFLegacy.Channel`，与官方 `df_channel_r`（地址簿 + 脚本版本分发，自己不知道任何玩家，`…/STATIC-FINDINGS.md:485-486`）对位。
2. 两进程之间跑一条**官方形状**的链路（11 字节头同构 + `[0]` 每链路 category 字节），报文语义现在自选（D2）。
3. 落盘权威不变（D4）。第一刀**没有落盘 RPC**，因此没有"重复发放/丢写"这类分布式风险。
4. 客户端可见行为**逐字节不变**，并用三段门槛证明（D5）。

### 3.2 非目标（本刀明确不做，防止范围蔓延）

- 不切 `game`/副本/组队/交易/好友（第二刀的候选，耦合清单见 §4.4）。
- 不建官方的 `coserver`/`manager`/`bridge`/`dbmw`/`stun`/`auction`/`guild`/`community`/`point`/`statics`/`monitor` 任何进程；它们当前被合并进 game 进程。
- 不改客户端面协议（11 字节头 / `PacketFrame` 同构模型不动）。
- **不把频道信息的 `cur_user_num` 改成实时值**：今天它是配置常量 `options.Channel.CurrentUsers`（`src/DFLegacy.Server/EntranceService.cs:17650-17656`，默认 1），改实时是行为变更，与"拆分不破坏行为"冲突。留给将来的动态地址簿。
- 不做跨进程的落盘事务/两阶段提交。

---

## 4. 进程边界与状态归属

### 4.1 边界表

| 进程 | 职责 | 状态归属 | 对外接口 | 官方对位 |
|---|---|---|---|---|
| **`DFLegacy.Channel`**（新，本刀） | 入口握手族 4 对 opcode：`11/12` 密钥、`5/6` 缓存判定、`9/10` 脚本下载、`1/3` 频道信息（`src/DFLegacy.Protocol/EntranceProtocolEngine.cs:41-48`） | **无持久状态**。每连接一个 `EntranceSessionState`（`EntranceService.cs:228`：`KeyMaterial` 每连接随机 32 B、`SessionToken` 前缀常量 `dfls-local`，`EntranceProtocolEngine.cs:8-21`）；内容缓存 `_content`（`EntranceService.cs:17612-17659`）只读 `etc/channel_info.etc` + 配置 | **TCP 2311、TCP 8080**（客户端，见 §4.2）；TCP 7004（链路服务端） | `df_channel_r`（`ASK/UPDATE_CHANNEL_INFO`、`GET_SCRIPT`、`CHECK_SCRIPT_VERSION`，`…/STATIC-FINDINGS.md:485`）。**世代错位**：官方那代入口是 7001，本项目是 2311（`…/topology.md:157-163`） |
| **`DFLegacy.Server`**（现有，保留） | 频道玩法（CMD 1 登录 + 全部玩法，`EntranceService.cs:15345`）、UDP 数据报、Admin 全部路由 | **全部落盘**（`JsonGameStore`）+ 全部跨会话内存表（城镇/副本/组队/交易/好友/背包/管理员 token） | TCP 7001；UDP 2311/7002/7003；HTTP 8081；TCP 7004（链路客户端） | `df_game_r`（客户端登录唯一在它这里，`…/STATIC-FINDINGS.md:472`）；官方 `coserver`/`manager`/`bridge`/`dbmw` 的职能被它合并 |
| `DFLegacy.Relay`（现有，**不属 S2**） | 无状态 1:1 TCP/UDP 端口转发，单客户端 | 无 | 2311/7001/8080 TCP、2311/7002/7003 UDP（`src/DFLegacy.Relay/RelayOptions.cs:22-23`） | `df_relay_r` 只是 S4 的地址垫片；与 `DFLegacy.Server` **零工程引用** |
| `DFLegacy.Launcher` / `AccountClient` | 客户端侧工具 | — | — | 无对应 |

### 4.2 端口归属（精确到 config 键）

现状（`server.json:20-46`）：**同一个** `Entrance` 监听器绑三个 TCP 端口 —— `{Entrance.Port=2311} ∪ {AdditionalPorts=[7001, 8080]}`，用 `localPort == Channel.Port`（=7001）分流（`EntranceService.cs:220`）。另有 `CharacterDatagram` UDP 2311、`GameplayDatagram` UDP 7002/7003、`Admin` 8081。

拆分后的归属：

| 端点 | 归属进程 | 说明 |
|---|---|---|
| TCP `Entrance.Port`（2311）与 `AdditionalPorts` **去掉** `Channel.Port` 后的项（8080） | **Channel** | 入口握手族 |
| TCP `Channel.Port`（7001） | **Server** | 频道玩法族 |
| UDP `CharacterDatagram.Port`（2311）、`GameplayDatagram.Primary/Secondary`（7002/7003） | **Server** | 与 TCP 2311 同号不同协议，可同时绑 |
| HTTP `Admin.Port`（8081） | **Server** | 管理面 |
| TCP 7004（新增） | Channel 监听 / Server 拨出 | 链路端口，可配 |

> **实现期需确认（`[未验证]`）**：`GameProbe` 也声明 7001 且默认关闭（`src/DFLegacy.Server/ServerOptions.cs:9`、`ProbeService.cs:18-23`）。它与 `Entrance.AdditionalPorts` 的 7001 在同一进程内是既有潜在冲突（**不是拆分引入的**），实现时确认一次即可。

### 4.3 为什么这条边界是干净的（证据，全部为本轮实读）

| 事实 | 位置 |
|---|---|
| 两条协议族由**一个布尔**分开 | `EntranceService.cs:220` `var isChannelPort = localPort == options.Channel.Port;` |
| 非频道口的整个处理体只有**一行** | `EntranceService.cs:15375` `EntranceProtocolEngine.Handle(request, protocolSession, LoadContent())` |
| 频道口是另一行（= 全部玩法） | `EntranceService.cs:15345` `GameProtocolEngine.Handle(request, protocolSession.SessionToken)` |
| 握手会话**每连接新建**，不跨连接、不落盘 | `EntranceService.cs:228`；`EntranceProtocolEngine.cs:6-22` |
| 入口引擎只依赖「内容」两个文件，不碰 store | `EntranceProtocolEngine.cs:36-48`（只吃 `EntranceContent`）；`EntranceService.cs:17612-17659` |

副作用（也是必须写进 §8 的风险）：`EntranceService.cs` 是 **17,727 行 / 单类 / 单个 15,316 行的方法**
（`HandleClientAsync` 起于 `:203`，止于 `:15518`），所以"切进程"之前必须先做一次**不改行为**的抽取（见 §6 I1）。

### 4.4 第二刀的耦合清单（记录，不在本刀）

切 `game`/副本/组队之前必须先有跨进程方案的东西：

1. 跨会话广播是**直接写别的会话的写句柄**：`TownMember.SendAsync`、`DungeonInstanceMember.Send`、`FriendService.Channel.Send`、`PartyChannel.Send`、`ItemTradeChannel.Send`。
2. 跨会话协调靠**不落盘的进程内信箱**：`ItemTradeService._pendingInstalls`、`DungeonInstanceRegistry` 的 6 个信箱。
3. 4 个 hosted 定时器在进程内（`PartyService`、`ItemTradeService`、`DungeonPartyService`、`FatigueResetService`）。
4. TCP 与 UDP 会话靠**同一个 `Guid` 在内存表配对**（`GameplayDatagramService.cs:13,147-198`），端点还是 UDP 侧动态学到的。
5. 同一角色有**两个状态所有者**（会话内存里的背包/金钱 vs store 记录），store 靠内存守门 `_pendingTradeDeliveries` 拒绝陈旧盲写。

---

## 5. 通信层

### 5.1 帧壳

| 项 | 决定 | 依据 / 状态 |
|---|---|---|
| 头布局 | 沿用 `PacketFrame`（10 B 头 + 载荷首字节=记录数）；线上与官方 11 B 头同构 | `…/SPEC-login-to-channel.md:495-498`；**不改它** |
| `[0]` category | 本链路取 **`0x7b`**，集中为一个常量 | `…/SPEC-login-to-channel.md:451`。**`[未验证]`**：Q-CLASSBYTE 两种读法（每链路协商值 / 分类常量）都未被证伪；若将来定案为分类常量，0x7b 是已有证据里最接近的取值；若是协商值，两进程都是我们自己，取任何值只影响我们自己 |
| `[6..9]` | 填 **0** | 客户端面与 channel↔bridge 帧实测全 0；`tagPacketHeader` 无 checksum 访问器、二进制无 `Crc` 符号（`…/STATIC-FINDINGS.md:557-563`）。两条进程间短帧实测 `0b`/`0d` **未解**，不模仿 |
| `[10]` | 填 **1** | 与现有帧一致。官方进程间短帧实测 `09`/`03` **未解**，不模仿 |
| `[2..5]` | 长度含 11 字节头（沿用现有语义） | 同构模型 |

### 5.2 报文清单（第一刀）

| ID | 方向 | 名称 | 载荷 | 编号依据 | 承重 |
|---|---|---|---|---|---|
| **4** | channel→game | `NOTICE_CHANNEL_SERVER` | `int32 serverGroup` + `char[16]` ASCII IP + `int32 port` = 24 B | **复用官方 ID 与官方载荷**：官方帧 35 B = 11+24，且三个字段值在官服日志里逐字对上（`…/dual-evidence.md:88-91`） | 记录 |
| **128** | game→channel | `REGISTER` | `u16 protoVer` + `u32 gcNo` + `u32 channelNo` + `u32 maxUsers` + `u32 curUsers` + `char[16]` IP + `u32 gamePort` = 38 B | 自选（D2）。字段集部分对应官方 `UPDATE_CHANNEL_INFO`(2)，但**不复用 ID 2**：官方载荷只解出 4 个字段且有缺口（`…/STATIC-FINDINGS.md:323-330`），复用 ID 会让人误以为字节一致 | 是 |
| **129** | channel→game | `REGISTER_ACK` | `u16 protoVer` + `u32 accepted` + `char[20]` channelName + `u32 channelPort` = 30 B | 自选 | 是 |
| **130** | **双向** | `STATUS` | `u64 uptimeMs` + `u32 activeSessions` + `u32 loggedInUsers` + `u64 totalConnections` + `u64 totalPackets` = 32 B | 自选；兼心跳。**两侧各发自己的计数**（单向心跳无法判定"对端还活着"：接收方没有回包可等，会把健康链路误判成 down）。game 侧上报"握手侧在线数"，让 `/api/status.ActiveSessions` 不因拆分而变小（§5.4） | 是 |

**载荷的口径（实现时必须照此，否则偏移会错一格）**：上表"载荷"= **官方模型的载荷** = 帧偏移 **11** 起 = `PacketFrame.Body[1..]`。
`PacketFrame.Body[0]` 是记录数（`PacketFrame.cs:16,34-36` 自证：`RecordCount => Body[0]`，`Create` 会把它前置），它**就是**官方模型的 `[10]`。
字段映射（`PacketFrame.cs:10,20-39`）：`Type`→`[0]`、`ProtocolId`→`[1]`、`DeclaredCrc32`→`[6..9]`、`Body[0]`→`[10]`；
本链路的建帧调用固定为 `PacketFrame.Create(protocolId, payload, type: 0x7b, recordCount: 1, includeCrc32: false)`。
所以 §5.1 的 `[10]=1` 与现行 `PacketFrame` 行为一致，而官方 `channel↔bridge` 的 `id=4` 实测 `[10]=00`（`…/dual-evidence.md:90`）——
⇒ 我们**复用 id=4 的 ID 与载荷布局，不复用它的头字节**；这一点是"官方形状、自选语义"（D2）的直接后果，不是疏漏。

私有号段从 **128** 起：官方 `df_channel_r` 的入站注册表是 `1,2,3,5,6,9,10,11,14`（`…/dual-evidence.md:229`），128 起不可能撞。

**明确不做**：官方 `CHECK_SCRIPT_VERSION`(5)。我们没有"脚本版本号"这个概念（脚本来自 `ScriptPvfPath`），官方那条是 channel↔bridge 的，第一刀没有这个对端。

### 5.3 生命周期与失败语义

- **谁拨谁**：game 拨 channel（生产者向聚合者注册；官方是 game→bridge 注册、channel→bridge，我们把 channel+bridge 合成一个进程）。
- **建链**：`REGISTER` → `REGISTER_ACK`。`accepted=0` ⇒ game 记错误日志并退避重试，**不崩、不影响客户端服务**。
- **稳态**：**双方各自**每 1 s 发一次 `STATUS`（兼心跳，方向双向）；连续 3 个间隔收不到对端任何帧 ⇒ 对端标记 down，打 warn，**继续服务客户端**。
- **重启**：channel 重启 ⇒ game 自动重连重注册；game 重启同理。**任一方单独启动都不失败**。
- **版本不匹配**：`accepted=0` + 版本回带；不降级、不猜测。
- **失败语义红线**：第一刀**没有任何落盘 RPC**，所以不存在重复发放/丢写的分布式风险；唯一失败语义是"对端不可达/不兼容 ⇒ 观测降级 + 重试"。

### 5.4 这条链路承什么重（诚实版）

| 类别 | 内容 | 依据 |
|---|---|---|
| **承重 1：admin 计数不丢** | `/api/status` 的 `TotalConnections`/`TotalPackets`/`ActiveSessions` 读 `RuntimeState`，而它在 `EntranceService.cs:221` 为**两个端口**都登记会话。拆完后 2311/8080 的握手流量不在 game 进程里 ⇒ 由 `STATUS` 的 `totalConnections`/`totalPackets`/`activeSessions` 补齐这三项，并在 `/api/status` 加"channel 侧在线/离线"字段 | `src/DFLegacy.Server/Program.cs:329-334`、`EntranceService.cs:221` |
| **承重 2：双侧存活可见** | 今天整栈一起死；拆完后能分别看到"握手门"与"玩法面"谁挂了。这是本刀**唯一的新观测能力** | 同上；另见 §8 R1 |
| **记录但不承重** | `REGISTER` 里的 gc_no / maxUsers / curUsers / IP / port **今天不驱动任何客户端可见字节**（频道信息仍来自配置，`EntranceService.cs:17650-17656`）。它是给第二/三刀（多 game 实例、动态地址簿）留的注册表 | 同左 |
| **明确接受的观测降级** | `/api/sessions`（**列表**）拆后只显示 7001 侧会话；**计数**（`ActiveSessions`）由 `STATUS.activeSessions` 补回。握手连接是毫秒级、用完即弃（`…/topology.md:161`），列表价值低，**本刀不补**。这是有意识的取舍，不是遗漏 | `…/topology.md:161`、`Program.cs:355` |

### 5.5 进程间报文进 `protocol/`（S1 契约变更）

- 新增 `kind=S2S` 的报文表 `protocol/map/s2s-map.tsv`，与 §5.2 一一对应。
- `protocol/` 的校验契约今天是**硬编码**的：kind 仅 `CMD`/`NOTI`、opcode 总数硬编码 **1171**、`map/`+`ledger/` 递归不得有清单外文件（`protocol/importer.py:10-16`、`protocol/verify.py:33,36,39-42,111-117`）。
- ⇒ 本刀必须**同一次提交**内扩展契约（新增 kind + 新文件 + 总数），且 **`python -m protocol.s1 verify` 仍须 5/5 PASS**（交接文件硬约束）。
- 基线（本轮实跑）：`python -m protocol.s1 verify` = **5/5 PASS，exit 0**（`OK sha256 / rows_and_keys / baselines / internal / s0_crosscheck`）。

### 5.6 端口与「单体模式仍可跑」开关

`server.json` 增加 `Processes` 段：`Channel.Enabled`、`Channel.LinkPort`（默认 7004）、`Game.EntranceBinding`。

- `Channel.Enabled=false` 且 `Game.EntranceBinding=true` ⇒ `DFLegacy.Server` 退化成**今天的单体模式**（一个进程绑全部端口）。
- 这不是为了兼容旧版，是为了**验收**：同一客户端、同一场景跑「单体 / 拆分」两档，**逐字节对比**——D5 第二段最便宜也最硬的证据。
- 端口选择：TCP **7004**。官方端口表（`…/topology.md:87-110`）与本项目现用端口（2311/7001/7002/7003/8081）都不含它。

---

## 6. 最小切片（五个增量，每步自带门槛，任一步可停）

| # | 增量 | 门槛（必须能变红） |
|---|---|---|
| **I1** | 把握手族的驱动逻辑从 `EntranceService` 抽成**可独立宿主**的单元（同进程内，行为不变） | 现有测试全绿 + 客户端握手字节流与改造前**逐字节相同** |
| **I2** | 新 exe `DFLegacy.Channel` + `Processes` 开关；单体模式仍可跑 | 同一客户端在**单体 / 拆分**两档下握手脚本字节相同 |
| **I3** | 链路库 + 建链/注册/心跳/超时/重连 | Tier 1 帧断言（含 `accepted=0` 不建链、3 次心跳缺失标记 down） |
| **I4** | admin 计数与双侧存活可见 | `/api/status` 既有字段不回归 + 新增 channel 侧总计与在线位 |
| **I5** | `protocol/` 扩展 + `verify` 5/5；S0 回归；真机一次 | `python -m protocol.s1 verify` 5/5 + 三段验收全绿 |

顺序理由：I1 是"改结构不改行为"，风险最低且可独立回滚；I2 让"拆分"可开关并立刻产出验收对比能力；I3 才引入新链路；I4/I5 收口。

---

## 7. 验收

### 7.1 三段门槛（D5）

**Tier 1 · 链路帧断言**（真 socket、两个真进程；每条期望都必须能变红）
1. `REGISTER` 线上字节 == 固定 hex：`[0]=0x7b`、`[1]=0x80`、`[2..5]`=11+载荷长 LE、`[6..9]=0`、`[10]=1`；
2. 长度自洽：任意载荷 N ⇒ `size = 11 + N`；
3. `REGISTER_ACK.accepted=0` ⇒ 不建链、双方均不崩、game 退避重试；
4. 对端静默 3 个心跳周期 ⇒ 标记 down 且**继续服务客户端**。

**Tier 2 · 单体 vs 拆分逐字节对比**
- 同一客户端、同一场景、两种模式各跑一次，比较客户端侧 netdump 的**帧序列与字节**；
- 判据：两条流**完全相同**；任何例外（时间戳、端口、会话 id）必须**显式列进例外清单**，不允许"看着差不多"；
- 现有可用资产：`town-two-clients` 有 3 条真 hex 断言；另外 3 个场景是占位骨架且会恒红（`tools/acceptance/scenarios/` 各自文件头自述），**不作为本刀判据**；
- 本刀必须**新增**一个场景：握手 → 登录 → 进城的完整链路（这才是这一刀的门）。

**Tier 3 · 真机一次**
- 双客户端进城（`DF/town-test` 设施），两模式各一次，比对服务端日志锚点：登录 / `SELECT_CHARACTER` / 进城 / 同屏。

### 7.2 验收平台改造项（不修这些，Tier 2 就是假的）

| 缺口 | 位置 |
|---|---|
| 服务端写死单例：单 `_server_proc`、`manifest.server` 单 dict 且第 N 次启动覆写前 N−1 条、单 `server.log`、`wait_until_listening` 只等 Entrance 且只盯最后一个 Popen | `tools/acceptance/runner.py:361,435`、`model.py:99`、`cli.py:186`、`runner.py:534-540` |
| 端口预检漏 `AdditionalPorts`(7001/8080) 与 UDP 7002/7003；bind 探测 + `SO_REUSEADDR` 有假阴性（实测 5 种占用形态里 2 种返回 FREE） | `runner.py:96-103,401-403,86-87` |
| 判定域不含服务端：`judge` 的 `server_dir` 形参**从未被引用**；`[字节]` 与 `[存在]` 是同一段代码；`pattern` 缺省会回落成中文 description 当匹配串 | `verdict.py:99-140,128` |
| `protocol/` 无进程间 opcode 槽位 | 见 §5.5 |

### 7.3 判定纪律

- 每个场景声明**至少一条能失败的期望**，否则判定视为无效（防恒真判定，S0 已立此规矩，`docs/superpowers/specs/2026-10-07-local-multiclient-acceptance-s0-design.md:204-206`）。
- 修复 `verdict.py` 之前，Tier 2 的"绿"不算证据。

---

## 8. 风险

| # | 风险 | 后果 | 处置 |
|---|---|---|---|
| **R1** | **新失败模式**：channel 活、game 死 | 客户端能握手但登录失败；今天整栈一起死，症状是"连不上" | `/api/status` 暴露 game 在线位；真机矩阵加一格；文档写死 |
| **R2** | 配置漂移：两进程读同一份 `server.json`，按角色解释字段 | 某进程绑错/漏绑 | 启动日志打印"我绑了哪些端点、忽略了哪些" |
| **R3** | 2311/8080 TCP 搬家（TCP 归 channel、UDP 2311 仍归 game） | 端口占用/漏绑 | 启动前预检；沿用坑 14 记录（`…/topology.md:129-142`） |
| **R4** | 验收平台自身有缺陷（判定不含服务端、`[字节]`=`[存在]`） | Tier 2 变成恒真判定 | 先修 `verdict`/`runner`，再谈门槛（§7.2） |
| **R5** | `protocol/` 契约变更让 `verify` 长期红 | S1 资产断掉 | 与报文表同一次提交；5/5 是硬门槛 |
| **R6** | `0x7b` 若将来定案为"分类常量"且官方 game↔channel 用别的值 | 改一个常量 | 单点常量（§5.1） |
| **R7** | 第一刀**隔离收益有限**（只保温登录门，不解决 party15） | 被读成"S2 没收益" | 本稿不吹收益；第二刀收益在 §4.4 排队 |
| **R8** | 半拆分运维复杂（谁先起、看谁的日志） | 排障变慢 | 任一方可单独启动；每进程一份日志；链路 up/down 双写 |
| **R9** | `EntranceService.cs` 17,727 行单类单方法，抽取本身会碰 15,316 行的方法体 | I1 变成"大手术"，回归面大 | I1 只搬"驱动逻辑"，不改玩法分支；门槛是字节级不变（§6 I1） |

---

## 9. 进入实现前还缺什么（诚实清单）

1. **官方进程间链路的建链/登录报文从没抓过**。D2 允许现在自选语义，但将来对齐时 `REGISTER`/`ACK` 可能返工。Docker 恢复后按 `…/RESUME.md:60-78` 重跑，目标：官服 `game → bridge/manager` 建链的头几帧。
2. 官方 `[6..9]`=`0b`/`0d` 与 `[10]`=`09`/`03` 的写入者未解（`…/dual-evidence.md:137-149`），所以"填 0 / 填 1"是**我们的选择**，不是官方事实。
3. `[0]` category 的两种读法未定（Q-CLASSBYTE）。
4. 官方每对进程的 opcode 全表（只有 channel 侧 3 条已解）。
5. **本地**：`tools/acceptance` 多进程改造 + `verdict` 服务端证据域 —— 属实现内容，但必须排在 Tier 2 之前。
6. 真机设施（`DF/town-test`）当前是否还能跑：**`[未验证]`**，本轮没验。
7. `GameProbe` 与 `Entrance.AdditionalPorts` 的 7001 既有潜在冲突：`[未验证]`，实现时确认（§4.2）。

---

## 10. 一页摘要：我替所有者做的决定 + 每个决定若错的代价

| 决定 | 若错了的代价 |
|---|---|
| 链路复用 `PacketFrame` 而非新帧格式 | **低**：换壳只是编解码层一处 |
| category 取 `0x7b` | **低**：单点常量 |
| 私有报文从 128 起；只有 `NOTICE_CHANNEL_SERVER`(4) 复用官方 ID | **低**：改号 + 重新生成 `protocol/` 表 |
| 第一刀不补 `/api/sessions` 的握手会话 | **中**：观测降级；将来要补就得加一条会话快照报文 |
| 保留"单体模式"开关（§5.6） | **低-中**：多一条启动路径；但它正是 Tier 2 的证据来源 |
| 落盘单写者不动（D4） | **高**（若将来必须分片写）：持久层与幂等提交要重做 |
| 链路口取 7004、只有 game→channel 一个方向拨号 | **低**：端口与拨号方向都是配置/代码一处 |
| `STATUS` 兼心跳、丢失不重传（只影响观测数据一格，不影响正确性） | **低**：重传/序号是可后加的字段 |

---

## 11. 术语与证据标记

- 六类证据标记沿用 `.recon/onboarding.md:60-71`：`[静态]` / `[抓包]` / `[真机]` / `[回放]` / `[推断]` / `[未验证]`；被推翻的结论加 `[作废]` 并指向推翻它的证据，**不删原文**。
- 本文的"S2"是子项目编号，**不是**"第二阶段"；它出自 S0 路线图（`docs/superpowers/specs/2026-10-07-local-multiclient-acceptance-s0-design.md:238`）。
- "第一刀" = 本刀 = 入口握手族拆分；"第二刀" = 尚未立项的 game/副本拆分。
- S0 的判定档 `[存在]/[字节]/[语义]` 与六类证据标记**不是同一套东西**，不要互用（`…s0-design.md:209-213`）。

---

## 12. 证据索引（本文引用的一手来源）

**官方侧（`.superpowers/sdd/2026-10-05-official-login-channel/`，正文中以 `…/` 缩写）**
- `SPEC-login-to-channel.md:451,465-473,475-498,495-498` —— 11 字节头、`[0]` 每链路值、同构陷阱
- `STATIC-FINDINGS.md:299,319,336-337`（`[作废]` 的 14/10 字节说法）、`339-371`（访问器）、`373-413`（死配置）、`306-331`（进程间帧样本与字段）、`323-330`（UPDATE_CHANNEL_INFO 字段偏移）、`442-444`（参考实现旁证）、`472-486`（角色划分：客户端登录只在 `df_game_r`；`df_channel_r` 不知道玩家）、`557-563`（无 checksum）
- `dual-evidence.md:64-66`（抓包方法与 206 包）、`78-122`（id=4/id=2/id=5 双证）、`137-149`（未解的 `[6..9]`/`[10]`）、`229`（官方注册表）、`251-264`（`[0]` 每链路取值）
- `topology.md:87-110`（端口表）、`129-142`（坑 14）、`157-163`（世代错位：官服 7001 ≡ 我们 2311）、`354`（manager 的服务器登录日志）、`161`（握手连接用完即弃）
- `open-questions.md:169-176`（Q-HDR 关闭：11 字节 + 不要改 `PacketFrame`）
- `RESUME.md:60-78`（重跑本轮实验的命令）

**本项目侧**
- `docs/superpowers/specs/2026-10-07-local-multiclient-acceptance-s0-design.md:204-213,238` —— 防恒真判定、判定档与证据标记的区别、S2 的原始定义
- `docs/superpowers/plans/2026-10-02-store-write-transaction-boundary.md:7,17` —— 唯一 store 锁与事务边界
- `docs/superpowers/specs/2026-10-02-dungeon-reward-idempotent-commit-design.md:17` —— 会话局部函数不能承担共享奖励提交
- `src/DFLegacy.Server/EntranceService.cs:203,220,221,228,15345,15375,15518,17612-17659` —— 边界与内容加载
- `src/DFLegacy.Server/Program.cs:329-334,355` —— `/api/status`、`/api/sessions`
- `src/DFLegacy.Protocol/EntranceProtocolEngine.cs:6-22,36-48` —— 每连接会话状态与 4 对 opcode
- `src/DFLegacy.Protocol/PacketFrame.cs:10-13,16,20-39` —— 10 B 头、`Body[0]`=记录数、`Create` 的默认值
- `src/DFLegacy.Protocol/GameDatagramProtocol.cs:44-45,123-138,183-192`、`LegacyDatagramProtocol.cs:14` —— 本仓库 UDP 的 7/9/11 字节头（§1.1 的否证依据）
- `src/DFLegacy.Protocol/EntranceMetadata.cs:27-57` —— 频道信息 4+24+48 的组/渠道记录
- `src/DFLegacy.Server/ServerOptions.cs:9`、`ProbeService.cs:18-23`、`server.json:20-46` —— 端口与 `GameProbe`
- `.recon/onboarding.md:60-71` —— 六类证据标记的产出方式
