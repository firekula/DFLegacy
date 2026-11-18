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
| **端到端购买** | `--shop-purchase` 跑了两份 PVF：线上 C2 服务端 PVF 与本候选服务端 PVF，结果都是 5 断言 0 失败——买 27601 后背包与存档都是 `State=10`，金币扣 2100；买 27600 保持 `State=0`，金币扣 1400 |
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

## 本地真机测试环境（2026-11-18 16:00，未购买）

本轮把候选 PVF 装进隔离测试客户端并跑通登录，**商店显示与购买仍待点一次**。

| 项目 | 值 |
| --- | --- |
| 测试客户端 | `G:\tmp\dfo-v1014-sinda`（从 `G:\tmp\dfo-v1014-clean\client` 复制） |
| 客户端 `Script.pvf` | 保持原版 `22FA7529…`，不破坏启动器的已校验安装记录 |
| 候选 native PVF | 经调试会话私有视图加载：`.dflegacy/chapter-two/debug-items/items-22FA7529C28225A2-v2.pvf` = `C0A4039F…`，同名 `.sha256` 已写入 |
| 启动配置 | `ChapterTwoDebugEnabled=true`、`ChapterTwoServerHost="localhost."`、账号 `localqa1` |
| 服务端 | `DFLegacy.Server.exe --config .tmp/manual-test/server.json`，`ScriptPvfPath` 指向候选 server PVF `3157683B…` |
| 通道密码 | 服务端必须以 `DFL_S1C2_TRIAL_CHANNEL_CIPHER=1` 启动；否则登录包解密失败（日志：`Could not recover the channel payload seed`） |
| 任务道具爆率 | `PUT http://127.0.0.1:48095/api/admin/drop/quest-rate {"ratePercent":1000}`（1000%，已即时生效并持久化） |
| 角色 | `拼音验收甲`（Lv60，localqa1），艾尔文防线，已接任务 444、2111，金币 18 |
| 服务端构建 | `src/DFLegacy.Server/bin/Release/net10.0`，HEAD `d1ab3d7` 重新构建，0 警告 0 错误 |

私有视图不是发布安装：`ChapterTwoDebugEnabled=true` 时启动器把该缓存作为 `DFLEGACY_CH2_DEBUG_PVF`
传给宿主，宿主只在调试会话重定向 `Script.pvf` 的只读打开；`installed.json`、启动器与宿主签名均未改动。
证据：运行中独占打开该文件被拒（说明宿主持有），`launcher.log` 记录
`debug labels enabled: 13044 item scripts; private view=items-22FA7529C28225A2-v2.pvf`。

同轮自动化（HEAD 重新构建后）：

```text
--quest-item-grant   4 assertions, 0 failures
--shop-purchase (候选 server PVF)  5 assertions, 0 failures
--quest-drop-api     9 assertions, 0 failures
```

**仍然没做的**：走到辛达打开商店（当前角色只有 18 金币，买 2100 的武士刀会失败，需要先补测试金币），
以及墓地/副本里杀怪观察任务道具。下一步就是把这两步点完；服务端、客户端和爆率都已就绪。

