using System.Buffers.Binary;
using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Party roster removal and scene departure are alternative actor-removal
/// signals. This pins protocol deduplication; the dead spectator's native
/// follow-target lifetime is independently verified by the client regression.
/// </summary>
internal static class DungeonDepartureSmokeTests
{
    public static async Task RunAsync(Action<bool, string> check, DungeonFixture fixture)
    {
        await CheckGiveUpRemovalAsync(check, fixture, leaderLeaves: false);
        await CheckGiveUpRemovalAsync(check, fixture, leaderLeaves: true);
        await CheckPartyRetainingReturnAsync(check, fixture);
        await CheckLastMemberReturnAsync(check, fixture);
    }

    private static async Task CheckGiveUpRemovalAsync(
        Action<bool, string> check, DungeonFixture fixture, bool leaderLeaves)
    {
        await using var harness = await PartyHarness.CreateAsync();
        await harness.InviteAndAcceptAsync("阿尔法", "布拉沃");
        var leaverName = leaderLeaves ? "阿尔法" : "布拉沃";
        var survivorName = leaderLeaves ? "布拉沃" : "阿尔法";
        var leaver = harness.IdOf(leaverName);
        var survivor = harness.IdOf(survivorName);
        var leavingUid = harness.UidOf(leaverName);
        var leavingSlot = leaderLeaves ? 0 : 1;
        var registry = new DungeonInstanceRegistry();
        var partyId = harness.Service.GetPartySlots(leaver)![0].PartyId;
        var instance = fixture.CreatePartyInstance(partyId);
        using var service = CreateService(harness, registry);
        var scenePackets = CreateSceneInboxes();
        AddMembers(harness, instance, scenePackets);
        registry.BindCharacter(leaver, instance);
        registry.BindCharacter(survivor, instance);
        check(instance.TryStartRun(fixture.Layout, 0, 0, out _),
            "the departure regression starts a shared dungeon run");
        check(instance.TrySetPartyMemberAlive(survivor, false, DateTimeOffset.UtcNow),
            "the teammate remaining in the dungeon is dead, matching the crash trigger");
        harness.ClearAll();

        var removedByRoster = await service.AbandonPartyForDungeonGiveUpAsync(leaver);
        var detached = registry.UnbindCharacter(leaver);
        check(removedByRoster && ReferenceEquals(detached, instance),
            "a real give-up removes party membership before detaching the dungeon instance");
        await service.NotifyMemberLeftAsync(
            instance, leaver, leaverName, leavingUid, 120, 340, 3,
            partyRosterAlreadyRemovesActor: removedByRoster);

        var received = harness.Of(survivorName).Concat(scenePackets[survivorName]).ToArray();
        var rosters = received.Where(packet =>
            packet.ProtocolId == GameProtocolEngine.PartyInfoNotification).ToArray();
        check(rosters.Length > 0 && rosters.All(packet => packet.Payload.Length == 20
                && BinaryPrimitives.ReadUInt16LittleEndian(
                    packet.Payload.AsSpan(7 + leavingSlot * 3, 2)) == ushort.MaxValue),
            "the surviving dead teammate receives the real roster with the departing actor's slot removed");
        check(received.All(packet => packet.ProtocolId != GameProtocolEngine.UserAreaNotification),
            $"{(leaderLeaves ? "leader" : "member")} give-up does not send USER_AREA after the roster already removed its actor");
        check(received.All(packet => packet.ProtocolId != GameProtocolEngine.WalkoutNotification)
                && harness.Service.GetPartySlots(leaver) is null,
            "give-up still uses the official silent membership removal rather than a walkout or retained party");
        check(instance.ContainsMember(survivor) && !instance.ContainsMember(leaver)
                && instance.PartyWipeDeadline is not null,
            "the dead teammate remains bound and starts its normal all-dead return after the last living member leaves");
    }

    private static async Task CheckPartyRetainingReturnAsync(
        Action<bool, string> check, DungeonFixture fixture)
    {
        await using var harness = await PartyHarness.CreateAsync();
        await harness.InviteAndAcceptAsync("阿尔法", "布拉沃");
        var leaver = harness.IdOf("布拉沃");
        var survivor = harness.IdOf("阿尔法");
        var registry = new DungeonInstanceRegistry();
        var instance = fixture.CreatePartyInstance(harness.Service.GetPartySlots(leaver)![0].PartyId);
        using var service = CreateService(harness, registry);
        var scenePackets = CreateSceneInboxes();
        AddMembers(harness, instance, scenePackets);
        registry.BindCharacter(leaver, instance);
        registry.BindCharacter(survivor, instance);
        harness.ClearAll();

        // Settlement and wipe returns retain party membership. There is no
        // changed roster to remove the foreign actor from the old dungeon.
        registry.UnbindCharacter(leaver);
        await service.NotifyMemberLeftAsync(
            instance, leaver, "布拉沃", harness.UidOf("布拉沃"), 120, 340, 3);

        var received = scenePackets["阿尔法"];
        check(received.Count == 1 && harness.Of("阿尔法").Count == 0
                && received[0].ProtocolId == GameProtocolEngine.UserAreaNotification
                && received[0].Payload.Length == 10
                && BinaryPrimitives.ReadUInt16LittleEndian(received[0].Payload) == harness.UidOf("布拉沃")
                && received[0].Payload[2] == byte.MaxValue && received[0].Payload[3] == byte.MaxValue,
            "a party-retaining return still sends exactly one FF/FF scene departure because no roster removed the actor");
        check(harness.Service.GetPartySlots(leaver) is { Length: 2 }
                && harness.Of("布拉沃").Count == 0 && scenePackets["布拉沃"].Count == 0,
            "the scene-only departure neither drops party membership nor sends the leaver their own departure");
    }

    private static async Task CheckLastMemberReturnAsync(
        Action<bool, string> check, DungeonFixture fixture)
    {
        await using var harness = await PartyHarness.CreateAsync();
        await harness.InviteAndAcceptAsync("阿尔法", "布拉沃");
        await harness.Service.HandleLeaveAsync(harness.IdOf("布拉沃"), CancellationToken.None);
        var owner = harness.IdOf("阿尔法");
        var registry = new DungeonInstanceRegistry();
        var instance = fixture.CreatePartyInstance(harness.Service.GetPartySlots(owner)![0].PartyId);
        using var service = CreateService(harness, registry);
        var scenePackets = new List<GameServerPacket>();
        instance.AddMember(new DungeonInstanceMember(owner, harness.UidOf("阿尔法"), 0,
            "阿尔法", packet => { scenePackets.Add(packet); return ValueTask.CompletedTask; }));
        registry.BindCharacter(owner, instance);
        harness.ClearAll();

        var removedByRoster = await service.AbandonPartyForDungeonGiveUpAsync(owner);
        registry.UnbindCharacter(owner);
        await service.NotifyMemberLeftAsync(
            instance, owner, "阿尔法", harness.UidOf("阿尔法"), 120, 340, 3,
            partyRosterAlreadyRemovesActor: removedByRoster);

        check(!removedByRoster && harness.Service.GetPartySlots(owner) is { Length: 1 }
                && harness.Of("阿尔法").Count == 0 && scenePackets.Count == 0,
            "the last member keeps their party and has no peer to receive a roster or scene departure");
    }

    private static DungeonPartyService CreateService(PartyHarness harness, DungeonInstanceRegistry registry) =>
        new(harness.Service, harness.Directory, registry, TimeProvider.System,
            NullLogger<DungeonPartyService>.Instance);

    private static Dictionary<string, List<GameServerPacket>> CreateSceneInboxes() =>
        new() { ["阿尔法"] = [], ["布拉沃"] = [] };

    private static void AddMembers(
        PartyHarness harness,
        DungeonInstance instance,
        Dictionary<string, List<GameServerPacket>> scenePackets)
    {
        foreach (var (name, slot) in new[] { ("阿尔法", (byte)0), ("布拉沃", (byte)1) })
        {
            instance.AddMember(new DungeonInstanceMember(harness.IdOf(name), harness.UidOf(name), slot,
                name, packet => { scenePackets[name].Add(packet); return ValueTask.CompletedTask; }));
        }
    }
}
