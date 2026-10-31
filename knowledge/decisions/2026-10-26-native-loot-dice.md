# 2026-10-26：恢复 ACT1 原生组队拾取骰子

> **后续勘误（2026-10-27）**：本页关于“本房尸体参与普通物品骰子”“ACT1必须扩展确认窗”及简化CanRoute门控的结论已被完整取证推翻；当前实现与验证以[官服完整取证及接口对齐](2026-10-27-official-loot-contract.md)为准。本页保留调查历史，不作为最新规格。

## 本轮范围

用户要求先核对官方服务端及客户端接口，再修复组队骰子。发现版本边界后，用户明确选择“本轮先恢复原生骰子分配，记录高品质弹窗所需扩展”。因此本轮实现普通骰子，高品质确认投票延期；不能将普通 NOTI39 与新版确认协议混为一谈。

## 官方证据与旧结论勘误

官方样本 `.superpowers/sdd/2026-09-29-dungeon-party/work/official/df_game_r.bin`，本轮重新核对 SHA256：`8859c99d8290dc5fb58151444138cf2697b2bfff12d47d6159596a03f20b5261`。引用原有反编译输出 `2026-10-02-party-dungeon-official-rewrite/work/out/ida_s5_loot.txt`、`ida_s5_routing.txt`；本轮客户端 IDA 探针及原始输出在 `.recon/loot-routing-20261026/`。

- **[静态]** `CItem::check_item_routing@0850D756` 是 `isEpicRoutingItem || rarity>0`；独立参数 `[epic routing] == 1` 写入 `CItem+0x79`。并非“不看稀有度只看单独参数”。
- **[静态]** `IsRoutingItem@08150F18` 是另一标志（`CItem+0xBD`），`[routing priority]` 是另一职业/成长优先列表（`CItem+0xC0`）。它们不能替代普通骰子门控。标签复制链见 [旧取证闭合](2026-10-01-pickup-and-routing-closure.md)。
- **[静态]** `get_item@085A3B98` 的普通分配还受地面来源 `v116[0]` 和请求 bool 控制：来源不是 1 时先关分配；rarity>3 时用请求 bool 覆盖。ACT1 的 8 字节请求不能套用新版 16 字节 reader；未知尾字段未改。
- **[静态]** 现有分数公式：每个有效候选基础 1，轮转者 +40，特定物品职业匹配 +30，再加 `rand(100-base)`；最高分获奖，同正分后遍历者减 1。轮转和并列逻辑保留。
- **[作废]** 旧代码称“本客户端没有骰子 UI”过于宽泛。ACT1 已有普通骰子文字反馈；缺的是新版的高品质确认协议。旧的临时 `rarity>3` 限制会漏掉大量非白物品，也不能称为“蓝色以上”。
- **[静态]** 官服样本普通入口还有 `EB16` 跳过特殊 StartRouting 的补丁痕迹，见 [旧勘误](2026-10-01-routing-static-corrections.md)；不能宣称所持样本无修改，也未将该跳转猜改。

## ACT1 客户端接口

本轮重新在 `G:/Game/DNF_ACT1/DF/DNF.exe.idb` 上读取 `NOTI39@00421EA7`。物品分支 `00422275`：

```text
u16 groundId
u16 pickerUid
u8 score[4]          // 按队伍槽位，未参与槽为 0
u16 winnerUid
u16 destinationSlot
```

**[静态]** `00422313–00422420` 对非零分数拼装成员名与数字；`004224AC–004224BF` 比较 winnerUid 与本地 uid，仅匹配者进入 `004224C5` 之后的物品入包路径。整个包是 ACT1 的 12 字节形态，未照搬新版尾 byte。

**[静态]** `NOTI39` 首先查地面对象，缺失就跳过结果；因此必须“先全队骰子结果，再 NOTI86 地面移除扩展”。直接拾取仍仅通知本会话，避免普通掉落增加队友骰子文字。

## 实现

- 恢复普通 `rarity>0` 门控；ItemCatalog 解析 `[epic routing]`，只有整数 1 启用，缺省及其他值不启用。
- 地面记录追加 `CanRoute`：怪物/被动对象掉落可分配，玩家丢弃和个人奖励溢出显式关闭。任务物品与金币不进普通物品骰子。这是按当前生产来源收口的兼容规则，未声称已还原新版 `map_item` 所有来源枚举。
- 实例锁内过滤候选：活人和本房尸体参与；换房清尸体资格；观战死者不会因重访旧图恢复资格；离队、复活取消和成功提交均有对应处理。
- 将 NOTI39 的 picker/winner 分开编码，发生骰子时向全队广播相同结果。普通背包、宠物、时装、仓库升级券统一使用此入口；保存和库存变更仍只在得主会话执行。
- 获奖信箱绑定原实例与原地面对象引用；真正 claim 在同一锁内核对原对象后摘除。旧局消息不能借复用 GroundId 领取新物品。
- 本轮没有修改 IJL15 的骰子接口；原有地面移除扩展仍需已验收版本的 IJL15。

## 高品质确认扩展待办（本轮用户明确延期）

官方 `CSpecialItemRoutingManager::StartRouting@0860A9D4` 通过 `NOTI281 SELECT_ROUTING` 广播物品 id；`CMD301 BIDDING_ROUTING_ITEM` 接收选择，`NOTI282 ROUTING_RESULT` 返回结论/失败。ACT1 原生命令读取 byte 且范围 <=0x9F，通知 <=0x97，不能截断大编号发送。

后续若实现，需要给 IJL15 增加兼容通知/回答接口及非阻塞确认窗；服务端维护物品与本局身份、候选快照、确认/放弃/超时、全部放弃、离队/掉线/换房取消及唯一发奖。优先职业列表与普通骰子算法分别建模，再做多客户端真机验收。

## 验证与限制

- **[回放]** 修复前新用例明确出现 5 项行为失败：非白门控、前房尸体、重复死亡、重访尸体、离队旧快照；修复后定向 `--loot-routing` 为 0 failures。
- **[回放]** `--pickup-removal` 为 0 failures，覆盖三端收到同一 12 字节骰子结果、不同 picker/winner、先结果后移除、直取与单人字节兼容。
- **[回放]** 新增独立 epic 标签、掉落来源、复活待保存/取消/成功、换房、旧实例/旧地面对象与 GroundId 复用保护。
- **[构建]** `dotnet build src/DFLegacy.Server/DFLegacy.Server.csproj -c Release --no-restore`：0 warnings、0 errors。
- **[部署]** 确认 `DF/town-test` 服务端未运行后，只替换 `DFLegacy.Server.dll` 和 `DFLegacy.Protocol.dll`，并逐一核对 SHA256 与 Release 构建一致。原件备份在 `G:/Game/DNF_ACT1/DF/town-test/backup-loot-routing-20261026-231144`；未启动服务端，配置/存档/客户端 DLL 未替换。
- **[回放]** 最终常规完整 SmokeTests：`All DFLegacy protocol smoke tests passed.`（退出码 0），见 `.recon/loot-routing-20261026/full-smoke-standard-final.log`。
- **[未验证]** 本轮未运行双客户端真机，不将包字节与静态分支核对扩大为画面验收。建议双开验证：rarity=1 的普通掉落、当前房间尸体、换房后的观战死者、两端分数与实际背包得主。
- **继承边界**：普通候选尚未复刻官服的全部背包预筛与职业 +30（生产 JobMatchesItem 仍 false，得主最终收包时校验容量/负重）；同地面物的近同时请求可能在最终唯一 claim 前推进两轮轮转。这些未在本轮扩展为分配架构重写；唯一物品入账仍由锁内 claim 保证。

## 附加真实 PVF 检查：8 项失败（不隐去）

用 `DF2008_REAL_PVF_PATH=G:/Game/DNF_ACT1/DFLegacy/DNF2008/Script.pvf` 运行完整 SmokeTests，退出码 1，完整输出 `full-smoke-final.log`。当前档案与测试数据期望有以下不符；未通过对旧版本重跑来认定它们的引入时间，本轮不擅改这些数据断言：

1. `shared PVF reader indexes the DFLegacy archive`
2. `DFLegacy Script.pvf indexes the complete baseline archive and local additions`
3. `DFLegacy Script.pvf reads etc/sptable.etc without extraction`
4. `DFLegacy Cera catalog resolves avatar cash prices from item definitions`
5. `real PVF Cera products settle item 190 as one million gold and item 191 as one hundred victory points`
6. `real PVF Cera prices account for the observed 1000-Cera gold voucher and 900-Cera creature-food purchases exactly once per commodity`
7. `Faras evolution quest starts with one remaining target and action 0x10 decrements its first packed hunt counter`
8. `Creature evolution changes only the species item while preserving uid, level, experience, hunger and name`
