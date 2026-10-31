using System.Buffers.Binary;
using DFLegacy.Server;
using DFLegacy.Protocol;
using Microsoft.Extensions.Logging.Abstractions;

internal static class MentorGoldLifecycleSmokeTests
{
    internal static async Task RunAsync(Action<bool,string> check)
    {
        foreach (var forced in new[] { false, true })
        {
            await using var f = await MentorGoldTestFixture.CreateAsync();
            var registry = new CharacterSessionRegistry(NullLogger<CharacterSessionRegistry>.Instance);
            var kicked = false;
            var old = await registry.ClaimAsync(f.Child.CharacterNo, f.Child.Id, f.ChildSession, () => kicked = true, default);
            await f.Pickup();
            var incoming = forced ? registry.ClaimAsync(f.Child.CharacterNo, f.Child.Id, Guid.NewGuid(), () => { }, default) : null;
            check(forced ? kicked && old.WasReplaced && !incoming!.IsCompleted : !old.WasReplaced,
                $"actual lease retirement distinguishes forced={forced} before old actor finally");
            var saved = await f.Store.SaveCharacterLocationAsync("bobby", f.Child.Id, 1, 1, 512, 240, 5);
            await f.Service.DetachChannelAsync(f.Child.Id, f.ChildSession, f.ChildToken,
                MentorGoldSessionRefresh.CaptureRetirement(old));
            await f.Service.DetachChannelAsync(f.Child.Id, f.ChildSession, f.ChildToken, () => MentorGoldRetirementReason.NormalLeave);
            registry.Release(old);
            if (incoming is not null)
            {
                var nextLease = await incoming;
                var reloaded = (await f.Store.GetCharacterByNameAsync(f.Child.Name))!;
                f.ChildToken = (await f.Service.AttachChannelAsync("bobby", f.Child.Id, 2, nextLease.SessionId,
                    _ => ValueTask.CompletedTask))!.Value;
                check(reloaded.X == 512 && (await f.Flush()).Receipt is null,
                    "incoming ClaimAsync reload and new attachment see final saved state without inheriting the retired pool");
                registry.Release(nextLease);
            }
            var awards = (await f.ReadMaster()).MentorGoldAwards;
            check(saved is not null && (await f.Store.GetCharacterByNameAsync(f.Child.Name))!.X == 512
                && (forced ? awards is null : awards is [{ CreditedGold: 14 }]),
                $"actual replacement chain forced={forced} preserves final save and correctly discards or flushes once before release");
        }
        await using (var f = await MentorGoldTestFixture.CreateAsync())
        {
            await f.Pickup();
            await f.Service.DetachChannelAsync(f.Master.Id, f.MasterSession);
            f.MasterToken = (await f.Service.AttachChannelAsync("alice", f.Master.Id, 1, f.MasterSession, _ => ValueTask.CompletedTask))!.Value;
            check((await f.Flush()).Receipt is null, "master logout/relogin discards the previous attachment's child pool");
            await f.Service.DetachChannelAsync(f.Master.Id, f.MasterSession);
            await f.Pickup();
            f.MasterToken = (await f.Service.AttachChannelAsync("alice", f.Master.Id, 1, f.MasterSession, _ => ValueTask.CompletedTask))!.Value;
            check((await f.Flush()).Receipt is null, "sources earned while master offline never catch up on login");
            await f.Pickup();
            await f.Store.RemoveMentorAsync(f.Child.Id, f.Master.Name, DateTimeOffset.UtcNow);
            await f.Store.EstablishMentorAsync(f.Child.Id, f.Master.Id, DateTimeOffset.UtcNow.AddDays(2));
            check((await f.Flush()).Receipt is null, "canonical removal and same-parent edge recreation discards basis immediately");
        }
        await using (var f = await MentorGoldTestFixture.CreateAsync(rand: () => 1000))
        {
            // Native normal branch second sample1000 => jitter100: growth is independent of tax700.
            await f.Pickup(); f.Clock.Advance(600); await f.Service.SweepChangesAsync();
            check((await f.ReadMaster()).MentorGoldAwards is null,
                "relationship growth at600 does not pay a tax timer due at700");
            f.Clock.Advance(100); await f.Service.SweepChangesAsync();
            check((await f.ReadMaster()).MentorGoldAwards is [{ Basis: 1000 }], "independent native delay flushes at700");
            await f.Pickup(); f.Clock.Advance(4000); await f.Service.SweepChangesAsync(); await f.Service.SweepChangesAsync();
            check((await f.ReadMaster()).MentorGoldAwards?.Count == 2, "late tax sweep handles one current period without catch-up");
        }
        await CheckDeliveryAsync(check);
        await CheckBlockedRetirementAsync(check);
        await CheckSocketDisconnectAsync(check);
        await CheckTradeDeliveryRaceAsync(check);
        await CheckCurrencySerializationAsync(check);
    }

    private static async Task CheckDeliveryAsync(Action<bool,string> check)
    {
        await using var f = await MentorGoldTestFixture.CreateAsync();
        await f.Pickup(); await f.Flush();
        var packets = new List<GameServerPacket>();
        var actor = f.Master with { Cash = 321, VictoryPoints = 654, Skills = [new(0, 4, 3)] };
        using var gate = new SemaphoreSlim(1, 1);
        using var dungeon = new DungeonFixture();
        GameServerPacket Absolute() => EntranceService.CreateMainInventoryRefresh(actor.Gold, actor.VictoryPoints, [], dungeon.Items);
        await MentorGoldSessionRefresh.ApplyAsync(f.Store, f.Master.Id, f.MasterToken, gate,
            () => Task.CompletedTask,
            saved => actor = actor with { Gold = saved.Gold, MentorGoldAwards = saved.MentorGoldAwards },
            p => { packets.Add(p); return ValueTask.CompletedTask; },
            Absolute, CancellationToken.None);
        var hud = f.Master.Gold;
        foreach (var packet in packets)
            hud = packet.ProtocolId == 76 ? checked(hud + (int)BinaryPrimitives.ReadUInt32LittleEndian(packet.Payload))
                : BinaryPrimitives.ReadInt32LittleEndian(packet.Payload.AsSpan(7));
        check(packets.Select(p => p.ProtocolId).SequenceEqual(new byte[] { 76, 13 })
            && hud == f.Master.Gold + 14 && actor.Gold == hud && actor.Cash == 321 && actor.VictoryPoints == 654
            && actor.Skills!.Single().Level == 3,
            "real session refresh sends NOTI76 delta before absolute and only installs Gold/receipt, preserving actor fields");
        packets.Clear();
        await MentorGoldSessionRefresh.ApplyAsync(f.Store, f.Master.Id, f.MasterToken, gate, () => Task.CompletedTask,
            _ => throw new Exception("duplicate install"), p => { packets.Add(p); return ValueTask.CompletedTask; }, Absolute, default);
        check(packets.Count == 0, "repeated or reordered invalidations cannot emit another incremental NOTI76");
        await f.Pickup(); await f.Flush();
        await f.Service.DetachChannelAsync(f.Master.Id, f.MasterSession);
        await MentorGoldSessionRefresh.ApplyAsync(f.Store, f.Master.Id, f.MasterToken, gate, () => Task.CompletedTask,
            _ => throw new Exception("old actor install"), p => { packets.Add(p); return ValueTask.CompletedTask; }, Absolute, default);
        var login = await f.Store.ConsumeMentorGoldCreditsAsync(f.Master.Id);
        check(packets.Count == 0 && login.Character?.Gold == f.Master.Gold + 28,
            "retired master cannot consume or emit; prelogin consumes already-awarded credit without old popup");
    }

    private sealed class BlockingWriter : IDungeonRewardSnapshotWriter
    {
        internal bool Block;
        internal TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Continue = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task WriteAsync(string path, ReadOnlyMemory<byte> snapshot, CancellationToken ct)
        {
            if (Block) { Entered.TrySetResult(); await Continue.Task.WaitAsync(ct); }
            await new DungeonRewardSnapshotWriter().WriteAsync(path, snapshot, ct);
        }
    }
    private static async Task CheckBlockedRetirementAsync(Action<bool,string> check)
    {
        var writer = new BlockingWriter();
        await using var f = await MentorGoldTestFixture.CreateAsync(writer: writer);
        var registry = new CharacterSessionRegistry(NullLogger<CharacterSessionRegistry>.Instance);
        var old = await registry.ClaimAsync(f.Child.CharacterNo, f.Child.Id, f.ChildSession, () => { }, default);
        await f.Pickup();
        writer.Block = true;
        var source = f.Pickup(0);
        await writer.Entered.Task;
        var retirement = f.Service.DetachChannelAsync(f.Child.Id, f.ChildSession, f.ChildToken,
            MentorGoldSessionRefresh.CaptureRetirement(old));
        var incoming = registry.ClaimAsync(f.Child.CharacterNo, f.Child.Id, Guid.NewGuid(), () => { }, default);
        check(!retirement.IsCompleted && old.WasReplaced && !incoming.IsCompleted,
            "retirement waits behind the actual store gate while a real replacement signal arrives");
        writer.Continue.TrySetResult(); await source; await retirement;
        registry.Release(old); registry.Release(await incoming);
        check((await f.ReadMaster()).MentorGoldAwards is null,
            "gate-time retirement probe observes replacement during its wait and discards instead of flushing");
    }
    private static async Task CheckSocketDisconnectAsync(Action<bool,string> check)
    {
        await using var f = await MentorGoldTestFixture.CreateAsync();
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            var accept = listener.AcceptTcpClientAsync();
            await client.ConnectAsync((System.Net.IPEndPoint)listener.LocalEndpoint);
            using var server = await accept;
            var registry = new CharacterSessionRegistry(NullLogger<CharacterSessionRegistry>.Instance);
            var lease = await registry.ClaimAsync(f.Child.CharacterNo, f.Child.Id, f.ChildSession, () => server.Close(), default);
            await f.Pickup();
            async Task Owner()
            {
                try { if (await server.GetStream().ReadAsync(new byte[1]) != 0) throw new IOException("Expected socket EOF."); }
                finally
                {
                    await f.Service.DetachChannelAsync(f.Child.Id, f.ChildSession, f.ChildToken,
                        MentorGoldSessionRefresh.CaptureRetirement(lease));
                    registry.Release(lease);
                }
            }
            var owner = Owner(); client.Close(); await owner;
            check(!lease.WasReplaced && (await f.ReadMaster()).MentorGoldAwards is [{ CreditedGold: 14 }],
                "ordinary socket EOF drives normal owner teardown and single departure flush, distinct from replacement kick");
        }
        finally { listener.Stop(); }
    }

    private static async Task CheckTradeDeliveryRaceAsync(Action<bool,string> check)
    {
        await using var f = await MentorGoldTestFixture.CreateAsync(commerce: true);
        await f.Pickup(); await f.Service.DetachChannelAsync(f.Child.Id, f.ChildSession);
        var trade = await f.Store.CommitItemTradeAsync(new(f.Master.Id, "alice", f.Child.Id, "bobby", [], [], 0, 0));
        check(trade.Committed, "real trade creates a pending installation marker before the master consumes its award");
        var packets = new List<GameServerPacket>(); var gold = f.Master.Gold;
        using var gate = new SemaphoreSlim(1, 1);
        async Task Process()
        {
            await MentorGoldSessionRefresh.ApplyNoticeAsync(f.Service, f.Store, f.Master.Id, f.MasterToken, gate,
                () => Task.CompletedTask, c => gold = c.Gold,
                p => { packets.Add(p); return ValueTask.CompletedTask; },
                () => GameProtocolEngine.CreateMainItemList((uint)gold, 0, []), CancellationToken.None);
        }
        await Process();
        check(packets.Count == 0 && f.Service.HasPendingGold(f.Master.Id, f.MasterToken),
            "an award waiting behind a not-yet-delivered trade keeps its invalidation instead of silently losing the wakeup");
        f.Store.AcknowledgeTradeDelivery(f.Master.Id, trade.TradeId);
        await Process();
        check(gold == f.Master.Gold + 14 && packets.Select(p => p.ProtocolId).SequenceEqual(new byte[] { 76, 13 }),
            "the actor retries a deferred notice after trade installation and publishes its actual award once");
    }

    private static async Task CheckCurrencySerializationAsync(Action<bool,string> check)
    {
        await using var f = await MentorGoldTestFixture.CreateAsync();
        await f.Pickup(); await f.Flush();
        using var gate = new SemaphoreSlim(1, 1);
        var deltaStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var continueDelta = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gold = f.Master.Gold; var competingEntered = false;
        var packets = new List<GameServerPacket>();
        async ValueTask Send(GameServerPacket p)
        {
            packets.Add(p);
            if (p.ProtocolId == 76) { deltaStarted.TrySetResult(); await continueDelta.Task; }
        }
        var refresh = MentorGoldSessionRefresh.ApplyAsync(f.Store, f.Master.Id, f.MasterToken, gate,
            () => Task.CompletedTask, c => gold = c.Gold, Send,
            () => GameProtocolEngine.CreateMainItemList((uint)gold, 0, []), default);
        await deltaStarted.Task;
        async Task CompetingCurrencyCallback()
        {
            await gate.WaitAsync();
            try
            {
                competingEntered = true;
                var canonical = await f.ReadMaster();
                var saved = await f.Store.SaveCharacterInventoryAsync("alice", f.Master.Id, canonical.Gold + 7,
                    canonical.Inventory ?? [], canonical.Equipment ?? []);
                gold = saved!.Gold;
                packets.Add(GameProtocolEngine.CreateMainItemList((uint)gold, 0, []));
            }
            finally { gate.Release(); }
        }
        var competing = CompetingCurrencyCallback();
        check(!competingEntered && !competing.IsCompleted, "competing currency callback cannot pass between NOTI76 and its absolute refresh");
        continueDelta.TrySetResult(); await refresh; await competing;
        var hud = f.Master.Gold;
        foreach (var packet in packets) hud = packet.ProtocolId == 76
            ? hud + (int)BinaryPrimitives.ReadUInt32LittleEndian(packet.Payload)
            : BinaryPrimitives.ReadInt32LittleEndian(packet.Payload.AsSpan(7));
        check(packets.Select(p => p.ProtocolId).SequenceEqual(new byte[] { 76, 13, 13 }) && hud == f.Master.Gold + 21
            && hud == (await f.ReadMaster()).Gold,
            "actual serialized increment/set packet sequence converges with a concurrent successful currency save");
    }
}
