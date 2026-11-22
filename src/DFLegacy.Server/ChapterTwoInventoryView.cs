using DFLegacy.Protocol;

namespace DFLegacy.Server;

internal readonly record struct InventoryWindowCell(byte Offset, CharacterItemRecord Item);

internal sealed class ChapterTwoInventoryView
{
    private readonly Dictionary<InventoryAddress, CharacterItemRecord> _slots = [];
    internal CharacterRecord Character { get; }

    internal ChapterTwoInventoryView(CharacterRecord character)
    {
        Character = character;
        AddLegacy(character.Inventory, false);
        AddLegacy(character.AvatarInventory, true);
        foreach (var entry in character.ExtendedInventory ?? []) _slots.Add(entry.Address, entry.Item);
    }

    private void AddLegacy(List<CharacterItemRecord>? entries, bool avatar)
    {
        foreach (var item in entries ?? [])
            if (item.ItemId != 0 && InventoryAddress.TryFromLegacy(avatar, item.Slot, out var address))
                _slots.Add(address, item);
    }

    internal bool TryRead(InventoryWindowRequest request, out InventoryWindowCell[] cells)
    {
        cells = [];
        var start = new InventoryAddress(request.Section, request.First);
        if (request.Operation != InventoryWindowOperation.Read || !start.IsValid) return false;

        var width = request.Section == InventorySection.Avatar ? 28 : 32;
        var length = Math.Min(width, InventoryAddress.Capacity(request.Section) - request.First);
        var items = new List<InventoryWindowCell>();
        for (byte offset = 0; offset < length; offset++)
            if (_slots.TryGetValue(start with { Index = start.Index + offset }, out var item))
                items.Add(new(offset, item));
        cells = items.ToArray();
        return true;
    }
}
