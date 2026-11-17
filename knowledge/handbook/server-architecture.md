# 服务端架构与玩法定位

用途：接手服务端、定位玩法问题或改变进程部署时，先确定由哪个入口接包、谁持有状态、谁负责落盘。本文按 2026-11-17 工作区源码整理；列出测试入口不表示本次执行过测试。

按需阅读：启动失败看[启动与配置](#启动与配置)；端口或拆进程看[进程与网络边界](#进程与网络边界)；玩法需求看[功能定位表](#功能定位表)；背包回滚、重复奖励看[会话与状态所有权](#会话与状态所有权)。字节布局、存档字段与迁移看[协议与数据](protocol-and-data.md)。部署操作另看 [DEPLOYMENT_MANAGER.md](../../DEPLOYMENT_MANAGER.md)、[DEPLOYING.md](../../DEPLOYING.md)；HTTP 请求字段看 [ADMIN_API.md](../../ADMIN_API.md)。

## 先读这几个文件

| 文件 / 关键符号 | 解决的问题 |
| --- | --- |
| [Server/Program.cs](../../src/DFLegacy.Server/Program.cs) | 配置、单写者租约、依赖注入、目录初始化、HTTP 路由、后台服务注册；服务端真正入口 |
| [ServerOptions](../../src/DFLegacy.Server/ServerOptions.cs) | 代码默认配置；不能只看根 `server.json`，后者含旧字段 |
| [EntranceService.HandleClientAsync](../../src/DFLegacy.Server/EntranceService.cs) | 频道 TCP 会话、客户端命令分发、角色内存快照和玩法编排；文件很大，应按符号搜索 |
| [JsonGameStore](../../src/DFLegacy.Server/JsonGameStore.cs) 及同名前缀 partial 文件 | 游戏存档模型、持久化操作、迁移、奖励去重 |
| [GameProtocolEngine](../../src/DFLegacy.Protocol/GameProtocolEngine.cs) 及其 partial 文件 | 命令常量、读请求、构造回包；不负责数据库或进程监听 |
| [tests/DFLegacy.SmokeTests/Program.cs](../../tests/DFLegacy.SmokeTests/Program.cs) | 控制台冒烟测试路由及专项参数；不是 xUnit/VSTest 测试发现入口 |

## 进程与网络边界

### 哪些项目负责什么

| 项目 | 职责 | 明确边界 |
| --- | --- | --- |
| `DFLegacy.Server` | ASP.NET Core 宿主、账号/角色存档、频道玩法 TCP、角色 UDP、玩法 UDP、回退中继和后台/玩家 HTTP | 唯一游戏存档写入者；一个进程内可有多个频道，不等于多份 Server 共享存档 |
| [DFLegacy.Channel](../../src/DFLegacy.Channel/Program.cs) | 可选独立入口握手进程，读取频道脚本与入口配置，运行入口 TCP 和进程间链路服务端 | 不引用 Server、不持有 `JsonGameStore`、不写 `state.json`；名称里的 Channel 不代表玩法已搬到它 |
| [DFLegacy.InterProcess](../../src/DFLegacy.InterProcess/InterProcessMessageIds.cs) | Channel 与 Server 的注册、状态/心跳、频道通知编码与链路生命周期 | 当前不是背包 RPC、分布式事务或跨进程队伍服务 |
| [DFLegacy.Protocol](../../src/DFLegacy.Protocol/DFLegacy.Protocol.csproj) | 帧、密码变换、入口协议宿主、游戏协议编解码、共享协议数据结构 | 不能在构造数据包时修改角色存档 |
| [DFLegacy.Manager](../../src/DFLegacy.Manager/ManagerApplication.cs) | 部署管理网页、配置/PVF 上传、托管 Server 子进程、日志、客户端包导出和可选更新服务 | 有独立管理身份；不是游戏 GM 账号；当前仅自动托管单体模式 |

### 端口按角色分，不按名称猜

下列是根 [server.json](../../server.json) 与代码常见默认值。第二章、发行包和部署环境可覆盖，操作前以生效配置和监听日志为准。

| 常见端口 | 协议 / 接收方 | 作用与入口 |
| --- | --- | --- |
| 2311、8080 | TCP / `EntranceHandshakeService` 或独立 Channel | 入口密钥交换、缓存判定、频道脚本、频道表；共享实现为 [EntranceHandshakeServer](../../src/DFLegacy.Protocol/EntranceHandshakeServer.cs) |
| 7001 | TCP / `EntranceService` | 频道登录、选角、城镇、地下城与玩法命令；绑定地址来自 `Entrance.Host` |
| 2311 | UDP / [CharacterDatagramService](../../src/DFLegacy.Server/CharacterDatagramService.cs) | 旧客户端角色选择工作线程的独立 UDP 协议；和同号 TCP 不是一个监听器 |
| 7002、7003 | UDP / [GameplayDatagramService](../../src/DFLegacy.Server/GameplayDatagramService.cs) | NAT 探测、保活、角色/队伍上下文及玩法 UDP 转发 |
| 7005 | TCP + UDP / [FallbackRelayService](../../src/DFLegacy.Server/FallbackRelayService.cs) | ACT1 原生组队网络回退；不等于启动器的 IPv4/IPv6 代理 |
| 7004 | TCP / 可选进程间链路，或可选 PvP | 拆进程时是 `Processes.Channel.LinkPort` 默认值；单体 PvP 时是 PvP 默认入口，两种模式不可混开 |
| 7100～7105 | TCP / `EntranceService` | 开启 `Channel.RegionalChannelsEnabled` 后的附加地区频道；首频道仍走 `Channel.Port` |
| 7110～7112 | TCP / `EntranceService` | 开启 PvP 后的其它决斗频道，具体档位见 [PvpChannelCatalog](../../src/DFLegacy.Server/PvpChannelCatalog.cs) |
| 8081 | HTTP / Server | 游戏账号 API、状态与诊断、后台 API；权限集中在 [AdminAccessPolicy](../../src/DFLegacy.Server/AdminAccessPolicy.cs) |
| 18081、18082 | HTTP / Server | GM 与玩家面板独立监听；由 [WebPanelHosting](../../src/DFLegacy.Server/WebPanelHosting.cs) 限定可访问面 |
| 18080 | HTTP / Manager | 部署控制台；默认本机回环监听 |
| 18083 | HTTP / Manager | 配置 `DFLEGACY_UPDATE_PUBLIC_URL` 后启用的更新文件服务；不是游戏 API |

入口端口集合由 [EntrancePortFamilies.Split](../../src/DFLegacy.Protocol/EntrancePortFamilies.cs) 划分。默认 `{Entrance.Port} ∪ AdditionalPorts` 中，玩法频道端口留给 `EntranceService`，其余交给入口握手宿主。新增端口不要只向某一处数组追加：还要核对 PvP/地区频道验证、监听就绪、Manager 端口检查和发行转发配置。

地区频道端口目前决定入口回包和通关经验，但 [TownSessionDirectory](../../src/DFLegacy.Server/TownSessionDirectory.cs) 的键是 `(town, area, instance)`，不含频道号；不要仅凭端口不同推断城镇/附近聊天已按频道隔离。跨频道可见性、组队和商店作用域的现状见 [区域频道与假人](social-world-and-bots.md#33-频道作用域只完成了一部分)。

### 支持的两种进程拓扑

单体是默认：`Processes.Channel.Enabled=false`，Server 同时运行入口握手和玩法；`Game.EntranceBinding` 默认 `true`。

拆分需 `Processes.Channel.Enabled=true` **且** `Processes.Game.EntranceBinding=false`。[ProcessOptions.Validate](../../src/DFLegacy.Server/ProcessOptions.cs) 拒绝两个进程同时认领入口。Server 的入口握手服务让出端口，独立 Channel 接管；Server 的玩法、UDP 和存档仍在原进程。两个开关都为 `false` 时，当前实现仍由 Server 绑定入口，不能将其理解为关闭入口。

拆分模式下 [ChannelLinkService](../../src/DFLegacy.Server/ChannelLinkService.cs) 从 Server 拨到 `Entrance.Host:Processes.Channel.LinkPort`，发送 REGISTER 并交换 STATUS。Channel 用同一地址绑定链路。当前没有单独的远程链路地址配置；不能据“有两个进程”推断任意跨主机部署已完成。链路状态通过 `/api/status` 的 `ChannelLink` 投影，单体显示未连接是正常现象。

[PvpOptions.Validate / ValidateRegionalChannels](../../src/DFLegacy.Server/PvpOptions.cs) 明确要求 PvP、地区频道使用共享存档的单体部署。[ServerSupervisor.StartCoreAsync](../../src/DFLegacy.Manager/ServerSupervisor.cs) 也拒绝 Manager 托管拆分模式。修改这些限制必须补齐所有权与就绪机制，不能仅删验证。

## 启动与配置

### Server 启动顺序

1. [HostingArguments.Parse](../../src/DFLegacy.Server/HostingArguments.cs) 读取 `--config`；显式路径必须是绝对路径。省略时用 `AppContext.BaseDirectory/server.json`，配置缺失退出码为 2。
2. 创建 Web 宿主，添加配置文件，再添加环境变量。`DFLegacy__...` 的双下划线对应嵌套配置，环境变量覆盖文件。
3. 绑定为一个 `ServerOptions` 对象、验证拓扑，按 `DataPath` 获取 [StoreProcessLease](../../src/DFLegacy.Server/StoreProcessLease.cs) 的 OS 独占文件句柄。
4. `--initialize-admin` 走标准输入 JSON 初始化流程后退出；正常启动继续验证面板、PvP/地区频道与回退中继配置。
5. 注册单例目录、状态服务及托管后台服务。需要跨连接共享的服务通过同一单例同时注册为 hosted service，不能误建第二个实例。
6. 初始化物品等 PVF 目录，`JsonGameStore.InitializeAsync` 读盘/迁移，`ApplyBootstrapAdminsAsync` 提升启动名单中的已有账号，然后初始化其它目录与 PvP 等级规范化。
7. 配置 HTTP 中间件/端点并运行宿主；各后台服务实际完成端口监听后标记 [ListenerReadiness](../../src/DFLegacy.Server/ListenerReadiness.cs)。

虽然 `AddJsonFile` 使用 `reloadOnChange: true`，当前依赖注入的是启动时绑定的单例 `ServerOptions`，不是 `IOptionsMonitor`。修改文件不能当作玩法热更新；Manager 也明确提示重启应用配置。`ScriptFileSystem` 与多数 Catalog 有内存缓存，替换 PVF 同样需要停服与重启。

### 相对路径与健康检查

`JsonGameStore` 和 `StoreProcessLease` 的相对 `DataPath` 按 `AppContext.BaseDirectory` 解析；不要把仓库当前目录的 `data/` 自动当作运行存档。直接源码启动会把根 `server.json` 复制到输出目录，其中资源路径可能只是开发样例；先准备隔离配置与隔离存档。

例如已经准备好测试配置后，从仓库根运行：

```powershell
$configPath = (Resolve-Path -LiteralPath 'artifacts/lab/server.json').Path
dotnet run --project src/DFLegacy.Server -c Release -- --config $configPath
```

该命令不会替你创建配置、准备 PVF 或选择正确章节。跨机连接还要区分绑定地址与广播地址：`0.0.0.0` / `::` 用于监听，客户端收到的地址应由 `GameplayDatagram.AdvertisedHost` 和 [EntranceDialHost.Resolve](../../src/DFLegacy.Protocol/EntranceDialHost.cs) 解析为可连接地址。

`GET /health` 只证明 HTTP 宿主响应。Manager 用回环来源、每次启动生成的专用令牌调用 [ManagerControl](../../src/DFLegacy.Server/ManagerControl.cs) 的 `/internal/manager/status`，只有 `ready=true` 才转为 running。不要用普通游戏管理员 token 代替这个控制令牌，也不要将令牌写进交接文档。

### Manager 的独立生命周期

Manager 入口转到 `ManagerApplication.RunAsync`：解析 [ManagerHostOptions](../../src/DFLegacy.Manager/ManagerHostOptions.cs)，锁定管理数据目录，加载配置和独立身份，启动网页与 `ServerSupervisor`。`--root` 决定发行根目录，`--data-dir` 决定可写数据目录；默认管理端口 18080、回环监听。第二章本地管理模式禁止非回环监听。

Supervisor 负责校验 PVF/端口、创建自己拥有的 Server 子进程、等待专用就绪端点和采集标准输出。停止先请求 `/internal/manager/stop` 等待优雅退出；超时才终止自己托管的进程。PVF 上传要求 Server 已停，配置可保存但运行状态可能标记 `RestartRequired`。

Manager 路径划分见 [ManagerPaths](../../src/DFLegacy.Manager/ManagerPaths.cs)：`data/config/` 存部署配置、`data/game/state.json` 存游戏数据、`data/management/` 存管理身份与密钥、`data/logs/` / `backups/` / `exports/` 存运维产物。它与直接运行 Server 的默认 `data/state.json` 不是同一个位置。

## 会话与状态所有权

一次普通游戏 TCP 连接的主要路径是：

```text
EntranceService.AcceptLoopAsync
  -> HandleClientAsync（连接取消源、RuntimeState、密码状态、会话局部背包）
  -> 读取 PacketFrame / 解客户端载荷 / 判断章节
  -> ChannelLoginAuthentication（账号身份）
  -> SELECT_CHARACTER -> CharacterSessionRegistry.ClaimAsync
  -> 玩法命令 -> Catalog / Planner / 共享 Service
  -> JsonGameStore（需要持久化时）
  -> 当前会话安装新状态 -> GameProtocolEngine 构包
  -> SerializedWriteStream（接收方章节投影 + 有序写包）
```

具体分支很多，主循环还会处理跨会话信箱、商店/交易状态、疲劳、奖励等事件。不能把上图理解为所有命令都按统一事务框架执行。

| 状态范围 | 持有者 | 修改时要保留的约束 |
| --- | --- | --- |
| 单连接的账号、选中角色、各背包字典、密码流状态 | `EntranceService.HandleClientAsync` 的局部变量 | 存档已更新不代表会话快照已更新；旧快照稍后保存可覆盖新结果 |
| 同一角色当前连接、踢旧连接、外部变更通知 | [CharacterSessionRegistry](../../src/DFLegacy.Server/CharacterSessionRegistry.cs) | 用 lease/会话身份区分重连前后，不让旧通知作用于新连接 |
| 同镇同区成员、区域用户号、外观与移动广播 | [TownSessionDirectory](../../src/DFLegacy.Server/TownSessionDirectory.cs)、[AreaUserIdPool](../../src/DFLegacy.Server/AreaUserIdPool.cs) | 区域号不是角色永久 ID；赛丽亚房间固定私有；先外观后名单的顺序是协议要求 |
| 队伍、交易、商店、师徒、PvP | `PartyService`、`ItemTradeService`、`PrivateStoreService`、`MentorService`、`PvpService` 单例 | 会话退出、超时、切频道时要撤销参与关系；跨会话效果走既有信箱/回调 |
| 地下城与房间、成员、地面掉落、结算状态 | [DungeonInstanceRegistry](../../src/DFLegacy.Server/DungeonInstanceRegistry.cs)、[DungeonInstance](../../src/DFLegacy.Server/DungeonInstance.cs) | 单人实例表与队伍实例表分开；角色绑定仅是索引；最后成员离开销毁；实例本身不落盘 |
| 账号、角色、余额、物品、奖励回执、公会 | `JsonGameStore` | 用 store 操作提交并返回权威结果；不能由 Channel、Protocol 或另一进程直接写 JSON |

特别容易出错的两条路径：

- **跨人奖励/交易**：`DungeonInstanceRegistry` 的拾取信箱将结果交给得主会话处理；`JsonGameStore` 的 `_pendingTradeDeliveries` 阻止交易提交后旧背包盲写。新功能应复用这种“提交 + 安装 + 确认”约束，不要直接修改另一个会话的字典。
- **网络发送**：[SerializedWriteStream](../../src/DFLegacy.Server/SerializedWriteStream.cs) 序列化完整游戏包，条件写用于防止旧房间数据晚于新房间包发送。[CrossSessionDelivery](../../src/DFLegacy.Server/CrossSessionDelivery.cs) 隔离收件人的断线/取消异常；不能因某队友断线取消健康发送者的会话。

## 功能定位表

从此表选一行，再在 `EntranceService` 搜相应命令或类名。Catalog 负责资源解释，Planner/Policy 负责规则或计划，Service/Session 负责运行期编排，Store 负责持久化；现有代码并非每项都完全拆开。

| 要改的功能 | 首批阅读文件 / 符号 | 对应冒烟入口或文件 |
| --- | --- | --- |
| 登录、账号种类、新角色、重复登录 | [ChannelLoginAuthentication](../../src/DFLegacy.Server/ChannelLoginAuthentication.cs)、[AccountIdentity](../../src/DFLegacy.Server/AccountIdentity.cs)、`JsonGameStore.CreateAccountAsync/CreateCharacterAsync`、`CharacterSessionRegistry` | `--new-character-defaults`；[AccountKindSmokeTests](../../tests/DFLegacy.SmokeTests/AccountKindSmokeTests.cs) |
| 物品空间、背包/仓库、排序与快速存取 | [CharacterInventoryPlanner](../../src/DFLegacy.Server/CharacterInventoryPlanner.cs)、[CharacterInventorySorter](../../src/DFLegacy.Server/CharacterInventorySorter.cs)、[ChapterTwoQuickStorePlanner](../../src/DFLegacy.Server/ChapterTwoQuickStorePlanner.cs)、`JsonGameStore` | `--item-spaces`、`--c2-quick-store`、`--c2-warehouse-swap` |
| 装备强化、封装、分解、合成 | [EquipmentReinforcementPlanner](../../src/DFLegacy.Server/EquipmentReinforcementPlanner.cs)、[ResealCatalog](../../src/DFLegacy.Server/ResealCatalog.cs)、[DisjointItemPlanner](../../src/DFLegacy.Server/DisjointItemPlanner.cs)、[CompoundItemPlanner](../../src/DFLegacy.Server/CompoundItemPlanner.cs) 与同领域 Catalog | [EquipmentReinforcementSmokeTests](../../tests/DFLegacy.SmokeTests/EquipmentReinforcementSmokeTests.cs)、[CompoundItemSmokeTests](../../tests/DFLegacy.SmokeTests/CompoundItemSmokeTests.cs) |
| 技能、属性、经验、任务/解锁地下城 | [SkillCatalog](../../src/DFLegacy.Server/SkillCatalog.cs)、[CharacterStatCatalog](../../src/DFLegacy.Server/CharacterStatCatalog.cs)、[QuestCatalog](../../src/DFLegacy.Server/QuestCatalog.cs)、[QuestRewardPlanner](../../src/DFLegacy.Server/QuestRewardPlanner.cs)、`Quest*Requirement` | `--skill-slot-persistence`、`--quest-map`、`--chapter-two-quest-visibility` |
| 城镇同屏、聊天、好友/黑名单 | `TownSessionDirectory`、[FriendService](../../src/DFLegacy.Server/FriendService.cs)、`EntranceService` 对应命令 | [TownSyncSmokeTests](../../tests/DFLegacy.SmokeTests/TownSyncSmokeTests.cs)、[FriendSmokeTests](../../tests/DFLegacy.SmokeTests/FriendSmokeTests.cs)、`--party-chat` |
| 队伍入场、选图、换房 | [PartyService](../../src/DFLegacy.Server/PartyService.cs)、[DungeonPartyService](../../src/DFLegacy.Server/DungeonPartyService.cs)、`DungeonInstance.TryAdvanceRoom` | `--party`、`--dungeon-map-selection`、[DungeonDepartureSmokeTests](../../tests/DFLegacy.SmokeTests/DungeonDepartureSmokeTests.cs) |
| 怪物/物件掉落、拾取、金币均分、投骰 | [DropCatalog](../../src/DFLegacy.Server/DropCatalog.cs)、[DungeonActiveDropTransaction](../../src/DFLegacy.Server/DungeonActiveDropTransaction.cs)、[DungeonPickupPlanner](../../src/DFLegacy.Server/DungeonPickupPlanner.cs)、[LootRouting](../../src/DFLegacy.Server/LootRouting.cs)、`JsonGameStore.DungeonGold.cs` | `--active-drop-four`、`--pickup-removal`、`--party-gold`、`--loot-routing` |
| 通关经验、免费/付费翻牌、奖励去重 | [ClearRewardCatalog](../../src/DFLegacy.Server/ClearRewardCatalog.cs)、[PartyCardSettlementFlow](../../src/DFLegacy.Server/PartyCardSettlementFlow.cs)、[PartyCardOwnColumnCommitter](../../src/DFLegacy.Server/PartyCardOwnColumnCommitter.cs)、[DungeonRewardLedgerModels](../../src/DFLegacy.Server/DungeonRewardLedgerModels.cs) | `--party-card`、`--dungeon-reward-commit`、`--store-write-transaction` |
| 交易、摆摊、邮件 | [ItemTradeService](../../src/DFLegacy.Server/ItemTradeService.cs)、[ItemTradePlanner](../../src/DFLegacy.Server/ItemTradePlanner.cs)、[PrivateStoreService](../../src/DFLegacy.Server/PrivateStoreService.cs)、`JsonGameStore` 邮件/商店 partial | `--trade`、`--private-store`；[AdminMailAttachmentSmokeTests](../../tests/DFLegacy.SmokeTests/AdminMailAttachmentSmokeTests.cs) |
| 商城、礼包、时装/宠物期限、赠送 | [CeraShopCatalog](../../src/DFLegacy.Server/CeraShopCatalog.cs)、[CeraShopContentPlanner](../../src/DFLegacy.Server/CeraShopContentPlanner.cs)、[AvatarLifetimePolicy](../../src/DFLegacy.Server/AvatarLifetimePolicy.cs)、[GiftBoosterOpeningSession](../../src/DFLegacy.Server/GiftBoosterOpeningSession.cs) | `--cera-gift`、`--avatar-lifetime`、`--chapter-two-booster` |
| 师徒成长、师父金币 | [MentorService](../../src/DFLegacy.Server/MentorService.cs)、[MentorGrowthPolicy](../../src/DFLegacy.Server/MentorGrowthPolicy.cs)、[MentorGoldPolicy](../../src/DFLegacy.Server/MentorGoldPolicy.cs)、`JsonGameStore.Mentor*.cs` | `--mentor`、`--mentor-growth`、`--mentor-gold` |
| 决斗场、段位、组队/擂台结算 | [PvpService](../../src/DFLegacy.Server/PvpService.cs)、[PvpService.Matches](../../src/DFLegacy.Server/PvpService.Matches.cs)、[PvpRatingPolicy](../../src/DFLegacy.Server/PvpRatingPolicy.cs)、`JsonGameStore.Pvp.cs` | `--pvp-multichannel`、`--pvp-protocol-rating`、`--pvp-team-relay-only` |
| 第二章公会、加入/退出、贡献 | [GuildSession](../../src/DFLegacy.Server/GuildSession.cs)、[GuildProgression](../../src/DFLegacy.Server/GuildProgression.cs)、`GuildSession.*`、`JsonGameStore.Guild*`、[ClientGuildEndpoints](../../src/DFLegacy.Server/ClientGuildEndpoints.cs) | `--chapter-two-guild`、`--chapter-two-guild-invitation`、`--chapter-two-guild-membership`、`--chapter-two-guild-progression`、`--chapter-two-guild-web` |
| 第二章塔、墓地、附魔、随机盒 | [ChapterTwoTowerPartyRun](../../src/DFLegacy.Server/ChapterTwoTowerPartyRun.cs)、[ChapterTwoTowerCatalog](../../src/DFLegacy.Server/ChapterTwoTowerCatalog.cs)、[ChapterTwoCemeteryGateState](../../src/DFLegacy.Server/ChapterTwoCemeteryGateState.cs)、[ChapterTwoEnchanting](../../src/DFLegacy.Server/ChapterTwoEnchanting.cs)、[RandomBoxOpeningSession](../../src/DFLegacy.Server/RandomBoxOpeningSession.cs) | `--chapter-two-tower`、`--chapter-two-cemetery`、`--chapter-two-random-box`；所需 PVF 参数见测试 Program |
| UDP/回退连接与重连 | `GameplayDatagramService`、`FallbackRelayService`、[GameplayDatagramService.FallbackRelay](../../src/DFLegacy.Server/GameplayDatagramService.FallbackRelay.cs) | `--gameplay-datagram-lifecycle`、`--party-network`、`--fallback-relay`、`--fallback-protocol` |
| HTTP 权限、GM 奖励、诊断上传 | `AdminAccessPolicy`、[AdminAuthService](../../src/DFLegacy.Server/AdminAuthService.cs)、[AdminRewardEndpoints](../../src/DFLegacy.Server/AdminRewardEndpoints.cs)、[ClientDiagnosticEndpoints](../../src/DFLegacy.Server/ClientDiagnosticEndpoints.cs) | `--admin-rewards`、`--client-diagnostics`；[AdminAuthSmokeTests](../../tests/DFLegacy.SmokeTests/AdminAuthSmokeTests.cs) |

## 新增或修复玩法的最短闭环

1. 写清章节、触发命令/用户动作、预期状态变化和失败行为。搜常量名与现有专项测试，先找到当前调用路径；不要只在大分发器末尾追加补丁。
2. 从 PVF / 客户端读取逻辑 / 已有抓包确定规则。把“推断”与“已验证字节”分开记录；其它世代官服代码只能作对照。
3. 把可计算规则放进相应 Planner/Policy；运行期协调留在 Service/Session；需要落盘的多对象变化由 Store 原子提交。接收方章节差异在协议/投影层处理。
4. 检查重放、容量不足、金币不足、断线、切频道、两人同时操作。修改共享状态时尤其检查锁顺序、当前 lease、房间 generation 和提交后的会话安装。
5. 给实际缺陷或新规则增加可失败的测试，运行对应专项。涉及共用协议、存档或主循环时，再运行默认冒烟；无需因为只改文档就重跑游戏验收。
6. 真机确认用户可见行为。多人功能至少记录参与者数量、章节、连接拓扑、成功与中断分支；使用全新日志，记录源码提交、产物版本、配置与资源指纹。

常用测试命令（从仓库根；本页仅说明入口）：

```powershell
dotnet run --project tests/DFLegacy.SmokeTests -c Release -- --trade
dotnet run --project tests/DFLegacy.SmokeTests -c Release -- --party-card
dotnet run --project tests/DFLegacy.SmokeTests -c Release -- --s2-ipc
dotnet run --project tests/DFLegacy.SmokeTests -c Release -- --deployment-hosting
dotnet run --project tests/DFLegacy.SmokeTests -c Release
```

测试参数由 `Program.cs` 顺序匹配并提前返回，不能随意把多个专项 flag 拼成“全跑”。没有对应参数的测试类可能只在默认套件调用。PVF 专项应显式传入匹配章节的资源并检查输出是否实际执行了资源相关断言。`dotnet test` 成功不证明这个控制台程序运行过。

## 已知限制与文档冲突

- **没有数据库或多写者扩展**：JSON 全量快照 + 进程租约是当前边界。拆入口进程不会解除这一限制。
- **`EntranceService` 仍是大体量编排器**：虽已有很多 Planner/Service，背包快照与不少功能分支仍在其局部函数和主循环中；不能按项目名推断完整分层已经完成。
- **第二章共用服务端代码但有显式适配**：`ChapterTwoRemote`、本地 trial 开关、接收方投影等条件决定行为。第二章测试通过不自动证明第一章兼容；反之亦然。
- **根配置旧门口等待字段已失效**：`server.json` 的 `Dungeon.DoorGraceSeconds` 描述仍在；`ServerOptions.cs` 的 `DungeonOptions` 已空，注释说明该机制被删除。换房看 `DungeonInstance.TryAdvanceRoom`，不要恢复等待机制来迎合旧注释。
- **管理员注册注释冲突**：`AdminOptions.Users` XML 注释仍写“启动时与注册时提升”，但 `JsonGameStore.CreateAccountAsync` 明确新账号一律 Player，只有启动 `ApplyBootstrapAdminsAsync` 或授权 API 提升；以实现为准。
- **旧总览有时间范围**：[README.md](../../README.md) 的“完整公会尚未实现”等描述不能解释为公会代码全空；目前存在第二章公会、成员、进度与网页专项。也不能因此宣称所有公会玩法完成。按上表逐项确认。
- **网络与真机结论不能外推**：历史双客户端、四人 fixture、本机 socket 测试分别覆盖不同层；跨机长时间运行、未覆盖的 PvP 模式、画面同步问题需要新的证据。[PARTY_NETWORK_DIAGNOSTICS.md](../../PARTY_NETWORK_DIAGNOSTICS.md) 保留具体历史诊断范围。
- **独立 Channel 的旧文件头不是现状清单**：`Program.cs` 开头仍有“后续 Task 6/7 再接链路”式历史说明，实际文件下半部分已经启动 `InterProcessLinkServer`。先看调用代码再引用注释。

本文交付时做的是源码与文档静态核对，没有启动服务、连接客户端、修改真实存档或完成新一轮玩法验收。
