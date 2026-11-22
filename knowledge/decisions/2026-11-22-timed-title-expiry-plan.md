# 限时装备/称号到期回收实施计划（2026-11-22）

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让带 `[usable period]` 的非堆叠装备（含魔盒称号）在服务端记录截止时间，到期后从背包/装备/仓库/邮件移除并刷新客户端。

**Architecture:** 在物品目录解析 `[usable period]`；复用装扮生命周期的字段和结算循环，把 `NormalizeCharacter` 的追踪范围从装扮扩展到限时装备；会话结算增加主背包和仓库的合并/刷新。

**Tech Stack:** C#/.NET 10、控制台 SmokeTests、git worktree。

---

### Task 1: 解析 `[usable period]`

**Files:**
- Modify: `src/DFLegacy.Server/ItemCatalog.cs`

- [ ] **Step 1: 给 `ItemDefinition` 增加字段**

在 `ItemDefinition` 记录的最后一个参数 `bool IsCloneAvatar = false` 后追加：

```csharp
    bool IsCloneAvatar = false,
    int? UsablePeriodDays = null)
```

- [ ] **Step 2: 增加正则**

在 `ClearAvatarPattern` 附近加入：

```csharp
    private static readonly Regex UsablePeriodPattern =
        CreateIntegerTagPattern("usable period");
```

- [ ] **Step 3: 构造 `ItemDefinition` 时读取**

把：

```csharp
                     IsCloneAvatar: scriptKind == ItemScriptKind.Equipment && ReadInteger(ClearAvatarPattern, text) == 1));
```

改为：

```csharp
                     IsCloneAvatar: scriptKind == ItemScriptKind.Equipment && ReadInteger(ClearAvatarPattern, text) == 1,
                     UsablePeriodDays: ReadInteger(UsablePeriodPattern, text)));
```

- [ ] **Step 4: 构建**

```powershell
dotnet build src/DFLegacy.Server/DFLegacy.Server.csproj -c Release
```

预期：0 错误。

---

### Task 2: 把生命周期策略扩展到限时装备

**Files:**
- Modify: `src/DFLegacy.Server/AvatarLifetimePolicy.cs`

- [ ] **Step 1: 增加追踪判定**

在 `NormalizeCharacter` 内，把：

```csharp
        bool IsAvatar(ushort id) => catalog.TryGetDefinition(id, out var d) && d.InventoryCategory == ItemInventoryCategory.Avatar;
```

改为：

```csharp
        bool IsLifetimeTracked(ushort id)
        {
            if (!catalog.TryGetDefinition(id, out var definition)) return false;
            return definition.InventoryCategory == ItemInventoryCategory.Avatar
                || (definition.InventoryCategory == ItemInventoryCategory.Equipment
                    && definition.UsablePeriodDays is > 0);
        }
```

- [ ] **Step 2: `known` 和邮件过滤改用新判定**

把 `known` 构建中的：

```csharp
        var known = Items(previous ?? candidate).Where(i => i.InstanceId != Guid.Empty && IsAvatar(i.ItemId))
```

改为：

```csharp
        var known = Items(previous ?? candidate).Where(i => i.InstanceId != Guid.Empty && IsLifetimeTracked(i.ItemId))
```

把 `Normalize` 开头：

```csharp
            if (!IsAvatar(original.ItemId)) return original;
```

改为：

```csharp
            if (!IsLifetimeTracked(original.ItemId)) return original;
```

把邮件处理中的：

```csharp
            if (mail.Attachment is not { } attachment || !IsAvatar(attachment.ItemId)) return mail;
```

改为：

```csharp
            if (mail.Attachment is not { } attachment || !IsLifetimeTracked(attachment.ItemId)) return mail;
```

- [ ] **Step 3: 在 `Normalize` 中锚定模板期限**

在现有 `AvatarRemainingSeconds` 锚定之后、`IsExpired` 之前加入：

```csharp
            if (item.AvatarExpiresAtUnixSeconds is null
                && catalog.TryGetDefinition(item.ItemId, out var definition)
                && definition.UsablePeriodDays is > 0)
            {
                item = item with
                {
                    AvatarExpiresAtUnixSeconds = checked(
                        nowUnixSeconds + definition.UsablePeriodDays.Value * 86_400L)
                };
            }
```

- [ ] **Step 4: `MergeForActor` 合并主背包/仓库/装备**

把现有 `MergeForActor` 方法体替换为：

```csharp
    public static CharacterRecord MergeForActor(CharacterRecord actor, CharacterRecord canonical, ItemCatalog catalog)
    {
        bool IsLifetimeTracked(CharacterItemRecord item) =>
            catalog.TryGetDefinition(item.ItemId, out var definition)
            && (definition.InventoryCategory == ItemInventoryCategory.Avatar
                || (definition.InventoryCategory == ItemInventoryCategory.Equipment
                    && definition.UsablePeriodDays is > 0));
        static List<CharacterItemRecord> MergeSpace(
            IEnumerable<CharacterItemRecord>? actorItems,
            IEnumerable<CharacterItemRecord>? canonicalItems,
            Func<CharacterItemRecord, bool> isLifetimeTracked) =>
            (actorItems ?? [])
                .Where(item => !isLifetimeTracked(item))
                .Concat((canonicalItems ?? []).Where(isLifetimeTracked))
                .OrderBy(item => item.Slot)
                .ToList();
        return actor with
        {
            Inventory = MergeSpace(actor.Inventory, canonical.Inventory, IsLifetimeTracked),
            Equipment = MergeSpace(actor.Equipment, canonical.Equipment, IsLifetimeTracked),
            Warehouse = MergeSpace(actor.Warehouse, canonical.Warehouse, IsLifetimeTracked),
            AvatarInventory = canonical.AvatarInventory,
            Mailbox = canonical.Mailbox,
            ExpiredAvatarInstanceIds = canonical.ExpiredAvatarInstanceIds
        };
    }
```

- [ ] **Step 5: 构建**

```powershell
dotnet build src/DFLegacy.Server/DFLegacy.Server.csproj -c Release
```

预期：0 错误。

---

### Task 3: 会话结算刷新主背包和仓库

**Files:**
- Modify: `src/DFLegacy.Server/AvatarLifetimeSessionRefresh.cs`
- Modify: `src/DFLegacy.Server/EntranceService.cs`

- [ ] **Step 1: 扩展 `PublishAsync`**

在 `AvatarLifetimeSessionRefresh` 中保留现有五参数方法作为重载，并新增：

```csharp
    internal Task<bool> PublishAsync(CharacterRecord before, CharacterRecord after,
        Func<Task> refreshBag, Func<Task> refreshEquipmentAndPeers, Func<Task> refreshMailbox) =>
        PublishAsync(before, after, refreshBag, () => Task.CompletedTask, () => Task.CompletedTask,
            refreshEquipmentAndPeers, refreshMailbox);

    internal async Task<bool> PublishAsync(CharacterRecord before, CharacterRecord after,
        Func<Task> refreshBag, Func<Task> refreshMainInventory, Func<Task> refreshWarehouse,
        Func<Task> refreshEquipmentAndPeers, Func<Task> refreshMailbox)
    {
        var expiryPending = HasPending(after);
        var bag = expiryPending || !(before.AvatarInventory ?? []).SequenceEqual(after.AvatarInventory ?? []);
        var main = expiryPending || !(before.Inventory ?? []).SequenceEqual(after.Inventory ?? []);
        var warehouse = expiryPending || !(before.Warehouse ?? []).SequenceEqual(after.Warehouse ?? []);
        var equipment = expiryPending || !(before.Equipment ?? []).SequenceEqual(after.Equipment ?? []);
        var mail = expiryPending || !(before.Mailbox ?? []).SequenceEqual(after.Mailbox ?? []);
        if (bag) await refreshBag();
        if (main) await refreshMainInventory();
        if (warehouse) await refreshWarehouse();
        if (equipment) await refreshEquipmentAndPeers();
        if (mail) await refreshMailbox();
        _published.UnionWith(after.ExpiredAvatarInstanceIds ?? []);
        return bag || main || warehouse || equipment || mail;
    }
```

- [ ] **Step 2: 会话快照加入主背包/仓库**

把 `AvatarActorSnapshot` 改为：

```csharp
                CharacterRecord AvatarActorSnapshot() => activeCharacter! with
                {
                    Inventory = mainInventory.Values.OrderBy(i => i.Slot).ToList(),
                    Equipment = equippedInventory.Values.OrderBy(i => i.Slot).ToList(),
                    Warehouse = warehouseInventory.Values.OrderBy(i => i.Slot).ToList(),
                    AvatarInventory = avatarInventory.Values.OrderBy(i => i.Slot).ToList()
                };
```

- [ ] **Step 3: 结算后同步内存并刷新**

把 `SettleCurrentAvatarLifetimesAsync` 中安装后的部分改为：

```csharp
                        var installed = AvatarLifetimePolicy.MergeForActor(before, result.Character, itemCatalog);
                        activeCharacter = installed;
                        mainInventory = (installed.Inventory ?? []).ToDictionary(i => i.Slot);
                        warehouseInventory = (installed.Warehouse ?? []).ToDictionary(i => i.Slot);
                        avatarInventory = (installed.AvatarInventory ?? []).ToDictionary(i => i.Slot);
                        equippedInventory = (installed.Equipment ?? []).ToDictionary(i => i.Slot);
                        var published = await avatarLifetimeRefresh.PublishAsync(before, installed,
                            refreshBag: async () =>
                            {
                                var packet = CreateAvatarInventoryPacket(avatarInventory.Values);
                                await packet.WriteAsync(stream, serverToken);
                                Interlocked.Increment(ref runtimeSession.SentPackets);
                                LogPacket("TX", runtimeSession, packet);
                            },
                            refreshMainInventory: async () =>
                            {
                                var packet = CreateMainInventoryPacket(
                                    currentGold, currentVictoryPoints, mainInventory.Values);
                                await packet.WriteAsync(stream, serverToken);
                                Interlocked.Increment(ref runtimeSession.SentPackets);
                                LogPacket("TX", runtimeSession, packet);
                            },
                            refreshWarehouse: SendWarehouseStateAsync,
                            refreshEquipmentAndPeers: async () =>
                            {
                                if (currentDungeonId == 0) await SendTownAppearanceAsync();
                                else
                                {
                                    await SendCurrentEquipmentStateAsync();
                                    if (dungeonInstance is { IsParty: true } instance)
                                        await dungeonPartyService.BroadcastToMembersAsync(instance,
                                            SendTownAppearanceDetailPacket(), installed.Id, serverToken);
                                }
                            },
                            refreshMailbox: async () => { await SendMailboxStateAsync(notifyNewMail: false); });
```

把日志里的 `Settled avatar lifetime` 改为 `Settled limited item lifetime`，并补主背包/仓库数量。

- [ ] **Step 4: 构建**

```powershell
dotnet build src/DFLegacy.Server/DFLegacy.Server.csproj -c Release
```

预期：0 错误。

---

### Task 4: 新增回归测试

**Files:**
- Create: `tests/DFLegacy.SmokeTests/TimedTitleLifetimeSmokeTests.cs`
- Modify: `tests/DFLegacy.SmokeTests/Program.cs`

- [ ] **Step 1: 新建测试文件**

测试使用临时目录作为 `SkillScriptPath`，写入 `equipment/equipment.lst` 和两个称号脚本：
`50020` 带 `[usable period] 30`，`50021` 永久；再写入一个空 `stackable/stackable.lst`。
用 `JsonGameStore(..., itemCatalog: catalog, avatarTimeProvider: clock)` 保存/结算。

核心断言：

1. `catalog.TryGetDefinition(50020, out var timed) && timed.UsablePeriodDays == 30 && timed.IsTitle`；
   `50021` 的 `UsablePeriodDays` 为 null。
2. 保存一个 `CharacterItemRecord(9, 50020, 1, InstanceId: guid)` 到主背包后，读取角色：
   `AvatarExpiresAtUnixSeconds == clock.Now + 30*86400`。
3. 把时钟推进到截止后，`SettleAvatarExpirationsAsync`：
   该实例从 `Inventory`/`Equipment`/`Warehouse`/`Mailbox` 移除，`ExpiredAvatarInstanceIds` 包含其 GUID；
   永久称号 50021 保留。
4. 魔盒路径：用 `RandomBoxOpeningPlanner.TryCreate` 生成带 50020 的奖励计划，保存计划后读取角色，
   该称号带截止时间。

- [ ] **Step 2: 注册单项和全量**

在 `Program.cs` 的 `--avatar-destroy` 块后加入：

```csharp
if (args.Contains("--timed-title"))
{
    await TimedTitleLifetimeSmokeTests.RunAsync(Check);
    foreach (var failure in failures) Console.Error.WriteLine("FAIL: " + failure);
    Console.WriteLine($"Timed title lifetime smoke: {failures.Count} failures");
    return failures.Count > 0 ? 1 : 0;
}
```

在全量入口 `await AvatarDestroySmokeTests.RunAsync(Check);` 后加入：

```csharp
await TimedTitleLifetimeSmokeTests.RunAsync(Check);
```

- [ ] **Step 3: 运行定向测试**

```powershell
$env:TEMP='g:\Game\DNF_ACT1\DFLegacy\.tmp\smoke-temp'
$env:TMP=$env:TEMP
dotnet run --project tests/DFLegacy.SmokeTests -c Release -p:OutputPath='g:\Game\DNF_ACT1\DFLegacy\.tmp\worktrees\avatar-destroy-v1017\tests\DFLegacy.SmokeTests\bin\Release-title\net10.0\' -- --timed-title
```

预期：`Timed title lifetime smoke: 0 failures`。

- [ ] **Step 4: 回归装扮与销毁**

```powershell
dotnet run --project tests/DFLegacy.SmokeTests -c Release -p:OutputPath=... -- --avatar-destroy
dotnet run --project tests/DFLegacy.SmokeTests -c Release -p:OutputPath=... -- --avatar-lifetime
```

（`--avatar-lifetime` 若没有独立参数，则运行全量中的 `AvatarLifetimeSmokeTests` 对应入口；现有测试在 Debug/Release 定向中均应 0 失败。）

---

### Task 5: 构建、发布测试包、提交

**Files:**
- Create: `knowledge/decisions/2026-11-22-timed-title-expiry-result.md`

- [ ] **Step 1: Release 构建和 publish**

```powershell
dotnet build src/DFLegacy.Server/DFLegacy.Server.csproj -c Release
dotnet publish src/DFLegacy.Server/DFLegacy.Server.csproj -c Release -r win-x64 --self-contained false -o 'g:\Game\DNF_ACT1\DFLegacy\.tmp\avatar-destroy-v1017\server-publish'
```

- [ ] **Step 2: 记录结果并提交**

```powershell
git add src/DFLegacy.Server/ItemCatalog.cs src/DFLegacy.Server/AvatarLifetimePolicy.cs src/DFLegacy.Server/AvatarLifetimeSessionRefresh.cs src/DFLegacy.Server/EntranceService.cs tests/DFLegacy.SmokeTests/TimedTitleLifetimeSmokeTests.cs tests/DFLegacy.SmokeTests/Program.cs knowledge/decisions/2026-11-22-timed-title-expiry-design.md knowledge/decisions/2026-11-22-timed-title-expiry-result.md
git commit -m "Expire limited equipment and titles on the v1.0.17 release line"
```

- [ ] **Step 3: 汇报**

给出实际验证、未验证项（真机到期消失、客户端倒计时）、测试包路径和下一步。
