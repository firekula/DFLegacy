using System.Text;
using DFLegacy.Protocol;
using DFLegacy.Server;

internal static class DungeonLootGatherSmokeTests
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        using var fixture = new DungeonFixture();
        var characterId = Guid.NewGuid();
        var requesterPackets = new List<GameServerPacket>();
        var otherPackets = new List<GameServerPacket>();
        ValueTask SendSelf(GameServerPacket packet)
        {
            requesterPackets.Add(packet);
            return ValueTask.CompletedTask;
        }
        ValueTask SendOther(GameServerPacket packet)
        {
            otherPackets.Add(packet);
            return ValueTask.CompletedTask;
        }
        var instance = fixture.CreateSoloInstance(characterId);
        instance.AddMember(new(characterId, 0x1234, 0, "ordinary player", SendSelf, AccountName: "ordinary-player"));
        instance.TryStartRun(fixture.Layout, 0, 0, out _);
        var other = fixture.CreateSoloInstance(Guid.NewGuid());
        other.AddMember(new(other.SoloCharacterId, 0x4321, 0, "other player", SendOther));
        other.TryStartRun(fixture.Layout, 0, 0, out _);
        var party = fixture.CreatePartyInstance(2003);
        party.AddMember(new(characterId, 0x1234, 0, "ordinary player", SendSelf));
        party.AddMember(new(other.SoloCharacterId, 0x4321, 1, "teammate", SendOther));
        party.TryStartRun(fixture.Layout, 0, 0, out _);
        var singleMemberParty = fixture.CreatePartyInstance(2004);
        singleMemberParty.AddMember(new(characterId, 0x1234, 0, "remaining party member", SendSelf));
        singleMemberParty.TryStartRun(fixture.Layout, 0, 0, out _);
        var inactive = fixture.CreateSoloInstance(characterId);
        var tutorial = fixture.CreateSoloInstance(characterId);
        tutorial.EnterDetachedRoom(DungeonFixture.Room([]));

        // These are the actual CMD 17 parser's type-3 GBK bytes for 牛来.
        byte[] request = [0, 0, 3, 0, 0, 0, 0, 0, 0, 4, 0, 0, 0, 0xC5, 0xA3, 0xC0, 0xB4];
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        check(EntranceService.TryReadSendMessage(request, out var parsed), "the native normal-map chat command parses");
        var text = Encoding.GetEncoding(936).GetString(parsed.MessageBytes);
        var drop = instance.AddGroundItem(new DungeonGeneratedDrop(true, 0, 123), 0x1234, 9, 0, 0, 111, 222);
        instance.RegisterRoomPassiveDrops(DungeonFixture.Room([], passiveDropCount: 1), 0x1234, 0, 0);
        var groundBefore = instance.GetRoomGroundItems(0, 0);
        var before = instance.Snapshot();
        check(instance.TryBeginLootPickup(characterId, drop, false, [new(0, characterId, 0x1234)], _ => 0, out var pending)
                && pending.Outcome == LootPickupOutcome.Granted && pending.PickupId != Guid.Empty,
            "the fixture reserves an existing ground item for pickup");
        check(await DungeonLootGatherCommand.TrySendAsync(parsed.MessageType, text, instance, characterId,
                0x1234, alive: true, inPvp: false, SendSelf),
            "an ordinary account can request one gather while alive in its active solo dungeon");
        // Wire contract is shared with the native marker parser. The length is 25,
        // the local user id is little-endian, and no nul or sender prefix is added.
        byte[] expected = [16, 0x34, 0x12, 25, 0, 0, 0,
            64, 68, 70, 76, 101, 103, 97, 99, 121, 58, 108, 111, 111, 116, 45, 109, 97, 103, 110, 101, 116, 58, 118, 49, 64];
        check(requesterPackets is [var notification] && notification.Type == 0 && notification.ProtocolId == 12
                && notification.Payload.SequenceEqual(expected) && otherPackets.Count == 0,
            "gather sends exactly one private Type16 marker with the requester uid and exact native ASCII bytes");
        check(instance.Snapshot().Version == before.Version && instance.GetRoomGroundItems(0, 0).SequenceEqual(groundBefore)
                && !instance.TryBeginLootPickup(characterId, drop, false, [], _ => 0, out var retry)
                && retry.Outcome == LootPickupOutcome.PickupInProgress,
            "gather neither grants items nor changes active/passive ground identity, coordinates, quantity or pending pickup");
        check(instance.TryClaimGroundItem(drop.GroundId, 0, 0, out var claimed, drop, pending.PickupId)
                && ReferenceEquals(claimed, drop),
            "the original room/id/pending pickup remains valid without server physics coordinate changes");

        foreach (var (messageType, message, run, who, alive, inPvp) in new[]
        {
            ((byte)3, " 牛来", instance, characterId, true, false),
            ((byte)3, "牛来 ", instance, characterId, true, false),
            ((byte)3, "牛来!", instance, characterId, true, false),
            ((byte)3, "牛來", instance, characterId, true, false),
            ((byte)2, "牛来", instance, characterId, true, false),
            ((byte)1, "牛来", instance, characterId, true, false),
            ((byte)7, "牛来", instance, characterId, true, false),
            ((byte)16, "牛来", instance, characterId, true, false),
            ((byte)18, "牛来", instance, characterId, true, false),
            ((byte)3, "牛来", party, characterId, true, false),
            ((byte)3, "牛来", singleMemberParty, characterId, true, false),
            ((byte)3, "牛来", inactive, characterId, true, false),
            ((byte)3, "牛来", tutorial, characterId, true, false),
            ((byte)3, "牛来", (DungeonInstance?)null, characterId, true, false),
            ((byte)3, "牛来", instance, Guid.NewGuid(), true, false),
            ((byte)3, "牛来", instance, characterId, false, false),
            ((byte)3, "牛来", instance, characterId, true, true),
        })
        {
            requesterPackets.Clear();
            check(!await DungeonLootGatherCommand.TrySendAsync(messageType, message, run, who, 0x1234,
                    alive, inPvp, SendSelf) && requesterPackets.Count == 0 && otherPackets.Count == 0,
                $"gather rejects non-exact/type/party/town/dead/PvP/foreign-run context: {messageType}, '{message}', {run?.PartyId}, {alive}, {inPvp}");
        }

        requesterPackets.Clear();
        var newDrop = instance.AddGroundItem(new DungeonGeneratedDrop(true, 0, 456), 0x1234, 10, 0, 0, 333, 444);
        check(await DungeonLootGatherCommand.TrySendAsync(3, "牛来", instance, characterId, 0x1234, true, false, SendSelf)
                && await DungeonLootGatherCommand.TrySendAsync(3, "牛来", instance, characterId, 0x1234, true, false, SendSelf)
                && requesterPackets.Count == 2 && instance.GetRoomGroundItems(0, 0).Contains(newDrop),
            "repeat commands each send one gather so newly spawned items can be moved without removing them");
        instance.TryMoveRoom(1, 0, out _);
        instance.TryMoveRoom(2, 0, out _);
        foreach (var monster in instance.RunCurrentRoom!.Monsters) instance.TryTakeMonsterDeath(monster.UniqueId, out _);
        instance.TryEnableClear();
        check(await DungeonLootGatherCommand.TrySendAsync(3, "牛来", instance, characterId, 0x1234, true, false, SendSelf)
                && requesterPackets.Count == 3 && instance.ClearEnabled,
            "the completed solo boss room still accepts gather while it retains final settlement state");
        instance.StopRun();
        requesterPackets.Clear();
        check(!await DungeonLootGatherCommand.TrySendAsync(3, "牛来", instance, characterId, 0x1234, true, false, SendSelf)
                && requesterPackets.Count == 0,
            "a stopped run cannot authorize a gather through its stale instance");
    }
}
