using System.Buffers.Binary;
using System.Text.Json;
using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;

internal static class MentorGoldExternalDeliverySmokeTests
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        foreach (var sale in new[] { false, true })
        foreach (var during in new[] { false, true })
            await CheckAsync(check, sale, during);
        await CheckAsync(check, sale: true, during: false, secondSale: true);
    }

    private static async Task CheckAsync(Action<bool, string> check, bool sale, bool during, bool secondSale = false)
    {
        await using var f = await MentorGoldTestFixture.CreateAsync(commerce: true, configure: (root, master, child) =>
            MentorGoldTestFixture.CharacterNode(root, master)["Inventory"] = JsonSerializer.SerializeToNode(new[]
                { new CharacterItemRecord(73, TradeFixtures.PotionItemId, 3, InstanceId: Guid.NewGuid()) }));
        Directory.CreateDirectory(Path.Combine(f.Harness.Root, "town")); Directory.CreateDirectory(Path.Combine(f.Harness.Root, "map"));
        File.WriteAllText(Path.Combine(f.Harness.Root, "town/town.lst"), "1 `test.twn`\n");
        File.WriteAllText(Path.Combine(f.Harness.Root, "town/test.twn"), "[area]\n0\n`test.map`\n`[normal]`\n[/area]\n");
        File.WriteAllText(Path.Combine(f.Harness.Root, "map/test.map"), "[virtual movable area]\n0 190 1600 200\n[/virtual movable area]\n");
        var town = new TownSessionDirectory(f.Harness.Options, TimeProvider.System, NullLogger<TownSessionDirectory>.Instance);
        var items = TradeFixtures.CreateItems(f.Harness.Root); var stats = TradeFixtures.CreateStats(f.Harness.Root);
        var party = new PartyService(f.Store, town, TimeProvider.System, NullLogger<PartyService>.Instance);
        using var trade = new ItemTradeService(f.Store, items, stats, town, TimeProvider.System, NullLogger<ItemTradeService>.Instance);
        var catalog = new PrivateStoreCatalog(new ScriptFileSystem(f.Harness.Options, NullLogger<ScriptFileSystem>.Instance));
        using var shop = new PrivateStoreService(f.Store, items, catalog, town, party, NullLogger<PrivateStoreService>.Instance);
        var packets = new List<GameServerPacket>(); var gold = f.Master.Gold;
        using var gate = new SemaphoreSlim(1, 1);
        var queue = new OwnerCurrencyDeliveryQueue(() => { });
        var sampled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var accepted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowExternal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var deltaStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowDelta = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstSaleSending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowFirstSale = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondAccepted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var externalCount = 0; var salesSent = 0;
        GameServerPacket? captured = null;
        async ValueTask Send(GameServerPacket p)
        {
            if (p.Type == 0 && p.ProtocolId == 76 && during)
            { deltaStarted.TrySetResult(); await allowDelta.Task; }
            if (secondSale && p.Type == 0 && p.ProtocolId == 82 && p.Payload[0] == 1 && ++salesSent == 1)
            { firstSaleSending.TrySetResult(); await allowFirstSale.Task; }
            lock (packets) packets.Add(p);
        }
        async ValueTask External(GameServerPacket p)
        {
            if (p.Type == 0 && (sale ? p.ProtocolId == 82 && p.Payload[0] == 1 : p.ProtocolId == 14))
            {
                captured = p; sampled.TrySetResult();
                if (!during) await allowExternal.Task;
            }
            if (sale) await queue.SendPrivateStoreAsync(p, Send); else await queue.SendTradeAsync(p, Send);
            if (p == captured)
            {
                if (Interlocked.Increment(ref externalCount) == 2) secondAccepted.TrySetResult();
                accepted.TrySetResult();
            }
        }
        ValueTask Ignore(GameServerPacket _) => ValueTask.CompletedTask;
        await town.EnterAsync(new(1, 0, 1, f.Master.Id, f.Master.CharacterNo, [65], f.Master.Name, null, 1000, 250, 0,
            (p, _) => External(p)), default);
        await town.EnterAsync(new(1, 0, 2, f.Child.Id, f.Child.CharacterNo, [66], f.Child.Name, null, 1200, 250, 0,
            (p, _) => Ignore(p)), default);
        if (sale)
        {
            await shop.AttachChannelAsync("alice", f.Master.Id, 1, f.MasterSession, External);
            await shop.AttachChannelAsync("bobby", f.Child.Id, 2, f.ChildSession, Ignore);
            await f.Store.GrantPremiumServiceToAccountAsync("alice", 8, 1);
            check(await shop.CreateAsync(f.Master.Id, f.MasterSession, new(1, 0, 1000, 250)), "R1 actual seller shop creates");
            check(await shop.DisplayAsync(f.Master.Id, f.MasterSession, [83], [new(0, 8, 0, 73, 3)]) is not null,
                "R1 actual seller stock displays");
            await shop.EnterAsync(f.Child.Id, f.ChildSession, 1);
        }
        else
        {
            await trade.AttachChannelAsync("alice", f.Master.Name, f.Master.Id, 1, f.MasterSession, External);
            await trade.AttachChannelAsync("bobby", f.Child.Name, f.Child.Id, 2, f.ChildSession, Ignore);
            await trade.HandleRequestAsync(f.Master.Id, 2, 0);
            await trade.HandleResponseAsync(f.Child.Id, 1, true, 0);
        }
        lock (packets) packets.Clear();
        await f.Pickup(); await f.Flush();
        Task Mentor() => MentorGoldSessionRefresh.ApplyAsync(f.Store, f.Master.Id, f.MasterToken, gate,
            () => Task.CompletedTask, c => gold = c.Gold, Send,
            () => GameProtocolEngine.CreateMainItemList((uint)gold, 0, []), default);
        Task Drain() => queue.DrainAsync(f.Store, "alice", f.Master.Id, gate, () => Task.CompletedTask,
            c => gold = c.Gold, Send, default);
        Task Trigger() => sale ? shop.BuyAsync(f.Child.Id, f.ChildSession, 0, 1) : trade.CancelForAreaChangeAsync(f.Child.Id);
        if (!during)
        {
            var external = Trigger(); await sampled.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await Mentor(); allowExternal.TrySetResult(); await external; await accepted.Task.WaitAsync(TimeSpan.FromSeconds(3));
            // Seller outbox is independent of the buyer; await its callback's completion via a barrier below.
        }
        else
        {
            var mentor = Mentor(); await deltaStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await Trigger(); await accepted.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var drain = Drain();
            allowDelta.TrySetResult(); await mentor; await drain;
        }
        if (secondSale)
        {
            var drain = Drain(); await firstSaleSending.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await Trigger(); await secondAccepted.Task.WaitAsync(TimeSpan.FromSeconds(3));
            allowFirstSale.TrySetResult(); await drain;
        }
        await Drain();
        var canonical = await f.ReadMaster();
        GameServerPacket[] delivered; lock (packets) delivered = packets.ToArray();
        var hud = f.Master.Gold;
        foreach (var p in delivered.Where(p => p.Type == 0))
            if (p.ProtocolId == 76) hud += (int)BinaryPrimitives.ReadUInt32LittleEndian(p.Payload);
            else if (p.ProtocolId is 13 or 14) hud = BinaryPrimitives.ReadInt32LittleEndian(p.Payload.AsSpan(7));
            else if (p.ProtocolId == 82 && p.Payload[0] == 1) hud = BinaryPrimitives.ReadInt32LittleEndian(p.Payload.AsSpan(1));
        var bearing = delivered.Where(p => p.Type == 0 && (p.ProtocolId is 76 or 13 or 14 || p.ProtocolId == 82 && p.Payload[0] == 1)).ToArray();
        var expectedOrder = secondSale ? new byte[] { 76, 13, 82, 82 } : new byte[] { 76, 13, sale ? (byte)82 : (byte)14 };
        check(hud == canonical.Gold && canonical.Gold == f.Master.Gold + 14 + (sale ? secondSale ? 16 : 8 : 0)
            && bearing.Select(p => p.ProtocolId).SequenceEqual(expectedOrder),
            $"R1 actual {(sale ? "seller NOTI82" : "trade cancellation NOTI14")} {(secondSale ? "second purchase during owner delivery" : during ? "during mentor delta" : "sampled before mentor, delivered late")} uses owner ordering and canonical final HUD");
        if (sale)
        {
            var notice = delivered.Last(p => p.Type == 0 && p.ProtocolId == 82 && p.Payload[0] == 1);
            check(captured is not null && notice.Payload.AsSpan(5).SequenceEqual(captured.Payload.AsSpan(5)),
                "R1 seller notice preserves exact item, slot, quantity, buyer, gross and fee bytes");
        }
        else check(delivered.Any(p => p.Type == 0 && p.ProtocolId == 16), "R1 actual trade cancellation still emits its cancel notification");
    }
}
