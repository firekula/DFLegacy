# 任务道具掉落修复与辛达 +10 强化券上架（第二章）

日期：2026-11-17。目标环境：第二章 Chapter Two（v1.0.13 / 准备中的 v1.0.14）。
源码基线：`664484f`（main）；线上服务端镜像对应源码 `7eac3c4`（`G:/Project/DNF2/Server/release-manifest.json`）。
本轮没有改启动器文件（另一个任务在做 `codex/chapter-two-full-starter-upgrade`）。

## 1. 用户可见问题的两个根因

### 1.1 发放已落库，但会话背包没有换新（单人也会中）

`GrantQuestItemDropsAsync` 把发放计划放在 `plannedInventory`，保存成功后只更新了
`currentQuests`，既没有更新会话的 `mainInventory`，更新包又从**发放前**的库存投影：

- 计划副本：`src/DFLegacy.Server/EntranceService.cs:2660-2664`（`mainInventory.ToDictionary`）。
- 新库存：`src/DFLegacy.Server/EntranceService.cs:2715`（`plannedInventory = placement.Inventory`）。
- 旧投影（修复前）：`EntranceService.cs:2756-2761` 的 `ToGameInventoryEntry(mainInventory[slot])`。

任务道具一次 1 个、通常落进空槽，而空槽不在发放前的 `mainInventory` 里 ⇒
`KeyNotFoundException`。异常发生在保存之后，整条会话以
`Entrance session ... ended with an error` 结束；即使槽位刚好已存在，客户端也会
收到旧数量。修复后同一处改为从 `plannedInventory` 投影，并在保存成功后
`mainInventory = plannedInventory`（`EntranceService.cs:2757,2762`），与交易/金币
“待安装”同一条红线。

### 1.2 组队时只有“抢先判定死亡”的那个人拿任务道具

击杀处理只在判定成立的那个会话里调用 `GrantMonsterQuestItemsAsync`
（`EntranceService.cs:3115-3119`）。同实例其他成员只收到经验信用
（`EntranceService.cs:3126-3139`），没有任务道具。于是 N 人队里每人的任务道具
实际掉率是单人的约 1/N，非击杀者可能一直“刷不出来”。

修复：在队友击杀信用的消费点（`EntranceService.cs:5063-5081`）先按本会话自己的
任务/背包/难度各掷一次任务道具，再走原来的经验结算。教学副本、已回城、Boss 级联
死亡不发（`ShouldGrantPartyQuestItems`，`EntranceService.cs:18682`）。

### 1.3 单人“爆率极低”不是解析错误

PVF 的 `.qst` 注释明确写了字段含义（`quest/value/vilmark8-1.qst` 的
`[monster reward item]` 注释，来自第二章原生 PVF）：

```
몬스터 인덱스, 던전 인덱스 (-1), 던전 난이도 (-1 / 0:EASY..3:VERYHARD),
아이템 인덱스, 최대 아이템 수, 등장 확률 (%), 최대 소유치 (-1: 무제한)
```

服务端 `QuestCatalog.ParseMonsterRewardItems`（`QuestCatalog.cs:903-918`）与
`QuestItemDropPlanner`（概率按百分比、`maximumCount` 为“最大拥有数”）与注释一致。
抽查 ACT1/第二章 PVF：`monsterIndex` 能在 `monster/monster.lst` 解析成真实怪物
（17=TauMetaCow、40=MasterDogri、53=Selion…），`최대 소유치 >= 需求数量` 无冲突。
因此单人 2% 的条目在原始数据里就是 2%（约 50 只/件），不是本轮缺陷；组队时的
1/N 与发放崩溃才是需要修的部分。

## 2. 改动与验证

| 文件 | 改动 |
| --- | --- |
| `src/DFLegacy.Server/EntranceService.cs` | 发放后同步 `mainInventory`；更新包从计划库存投影；队友击杀信用补发任务道具 |
| `tests/DFLegacy.SmokeTests/QuestItemGrantSmokeTests.cs` | 新增：计划库存投影、旧库存必抛、组队信用口径 |
| `tests/DFLegacy.SmokeTests/Program.cs` | 注册该用例；新增 `--quest-item-grant` 单跑入口 |
| `tools/pvf/chapter-two/sinda_shop_item.py` | 生成辛达商店候选 PVF（只改一个 `.shp` 条目；原名 `sinda_ticket.py`） |

> 后续（2026-11-18）：强化券 7274 确认无使用入口，改为让辛达直接出售
> **+10 破损武士刀（27600）**，见 [辛达出售 +10 破损武士刀](2026-11-18-sinda-reinforced-katana.md)。
> 券的候选 PVF 保留作为证据，不再作为上架方案。

实际执行（本机，2026-11-17）：

```text
dotnet build tests/DFLegacy.SmokeTests/DFLegacy.SmokeTests.csproj -c Release \
  -m:1 -nodeReuse:false /p:UseSharedCompilation=false   → 成功，0 警告 0 错误
dotnet run --project tests/DFLegacy.SmokeTests/... --no-build -- --quest-item-grant
  → Quest item grant smoke: 4 assertions, 0 failures
dotnet run --project tests/DFLegacy.SmokeTests/... --no-build（全量）
  → 退出码 0（无 FAIL 行；日志 .tmp/smoke-full.log）
```

构建一度报 “0 errors 生成失败 / Access denied”，是本机
`tests/` 目录 DACL 缺 `WRITE_OWNER` 导致的沙箱问题，已用
`diagnose-windows-sandbox-acl` 修复并验证（`G:/Game/DNF_ACT1/DFLegacy._acl-report2/`），
不是代码错误。

## 3. 辛达出售 +10 强化券

### 3.1 商店定位

- 辛达 = NPC 4（`etc/materialitemwithnpc.tbl` 的 `[npc]` 表）。
- 商店 = `itemshop/equipmentshop2.shp`（`itemshop/itemshop.lst` 索引 3，文件内 `[NPC] 4`），
  当前 7 行 × 7 格，前两格是 3162/3163（火炉/铁匠铺使用券），其余为 `-1`。
- 道具 7274 = `stackable/event/tcg/deed_10equipmentstrengthening.stk`
  （`+10 장비 강화권` / `+10 装备强化券`），`[stackable type] \`[etc]\``、`[price] 10`。

### 3.2 候选 PVF（已生成）

`dist/c2-sinda-ticket-20261117/`（不提交，交付给打包任务）：

| 文件 | 输入 | SHA256 |
| --- | --- | --- |
| `Script.chapter-two.native.pvf` | 客户端原生 v1.0.13（22FA7529…） | 7B084C28… |
| `Script.chapter-two.server.pvf` | 同一内容，服务端容器 | A33A93A4… |
| `verification.json` | 只改 `itemshop/equipmentshop2.shp`，其余 69,398 条逐条相等 | — |

工具在输入上先验证 native/server 内容一致，再在**原地**把第一个 `-1` 换成 7274，
其余字节不动；写回后重开两份输出逐条比对。

注意：本机 `G:/Project/DNF2/Server/resources/Script.pvf`（4A4370BC…）比 v1.0.13
客户端 PVF 少一条 `quest/dflegacy-disabled-quests.lst` 且至少一条道具脚本内容不同，
不是当前客户端的配对资源。**上架时必须同时替换客户端与服务端 PVF**（本候选的两份
就是成对的），不能只换服务端。

### 3.3 配套功能：不可用（需要决策）

静态结论：

- 7274 的 `.stk` 没有 `[use info]`，类型是 `[etc]`；2008 系客户端的可用道具入口按
  类型/使用信息走，`[etc]` 没有“使用”入口。
- 第二章客户端 `DNF.exe`（`.tmp/chapter-two-local-qa/client-a/DNF.exe`）里没有
  `cmp ax/eax, 0x1C6A` 之类对 7274 的常量比较，也没有 `TCG`/`강화권` 字符串 ⇒
  客户端没有对这张券的专门处理。
- 服务端 `USE_STACKABLE` 只带 `slot` + `listType`
  （`EntranceService.cs:19727-19744`），不带目标装备；即使客户端发出来也无法选装备。
- 服务端现有道具使用只实现增强状态、经验书、PVP 经验、技能重置、疲劳恢复、成长药水等
  （`JsonGameStore.cs:4408-5230`），没有强化券处理。

因此**只把 7274 上架 = 玩家买到一件无法使用的道具**。可选方案：

1. 原生补丁 + 服务端命令：在第二章宿主里拦 7274 的双击/使用，弹装备选择并发自定义命令；
   服务端校验后把目标强化到 +10 并消耗券。工作量大（原生 UI + 协议 + 服务端），需单独一波。
2. 先用聊天命令兜底（例如输入“使用强化券”）：服务端消耗 7274、把已装备武器强化到 +10。
   现有客户端即可用，但与原版“使用券”交互不同，需用户确认。
3. 暂不上架，等 1 完成。

本轮未实现任何一种；候选 PVF 已就绪，装上之前必须先选方案。

## 4. 未验证 / 下一步

- [未验证] 真实客户端双击 7274 的行为（静态证据指向“无入口”，但未在客户端里点过）。
- [未验证] 候选 PVF 的客户端商店显示与实际购买（需要装到隔离客户端/服务端）。
- [未验证] 会话级整链路回归（进副本→杀怪→任务道具包→不断线）；本轮只有纯函数用例，
  会话接线靠代码审查。
- [推断] 组队“每人各自掷骰”是原版行为；依据是任务道具为个人掉落，但未用参考服务端抓包对照。
- 下一步（可直接执行）：把 `dist/c2-sinda-ticket-20261117/` 的两份 PVF 与
  `DFLegacy.Server.exe` 一起装到隔离运行目录，用测试账号：接 917 任务→杀 Tau→
  观察物品包/任务进度；再开 7274 的商店与双击行为。
