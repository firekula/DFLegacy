using System.Text.Json.Serialization;
using DFLegacy.Protocol;

namespace DFLegacy.Server;

public readonly record struct InventoryAddress(
    [property: JsonRequired] InventorySection Section,
    [property: JsonRequired] int Index)
{
    public const int BagCapacity = 99_999;
    public const int AvatarCapacity = 999_999;

    public bool IsValid => Index >= 0 && Index < Capacity(Section);

    public static int Capacity(InventorySection section) => section switch
    {
        InventorySection.Equipment or InventorySection.Consumable or InventorySection.Material or InventorySection.Quest => BagCapacity,
        InventorySection.Avatar => AvatarCapacity,
        _ => 0
    };

    private static (int Start, int End) LegacyRange(InventorySection section) => section switch
    {
        InventorySection.Equipment => (CharacterInventoryLayout.EquipmentSlotStart, CharacterInventoryLayout.EquipmentSlotEnd),
        InventorySection.Consumable => (CharacterInventoryLayout.ConsumableSlotStart, CharacterInventoryLayout.ConsumableSlotEnd),
        InventorySection.Material => (CharacterInventoryLayout.MaterialSlotStart, CharacterInventoryLayout.MaterialSlotEnd),
        InventorySection.Quest => (CharacterInventoryLayout.QuestSlotStart, CharacterInventoryLayout.QuestSlotEnd),
        InventorySection.Avatar => (0, CharacterAvatarInventoryLayout.Capacity),
        _ => (0, 0)
    };

    public bool TryGetLegacySlot(out ushort slot)
    {
        var (start, end) = LegacyRange(Section);
        slot = 0;
        if ((uint)Index >= (uint)(end - start)) return false;
        slot = (ushort)(start + Index);
        return true;
    }

    public static bool TryFromLegacy(bool avatar, ushort slot, out InventoryAddress address)
    {
        var section = avatar ? InventorySection.Avatar : slot switch
        {
            < CharacterInventoryLayout.ConsumableSlotStart => InventorySection.Equipment,
            < CharacterInventoryLayout.MaterialSlotStart => InventorySection.Consumable,
            < CharacterInventoryLayout.QuestSlotStart => InventorySection.Material,
            _ => InventorySection.Quest
        };
        var (start, end) = LegacyRange(section);
        address = default;
        if (slot < start || slot >= end) return false;
        address = new(section, slot - start);
        return true;
    }
}
