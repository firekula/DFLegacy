using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace DFLegacy.Server;

internal sealed record DungeonTestPlacement(ushort GroundId, int X, int Y);
internal sealed record DungeonTestCommand(string Schema, long RequestId, ulong SessionNonce, long RunId,
    long RoomGeneration, string Operation, long IssuedFiletime, ushort UserId, DungeonTestPlacement[] Placements);
internal sealed record DungeonTestCommandResult(string Schema, long RequestId, ulong SessionNonce, long RunId,
    long RoomGeneration, string Status, string Reason);
internal sealed record DungeonTestLootContext(long RunId, long RoomGeneration, ushort MapId, byte RoomX, byte RoomY,
    ushort GroundId, ushort ItemId, uint Count, Guid PickupId, ushort PickerUid, ushort WinnerUid,
    Guid WinnerCharacterId, byte PartySlot, byte[] Scores, bool IsGold = false);
internal sealed record DungeonTestLootResult(DungeonTestLootContext Context, string Status, string Reason,
    ushort? DestinationSlot, Guid InstanceId, long CapturedFiletime, long EventId, string? PublicationError = null);

// Local test diagnostics only. All disk work runs on the overlay's background task.
internal sealed class DungeonTestControl(ServerOptions options, DungeonInstanceRegistry instances,
    JsonGameStore? store, ItemCatalog? items, CharacterStatCatalog? stats)
{
    internal static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    internal readonly ulong SessionNonce = BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8)) | 1;
    private readonly long _started = Process.GetCurrentProcess().StartTime.ToUniversalTime().ToFileTimeUtc();
    private readonly HashSet<long> _requests = [];
    internal bool Enabled => options.Debug.TestControl.Enabled && options.Debug.ClientOverlay.Enabled;

    internal byte[] Lease(long runId, ushort userId, long now)
    {
        var bytes = new byte[40];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0x4c545444);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), SessionNonce);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(16), runId);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(24), now);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(32), userId);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(36), 1);
        return bytes;
    }

    internal async Task PublishAsync(string directory, HashSet<string> active, CancellationToken ct)
    {
        if (!Enabled) return;
        await ConsumeAsync(directory, ct);
        var snapshots = new List<object>();

        var now = DateTime.UtcNow.ToFileTimeUtc();
        foreach (var instance in instances.Instances)
        {
            var snapshot = instance.CaptureTestControlState();
            if (snapshot is null) continue;
            var inventories = new List<object>();
            foreach (var member in snapshot.Members)
            {
                var lease = Path.Combine(directory, $"dfdebug-test-lease-{member.AreaUserId}.bin");
                await WriteAsync(lease, Lease(snapshot.RunId, member.AreaUserId, now), ct);
                active.Add(lease);
                var character = store is null ? null : await store.GetCharacterAsync(member.AccountName, member.CharacterId, ct);
                if (character is null) continue;
                object[] Rows(IEnumerable<CharacterItemRecord>? rows, string space) => (rows ?? []).Select(row => (object)new
                    { space, slot = row.Slot, item_id = row.ItemId, count = row.CountOrValue, instance_id = row.InstanceId,
                        state = row.State, durability = row.Durability, seal_state = row.SealState,
                        equipment_quality_seed = row.EquipmentQualitySeed }).ToArray();
                inventories.Add(new { uid = member.AreaUserId, character_id = member.CharacterId, gold = character.Gold,
                    weight = items is null ? 0 : CharacterInventoryPlanner.CalculateCarriedWeight(character.Inventory ?? [], character.Equipment ?? [], character.CreatureInventory ?? [], items),
                    weight_limit = items is null || stats is null ? 0 : CharacterInventoryPlanner.CalculateCarriedWeightLimit(stats.Get(character).InventoryLimit, character.Equipment ?? [], items),
                    items = Rows(character.Inventory, "main").Concat(Rows(character.Equipment, "equipment"))
                        .Concat(Rows(character.AvatarInventory, "avatar")).Concat(Rows(character.CreatureInventory, "creature")).ToArray() });
            }
            snapshots.Add(new { run_id = snapshot.RunId, room_generation = snapshot.Generation, map_id = snapshot.MapId,
                dungeon_id = snapshot.DungeonId, room_x = snapshot.RoomX, room_y = snapshot.RoomY,
                members = snapshot.Members.Select(member => new { uid = member.AreaUserId, character_id = member.CharacterId, party_slot = member.PartySlot,
                    alive = instance.IsParty ? (bool?)!snapshot.DeadMemberUids.Contains(member.AreaUserId) : null }),
                monsters = snapshot.Monsters, ground_items = snapshot.GroundItems, inventories });
        }
        var path = Path.Combine(directory, "dfdebug-test-server.json");
        await WriteAsync(path, JsonSerializer.SerializeToUtf8Bytes(new { schema = "dfl-server-test-control/v1", enabled = true,
            server_pid = Environment.ProcessId, server_started_filetime = _started, session_nonce = SessionNonce,
            captured_filetime = now, instances = snapshots, loot_results = DungeonTestLootJournal.Capture().OrderBy(value => value.EventId).Select(value => new {
                event_id = value.EventId, persisted = value.Status == "committed",
                run_id = value.Context.RunId, room_generation = value.Context.RoomGeneration, map_id = value.Context.MapId,
                room_x = value.Context.RoomX, room_y = value.Context.RoomY, ground_id = value.Context.GroundId,
                item_id = value.Context.ItemId, count = value.Context.Count, is_gold = value.Context.IsGold, pickup_id = value.Context.PickupId,
                picker_uid = value.Context.PickerUid, winner_uid = value.Context.WinnerUid,
                winner_character_id = value.Context.WinnerCharacterId, party_slot = value.Context.PartySlot,
                scores = value.Context.Scores.Select(score => (int)score).ToArray(), status = value.Status, reason = value.Reason,
                publication_error = value.PublicationError,
                destination_slot = value.DestinationSlot, instance_id = value.InstanceId, captured_filetime = value.CapturedFiletime }) }, Json), ct);
        active.Add(path);
    }

    private async Task ConsumeAsync(string directory, CancellationToken ct)
    {
        var path = Path.Combine(directory, "dfdebug-test-server-command.json");
        if (!File.Exists(path)) return;
        var claimed = path + ".processing";
        File.Move(path, claimed, true);
        try
        {
            // Never allocate arbitrarily large input from a local mailbox.
            if (new FileInfo(claimed).Length > 65536) return;
            DungeonTestCommand? command;
            try { command = JsonSerializer.Deserialize<DungeonTestCommand>(await File.ReadAllBytesAsync(claimed, ct), Json); }
            catch (JsonException) { return; }
            if (command is null) return;
            var now = DateTime.UtcNow.ToFileTimeUtc();
            var reason = !Enabled ? "disabled" : command.Schema != "dfl-server-test-command/v1" ? "schema"
                : command.SessionNonce != SessionNonce ? "session" : command.RequestId <= 0 ? "request-id"
                : command.IssuedFiletime < now - 50_000_000 || command.IssuedFiletime > now + 2_500_000 ? "expired"
                : command.Operation != "relocate-ground" ? "operation" : _requests.Contains(command.RequestId) ? "duplicate"
                : _requests.Count >= 65536 ? "request-capacity" : "";
            if (reason.Length == 0)
            {
                _requests.Add(command.RequestId);
                var instance = instances.Instances.FirstOrDefault(value => value.TestRunId == command.RunId);
                reason = instance is null ? "run" : instance.RelocateTestGround(command);
            }
            var reply = new DungeonTestCommandResult("dfl-server-test-command-result/v1", command.RequestId,
                command.SessionNonce, command.RunId, command.RoomGeneration, reason.Length == 0 ? "applied" : "rejected", reason);
            await WriteAsync(Path.Combine(directory, "dfdebug-test-server-command-result.json"), JsonSerializer.SerializeToUtf8Bytes(reply, Json), ct);
        }
        finally { File.Delete(claimed); }
    }
    private static async Task WriteAsync(string path, byte[] bytes, CancellationToken ct)
    {
        await File.WriteAllBytesAsync(path + ".tmp", bytes, ct);
        File.Move(path + ".tmp", path, true);
    }
}
