using DFLegacy.InterProcess;
using DFLegacy.Protocol;

internal static class InterProcessCodecSmokeTests
{
    public static void Run(Action<bool, string> check)
    {
        var register = new RegisterPayload(
            ProtocolVersion: InterProcessMessageIds.LinkProtocolVersion,
            GcNo: 3011,
            ChannelNo: 11,
            MaximumUsers: 100,
            CurrentUsers: 1,
            AdvertisedHost: "127.0.0.1",
            GamePort: 7001);
        var frame = InterProcessLink.Create(InterProcessMessageIds.Register, register.Encode());

        check(Convert.ToHexString(frame.Encode()) ==
              "7B803100000000000000" + "01" + "0200" + "C30B0000" + "0B000000" +
              "64000000" + "01000000" + "3132372E302E302E31" + "00000000000000" + "591B0000",
            "REGISTER frame is 49 bytes: category 0x7b, id 128, size 0x31, crc 0, ack 1");
        check(frame.Type == InterProcessMessageIds.LinkCategory
                && frame.ProtocolId == InterProcessMessageIds.Register
                && frame.TotalLength == 49
                && frame.Body.Length == 39
                && frame.RecordCount == 1,
            "REGISTER header fields match the 11-byte model (size includes header)");
        check(InterProcessLink.TryGetMessageId(frame, out var id) && id == InterProcessMessageIds.Register,
            "link frame decodes back to its message id");

        var ack = new RegisterAckPayload(InterProcessMessageIds.LinkProtocolVersion, 1, "Local Channel 1", 7001);
        check(ack.Encode().Length == 30, "REGISTER_ACK payload is 30 bytes");
        var status = new StatusPayload(1, 2, 3, 4, 5);
        check(status.Encode().Length == 32, "STATUS payload is 32 bytes");
        var notice = new NoticeChannelServerPayload(1, "127.0.0.1", 2311);
        check(notice.Encode().Length == 24, "NOTICE_CHANNEL_SERVER payload is 24 bytes");

        var wrongCategory = new PacketFrame(0x00, InterProcessMessageIds.Register, 0, [1, 2, 3]);
        check(!InterProcessLink.IsLinkFrame(wrongCategory),
            "a frame from another link (category 0x00) is not accepted as a link frame");
    }
}
