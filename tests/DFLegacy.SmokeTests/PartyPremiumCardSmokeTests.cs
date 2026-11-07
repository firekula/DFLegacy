using System.Buffers.Binary;
using System.Text.Json.Nodes;
using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;

internal static class PartyPremiumCardSmokeTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        CheckPackets(check, [new(0, true, 12056), new(2, false, null)]);
        CheckPackets(check, [new(0, false, null), new(1, true, 12057),
            new(2, false, null, CanSelect: false), new(3, true, 12059)]);
        CheckPackets(check, [new(0, true, null), new(2, true, 12058)]);
        await CheckRealStoreAsync(check);
        await CheckConcurrentRollsAsync(check);
    }

    private sealed record Member(byte Slot, bool Premium, ushort? Item, bool CanSelect = true);

    private static void CheckPackets(Action<bool, string> check, Member[] members)
    {
        foreach (var receiver in members)
        {
            var reward = BuildReward(receiver, members);
            var previews = ReadPremium(reward.Payload);
            var rights = BuildRights(receiver, members).Payload;
            for (byte slot = 0; slot < 4; slot++)
            {
                var owner = members.SingleOrDefault(member => member.Slot == slot);
                check(previews[slot] == (owner is { Premium: true } ? (true, owner.Item) : (false, null)),
                    $"{members.Length}-member receiver {receiver.Slot} sees slot {slot}'s own premium eligibility/item");
                var expectedRight = owner is null ? ushort.MaxValue : (ushort)(owner.CanSelect ? 1 : 0);
                check(BinaryPrimitives.ReadUInt16LittleEndian(rights.AsSpan(1 + 2 * slot)) == expectedRight,
                    $"{members.Length}-member receiver {receiver.Slot} sees slot {slot}'s actual card right (only holes are FFFF)");
            }
        }
    }

    private static GameServerPacket BuildReward(Member receiver, Member[] members)
    {
        var ids = members.ToDictionary(member => member.Slot, _ => Guid.NewGuid());
        var snapshot = new PartyClearRewardSnapshot(members.Select(member => new PartyClearRewardMember(
            ids[member.Slot], member.Slot, member.Premium, new(10, true, 111, null, member.Item, null))).ToArray());
        return snapshot.CreateReward(1, ids[receiver.Slot]);
    }

    private static GameServerPacket BuildRights(Member receiver, Member[] members) =>
        GameProtocolEngine.CreateCardSelectRightState(Enumerable.Range(0, 4).Select(slot =>
            members.SingleOrDefault(member => member.Slot == slot) is { } member
                ? (ushort)(member.CanSelect ? 1 : 0) : ushort.MaxValue).ToArray());

    private static async Task CheckRealStoreAsync(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "dflegacy-party-premium-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "etc"));
            Directory.CreateDirectory(Path.Combine(root, "equipment"));
            await File.WriteAllTextAsync(Path.Combine(root, "equipment/equipment.lst"), "5 `bonus.equ`\n");
            await File.WriteAllTextAsync(Path.Combine(root, "equipment/bonus.equ"),
                "[minimum level] 1\n[grade] 1\n[rarity] 0\n[creation rate] 100\n[equipment type] `[weapon]`\n");
            await File.WriteAllTextAsync(Path.Combine(root, "etc/itemdropinfo_clearreward.etc"),
                "[drop prob]\n`default`\n1 100 0\n`gold`\n1 100 0\n`event`\n1 100 0\n"
                + "`premium default`\n1 100 0\n`premium gold`\n1 100 0\n`premium bonus`\n1 100 10000\n[/drop prob]\n"
                + "[item drop ref table]\n1 0 1\n[/item drop ref table]\n"
                + "[basis of rarity dicision]\n10000 10000 10000 10000 10000\n[/basis of rarity dicision]\n");
            var options = new ServerOptions { DataPath = Path.Combine(root, "store.json"), ScriptPvfPath = "", SkillScriptPath = root };
            // This fixture compares the weekday 156/188 premium boundary.
            // Weekend doubling is verified separately by the fatigue suite.
            options.Fatigue.WeekendDoubleEnabled = false;
            var store = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance);
            await store.InitializeAsync();
            var characters = new CharacterRecord[4];
            var accounts = new[] { "ordinary", "diamond", "expired", "diamondtwo" };
            for (var slot = 0; slot < 4; slot++)
            {
                await store.CreateAccountAsync(new(accounts[slot], "password", OicqCode: "123456"));
                characters[slot] = (await store.CreateCharacterAsync(accounts[slot], new("Premium" + slot))).Character!;
                if (slot > 0)
                {
                    var grant = await store.GrantPremiumServiceToAccountAsync(accounts[slot], GameProtocolEngine.BlackDiamondServiceType, 30);
                    if (!grant.Success) throw new InvalidOperationException("premium fixture grant failed: " + grant.Error);
                }
            }
            await store.SetCharacterUsedFatigueAsync(characters[1].Id, GameProtocolEngine.DefaultMaximumFatigue);
            await store.SetCharacterUsedFatigueAsync(characters[2].Id, GameProtocolEngine.DefaultMaximumFatigue);
            var saved = JsonNode.Parse(await File.ReadAllTextAsync(options.DataPath))!;
            var expired = saved["Accounts"]!.AsArray().Single(account => account!["UserName"]!.GetValue<string>() == "expired")!;
            expired["PremiumServices"]![0]!["ExpiresAt"] = 1u;
            await File.WriteAllTextAsync(options.DataPath, saved.ToJsonString());
            store = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance);
            await store.InitializeAsync();
            var scripts = new ScriptFileSystem(options, NullLogger<ScriptFileSystem>.Instance);
            var generator = new ClearRewardGenerator(new(scripts, NullLogger<ClearRewardCatalog>.Instance),
                new(scripts, NullLogger<GoldDropCatalog>.Instance));
            using var fixture = new DungeonFixture();
            var instance = fixture.CreatePartyInstance(9910);
            var inboxes = Enumerable.Range(0, 4).Select(_ => new List<GameServerPacket>()).ToArray();
            for (byte slot = 0; slot < 4; slot++)
            {
                var inbox = inboxes[slot];
                instance.AddMember(new(characters[slot].Id, (ushort)(slot + 1), slot, characters[slot].Name,
                    packet => { inbox.Add(packet); return ValueTask.CompletedTask; }, accounts[slot]));
            }

            var snapshots = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
                PartyClearRewardSnapshot.PrepareAsync(instance, store, generator, 1, 0, false, CancellationToken.None)));
            check(snapshots.All(snapshot => snapshot?.Members.Count == 4), "concurrent receivers obtain a full four-member reward snapshot");
            for (byte slot = 0; slot < 4; slot++)
            {
                var snapshot = snapshots[slot]!;
                var member = snapshot.Members.Single(member => member.Slot == slot);
                check(member.HasBlackDiamond == (slot is 1 or 3)
                    && member.Roll.PremiumItemId == (slot is 1 or 3 ? (ushort?)5 : null),
                    $"stored member {slot} gets its own active membership roll; ordinary and expired accounts get no premium item");
                check(snapshots.All(other => ReferenceEquals(member.Roll, other!.Members.Single(value => value.Slot == slot).Roll)),
                    $"receiver snapshots reuse member {slot}'s exact single roll");
                await instance.FindMember(member.CharacterId)!.Send(snapshot.CreateReward((ushort)(slot + 1), member.CharacterId));
                await instance.FindMember(member.CharacterId)!.Send((await PartyClearRewardSnapshot.CreateRightsAsync(instance, store, CancellationToken.None))!);
                check(ReadPremium(inboxes[slot][0].Payload).SequenceEqual(new (bool, ushort?)[] { (false, null), (true, 5), (false, null), (true, 5) }),
                    $"real receiver {slot} packet exposes both active owners and excludes the expired member");
                check(inboxes[slot][1].Payload.SequenceEqual(new byte[] { 1, 1, 0, 1, 0, 0, 0, 1, 0 }),
                    $"real receiver {slot} rights preserve occupied exhausted members and each owner's premium fatigue limit");
                characters[slot] = (await store.GetCharacterAsync(accounts[slot], member.CharacterId))!;
                var index = slot;
                instance.RegisterPartyCardRewardAgent(member.CharacterId, new SessionPartyCardRewardAgent(member.CharacterId,
                    (_, type, column, _) => new(type, column, index, member.Roll, PartyCardCommitStatus.Pending),
                    async (settlement, type, column, generation, _, ct) =>
                    {
                        var owner = characters[index];
                        var result = await PartyCardOwnColumnCommitter.CommitAsync(store, accounts[index], owner, index,
                            type, column, generation, settlement, member.Roll, owner.Gold,
                            (owner.Inventory ?? []).ToDictionary(item => item.Slot), owner.Equipment ?? [], owner.WarehouseCapacity,
                            100_000, fixture.Items, ct);
                        if (result.InstalledCharacter is { } installed) characters[index] = installed;
                        return result.Outcome;
                    }));
            }

            byte[] columns = [3, 0, 2, 1];
            for (byte slot = 0; slot < 4; slot++)
            {
                var ownSlot = slot;
                var result = await PartyCardSettlementFlow.PickFreeCardAsync(instance, characters[slot].Id, columns[slot], "premium-smoke",
                    packet => { inboxes[ownSlot].Add(packet); return Task.CompletedTask; },
                    async packet => { foreach (var peer in instance.Members.Where(peer => peer.PartySlot != ownSlot)) await peer.Send(packet); },
                    _ => { }, CancellationToken.None);
                check(result.Accepted && result.Published, $"member {slot} independently settles its selected column");
            }
            for (byte slot = 0; slot < 4; slot++)
            {
                var savedCharacter = (await store.GetCharacterAsync(accounts[slot], characters[slot].Id))!;
                check(savedCharacter.WarehouseCapacity == (slot is 1 or 3 ? 24 : 8),
                    $"premium item grants only to active owner {slot}, without sharing the benefit with ordinary or expired members");
                check(inboxes[slot][^1].Payload.SequenceEqual(inboxes[0][^1].Payload),
                    $"receiver {slot} sees the same final flip ownership publication");
            }

            // Hold the real storage boundary to deterministically cross a run
            // change while the async rights/snapshot builder is suspended.
            var storeGate = (SemaphoreSlim)typeof(JsonGameStore).GetField("_gate",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(store)!;
            await storeGate.WaitAsync();
            Task<GameServerPacket?> delayedRights;
            Task<PartyClearRewardSnapshot?> delayedRewards;
            try
            {
                delayedRights = PartyClearRewardSnapshot.CreateRightsAsync(instance, store, CancellationToken.None);
                delayedRewards = PartyClearRewardSnapshot.PrepareAsync(instance, store, generator, 1, 0, false, CancellationToken.None);
                instance.StopRun();
            }
            finally { storeGate.Release(); }
            check(await delayedRights is null, "rights awaiting storage cannot escape after their settlement stops");
            check(await delayedRewards is null, "reward previews awaiting storage cannot escape after their settlement stops");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static async Task CheckConcurrentRollsAsync(Action<bool, string> check)
    {
        using var fixture = new DungeonFixture();
        var instance = fixture.CreatePartyInstance(9911);
        var id = Guid.NewGuid();
        var member = new DungeonInstanceMember(id, 1, 2, "owner", _ => ValueTask.CompletedTask);
        instance.AddMember(member);
        var generation = instance.EnsurePartyCardSettlement();
        var generated = 0;
        var rewards = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
            instance.GetOrCreatePartyClearReward(member, generation, true, () =>
            {
                Interlocked.Increment(ref generated);
                return new(0, false, 101, null, 5, null);
            }))));
        check(generated == 1 && rewards.All(reward => ReferenceEquals(reward, rewards[0])),
            "concurrent party previews generate exactly one independent owner roll");
        instance.RemoveMember(id);
        var replacement = member with { PartySlot = 1 };
        instance.AddMember(replacement);
        check(instance.GetOrCreatePartyClearReward(member, generation, true, () => throw new Exception("stale member generated")) is null
            && instance.SnapshotPartyClearRewards(generation)!.Members.Count == 0,
            "removed member preview is discarded and a stale incarnation cannot populate a reused slot");
        instance.StopRun();
        check(instance.SnapshotPartyClearRewards(generation) is null
            && instance.GetOrCreatePartyClearReward(replacement, generation, true, () => throw new Exception("stale run generated")) is null,
            "a stopped settlement cannot publish or generate an old premium preview");
        var next = instance.EnsurePartyCardSettlement();
        var fresh = instance.GetOrCreatePartyClearReward(replacement, next, false, () => new(0, false, 202, null, null, null));
        check(next != generation && fresh is { HasBlackDiamond: false, Roll.PremiumItemId: null, Roll.FreeGoldAmount: 202 },
            "the next run obtains a fresh roll and does not retain the previous premium entitlement");
    }

    private static (bool Enabled, ushort? Item)[] ReadPremium(byte[] payload)
    {
        var at = 38;
        void SkipGroup()
        {
            for (var slot = 0; slot < 4; slot++)
            {
                var count = payload[at++];
                at += count * 6;
            }
        }
        SkipGroup();
        at += 4;
        SkipGroup();
        var result = new (bool, ushort?)[4];
        for (var slot = 0; slot < 4; slot++)
        {
            var count = payload[at++];
            result[slot] = (count > 0, count > 1
                ? BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(at + 6)) : null);
            at += count * 6;
        }
        if (at != payload.Length) throw new InvalidDataException("NOTI35 premium records were not consumed exactly.");
        return result;
    }
}
