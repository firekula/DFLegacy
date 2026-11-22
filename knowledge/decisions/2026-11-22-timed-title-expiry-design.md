# 限时装备/称号到期回收设计（2026-11-22）

状态：已实施。验证结果见 [限时装备/称号到期回收结果](2026-11-22-timed-title-expiry-result.md)。范围限第二章发布线，交付本机测试包，不发布、不上传。

## 背景与根因

用户报告魔盒开出的限时称号到期后不消失。核对源码与 PVF：

- 称号是装备（`ItemDefinition.IsTitle`），占装备槽 10，不属于 `ItemInventoryCategory.Avatar`。
- 现有到期回收 [AvatarLifetimePolicy](../../src/DFLegacy.Server/AvatarLifetimePolicy.cs) 只处理
  `InventoryCategory.Avatar`；称号不进入 `NormalizeCharacter`。
- [ItemCatalog](../../src/DFLegacy.Server/ItemCatalog.cs) 没有解析 `[usable period]`，所以服务端不知道
  某件称号模板是限时的。
- 魔盒发放 [RandomBoxOpeningPlanner](../../src/DFLegacy.Server/RandomBoxOpeningPlanner.cs) 只创建附件并放入
  背包，不写任何到期时间。
- 本机客户端 PVF 扫描到 23 个 `title name` 模板带 `[usable period]`，期限为 15/30/45/60/90 天。
- 因此不是“到期没删”，而是**发放时没有记录截止时间**，现有循环也不会处理称号。

## 范围

纳入：

- 所有非堆叠装备（`InventoryCategory.Equipment`）模板带 `[usable period] > 0` 的实例，包括称号。
- 背包、装备槽、仓库、系统邮件附件。
- 魔盒、GM 邮件、商城、任务、掉落等所有经过存档归一化的发放路径。

不纳入：

- 堆叠消耗品（同一叠可能有多件不同到期时间，拆分语义未验证）。
- 客户端称号倒计时显示；本轮只保证服务端到期移除。
- 装扮现有生命周期逻辑的行为改变；装扮继续按原方式处理。

## 数据模型

- `ItemDefinition` 增加 `int? UsablePeriodDays`，由 `[usable period]` 解析。
- 复用现有 `CharacterItemRecord.AvatarExpiresAtUnixSeconds` 和 `ExpiredAvatarInstanceIds` 作为
  “限时物品”通用字段；JSON 键不变，避免存档迁移；代码注释说明名称是历史遗留。
- `CharacterItemWireProjection` 对装备（含称号）继续发送品质种子，不把 `add_info` 改成剩余秒。
  客户端倒计时留待真机确认后再做。

## 锚定与回收

### 锚定

在 `AvatarLifetimePolicy.NormalizeCharacter` 中把“只处理装扮”扩展为“装扮 + 带 `UsablePeriodDays` 的装备”：

- 第一次看到没有截止时间的限时装备时，锚定
  `AvatarExpiresAtUnixSeconds = now + UsablePeriodDays * 86400`。
- 兼容旧装扮 `AvatarRemainingSeconds > 0` 的锚定顺序：先按剩余秒，再按模板天数。
- 已存在的限时称号在升级后第一次归一化时获得一个完整期限，然后正常到期；这是有意的迁移行为。

### 回收

- `NormalizeCharacter` 从背包、装备、仓库、装扮栏、宠物栏、邮件中移除到期实例，并写入
  `ExpiredAvatarInstanceIds` 墓碑。
- `MergeForActor` 扩展为同时合并 `Inventory`、`Equipment`、`Warehouse` 中的限时物品，非限时物品保留
  会话内的值；`AvatarInventory`/`Mailbox` 继续用权威记录替换。
- `EntranceService` 的会话快照加入 `mainInventory`/`warehouseInventory`；结算后同步这两个字典。
- `AvatarLifetimeSessionRefresh.PublishAsync` 增加主背包和仓库刷新回调；装备槽到期继续触发现有
  外观/属性/同屏刷新。
- `GetExpiryWait` 已经覆盖背包/装备/仓库/邮件，会自动按最近到期时间唤醒，不需要新增定时器。

## 测试

新增 `TimedTitleLifetimeSmokeTests`：

1. 目录解析：带 `[usable period] 30` 的称号模板得到 `UsablePeriodDays == 30`；无该标签的称号为 null。
2. 首次保存：带限时称号的角色保存后，实例获得 `now + 30*86400` 的截止时间，`InstanceId` 稳定。
3. 到期回收：推进测试时钟过截止时间，`SettleAvatarExpirationsAsync` 后称号从背包/装备/仓库/邮件移除，
   墓碑记录该实例；永久称号保留。
4. 魔盒路径：魔盒奖励中的限时称号经过 `RandomBoxOpeningPlanner` + `SaveCharacterItemSpacesAsync` 后
   带截止时间。
5. 既有 `AvatarLifetimeSmokeTests` 必须保持通过，证明装扮行为未回归。

真机验收：把测试存档中一件限时称号的截止时间改成 1 分钟后，登录观察是否消失；不需要等 15 天。

## 风险与限制

- 现有无限期称号会在升级后获得一个完整期限；需要向用户说明。
- 交易/邮件/仓库是否完整保留截止字段需要测试覆盖；现有装扮测试提供了同类路径参考。
- 客户端可能不显示称号倒计时；本轮只保证到期消失。
- 全量 SmokeTests 在发布线存在无关的 `IdleCeraRefreshSmokeTests` 环境失败；验收时分开记录。

## 交付

- 在 `fix/avatar-destroy-v1017` 基础上新建 `fix/timed-title-expiry` 分支，保留两个独立提交。
- 本机测试包同时包含装扮销毁和限时称号到期。
- 不修改 `LauncherVersion.txt`，不生成公开发行包，不推送/上传。
