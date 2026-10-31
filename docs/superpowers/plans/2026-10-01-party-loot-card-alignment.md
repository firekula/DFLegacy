# 组队掉落与翻牌官方对齐实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use subagent-driven-development (recommended) or executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在不破坏当前双人同图通关和单人报文序列的前提下，把结算翻牌收敛为官方 party-level 状态机，并将普通多人掉落从当前共享先到先得模型收敛为服务端统一计分、胜者仲裁和全队 `NOTI 39` 广播。

**Architecture:** 保留 `DungeonInstance` 作为队伍共享状态唯一真源；会话层只解析命令、调用实例仲裁、按收件人投递结果。翻牌和普通掉落各自拥有实例级状态与 generation，锁内只做状态转换，锁外发送包和写存档。Special Item Routing (`CItem +0x79/+0xBD/+0xC0`、NOTI 281/282、CMD 301) 本计划不接入第一层流程，因为当前 ACT1 PVF 未发现直接 routing 样本，当前客户端主分发也不覆盖这些协议。[静态：`knowledge/decisions/2026-10-01-pickup-and-routing-closure.md:54-97`]

**Tech Stack:** C#/.NET 10、现有 `DungeonInstance`/`DungeonPartyService`、`GameProtocolEngine`、`DFLegacy.SmokeTests`、现有双客户端真机日志与逐包追踪。

**Spec:** `docs/superpowers/specs/2026-10-01-dungeon-official-alignment-design.md`

## 执行安全门（自审发现，优先于下方示例）

**本文仍为草案，不可照示例直接执行。** 自审发现：Task 1/2 的成员注册、generation、结果类型与奖励存档失败处理还未定义一致；Task 3 的简化计分丢失了官服轮转加40和参与开关，不能宣称官方等价；Task 4 将尾字段默认为0没有客户端契约依据；Task 5 应在掉落模型前完成。以上示例仅表示测试意图，不是最终接口。

下一步先完成翻牌独立计划的接口和存档事务定义，只处理已经确认的卡位唯一归属问题。**Task 3–5 暂停实现**：普通掉落的协议尾字段、分配启用条件和事务投递必须先确定，否则维持当前掉落路径，不以猜测替换可运行行为。Special Item Routing 继续延期。

## Global Constraints

- **单人零回归**：单人实例的报文条数、顺序和字节不新增、不改序；所有新增队伍逻辑必须由 `IsParty == true` 门控。[静态：`final-fix-report.md:163-171`]
- **队伍唯一真源**：房间、成员 slot、掉落仲裁、翻牌 owner 和 settlement generation 不得同时由会话局部变量与实例分别维护。
- **锁内不 await**：实例锁内只修改状态并返回待发送描述；`WriteAsync`、跨会话发送、存档写入在锁外完成。[静态：`DungeonInstance.cs:432-495`]
- **普通掉落与 Special Item Routing 分离**：本轮只实现普通 `CParty::get_item` 计分/`NOTI 39`；不凭空生成或广播 281/282/301。
- **证据边界**：`NOTI 39` 两个尾字段只按已证实宽度和顺序实现，未验证字段名称不改变为自定义语义。
- **每个事件幂等**：同一 room generation、ground item、card selection 或 settlement generation 的重复输入不得重复奖励、广播或清理。
- **验证顺序**：模型 smoke → 协议字节回放 → Release build → 双客户端真机；不能用一次真机成功代替自动化断言。

---

## 文件/模块分工

计划修改范围固定为：

- `src/DFLegacy.Server/DungeonInstance.cs`：新增共享普通掉落仲裁状态和 party-level 翻牌状态；只负责锁内状态转换。
- `src/DFLegacy.Server/DungeonPartyService.cs`：增加按实例/成员投递统一掉落广播的薄封装，复用现有坏通道隔离。
- `src/DFLegacy.Server/DungeonRoomSettlementState.cs`：保留单人会话结算镜像；组队路径只作为兼容投影，不再作为自动翻牌真源。
- `src/DFLegacy.Server/EntranceService.cs`：CMD 74、自动选牌、拾取命令接线；删除会话级组队自动补牌和独立掉落仲裁，不重写无关入口。
- `src/DFLegacy.Protocol/GameProtocolEngine.cs`：仅在现有构建器字段不足时补充 `NOTI 39` 构建器；保持已确认的 `u16,u16,u8[4],u16,u16,u8` 顺序。
- `tests/DFLegacy.SmokeTests/DungeonPartySmokeTests.cs`：模型、广播、幂等、单人隔离和变异测试。
- `tests/DFLegacy.SmokeTests/`：如需独立测试构建器，新增小型协议测试文件，不把会话 socket 接线伪装成已覆盖。

不修改：`PvfArchiveReader.cs`、PVF 原始文件、Special Item Routing 代码（本轮没有生产 routing 实现）、城镇/好友/交易模块。

---

### Task 1: 建立队伍级翻牌状态模型

**Files:**
- Modify: `src/DFLegacy.Server/DungeonInstance.cs`：新增 `PartyCardSelectionState` 及其锁内方法。
- Modify: `src/DFLegacy.Server/DungeonRoomSettlementState.cs`：注明组队状态由实例持有，会话对象仅作本地投影。
- Test: `tests/DFLegacy.SmokeTests/DungeonPartySmokeTests.cs`

**Interfaces:**
- Consumes: 实例成员 slot、`DungeonClearReward`、当前 settlement generation。
- Produces: `TryRecordCardSelection(Guid memberId, byte cardType, byte cardIndex, out CardSelectionOutcome)`、`TryCompleteCardSelection(out CardCompletionOutcome)`、`CaptureCardSnapshot()`、`ResetCardsForGeneration(long generation)`。

- [ ] **Step 1: 写失败测试，钉住两成员共享 owner 表**

```csharp
var state = new PartyCardSelectionState(memberCount: 2, generation: 7);
check(state.TryRecordCardSelection(alpha, cardType: 0, cardIndex: 0, out _), "alpha selection accepted");
check(!state.TryRecordCardSelection(bravo, cardType: 0, cardIndex: 0, out _), "same card cannot be claimed by bravo");
check(state.GetOwner(cardType: 0, cardIndex: 0) == 0, "card owner is alpha slot");
```

还要覆盖：重复选择幂等、非法 card type/index 拒绝、旧 generation 拒绝、成员离开后只对剩余有效 slot 补全。

- [ ] **Step 2: 运行 smoke 确认测试先失败**

Run:

```powershell
dotnet run --project tests/DFLegacy.SmokeTests/DFLegacy.SmokeTests.csproj -c Release
```

Expected: 新增的 party card state 检查失败，因为类型和共享 owner 表尚不存在。

- [ ] **Step 3: 实现最小状态对象**

实现要求：

```csharp
internal sealed class PartyCardSelectionState
{
    internal PartyCardSelectionState(int memberCount, long generation);
    internal bool TryRecordCardSelection(Guid memberId, byte cardType, byte cardIndex, out CardSelectionOutcome outcome);
    internal bool TryCompleteCardSelection(IReadOnlyList<PartySlotHandle> members, out CardCompletionOutcome outcome);
    internal CardSnapshot CaptureCardSnapshot();
    internal bool IsComplete { get; }
}
```

状态必须由 `DungeonInstance.Gate` 保护；不要在状态类内部 await 或发送包。自动补全只能从共享 owner 表读取和写入一次。

- [ ] **Step 4: 运行模型测试并做变异验证**

验证：删掉“同卡已占用拒绝”或把 `TryCompleteCardSelection` 改成每个调用者独立补第一列，测试必须变红；恢复后全绿。

- [ ] **Step 5: 提交**

```powershell
git add src/DFLegacy.Server/DungeonInstance.cs src/DFLegacy.Server/DungeonRoomSettlementState.cs tests/DFLegacy.SmokeTests/DungeonPartySmokeTests.cs
git commit -m "副本组队：加入队伍级翻牌状态模型"
```

---

### Task 2: 统一手动/自动 `CMD 74` 和 party-level `dungeon_clear`

**Files:**
- Modify: `src/DFLegacy.Server/EntranceService.cs:1654-1822,14409-14509,14580-14645`
- Modify: `src/DFLegacy.Server/DungeonPartyService.cs`
- Modify: `tests/DFLegacy.SmokeTests/DungeonPartySmokeTests.cs`

**Interfaces:**
- Consumes: Task 1 `PartyCardSelectionState`、当前 `DungeonClearReward`、`GameProtocolEngine.CreateSelectCardReply`。
- Produces: 组队手动和自动均返回一份完整 snapshot；发送者不重复接收；只允许一次最终清理。

- [ ] **Step 1: 写失败测试，复现“两人都自动第一张”**

测试必须调用同一个 party state 的自动完成入口两次：

```csharp
var first = party.TryCompleteCardSelection(out var result1);
var second = party.TryCompleteCardSelection(out var result2);
check(first, "first party timeout completes cards");
check(!second, "second timeout is idempotent");
check(result1.OwnerByColumn.SequenceEqual(new byte[] { 0, 1, 0xFF, 0xFF }), "owners differ by party slot");
```

同时验证两名成员收到的 `CMD 74` 字节完全相同；发起自动完成的会话不额外再收一份。

- [ ] **Step 2: 运行定向测试确认失败**

Run：

```powershell
dotnet run --project tests/DFLegacy.SmokeTests/DFLegacy.SmokeTests.csproj -c Release
```

Expected：当前实现会为两个会话各自选 card 0，或没有 party-level completion 状态，测试失败。

- [ ] **Step 3: 删除组队自动路径对 `FindLowestAvailableFreeCardIndex(..., [])` 的依赖**

`TryAutomaticallySelectFreeCardAsync` 保留单人分支；组队分支改为：

```csharp
if (dungeonInstance is { IsParty: true } partyInstance)
{
    var completion = partyInstance.TryCompleteCardSelectionForGeneration(
        currentSettlementGeneration,
        out var snapshot);
    if (!completion.CompletedNow)
        return completion.AlreadyCompleted;

    // 锁外：给每个成员按其 slot 应用对应奖励，然后广播同一份完整 CMD 74。
}
else
{
    // 单人原有 GrantFreeClearRewardAsync 与包序列保持不变。
}
```

不能在每个会话里直接调用 `GrantFreeClearRewardAsync(0)`。奖励落库按最终 owner slot 分配，必须在实例锁外按确定顺序执行，并用 generation/owner 标志防止重复写入。

- [ ] **Step 4: 手动 CMD 74 接入同一个实例状态**

在 `EntranceService` CMD 74 分支中：

```csharp
if (dungeonInstance is { IsParty: true } partyInstance)
{
    var outcome = partyInstance.TryRecordCardSelection(
        activeCharacter.Id, cardType, selectedCardIndex, out var snapshot);
    if (outcome.AcceptedNow)
    {
        // 锁外按 outcome.OwnerSlot 应用奖励
        // 锁外向全队发送完整 snapshot，omit 发起者
    }
}
else
{
    // 原有单人路径原样保留
}
```

重复输入只回幂等结果，不重复保存奖励。禁止把“发送者自己的局部奖励快照”广播给其他成员。

- [ ] **Step 5: 自动完成和手动完成统一最终清理**

只有 party state 第一次从未完成变成完成时才：

```text
生成完整 CMD 74
→ 更新所有成员 reward/selected 状态
→ 设置 settlement generation completed
→ 执行一次 dungeon_clear/回城 timer
```

- [ ] **Step 6: 运行测试、变异测试和协议字节断言**

必须验证：

- 两成员无人操作只产生一次自动完成；
- 两边收到同一份 CMD 74；
- card owner 不会都落在 column 0；
- 已有 owner 的客户端不会依赖第二次覆盖，因此第一份快照必须是最终快照；
- 单人现有测试与 packet planner 不变。

- [ ] **Step 7: 提交**

```powershell
git add src/DFLegacy.Server/EntranceService.cs src/DFLegacy.Server/DungeonPartyService.cs tests/DFLegacy.SmokeTests/DungeonPartySmokeTests.cs
git commit -m "副本组队：统一队伍级手动与自动翻牌"
```

---

### Task 3: 建立普通多人掉落计分模型

**Files:**
- Create: `src/DFLegacy.Server/PartyLootRollState.cs`
- Modify: `src/DFLegacy.Server/DungeonInstance.cs`
- Test: `tests/DFLegacy.SmokeTests/DungeonPartySmokeTests.cs`

**Interfaces:**
- Consumes: ground item、picker uid、party slot、成员资格、PVF/ItemDefinition rarity/job 信息。
- Produces: `PartyLootRollResult`，包含 `Score[4]`、`PickerUid`、`WinnerUid`、两个保留尾字段、generation 和 `Resolved`。

- [ ] **Step 1: 写失败测试，钉住一次性队伍仲裁**

```csharp
var result = instance.TryResolvePartyLoot(
    groundItemId: 77,
    pickerCharacterId: alpha,
    roomGeneration: 12,
    out var roll);
check(result, "first pickup resolves");
check(!instance.TryResolvePartyLoot(77, bravo, 12, out _), "second pickup cannot re-resolve");
check(roll.Score.Length == 4, "NOTI39 carries four scores");
check(roll.WinnerUid != 0, "winner is selected by shared state");
```

- [ ] **Step 2: 运行 smoke 确认失败**

```powershell
dotnet run --project tests/DFLegacy.SmokeTests/DFLegacy.SmokeTests.csproj -c Release
```

Expected：当前是共享地面物先到先得，没有统一 score/winner 结果对象。

- [ ] **Step 3: 实现普通掉落计分**

实现固定算法边界：

```csharp
baseScore = eligible ? 1 : 0;
if (item.IsStackableSpecialType || item.MatchesUsableJob(member.Job))
    baseScore += 30;
score = baseScore + random.Next(Math.Max(1, 100 - baseScore));
```

注意：这是根据官服静态控制流建立的第一层兼容模型；未验证的特殊物主调整不得自行加入。随机源必须注入 `IDropRandomSource`，测试用固定序列，不能调用全局随机。

- [ ] **Step 4: 实现最高分和同分规则**

遍历 slot 0..3，只看有效且参与标志为真的成员；最高分胜出；正分同分时按官服控制流逐一减一打破平局。将 winner uid 和 score[4] 一起存入 result。

- [ ] **Step 5: 运行模型测试和变异验证**

覆盖：非队伍/单人不进入 party roll、无效成员不参与、固定随机可重放、并发只一次、旧 generation 拒绝。把 winner 改成 picker 恒赢时，职业/随机测试必须变红。

- [ ] **Step 6: 提交**

```powershell
git add src/DFLegacy.Server/PartyLootRollState.cs src/DFLegacy.Server/DungeonInstance.cs tests/DFLegacy.SmokeTests/DungeonPartySmokeTests.cs
git commit -m "副本组队：建立普通掉落队伍计分仲裁"
```

---

### Task 4: 构造并广播普通 `NOTI 39`

**Files:**
- Modify: `src/DFLegacy.Protocol/GameProtocolEngine.cs`
- Modify: `src/DFLegacy.Server/EntranceService.cs`
- Modify: `src/DFLegacy.Server/DungeonPartyService.cs`
- Test: `tests/DFLegacy.SmokeTests/DungeonPartySmokeTests.cs`

**Interfaces:**
- Consumes: Task 3 `PartyLootRollResult`。
- Produces: `CreatePartyLootResult(...)`，载荷严格为 `u16,u16,u8[4],u16,u16,u8`，并向队伍发送一份统一快照。

- [ ] **Step 1: 写协议字节失败测试**

```csharp
var packet = GameProtocolEngine.CreatePartyLootResult(
    itemOrPickupId: 77,
    pickerUid: 11,
    scores: new byte[] { 4, 19, 0, 0 },
    winnerUid: 22,
    extraShort: 0,
    extraByte: 0);
check(packet.ProtocolId == 39, "NOTI39 id");
check(packet.Payload.Length == 2 + 2 + 4 + 2 + 2 + 1, "NOTI39 field widths");
```

- [ ] **Step 2: 运行定向测试确认失败**

```powershell
dotnet run --project tests/DFLegacy.SmokeTests/DFLegacy.SmokeTests.csproj -c Release
```

Expected：当前没有以该字段顺序构建的公开协议构建器。

- [ ] **Step 3: 实现构建器并锁定字段顺序**

构建器只接受已经解析好的值，不在协议层重新随机或判断归属：

```csharp
public static GameServerPacket CreatePartyLootResult(
    ushort itemOrPickupId,
    ushort pickerUid,
    IReadOnlyList<byte> scores,
    ushort winnerUid,
    ushort extraShort,
    byte extraByte)
```

`extraShort/extraByte` 在未闭合业务语义前使用现有服务端等价值，并在注释中标 `[未验证]`；不得把它们命名成未经证实的字段。

- [ ] **Step 4: 替换组队拾取发送路径**

组队拾取流程改为：

```text
锁内 TryResolvePartyLoot
→ 锁外发放 winner 的物品/金币
→ 锁外构造完整 NOTI39
→ BroadcastToMembersAsync(instance, packet, omitCharacterId: null)
```

发包者也必须收到同一份队伍结果；不能继续沿用“拾取者私有奖励包 + 队友短移除包”作为组队最终结果。单人保持原有直接拾取路径。

- [ ] **Step 5: 运行协议、回放和变异测试**

覆盖：同一 ground item 只发一份结果、四个 score 均有位置、胜者 UID 稳定、所有成员收到相同字节、坏通道不阻断其他成员、单人不额外发送 NOTI39。

- [ ] **Step 6: 提交**

```powershell
git add src/DFLegacy.Protocol/GameProtocolEngine.cs src/DFLegacy.Server/EntranceService.cs src/DFLegacy.Server/DungeonPartyService.cs tests/DFLegacy.SmokeTests/DungeonPartySmokeTests.cs
git commit -m "副本组队：对齐普通掉落 NOTI39 广播"
```

---

### Task 5: PVF rarity/job 映射与现有 ItemDefinition 对账

**Files:**
- Modify: `src/DFLegacy.Server/ItemCatalog.cs`：只补充已存在的 rarity/usable-job 数据暴露，不引入 routing 物品生成。
- Modify: `tests/DFLegacy.SmokeTests/`：新增 `ItemCatalogRoutingMappingSmokeTests.cs`。
- Documentation: `knowledge/decisions/2026-10-01-pickup-and-routing-closure.md`：记录实际载入数量和缺省字段。

**Interfaces:**
- Consumes: `ScriptFileSystem` 的 `.equ/.stk` 内容。
- Produces: `ItemDefinition.Rarity`、usable job 映射供 Task 3 计分使用；不产生 `SpecialItemRouting` 状态。

- [ ] **Step 1: 写 PVF 样本测试**

选取现有 PVF 中一个 `[rarity] 4`、一个 `[rarity] 0`、一个 `[usable job] [fighter]` 样本，断言解析结果；选一个无 `[rarity]` 脚本，断言保持 null/默认，而不是强行映射为 routing。

- [ ] **Step 2: 运行失败测试**

```powershell
dotnet run --project tests/DFLegacy.SmokeTests/DFLegacy.SmokeTests.csproj -c Release
```

Expected：若当前 `ItemDefinition` 已有字段但加载器未覆盖样本，测试指出缺口；若已覆盖，测试直接作为回归护栏。

- [ ] **Step 3: 最小补齐加载映射**

只实现：

```text
[rarity] → ItemDefinition.Rarity
[usable job] → ItemDefinition/可用职业集合
```

不添加 `[epic routing]` 或 `[routing priority]` 的默认生成逻辑，因为当前 PVF 无直接样本，且当前客户端主分发不支持对应新协议。

- [ ] **Step 4: 运行完整 smoke 并记录真实样本数量**

把解析数量和缺省项写入知识记录，保留 `[静态]` PVF 统计与 `[回放]` 加载结果的区别。

- [ ] **Step 5: 提交**

```powershell
git add src/DFLegacy.Server/ItemCatalog.cs tests/DFLegacy.SmokeTests knowledge/decisions/2026-10-01-pickup-and-routing-closure.md
git commit -m "副本掉落：对账 PVF 稀有度和职业字段"
```

---

### Task 6: 综合回放、单人回归和双客户端验收

**Files:**
- Modify: `tests/DFLegacy.SmokeTests/DungeonPartySmokeTests.cs`：加入最终跨任务回归入口。
- Documentation: `STATUS.md`：仅在验证完成后更新验收矩阵；保留未验证项。

**Interfaces:**
- Consumes: Tasks 1–5 的实例仲裁、协议构建器和 PVF 映射。
- Produces: 可复现的 party card/loot replay 与真机验收记录。

- [ ] **Step 1: 运行全量 smoke**

```powershell
dotnet run --project tests/DFLegacy.SmokeTests/DFLegacy.SmokeTests.csproj -c Release
```

Expected：末行 `All DFLegacy protocol smoke tests passed.`，且没有 `Exception`。

- [ ] **Step 2: 运行 Release build**

```powershell
dotnet build src/DFLegacy.Server/DFLegacy.Server.csproj -c Release --no-incremental
```

Expected：0 warnings、0 errors。

- [ ] **Step 3: 运行确定性 party replay**

固定：party slots、item IDs、random source、room generation、卡片输入顺序。重复运行两次，要求：

```text
CMD 74 字节相同
NOTI39 字节相同
winner slot/uid 相同
奖励写入结果相同
重复 timer 不产生第二份奖励
```

- [ ] **Step 4: 运行单人回归**

对单人进图、换房、清房、翻牌、回城执行已有 packet tracing；确认新代码没有新增队伍广播、NOTI39 或跨会话投递。

- [ ] **Step 5: 双客户端真机验收翻牌**

至少覆盖：

1. 两人都不操作，等待自动翻牌：两端收到同一份最终 `CMD 74`，不会都把第一张显示为自己获得；
2. 一人手动、一人等待：party state 不重复发奖，双方最终快照一致；
3. 两人手动选择不同列：每个成员的 owner 归属一致；
4. 翻牌完成后双方都能继续回城。

- [ ] **Step 6: 双客户端真机验收普通掉落**

至少覆盖：

1. 组队击杀产生普通地面物；
2. 一方触发拾取；
3. 两端收到同一份 `NOTI 39`；
4. 只有一个 winner；
5. 非 winner 端不额外获得物品；
6. 单人拾取流程保持原行为。

- [ ] **Step 7: 只记录已证实结论**

更新 `STATUS.md` 时：

- 将已通过 smoke/回放/真机的项目标上对应证据类别；
- `NOTI39` 尾字段精确名称保持 `[未验证]`；
- Special Item Routing 保持 `[未验证/未启用]`；
- 不把一次双客户端成功局升级成“所有副本已复刻”。

- [ ] **Step 8: 提交验收文档**

```powershell
git add tests/DFLegacy.SmokeTests/DungeonPartySmokeTests.cs STATUS.md
git commit -m "副本掉落与翻牌：完成确定性回放和验收记录"
```

---

## 后续明确不进入本计划的项目

1. `CItem +0x79/+0xBD/+0xC0` 的 Special Item Routing 运行时接入；
2. `NOTI 281/282`、`CMD 301` 当前客户端扩展入口；
3. 未验证的 `NOTI 39` 尾字段业务命名；
4. 未验证的特殊物主强制加分条件；
5. 以“蓝装以上”直接替代 PVF routing flag 的规则；
6. 任何新的单包试错式兼容补丁。

## 计划自审

- 规范覆盖：翻牌状态、自动/手动统一、客户端 owner 首写不覆盖、普通掉落计分、NOTI39 字段宽度、PVF rarity/job 映射、单人隔离、回放/真机验收均有任务。
- 证据边界：Special Item Routing 和未命名尾字段明确排除，不会被实现者误当成已确认规则。
- 类型一致性：Task 1 产出 `PartyCardSelectionState`；Task 2 消费其 selection/completion；Task 3 产出 `PartyLootRollResult`；Task 4 消费并构造 `CreatePartyLootResult`；Task 5 供 Task 3 使用；Task 6 汇总验收。
- 当前工作区有未提交 `.zcodeignore`，计划执行前须保持不触碰；文档和生产代码提交时只添加任务列出的路径。
