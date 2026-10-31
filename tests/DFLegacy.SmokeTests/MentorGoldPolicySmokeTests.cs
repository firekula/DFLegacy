using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using DFLegacy.Protocol;
using DFLegacy.Server;

internal static class MentorGoldPolicySmokeTests
{
    internal static string FixtureRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                var candidate = Path.Combine(directory.FullName, "tests/DFLegacy.SmokeTests/Fixtures/MentorGold");
                if (Directory.Exists(candidate)) return candidate;
                directory = directory.Parent;
            }
            throw new DirectoryNotFoundException("MentorGold fixtures");
        }
    }

    public static void Run(Action<bool, string> check)
    {
        CheckProvenance(check);
        CheckMath(check, MentorGoldPolicy.Capacity, MentorGoldPolicy.Rate, MentorGoldPolicy.Award);
        CheckTimer(check, MentorGoldTimerPolicy.NextJitter, MentorGoldTimerPolicy.DelaySeconds);
        CheckPacket(check, GameProtocolEngine.CreateMentorGoldAward);
    }

    private static void CheckProvenance(Action<bool, string> check)
    {
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(FixtureRoot, "manifest.json")))!;
        var pvf = manifest["pvfEntry"]!;
        var entry = File.ReadAllBytes(Path.Combine(FixtureRoot, pvf["path"]!.GetValue<string>()));
        check(pvf["pvfSha256"]!.GetValue<string>() == "288d58aace8ed4724a0a94aaa4cd0957b1d52c633771132ede2f96363c1b506f"
              && entry.Length == pvf["length"]!.GetValue<int>()
              && Convert.ToHexStringLower(SHA256.HashData(entry)) == pvf["entrySha256"]!.GetValue<string>()
              && pvf["fixtureSha256"]!.GetValue<string>() == pvf["entrySha256"]!.GetValue<string>(),
            "mentor wallet fixture retains the original ACT1 PVF entry bytes and archive provenance");
        var member = manifest["memberTable"]!;
        check(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(FixtureRoot, "member_cnt_config.tbl"))))
              == member["sha256"]!.GetValue<string>(), "mentor capacity fixture retains original Monitor member table bytes");
        foreach (var derived in manifest["derived"]!.AsArray())
        {
            var file = derived!["path"]!.GetValue<string>();
            var bytes = File.ReadAllBytes(Path.Combine(FixtureRoot, file));
            // Derived JSON/Python may use checkout CRLF; original table/PVF bytes above never normalize.
            var canonical = bytes.Where((b, i) => b != 13 || i + 1 >= bytes.Length || bytes[i + 1] != 10).ToArray();
            check(Convert.ToHexStringLower(SHA256.HashData(canonical)) == derived["sha256"]!.GetValue<string>(),
                $"mentor independent oracle provenance: {file}");
        }
    }

    // Catches incorrect master tiers, float constants/stores, cached N/C, and rounded final multiplication.
    private static void CheckMath(Action<bool, string> check, Func<int, int> capacity,
        Func<byte, int, int, float> rate, Func<uint, float, uint> award)
    {
        foreach (var (level, expected) in new (int, int)[] { (int.MinValue,0),(-1,0),(0,0),(9,0),(10,5),(18,5),
                     (19,5),(20,8),(29,8),(30,10),(59,10),(99,10),(100,0),(int.MaxValue,0) })
            check(capacity(level) == expected, $"mentor native capacity master level {level} = {expected}");
        var oracle = JsonNode.Parse(File.ReadAllText(Path.Combine(FixtureRoot, "math-oracle.json")))!;
        var rows = oracle["rates"]!.AsArray();
        check(rows.Count == 230, "independent rate oracle covers every L1..10 / C5,8,10 / N1..C");
        foreach (var row in rows)
        {
            var l = row!["level"]!.GetValue<byte>(); var n = row["count"]!.GetValue<int>(); var c = row["capacity"]!.GetValue<int>();
            var bits = uint.Parse(row["rateBits"]!.GetValue<string>(), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var actual = rate(l, n, c);
            check(BitConverter.SingleToUInt32Bits(actual) == bits, $"mentor Monitor float stores L{l}/N{n}/C{c}");
            check(award(1000, actual) == row["award1000"]!.GetValue<uint>()
                  && award(uint.MaxValue, actual) == row["awardMax"]!.GetValue<uint>(),
                $"mentor exact unsigned batch truncation L{l}/N{n}/C{c}");
        }
        foreach (var row in oracle["integerBoundaries"]!.AsArray())
        {
            var basis = row!["basis"]!.GetValue<uint>();
            var bits = uint.Parse(row["rateBits"]!.GetValue<string>(), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            check(award(basis, BitConverter.UInt32BitsToSingle(bits)) == row["award"]!.GetValue<uint>(),
                $"mentor exact product at integer boundary basis {basis} / rate {bits:X8}");
        }
        var low = rate(1, 1, 10);
        check(award(1000, low) == 14 && award(1, low) == 0 && award(0, low) == 0,
            "mentor L1/N1/C10 produces fourteen per thousand and truncates sub-gold batches");
        check(award(67, low) + award(67, low) == 2 && award(134, low) == 2
              && award(66, low) + award(66, low) == 0 && award(132, low) == 1,
            "mentor flushes discard their own fractions instead of carrying between batches");
        check(BitConverter.SingleToUInt32Bits(rate(1, 11, 10)) == 0x3D851EB8,
            "mentor connected count above capacity is evaluated without clamping the ratio");
        check(Throws<ArgumentOutOfRangeException>(() => rate(1, 1, 0)), "mentor rejects zero denominator before division");
        check(Throws<ArgumentOutOfRangeException>(() => rate(0, 1, 10))
              && Throws<ArgumentOutOfRangeException>(() => rate(11, 1, 10))
              && Throws<ArgumentOutOfRangeException>(() => rate(1, -1, 10)), "mentor rejects invalid relationship/count inputs");
        check(Throws<ArgumentOutOfRangeException>(() => award(1, float.NaN))
              && Throws<ArgumentOutOfRangeException>(() => award(1, float.PositiveInfinity))
              && Throws<ArgumentOutOfRangeException>(() => award(1, -0.01f)), "mentor rejects nonfinite and negative award rates");
        check(award(uint.MaxValue, 0.5f) == 2147483647 && award(uint.MaxValue, 1f) == uint.MaxValue
              && award(uint.MaxValue, float.Epsilon) == 0 && award(uint.MaxValue, 0f) == 0,
            "mentor exact product handles unsigned high-bit bases, identity, zero and subnormal rates");
    }

    // Catches replacing get_rand_int(300) with uniform Next(301), signed shifts, or a one-draw modulo.
    private static void CheckTimer(Action<bool, string> check, Func<Func<int>, int> next, Func<int, int> delay)
    {
        foreach (var (second, expected) in new (int, int)[] { (0,0),(299,299),(300,0),(int.MaxValue,247) })
        {
            var calls = 0;
            int Rand() => ++calls == 1 ? 301 : second;
            check(next(Rand) == expected && calls == 2, $"mentor ordinary random branch draws twice and maps {second} to {expected}");
        }
        var oracle = JsonNode.Parse(File.ReadAllText(Path.Combine(FixtureRoot, "math-oracle.json")))!;
        foreach (var row in oracle["alternateJitter"]!.AsArray())
        {
            var seed = row!["seed"]!.GetValue<int>(); var calls = 0;
            int Rand() { calls++; return seed; }
            check(next(Rand) == row["jitter"]!.GetValue<int>() && calls == 1,
                $"mentor alternate signed-LCG random branch seed {seed}");
        }
        check(delay(0) == 600 && delay(299) == 899 && delay(300) == 900,
            "mentor tax timer preserves the full 600/899/900-second delay contract");
        check(Throws<ArgumentOutOfRangeException>(() => delay(-1)) && Throws<ArgumentOutOfRangeException>(() => delay(301)),
            "mentor delay rejects values outside native bounded result domain");
        check(Throws<ArgumentNullException>(() => next(null!)) && Throws<ArgumentOutOfRangeException>(() => next(() => -1)),
            "mentor random adapter rejects missing or invalid rand31 samples");
    }

    // Catches the six-byte shortcut, missing CP936 source bytes, character-length prefixes, and wrong notification id.
    private static void CheckPacket(Action<bool, string> check, Func<uint, byte[], GameServerPacket> build)
    {
        var packet = build(14, [0xCA,0xD5,0xC0,0xF1]);
        check(packet.Type == GameProtocolEngine.NotificationPacketType && packet.ProtocolId == 76
              && Convert.ToHexString(packet.Payload) == "0E000000000004000000CAD5C0F1",
            "mentor NOTI76 includes gold14, zero fatigue and full string32 CP936 收礼");
        var longName = Enumerable.Repeat((byte)0x41, 49).ToArray();
        var accepted = build(uint.MaxValue, longName);
        check(accepted.Payload.Length == 59 && BinaryPrimitives.ReadUInt32LittleEndian(accepted.Payload) == uint.MaxValue
              && BinaryPrimitives.ReadInt16LittleEndian(accepted.Payload.AsSpan(4)) == 0
              && BinaryPrimitives.ReadUInt32LittleEndian(accepted.Payload.AsSpan(6)) == 49
              && accepted.Payload.AsSpan(10).SequenceEqual(longName), "mentor NOTI76 accepts the native 49-byte name boundary");
        check(Throws<ArgumentException>(() => build(14, [])) && Throws<ArgumentException>(() => build(14, new byte[50]))
              && Throws<ArgumentNullException>(() => build(14, null!)), "mentor NOTI76 rejects missing or invalid native name byte lengths");
    }

    private static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); return false; }
        catch (T) { return true; }
    }
}
