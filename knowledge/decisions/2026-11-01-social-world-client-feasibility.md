# ACT1 区域频道与假人服务的客户端可行性研究

日期：2026-11-01。范围：当前 ACT1 可执行文件、客户端与服务端 PVF、现有 C# 服务。用户已接受复用 ACT1 原有窗口，并授权深入探索客户端逻辑。

范围接续：用户随后决定副职业全部延期；本文的分解/附魔内容仅保留为前期研究记录。人偶的本地决策和执行链已有进一步证据，见 [原生人偶战斗 AI 接续研究](2026-11-01-native-ai-companion-feasibility.md)。当前任务范围以更新后的设计稿为准。

结论：频道、城镇角色、社交和普通商业有明确复用基础；陪刷必须验证普通队员同步路线。不能直接把保留的 AI 创建消息当成可用接口。分解有原生窗口，但收费摊主身份需要服务端上下文；附魔的永久属性闭环仍未证明。

## 核验对象与证据

| 对象 | SHA256 |
| --- | --- |
| DF/DNF.exe | 6b4850beaf67ec61adb04ae17f119f16278dd05e97b8ffca871a9bdb547de0d5 |
| DF/Script.pvf | 288d58aace8ed4724a0a94aaa4cd0957b1d52c633771132ede2f96363c1b506f |
| DFLegacy/DNF2008/Script.pvf | bc397d1463f1f0f749bbd169400b640d0c8a5d1f6e6e97cfda797099f995ac27 |

本次 IDA 只打开 .recon/social-world-20261101/ida/DNF.exe.idb 副本。没有向原客户端或 PVF 写入，也未启动游戏、服务端或修改存档。原始导出在 .recon/social-world-20261101；可移植的关键证据在 [assets/2026-11-01-social-world](assets/2026-11-01-social-world/verification.json)。

运行 python knowledge/decisions/assets/2026-11-01-social-world/verify_evidence.py 可对本机指定源文件复核 21 项静态检查。它核对哈希、消息名和真实跳转表字节；不代表实机功能通过。

## 频道

[静态] 两份 PVF 都含 etc/channel_info.etc。服务端 DungeonExperienceCatalog.LoadChannelBonusRates 已按服务器号、频道号和副本组解析百分比，CalculateClearExperience 将 ChannelBonus 单列相加；当前普通杀怪经验路径没有使用该加成。

服务器 1 的原始普通区域频道包括：

| 区域 | 可选频道号 | 原表通关加成 |
| --- | --- | --- |
| 洛兰 | 1 | 10% |
| 格兰之森 | 10–14 | 5% |
| 天空之城 | 15–19 | 3% |
| 天帷巨兽 | 20–22 | 2% |
| 雪山 | 23 | 2% |
| 阿法利亚 | 24–26 | 2% |
| 诺斯玛尔 | 27–28 | 2% |
| 交易场 | 6 | 0 |

原表中的 north_myre 和 Alfhlyra 副本成员完全相同，不能未经交叉核验直接用作两个独立区域的最终映射。名称 sky_catle 保留原拼写，解析时不要自行改名导致匹配失败。

当前 ServerOptions.Channel 是单个普通频道配置；PvpChannelConfiguration 才会追加 PvP 入口。入口协议 EntranceMetadata 支持列表，拆分进程 DFLegacy.Channel.Program 仍只发布一个普通频道。TownSessionDirectory 的 AreaKey 尚无频道维度。多频道必须同时修改会话上下文、目录隔离、经验解析和入口列表，不能只多显示几个名称。

## 人偶与普通队友

[静态] 两份 PVF 的 aicharacter/ 均为 15 个文件，只有一份 .aic 定义 cuwaki/cuwaki.aic。它描述等级 55、装备、技能、思考周期、目标距离、四类 AI 模式和按键流；Action.ai 含敌人数与随机分支，DestinationSelect.ai 含 HP 比例分支。样例属性明显属于特殊角色，不作为陪刷平衡值。

[静态] 0x0071BC00 解析 think term、destination change term、keep distance with target、vision、targeting bonus、ai pattern、key stream 等字段。0x004DEB50 尝试读 AICharacter/OperatingValue.lst，但当前两份 PVF 都没有该文件。找到数据解析器不等于找到可运行的陪刷实例。

关键反证：

1. 原生 UDP 名表基址 0x00B51C08，索引 40 的名字为 UDPPACKET_CREATE_AI_CHARACTER。
2. 接收函数 0x0078EE00 在 0x0078EF40 分发应用消息，跳转表为 0x0079213C。
3. 真实 PE 表项 [40] 是 0x00792127，与索引 1 共用默认分支。
4. 该目标直接恢复异常处理链和栈，并 ret 0x0C，没有创建对象的调用。

因此，向这个版本发送编号 40 不会经此入口创建人偶。该结论仅约束已定位的网络入口；未据此宣称所有本地或 Monster 型 AI 都不存在。

[静态] 同一分发器确实处理普通对象的移动 4、状态 5、HP 12、MP 13、命中 19、伤害 20、消耗品 24、技能 25、复活币 30。技能分支以对象类型和 ID 查找 IRDActiveObject，再调用技能相关虚函数。这提供普通虚拟队员的候选协议路径，但不能证明仅发技能消息即可产生合法命中和掉落。

[未验证] 虚拟对端的握手、可靠传输、角色物理推进、技能状态、命中来源、对象主控权、死亡、换房重建及主机变化，需要一真人一假人、随后二真人一假人的实机验证。现有 GameDatagramProtocol 有应用层封装和不可靠帧构造，尚不是完整的虚拟客户端。

地图怪物/召唤物 AI 与正式玩家槽位属于不同对象生命周期。可参考其选敌和跟随，但不能用友方怪物外观替代组队、拾取、复活与结算语义。

## 分解服务

[静态] 二进制含 Sprite/Interface/DisjointShopWindow.img。NPC 商店脚本解析器 0x00748130 将 disjoint shop 识别为商店类型 2，NPC 对话动作解析器识别 disjoint item。它们是既有 NPC 商业入口，不是后期玩家分解机协议。

[静态] 0x004601B0 构造 CMD 28，发送装备格子 u16 和物品栏类型 u8。请求没有摊主 UID、机器等级、价格或服务会话 ID。当前 EntranceService 同样仅从正文偏移 2、4 读取这两个值。

服务端可复用 DisjointCatalog、DisjointItemPlanner 的物品验证、产物和背包规划；收费功能还必须绑定服务者、有效期、区域、报价和一次性结算。不能对所有 CMD 28 全局扣摊位费用。

两种候选交互：

- 原生分解窗口：验证能否从现有交互建立明确的摊主上下文，并证明打开、关闭、取消和跨区会清除上下文。仅看见窗口资源不算通过。
- 原生交易窗口：向摊主提交装备和手续费，摊主回填本次产物；玩家确认后一次提交消耗、产物及费用。它可复用窗口，但需要扩展交易结算，不能先收装备再以另一笔交易返还。

第二种是设计稿中的默认技术建议，尚未实机验证。机器模型与原生分解窗口的独立点击能力未证明。

## 附魔服务

[静态] 163 项 ACT1 命令名表没有 ENCHANT 类命令；本次匹配到的 CNEnchantWeapon、Disenchant 等主要属于战斗效果，不能当作附魔摊入口。后期参考服务端的 AGREE_ENCHANT、TRY_ENCHANT、CREATE_DISJOINT_STORE 等编号不属于本 ACT1 命令合同。

[静态] 当前 CharacterItemRecord 和普通装备传输包含装备 ID、品质种子、强化/再封装、耐久及封装状态，没有已实现的独立附魔记录。服务端存在人物属性投影接口，但未证明它能完整替代物品附魔对本地技能伤害、重登和他人查看信息的影响。

[未验证] 先用单项属性验证存储、穿脱、角色重登、交易转移、面板和真实伤害的一致性，再决定采用原生可用字段、服务端逐物品元数据加属性投影，还是必要的数据资源扩展。不能拿临时技能效果冒充永久附魔，也不能默认复用装备品质或强化位保存卡片。

## 对设计的影响

采用单进程多频道、统一虚拟角色和原有窗口，分别设置战斗、收费分解、附魔三项启用门。每项通过自己的功能闭环后再开放，不把站街成功当作陪刷成功，不把收款成功当作附魔成功。

设计见 [区域频道与假人玩家设计稿](../../docs/superpowers/specs/2026-11-01-social-world-design.md)。本报告不改变既有部署状态。
