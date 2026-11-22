# 限时装备/称号到期回收结果（2026-11-22）

状态：源码、自动化测试和本机构建完成；真机客户端到期消失与倒计时显示待用户验证。未发布、未上传。

## 基线与改动

- 分支：`fix/timed-title-expiry`，基于 `fix/avatar-destroy-v1017`（提交 `23f87fb`）。
- Worktree：`.tmp/worktrees/avatar-destroy-v1017`。
- 涉及文件：
  - `src/DFLegacy.Server/ItemCatalog.cs`
  - `src/DFLegacy.Server/AvatarLifetimePolicy.cs`
  - `src/DFLegacy.Server/AvatarLifetimeSessionRefresh.cs`
  - `src/DFLegacy.Server/EntranceService.cs`
  - `tests/DFLegacy.SmokeTests/TimedTitleLifetimeSmokeTests.cs`
  - `tests/DFLegacy.SmokeTests/Program.cs`

## 根因

称号是装备（`ItemDefinition.IsTitle`），不属于 `ItemInventoryCategory.Avatar`；现有到期回收只处理装扮，
`ItemCatalog` 也没有解析 `[usable period]`，魔盒发放不写到期时间。服务端从未记录限时称号的截止时间，
所以到期后不会消失。

## 实现

1. `ItemCatalog` 解析 `[usable period]`，`ItemDefinition` 增加 `UsablePeriodDays`。
2. `AvatarLifetimePolicy.NormalizeCharacter` 的追踪范围从装扮扩展到“装扮 + 带 `UsablePeriodDays` 的装备”：
   - 第一次归一化时锚定 `now + UsablePeriodDays * 86400`；
   - 从背包、装备、仓库、装扮栏、宠物栏、邮件移除到期实例并写墓碑；
   - 旧 `AvatarRemainingSeconds` 锚定顺序保持优先。
3. `MergeForActor` 合并主背包、装备、仓库中的限时物品；非限时物品保留会话值。
4. 会话结算同步 `mainInventory`/`warehouseInventory`，并通过 `AvatarLifetimeSessionRefresh` 刷新主背包、
   仓库、装备/外观和邮件。
5. 客户端称号 `add_info` 仍发品质种子；本轮不做称号倒计时显示。

## 验证结果

| 验证 | 命令/环境 | 结果 |
| --- | --- | --- |
| 服务端 Release 构建 | `dotnet build ... -c Release` | 0 错误、0 警告 |
| 限时称号定向测试 | `--timed-title` | `Timed title lifetime smoke: 0 failures` |
| 装扮生命周期回归 | `--avatar-lifetime` | `Avatar lifetime smoke: 0 failures` |
| 装扮销毁回归 | `--avatar-destroy` | `Avatar destroy smoke: 0 failures` |
| 魔盒回归 | `--chapter-two-random-box` | `Chapter Two random box smoke: 0 failures` |
| 金币销毁回归 | `--town-gold-discard` | `Town gold discard smoke: 0 failures` |
| 地面丢弃回归 | `--active-drop` | `Active drop smoke: 0 failures` |
| 本机服务端发布 | `dotnet publish ... --self-contained false` | 退出码 0，产物在 `.tmp/avatar-destroy-v1017/server-publish` |

### 测试覆盖

`TimedTitleLifetimeSmokeTests` 验证：

- 带 `[usable period] 30` 的称号模板解析为 `UsablePeriodDays == 30`；无标签称号保持永久。
- 首次保存后，背包/装备/仓库中的限时称号获得 `now + 30 天` 截止时间；永久称号没有截止时间。
- 系统邮件中的限时称号在创建时锚定。
- 魔盒奖励中的限时称号经过 `RandomBoxOpeningPlanner` + 普通保存后带截止时间。
- 推进时钟过期后，称号从背包/装备/仓库/邮件移除，墓碑包含对应实例；永久称号保留。
- 新建 store 重新加载不会复活过期称号。

### 全量 SmokeTests

未完成。发布线全量运行在 `IdleCeraRefreshSmokeTests` 的固定端口 `127.0.0.44:47001` 连接阶段失败，
与本次改动无调用关系；本轮只记录定向回归，不宣称全量通过。

## 迁移行为

升级后，已存在的限时称号会在第一次归一化（登录/保存/启动结算）时获得一个完整期限，然后正常到期。
这是有意行为，因为旧记录没有原始发放时间，无法补算已流逝时间。

## 真机验收步骤

1. 用本机测试服务端（`server-publish`）和测试账号登录。
2. 在测试存档中把一件限时称号的 `AvatarExpiresAtUnixSeconds` 改成当前时间 + 60 秒；
   或等魔盒开出一件限时称号后直接改存档。
3. 登录后观察称号仍在；约 1 分钟后服务端结算，客户端背包/装备/仓库应刷新，称号消失。
4. 重登确认不会复活；服务端日志出现 `Settled limited item lifetime`，墓碑包含该实例。
5. 同时确认永久称号、普通装备、金币和邮件正文不受影响。

## 未验证项

1. 真实客户端上称号到期后的实际刷新和消失。
2. 客户端是否显示称号剩余时间/倒计时；本轮未改 `add_info`。
3. 交易/邮件往返是否在所有路径保留截止时间；现有测试覆盖保存、系统邮件和魔盒，玩家交易未单独覆盖。
4. 全量 SmokeTests 未完成（无关的 `IdleCeraRefreshSmokeTests` 环境失败）。

## 下一步

1. 完成真机到期消失验收。
2. 如需要客户端倒计时，再单独确认称号 `add_info` 语义并设计。
3. 与装扮销毁一起决定是否纳入下一个正式版本。
