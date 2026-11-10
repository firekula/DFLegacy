using System.Text.Json;
using System.Buffers.Binary;
using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

internal static class AdminRewardStoreSmokeTests
{
    private sealed class Writer(bool after = false) : IDungeonRewardSnapshotWriter
    {
        internal bool Armed;
        internal int Calls;
        public async Task WriteAsync(string path, ReadOnlyMemory<byte> bytes, CancellationToken ct)
        {
            Calls++;
            if (Armed && !after) throw new IOException("admin before replacement");
            await new DungeonRewardSnapshotWriter().WriteAsync(path, bytes, ct);
            if (Armed) throw new IOException("admin after replacement");
        }
    }
    internal sealed class Fixture : IDisposable
    {
        internal readonly string Root = Directory.CreateTempSubdirectory("dflegacy-admin-rewards-").FullName;
        internal readonly ServerOptions Options;
        internal readonly ItemCatalog Items;
        internal Fixture()
        {
            Directory.CreateDirectory(Path.Combine(Root, "stackable"));
            Directory.CreateDirectory(Path.Combine(Root, "equipment"));
            File.WriteAllText(Path.Combine(Root, "stackable/stackable.lst"), "1000 `potion.stk`\n");
            File.WriteAllText(Path.Combine(Root, "stackable/potion.stk"), "[name] `Potion`\n[stackable type] `[usable]`\n[stack limit] 1000\n");
            File.WriteAllText(Path.Combine(Root, "equipment/equipment.lst"), "200 `weapon.equ`\n");
            File.WriteAllText(Path.Combine(Root, "equipment/weapon.equ"), "[name] `Weapon`\n[equipment type] `[weapon]`\n[attach type] `[sealing]`\n[durability] 30\n");
            Options = new() { DataPath = Path.Combine(Root, "state.json"), SeedTestAccount = false,
                ScriptPvfPath = Path.Combine(Root, "missing.pvf"), SkillScriptPath = Root };
            Items = new(new ScriptFileSystem(Options, NullLogger<ScriptFileSystem>.Instance), NullLogger<ItemCatalog>.Instance);
        }
        internal JsonGameStore Open(IDungeonRewardSnapshotWriter? writer = null) => writer is null
            ? new(Options, NullLogger<JsonGameStore>.Instance, itemCatalog: Items)
            : new(Options, NullLogger<JsonGameStore>.Instance, writer, itemCatalog: Items);
        public void Dispose() => Directory.Delete(Root, true);
    }

    public static async Task RunAsync(Action<bool, string> check)
    {
        await AdminMailAttachmentSmokeTests.RunAsync(check);
        await CheckSessionAsync(check);
        using var f = new Fixture();
        var writer = new Writer();
        var store = f.Open(writer);
        await store.InitializeAsync();
        await store.CreateAccountAsync(new("rewards", "password", OicqCode: "123456"));
        var a = (await store.CreateCharacterAsync("rewards", new("One"))).Character!;
        var b = (await store.CreateCharacterAsync("rewards", new("Two"))).Character!;
        var op = Guid.NewGuid();
        var granted = await store.GrantAdminVictoryPointsAsync("gm", op, a.Id, 123);
        check(granted.Status == AdminOperationStatus.Completed && granted.Balance == 123, "GM VP grants update canonical spendable points");
        var same = await store.GrantAdminVictoryPointsAsync("GM", op, a.Id, 123);
        check(same.Replayed && same.Balance == 123, "VP operation is actor-case-insensitive and idempotent");
        check((await store.GrantAdminVictoryPointsAsync("gm", op, a.Id, 124)).Error == "operation-conflict"
            && (await store.GrantAdminVictoryPointsAsync("other", op, a.Id, 123)).Error == "operation-conflict", "VP operation cannot be reused for another payload or actor");
        check((await store.GrantAdminVictoryPointsAsync("gm", Guid.NewGuid(), a.Id, 0)).Status == AdminOperationStatus.Rejected
            && (await store.GrantAdminVictoryPointsAsync("gm", Guid.NewGuid(), a.Id, uint.MaxValue)).Error == "victory-points-overflow",
            "VP zero and overflow grants are rejected");
        await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => store.GrantAdminVictoryPointsAsync("gm", Guid.NewGuid(), a.Id, 1)));
        var repeatedId = Guid.NewGuid();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => store.GrantAdminVictoryPointsAsync("gm", repeatedId, a.Id, 7)));
        var current = (await store.GetCharacterAsync("rewards", a.Id))!;
        check(current.VictoryPoints == 142 && current.PvpPoints == 0 && current.PvpGrade == 0 && current.PvpWins == 0,
            "concurrent grants add once and leave rating/grade/record unchanged");
        await store.SaveCharacterInventoryAsync("rewards", a.Id, a.Gold, a.Inventory ?? [], a.Equipment ?? []);
        check((await store.GetCharacterAsync("rewards", a.Id))!.VictoryPoints == 142, "older inventory save preserves GM-granted VP");
        var rating = new PvpExperienceCatalog(new ScriptFileSystem(f.Options, NullLogger<ScriptFileSystem>.Instance), NullLogger<PvpExperienceCatalog>.Instance);
        var settlement = await store.CommitPvpMatchAsync(new(Guid.NewGuid(),
            [new(a.Id, 0, false, new ushort[11], new uint[8]), new(b.Id, 0, false, new ushort[11], new uint[8])], a.Id, false), rating);
        check(settlement.Status == DungeonRewardLedgerStatus.Committed
            && (await store.GetCharacterAsync("rewards", a.Id))!.VictoryPoints == 146,
            "PvP settlement adds its award to the latest GM-granted balance");

        var mail = new AdminSendMailRequest("GM", "Bulk", 17, AdminMailAttachmentKind.Item, 200, 1);
        var full = (await store.CreateCharacterAsync("rewards", new("Full"))).Character!;
        for (var i = 0; i < 255; i++) await store.SendCharacterMailAsync(full.Id, new("Old", "Full mailbox"));
        var batch = Guid.NewGuid();
        var prepared = await store.PrepareAdminMailBatchAsync("gm", batch, mail);
        check(prepared.Status == AdminOperationStatus.Prepared && prepared.TargetCount == 3
            && (await store.GetCharacterMailsAsync(a.Id)).Length == 0, "durable preview freezes existing characters without sending");
        var late = (await store.CreateCharacterAsync("rewards", new("Late"))).Character!;
        var writesBefore = writer.Calls;
        var sent = await store.SendAdminMailBatchAsync("gm", batch);
        check(sent.SentCount == 2 && sent.FailedCount == 1 && writer.Calls == writesBefore + 1,
            "batch performs one snapshot write and reports full mailbox separately");
        var aMail = (await store.GetCharacterMailsAsync(a.Id)).Single();
        var bMail = (await store.GetCharacterMailsAsync(b.Id)).Single();
        check(aMail.Attachment!.InstanceId != Guid.Empty && aMail.Attachment.InstanceId != bMail.Attachment!.InstanceId
            && aMail.Gold == 17 && bMail.Gold == 17, "each recipient receives an independent equipment identity and full gold");
        check((await store.GetCharacterMailsAsync(late.Id)).Length == 0, "characters created after preview are excluded");
        check((await store.SendAdminMailBatchAsync("gm", batch)).Replayed
            && (await store.GetCharacterMailsAsync(a.Id)).Length == 1, "repeat confirmation does not redeliver mail");
        check((await store.PrepareAdminMailBatchAsync("gm", batch, mail with { Gold = 18 })).Error == "operation-conflict",
            "broadcast operation content cannot change");
        check((await store.GrantAdminVictoryPointsAsync("gm", batch, a.Id, 1)).Error == "operation-conflict",
            "reward operation IDs cannot cross operation types");
        var retryA = Guid.NewGuid(); var retryB = Guid.NewGuid();
        check((await store.PrepareAdminMailBatchAsync("gm", retryA, mail, batch)).TargetCount == 1
            && (await store.PrepareAdminMailBatchAsync("gm", retryB, mail, batch)).TargetCount == 1,
            "retries include only original unsuccessful recipients");
        await store.DeleteCharacterMailAsync(full.Id, (await store.GetCharacterMailsAsync(full.Id))[0].Id);
        var retries = await Task.WhenAll(store.SendAdminMailBatchAsync("gm", retryA), store.SendAdminMailBatchAsync("gm", retryB));
        check(retries.Sum(r => r.SentCount) == 1 && retries.Sum(r => r.SkippedCount) == 1
            && (await store.GetCharacterMailsAsync(full.Id)).Count(m => m.Text == "Bulk") == 1,
            "two prepared retries cannot redeliver the same failed recipient");
        check((await store.PrepareAdminMailBatchAsync("gm", Guid.NewGuid(), mail, batch)).TargetCount == 0,
            "stale retry of original batch excludes recipients delivered by later retries");
        store = f.Open(); await store.InitializeAsync();
        check((await store.GrantAdminVictoryPointsAsync("gm", op, a.Id, 123)).Replayed
            && (await store.GetAdminMailBatchAsync("gm", batch)).SentCount == 2,
            "normal saves, PvP settlement and restart preserve both operator ledgers");
        var deleteBatch = Guid.NewGuid();
        await store.PrepareAdminMailBatchAsync("gm", deleteBatch, new("GM", "Deleted target"));
        var lateNow = (await store.GetCharacterAsync("rewards", late.Id))!;
        await store.DeleteCharacterAsync("rewards", lateNow.Slot);
        var deletedResult = await store.SendAdminMailBatchAsync("gm", deleteBatch);
        check(deletedResult.Deliveries.Any(d => d.CharacterId == late.Id && d.Error == "character-not-found"),
            "deleted preview recipient is reported without aborting other deliveries");
        foreach (var after in new[] { false, true })
            await CheckFaultAsync(check, after);
        using var emptyFixture = new Fixture();
        var empty = emptyFixture.Open(); await empty.InitializeAsync();
        var emptyId = Guid.NewGuid();
        check((await empty.PrepareAdminMailBatchAsync("gm", emptyId, new("GM", "Empty"))).TargetCount == 0
            && (await empty.SendAdminMailBatchAsync("gm", emptyId)).Status == AdminOperationStatus.Completed,
            "empty broadcast safely completes with zero recipients");
    }

    private static async Task CheckSessionAsync(Action<bool, string> check)
    {
        using var f = new Fixture(); var store = f.Open(); await store.InitializeAsync();
        await store.CreateAccountAsync(new("online", "password", OicqCode: "123456"));
        var c = (await store.CreateCharacterAsync("online", new("OnlineReward"))).Character!;
        await using var services = NewCharacterDefaultsSmokeTests.CreateSessionServices(f.Options, store, f.Items);
        var sessions = services.GetRequiredService<CharacterSessionRegistry>();
        var service = ActivatorUtilities.CreateInstance<EntranceService>(services);
        await using (var connection = await NewCharacterDefaultsSmokeTests.Connection.CreateAsync(service, f.Options))
        {
            await connection.LoginAsync("online", c.Slot);
            await store.GrantAdminVictoryPointsAsync("gm", Guid.NewGuid(), c.Id, 321);
            check(sessions.NotifyVictoryPointsChanged(c.Id), "GM VP invalidation reaches the numeric character lease");
            var refresh = await connection.ReadAsync(0, GameProtocolEngine.ItemListNotification);
            check(refresh.Payload.Length >= 27
                && BinaryPrimitives.ReadUInt16LittleEndian(refresh.Payload.AsSpan(15)) == 2
                && BinaryPrimitives.ReadUInt16LittleEndian(refresh.Payload.AsSpan(17)) == 2
                && BinaryPrimitives.ReadUInt32LittleEndian(refresh.Payload.AsSpan(19)) == 321,
                "idle encrypted game session receives canonical VP in slot2 without another client command");
            await store.GrantAdminVictoryPointsAsync("gm", Guid.NewGuid(), c.Id, 17);
            sessions.NotifyVictoryPointsChanged(c.Id);
            var second = await connection.ReadAsync(0, GameProtocolEngine.ItemListNotification);
            check(BinaryPrimitives.ReadUInt32LittleEndian(second.Payload.AsSpan(19)) == 338,
                "subsequent online VP refresh preserves both grants");
        }
        check((await store.GetCharacterAsync("online", c.Id))!.VictoryPoints == 338,
            "game disconnect cannot overwrite GM victory points");
        check(!sessions.NotifyVictoryPointsChanged(c.Id), "released game lease receives no VP notifications");
    }

    private static async Task CheckFaultAsync(Action<bool, string> check, bool after)
    {
        using var f = new Fixture(); var writer = new Writer(after);
        var store = f.Open(writer); await store.InitializeAsync();
        await store.CreateAccountAsync(new("fault", "password", OicqCode: "123456"));
        var c = (await store.CreateCharacterAsync("fault", new("Fault"))).Character!;
        var id = Guid.NewGuid(); writer.Armed = true;
        var result = await store.GrantAdminVictoryPointsAsync("gm", id, c.Id, 37);
        check(result.Status == AdminOperationStatus.Unknown
            && (await store.GetCharacterAsync("fault", c.Id))!.VictoryPoints == 0,
            $"VP snapshot failure afterReplace={after} does not install unconfirmed memory state");
        store = f.Open(); await store.InitializeAsync();
        var retry = await store.GrantAdminVictoryPointsAsync("gm", id, c.Id, 37);
        check(retry.Status == AdminOperationStatus.Completed && retry.Replayed == after
            && (await store.GetCharacterAsync("fault", c.Id))!.VictoryPoints == 37,
            $"VP snapshot recovery afterReplace={after} applies grant exactly once");
        writer.Armed = false; store = f.Open(writer); await store.InitializeAsync();
        var batch = Guid.NewGuid(); await store.PrepareAdminMailBatchAsync("gm", batch, new("GM", "Fault mail", 7));
        writer.Armed = true; var sent = await store.SendAdminMailBatchAsync("gm", batch);
        check(sent.Status == AdminOperationStatus.Unknown && (await store.GetCharacterMailsAsync(c.Id)).Length == 0,
            $"batch snapshot failure afterReplace={after} does not install unconfirmed mail");
        store = f.Open(); await store.InitializeAsync();
        var recovered = await store.SendAdminMailBatchAsync("gm", batch);
        check(recovered.Status == AdminOperationStatus.Completed && recovered.Replayed == after
            && (await store.GetCharacterMailsAsync(c.Id)).Length == 1,
            $"batch snapshot recovery afterReplace={after} delivers exactly once");
    }
}
