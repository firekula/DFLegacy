using System.Text.Json;
using System.Text.Json.Serialization;

namespace DFLegacy.Server;

public sealed record ExtendedInventoryItem(
    [property: JsonRequired] InventoryAddress Address,
    [property: JsonRequired] CharacterItemRecord Item);

public sealed partial record CharacterRecord
{
    // Only slots beyond the legacy arrays live here. Item.Slot is zero in this list.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ExtendedInventoryItem>? ExtendedInventory { get; init; }
}

// Applied only to Accounts inside a saved state. Old servers require a JSON array
// there, so they reject the versioned object before running any startup migrations.
internal sealed class InventoryAccountsConverter : JsonConverter<List<AccountRecord>>
{
    private sealed record InventoryEnvelope(
        [property: JsonRequired] int Version,
        [property: JsonRequired] AccountRecord[] Records);

    public override List<AccountRecord> Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.StartArray)
        {
            var legacy = JsonSerializer.Deserialize<AccountRecord[]>(ref reader, options)!;
            RequireLegacy(legacy);
            return [.. legacy];
        }

        var envelope = JsonSerializer.Deserialize<InventoryEnvelope>(ref reader, options);
        if (envelope is not { Version: 2, Records: not null })
            throw new JsonException("Unsupported inventory save format.");
        Validate(envelope.Records);
        return [.. envelope.Records];
    }

    public override void Write(Utf8JsonWriter writer, List<AccountRecord> accounts, JsonSerializerOptions options)
    {
        var extended = HasExtendedSlots(accounts);
        if (extended)
        {
            Validate(accounts);
            writer.WriteStartObject();
            writer.WriteNumber("Version", 2);
            writer.WritePropertyName("Records");
        }
        writer.WriteStartArray();
        foreach (var account in accounts) JsonSerializer.Serialize(writer, account, options);
        writer.WriteEndArray();
        if (extended) writer.WriteEndObject();
    }

    internal static void RequireLegacy(IEnumerable<AccountRecord> accounts)
    {
        if (HasExtendedSlots(accounts))
            throw new JsonException("Extended inventory requires the versioned account envelope.");
    }

    private static bool HasExtendedSlots(IEnumerable<AccountRecord> accounts) =>
        accounts.Any(account => account.Characters.Any(character => character.ExtendedInventory is { Count: > 0 }));

    private static void Validate(IReadOnlyCollection<AccountRecord> accounts)
    {
        var extendedIds = new HashSet<Guid>();
        foreach (var character in accounts.SelectMany(account => account.Characters))
        {
            if (character.ExtendedInventory is not { Count: > 0 } items) continue;
            var occupied = new HashSet<InventoryAddress>();
            foreach (var entry in items)
            {
                if (entry?.Item is not { ItemId: > 0, Slot: 0 } item
                    || !entry.Address.IsValid || entry.Address.TryGetLegacySlot(out _)
                    || !occupied.Add(entry.Address))
                    throw new JsonException($"Invalid or duplicate extended inventory slot on {character.Id}.");
                if (item.InstanceId != Guid.Empty && !extendedIds.Add(item.InstanceId))
                    throw new JsonException($"Duplicate extended inventory instance {item.InstanceId}.");
            }
        }

        foreach (var character in accounts.SelectMany(account => account.Characters))
        {
            foreach (var list in new[] { character.Inventory, character.AvatarInventory, character.Warehouse,
                         character.Equipment, character.CreatureInventory })
            {
                if (list is not null && list.Any(item => extendedIds.Contains(item.InstanceId)))
                    throw new JsonException($"An extended inventory instance also exists in a legacy container on {character.Id}.");
            }
            if (character.PrivateStoreStock?.Listings.Any(listing => extendedIds.Contains(listing.Item.InstanceId)) == true)
                throw new JsonException($"An extended inventory instance also exists in a private store on {character.Id}.");
            if (character.Mailbox?.Any(mail => mail.Attachment is { } attachment && extendedIds.Contains(attachment.InstanceId)) == true)
                throw new JsonException($"An extended inventory instance also exists in the mailbox of {character.Id}.");
        }
    }
}
