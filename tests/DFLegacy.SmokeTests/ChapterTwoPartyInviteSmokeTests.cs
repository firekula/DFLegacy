using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Text;
using DFLegacy.Protocol;
using DFLegacy.Server;

internal static class ChapterTwoPartyInviteSmokeTests
{
    private static readonly string[] Names = ["阿尔法", "布拉沃", "查理", "德尔塔"];
    private static readonly string[] Accounts = ["alice", "bobby", "carol", "dave"];

    internal static async Task RunAsync(Action<bool, string> check)
    {
        await CheckUnknownInviterAsync(check);
        await CheckRefusalAndTimeoutAsync(check);
        await CheckBlockedAndOccupiedAsync(check);
        foreach (var change in new[] { "target-session", "requester-session", "target-uid", "requester-area", "timeout", "cancel" })
            await CheckInviteRaceAsync(change, check);
        await CheckFourMemberPreparationAsync(check);
        await CheckPreparationSessionRaceAsync(check);
        foreach (var failure in new[] { "missing-peer", "missing-directory", "send-failure", "directory-replacement" })
            await CheckPreparationFailureAsync(failure, check);
        await CheckKnownAppearanceAsync(check);
        await CheckAppearanceArrivalDuringLookupAsync(check);
        await CheckCrossChannelAsync(check);
        foreach (var change in new[] { "target-other-party", "requester-full" })
            await CheckEligibilityDuringLookupAsync(change, check);
        await CheckDeliveredInviteSessionAsync(check);
        await CheckUnpreparedCandidatePublicationAsync(check);
        await CheckConcurrentCandidatesAsync(check);
        await CheckCandidateStableSlotsAsync(check);
        foreach (var path in new[] { "generic", "area" }) await CheckStalePublicationAsync(path, check);
        foreach (var path in new[] { "generic", "area" }) await CheckStaleAreaBroadcastAsync(path, check);
        await CheckRetiredRosterWithoutChannelsAsync(check);
    }

    private static GameServerPacket Appearance(CharacterRecord character, ushort uid) =>
        GameProtocolEngine.CreateCurrentCharacterInfo(
            new GameCharacterSummary(0, Encoding.UTF8.GetBytes(character.Name),
                (byte)character.Job, (byte)character.GrowType, (byte)character.Level),
            uid, equippedItems: [], includeChapter2ActorFields: true);

    private static GamePeerInfo Peer(ushort uid) => new(uid, IPAddress.Loopback, IPAddress.Loopback,
        (ushort)(51967 + uid), (uint)(200 + uid), 1, 768, (byte)(35 + uid));

    private static async Task<PartyHarness> CreateAsync()
    {
        var h = await PartyHarness.CreateAsync();
        await h.LeaveTownAsync("艾琳");
        for (var i = 0; i < Names.Length; i++)
        {
            var name = Names[i];
            await h.MoveToAsync(name, 1, 1, h.UidOf(name), chapterTwo: true, peer: Peer(h.UidOf(name)));
            await AttachAsync(h, i, h.Record(name));
            var character = (await h.Store.GetCharacterAsync(Accounts[i], h.IdOf(name)))!;
            await h.SetTownAppearanceAsync(name, Appearance(character, h.UidOf(name)),
                new GameServerPacket(0, 2, [1]));
        }
        h.ClearAll();
        return h;
    }

    private static Task AttachAsync(PartyHarness h, int index, Func<GameServerPacket, ValueTask> send, Guid? session = null) =>
        h.Service.AttachChannelAsync(Accounts[index], Names[index], h.IdOf(Names[index]), h.UidOf(Names[index]),
            session ?? h.SessionIdOf(Names[index]), send, createPeerAppearance: Appearance, channelNumber: 1);

    private static Task RequestAsync(PartyHarness h, int from = 0, int to = 1, CancellationToken token = default) =>
        h.Service.HandleRequestAsync(h.IdOf(Names[from]), h.UidOf(Names[to]), PartyHarness.DefaultInfo, token);

    private static async Task CheckUnknownInviterAsync(Action<bool, string> check)
    {
        await using var h = await CreateAsync();
        await h.Store.AddFriendAsync(Accounts[0], Names[1]);
        await RequestAsync(h);
        var packets = h.Of(Names[1]);
        var source = (await h.Store.GetCharacterAsync(Accounts[0], h.IdOf(Names[0])))!;
        check(packets.Count == 2 && packets[0].ProtocolId == 2 && packets[1].ProtocolId == 7
                && packets[0].Payload.SequenceEqual(Appearance(source, h.UidOf(Names[0])).Payload)
                && BinaryPrimitives.ReadUInt16LittleEndian(packets[1].Payload) == h.UidOf(Names[0]),
            "Chapter Two private-room invite sends only the public empty-equipment profile before NOTI7 with the same inviter UID");
        check((await h.Store.GetFriendsAsync(Accounts[1])).Count == 0
                && !h.Directory.HasKnownAppearance(h.IdOf(Names[1]), h.IdOf(Names[0]), h.UidOf(Names[0]))
                && !h.Directory.SharesPublicArea(h.IdOf(Names[0]), h.IdOf(Names[1])),
            "one-way friendship remains one-way and invitation preparation creates no town visibility membership");
    }

    private static async Task CheckRefusalAndTimeoutAsync(Action<bool, string> check)
    {
        await using var h = await CreateAsync();
        await RequestAsync(h); h.ClearAll();
        await h.Service.HandleResponseAsync(h.IdOf(Names[1]), h.UidOf(Names[0]), accepted: false);
        check(h.Of(Names[1]).Count == 0 && h.Of(Names[0]).Single().Payload.SequenceEqual(new byte[] { 0, 100, 0 })
                && h.Service.GetPartySlots(h.IdOf(Names[0])) is null,
            "Chapter Two invitation refusal still emits only the existing requester failure and creates no party");
        h.Service.ResetRequestThrottleForTests(h.IdOf(Names[0]));
        await RequestAsync(h); h.ClearAll();
        h.Service.ExpirePendingForTests(); await h.Service.SweepExpiredAsync(CancellationToken.None);
        await h.Service.HandleResponseAsync(h.IdOf(Names[1]), h.UidOf(Names[0]), accepted: true);
        check(h.Of(Names[1]).Count == 0 && h.Of(Names[0]).Count == 1
                && h.Service.GetPartySlots(h.IdOf(Names[0])) is null,
            "Chapter Two timeout remains a single failure at the existing ten-second window and a late acceptance is ignored");
    }

    private static async Task CheckBlockedAndOccupiedAsync(Action<bool, string> check)
    {
        await using var h = await CreateAsync();
        await RequestAsync(h); h.ClearAll();
        await RequestAsync(h, 2, 1);
        check(h.Of(Names[1]).Count == 0 && h.Of(Names[2]).Single().Payload.SequenceEqual(new byte[] { 0, 100, 0 }),
            "a live pending invitation receives no extra public profile or second dialog for a later requester");
        await RequestAsync(h);
        check(h.Of(Names[1]).Count == 0, "a throttled Chapter Two retry emits neither profile nor invitation");
        await h.Service.HandleResponseAsync(h.IdOf(Names[1]), h.UidOf(Names[0]), accepted: false);
        await h.Store.AddBlacklistAsync(Accounts[1], Names[0]);
        h.Service.ResetRequestThrottleForTests(h.IdOf(Names[0])); h.ClearAll();
        await RequestAsync(h); h.Service.ExpirePendingForTests(); await h.Service.SweepExpiredAsync(CancellationToken.None);
        check(h.Of(Names[1]).Count == 0 && h.Of(Names[0]).Count == 1,
            "a blacklisted requester remains invisible to the target including profile preparation");
    }

    private static async Task CheckInviteRaceAsync(string change, Action<bool, string> check)
    {
        await using var h = await CreateAsync();
        using var cancellation = new CancellationTokenSource();
        var replacement = new List<GameServerPacket>();
        var changed = false;
        await AttachAsync(h, 1, async packet =>
        {
            await h.Record(Names[1])(packet);
            if (changed || packet.ProtocolId != 2) return;
            changed = true;
            switch (change)
            {
                case "target-session":
                    await AttachAsync(h, 1, packet => { replacement.Add(packet); return ValueTask.CompletedTask; }, Guid.NewGuid());
                    break;
                case "requester-session":
                    await AttachAsync(h, 0, packet => { replacement.Add(packet); return ValueTask.CompletedTask; }, Guid.NewGuid());
                    break;
                case "target-uid":
                    await h.MoveToAsync(Names[1], 1, 1, 99, chapterTwo: true, peer: Peer(99));
                    break;
                case "requester-area":
                    await h.MoveToAsync(Names[0], 1, 2, h.UidOf(Names[0]), chapterTwo: true, peer: Peer(h.UidOf(Names[0])));
                    break;
                case "timeout":
                    h.Service.ExpirePendingForTests(); await h.Service.SweepExpiredAsync(CancellationToken.None);
                    break;
                case "cancel": cancellation.Cancel(); break;
            }
        });
        try { await RequestAsync(h, token: cancellation.Token); }
        catch (OperationCanceledException) when (change == "cancel") { }
        check(changed && h.Of(Names[1]).All(packet => packet.ProtocolId != 7) && replacement.Count == 0,
            $"Chapter Two invite stops after profile delivery when {change} invalidates its captured identity/window");
        if (change is "target-session" or "target-uid" or "timeout" or "cancel")
        {
            h.ClearAll(); await RequestAsync(h, 2, 1);
            check(change == "target-session" ? replacement.Any(packet => packet.ProtocolId == 7)
                    : h.Of(Names[1]).Any(packet => packet.ProtocolId == 7),
                $"an aborted {change} invitation does not retain the target pending slot");
        }
    }

    private static async Task CheckFourMemberPreparationAsync(Action<bool, string> check)
    {
        await using var h = await CreateAsync();
        for (var i = 1; i < 4; i++)
        {
            h.Service.ResetRequestThrottleForTests(h.IdOf(Names[0]));
            await RequestAsync(h, 0, i); h.ClearAll();
            await h.Service.HandleResponseAsync(h.IdOf(Names[i]), h.UidOf(Names[0]), accepted: true);
        }
        foreach (var name in Names)
        {
            var packets = h.Of(name);
            var tableIndex = packets.FindIndex(packet => packet.ProtocolId == 11);
            var rosterIndex = packets.FindIndex(packet => packet.ProtocolId == 9);
            var profiles = packets.Where(packet => packet.ProtocolId == 2 && packet.Payload[0] == 0).ToArray();
            check(profiles.Length == 3 && profiles.Select(packet => BinaryPrimitives.ReadUInt16LittleEndian(packet.Payload.AsSpan(3))).Distinct().Count() == 3
                    && tableIndex > 2 && rosterIndex > tableIndex && packets.Count(packet => packet.ProtocolId == 11) == 1,
                $"four-member private preparation sends each other UID once, then one endpoint table, then roster to {name}");
            var table = packets[tableIndex].Payload;
            var expected = GameProtocolEngine.CreateUdpPeerInfo(Names.OrderBy(n => n == name ? 0 : 1)
                .Select(n => Peer(h.UidOf(n))).ToArray(), chapterTwoTown: true);
            check(table.SequenceEqual(expected.Payload)
                    && packets.All(packet => packet.ProtocolId is not (GameProtocolEngine.UserAreaNotification or GameProtocolEngine.AreaUsersNotification))
                    && Names.Where(n => n != name).All(n => !h.Directory.HasKnownAppearance(h.IdOf(name), h.IdOf(n), h.UidOf(n))),
                $"private party projection preserves self-first network-order endpoints/account/MTU/vitality without town visibility changes for {name}");
        }
        check(h.Directory.GetMemberCount(1, 1) == 4 && Names.All(name => !h.Directory.IsPublicTownMember(h.IdOf(name))),
            "four-member preparation leaves all four independent Seria room memberships private");
        h.ClearAll();
        await h.Directory.SendChapterTwoPartyPeerTablesAsync([h.IdOf(Names[0]), h.IdOf(Names[1]), h.IdOf(Names[0])], (_, _) => true, CancellationToken.None);
        check(h.Of(Names[0]).Single().Payload[0] == 2 && h.Of(Names[1]).Single().Payload[0] == 2,
            "duplicate requested character IDs never duplicate a peer row or its recipient delivery");
    }

    private static async Task CheckPreparationSessionRaceAsync(Action<bool, string> check)
    {
        await using var h = await CreateAsync();
        await RequestAsync(h); h.ClearAll();
        var replacement = new List<GameServerPacket>();
        var changed = false;
        await AttachAsync(h, 0, async packet =>
        {
            await h.Record(Names[0])(packet);
            if (changed || packet.ProtocolId != 2) return;
            changed = true;
            await AttachAsync(h, 1, packet => { replacement.Add(packet); return ValueTask.CompletedTask; }, Guid.NewGuid());
        });
        await h.Service.HandleResponseAsync(h.IdOf(Names[1]), h.UidOf(Names[0]), accepted: true);
        check(changed && replacement.Count == 0
                && Names.All(name => h.Of(name).All(packet => packet.ProtocolId is not (9 or 11))),
            "a replacement session during party preparation receives no old endpoint table or roster and stops later preparation writes");
    }

    private static async Task CheckKnownAppearanceAsync(Action<bool, string> check)
    {
        await using var h = await CreateAsync();
        for (var i = 0; i < 2; i++)
        {
            var name = Names[i];
            await h.MoveToAsync(name, 1, 0, h.UidOf(name), chapterTwo: true, peer: Peer(h.UidOf(name)));
            var source = (await h.Store.GetCharacterAsync(Accounts[i], h.IdOf(name)))!;
            await h.SetTownAppearanceAsync(name, Appearance(source, h.UidOf(name)), new GameServerPacket(0, 2, [1]));
        }
        h.ClearAll(); await RequestAsync(h);
        check(h.Of(Names[1]).Count == 1 && h.Of(Names[1])[0].ProtocolId == 7,
            "an already-known shared-town appearance is not overwritten with an empty-equipment invitation profile");
        h.ClearAll(); await h.Service.HandleResponseAsync(h.IdOf(Names[1]), h.UidOf(Names[0]), accepted: true);
        check(Names.Take(2).All(name => h.Of(name).All(packet => packet.ProtocolId != 2 || packet.Payload[0] != 0)),
            "party preparation preserves the existing public appearance for players already known through town visibility");
    }

    private static async Task CheckPreparationFailureAsync(string failure, Action<bool, string> check)
    {
        await using var h = await CreateAsync();
        var trigger = false;
        if (failure == "missing-directory")
        {
            await RequestAsync(h, 0, 2);
            await h.Service.HandleResponseAsync(h.IdOf(Names[2]), h.UidOf(Names[0]), accepted: true);
            h.Service.ResetRequestThrottleForTests(h.IdOf(Names[0]));
        }
        if (failure != "missing-directory")
        {
            await h.MoveToAsync(Names[1], 1, 1, h.UidOf(Names[1]), chapterTwo: true,
                peer: failure == "missing-peer" ? null : Peer(h.UidOf(Names[1])), townSend: async packet =>
                {
                    await h.Record(Names[1])(packet);
                    if (!trigger || packet.ProtocolId != 11) return;
                    trigger = false;
                    if (failure == "send-failure") throw new IOException("fixture rejected peer table");
                    if (failure == "directory-replacement")
                        await h.Directory.EnterAsync(new TownEntryRequest(1, 1, h.UidOf(Names[1]), h.IdOf(Names[1]), 102,
                            Encoding.UTF8.GetBytes(Names[1]), Names[1], Peer(h.UidOf(Names[1])), 10, 10, 0,
                            (p, _) => h.Record(Names[1])(p), IsChapterTwo: true), CancellationToken.None);
                });
        }
        await RequestAsync(h); h.ClearAll();
        if (failure == "missing-directory") await h.LeaveTownAsync(Names[2]);
        h.ClearAll(); trigger = true;
        await h.Service.HandleResponseAsync(h.IdOf(Names[1]), h.UidOf(Names[0]), accepted: true);
        check(Names.All(name => h.Of(name).All(packet => packet.ProtocolId is not (9 or 8))),
            $"incomplete Chapter Two party preparation ({failure}) cannot publish a roster or accepted notice");
    }

    private static async Task CheckAppearanceArrivalDuringLookupAsync(Action<bool, string> check)
    {
        await using var h = await CreateAsync();
        for (var i = 0; i < 2; i++)
            await h.MoveToAsync(Names[i], 1, 0, h.UidOf(Names[i]), chapterTwo: true, peer: Peer(h.UidOf(Names[i])));
        var source = (await h.Store.GetCharacterAsync(Accounts[0], h.IdOf(Names[0])))!;
        var publicAppearance = GameProtocolEngine.CreateCurrentCharacterInfo(
            new GameCharacterSummary(0, Encoding.UTF8.GetBytes(source.Name), (byte)source.Job, (byte)source.GrowType, (byte)source.Level),
            h.UidOf(Names[0]), equippedItems: [new(9, 1001, 1, 0, 0, 0)], includeChapter2ActorFields: true);
        var changed = false;
        await h.Service.AttachChannelAsync(Accounts[1], Names[1], h.IdOf(Names[1]), h.UidOf(Names[1]), h.SessionIdOf(Names[1]),
            h.Record(Names[1]), createPeerAppearance: (character, uid) =>
            {
                if (!changed)
                {
                    changed = true;
                    h.SetTownAppearanceAsync(Names[0], publicAppearance, new GameServerPacket(0, 2, [1])).GetAwaiter().GetResult();
                }
                return Appearance(character, uid);
            }, channelNumber: 1);
        h.ClearAll(); await RequestAsync(h);
        var profiles = h.Of(Names[1]).Where(packet => packet.ProtocolId == 2 && packet.Payload[0] == 0).ToArray();
        check(changed && profiles.Length == 1 && profiles[0].Payload.SequenceEqual(publicAppearance.Payload)
                && h.Of(Names[1]).Last().ProtocolId == 7,
            "a full town appearance arriving during the store lookup is preserved and suppresses the stale fallback profile");
    }

    private static async Task CheckCrossChannelAsync(Action<bool, string> check)
    {
        await using var h = await CreateAsync();
        await h.Service.AttachChannelAsync(Accounts[1], Names[1], h.IdOf(Names[1]), h.UidOf(Names[1]), h.SessionIdOf(Names[1]),
            h.Record(Names[1]), createPeerAppearance: Appearance, channelNumber: 2);
        await RequestAsync(h);
        check(h.Of(Names[1]).Count == 0 && h.Of(Names[0]).Single().Payload.SequenceEqual(new byte[] { 0, 100, 0 }),
            "Chapter Two rejects a same-town/area UID on another channel without sending that channel a public profile or endpoint");
    }

    private static async Task CheckEligibilityDuringLookupAsync(string change, Action<bool, string> check)
    {
        await using var h = await CreateAsync();
        var changed = false;
        await h.Service.AttachChannelAsync(Accounts[1], Names[1], h.IdOf(Names[1]), h.UidOf(Names[1]), h.SessionIdOf(Names[1]),
            h.Record(Names[1]), createPeerAppearance: (character, uid) =>
            {
                if (!changed && character.Id == h.IdOf(Names[0]))
                {
                    changed = true;
                    if (change == "requester-full")
                        h.Service.HandleSetInfoAsync(h.IdOf(Names[0]), 1, [], 1).GetAwaiter().GetResult();
                    else
                    {
                        RequestAsync(h, 2, 1).GetAwaiter().GetResult();
                        h.Service.HandleResponseAsync(h.IdOf(Names[1]), h.UidOf(Names[2]), accepted: true).GetAwaiter().GetResult();
                    }
                    h.ClearAll();
                }
                return Appearance(character, uid);
            }, channelNumber: 1);
        await RequestAsync(h);
        check(changed && h.Of(Names[1]).All(packet => packet.ProtocolId is not (2 or 7))
                && (change == "requester-full" ? h.Of(Names[0]).Single().Payload.SequenceEqual(new byte[] { 0, 100, 0 })
                    : h.Of(Names[0]).Count == 0),
            $"party eligibility is rechecked after profile lookup when {change} changes the original admission snapshot");
    }

    private static async Task CheckDeliveredInviteSessionAsync(Action<bool, string> check)
    {
        await using var h = await CreateAsync();
        await RequestAsync(h); h.ClearAll();
        await AttachAsync(h, 1, h.Record(Names[1]), Guid.NewGuid());
        await h.Service.HandleResponseAsync(h.IdOf(Names[1]), h.UidOf(Names[0]), accepted: true);
        check(Names.All(name => h.Of(name).Count == 0) && h.Service.GetPartySlots(h.IdOf(Names[0])) is null,
            "an already-delivered Chapter Two invitation cannot be accepted by a replacement target session");
    }

    private static async Task CheckUnpreparedCandidatePublicationAsync(Action<bool, string> check)
    {
        await using var h = await CreateAsync();
        await RequestAsync(h, 0, 2);
        await h.Service.HandleResponseAsync(h.IdOf(Names[2]), h.UidOf(Names[0]), accepted: true);
        h.Service.ResetRequestThrottleForTests(h.IdOf(Names[0]));
        await h.MoveToAsync(Names[1], 1, 1, h.UidOf(Names[1]), chapterTwo: true, peer: null);
        await RequestAsync(h); h.ClearAll();
        await h.Service.HandleResponseAsync(h.IdOf(Names[1]), h.UidOf(Names[0]), accepted: true);
        check(h.Service.GetPartySlots(h.IdOf(Names[0]))?.Length == 2 && h.Service.GetPartySlots(h.IdOf(Names[1])) is null,
            "an unprepared third member never enters the live party membership query");
        h.ClearAll(); h.Service.ExpireResettleForTests(); await h.Service.SweepExpiredAsync(CancellationToken.None);
        CheckNoCandidate("prior resettle");
        h.ClearAll(); await h.Service.HandleSetInfoAsync(h.IdOf(Names[0]), 1, [], 4);
        CheckNoCandidate("SetInfo");
        h.ClearAll(); await h.LeaveTownAsync(Names[0]); await h.Service.OnLeftAreaAsync(h.IdOf(Names[0]));
        CheckNoCandidate("leaving town");
        void CheckNoCandidate(string trigger) => check(Names.SelectMany(h.Of)
                .Where(packet => packet.ProtocolId == 9 && packet.Payload.Length == 20)
                .All(packet => Enumerable.Range(0, 4).All(slot => BinaryPrimitives.ReadUInt16LittleEndian(packet.Payload.AsSpan(7 + slot * 3)) != h.UidOf(Names[1]))),
            $"a failed third-member preparation cannot escape through {trigger} publication");
    }

    private static async Task CheckConcurrentCandidatesAsync(Action<bool, string> check)
    {
        await using var h = await CreateAsync();
        await RequestAsync(h); await h.Service.HandleResponseAsync(h.IdOf(Names[1]), h.UidOf(Names[0]), accepted: true);
        h.Service.ResetRequestThrottleForTests(h.IdOf(Names[0])); await RequestAsync(h, 0, 2);
        h.Service.ResetRequestThrottleForTests(h.IdOf(Names[0])); await RequestAsync(h, 0, 3);
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waiting = false;
        await AttachAsync(h, 0, async packet =>
        {
            await h.Record(Names[0])(packet);
            if (!waiting && packet.ProtocolId == 2 && packet.Payload[0] == 0
                && BinaryPrimitives.ReadUInt16LittleEndian(packet.Payload.AsSpan(3)) == h.UidOf(Names[2]))
            { waiting = true; paused.TrySetResult(); await release.Task; }
        });
        h.ClearAll();
        var first = h.Service.HandleResponseAsync(h.IdOf(Names[2]), h.UidOf(Names[0]), accepted: true);
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(3));
        check(h.Service.GetPartySlots(h.IdOf(Names[0]))?.Length == 2 && h.Service.GetPartySlots(h.IdOf(Names[2])) is null,
            "a suspended candidate preparation is invisible to live membership readers");
        h.Service.ExpireResettleForTests(); await h.Service.SweepExpiredAsync(CancellationToken.None);
        check(Names.SelectMany(h.Of).Where(packet => packet.ProtocolId == 9 && packet.Payload.Length == 20)
                .All(packet => Enumerable.Range(0, 4).All(slot => BinaryPrimitives.ReadUInt16LittleEndian(packet.Payload.AsSpan(7 + slot * 3)) != h.UidOf(Names[2]))),
            "an existing resettle may publish only the old prepared roster while a candidate preparation is suspended");
        try { await h.Service.HandleResponseAsync(h.IdOf(Names[3]), h.UidOf(Names[0]), accepted: true); }
        finally { release.TrySetResult(); }
        await first;
        var slots = h.Service.GetPartySlots(h.IdOf(Names[0]));
        check(slots is { Length: 3 } && slots.Any(slot => slot.CharacterId == h.IdOf(Names[3]))
                && slots.All(slot => slot.CharacterId != h.IdOf(Names[2])) && h.Service.GetPartySlots(h.IdOf(Names[2])) is null,
            "concurrent candidate admission installs only the preparation based on the still-current roster version");
    }

    private static async Task CheckCandidateStableSlotsAsync(Action<bool, string> check)
    {
        await using var h = await CreateAsync();
        await RequestAsync(h); await h.Service.HandleResponseAsync(h.IdOf(Names[1]), h.UidOf(Names[0]), accepted: true);
        h.Service.ResetRequestThrottleForTests(h.IdOf(Names[0])); await RequestAsync(h, 0, 2);
        await h.Service.HandleResponseAsync(h.IdOf(Names[2]), h.UidOf(Names[0]), accepted: true);
        await h.Service.HandleLeaveAsync(h.IdOf(Names[0]));
        await RequestAsync(h, 1, 3); await h.Service.HandleResponseAsync(h.IdOf(Names[3]), h.UidOf(Names[1]), accepted: true);
        var slots = h.Service.GetPartySlots(h.IdOf(Names[1]));
        check(slots is { Length: 3 } && slots.Single(slot => slot.CharacterId == h.IdOf(Names[1])).SlotIndex == 1
                && slots.Single(slot => slot.CharacterId == h.IdOf(Names[2])).SlotIndex == 2
                && slots.Single(slot => slot.CharacterId == h.IdOf(Names[3])).SlotIndex == 0
                && h.Service.TryGetLeaderSlot(h.IdOf(Names[3])) == 1,
            "candidate copying preserves fixed slot holes and a nonzero leader slot while filling the lowest free slot");
    }

    private static async Task CheckStalePublicationAsync(string path, Action<bool, string> check)
    {
        await using var h = await CreateAsync();
        await RequestAsync(h); await h.Service.HandleResponseAsync(h.IdOf(Names[1]), h.UidOf(Names[0]), accepted: true);
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waiting = false;
        await AttachAsync(h, 0, async packet =>
        {
            await h.Record(Names[0])(packet);
            if (!waiting && packet.ProtocolId == 9) { waiting = true; paused.TrySetResult(); await release.Task; }
        });
        h.ClearAll();
        var old = path == "generic" ? h.Service.HandleSetInfoAsync(h.IdOf(Names[0]), 2, [], 4)
            : h.Service.OnAreaChangedAsync(h.IdOf(Names[0]), 1, 1, h.UidOf(Names[0]));
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(3));
        try { await h.Service.HandleSetInfoAsync(h.IdOf(Names[0]), 3, [], 4); }
        finally { release.TrySetResult(); }
        await old;
        check(h.Of(Names[1]).Where(packet => packet.ProtocolId == 9 && packet.Payload.Length == 20)
                .All(packet => packet.Payload[5] == 3),
            $"an awaited old {path} roster snapshot cannot overwrite a newer Chapter Two party revision");
    }

    private static async Task CheckStaleAreaBroadcastAsync(string path, Action<bool, string> check)
    {
        await using var h = await CreateAsync();
        await RequestAsync(h); await h.Service.HandleResponseAsync(h.IdOf(Names[1]), h.UidOf(Names[0]), accepted: true);
        if (path == "area") await h.Service.HandleSetInfoAsync(h.IdOf(Names[0]), 2, [], 4);
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string? firstRecipient = null;
        var armed = false;
        foreach (var name in Names)
            await h.MoveToAsync(name, 1, 1, h.UidOf(name), chapterTwo: true, peer: Peer(h.UidOf(name)), townSend: async packet =>
            {
                await h.Record(name)(packet);
                if (armed && packet.ProtocolId == 9 && packet.Payload.Length == 20 && packet.Payload[5] == 2
                    && Interlocked.CompareExchange(ref firstRecipient, name, null) is null)
                { paused.TrySetResult(); await release.Task; }
            });
        h.ClearAll();
        armed = true;
        var old = path == "generic" ? h.Service.HandleSetInfoAsync(h.IdOf(Names[0]), 2, [], 4)
            : h.Service.OnAreaChangedAsync(h.IdOf(Names[0]), 1, 1, h.UidOf(Names[0]));
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Dictionary<string, int> starts;
        try
        {
            await h.Service.HandleSetInfoAsync(h.IdOf(Names[0]), 3, [], 4);
            starts = Names.ToDictionary(name => name, name => h.Of(name).Count);
        }
        finally { release.TrySetResult(); }
        await old;
        check(Names.Where(name => name != firstRecipient).All(name => h.Of(name).Skip(starts[name])
                .All(packet => packet.ProtocolId != 9 || packet.Payload.Length != 20 || packet.Payload[5] != 2)),
            $"a paused {path} area broadcast stops before later recipients after a newer party version publishes");
    }

    private static async Task CheckRetiredRosterWithoutChannelsAsync(Action<bool, string> check)
    {
        await using var h = await CreateAsync();
        await RequestAsync(h); await h.Service.HandleResponseAsync(h.IdOf(Names[1]), h.UidOf(Names[0]), accepted: true);
        var parties = (ConcurrentDictionary<ushort, PartyRoster>)typeof(PartyService)
            .GetField("_parties", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(h.Service)!;
        var retired = parties.Values.Single();
        h.Service.ResetRequestThrottleForTests(h.IdOf(Names[0])); await RequestAsync(h, 0, 2);
        await h.Service.HandleResponseAsync(h.IdOf(Names[2]), h.UidOf(Names[0]), accepted: true);
        foreach (var name in Names.Take(3)) await h.Service.DetachChannelAsync(h.IdOf(name), h.SessionIdOf(name));
        h.ClearAll();
        await h.Service.PublishRosterAsync(retired, CancellationToken.None);
        check(Names.All(name => h.Of(name).All(packet => packet.ProtocolId != 9)),
            "a retired Chapter Two roster retains its protocol identity and cannot republish after every old attachment is gone");
    }
}
