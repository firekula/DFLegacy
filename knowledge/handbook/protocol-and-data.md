# 协议、资源与存档契约

用途：修改报文、解析 PVF、增加存档字段或处理重复奖励前，先核对兼容边界。本文按 2026-11-17 工作区源码整理，不是完整逆向规格，也不代表一次新的真机验收。

按需阅读：收包错位看[先区分五种线路](#先区分五种线路)；第一章/第二章差异看[游戏 TCP 与章节适配](#游戏-tcp-与章节适配)（完整移植检查见[章别适配](chapter-adaptation.md)）；物品显示/背包问题看[物品有存档语义和线缆语义](#物品有存档语义和线缆语义)；增加字段看[存档与事务](#存档与事务)；更新证据看[协议证据账](#协议证据账)。进程、玩法责任人与测试定位见[服务端架构](server-architecture.md)。

## 先区分五种线路

同一个数字 opcode 只有在**方向、协议族、包类型、客户端章节**都明确后才有意义。不要将入口握手、游戏通知或原生回退包送进同一个通用解码器。

| 线路 | 规范实现 | 头部与关键边界 |
| --- | --- | --- |
| 入口握手 TCP；客户端到游戏频道的基础帧壳 | [PacketFrame](../../src/DFLegacy.Protocol/PacketFrame.cs) | `type:u8 + id:u8 + totalLength:u32 LE + crc32:u32 LE`，共 10 字节；之后是 `Body`。入口 `Create` 会先写一个记录计数字节；游戏命令正文则含序列等自己的布局 |
| 游戏 Server → Client TCP | [GameServerPacket](../../src/DFLegacy.Protocol/GameServerPacket.cs) | 6 字节 `type/id/totalLength`，没有 `PacketFrame` 的 CRC 字段；完整 payload 经服务端字节变换后发送 |
| 角色选择 UDP | [LegacyDatagramProtocol](../../src/DFLegacy.Protocol/LegacyDatagramProtocol.cs) | 11 字节 header 和 body 分别作为两个 datagram；不能拼成 TCP 式数据流，也不能丢失 datagram 边界 |
| 玩法 UDP / MT 应用数据 | [GameDatagramProtocol](../../src/DFLegacy.Protocol/GameDatagramProtocol.cs) | NAT 探测、保活、MT 帧与应用子协议分别识别；MT 头 9 字节，应用头 7 字节，CRC 覆盖应用明文 |
| 原生组队回退 TCP/UDP | [NativeFallbackRelayProtocol](../../src/DFLegacy.Protocol/NativeFallbackRelayProtocol.cs) | 12 字节头：`opcode:u16 + length:u16 + senderAccountUid:u32 + recipientAccountUid:u32`，LE；总长上限 8192，独立于游戏 TCP |

另有内部 S2S 链路：使用 `PacketFrame` 壳，但固定 category `0x7b`、记录数 1、无 CRC，由 [InterProcessLink](../../src/DFLegacy.InterProcess/InterProcessLink.cs) 封装，不属于玩家游戏指令。

### “10 字节头”与“11 字节头”不是同一层口径

当前 C# `PacketFrame.HeaderLength=10`，`Body[0]` 在入口握手中通常为记录数。部分原版逆向资料把相同偏移 10 归入 header，称 `ackOk`，因此记成 11 字节头。对入口与 S2S 的现有报文，这两种分段可得到相同线上字节。不要为了统一文案将 `HeaderLength` 改为 11，或给包再插入一个字节。

`PacketFrame.RecordCount` 只是取 `Body[0]` 的便利属性，不能证明所有游戏命令正文第一个字节都是记录数。解析游戏命令时以 `GameProtocolEngine.TryRead*` 的序列与字段偏移为准。游戏服务端 6 字节包头更不能套用上述 10/11 字节说明。

`PacketFrame.ReadAsync` 先读完整头，再检查总长范围并读 body；默认上限 4 MiB，可被 `MaximumPacketLength` 覆盖。CRC 为 0 时 `HasValidCrc32` 视为有效；因此“CRC 检查通过”不能单独证明密码、身份或语义有效。

## 入口握手

[EntranceProtocolEngine.Handle](../../src/DFLegacy.Protocol/EntranceProtocolEngine.cs) 当前只分发以下请求；未知请求返回 null。它由单体 Server 和独立 Channel 共用的 [EntranceHandshakeServer](../../src/DFLegacy.Protocol/EntranceHandshakeServer.cs) 驱动。

| 请求 → 回应 | 当前实现语义 | 重要约束 |
| --- | --- | --- |
| 11 → 12 | 创建入口会话密钥材料并下发 | `EntranceSessionState` 每次生成 32 字节 KeyMaterial；不会据此完成游戏账号认证 |
| 5 → 6 | 频道脚本缓存选择 | 有脚本时 flag=0 请求下载；没有时 flag=1 跳过下载；会话标识使用兼容客户端 C 字符串的形式 |
| 9 → 10 | 下发频道脚本 | 内容来自 `etc/channel_info.etc`，不是将整个 `Script.pvf` 直接发出 |
| 1 → 3 | 下发频道元数据 | 地址来自运行配置与 `EntranceDialHost`；布局由 [EntranceMetadata](../../src/DFLegacy.Protocol/EntranceMetadata.cs) 构造 |

[EntranceCrypto](../../src/DFLegacy.Protocol/EntranceCrypto.cs) 使用 KeyMaterial 前 16 字节进行 AES-128 ECB、无 PKCS#7；明文补零到 16 字节倍数，至少一块。`EncryptCompressed` 的顺序是**补零并加密，再 zlib 压缩密文**。只对有压缩的消息使用这一层，不能把 cache reply 等所有报文统一处理成压缩流。

[EntranceContentFactory](../../src/DFLegacy.Protocol/EntranceContentFactory.cs) 组合脚本与元数据。[EntranceHandshakeService](../../src/DFLegacy.Server/EntranceHandshakeService.cs) 在缺 `etc/channel_info.etc` 时告警并使客户端跳过下载，并不会证明客户端缓存和服务端资源已经匹配。排查“能握手但无法进城”时继续检查频道 TCP、章节、账号与选角流程。

## 游戏 TCP 与章节适配

### 第一章当前路径

客户端命令经 `PacketFrame` 读入后，由 [GameClientCipherState.TryDecode](../../src/DFLegacy.Protocol/GameClientPayloadCipher.cs) 处理滚动字节变换。正文前两个字节保留，后续载荷解密；初始候选为 15 位种子集合，以“序列 + 明文正文”的 CRC 收敛候选。`CHECK_CONNECTION` 有直发明文特例，不推进普通随机状态。解码失败不是未知 opcode：应先查 `RX-ENC`、CRC 和候选数日志。

[GameProtocolEngine](../../src/DFLegacy.Protocol/GameProtocolEngine.cs) 是 partial 静态类：主文件包含大量常量、模型、读取器与构包器；`Login`、`Pvp`、`PrivateStore`、`Mentor`、`Guild*`、`CeraGift`、`RandomBox`、`ChapterTwo*` 等文件承载各领域扩展。改某类报文时先搜已有常量和 `TryRead/Create` 方法，不要另外建立重复 opcode 表。

[GameServerPacket.Encode](../../src/DFLegacy.Protocol/GameServerPacket.cs) 构造 6 字节头，对 payload 使用 `GamePayloadCipher.EncryptServerPayload` 的固定字节变换。这个变换与入口 AES、客户端滚动种子完全不同。单元级验证可以编码/解码比较，但同一错误算法自己往返成功不等于兼容真实客户端；仍应比对已知字节样例。

客户端文本通常按 GBK / CP936 字节编码；Server 的 HTTP JSON 是另一层。定长字符串字段要按编码后的字节截断/补齐，不要用 `.Length` 代替 wire 字节长度。具体入口有严格和替换两种编码回退配置，应沿用原调用方规则。

### 第二章不是全局替换包格式

第二章频道解码在 [EntranceService.TryDecodeChapter2Command](../../src/DFLegacy.Server/EntranceService.cs) 中。它要求命令类型、非零 CRC、两个序列字节之后 16 字节对齐的 AES 正文，正文长度限制为 16～1024；先通过 `CHECK_CONNECTION`，后续才允许常规第二章命令解码。常量只应引用源码，不要把协议密钥或本地凭据抄进文档。

启用条件也在 `EntranceService.HandleClientAsync`：`ChapterTwoRemote` 可开启第二章要求；本地 trial 还有特定端口、回环端点和 `DFL_S1C2_TRIAL_CHANNEL_CIPHER` 检查。远程第二章模式未通过相应连接检查会断开。遇到第二章命令未进入分发，先核对这个门槛，不要只改某个 `TryRead*`。

发送时 [SerializedWriteStream](../../src/DFLegacy.Server/SerializedWriteStream.cs) 调用接收方投影；第二章差异集中在 [ChapterTwoPacketProjection.Apply](../../src/DFLegacy.Server/ChapterTwoPacketProjection.cs) 及专用构包器。示例包括物品列表条目扩展、附魔字段、克隆时装外观和金币拾取分支。`GameServerPacket` 的 `ChapterTwoInventoryEnchantments`、`ChapterTwoAvatarAppearances` 是投影所需旁路数据，不会自动写入第一章原始 payload。

修改共享报文的验收必须同时核对第一章原始长度/字段和第二章接收长度/字段，避免给所有人都附加第二章字段。部分第二章读取器允许 AES 填充尾部，应使用现有 `HasCommandBodyLength` / `allowChapterTwoPadding` 规则，不要宽泛放开任意尾随字节。

## UDP、回退与 S2S

### 玩法 UDP 的分类先于转发

`GameDatagramProtocol.TryCreateNatProbeResponse` 只接受**恰好 1 字节**、类型 1 或 5 的探测。多字节 MT 包也可能以这些值开头，按首字节猜探测曾造成回声风暴。`IsNatKeepAlive` 的 10 字节形状与某些 ACK 有重叠，调用方还必须限制为第二反射器，不能在所有玩法端口丢掉它。

`GameplayDatagramService` 将 TCP 频道会话、账号和角色上下文关联到 UDP 端点；重连、离队或换房后不能继续让旧上下文转发。[GameplayDatagramLifecycleSmokeTests](../../tests/DFLegacy.SmokeTests/GameplayDatagramLifecycleSmokeTests.cs) 与 [PartyNetworkFaultSmokeTests](../../tests/DFLegacy.SmokeTests/PartyNetworkFaultSmokeTests.cs) 针对此类边界。

原生回退协议类只解析/转换帧，身份认证、收发方向、当前队伍/PvP 范围限制在 [FallbackRelayService](../../src/DFLegacy.Server/FallbackRelayService.cs) 和 `GameplayDatagramService.FallbackRelay.cs`。`NativeFallbackRelayProtocol.CreateDelivery` 将允许的入站类型改成投递类型；不能因头里自报 `AccountUid` 就信任发送身份。

### 进程间链路只是有限协议

[InterProcessMessageIds](../../src/DFLegacy.InterProcess/InterProcessMessageIds.cs) 当前 `LinkProtocolVersion=2`。字段全布局在 [InterProcessPayloads](../../src/DFLegacy.InterProcess/InterProcessPayloads.cs)，以下长度均为 payload，不含 PacketFrame 壳和记录字节。

| 消息 | ID | 字节数 | 当前用途 |
| --- | --- | --- | --- |
| `NoticeChannelServer` | 4 | 24 | 复用有历史证据的频道通知布局：服务器组、定长主机、端口 |
| `Register` | 128 | 38 | Game → Channel 注册版本、频道身份、容量、广告地址与游戏端口 |
| `RegisterAck` | 129 | 30 | 回应版本、接受状态、频道名和端口 |
| `Status` | 130 | 32 | 运行时间、活动会话、登录数、累计连接/收包数，并作心跳 |

除通知 4 外其余使用私有号段，不能称为“完整复刻官方 S2S”。主机字段固定 16 字节 ASCII 且需留终止零，不能直接容纳一般 IPv6 文本；改成 IPv6 或任意长主机名需要升级协议与双方实现，不能静默截断。状态里的 `TotalPackets` 是收包数，当前 Server 和 Channel 的 `LoggedInUsers` 都暂报 0。

[InterProcessLinkClient](../../src/DFLegacy.InterProcess/InterProcessLinkClient.cs) 与 [InterProcessLinkServer](../../src/DFLegacy.InterProcess/InterProcessLinkServer.cs) 处理握手、超时与重连。Server 默认每秒发送状态、连续缺失阈值为 3；这不意味着链路断开时已有游戏状态会被迁移到另一个进程。

## 资源读取与 Catalog

[ScriptFileSystem](../../src/DFLegacy.Server/ScriptFileSystem.cs) 提供只读虚拟文件接口，顺序如下：

1. `ScriptPvfPath` 存在则加载 PVF、建立条目索引并缓存读取内容。
2. PVF 不存在时，才检查 `SkillScriptPath` 指定的解包目录，作为开发/测试后备。
3. 两者都不可用时告警；某些目录可降级启动，但不等于全部玩法资源就绪。

它不是“PVF 上叠加目录”的覆盖系统。PVF 存在但损坏时会抛出异常，不会静默改用目录；相对资源路径按 `AppContext.BaseDirectory` 解析。虚拟路径按大小写不敏感方式索引；目录模式发现仅大小写不同的重复虚拟路径会报错，路径解析也有越界保护。跨 Windows/Linux 测试应保留这些规则，不能依赖本机文件系统碰巧不区分大小写。

Channel 使用 [ChannelScriptSource](../../src/DFLegacy.Channel/ChannelScriptSource.cs) 和 Protocol 的 [PvfArchiveReader](../../src/DFLegacy.Protocol/PvfArchiveReader.cs)，避免依赖整个 Server。两条资源读取路径的来源选择和频道脚本结果应保持一致。

当前有一个实现差异：Server 的解包目录用大小写不敏感索引，独立 Channel 则直接访问小写 `etc/channel_info.etc` 路径。在大小写敏感文件系统上，混合大小写的解包资源可能只在单体路径正常；尚不能把两者的目录模式兼容性视为完全相同。PVF 路径不受这个目录文件名差异影响。

| 资源领域 | 首选目录类 | 更改时核对 |
| --- | --- | --- |
| 物品属性、类别、重量、使用类型 | [ItemCatalog](../../src/DFLegacy.Server/ItemCatalog.cs) | ItemId 能否在目标章节 PVF 解析，分类是否影响背包、交易与奖励 |
| 任务与前置/奖励 | [QuestCatalog](../../src/DFLegacy.Server/QuestCatalog.cs)、`Quest*Requirement/Planner` | 任务可见性、接受、完成、奖励与持久化历史分别验证 |
| 副本、地图、怪物/物件掉落 | [DungeonCatalog](../../src/DFLegacy.Server/DungeonCatalog.cs)、[WorldMapCatalog](../../src/DFLegacy.Server/WorldMapCatalog.cs)、[DropCatalog](../../src/DFLegacy.Server/DropCatalog.cs) | 列表索引和实体 ID 不是同一个值；服务端和客户端选图必须一致 |
| 商城/礼包与高级服务 | [CeraShopCatalog](../../src/DFLegacy.Server/CeraShopCatalog.cs)、[PremiumBenefitCatalog](../../src/DFLegacy.Server/PremiumBenefitCatalog.cs) | 列表展示、购买扣费、发放、期限、赠送规则与章节限制 |
| 经验、属性、技能与段位 | `CharacterExperienceCatalog`、`CharacterStatCatalog`、`SkillCatalog`、`PvpExperienceCatalog` | 阈值、等级投影和历史存档规范化；不要只修客户端显示 |

Catalog 的“存在一条资源”不能作为玩法已实现或真实客户端可用的证据。新增资源后需要验证调用方如何使用它，不能只改 PVF 然后跳过服务端类型和协议约束。

## 存档与事务

### 三类数据不要混放

| 数据 | 权威位置 / 生命周期 | 处理方式 |
| --- | --- | --- |
| 游戏静态资源 | 运行配置指定的 `Script.pvf` 或测试目录 | 与客户端章节和版本匹配；只读；记录指纹，不入角色存档 |
| 游戏长期状态 | `JsonGameStore` 的单个 JSON 快照 | 由一个 Server 写；停服备份后操作；禁止运行中手改 |
| 当前连接/实例/索引 | `RuntimeState`、会话字典、队伍/副本注册表等内存对象 | 重启会丢失；需要恢复的业务结果应单独落盘，而不是序列化 socket/回调 |

Manager 自己还存部署配置、身份、Data Protection 密钥、日志和导出物，其身份不等于游戏账号。状态文件含 `PasswordHash`、找回码等敏感字段；排障样本使用合成或脱敏数据，不复制真实账号、凭据或密钥到仓库。

### 根结构与身份

[JsonGameStoreState](../../src/DFLegacy.Server/JsonGameStore.cs) 当前含 `Accounts`、账号/角色编号分配游标、疲劳日、地下城奖励/金币账、PvP 结算回执、GM 奖励回执、第二章塔排行、公会和公会编号游标。较旧的“根节点直接为账号数组”仍可读取，并在初始化阶段迁移成当前对象结构。

| 身份字段 | 用途 | 不应混同 |
| --- | --- | --- |
| `AccountRecord.Id` / `CharacterRecord.Id` | 服务端存储 GUID | 不能直接放入要求 u16/u32 的客户端字段 |
| `AccountUid` | 游戏账号数字身份；用于客户端及回退路由 | 不等于区域用户号，也不表示管理员权限 |
| `CharacterNo` | 持久角色数字编号 | 不等于角色在账号里的 Slot |
| `CharacterRecord.Slot` | 账号角色列表槽位 | 删除/重排后可能变化 |
| `AreaUserId` | 在线区域/会话使用的 u16 用户号 | 从池中分配回收，不是永久角色主键 |
| `AccountRole` | HTTP 玩家/管理员权限 | 与测试/普通 UID 段的 `AccountKind` 正交 |

具体编号范围由 [AccountUidRules](../../src/DFLegacy.Server/AccountIdentity.cs) 定义：0 无效，1～10000000 测试，10000001～17999999 保留，18000000 起普通。保留号能被识别不代表新账号分配器会分配它。公共查询应使用 `PublicAccountRecord` 等投影，而非直接序列化内部账号记录。

### 初始化会修改数据，不能当作只读检查

`InitializeAsync` 会执行身份、高级服务、虚弱、测试预设、账号现金、经验、师徒、仓库扩容物品、物品实例 ID 等迁移，并做时装寿命、疲劳日与商店恢复处理。新环境是否创建内建测试账号由 `SeedTestAccount` 决定。加载副本验证时应把 `DataPath` 指向备份副本，不能用真实存档“试启动”。

新增持久字段时：选择向后兼容的缺省值；明确 JSON 名称；补旧快照读取与迁移测试；验证保存/重启后值保持；确认所有写快照入口都携带该字段。这里除了普通 `SaveLockedAsync`，奖励和其它事务还可能构造候选快照，漏一个会让下一笔提交丢字段。

### 单写者、提交和不确定结果

[StoreProcessLease](../../src/DFLegacy.Server/StoreProcessLease.cs) 在 `DataPath + .lock` 持有 OS 独占句柄，防止另一写入进程进入。锁文件保留是设计行为，不能因为文件存在就删除它；“是否锁住”取决于句柄，不是文件外观。

`JsonGameStore` 内用 `_gate` 串行化持久状态操作。普通 `SaveLockedAsync` 序列化到同目录 `.tmp`，再经 [AtomicSnapshotReplacement](../../src/DFLegacy.Server/AtomicSnapshotReplacement.cs) 替换目标。它针对 Windows 指定拒绝访问错误作最多 5 次、间隔 25 ms 的有限重试；这不是任意 IO 失败都可忽略，也不是数据库事务隔离。

奖励等路径使用候选状态、预期状态比较与回执。[DungeonRewardCommitRequest](../../src/DFLegacy.Server/DungeonRewardLedgerModels.cs) 包含幂等键、payload 指纹、角色、地下城 generation、队伍槽、卡类型/列，以及预期和计划背包/金币。`Committed`、`Rejected`、`Unknown` 是不同结果，不能把“没有拿到成功返回”一律当作“没写进去，可以再发奖”。

发现不确定奖励写入后，`_rewardQuarantined` 会阻止后续普通保存；启动时看到 `Unknown` 也恢复隔离。有关诊断/恢复实现从 [AdminRewardEndpoints](../../src/DFLegacy.Server/AdminRewardEndpoints.cs) 与 `JsonGameStore` 对应方法继续追，不要删除回执或直接把状态改成 Committed。写文件失败、取消与提交成功后日志失败的边界由 [DungeonRewardUnknownSmokeTests](../../tests/DFLegacy.SmokeTests/DungeonRewardUnknownSmokeTests.cs) 等测试约束。

上述措施并不自动使所有新功能原子化。新增涉及两个角色、金币和物品的操作时，要先确定提交点、失败前状态、提交后会话安装和重试规则，再选择现有事务方法扩展。

## 物品有存档语义和线缆语义

[CharacterItemRecord](../../src/DFLegacy.Server/JsonGameStore.cs) 保存实例状态；[CharacterItemWireProjection.Project](../../src/DFLegacy.Server/CharacterItemWireProjection.cs) 负责客户端所需的 `AddInfo/ItemAttr/Durability/SealState`。不能把“客户端某一字段显示错”直接修成覆盖存档同名数字。

| 字段/概念 | 当前语义与常见陷阱 |
| --- | --- |
| `ItemId` 与 `Slot` | 模板 ID 与所在空间的格子不同；相同 Slot 在主背包、仓库、时装、宠物空间不是同一格 |
| `CountOrValue` | 类型相关的本地数量/值；不保证可直接用作所有物品的线缆 add_info |
| `EquipmentQualitySeed` | 装备品质投影使用的 32 位值，与本地实例数量分开；称号有固定品质处理 |
| `State` | 普通装备低 5 位强化、高 3 位已封装次数；移动装备不能清零 |
| `SealState` | 封装状态；不同于强化/封装次数，也不同于物品模板中的可交易规则 |
| `InstanceId` | 非堆叠装备等的服务端实例 GUID；[CharacterItemIdentity](../../src/DFLegacy.Server/CharacterItemIdentity.cs) 创建/保留它，客户端物品包没有这个 GUID 字段 |
| `AvatarRemainingSeconds` | JSON 为兼容旧存档仍使用名称 `AvatarExpireAt`，语义是剩余秒，0 表无限；不是 Unix 时间戳 |
| `AvatarExpiresAtUnixSeconds` | 服务端绝对到期时间；实际发包按当前时间计算剩余秒；移动/邮寄不能重置寿命 |
| `AvatarAbilityIndex` | 投影时可能占用客户端的 durability 位置，不能按普通装备耐久解释 |
| `EnchantmentCardId` / `AvatarAppearanceItemId` | 第二章附魔/克隆时装数据；从持久状态传至接收方章节投影，不能写坏第一章条目长度 |

主背包范围由 [CharacterInventoryLayout](../../src/DFLegacy.Server/CharacterInventoryPlanner.cs) 定义，区间为左闭右开：快捷栏 `[3,9)`、装备 `[9,41)`、消耗品 `[41,73)`、材料 `[73,105)`、任务 `[105,137)`；0/1/2 是金币、复活币、勋章特殊槽。宠物、时装、仓库有自己的布局/容量规则，读 [CharacterCreatureInventoryPlanner](../../src/DFLegacy.Server/CharacterCreatureInventoryPlanner.cs)、[CharacterAvatarInventoryPlanner](../../src/DFLegacy.Server/CharacterAvatarInventoryPlanner.cs)、[CharacterWarehouseProgression](../../src/DFLegacy.Server/CharacterWarehouseProgression.cs)。

背包插入还需要重量、堆叠、空间类型与溢出处理。优先复用 Planner，不要用“找一个空 Slot 然后 Add”的捷径替代。奖励、邮寄、交易和商店都可能移动同一物品，验收必须覆盖这些边界的实例标识、品质、期限和附加属性保持。

## 改协议或数据时如何验证

以下命令是待执行入口；本次文档整理没有运行它们。

| 变更 | 最小相关验证 |
| --- | --- |
| 入口分包、频道独立进程、S2S | `dotnet run --project tests/DFLegacy.SmokeTests -c Release -- --s2-ipc` |
| 玩法 UDP 分类、生命周期、回退 | 分别运行 `--gameplay-datagram`、`--gameplay-datagram-lifecycle`、`--fallback-protocol`、`--fallback-relay`、`--party-network` |
| 物品空间、仓库映射、身份/品质 | `--item-spaces`、`--c2-warehouse-swap`；默认套件中的 ItemIdentity/ItemSealing 相关测试 |
| 原子替换与奖励写入失败 | `--atomic-snapshot-replace`、`--store-write-transaction`、`--dungeon-reward-commit` |
| 实际文件替换专项 | `--atomic-snapshot-replace-real <独立测试产物目录>`；先读 [AtomicSnapshotReplacementSmokeTests.RealFiles](../../tests/DFLegacy.SmokeTests/AtomicSnapshotReplacementSmokeTests.RealFiles.cs) 的平台要求 |
| 第二章条目/填充/时装 | `--chapter-two-inventory`、`--cera-gift-padding`、`--chapter-two-avatar-clone`、`--avatar-lifetime` |
| 真实资源路径/章节资源 | `--script-path-case`；`--chapter-two-pvf <PVF路径>` 等参数以 [测试 Program](../../tests/DFLegacy.SmokeTests/Program.cs) 为准 |
| 共享协议/存储层 | 相关专项通过后，运行无参数默认冒烟；再做受影响章节真机验收 |

一个有用的协议回归应至少同时约束：精确字节数/顺序、错误输入拒绝、状态变化、重复请求、章节差异。`Create` 后用自己的 `TryRead` 往返只能证明内部自洽；需保留独立字节 fixture 或明确来源的客户端证据。存档回归要验证重启后结果，并确保失败前后的资产守恒与幂等。

## 协议证据账

仓库 [protocol/README.md](../../protocol/README.md) 说明协议地图与台账体系；[protocol/map/opcode-map.tsv](../../protocol/map/opcode-map.tsv) 是规范地图，[protocol/ledger](../../protocol/ledger) 保存证据/场景/run 台账，[manifest.json](../../protocol/manifest.json) 固定受跟踪产物摘要。它是逆向证据资产，不是从当前 C# 自动生成的“已完成功能列表”。

三个概念分开记录：

- 事实来源：`[静态]`、`[抓包]`、`[真机]`、`[回放]`、`[推断]`、`[未验证]`，旧结论可标 `[作废]`。
- 字段可见性分档：`T0` 到 `T4` 及中间分支，表示证据能读到什么，不表示产品完成度。
- 一次验收判定：`pass`、`needs-human`、`missing-evidence`、`fail`，与一次 run 整体 green/red 又是不同层。

“出现某帧”不证明登录成功；“看到字段读写”不证明完整有序布局；历史 run 通过不证明今天代码通过。补运行证据使用 `python -m protocol.s1 ledger` 摄取明确 run，不手拼 TSV。改输入后由 `render` 更新派生摘要，再用 `verify` 只读检查：

```powershell
python -m protocol.s1 verify
python -m pytest protocol -q
```

`import` 依赖历史 `.superpowers` 原始材料；`ledger` 除指定 run 的证据外，也需要 `--raw-dir` 指向的原始 `tiers-external.tsv`，因为它调用 `importer.load_external` 读取人工外部证据。仅有 run 不能在全新 clone 中直接落账。普通 `verify` 仅需入库资产；缺少原始材料时先做 verify，不伪造输入。`map/index.md` 和 `manifest.json` 是生成物，不手动改计数来制造通过。完整 CLI 参数、runId 约束与证据更新流程以该目录 README 和 `s1.py` 为准。

### 已知资料局限

- `protocol/README.md` 的固定历史测试数量、根目录 pytest 收集错误说明具有时间范围；当前构建/测试环境入口以 [BUILDING.md](../../BUILDING.md) 和当前代码为准，不能原样作为新验收结果。
- 根 [STATUS.md](../../STATUS.md)、旧 HANDOFF 和代码注释包含不同阶段甚至不同世代原版程序的研究。引用前记下章节、方向、提交与证据来源；不把第四季接口推断为当前第一/第二章实现。
- 许多源码注释链接到 `docs/` 或 `.superpowers/` 历史材料；这些目录可能未随普通克隆提供。核心交接说明应保留在 `knowledge/`，缺失原证据时明确标注，不能补造地址、字段或通过记录。

本页只记录当前代码可见契约与可复查入口，没有复制本机账号、资源正文、运行令牌或密钥，也没有宣称网络、游戏或存档测试在本次编写中通过。
