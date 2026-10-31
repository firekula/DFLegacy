# 设计稿：官方副本双层对齐（2008 客户端行为 + 官服内部机制）

> 状态：待用户审阅。本文只定义设计与验收，不修改生产代码。
> 目标：在保留当前“双客户端可同图、共同打怪、共同通关”基线的前提下，逐段收敛到官方 `df_game_r` 的队伍副本状态机；当前 2008 客户端可观察行为是第一层交付目标，官服静态可确认但客户端未必触发的内部机制是第二层目标。
> 证据类别固定为 `[静态]`、`[抓包]`、`[真机]`、`[回放]`、`[推断]`、`[未验证]`；无法验证的内容不得写成已完成事实。

## 1. 已确认的现状与边界

### 1.1 当前已成功的能力

双客户端已经能够进入同一副本实例，共享房间、怪物、击杀、掉落、开门和通关状态；最新真机记录还包含双人完整通关且无弹窗/崩溃的一轮。[真机：`STATUS.md:903-965`]

因此本轮不是重做“队友不可见”或“无法共同打怪”，而是修复调试补丁造成的流程偏差，重点放在过图、加载、ALLDIE、掉落路由、翻牌和回城。

### 1.2 两层目标

**第一层：2008 客户端可观察行为。**

- 双人使用一个权威副本状态；
- 入场、换房、加载、清房、结算、翻牌、回城的实际包序列和状态转移符合客户端要求；
- 当前客户端只读取的 `NOTI 11` 七字段布局保持不变；
- 客户端不处理的 `NOTI 67` 不作为 2008 流程必需包。[静态：`.superpowers/sdd/2026-09-29-dungeon-party/work/official/STATIC-FINDINGS.md:289-334`]

**第二层：官服内部对齐。**

- 建模 `CParty` 的 party slot、host、room、loaded、settlement、timer、P2P 和 leave/give-up 状态；
- 实现官服静态明确的路由、超时、失败、队伍广播和状态机；
- 官服只由静态符号或伪码确认、但无法用当前客户端/重放验证的项目保留 `[未验证]` 标签。

### 1.3 不可偷换的判断

“蓝色及以上装备必定掷骰”和“routing 必然是随机掷骰”目前不能算官服静态已证实事实。官服明确存在 `CItem::IsRoutingItem` 与 `CSpecialItemRoutingManager`，但品质阈值和具体选择规则仍需函数级反编译、抓包或真机验证。[静态：`game_r.syms.txt` 中 `CItem::IsRoutingItem` 与 `CSpecialItemRoutingManager` 条目；`packet_senders.txt:207-209,458`；未验证：`STATUS.md:855-860`]

## 2. 架构原则

### 2.1 唯一权威状态

保留 `DungeonInstance` 作为共享实例容器，并逐步把官方 `CParty` 状态集中进去；不再允许 `EntranceService` 会话局部变量、门口集结器、结算状态和实例同时拥有同一状态的多个真源。

```text
DungeonPartyRuntime / DungeonInstance
├── PartySlotTable(character, uid, slot, send handle)
├── HostState(manager slot, host slot)
├── DungeonRun(room, map, monsters, clear, drops)
├── LoadingState(room generation, loaded[slot], timeout)
├── SettlementState(result, reward, cards, selected[slot], deadlines)
├── RoutingState(item, candidates, selections, timeout, outcome)
├── NetworkState(endpoint[slot], peer status, host announcement)
└── LeaveState(member leave, give-up, instance teardown)
```

锁内只做状态判断/转换并生成待发包描述；实际写流、跨会话发送和日志在锁外进行。此约束与现有 `DungeonInstance` 的锁模型一致。[静态：`src/DFLegacy.Server/DungeonInstance.cs:432-495`]

### 2.2 `EntranceService` 的最终职责

会话层只做：

```text
读取/校验客户端命令 → 调用 DungeonPartyRuntime → 按收件人投递结果
```

谁是 host、是否允许换房、是否全员加载、是否完成翻牌、是否回城，不再由会话局部代码独立决定。

### 2.3 单人隔离

单人实例仍以角色 ID 为键、`IsParty == false`、只有一个成员；单人立即换房、立即满足加载门槛、不进入队伍广播/路由/门口集结。任何组队专用新增逻辑必须以 `IsParty` 为边界，并通过单人报文回放守住零回归。[静态：`task-2-report.md:80-100`；`task-5-brief.md:35-104`]

## 3. 官方对齐流程

### 3.1 入场与 host

官服 `CParty::game_start` 会检查成员是否处于同一城镇/区域，为成员准备基本信息和 `USER_AREA`，再设置 host、发送队伍信息和副本信息。[静态：`ida_targeted_out.txt:154-233,258-350`]

计划：建立 `PartyDungeonStart` 单一入口：

1. 校验全员区域；
2. 冻结 slot 表；
3. 创建或加入 party 实例；
4. 只在首次开局生成 layout；
5. 设置 host slot；
6. 准备成员记录/区域状态；
7. 发送队伍 peer 表；
8. 对全队发送同一个 `NOTI 26` host slot；
9. 发送副本信息和起始地图；
10. 按 room generation 建立加载门槛。

`NOTI 26` 不是每个人自己的 slot，而是队长/主机 slot，全队相同；客户端把该字节作为 `0..7` 的 host slot 使用。[静态：`STATIC-FINDINGS.md:153-166,307-327`]

### 3.2 换房与加载

官服 `CParty::move_map` 使用服务端 `CBattle_Field` 状态校验并推进一次，广播 `NOTI 29`，启动 loading check，清零全员 loaded，并更新成员副本信息。[静态：`calls_party_dungeon.txt:109-146`]

当前 `DungeonDoorAssembly` 只能作为 2008 客户端输入的兼容适配器，不能拥有第二个副本真源。最终唯一推进入口为 `DungeonPartyRuntime.TryAdvanceRoom`：

- 同目标并发请求只产生一次状态转移；
- 非法路径产生官方形式的 `CMD 48` 错误；
- 成功推进生成新 room generation，清空 loaded；
- 对全员投递同一逻辑的 `NOTI 29`；
- 单人仍立即推进。

宽限窗口的真实必要性需由真机事件验证；已有记录显示 2 秒窗口 15 次均以 `1/2` 强制推进，不能将其误称为官方行为。[真机：`STATUS.md:949-965`]

每个成员 `CMD 40` 只标记自己在当前 room generation 的 loaded；全员完成后才广播空载荷 `NOTI 30`。官服同时存在发送者 ACK 与全队 `NOTI 30` 两层信号。[静态：`ida_targeted_out.txt:526-617`]

当前客户端会在地图重载时重复上报 `CMD 40`，因此第一层要求重复上报幂等且不阻断最终广播；第二层再验证官服严格重复早退语义。[推断/真机记录：`src/DFLegacy.Server/DungeonInstance.cs:269-276`]

### 3.3 怪物、ALLDIE 与清房

统一为 `NormalMonsterDeath`、`BossDeath`、`ForceAllDie` 三种状态转换：

```text
锁内摘除存活怪 → 判断新鲜/陈旧 → 生成广播描述 → 重算清房/开门
```

规则：重复死亡不重复广播或奖励；ALLDIE 只处理实例内仍存活集合；级联死亡不产生普通经验/掉落；死亡包奖励序号只能使用合法值或默认哨兵。近期非法 `championRewardOrdinal=0` 曾导致会话异常断开，已由真机日志闭环。[真机/回放：`STATUS.md:1017-1039,1062-1087`]

动态生成怪物是否完整纳入官服级 ALLDIE 仍需单独验证；不能仅凭静态房间初始列表声称全部复刻。[未验证：`STATUS.md:1037-1039`]

## 4. 掉落归属与 Special Item Routing

### 4.1 官服已证实的结构

官服存在独立 `CSpecialItemRoutingManager`，包括：

```text
StartRouting(CUser, Inven_Item, ...)
processRouting(CParty)
ArriveMemberSelection(CUser, CParty)
ProcessTimeOut(CParty)
ProcessGiveUp(CParty)
ProcessFailEtc(CParty, ...)
cancelTimer(CParty)
```

并存在 `CItem::IsRoutingItem` 物品级判定、`Dispatcher_BiddingRoutingItem` 和专用包：

```text
NOTI 281 StartRouting
NOTI 282 processRouting / failure
CMD 301 BiddingRoutingItem
```

[静态：`game_r.syms.txt` 中对应符号；`packet_senders.txt:207-209,458`]

因此官方不是简单的“共享地面物 + 谁先拾取谁获得”单层模型。至少存在：物品进入 routing、队伍级选择/等待、成员选择到达、超时/放弃/失败、最终归属的独立生命周期。

### 4.2 尚未证实的规则

以下都必须保持 `[未验证]`：

- 蓝色及以上是否是 routing 的唯一品质阈值；
- routing 是掷骰、竞价、轮转还是混合机制；
- CMD 301 的字段和 UI 语义；
- routing 期间物品是否仍存在于普通地面物集合；
- 成员掉线、拒绝、超时后的候选重算。

下一步静态取证固定为：反编译 `CItem::IsRoutingItem`、`StartRouting`、`processRouting`、`ArriveMemberSelection`、`ProcessTimeOut`、`ProcessGiveUp` 和 `Dispatcher_BiddingRoutingItem::dispatch_sig`；随后用同一物品类别的抓包或真机实验交叉验证。

### 4.3 第一层实现契约

在官方规则未闭合前，不把当前“先到先得”升级成官方事实；只建立可替换 `ItemRoutingState`：

```text
item id/category
candidate party slots
selection per slot
routing timer
assigned slot
failure/timeout/give-up result
```

普通物品继续保持当前已验证的客户端行为；高品质/特殊物品走显式待验证分支，不在会话层随手随机。每件物品只能产生一次最终归属，跨成员拾取必须幂等。

## 5. 结算与翻牌

### 5.1 官方手动选牌

官服 `CMD 74` 上行读取卡类型/卡号，按发送者 party slot 调用 `SetCardNumber`，拒绝无效 slot、重复选择和已占用卡，然后调用 `SendEachCardInfo` 向全队广播更新快照；满足完成条件后进入 `dungeon_clear`。[静态：`ida_cardflow_out.txt:2-49`；`ida_targeted_out.txt:771-865`]

`SendEachCardInfo` 构造一份包含所有卡列 owner、物品信息和 selected 状态的 class 1 / `CMD 74`，对全队发送；它不是“发送者自己的奖励回包”。[静态：`ida_targeted_out.txt:712-755`]

### 5.2 官方无人操作自动翻牌

`TimerCardSelect::dispatch_sig` 的核心是：

```text
获取 party
→ 校验 timer key
→ 若队伍未完成选牌：CompleteCardSelect(party)，随后 SendEachCardInfo(party)
→ 设 dungeon clear state = 5
→ dungeon_clear(party)
```

[静态：`ida_timers_out.txt:73-97`]

`CompleteCardSelect` 在共享 party 卡表上遍历未分配卡列，按有效成员 slot 补全，而不是每个会话各自寻找本地最低卡位。[静态：`ida_cardflow_out.txt:51-87`]

因此当前已知 bug 的根因是会话级自动选择：`FindLowestAvailableFreeCardIndex(..., [])` 对两名成员都返回 0，再各自 `GrantFreeClearRewardAsync(0)` 和广播局部快照。[静态：`EntranceService.cs:1781-1813`；`ClearRewardCardPlanner.cs:5-25`]

### 5.3 翻牌改造契约

新增队伍实例唯一 `PartyCardSelectionState`：

```text
CardOwner[card column]
CardType[card column]
RewardItem[card column]
SelectedBySlot[party slot]
ManualSelectionReceived[party slot]
AutoCompleted
SettlementGeneration
```

手动和自动路径必须共用同一套状态转换：

1. 成员手动选择：锁内按其 slot 记账；锁外广播完整 `CMD 74`；
2. 计时器到期：锁内只执行一次 `CompleteCardSelect`，统一补全未选成员；
3. 为每个 slot 结算对应奖励，不允许两个成员各自领取同一逻辑卡位；
4. 构造一次完整队伍 `CMD 74` 并向全队发送；
5. 统一进入 `dungeon_clear`；
6. 结算/回城 timer 只由 party generation 驱动。

两人都不操作时，必须满足这些已证实不变量：

- 自动补牌只执行一次；
- 结果来自共享 party 状态；
- 每个有效成员最多拥有一个对应奖励归属；
- 两名客户端看到相同的卡主—内容关系；
- 不会都把本地第一列标成自己获得；
- 后续统一进入结算清理。

具体“哪一列给哪个 slot”在完成 `IsSelectCard`/`CompleteCardSelect` 全部字段语义核对前保留 `[未验证]`，不能用固定的“第一列给 0、第二列给 1”代替官方算法。

### 5.4 `NOTI 67` 边界

官服有空载荷 `NOTI 67 CARD_SELECT_COMPLETE_STATE` 广播，但当前 2008 客户端通知分发表将 case 67 落入 default，因此第一层不能把它当作解决当前翻牌 bug 的必需包。[静态：`STATIC-FINDINGS.md:329-334`]

## 6. 离队、超时和回城

统一建模主动回城、结算 EPLP、掉线、换角色、队伍成员离开和最后成员销毁。旧 room/settlement/routing generation 的异步 timer 必须失效；队长离开不应自动销毁仍有成员的 party 实例，最后一人离开才清理共享状态。[静态：`final-fix-report.md:46-66`；`task-6-report.md:39-74`]

## 7. 实施顺序

1. 冻结当前成功双人通关与已知异常基线；统一日志字段为 session/character/uid/slot/party/run/room generation。
2. 将 `PartySlotTable`、host、room generation、loaded、settlement、card、routing、timer 集中到 runtime；不改包序列。
3. 复刻 `game_start` 与 host slot，全队 `NOTI 26` 一致。
4. 将 `CMD 48` 收敛到唯一 `TryAdvanceRoom`；门口集结只作为兼容输入层。
5. 复刻 `CMD 40` 全员门槛和 `NOTI 30` 广播，同时保持客户端重复上报幂等。
6. 收口 Normal/Boss/ALLDIE 清房链，验证动态生成怪物。
7. 静态反编译并独立接入 Special Item Routing；在证据闭合前不宣称品质阈值和掷骰规则。
8. 先修队伍实例唯一自动翻牌，再统一手动/自动 `CMD 74` 和 dungeon clear。
9. 统一结算 EPLP、回城、离队、掉线和实例销毁。
10. 每阶段完成 smoke、协议回放、单人回归和双客户端真机验收后再进入下一阶段。

## 8. 验收矩阵

### 8.1 自动化/回放

- party slot 稳定且 host slot 全队一致；
- 同一房间并发换房只有一次状态转移；
- 换房清空 loaded，收齐 CMD 40 后只广播一次 NOTI 30；
- 怪物死亡幂等，ALLDIE 后重新判定清房；
- routing 状态超时/放弃/失败可重放且不重复归属；
- 两人无人翻牌只生成一份 party-level 自动结果；
- 所有成员收到一致的 CMD 74 归属快照；
- 单人包序列无新增、无改序；
- 旧 generation 事件不能污染新房/新结算。

### 8.2 真机

- 双人多房连续换房不分叉、不出现 3525/网络断开；
- 双人共享清房、动态生成怪物、掉落和结算；
- 两人都不操作翻牌时，两边显示一致且都能继续回城；
- 一人手动、一人自动时，自动结果仍由 party 状态统一；
- routing 物品按后续静态/抓包结论表现；
- 队长离开、队员离开、掉线、最后成员离开均无残留；
- 单人完整流程保持原包序列和行为。

### 8.3 证据纪律

每个结论至少绑定文件+行号或可重跑命令，并标证据类别；静态伪码只证明服务端意图，不能单独证明客户端真实行为；相互引用同一份报告不算第二种独立证据。新证据推翻旧结论时，旧结论保留并标 `[作废]`，另写决策记录。

## 9. 本设计的明确非目标

- 不在未闭合证据前直接实现“蓝装以上掷骰”；
- 不把 2012 客户端才处理的通知强行加入 2008 第一层流程；
- 不通过增加单个补包继续试错；
- 不以一次成功真机局宣称所有时序完成；
- 不改动与本阶段无关的城镇、交易、好友或启动器功能。
