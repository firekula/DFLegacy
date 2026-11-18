# 辛达出售 +10 武士刀（2026-11-18）

用户问：强化券（7274）现在是不是没效果？能不能直接让辛达卖 +10 的武士刀。
确认目标是 **27601**（用户指出“生锈武士刀”即此件）。

## 强化券的结论

是。7274 `+10 장비 강화권` 是 `[stackable type] [etc]`、无 `[use info]`；第二章
`DNF.exe` 里没有对 7274 的常量比较或 TCG/强化券字符串；服务端 `USE_STACKABLE`
只带槽位与列表类型，也没有券处理。只上架会得到一件无法使用的道具。
（候选仍在 `dist/c2-sinda-ticket-20261117/`，不删除，保留证据。）

## 新方案：直接卖已强化的装备

物品：**27601 `character/swordman/weapon/katana/kat_worn.equ`**
（韩文 `낡아빠진 카타나` / 英文 `Worn Katana`）。
鬼剑士武器、3 级、白色（rarity 0）、耐久 35、`[price] 2100`、可自由交易。
同一系列还有 27600（Damaged Katana，价 1400）与 27607（Rusty Yaiba）；换目标只需改
`NpcShopOffers.ReinforcedKatanaItemId` 一行并重跑候选工具。

### 为什么需要服务端规则

- 商店**列表**是客户端 PVF（`itemshop/equipmentshop2.shp`，`[NPC] 4` 辛达）。
- 服务端购买路径只按物品 `[price]` 收钱，不校验 NPC 商店；但强化等级是**物品状态**
  （`State & 0x1F`），不是模板字段，通用购买路径创建的是 `State = 0`。
- 因此新增 [NpcShopOffers](../../src/DFLegacy.Server/NpcShopOffers.cs)：
  `27601 -> +10`，在
  [EntranceService.cs](../../src/DFLegacy.Server/EntranceService.cs) 的 BuyItem 分支里
  套在购买记录上；客户端从状态位读取 +N 并自行计算属性，服务端只需交付正确的记录。

### 候选 PVF

`dist/c2-sinda-katana-20261118/`（不提交）：

| 文件 | SHA256 |
| --- | --- |
| `Script.chapter-two.native.pvf` | `C0A4039F…` |
| `Script.chapter-two.server.pvf` | `3157683B…` |
| `verification.json` | 只改 `itemshop/equipmentshop2.shp`，其余 69,398 条逐条相等 |

工具：[tools/pvf/chapter-two/sinda_shop_item.py](../../tools/pvf/chapter-two/sinda_shop_item.py)
（由 `sinda_ticket.py` 泛化而来，支持 stackable 与 equipment）。辛达商店第一行空位
从 `-1` 换成 27601（`3162, 3163, 27601, -1 …`），其余字节不动；价格取物品模板的
2100 金币。

## 验证

| 项目 | 结果 |
| --- | --- |
| 规则单测 | `NpcShopOffersSmokeTests`：27601 → 10、其他 → 0、状态位不越界、其余字段保留 |
| **端到端购买** | `--shop-purchase`（真实 C2 PVF + 真实会话）：买 27601 后背包与存档都是 `State=10`，金币扣 2100；买 27600 保持 `State=0`，金币扣 1400。5 断言 0 失败 |
| 全量冒烟 | 退出码 0，无 FAIL |
| 候选 PVF | 工具逐条比对通过；商店 `[NPC] 4`、27601 已占第一行空位 |

端到端用例：[NpcShopPurchaseSmokeTests.cs](../../tests/DFLegacy.SmokeTests/NpcShopPurchaseSmokeTests.cs)，
本地运行：

```powershell
dotnet run --project tests/DFLegacy.SmokeTests/DFLegacy.SmokeTests.csproj -c Release --no-build -- \
  --shop-purchase G:\Project\DNF2\Server\resources\Script.pvf
```

## 未验证 / 边界

- [未验证] 真实**客户端**里从辛达商店看到、购买并显示 +10（服务端与存档已由真实会话验证；
  还需要装候选 PVF + 新服务端后开客户端点一次）。
- 商店价格就是物品模板价 2100；要改成别的价格会改到该物品的所有来源与出售价，
  目前没有“按商店单独定价”的机制。
- 服务端属性表不聚合装备强化（客户端自己算）；如果以后服务端做伤害权威，需要另补强化加成。
