using System.Net;
using System.Net.Sockets;
using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;

internal static class EntranceWeaknessRecoverySmokeTests
{
    private static readonly GameCharacterCombatStats Stats = new(
        14_400, 19_600, 110, 220, 330, 440, 1, 2, 3, 4,
        410_000, 5, 6, 8_500, 8_600, 7_000, 6_000, 4_300, 500_000);

    public static async Task RunAsync(Action<bool, string> check)
    {
        await using var harness = await PartyHarness.CreateAsync();
        await CheckPassiveEntryAsync(check, harness);
        await CheckLifecycleAsync(check, harness);
        await CheckLoadingDeliveryAsync(check, harness);
        await CheckConcurrentLateDeliveryAsync(check);
    }

    private static async Task CheckConcurrentLateDeliveryAsync(Action<bool, string> check)
    {
        var noRegression = true;
        for (var round = 0; round < 1_000; round++)
        {
            var state = new EntranceWeaknessRecovery(new EntranceSessionWakeSignal());
            var releaseOldWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var old = state.BeginDungeonDelivery(async _ => await releaseOldWrite.Task);
            state.Notify(Guid.NewGuid());
            var oldCompletion = old(GameProtocolEngine.CreateFinishLoading()).AsTask();
            var newCompletion = Task.Run(async () =>
            {
                var current = state.BeginDungeonDelivery(_ => ValueTask.CompletedTask);
                await current(GameProtocolEngine.CreateFinishLoading());
            });
            var release = Task.Run(() => releaseOldWrite.TrySetResult());
            await Task.WhenAll(oldCompletion, newCompletion, release);
            noRegression &= state.CanProcessPending(true);
        }
        check(noRegression,
            "concurrent old-run NOTI 30 completion never rolls back a newer completed admission or strands pending recovery");
    }

    private static async Task CheckPassiveEntryAsync(Action<bool, string> check, PartyHarness harness)
    {
        var id = harness.IdOf("布拉沃");
        var uid = harness.UidOf("布拉沃");
        var current = (await harness.Store.SetCharacterWeaknessRecoveryAsync(id, 46))!;
        var before = PartyMemberDetailProjection.CreatePacket(uid, current, Stats, [], false);
        var wake = new EntranceSessionWakeSignal();
        var recovery = new EntranceWeaknessRecovery(wake);
        var sessions = new CharacterSessionRegistry(NullLogger<CharacterSessionRegistry>.Instance);
        var lease = await sessions.ClaimAsync(id, Guid.NewGuid(), () => { }, CancellationToken.None,
            weaknessRecovery: _ => recovery.Notify(id));
        try
        {
            // A real blocked TCP read remains untouched. The persisted minute
            // update and actual registry callback wake a completely passive peer.
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            using var peer = new TcpClient();
            await peer.ConnectAsync((IPEndPoint)listener.LocalEndpoint, deadline.Token);
            using var receiver = await listener.AcceptTcpClientAsync(deadline.Token);
            var pendingRead = receiver.GetStream().ReadAsync(new byte[1], deadline.Token).AsTask();
            var wait = EntranceSessionReadWait.WaitAsync(pendingRead, Timeout.InfiniteTimeSpan,
                pollPartyMailbox: false, deadline.Token, wakeTask: wake.Capture());
            var updated = (await harness.Store.AdvanceCharacterWeaknessRecoveryAsync(
                DateTimeOffset.UtcNow)).Single(update => update.CharacterId == id);
            check(updated.Recovery == 55 && sessions.NotifyWeaknessRecovery(id, updated.Recovery),
                "the real minute store update notifies the passive character's lease at recovery 55");
            check(await wait == EntranceReadWakeReason.PartyMailTick && !pendingRead.IsCompleted,
                "minute recovery wakes a passive session without receiving or restarting its TCP read");

            // The signal may predate the next Capture. It must also be visible
            // as pending work, rather than requiring another notification.
            check(recovery.CanProcessPending(false),
                "queued recovery remains discoverable even when its wake pulse preceded the next wait");
            var mail = new DungeonInstanceRegistry();
            mail.PostEntryRequest(id, new DungeonEntryRequest(1, 14, 0, 0, 1, 0,
                harness.IdOf("阿尔法")));
            var latest = await recovery.TakeLatestAsync(id, false, FindAsync, deadline.Token);
            current = EntranceWeaknessRecovery.WithRecoveryFrom(current, latest!);
            check(mail.TryTakeEntryRequest(id, out _) && current.WeaknessRecovery == 55,
                "a passive entry consumes current persisted recovery before building its actor detail");

            await harness.InviteAndAcceptAsync("布拉沃", "阿尔法");
            harness.ClearAll();
            var owner = GameProtocolEngine.CreateStamina(current.WeaknessRecovery);
            await harness.Service.SendToOtherMembersAsync(id,
                PartyMemberDetailProjection.CreatePacket(uid, current, Stats, [], false), deadline.Token);
            var foreign = harness.Of("阿尔法").Single();
            check(owner.Payload[0] == 55 && foreign.Payload[93] == owner.Payload[0]
                    && before.Payload[93] == 46,
                "actual party delivery uses the same recovery 55 as its owner's notification while prior details stay immutable");

            async Task<CharacterRecord?> FindAsync(Guid characterId, CancellationToken token) =>
                (await harness.Store.GetCharactersAsync("bobby", token))
                    .FirstOrDefault(character => character.Id == characterId);
        }
        finally
        {
            sessions.Release(lease);
        }
    }

    private static async Task CheckLifecycleAsync(Action<bool, string> check, PartyHarness harness)
    {
        var id = harness.IdOf("布拉沃");
        var otherId = harness.IdOf("阿尔法");
        var recovery = new EntranceWeaknessRecovery(new EntranceSessionWakeSignal());
        var sessions = new CharacterSessionRegistry(NullLogger<CharacterSessionRegistry>.Instance);
        Action<byte> delayedOldCallback = _ => recovery.Notify(id);
        var lease = await sessions.ClaimAsync(id, Guid.NewGuid(), () => { }, CancellationToken.None,
            weaknessRecovery: delayedOldCallback);
        var lookupCount = 0;

        await harness.Store.SetCharacterWeaknessRecoveryAsync(id, 91);
        var oldFullTick = (await harness.Store.AdvanceCharacterWeaknessRecoveryAsync(
            DateTimeOffset.UtcNow)).Single(update => update.CharacterId == id);
        var newWeakness = (await harness.Store.SetCharacterWeaknessRecoveryAsync(id, 10))!;
        sessions.NotifyWeaknessRecovery(id, oldFullTick.Recovery);
        var latest = await recovery.TakeLatestAsync(id, false, FindAsync, CancellationToken.None);
        check(oldFullTick.Recovery == 100 && latest?.WeaknessRecovery == 10,
            "a delayed full-recovery callback reads the newer wipe's persisted 10 rather than overwriting it with 100");

        await harness.Store.SetCharacterWeaknessRecoveryAsync(id, 46);
        var staleTrade = (await harness.Store.GetCharactersAsync("bobby")).Single(c => c.Id == id);
        var staleTick = (await harness.Store.AdvanceCharacterWeaknessRecoveryAsync(
            DateTimeOffset.UtcNow)).Single(update => update.CharacterId == id);
        sessions.NotifyWeaknessRecovery(id, staleTick.Recovery);
        var paid = await harness.Store.RecoverCharacterWeaknessAsync(id, recoveryCost: 0);
        latest = await recovery.TakeLatestAsync(id, false, FindAsync, CancellationToken.None);
        var live = EntranceWeaknessRecovery.WithRecoveryFrom(newWeakness, latest!);
        var tradeItems = new List<CharacterItemRecord>();
        var installed = EntranceWeaknessRecovery.WithRecoveryFrom(
            staleTrade with { Gold = 123, Inventory = tradeItems }, live);
        check(paid.Success && live.WeaknessRecovery == 100
                && installed.WeaknessRecovery == 100 && installed.Gold == 123
                && ReferenceEquals(installed.Inventory, tradeItems),
            "a queued old tick cannot undo paid recovery, and installing an older trade keeps recovery while adopting trade inventory/gold");
        check(ReferenceEquals(EntranceWeaknessRecovery.WithRecoveryFrom(live,
                live with { Id = otherId, WeaknessRecovery = 19 }), live),
            "recovery fields cannot be copied between different character identities");

        sessions.Release(lease);
        check(!sessions.NotifyWeaknessRecovery(id, 55),
            "releasing a character lease unsubscribes its normal minute notification");
        delayedOldCallback(100); // Already captured before release/switch.
        var previousLookups = lookupCount;
        check(await recovery.TakeLatestAsync(otherId, false, FindAsync, CancellationToken.None) is null
                && lookupCount == previousLookups,
            "an in-flight callback for the previous character is discarded without reading or altering the newly selected character");
        recovery.Notify(id);
        check(await recovery.TakeLatestAsync(null, false, FindAsync, CancellationToken.None) is null
                && !recovery.CanProcessPending(false),
            "returning to character selection drains obsolete recovery without leaving a pending notification");
        recovery.Notify(id);
        check(await recovery.TakeLatestAsync(id, false,
                (_, _) => Task.FromResult<CharacterRecord?>(live with { Id = otherId }),
                CancellationToken.None) is null,
            "a mismatched store result cannot supply another character's recovery");

        async Task<CharacterRecord?> FindAsync(Guid characterId, CancellationToken token)
        {
            lookupCount++;
            return (await harness.Store.GetCharactersAsync("bobby", token))
                .FirstOrDefault(character => character.Id == characterId);
        }
    }

    private static async Task CheckLoadingDeliveryAsync(Action<bool, string> check, PartyHarness harness)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var fixture = new DungeonFixture();
        using var service = new DungeonPartyService(harness.Service, harness.Directory,
            new DungeonInstanceRegistry(), TimeProvider.System, NullLogger<DungeonPartyService>.Instance);
        var instance = fixture.CreatePartyInstance(8123);
        var alpha = harness.IdOf("阿尔法");
        var bravo = harness.IdOf("布拉沃");
        var a = new EntranceWeaknessRecovery(new EntranceSessionWakeSignal());
        var b = new EntranceWeaknessRecovery(new EntranceSessionWakeSignal());
        var enteredSend = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSend = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var aSend = a.BeginDungeonDelivery(_ => ValueTask.CompletedTask);
        var bSend = b.BeginDungeonDelivery(async packet =>
        {
            if (packet.ProtocolId == GameProtocolEngine.FinishLoadingNotification)
            {
                enteredSend.TrySetResult();
                await releaseSend.Task.WaitAsync(deadline.Token);
            }
        });
        instance.AddMember(new DungeonInstanceMember(alpha, 1, 0, "A", aSend));
        instance.AddMember(new DungeonInstanceMember(bravo, 2, 1, "B", bSend));
        instance.TryStartRun(fixture.Layout, 0, 0, out _);
        a.Notify(alpha);
        b.Notify(bravo);
        var lookups = 0;
        check(!instance.TryMarkMemberLoaded(alpha)
                && await b.TakeLatestAsync(bravo, true, FindAsync, deadline.Token) is null
                && lookups == 0,
            "entry recovery stays frozen, including its fields, while another member has not finished loading");
        check(instance.TryMarkMemberLoaded(bravo), "the real loading barrier opens only after both members load");
        var broadcast = service.BroadcastToMembersAsync(instance,
            GameProtocolEngine.CreateFinishLoading(), omitCharacterId: null, deadline.Token);
        await enteredSend.Task.WaitAsync(deadline.Token);
        check(a.CanProcessPending(true) && !b.CanProcessPending(true)
                && await b.TakeLatestAsync(bravo, true, FindAsync, deadline.Token) is null
                && lookups == 0,
            "another recipient's NOTI 30 cannot release recovery before this recipient's write/flush finishes");
        releaseSend.TrySetResult();
        await broadcast;
        check(b.CanProcessPending(true)
                && (await b.TakeLatestAsync(bravo, true, FindAsync, deadline.Token))?.Id == bravo,
            "the recipient's successfully completed NOTI 30 releases the retained recovery for battle metadata notification");

        var solo = new EntranceWeaknessRecovery(new EntranceSessionWakeSignal());
        var oldRunSend = solo.BeginDungeonDelivery(_ => ValueTask.CompletedTask);
        var currentRunSend = solo.BeginDungeonDelivery(_ => ValueTask.CompletedTask);
        solo.Notify(bravo);
        await oldRunSend(GameProtocolEngine.CreateFinishLoading());
        await currentRunSend(GameProtocolEngine.CreateStamina(55));
        check(!solo.CanProcessPending(true),
            "a late old-run NOTI 30 and unrelated packets cannot release a newer solo admission");
        await currentRunSend(GameProtocolEngine.CreateFinishLoading());
        check(solo.CanProcessPending(true), "solo loading uses the same per-admission successful NOTI 30 boundary");

        var failed = solo.BeginDungeonDelivery(_ => throw new IOException("test failed write"));
        try { await failed(GameProtocolEngine.CreateFinishLoading()); }
        catch (IOException) { }
        check(!solo.CanProcessPending(true) && solo.CanProcessPending(false)
                && await solo.TakeLatestAsync(bravo, false, FindAsync, deadline.Token) is not null,
            "failed NOTI 30 does not release loading, while a later return to Town safely consumes the retained latest recovery");

        async Task<CharacterRecord?> FindAsync(Guid characterId, CancellationToken token)
        {
            lookups++;
            return (await harness.Store.GetCharactersAsync("bobby", token))
                .FirstOrDefault(character => character.Id == characterId);
        }
    }
}
