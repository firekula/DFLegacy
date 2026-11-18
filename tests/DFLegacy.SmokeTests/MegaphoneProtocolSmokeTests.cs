using DFLegacy.Protocol;

internal static class MegaphoneProtocolSmokeTests
{
    // Captured original server reply: NOTI 118, payload type=11, channel=11,
    // uid=6, name "444444", text "11111".
    internal static void Run(Action<bool, string> check)
    {
        var packet = GameProtocolEngine.CreateMegaphoneNotification(
            GameMessageType.Type11LightCyan, 11, 6, "444444"u8, "11111"u8);
        check(packet.Type == 0 && packet.ProtocolId == 118,
            "a megaphone reply is NOTI 118");
        check(packet.Payload.SequenceEqual(
                Convert.FromHexString("0B0B060006000000343434343434050000003131313131")),
            "the megaphone payload matches the captured type/channel/uid/name/text bytes");

        var channel = GameProtocolEngine.CreateMegaphoneNotification(
            GameMessageType.Type13SpeakerYellow, 7, 1234, "abc"u8, "hello"u8);
        check(channel.ProtocolId == 118 && channel.Payload[0] == 13
            && channel.Payload[1] == 7
            && BitConverter.ToUInt16(channel.Payload, 2) == 1234,
            "a channel megaphone keeps type 13 and its channel number");
    }
}
