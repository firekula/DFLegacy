namespace DFLegacy.Server;

public enum EquipmentReinforcementFailure : byte
{
    None = 0,
    InvalidTarget = 4,
    InsufficientGold = 10,
    RuleUnavailable = 13,
    InsufficientMaterial = 22
}

public sealed record EquipmentReinforcementPlan(
    IReadOnlyDictionary<ushort, CharacterItemRecord> MainInventory,
    IReadOnlyDictionary<ushort, CharacterItemRecord> Equipment,
    int RemainingGold,
    ushort TargetSlot,
    byte TargetListType,
    ushort TargetItemId,
    ushort MaterialSlot,
    uint MaterialRemaining,
    byte PreviousLevel,
    byte NewLevel,
    byte ResultCode,
    bool Failed,
    bool Destroyed,
    int FailureRoll,
    EquipmentReinforcementCost Cost);

public static class EquipmentReinforcementPlanner
{
    public static bool TryCreate(
        IReadOnlyDictionary<ushort, CharacterItemRecord> mainInventory,
        IReadOnlyDictionary<ushort, CharacterItemRecord> equipment,
        int currentGold,
        ushort requestedTargetSlot,
        ushort expectedTargetItemId,
        ushort materialSlot,
        ItemCatalog itemCatalog,
        EquipmentReinforcementCatalog reinforcementCatalog,
        IDropRandomSource random,
        out EquipmentReinforcementPlan plan,
        out EquipmentReinforcementFailure failure,
        bool chapterTwo = false)
    {
        ArgumentNullException.ThrowIfNull(mainInventory);
        ArgumentNullException.ThrowIfNull(equipment);
        ArgumentNullException.ThrowIfNull(itemCatalog);
        ArgumentNullException.ThrowIfNull(reinforcementCatalog);
        ArgumentNullException.ThrowIfNull(random);
        plan = null!;
        failure = EquipmentReinforcementFailure.InvalidTarget;
        if (expectedTargetItemId == 0
            || !TryResolveTarget(
                mainInventory,
                equipment,
                requestedTargetSlot,
                expectedTargetItemId,
                out var targetSlot,
                out var targetListType,
                out var target)
            || targetListType == 0 && targetSlot == materialSlot
            || !itemCatalog.TryGetDefinition(target.ItemId, out var definition)
            || definition.ScriptKind != ItemScriptKind.Equipment
            || definition.InventoryCategory != ItemInventoryCategory.Equipment)
        {
            return false;
        }

        var previousLevel = (byte)(target.State & EquipmentReinforcementCatalog.MaximumLevel);
        if (mainInventory.TryGetValue(materialSlot, out var ticket)
            && TryGetReinforcementTicketLevel(ticket.ItemId, out var ticketLevel))
        {
            if (ticket.CountOrValue == 0)
            {
                failure = EquipmentReinforcementFailure.InsufficientMaterial;
                return false;
            }

            var remainingTickets = ticket.CountOrValue - 1;
            var ticketMain = mainInventory.ToDictionary(pair => pair.Key, pair => pair.Value);
            var ticketEquipment = equipment.ToDictionary(pair => pair.Key, pair => pair.Value);
            if (remainingTickets == 0)
            {
                ticketMain.Remove(materialSlot);
            }
            else
            {
                ticketMain[materialSlot] = ticket with
                {
                    CountOrValue = remainingTickets
                };
            }

            var ticketTargetSpace = targetListType == 3 ? ticketEquipment : ticketMain;
            ticketTargetSpace[targetSlot] = target with
            {
                State = (byte)((target.State & ~EquipmentReinforcementCatalog.MaximumLevel)
                    | ticketLevel)
            };
            plan = new EquipmentReinforcementPlan(
                ticketMain,
                ticketEquipment,
                currentGold,
                targetSlot,
                targetListType,
                target.ItemId,
                materialSlot,
                remainingTickets,
                previousLevel,
                ticketLevel,
                0,
                Failed: false,
                Destroyed: false,
                FailureRoll: 0,
                new EquipmentReinforcementCost(
                    ticket.ItemId,
                    1,
                    0,
                    0,
                    0,
                    ticketLevel));
            failure = EquipmentReinforcementFailure.None;
            return true;
        }

        if (!reinforcementCatalog.TryGetCost(
                previousLevel,
                definition,
                out var cost,
                chapterTwo))
        {
            failure = EquipmentReinforcementFailure.RuleUnavailable;
            return false;
        }

        if (!mainInventory.TryGetValue(materialSlot, out var material)
            || material.ItemId != cost.MaterialItemId
            || material.CountOrValue < cost.MaterialCount)
        {
            failure = EquipmentReinforcementFailure.InsufficientMaterial;
            return false;
        }

        if (cost.Gold < 0 || currentGold < cost.Gold)
        {
            failure = EquipmentReinforcementFailure.InsufficientGold;
            return false;
        }

        var plannedMain = mainInventory.ToDictionary(pair => pair.Key, pair => pair.Value);
        var plannedEquipment = equipment.ToDictionary(pair => pair.Key, pair => pair.Value);
        var materialRemaining = material.CountOrValue - cost.MaterialCount;
        if (materialRemaining == 0)
        {
            plannedMain.Remove(materialSlot);
        }
        else
        {
            plannedMain[materialSlot] = material with
            {
                CountOrValue = materialRemaining
            };
        }

        var failureRoll = random.Next(
            EquipmentReinforcementCatalog.FailureRollUpperBound);
        var reinforcementFailed = cost.FailureWeight > 0
            && failureRoll < cost.FailureWeight;
        var destroyed = false;
        byte resultCode = 0;
        var newLevel = cost.TargetLevel;
        if (reinforcementFailed)
        {
            resultCode = checked((byte)Math.Clamp(
                Math.Max(1, cost.PenaltyType),
                byte.MinValue,
                byte.MaxValue));
            if (cost.PenaltyType == 3)
            {
                destroyed = true;
                newLevel = previousLevel;
            }
            else if (cost.PenaltyType == 2)
            {
                newLevel = previousLevel > 0
                    ? checked((byte)(previousLevel - 1))
                    : (byte)0;
            }
            else
            {
                newLevel = previousLevel;
            }
        }

        var targetSpace = targetListType == 3 ? plannedEquipment : plannedMain;
        if (destroyed)
        {
            targetSpace.Remove(targetSlot);
        }
        else
        {
            targetSpace[targetSlot] = target with
            {
                State = (byte)((target.State & ~EquipmentReinforcementCatalog.MaximumLevel)
                    | newLevel)
            };
        }

        plan = new EquipmentReinforcementPlan(
            plannedMain,
            plannedEquipment,
            checked(currentGold - cost.Gold),
            targetSlot,
            targetListType,
            target.ItemId,
            materialSlot,
            materialRemaining,
            previousLevel,
            newLevel,
            resultCode,
            reinforcementFailed,
            destroyed,
            failureRoll,
            cost);
        failure = EquipmentReinforcementFailure.None;
        return true;
    }

    private static bool TryGetReinforcementTicketLevel(
        ushort itemId,
        out byte level)
    {
        level = itemId switch
        {
            7_273 => 7,
            7_274 => 10,
            7_275 => 12,
            _ => 0
        };
        return level != 0;
    }

    private static bool TryResolveTarget(
        IReadOnlyDictionary<ushort, CharacterItemRecord> mainInventory,
        IReadOnlyDictionary<ushort, CharacterItemRecord> equipment,
        ushort requestedSlot,
        ushort expectedItemId,
        out ushort resolvedSlot,
        out byte listType,
        out CharacterItemRecord target)
    {
        resolvedSlot = requestedSlot;
        listType = 0;
        target = null!;
        if (mainInventory.TryGetValue(requestedSlot, out var item)
            && IsExpectedTarget(item, expectedItemId))
        {
            target = item;
            return true;
        }

        if (equipment.TryGetValue(requestedSlot, out item)
            && IsExpectedTarget(item, expectedItemId))
        {
            listType = 3;
            target = item;
            return true;
        }

        if (requestedSlot < 10)
        {
            var mappedSlot = checked((ushort)(requestedSlot + 9));
            if (equipment.TryGetValue(mappedSlot, out item)
                && IsExpectedTarget(item, expectedItemId))
            {
                resolvedSlot = mappedSlot;
                listType = 3;
                target = item;
                return true;
            }
        }

        return false;
    }

    private static bool IsExpectedTarget(
        CharacterItemRecord item,
        ushort expectedItemId) =>
        item.ItemId != 0 && item.ItemId == expectedItemId;
}
