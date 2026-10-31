# 副本奖励幂等存档提交设计

> 状态：待用户审阅。本文只定义存档层契约，不修改生产代码，也不接入翻牌流程。
>
> 目标：为组队副本奖励提供可恢复、可审计的幂等提交边界，使同一奖励操作不会因重复请求、跨会话自动翻牌或写入异常而重复发放。
>
> 证据来源：现有 `JsonGameStore.SaveCharacterInventoryAsync`、`SaveLockedAsync` 与 `EntranceService.SaveClearRewardEquipmentAsync` 的静态代码检查；证据等级为 `[静态]`。写盘掉电 durability 和真实崩溃恢复仍需后续实验标为 `[未验证]`。

## 1. 问题与范围

### 1.1 已确认问题

当前 `SaveCharacterInventoryAsync` 在 `_gate` 内执行账户/角色/容量校验，修改 `_accounts`，然后调用 `SaveLockedAsync`；失败统一返回 `null`，成功返回更新角色。[静态：`JsonGameStore.cs:3192-3267`]

`SaveLockedAsync` 将完整状态写入 `StorePath + ".tmp"`，随后 `File.Move(..., overwrite: true)` 替换正式文件；写入、关闭或替换阶段异常时，调用方无法判断正式文件是否已替换。[静态：`JsonGameStore.cs:6107-6129`]

现有 `EntranceService.SaveClearRewardEquipmentAsync` 在保存成功后立即更新会话内存金币/背包；它是会话局部函数，无法安全承担多会话共享奖励的幂等提交。[静态：`EntranceService.cs:1543-1651`]

### 1.2 本设计包含

- 同一 JSON 快照内的奖励操作账本；
- 幂等 key 与 payload fingerprint；
- `Committed` / `Rejected` / `Unknown` 三态结果；
- 明确拒绝与写入未知的区分；
- 重启后恢复已提交账本；
- 未知状态对旧内存写入口的隔离；
- 单元、异常注入、重启恢复测试设计。

### 1.3 本设计不包含

- 翻牌 `EntranceService` 接线；
- `CMD 74` 构造或队伍广播；
- Special Item Routing；
- 普通掉落 `NOTI 39`；
- 声称操作系统掉电后 exactly-once durability 已被证明。

## 2. 核心数据模型

扩展持久化根对象 `JsonGameStoreState`：

```csharp
public sealed record DungeonRewardLedgerEntry(
    string IdempotencyKey,
    string PayloadFingerprint,
    Guid CharacterId,
    long DungeonGeneration,
    byte PartySlot,
    byte CardType,
    byte CardColumn,
    DungeonRewardLedgerStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    ushort? RewardItemId,
    int GoldDelta,
    string? RejectionCode,
    string? UnknownReason);

public enum DungeonRewardLedgerStatus : byte
{
    Committed = 0,
    Rejected = 1,
    Unknown = 2
}
```

`JsonGameStoreState` 增加：

```csharp
public List<DungeonRewardLedgerEntry> DungeonRewardLedger { get; init; } = [];
```

老存档缺少该字段时按空列表读取。账本与账户状态必须在同一个 JSON 快照中保存，不能用第二个独立文件。

### 2.1 IdempotencyKey

key 必须包含不可变业务身份：

```text
party/dungeon settlement identity
+ dungeon generation
+ member character id
+ party slot
+ card type
+ card column
```

同一奖励操作重试必须生成完全相同的 key；换角色、换副本、换 generation 或换卡列必须生成不同 key。

### 2.2 PayloadFingerprint

fingerprint 覆盖所有影响奖励的输入：

```text
character id
settlement generation
card type/column
reward item ids and counts
free gold / paid cost
warehouse expected capacity
planned inventory digest
```

同 key 但 fingerprint 不同必须返回 `Rejected("idempotency-payload-mismatch")`，不得修改账户或账本已有 committed 结果。

## 3. Store API

新增请求和结果：

```csharp
public sealed record DungeonRewardCommitRequest(
    string IdempotencyKey,
    string PayloadFingerprint,
    string UserName,
    Guid CharacterId,
    long DungeonGeneration,
    byte PartySlot,
    byte CardType,
    byte CardColumn,
    int TargetGold,
    IReadOnlyList<CharacterItemRecord> PlannedInventory,
    IReadOnlyList<CharacterItemRecord> PlannedEquipment,
    IReadOnlyList<CreateCharacterMailRequest> GeneratedMails,
    ushort? WarehouseCapacity,
    ushort? ExpectedWarehouseCapacity);

public readonly record struct DungeonRewardCommitResult(
    DungeonRewardLedgerStatus Status,
    CharacterRecord? UpdatedCharacter,
    ushort? RewardItemId,
    int GoldDelta,
    string? RejectionCode,
    string? UnknownReason);

public Task<DungeonRewardCommitResult> CommitDungeonRewardAsync(
    DungeonRewardCommitRequest request,
    CancellationToken cancellationToken = default);
```

结果语义：

- `Committed`：账本和账户更新已写入同一正式快照；重复同 key/fingerprint 不再规划或写盘，并返回已记录摘要/角色。
- `Rejected`：可证明账户未修改的业务拒绝，例如角色不存在、交易交付 pending、容量 expected 不匹配、fingerprint 冲突；可安全换新 key 或修正后重新请求。
- `Unknown`：进入写入阶段后发生异常/取消，无法证明正式文件是否替换；禁止自动重试、禁止释放 reservation、禁止再次发奖。

## 4. 提交算法

所有步骤在 `JsonGameStore._gate` 下协调；不持有 `DungeonInstance.Gate` 跨 await。

### 4.1 账本查找

1. 校验 key/fingerprint 非空且格式合法。
2. 查找同 key：
   - `Committed` + fingerprint 相同 → 返回 `Committed`，不改账户；
   - `Rejected` → 返回原拒绝结果；
   - `Unknown` → 返回 `Unknown`，禁止自动重试；
   - fingerprint 不同 → `Rejected(idempotency-payload-mismatch)`。
3. 未找到则继续业务校验。

### 4.2 明确拒绝阶段

在修改 `_accounts` 前完成：

- 账号/角色存在；
- `_pendingTradeDeliveries` 不包含该角色；
- 仓库 expected capacity 校验；
- 物品、邮件和金币规划有效；
- 规划结果不会产生不允许的负数或溢出。

明确拒绝必须不修改账户；可写入 `Rejected` ledger 记录，但不能把失败规划写成 `Committed`。

### 4.3 候选快照

不要先修改真实 `_accounts` 再调用原有 `SaveLockedAsync`。构造隔离候选状态：

```text
candidate accounts
+ candidate DungeonRewardLedger[Committed]
```

使用专用 snapshot-save primitive 写入临时文件并替换正式文件。成功完成替换后，才安装 candidate 到内存并返回 `Committed`。

### 4.4 写入异常

以下任一阶段进入 `Unknown`：

- 序列化中异常；
- 临时文件创建/写入/关闭异常；
- 正式文件替换异常；
- 替换调用后响应返回前异常；
- 写入阶段 cancellation。

在 `Unknown` 情况下：

- 不自动重试；
- 不释放副本卡位 reservation；
- 不把本地会话奖励标记为已领取；
- 不允许旧内存继续覆盖正式文件；
- 记录 `UnknownReason` 和 key，等待恢复流程。

### 4.5 Unknown quarantine

因为旧内存可能与正式文件不同，Store 进入写入隔离：

```text
storeWriteQuarantined = true
```

所有会修改账户的普通保存入口在隔离期间拒绝或进入明确失败，直到：

1. 重新读取正式 JSON；
2. 检查 ledger 中 key 的最终状态；
3. 若找到 `Committed`，安装正式状态并解除隔离；
4. 若没有 committed 记录，人工/显式恢复动作决定是否重新构造新 key；
5. 不能仅凭 `.tmp` 存在判断成功。

## 5. 重启恢复

启动时：

- 读取正式 JSON 的 `DungeonRewardLedger`；
- `Committed` 记录可安全返回 committed 摘要；
- `Rejected` 记录可返回原拒绝；
- `Unknown` 记录保持人工恢复状态，不自动发奖；
- `.tmp` 不作为成功证据；
- 缺失 ledger 字段的旧存档按空列表迁移。

当前设计不能声称掉电后 exactly-once：`File.Move` 的原子可见性与 durable flush 不是同一保证；必须单独做崩溃/掉电实验后才能升级证据等级。[未验证]

## 6. 与翻牌接线的边界

翻牌接入必须按以下顺序：

```text
party reservation
→ 构造 member-specific reward request
→ CommitDungeonRewardAsync
→ Committed 才安装该成员会话内存
→ 所有必要成员 committed 后构造一次最终 CMD 74
→ 发送一次 party snapshot
```

- `Rejected`：只释放未确认的临时尝试，不释放已确认 reservation；不发奖。
- `Unknown`：保留 reservation/pending，禁止再次 grant 和自动重试；不发布可能错误的 owner 快照。
- generation 在 await 期间失效：
  - `Committed`：保留旧 generation 权益，但禁止向新 generation 广播；
  - `Unknown`：保留 pending；
  - `Rejected`：记录明确拒绝。
- 单人继续旁路旧 `SaveClearRewardEquipmentAsync`，直到另行证明 byte-level 零回归。

## 7. 测试矩阵

### 7.1 Store 单元/回放

- 同 key 首次成功：一次账户变化，一条 committed ledger；
- 同 key 同 fingerprint 重放：返回 committed，不二次规划/写盘；
- 同 key 不同 fingerprint：rejected，账户不变；
- 角色/账号不存在、pending trade、容量冲突：rejected；
- 序列化异常、临时文件异常、替换前异常、替换后异常：unknown，不自动重试；
- unknown quarantine 阻止旧内存普通保存；
- 重启读取 committed ledger：重放不变更；
- 只有 `.tmp` 或 unknown：不自动发奖；
- 老存档无 ledger 字段：正常迁移。

### 7.2 翻牌协调回放

- 双人 idle：一次 reservation/一次 commit/member，一份一致 CMD 74；
- 手动+idle：手动 reservation 保留，自动补另一成员；
- 重复手动和重复 timer：不二次保存/发奖；
- Paid 单独扣费且不被自动流程购买；
- 成员离队不重置剩余 reservation；
- generation 失效不向新 generation 发布；
- save unknown 不自动重试、不释放 reservation；
- solo coordinator 未调用且旧 packet snapshot 一致。

## 8. 明确不包含

- 不实现 Special Item Routing；
- 不改变普通掉落；
- 不声称 OS 掉电 exactly-once；
- 不在未有 store commit 结果前接入队伍翻牌；
- 不修改用户工作区权限或绕过现有测试失败。
