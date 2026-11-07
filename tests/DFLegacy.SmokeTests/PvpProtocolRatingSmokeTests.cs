using DFLegacy.Protocol;
using DFLegacy.Server;
using System.Buffers.Binary;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;

internal static class PvpProtocolRatingSmokeTests
{
    internal static Task RunAsync(Action<bool, string> check)
    {
        ExpandedModes(check);
        Protocol(check);
        Parsers(check);
        RatingAndMaps(check);
        return Task.CompletedTask;
    }

    private static void ExpandedModes(Action<bool, string> check)
    {
        check(GameProtocolEngine.TryReadPvpMode([0,0,1], out var team) && team == 1
            && GameProtocolEngine.TryReadPvpMode([0,0,2], out var relay) && relay == 2,
            "ACT1 accepts team1 and relay2 mode commands");
        check(GameProtocolEngine.TryReadPvpSeat([0,0,7,2], out var seat, out var state) && seat == 7 && state == 2,
            "ACT1 accepts eighth seat and blue team");
    }

    private static void Protocol(Action<bool, string> check)
    {
        void Vector(GameServerPacket packet, byte type, byte id, string hex) => check(
            packet.Type == type && packet.ProtocolId == id && packet.Payload.SequenceEqual(Convert.FromHexString(hex)),
            $"ACT1 PvP {type}/{id} matches independently specified wire bytes");
        Vector(GameProtocolEngine.CreatePvpLoginSuccess(0x12345678, "", 0, 0), 1, 1,
            "0100000400785634120000000000000000000000000100");
        Vector(GameProtocolEngine.CreatePvpLoginSuccess(7, "127.0.0.1", 7007, 7008), 1, 1,
            "010000040007000000090000003132372E302E302E315F1B0000601B00000100");
        var seats = new PvpSeatInfo[] { new(0, 0, 0x1234), new(1, 255, 65535), new(2, 254, 65535),
            new(3, 254, 65535), new(4, 254, 65535), new(5, 254, 65535), new(6, 254, 65535), new(7, 254, 65535) };
        Vector(GameProtocolEngine.CreatePvpRoomList([new(9, 0, [0xBE, 0xC6, 0xB9, 0xDD], 1, 0, 3, 0, seats)]), 0, 41,
            "010009000004000000BEC6B9DD0100030000003412FFFFFFFEFFFFFEFFFFFEFFFFFEFFFFFEFFFFFEFFFF00");
        Vector(GameProtocolEngine.CreatePvpRoomList([]), 0, 41, "0000");
        Vector(GameProtocolEngine.CreatePvpRoomState(9, 2, 1, 3), 0, 42, "09000201030000");
        Vector(GameProtocolEngine.CreatePvpSeatState(9, [new(1, 0, 0x5678)]), 0, 43, "0900000101007856");
        Vector(GameProtocolEngine.CreatePvpReadyState(1, true), 0, 44, "0101");
        Vector(GameProtocolEngine.CreatePvpStart(3), 0, 45, "0300");
        Vector(GameProtocolEngine.CreatePvpDeath(1, 255), 0, 46, "01FF");
        Vector(GameProtocolEngine.CreatePvpRankRequest(), 0, 49, "");
        Vector(GameProtocolEngine.CreatePvpRecord(2, 3, 240001, 240000, 275000, 10), 0, 48,
            "020000000300000081A9030080A90300383204000A00");
        Vector(GameProtocolEngine.CreatePvpEnterReply([false, true, false, false, false, false, false, false]), 1, 54, "010001000000000000");
        Vector(GameProtocolEngine.CreatePvpResult(1, 17, 10, -899,
            [new(0x1234, 9, 10, 240001, 240000, 275000)], 0x1234, false), 0, 47,
            "01110000000A7DFCFFFF013412090000000A81A9030080A903003832040000000000000000003412FFFF0000000000");
        Vector(GameProtocolEngine.CreatePvpStart(3,2),0,45,"0302");
        Vector(GameProtocolEngine.CreatePvpSeatState(9,[new(7,2,0x5678)],1),0,43,"0900010107027856");
        Vector(GameProtocolEngine.CreatePvpRelayTurn([
            new(0,[new(0,4),new(2,2)]),new(255,[new(1,3),new(7,1)])]),0,124,"020002000400020200FF02010300070100");
        Vector(GameProtocolEngine.CreatePvpFightRequest(7),0,125,"07");
        check(Throws(()=>GameProtocolEngine.CreatePvpRelayTurn([new(8,[]),new(0,[])]))
            && Throws(()=>GameProtocolEngine.CreatePvpSeatState(1,[new(7,2,65535)],1)),
            "malformed relay/seat packets are rejected before serialization");
        check(Throws(() => GameProtocolEngine.CreatePvpLoginSuccess(7, "localhost", 9, 10))
            && Throws(() => GameProtocolEngine.CreatePvpLoginSuccess(7, "127.0.0.1", 0, 10))
            && Throws(() => GameProtocolEngine.CreatePvpStart(0))
            && Throws(() => GameProtocolEngine.CreatePvpStart(3, 3))
            && Throws(() => GameProtocolEngine.CreatePvpEnterReply([true]))
            && Throws(() => GameProtocolEngine.CreatePvpResult(0, 0, 0, 0, [], 0, true)),
            "PvP builders reject incomplete relay, unsupported mode/map and empty native result rows");
    }

    private static void Parsers(Action<bool, string> check)
    {
        byte[] CustomCreate() => Convert.FromHexString("34120004000000BEC6B9DD030000");
        check(GameProtocolEngine.TryReadPvpCreate(CustomCreate(), out var create)
            && create!.NameSelection == 0 && create.Name.SequenceEqual(new byte[] { 0xBE, 0xC6, 0xB9, 0xDD }) && create.RequestedMap == 3,
            "create consumes the seq prefix and CP936 byte count without a later-server mode byte");
        check(GameProtocolEngine.TryReadPvpCreate([0, 0, 16, 0, 0, 0], out var preset) && preset!.NameSelection == 16
            && !GameProtocolEngine.TryReadPvpCreate([0, 0, 17, 0, 0, 0], out _)
            && !GameProtocolEngine.TryReadPvpCreate([0, 0, 1, 0, 0, 1, 0, 0, 0, 0], out _)
            && !GameProtocolEngine.TryReadPvpCreate([..CustomCreate(), 0], out _)
            && !GameProtocolEngine.TryReadPvpCreate([0, 0, 0, 255, 255, 255, 255], out _)
            && !GameProtocolEngine.TryReadPvpCreate([0, 0, 0, 1, 0, 0, 0, 0xBE, 0, 0, 0], out _)
            && !GameProtocolEngine.TryReadPvpCreate([0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0], out _),
            "create rejects unknown presets, password rooms, NUL, length overflow and trailing bytes");
        check(GameProtocolEngine.TryReadPvpEnter([0, 0, 9, 0, 0], out var room) && room == 9
            && !GameProtocolEngine.TryReadPvpEnter([0, 0, 9, 0, 1], out _)
            && !GameProtocolEngine.TryReadPvpEnter([0, 0, 9, 0, 0, 0], out _), "join accepts only the exact unlocked ACT1 layout");
        check(GameProtocolEngine.TryReadPvpSeat([0, 0, 1, 254], out var seat, out var state) && seat == 1 && state == 254
            && !GameProtocolEngine.TryReadPvpSeat([0, 0, 8, 0], out _, out _)
            && !GameProtocolEngine.TryReadPvpSeat([0, 0, 1, 3], out _, out _), "seat requests bound individual seats and disallow observer roles");
        check(GameProtocolEngine.TryReadPvpReady([0, 0, 1], out var ready) && ready
            && !GameProtocolEngine.TryReadPvpReady([0, 0, 2], out _)
            && GameProtocolEngine.TryReadPvpMode([0, 0, 0], out _)
            && !GameProtocolEngine.TryReadPvpMode([0, 0, 3], out _)
            && GameProtocolEngine.TryReadPvpMap([0, 0, 3, 0], out var map) && map == 3
            && !GameProtocolEngine.TryReadPvpMap([0, 0, 3, 0, 0], out _)
            && GameProtocolEngine.TryReadPvpDeath([0, 0, 255, 255], out var killer) && killer == 65535,
            "ready, individual mode, map and death have strict ACT1 lengths and values");
        var timeout = new byte[34];
        BinaryPrimitives.WriteUInt32LittleEndian(timeout.AsSpan(2), 1234);
        check(GameProtocolEngine.TryReadPvpTimeout(timeout, out var hp) && hp.Length == 8 && hp[0] == 1234
            && !GameProtocolEngine.TryReadPvpTimeout(timeout[..^1], out _)
            && !GameProtocolEngine.TryReadPvpTimeout([..timeout, 0], out _), "timeout is exactly eight u32 HP-decrease accumulators");
        var rank = Convert.FromHexString("34120100020003000400050006000700080009000A000B000100000002000000030000000400000005000000060000000700000008000000");
        check(GameProtocolEngine.TryReadPvpRank(rank, out var report) && report!.Stats.SequenceEqual(Enumerable.Range(1, 11).Select(i => (ushort)i))
            && report.Scores.SequenceEqual(Enumerable.Range(1, 8).Select(i => (uint)i)) && report.TotalScore == 36
            && !GameProtocolEngine.TryReadPvpRank(rank[..^1], out _)
            && !GameProtocolEngine.TryReadPvpRank([..rank, 0, 0, 0, 0, 0, 0, 0, 0], out _),
            "rank report consumes eleven shorts and eight ints and rejects the later-server tail");
        BinaryPrimitives.WriteUInt32LittleEndian(rank.AsSpan(24), uint.MaxValue);
        check(!GameProtocolEngine.TryReadPvpRank(rank, out _), "rank sums in 64 bits and rejects overflow rather than wrapping");
        rank.AsSpan(28).Clear();
        check(GameProtocolEngine.TryReadPvpRank(rank, out var maximum) && maximum!.TotalScore == uint.MaxValue,
            "rank accepts the maximum representable sum without signed overflow");
        check(GameProtocolEngine.TryReadPvpAck([0, 0]) && !GameProtocolEngine.TryReadPvpAck([0, 0, 0])
            && GameProtocolEngine.TryReadPvpHeartbeat([0, 0]) && !GameProtocolEngine.TryReadPvpHeartbeat([]),
            "result ACK and heartbeat have empty payloads after their sequence");
    }

    private static void RatingAndMaps(Action<bool, string> check)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "tests", "DFLegacy.SmokeTests", "Fixtures"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("Cannot locate checked-in PvP numeric oracle.");
        var rows = File.ReadAllLines(Path.Combine(root.FullName, "tests", "DFLegacy.SmokeTests", "Fixtures", "PvpRatingOracle.csv"))
            .Where(line => !line.StartsWith('#')).Select(line => line.Split(',').Select(int.Parse).ToArray()).ToArray();
        var correct = rows.Length == 961;
        foreach (var row in rows)
        {
            var actual = PvpRatingPolicy.CalculateDeltas((byte)row[0], (byte)row[1]);
            correct &= actual.RawWinnerDelta == row[2] && actual.RawLoserDelta == row[3]
                && actual.WinnerDelta == row[4] && actual.LoserDelta == row[5]
                && PvpRatingPolicy.ForfeitDelta((byte)row[1], (byte)row[0]) == row[5];
        }
        check(correct, "all 961 rank pairs match the independently recovered instruction-arithmetic oracle, including leave penalty");
        var folder = Path.Combine(Path.GetTempPath(), "dflegacy-pvp-catalog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "etc"));
        Directory.CreateDirectory(Path.Combine(folder, "map"));
        try
        {
            int[] thresholds = [2000,8000,19000,36000,59000,91000,131000,181000,240000,275000,315000,365000,422000,490000,566000,655000,753000,865000,986000,1081000,1259000,1526000,1889000,2355000,2930000,3620000,4431000,5370000,6444000,9999999];
            File.WriteAllText(Path.Combine(folder, "etc", "pvp_ref.etc"), "[pvp experience max grade]\n30\n[pvp experience]\n"
                + string.Join('\n', thresholds.Select((value, index) => $"{index + 1} {value}")) + "\n[/pvp experience]\n");
            File.WriteAllText(Path.Combine(folder, "map", "map.lst"), "50039 `seven.map`\n50000 `RANDOM`\n50035 `three.map`\n50019 `two.map`\n50036 `four.map`\n50037 `five.map`\n50038 `six.map`\n50018 `one.map`\n49999 `ordinary.map`\n51000 `unrelated.map`");
            foreach (var name in new[] { "one", "two", "three", "four", "five", "six", "seven" })
                File.WriteAllText(Path.Combine(folder, "map", name + ".map"), name == "four" ? "[player number]\n6 8" : "[player number] 2 8");
            var scripts = new ScriptFileSystem(new ServerOptions { ScriptPvfPath = "", SkillScriptPath = folder }, NullLogger<ScriptFileSystem>.Instance);
            var catalog = new PvpExperienceCatalog(scripts, NullLogger<PvpExperienceCatalog>.Instance);
            var fresh = PvpRatingPolicy.Apply(catalog, 0, 0, 1000);
            var novice = PvpRatingPolicy.Apply(catalog, 1, 0, -1000);
            var floor = PvpRatingPolicy.Apply(catalog, 240100, 10, -899);
            var last = PvpRatingPolicy.Apply(catalog, 10000, 30, -899);
            var threshold = PvpRatingPolicy.Apply(catalog, 1000, 1, 1000);
            check(fresh == new PvpPointChange(1000, 1, 1000) && novice == new PvpPointChange(1, 0, 0)
                && floor == new PvpPointChange(240001, 10, -99) && last == new PvpPointChange(9101, 30, -899)
                && threshold.Grade == 2 && PvpRatingPolicy.Apply(catalog, 8000, 1, 0).Grade == 1,
                "point application preserves novice/zero early exits, source veteran floor, old30 and ACT1 exact-threshold promotion");
            var maps = new PvpMapCatalog(scripts);
            check(maps.Entries.Select(m => m.ResourceId).SequenceEqual(new[] { 50000, 50018, 50019, 50035, 50036, 50037, 50038, 50039 })
                && maps.Select(3) == 3 && !maps.IsValidRequested(4) && !maps.IsValidRequested(50035)
                && maps.IsValidRequested(0) && !maps.IsValidRequested(8), "maps use sorted existing resource ordinals and enforce 2-player bounds");
            var random = new Random(1234);
            var choices = Enumerable.Range(0, 256).Select(_ => maps.Select(0, random: random)).ToHashSet();
            check(choices.SetEquals(new byte[] { 1, 2, 3, 5, 6, 7 }) && Throws(() => maps.Select(4)),
                "random selection uses every legal native map while excluding placeholder and six-player map");
        }
        finally { Directory.Delete(folder, true); }
    }

    private static bool Throws(Action action)
    {
        try { action(); return false; }
        catch (ArgumentException) { return true; }
        catch (InvalidOperationException) { return true; }
    }
}
