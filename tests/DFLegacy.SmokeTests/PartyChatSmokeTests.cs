using System.Buffers.Binary;
using System.Text;
using DFLegacy.Protocol;

internal static class PartyChatSmokeTests
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
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
}
