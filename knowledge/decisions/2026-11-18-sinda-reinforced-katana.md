# 辛达出售 +10 破损武士刀（2026-11-18）

用户问：强化券（7274）现在是不是没效果？能不能直接让辛达卖 +10 的武士刀。

## 强化券的结论

是。7274 `+10 장비 강화권` 是 `[stackable type] [etc]`、无 `[use info]`；第二章
`DNF.exe` 里没有对 7274 的常量比较或 TCG/强化券字符串；服务端 `USE_STACKABLE`
只带槽位与列表类型，也没有券处理。只上架会得到一件无法使用的道具。
（候选仍在 `dist/c2-sinda-ticket-20261117/`，不删除，保留证据。）

## 新方案：直接卖已强化的装备

物品：**27600 `character/swordman/weapon/katana/kat_damaged.equ`**
（韩文 `망가진 카타나` / 英文 `Damaged Katana`，玩家侧常见译名“破损的武士刀”）。
鬼剑士武器、2 级、白色（rarity 0）、耐久 35、`[price] 1400`、可自由交易。
“生锈”系列另有 27601（Worn Katana）与 27607（Rusty Yaiba）；如果玩家指的是另外
两把，只需改一行常量并重跑候选工具。

### 为什么需要服务端规则

- 商店**列表**是客户端 PVF（`itemshop/equipmentshop2.shp`，`[NPC] 4` 辛达）。
- 服务端购买路径只按物品 `[price]` 收钱，不校验 NPC 商店；但强化等级是**物品状态**
  （`State & 0x1F`），不是模板字段，通用购买路径创建的是 `State = 0`。
- 因此新增 [NpcShopOffers](../../src/DFLegacy.Server/NpcShopOffers.cs)：
  `27600 -> +10`，在
  [EntranceService.cs](../../src/DFLegacy.Server/EntranceService.cs) 的 BuyItem 分支里
  套在购买记录上；客户端从状态位读取 +N 并自行计算属性，服务端只需交付正确的记录。

### 候选 PVF

`dist/c2-sinda-katana-20261118/`（不提交）：

| 文件 | SHA256 |
| --- | --- |
| `Script.chapter-two.native.pvf` | `E6707B68…` |
| `Script.chapter-two.server.pvf` | `9D665646…` |
| `verification.json` | 只改 `itemshop/equipmentshop2.shp`，其余 69,398 条逐条相等 |

工具：[tools/pvf/chapter-two/sinda_shop_item.py](../../tools/pvf/chapter-two/sinda_shop_item.py)
（由 `sinda_ticket.py` 泛化而来，支持 stackable 与 equipment）。辛达商店第一行空位
从 `-1` 换成 27600，其余字节不动；价格取物品模板的 1400 金币。

## 验证

| 项目 | 结果 |
| --- | --- |
| 规则单测 | `NpcShopOffersSmokeTests`：27600 → 10、其他 → 0、状态位不越界、其余字段保留 |
| 全量冒烟 | 退出码 0，无 FAIL（`.tmp/smoke-full3.log`） |
| 候选 PVF | 工具逐条比对通过；商店 `[NPC] 4`、27600 已占第一行空位 |

## 未验证 / 边界

- [未验证] 真实客户端里从辛达商店购买后显示 +10 并生效（需要装候选 PVF + 新服务端）。
- 商店价格就是物品模板价 1400；要改成别的价格会改到该物品的所有来源与出售价，
  目前没有“按商店单独定价”的机制，不要为了一个价格复制物品模板。
- 服务端属性表不聚合装备强化（客户端自己算），所以本轮只保证交付正确的状态位；
  如果以后服务端做伤害权威，需要另补强化加成。
