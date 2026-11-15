using System.Buffers.Binary;
using System.Text;
using DFLegacy.Protocol;
using DFLegacy.Server;

internal static class PartyChatSmokeTests
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        CheckChapterTwoChatPadding(check);
        await using var harness = await PartyHarness.CreateAsync();
        Task<int> Say(string name, byte[] text) =>
            harness.Service.SendPartyChatAsync(harness.IdOf(name), text, CancellationToken.None);

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var text = Encoding.GetEncoding(936).GetBytes("队伍测试 only");
        harness.ClearAll();
        check(await Say("阿尔法", text) == 0 && harness.Of("阿尔法").Count == 0,
            "a party-chat request without a party stays silent");

        await harness.InviteAndAcceptAsync("阿尔法", "布拉沃");
        await harness.LeaveTownAsync("阿尔法");
        await harness.LeaveTownAsync("布拉沃");
        harness.ClearAll();
        check(await Say("阿尔法", text) == 2,
            "party chat reaches self and teammate while both are outside the town directory");
        var expected = new byte[7 + text.Length];
        expected[0] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(expected.AsSpan(1), harness.UidOf("阿尔法"));
        BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(3), (uint)text.Length);
        text.CopyTo(expected, 7);
        foreach (var name in new[] { "阿尔法", "布拉沃" })
            check(harness.Of(name) is [var packet] && packet.Type == 0 && packet.ProtocolId == 12
                    && packet.Payload.SequenceEqual(expected),
                "ACT1 party chat carries type, sender uid and text without a server-group byte");
        check(harness.Of("查理").Count == 0 && harness.Of("德尔塔").Count == 0,
            "party chat never leaks to town bystanders");

        await harness.Service.AttachChannelAsync("bobby", "布拉沃", harness.IdOf("布拉沃"),
            harness.UidOf("布拉沃"), harness.SessionIdOf("布拉沃"),
            _ => ValueTask.FromException(new IOException("disconnected teammate")), CancellationToken.None);
        harness.ClearAll();
        check(await Say("阿尔法", text) == 1 && harness.Of("阿尔法").Count == 1,
            "a broken teammate channel cannot end the sender session or suppress other recipients");

        await harness.Service.HandleLeaveAsync(harness.IdOf("布拉沃"), CancellationToken.None);
        harness.ClearAll();
        check(await Say("阿尔法", text) == 1 && harness.Of("布拉沃").Count == 0,
            "a departed teammate receives no future party messages");
        check(await Say("布拉沃", text) == 0,
            "a former member cannot send to the old party");
    }

    private static void CheckChapterTwoChatPadding(Action<bool, string> check)
    {
        // Exact decoded native CMD17, Fix07 .2, 2026-11-09 19:16:11.
        var captured = Convert.FromHexString(
            "1B00030100000000001700000061626262626262626262626262626262626262626262620000000000000000000000000000");
        check(EntranceService.TryReadSendMessage(captured, out var native, chapterTwo: true)
                && native.MessageType == 3 && native.TargetOrItemSpace == 1
                && native.MessageBytes.SequenceEqual(Encoding.ASCII.GetBytes("a" + new string('b', 22))),
            "captured native Chapter Two nearby chat accepts its 23 text bytes and 14 zero AES-padding bytes");
        check(!EntranceService.TryReadSendMessage(captured, out _),
            "the same padded native chat remains invalid in the strict Chapter One format");

        // Exercise every AES tail width, plus the largest legal message. Peer
        // names remain a separate length-prefixed field before any transport padding.
        foreach (var type in new byte[] { 1, 2, 3, 7, 18 })
        foreach (var length in Enumerable.Range(1, 32).Append(255))
        {
            var hasPeer = type is 1 or 7 or 18;
            var text = Enumerable.Repeat((byte)'m', length).ToArray();
            var logical = Body(type, text, hasPeer ? "PeerOne"u8.ToArray() : null);
            var padded = Pad(logical);
            var acceptsPadded = !hasPeer || padded.Length == logical.Length;
            var accepted = EntranceService.TryReadSendMessage(padded, out var parsed, chapterTwo: true);
            check(accepted == acceptsPadded && (!accepted
                    || parsed.MessageType == type && parsed.MessageBytes.SequenceEqual(text)),
                $"Chapter Two chat type{type} length{length} preserves its evidenced content boundary");
            check(EntranceService.TryReadSendMessage(logical, out var legacy)
                    && legacy.MessageBytes.SequenceEqual(text)
                    && (padded.Length == logical.Length || !EntranceService.TryReadSendMessage(padded, out _)),
                $"Chapter One chat type{type} length{length} retains its exact content boundary");

            var extraBlock = new byte[padded.Length + 16]; padded.CopyTo(extraBlock, 0);
            check(!EntranceService.TryReadSendMessage(extraBlock, out _, chapterTwo: true),
                $"Chapter Two chat type{type} length{length} rejects an unnecessary all-zero AES block");
            var nonzero = padded.Length == logical.Length ? extraBlock : padded.ToArray();
            nonzero[^1] = 1;
            check(!EntranceService.TryReadSendMessage(nonzero, out _, chapterTwo: true),
                $"Chapter Two chat type{type} length{length} rejects nonzero transport tail bytes");
            check(!EntranceService.TryReadSendMessage([.. padded, 0], out _, chapterTwo: true),
                $"Chapter Two chat type{type} length{length} rejects a tail outside the AES boundary");
        }

        foreach (var type in new byte[] { 1, 7, 18 })
        foreach (var peerLength in new[] { 0, 1, 30, 31 })
        {
            var logical = Body(type, "text"u8.ToArray(), Enumerable.Repeat((byte)'p', peerLength).ToArray());
            check(EntranceService.TryReadSendMessage(Pad(logical), out _, chapterTwo: true)
                    == (peerLength is >= 1 and <= 30 && Pad(logical).Length == logical.Length),
                $"Chapter Two chat type{type} preserves the peer-name length boundary at{peerLength}");
        }
        foreach (var type in new byte[] { 1, 2, 3, 7, 18 })
        {
            var peer = type is 1 or 7 or 18 ? "PeerOne"u8.ToArray() : null;
            check(!EntranceService.TryReadSendMessage(Pad(Body(type, [], peer)), out _, true)
                    && !EntranceService.TryReadSendMessage(Pad(Body(type, new byte[256], peer)), out _, true)
                    && !EntranceService.TryReadSendMessage(Pad(Body(type, [1, 0, 2], peer)), out _, true),
                $"Chapter Two chat type{type} rejects empty, overlong and embedded-NUL text");
        }
        foreach (var type in new byte[] { 4, 16, 99 })
            check(!EntranceService.TryReadSendMessage(Pad(Body(type, "short!"u8.ToArray(), null)), out _, true),
                $"unhandled chat type{type} does not gain a new padded format");

        static byte[] Body(byte type, byte[] text, byte[]? peer)
        {
            using var body = new MemoryStream(); using var writer = new BinaryWriter(body);
            writer.Write((ushort)27); writer.Write(type); writer.Write((ushort)1); writer.Write(0u);
            writer.Write((uint)text.Length); writer.Write(text);
            if (peer is not null) { writer.Write((uint)peer.Length); writer.Write(peer); }
            return body.ToArray();
        }
        static byte[] Pad(byte[] logical)
        {
            var padded = new byte[2 + (logical.Length - 2 + 15) / 16 * 16];
            logical.CopyTo(padded, 0); return padded;
        }
    }
}
