using System.Buffers.Binary;
using System.Text.Json;
using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;

internal static class TestControlSmokeTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        await CheckActiveDropProjectionAsync(check);
        await CheckPersistenceBoundaryAsync(check);
        // Removing either activation guard must make the disabled cases fail.
        foreach (var flags in new[] { (false, false), (false, true), (true, false), (true, true) })
            await CheckPublisherAsync(check, flags.Item1, flags.Item2);
    }

    private static async Task CheckActiveDropProjectionAsync(Action<bool, string> check)
    {
        // This catches missing/provenance-confused JSON fields, loss of equipment bytes,
        // and diagnostic code inventing zero values for absent preserved metadata.
        var directory = Path.GetFullPath("../../../../../.tmp/smoke-tests/test-control-projection/"
            + Guid.NewGuid().ToString("N"), AppContext.BaseDirectory);
        Directory.CreateDirectory(directory);
        var options = JsonSerializer.Deserialize<ServerOptions>(JsonSerializer.Serialize(new
        {
            DataPath = Path.Combine(directory, "state.json"),
            Debug = new { TestControl = new { Enabled = true }, ClientOverlay = new { Enabled = true } }
        }))!;
        var store = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance);
        await store.InitializeAsync();
        var character = (await store.GetCharactersAsync("test"))[0];
        var preserved = new CharacterItemRecord(9, 50011, 1, State: 13, Durability: 11,
            SealState: 1, EquipmentQualitySeed: 0xF1234567, InstanceId: Guid.NewGuid());
        var noQuality = new CharacterItemRecord(10, 50014, 1, State: 2, Durability: 7,
            SealState: 3, InstanceId: Guid.NewGuid());
        character = (await store.SaveCharacterInventoryAsync("test", character.Id,
            character.Gold, [preserved, noQuality], []))!;
        using var fixture = new DungeonFixture();
        var instance = fixture.CreatePartyInstance(61030);
        instance.AddMember(fixture.Member(character.Id, 2, "projection") with { AreaUserId = 74, AccountName = "test" });
        instance.TryStartRun(fixture.Layout, 0, 0, out _, fixture.Random);
        var active = instance.AddPlayerDroppedGroundItem(preserved, 74, 0, 0, 123, 234);
        var generated = instance.AddGroundItem(new(true, 0, 23), 91, 5, 0, 0, 345, 456);
        var registry = new DungeonInstanceRegistry();
        registry.BindCharacter(character.Id, instance);
        var publisher = new DungeonTestControl(options, registry, store, null, null);
        var version = instance.Version;
        await publisher.PublishAsync(directory, [], CancellationToken.None);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "dfdebug-test-server.json")));
        var snapshot = document.RootElement.GetProperty("instances")[0];
        check(snapshot.GetProperty("members")[0].TryGetProperty("alive", out var living) && living.GetBoolean(),
            "test member projection starts with the actual alive state");
        check(instance.TrySetPartyMemberAlive(character.Id, false, DateTimeOffset.UtcNow), "projection fixture enters normal member-death state");
        await publisher.PublishAsync(directory, [], CancellationToken.None);
        using (var deadDocument = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "dfdebug-test-server.json"))))
            check(deadDocument.RootElement.GetProperty("instances")[0].GetProperty("members")[0].TryGetProperty("alive", out var dead) && !dead.GetBoolean(),
                "test member projection reports authoritative death rather than stale saved health");
        check(instance.TrySetPartyMemberAlive(character.Id, true, DateTimeOffset.UtcNow), "projection fixture revives through the normal member state");
        version = instance.Version;
        await publisher.PublishAsync(directory, [], CancellationToken.None);
        using (var revivedDocument = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "dfdebug-test-server.json"))))
            check(revivedDocument.RootElement.GetProperty("instances")[0].GetProperty("members")[0].TryGetProperty("alive", out var revived) && revived.GetBoolean(),
                "test member projection reports revival without mutating the member");
        var grounds = snapshot.GetProperty("ground_items").EnumerateArray().ToArray();
        var activeRow = grounds.Single(row => row.GetProperty("ground_id").GetUInt16() == active.GroundId);
        var generatedRow = grounds.Single(row => row.GetProperty("ground_id").GetUInt16() == generated.GroundId);
        check(Number(activeRow, "owner_uid", 74) && Number(activeRow, "routing_source", 0)
                && activeRow.GetProperty("instance_id").GetGuid() == preserved.InstanceId,
            "published active ground exposes actual owner UID, source0 and preserved instance identity");
        check(Number(activeRow, "state", 13) && Number(activeRow, "durability", 11)
                && Number(activeRow, "seal_state", 1) && Number(activeRow, "equipment_quality_seed", 0xF1234567),
            "published active ground preserves reinforcement, durability, seal and unsigned quality seed");
        check(Number(generatedRow, "owner_uid", 91) && Number(generatedRow, "routing_source", 1)
                && Null(generatedRow, "state") && Null(generatedRow, "durability")
                && Null(generatedRow, "seal_state") && Null(generatedRow, "equipment_quality_seed")
                && generatedRow.GetProperty("instance_id").GetGuid() == Guid.Empty,
            "generated gold retains source1 provenance and null absent preserved metadata");
        var inventory = snapshot.GetProperty("inventories")[0].GetProperty("items").EnumerateArray().ToArray();
        var itemRow = inventory.Single(row => row.GetProperty("slot").GetUInt16() == 9);
        var nullRow = inventory.Single(row => row.GetProperty("slot").GetUInt16() == 10);
        check(Number(itemRow, "state", 13) && Number(itemRow, "durability", 11)
                && Number(itemRow, "seal_state", 1) && Number(itemRow, "equipment_quality_seed", 0xF1234567)
                && itemRow.GetProperty("instance_id").GetGuid() == preserved.InstanceId,
            "published persisted inventory exposes the same equipment bytes and instance identity");
        check(Number(nullRow, "state", 2) && Number(nullRow, "durability", 7)
                && Number(nullRow, "seal_state", 3) && Null(nullRow, "equipment_quality_seed"),
            "inventory projection keeps a null quality seed nullable rather than fabricating zero");
        var after = await store.GetCharacterAsync("test", character.Id);
        check(instance.Version == version && instance.TryGetGroundItem(active.GroundId, 0, 0, out var unchanged)
                && ReferenceEquals(unchanged, active) && (after!.Inventory ?? []).SequenceEqual(character.Inventory ?? []),
            "publishing equipment diagnostics does not mutate ground identity, instance version or persisted inventory");
        Console.WriteLine($"Test control projection artifacts: {directory}");
        var solo = fixture.CreateSoloInstance(character.Id);
        solo.AddMember(fixture.Member(character.Id, 2, "projection") with { AreaUserId = 74, AccountName = "test" });
        solo.TryStartRun(fixture.Layout, 0, 0, out _, fixture.Random);
        var soloRegistry = new DungeonInstanceRegistry();soloRegistry.BindCharacter(character.Id, solo);
        await new DungeonTestControl(options, soloRegistry, store, null, null).PublishAsync(directory, [], CancellationToken.None);
        using (var soloDocument = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "dfdebug-test-server.json"))))
            check(soloDocument.RootElement.GetProperty("instances")[0].GetProperty("members")[0].TryGetProperty("alive", out var soloLife)
                    && soloLife.ValueKind == JsonValueKind.Null,
                "solo session-private life is explicitly unknown rather than invented alive");

        static bool Number(JsonElement row, string key, uint expected) => row.TryGetProperty(key, out var value)
            && value.ValueKind == JsonValueKind.Number && value.TryGetUInt32(out var actual) && actual == expected;
        static bool Null(JsonElement row, string key) => row.TryGetProperty(key, out var value)
            && value.ValueKind == JsonValueKind.Null;
    }

    private static async Task CheckPersistenceBoundaryAsync(Action<bool, string> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "df-test-persistence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var options = new ServerOptions { DataPath = Path.Combine(directory, "state.json") };
            var store = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance);
            await store.InitializeAsync();
            var before = (await store.GetCharactersAsync("test"))[0];
            using var fixture = new DungeonFixture();
            var instance = fixture.CreateSoloInstance(before.Id);
            instance.AddMember(fixture.Member(before.Id, 0, before.Name));
            instance.TryStartRun(fixture.Layout, 0, 0, out _, fixture.Random);
            var preserved = new CharacterItemRecord(12, 1, 1, InstanceId: Guid.NewGuid());
            var ground = instance.AddGroundItem(new(false, 1, 1), 1, 1, 0, 0, 100, 100, preserved);
            var context = instance.CaptureTestLootContext(ground, Guid.NewGuid(), 1, 1, new byte[] { 42, 0, 0, 0 });
            var adopted = false;
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            IEnumerable<CharacterItemRecord> BlockedRows()
            {
                entered.TrySetResult();
                release.Task.GetAwaiter().GetResult();
                yield return preserved;
            }
            var save = DungeonTestLootPersistence.SaveAndInstallAsync(
                () => Task.Run(() => store.SaveCharacterInventoryAsync("test", before.Id, before.Gold, BlockedRows(), before.Equipment ?? [], default)),
                _ => adopted = true,
                saved => instance.RecordTestLootResult(context, true, "persisted", 12, saved.Inventory!.Single().InstanceId));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            check(!adopted && instance.CaptureTestLootResults().Length == 0 && !save.IsCompleted,
                "an in-flight real store write cannot publish a committed loot result or install inventory");
            release.TrySetResult();
            var persisted = await save;
            var reloaded = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance);
            await reloaded.InitializeAsync();
            var savedCharacter = await reloaded.GetCharacterAsync("test", before.Id);
            var committed = instance.CaptureTestLootResults().Single();
            check(adopted && persisted is not null && committed.Status == "committed"
                && committed.DestinationSlot == 12 && committed.InstanceId == preserved.InstanceId
                && savedCharacter!.Inventory!.Single().InstanceId == preserved.InstanceId,
                "commit event follows actual disk persistence and session installation with the saved destination identity");
            var count = instance.CaptureTestLootResults().Length;
            var rejected = await DungeonTestLootPersistence.SaveAndInstallAsync(
                () => store.SaveCharacterInventoryAsync("test", Guid.NewGuid(), 0, [], [], default),
                _ => throw new InvalidOperationException("rejected persistence adopted state"),
                _ => instance.RecordTestLootResult(context, true, "persisted"));
            check(rejected is null && instance.CaptureTestLootResults().Length == count,
                "null business rejection from the real store cannot create a committed event");
            var failed = false;
            using var blockedFile = new FileStream(options.DataPath + ".tmp", FileMode.Create, FileAccess.ReadWrite, FileShare.None);
            try
            {
                await DungeonTestLootPersistence.SaveAndInstallAsync(
                    () => store.SaveCharacterInventoryAsync("test", before.Id, before.Gold, new[] { preserved }, before.Equipment ?? [], default),
                    _ => throw new InvalidOperationException("failed write adopted state"),
                    _ => instance.RecordTestLootResult(context, true, "persisted"));
            }
            catch (IOException) { failed = true; }
            check(failed && instance.CaptureTestLootResults().Length == count,
                "real store write failure cannot create a committed loot event");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static async Task CheckPublisherAsync(Action<bool, string> check, bool control, bool overlay)
    {
        using var fixture = new DungeonFixture();
        var directory = Path.Combine(Path.GetTempPath(), "df-test-control-" + Guid.NewGuid().ToString("N"));
        var options = JsonSerializer.Deserialize<ServerOptions>(JsonSerializer.Serialize(new
        {
            Debug = new { TestControl = new { Enabled = control }, ClientOverlay = new { Enabled = overlay, SnapshotDirectory = directory } }
        }))!;
        var registry = new DungeonInstanceRegistry();
        var owner = Guid.NewGuid();
        var instance = fixture.CreateSoloInstance(owner);
        instance.AddMember(fixture.Member(owner, 0, "control") with { AccountName = "test" });
        instance.TryStartRun(fixture.Layout, 0, 0, out _, fixture.Random);
        registry.BindCharacter(owner, instance);
        var original = instance.AddGroundItem(new(false, 1, 1), 1, 1, 0, 0, 100, 100,
            new CharacterItemRecord(0, 1, 1, InstanceId: Guid.NewGuid()));
        using var service = new DungeonDebugOverlayService(options, registry, NullLogger<DungeonDebugOverlayService>.Instance);
        await service.StartAsync(CancellationToken.None);
        try
        {
            var path = Path.Combine(directory, "dfdebug-test-server.json");
            for (var tick = 0; tick < 30 && !File.Exists(path); ++tick) await Task.Delay(50);
            check(File.Exists(path) == (control && overlay), $"test state publication requires both explicit flags ({control}/{overlay})");
            var leasePath = Path.Combine(directory, "dfdebug-test-lease-1.bin");
            check(File.Exists(leasePath) == (control && overlay), "native control lease follows both activation guards");
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(directory);
                var disabledCommand = new { schema = "dfl-server-test-command/v1", request_id = 55, session_nonce = 1,
                    run_id = instance.TestRunId, room_generation = instance.RoomGeneration, operation = "relocate-ground",
                    issued_filetime = DateTime.UtcNow.ToFileTimeUtc(), user_id = 1,
                    placements = new[] { new { ground_id = original.GroundId, x = 999, y = 100 } } };
                var commandPath = Path.Combine(directory, "dfdebug-test-server-command.json");
                await File.WriteAllTextAsync(commandPath, JsonSerializer.Serialize(disabledCommand));
                await Task.Delay(350);
                check(instance.GetRoomGroundItems(0, 0).Single().X == 100 && File.Exists(commandPath)
                    && !File.Exists(leasePath), "disabled control never consumes or mutates a test command");
                return;
            }
            using var state = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            var root = state.RootElement;
            check(root.GetProperty("schema").GetString() == "dfl-server-test-control/v1"
                && root.GetProperty("server_pid").GetInt32() == Environment.ProcessId
                && root.GetProperty("session_nonce").GetUInt64() != 0, "published state binds the current process and random session");
            var lease = await File.ReadAllBytesAsync(leasePath);
            check(lease.Length == 40 && BinaryPrimitives.ReadUInt32LittleEndian(lease) == 0x4c545444
                && BinaryPrimitives.ReadUInt32LittleEndian(lease.AsSpan(4)) == 1
                && BinaryPrimitives.ReadUInt64LittleEndian(lease.AsSpan(8)) == root.GetProperty("session_nonce").GetUInt64()
                && BinaryPrimitives.ReadUInt32LittleEndian(lease.AsSpan(32)) == 1
                && BinaryPrimitives.ReadUInt32LittleEndian(lease.AsSpan(36)) == 1, "native lease is the exact 40-byte DTTL v1 contract");
            var view = root.GetProperty("instances")[0];
            var nonce = root.GetProperty("session_nonce").GetUInt64();
            var run = view.GetProperty("run_id").GetInt64();
            var generation = view.GetProperty("room_generation").GetInt64();
            async Task<JsonDocument> Send(long id, ulong session, long room, object[] placements, long? issued = null, ushort user = 1)
            {
                var command = new { schema = "dfl-server-test-command/v1", request_id = id, session_nonce = session,
                    run_id = run, room_generation = room, operation = "relocate-ground", issued_filetime = issued ?? DateTime.UtcNow.ToFileTimeUtc(),
                    user_id = user, placements };
                var commandPath = Path.Combine(directory, "dfdebug-test-server-command.json");
                await File.WriteAllTextAsync(commandPath + ".tmp", JsonSerializer.Serialize(command));
                File.Move(commandPath + ".tmp", commandPath, true);
                var resultPath = Path.Combine(directory, "dfdebug-test-server-command-result.json");
                for (var tick = 0; tick < 40; ++tick)
                {
                    if (File.Exists(resultPath))
                    {
                        var result = JsonDocument.Parse(await File.ReadAllTextAsync(resultPath));
                        if (result.RootElement.GetProperty("request_id").GetInt64() == id) return result;
                        result.Dispose();
                    }
                    await Task.Delay(50);
                }
                throw new InvalidOperationException("test control command had no bounded reply");
            }
            using var rejected = await Send(1, nonce + 1, generation, [new { ground_id = original.GroundId, x = 500, y = 200 }]);
            check(rejected.RootElement.GetProperty("status").GetString() == "rejected"
                && instance.GetRoomGroundItems(0, 0).Single().X == 100, "wrong session cannot relocate a ground item");
            using var atomic = await Send(2, nonce, generation, [new { ground_id = original.GroundId, x = 500, y = 200 }, new { ground_id = 999, x = 300, y = 200 }]);
            check(atomic.RootElement.GetProperty("status").GetString() == "rejected"
                && instance.GetRoomGroundItems(0, 0).Single().X == 100, "invalid batch leaves all original ground positions intact");
            using var applied = await Send(3, nonce, generation, [new { ground_id = original.GroundId, x = 500, y = 200 }]);
            var moved = instance.GetRoomGroundItems(0, 0).Single();
            check(applied.RootElement.GetProperty("status").GetString() == "applied" && moved.X == 500 && moved.Y == 200
                && moved.GroundId == original.GroundId && ReferenceEquals(moved.PreservedItem, original.PreservedItem)
                && moved.RoutingSource == original.RoutingSource, "relocation retains ground identity, exact item instance, and routing provenance");
            using var replay = await Send(3, nonce, generation, [new { ground_id = original.GroundId, x = 600, y = 200 }]);
            await Task.Delay(350);
            check(instance.GetRoomGroundItems(0, 0).Single().X == 500, "duplicate request does not reapply different placements");
            instance.TryBeginLootPickup(owner, moved, false, [], _ => 0, out var pending);
            using var reserved = await Send(4, nonce, generation, [new { ground_id = original.GroundId, x = 600, y = 200 }]);
            check(reserved.RootElement.GetProperty("reason").GetString() == "pickup-pending"
                && ReferenceEquals(instance.GetRoomGroundItems(0, 0).Single(), moved), "pending pickup reservation retains its exact original ground reference");
            instance.ReleaseLootPickup(moved, pending.PickupId);
            using var duplicate = await Send(5, nonce, generation, [new { ground_id = original.GroundId, x = 600, y = 200 }, new { ground_id = original.GroundId, x = 700, y = 200 }]);
            check(duplicate.RootElement.GetProperty("reason").GetString() == "duplicate-ground", "duplicate ground identifiers reject the whole batch");
            using var coordinates = await Send(6, nonce, generation, [new { ground_id = original.GroundId, x = -1, y = 200 }]);
            check(coordinates.RootElement.GetProperty("reason").GetString() == "coordinates", "invalid coordinates cannot mutate ground state");
            using var stale = await Send(7, nonce, generation + 1, [new { ground_id = original.GroundId, x = 600, y = 200 }]);
            check(stale.RootElement.GetProperty("status").GetString() == "rejected", "stale room generation rejects relocation");
            using var expired = await Send(8, nonce, generation, [new { ground_id = original.GroundId, x = 600, y = 200 }], issued: 0);
            check(expired.RootElement.GetProperty("reason").GetString() == "expired", "expired mailbox commands cannot mutate state");
            using var foreign = await Send(9, nonce, generation, [new { ground_id = original.GroundId, x = 600, y = 200 }], user: 2);
            check(foreign.RootElement.GetProperty("reason").GetString() == "member", "only current instance members may relocate ground");
            using var oversized = await Send(10, nonce, generation, Enumerable.Range(0, 257).Select(_ => (object)new { ground_id = original.GroundId, x = 600, y = 200 }).ToArray());
            check(oversized.RootElement.GetProperty("reason").GetString() == "placements", "oversized relocation batch cannot mutate state");
            var context = instance.CaptureTestLootContext(moved, Guid.NewGuid(), 1, 1, new byte[] { 17, 0, 0, 0 });
            instance.RecordTestLootResult(context, false, "persist-failed");
            instance.RecordTestLootResult(context, true, "persisted", 9, original.PreservedItem!.InstanceId);
            for (var tick = 0; tick < 30; ++tick)
            {
                using var updated = JsonDocument.Parse(await File.ReadAllTextAsync(path));
                var events = updated.RootElement.GetProperty("loot_results").EnumerateArray()
                    .Where(value => value.GetProperty("pickup_id").GetGuid() == context.PickupId).ToArray();
                if (events.Length == 2)
                {
                    check(!events[0].GetProperty("persisted").GetBoolean()
                        && events[0].GetProperty("status").GetString() == "rejected"
                        && events[0].GetProperty("reason").GetString() == "persist-failed"
                        && events[1].GetProperty("persisted").GetBoolean()
                        && events[1].GetProperty("status").GetString() == "committed"
                        && events[1].GetProperty("event_id").GetInt64() > events[0].GetProperty("event_id").GetInt64()
                        && !events[1].GetProperty("is_gold").GetBoolean()
                        && events[1].GetProperty("scores").ValueKind == JsonValueKind.Array
                        && events[1].GetProperty("scores").GetArrayLength() == 4
                        && events[1].GetProperty("scores")[0].GetInt32() == 17
                        && events[1].GetProperty("destination_slot").GetInt32() == 9
                        && events[1].GetProperty("instance_id").GetGuid() == original.PreservedItem.InstanceId,
                        "published loot journal distinguishes persistence outcome, destination identity, and monotonic event cursor");
                    break;
                }
                if (tick == 29) check(false, "loot result publication is bounded");
                await Task.Delay(50);
            }
            DungeonTestLootJournal.MarkPublicationFailure(context.PickupId, "IOException");
            check(DungeonTestLootJournal.Capture().Single(value => value.Context.PickupId == context.PickupId && value.Status == "committed").PublicationError == "IOException",
                "post-commit publication failure retains the persisted outcome and separate transport evidence");
            for (var i = 0; i < 260; ++i) instance.RecordTestLootResult(context, false, "not-available");
            var bounded = instance.CaptureTestLootResults();
            check(bounded.Length == 256 && bounded[0].EventId < bounded[^1].EventId, "loot result retention is bounded without resetting event identity");
            instance.StopRun();
            for (var tick = 0; tick < 30 && File.Exists(leasePath); ++tick) await Task.Delay(50);
            check(!File.Exists(leasePath), "leaving a run revokes its native lease");
            registry.UnbindCharacter(owner);
            await Task.Delay(350);
            using var afterDeparture = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            check(afterDeparture.RootElement.GetProperty("instances").GetArrayLength() == 0
                && afterDeparture.RootElement.GetProperty("loot_results").EnumerateArray().Any(value => value.GetProperty("pickup_id").GetGuid() == context.PickupId),
                "bounded process journal survives last member departure and destroyed instance");
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
