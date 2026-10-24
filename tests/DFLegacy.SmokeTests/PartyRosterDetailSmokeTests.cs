using System.Buffers.Binary;
using System.Text;
using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;

internal static class PartyRosterDetailSmokeTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        await CheckRealAcceptAndRejoinAsync(check);
        await CheckDirectoryBoundariesAsync(check);
    }

    private static async Task CheckRealAcceptAndRejoinAsync(Action<bool, string> check)
    {
        await using var harness = await PartyHarness.CreateAsync();
        var alpha = "阿尔法";
        var bravo = "布拉沃";
        var alphaDetail = Detail(harness.IdOf(alpha), harness.UidOf(alpha), 37);
        var bravoDetail = Detail(harness.IdOf(bravo), harness.UidOf(bravo), 19);
        await harness.SetTownAppearanceAsync(alpha, Basic(harness.UidOf(alpha)), alphaDetail);
        await harness.SetTownAppearanceAsync(bravo, Basic(harness.UidOf(bravo)), bravoDetail);
        await harness.SetTownAppearanceAsync("查理", Basic(harness.UidOf("查理")),
            Detail(harness.IdOf("查理"), harness.UidOf("查理"), 88));
        harness.ClearAll();

        await harness.InviteAndAcceptAsync(alpha, bravo);
        CheckDelivery("accept");

        harness.ClearAll();
        await harness.Service.HandleLeaveAsync(harness.IdOf(alpha), CancellationToken.None);
        check(!harness.Of(alpha).Any(IsDetail) && !harness.Of(bravo).Any(IsDetail),
            "a leaver receiving a cleanup roster is not included as a real member in combat detail rebuilding");
        await harness.Service.HandleLeaveAsync(harness.IdOf(bravo), CancellationToken.None);
        harness.ClearAll();
        // Reverse requester to avoid the independent three-second invite limit.
        // No new appearance/recovery packet is cached: identity is unchanged.
        await harness.InviteAndAcceptAsync(bravo, alpha);
        CheckDelivery("rejoin after disband");

        void CheckDelivery(string phase)
        {
            foreach (var (recipient, uid, expected) in new[]
                     { (alpha, harness.UidOf(bravo), bravoDetail),
                       (bravo, harness.UidOf(alpha), alphaDetail) })
            {
                var packets = harness.Of(recipient);
                var details = packets.Where(IsDetail).ToArray();
                var firstDetail = packets.FindIndex(IsDetail);
                var lastRoster = packets.FindLastIndex(p => p.ProtocolId == GameProtocolEngine.PartyInfoNotification);
                check(details.Length == 1 && IsDetailFor(details[0], uid)
                        && ReferenceEquals(details[0], expected)
                        && lastRoster >= 0 && lastRoster < firstDetail,
                    $"real party {phase} sends all direct/area NOTI9 before one cached foreign detail, bypassing stale delivery identity");
                check(details.Length == 1 && details[0].Payload.AsSpan(details[0].Payload.Length - 4)
                        .SequenceEqual(new byte[] { 1, 20, 10, 0 }),
                    $"real party {phase} rebuilds the foreign skill20 level10 snapshot after NOTI9");
            }
            check(harness.Of("查理").Any(p => p.ProtocolId == GameProtocolEngine.PartyInfoNotification)
                    && !harness.Of("查理").Any(IsDetail)
                    && !harness.Of("德尔塔").Any(IsDetail)
                    && !harness.Of("艾琳").Any(IsDetail),
                $"party {phase} rebuilds only real members, never same-area roster observers or another area");
        }
    }

    private static async Task CheckDirectoryBoundariesAsync(Action<bool, string> check)
    {
        var pair = await DirectoryPair.CreateAsync();
        pair.Clear();
        // Prove force replay still requires an introduction, not merely an
        // active member GUID. Other direction remains a valid control case.
        pair.BTicket.Member.KnownAppearances.Clear();
        await pair.Directory.ReplayPartyCombatDetailsAsync([pair.AId, pair.BId, Guid.NewGuid()], CancellationToken.None);
        check(pair.A.Count(IsDetail) == 1 && pair.B.Count(IsDetail) == 0,
            "forced party rebuilding ignores unknown GUIDs and never sends a combat detail before that recipient knows its basic user");

        pair = await DirectoryPair.CreateAsync();
        pair.Clear();
        pair.BInTown = false;
        await pair.Directory.ReplayPartyCombatDetailsAsync([pair.AId, pair.BId], CancellationToken.None);
        check(pair.A.Count(IsDetail) == 0 && pair.B.Count(IsDetail) == 0,
            "a still-registered dungeon member is neither a subject nor recipient of forced town combat initialization");

        pair = await DirectoryPair.CreateAsync();
        pair.Clear();
        var liveMembers = new HashSet<Guid> { pair.AId, pair.BId };
        await pair.ATicket.Member.AppearanceDeliveryGate.WaitAsync();
        var memberReplay = pair.Directory.ReplayPartyCombatDetailsAsync(
            [pair.AId, pair.BId], CancellationToken.None, liveMembers.Contains);
        // The member leaves the party, but remains the exact registered town
        // instance in the same area with the same already-known basic user.
        liveMembers.Remove(pair.BId);
        pair.ATicket.Member.AppearanceDeliveryGate.Release();
        await memberReplay.WaitAsync(TimeSpan.FromSeconds(5));
        check(pair.A.Count(IsDetail) == 0 && pair.B.Count(IsDetail) == 0,
            "a same-town member that leaves the party while a replay awaits its subject gate receives no forced detail");

        pair = await DirectoryPair.CreateAsync();
        var oldA = pair.ATicket.Member;
        await oldA.AppearanceDeliveryGate.WaitAsync();
        var pending = pair.Directory.ReplayPartyCombatDetailsAsync([pair.AId, pair.BId], CancellationToken.None);
        try
        {
            pair.ATicket = (await pair.Directory.EnterAsync(pair.Entry(true, area: 2), CancellationToken.None))!;
            pair.Clear();
        }
        finally
        {
            oldA.AppearanceDeliveryGate.Release();
        }
        await pending.WaitAsync(TimeSpan.FromSeconds(5));
        check(pair.A.Count(IsDetail) == 0 && pair.B.Count(IsDetail) == 0,
            "a captured party replay cannot send an expired member instance after migration while awaiting its delivery gate");

        // Preserve known-user evidence across the area boundary so this test
        // exercises exact town/area isolation rather than missing mode0 alone.
        pair.ATicket.Member.KnownAppearances[2] = pair.BTicket.Member;
        pair.BTicket.Member.KnownAppearances[1] = pair.ATicket.Member;
        await pair.Directory.RefreshAppearanceAsync(pair.ATicket, Basic(1), CancellationToken.None,
            Detail(pair.AId, 1, 37));
        pair.Clear();
        await pair.Directory.ReplayPartyCombatDetailsAsync([pair.AId, pair.BId], CancellationToken.None);
        check(pair.A.Count(IsDetail) == 0 && pair.B.Count(IsDetail) == 0,
            "even known party users in different town areas never receive forced combat detail from one another");

        pair = await DirectoryPair.CreateAsync(privateArea: true);
        pair.ATicket.Member.KnownAppearances[2] = pair.BTicket.Member;
        pair.BTicket.Member.KnownAppearances[1] = pair.ATicket.Member;
        pair.Clear();
        await pair.Directory.ReplayPartyCombatDetailsAsync([pair.AId, pair.BId], CancellationToken.None);
        check(pair.A.Count(IsDetail) == 0 && pair.B.Count(IsDetail) == 0,
            "forced party detail rebuilding preserves private-instance isolation within the same town and area");
    }

    private static bool IsDetail(GameServerPacket packet) =>
        packet.ProtocolId == GameProtocolEngine.UserInfoNotification && packet.Payload.Length >= 5 && packet.Payload[0] == 1;

    private static bool IsDetailFor(GameServerPacket packet, ushort uid) =>
        IsDetail(packet) && BinaryPrimitives.ReadUInt16LittleEndian(packet.Payload.AsSpan(3)) == uid;

    private static GameServerPacket Basic(ushort uid) => GameProtocolEngine.CreateCurrentCharacterInfo(
        new GameCharacterSummary(0, Encoding.ASCII.GetBytes($"User{uid}"), 3, 1, 45), uid,
        equippedItems: [], equippedCreature: null, hasBlackDiamond: false);

    private static GameServerPacket Detail(Guid id, ushort uid, byte recovery) =>
        PartyMemberDetailProjection.CreatePacket(uid,
            new CharacterRecord(id, $"User{uid}", 3, 1, 45, 10, 0, WeaknessRecovery: recovery),
            new GameCharacterCombatStats(14_400, 19_600, 110, 220, 330, 440,
                1, 2, 3, 4, 410_000, 5, 6, 8_500, 8_600, 7_000, 6_000, 4_300, 500_000), [], false,
            learnedSkills: [new(20, 10)]);

    private sealed class DirectoryPair
    {
        public readonly Guid AId = Guid.NewGuid();
        public readonly Guid BId = Guid.NewGuid();
        public readonly List<GameServerPacket> A = [];
        public readonly List<GameServerPacket> B = [];
        public bool BInTown = true;
        public required TownSessionDirectory Directory;
        public TownMemberTicket ATicket = null!;
        public TownMemberTicket BTicket = null!;

        public TownEntryRequest Entry(bool a, byte area = 0) => new(
            1, area, (ushort)(a ? 1 : 2), a ? AId : BId, (uint)(a ? 1 : 2), [], a ? "A" : "B",
            null, 474, 234, 5, (packet, _) =>
            {
                (a ? A : B).Add(packet);
                return ValueTask.CompletedTask;
            }, IsInTown: () => a || BInTown);

        public void Clear() { A.Clear(); B.Clear(); }

        public static async Task<DirectoryPair> CreateAsync(bool privateArea = false)
        {
            var options = new ServerOptions();
            if (privateArea) options.TownSync.PrivateAreas = ["1:1"];
            var pair = new DirectoryPair { Directory = new TownSessionDirectory(
                options, TimeProvider.System, NullLogger<TownSessionDirectory>.Instance) };
            var area = (byte)(privateArea ? 1 : 0);
            pair.ATicket = (await pair.Directory.EnterAsync(pair.Entry(true, area), CancellationToken.None))!;
            pair.BTicket = (await pair.Directory.EnterAsync(pair.Entry(false, area), CancellationToken.None))!;
            await pair.Directory.RefreshAppearanceAsync(pair.ATicket, Basic(1), CancellationToken.None, Detail(pair.AId, 1, 37));
            await pair.Directory.RefreshAppearanceAsync(pair.BTicket, Basic(2), CancellationToken.None, Detail(pair.BId, 2, 19));
            return pair;
        }
    }
}
