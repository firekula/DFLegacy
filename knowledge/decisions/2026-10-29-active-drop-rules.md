# 副本主动丢弃：官服规则、组队可见性与金币

本轮 U04 在独立 worktree `D:/codex_worktree/active-drop-rules/DFLegacy`、基线 `626890396dfba6eeed2a345ea04ca3981252efa7` 实现。没有改动原始 ELF/PVF/EXE、客户端、运行配置或部署目录；没有启动真实游戏服务。本轮的功能证据是离线回归，双客户端实机验收由主任务集成后执行。

## 原字节依据

官服 ELF SHA256 `8859C99D8290DC5FB58151444138CF2697B2BFFF12D47D6159596A03F20B5261`；ACT1 EXE SHA256 `6B4850BEAF67EC61ADB04AE17F119F16278DD05E97B8FFCA871A9BDB547DE0D5`，均在只读取证中重新核对。完整报告和新派生汇编在本机 `G:/Game/DNF_ACT1/DFLegacy/.tmp/active-drop-rule-audit-20261029/`，不纳入源码。

- `DisPatcher_DropItem::process@081C2F94` 的普通副本分支 `081C30C0` 调用 `CParty::drop_item@085A63F4`，`085A70C2` 调用 `CBattle_Field::drop_item@0830DB84`，插入当前地图共享物品表。
- `085A72B0` 对成功 NOTI40 调用 `CParty::send_to_party@0859D14E`；后者逐槽检查有效成员并发送。随后 `085A7360` 才向原丢弃者私发 CMD50 背包确认。这不是“物主私有可见”的实现。
- 主动丢弃写 `source=0`（物品 `085A6A42`，金币 `085A64E5`）；自然怪物生成写 `source=1@0830B5AB`。允许主动丢弃的普通物品在 `get_item@085A3B98` 的 `085A57E2→085A5C4E` 走当前拾取者直取，不要求原丢弃者，也不改为自然掉落骰子。
- ACT1 `00422CC3..00422E98` 读 17 字节 NOTI40，建立地面 actor。前 u16 丢弃者 UID 读后没有本机 UID 门控，也没有 owner 尾字段。保留当前 `u16 uid,s16 x,s16 y,u16 ground,u16 item,u8 attr,u32 addInfo,u16 durability`，没有照搬较新官服的 u32 item 或附加尾字段。

## 实现与事务边界

`DungeonItemDropPlanner` 只增加主动丢弃判据；全局 `CharacterItemSealing.CanTrade` 及交易/邮件规则保持原样。模板 rarity>2 回13；title/avatar 回23；Free、SealingTrade 通过附件门控，Sealing 仅实例 SealState==1 通过，其他已知附件拒绝。原有分类/格位、数量及交易托管保护继续生效。

金币明确识别 CMD50 的 space0/slot0：仅1..1000，超过1000回70，不足余额/非法数量回17。plan 保留物品背包，只减少金币；地面记录为 `IsGold=true,itemId=0,RoutingSource=0`。CMD50 仍用 ACT1 已捕获载荷，不添加较新服务器的安全 byte。

`DungeonActiveDropTransaction` 是实际 CMD50 handler 使用的持久化与发布入口：先分配不可见、不可拾取的地面预留，调用原 `SaveCharacterItemSpacesAsync`，成功才激活并发 NOTI40，最后只向自己发 CMD50 ACK。保存返回空或抛异常时，按原对象引用回收预留，不会误删 reset 后复用同 GroundId 的新对象。物品品质 seed、强化/耐久/封印和实例身份继续保留。

组队通过 `BroadcastActiveDropAsync` 使用现有 CrossSessionDelivery 的坏会话保护；包括丢弃者，每端只发一个 actor。独立审查复现了原先“检查后 await Send”的窗口：若新房 NOTI29 先抢到发送锁，旧房40仍会随后写出。因此增加明确的条件发送句柄，四处实际入场注册和单人丢弃均接入 `SerializedWriteStream.WritePacketIfAsync`：取得原有 `_writeGate` 后才检查地面对象、激活时同锁捕获的房间代次，以及当前成员引用，再对 inner 写完整包并 flush。实例状态锁没有跨 await；条件发送不允许退回普通 Send。即使离房后回到同一坐标，旧代次也不能发布旧 actor。保存中换房保留旧房地面物，私有背包 ACK 仍准确发送。

金币拾取复用已有预留、唯一 claim、持久化分账和移除流程；source0 一并传给经济账本，不制造自然掉落来源的额外收益。队友可拾取，均分及拾取者余数保持现有金币规则。

真实临时快照文件锁回归发现原 `SaveCharacterItemSpacesAsync` 在写盘失败前已修改内存记录。经主任务明确授权，仅该 API 增加失败回退：`SaveLockedAsync` 在原子 `File.Move` 返回后标记已提交，只有未到该边界才恢复原角色记录。独立审查又以真实 ILogger 复现：File.Move 后“Saved emulator state”诊断日志抛 IOException，会让事务误判未提交并删掉预留。修复将这一条已提交诊断日志与该保存 API 的成功结果隔离，正常返回已提交角色，使事务继续激活可拾取掉落并发送40/ACK；没有重试 save 或回退已持久化数据。其他保存 API 保持原日志抛错行为，提交前异常仍抛出并回收预留。未知提交结果不扩大成可撤销已持久化数据的承诺。

## 明确的适配界限

- 官服 wire `get_unique_id` 为 u16、有效性 `GetUID` 为复合32位会话 ID、`getCurCharacNo` 为角色号；三者不混用。当前仍用 localUserId/AreaUserId 发包，以 Guid 做本地身份仲裁。
- 官服 `UpgradeSeparateInfo` 交易限制 bit、较新 mode/security/service-lock 条件没有伪映射到 ACT1 字段。ACT1 的 State 高3位用于重新封装次数，不能冒充较新交易限制 bit。
- 不猜造较新 space7/Creature 类型5的全套映射；保留工程现有 ACT1 Creature 栏位校验。未知附件类型仍拒绝，没有照搬原跳转表对未知枚举的宽松 fallthrough。
- 本轮不更改自然掉落概率、随机序列、史诗矩阵或 U03。

## 回归与后续验收

TDD 中，规则回归先捕获4项旧行为失败；金币规划捕获7项失败；事务与投递捕获7项失败；真实提交前文件锁捕获1项余额残留失败；另补 detached tutorial 房间坐标的1项 RED，再分别修改实现取得 GREEN。

审查修复另保留 `queued-room-red.log` 的2项真实 SerializedWriteStream/TCS 失败，以及 `post-commit-log-red.log` 的1项完整事务失败。最终回归覆盖 party/solo、同坐标重访、成员重挂、缺条件句柄禁止 fallback、持真实发送锁后才运行 predicate、真实已提交磁盘/内存扣款和可拾取地面/40/ACK；同时证明其他保存 API 仍会抛同一日志异常。`review-fixes-*.log` 为修复后的门与构建输出。

定向门：`--active-drop`、`--pickup-removal`、`--party-gold`、`--loot-routing`、`--store-write-transaction` 均退出0、0 failures。新增用例覆盖白/蓝/rarity2、rarity3拒绝、title/avatar、精确封印及 SealingTrade、堆叠分拆、金币边界、原数据不变、未保存预留不可拾取、双成员/单人唯一 actor、私有 ACK、异步断开成员、换房、旧 GroundId 回滚、队友唯一领取/两端移除、真实存盘金币守恒与失败内存/磁盘余额。

托管 Server Release build：0 warnings、0 errors。解决方案级 `dotnet build DFLegacy.Emulator.slnx --no-restore` 未通过：5个 native vcxproj 缺 VS C++ 导入，3个无关 relay/launcher 项目缺未还原 assets；未为此构建或修改 native 代码。

验证日志保存在本 worktree `.tmp/active-drop-validation/`。默认完整 smoke 在主任务确认允许现有临时双栈/loopback 测试夹具后执行，所有 TEMP/TMP 均重定向到本 worktree。首轮在无关 TradeSmokeTests.CreateCharacterAsync 的原有 File.Move 遇到 UnauthorizedAccessException；新短临时目录的第二轮到达 CeraGiftSmokeTests.Rejections，又在 SendCharacterMailAsync 的 File.Move 遇到相同错误。两轮均退出-532462766，保留 full-smoke.log/full-smoke-retry.log；没有扩改其他保存 API 或为绕过环境问题加入全局文件重试，也没有把定向通过冒充完整套件通过。

两项审查修复后的第三轮完整默认 smoke 仍在无关 FriendSmokeTests.CheckFriendStorageAsync→CreateAccountAsync 的 File.Move 遇到同一 UnauthorizedAccessException，退出-532462766，保留 `review-fixes-full-smoke.log`。最终五项聚焦门仍全部0 failures，托管 Server Release仍0 warnings/0 errors；完整套件未取得 GREEN。

主任务随后集成为托管源码 `65e53d4`，独立构建0警告／0错误，五项聚焦门再次全绿。主工作区的完整套件成功跑至结尾，发现一项旧夹具冲突：`test-3176.stk` 为 `[sealing]`，原测试却期望实例 SealState=3 可以丢弃。现保留该实例且断言拒绝、源数量和封印状态不变，再将允许路径的夹具设为官服要求的状态1，继续验证分拆数量及State／Durability／SealState保留；没有放宽产品策略。再次构建后完整默认套件退出0，输出 `All DFLegacy protocol smoke tests passed.`。主任务原始RED与GREEN分别保存在 `G:/Game/DNF_ACT1/DFLegacy/.tmp/active-drop-acceptance-20261029/verification-managed/root-full-smoke.log`、`root-full-fixture-smoke.log`，独立工作区前述三次文件访问失败的历史记录仍保留。

待主任务集成后的双客户端验收：同房 A 丢金币1/1000、白装、蓝装、自由堆叠物；两端恰有同 GroundId 的一个 actor，只有 A 收 CMD50 ACK；B 拾取，仅一份物品入账，两端地面消失；金币核对双方余额与余数。反向 A/B、自己拾取、跨实例隔离、失败余额、1001拒绝、封印/title/rarity3拒绝也需实测。没有把这些静态/离线证据称为新实机 GREEN。
