using System.Buffers.Binary;
using System.Text;
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
        var ackBytes = ack.Encode();
        check(ackBytes.Length == 30 && RegisterAckPayload.TryDecode(ackBytes, out var ackBack) && ackBack == ack,
            "REGISTER_ACK round-trips through TryDecode to the same record (field order pinned)");
        check(BinaryPrimitives.ReadUInt16LittleEndian(ackBytes.AsSpan(0, 2)) == 2
                && BinaryPrimitives.ReadUInt32LittleEndian(ackBytes.AsSpan(2, 4)) == 1
                && Encoding.ASCII.GetString(ackBytes.AsSpan(6, 15)) == "Local Channel 1"
                && ackBytes.AsSpan(21, 5).IndexOfAnyExcept((byte)0) < 0
                && BinaryPrimitives.ReadUInt32LittleEndian(ackBytes.AsSpan(26, 4)) == 7001,
            "REGISTER_ACK fields sit at protocolVersion@0, accepted@2, name@6[20], channelPort@26");
        check(InterProcessLink.Create(InterProcessMessageIds.RegisterAck, ackBytes).TotalLength == 41,
            "REGISTER_ACK frame is 41 bytes: 10-byte header + record count + 30-byte payload");

        var status = new StatusPayload(1, 2, 3, 4, 5);
        var statusBytes = status.Encode();
        check(statusBytes.Length == 32 && StatusPayload.TryDecode(statusBytes, out var statusBack) && statusBack == status,
            "STATUS round-trips through TryDecode to the same record (field order pinned)");
        check(BinaryPrimitives.ReadUInt64LittleEndian(statusBytes.AsSpan(0, 8)) == 1
                && BinaryPrimitives.ReadUInt32LittleEndian(statusBytes.AsSpan(8, 4)) == 2
                && BinaryPrimitives.ReadUInt32LittleEndian(statusBytes.AsSpan(12, 4)) == 3
                && BinaryPrimitives.ReadUInt64LittleEndian(statusBytes.AsSpan(16, 8)) == 4
                && BinaryPrimitives.ReadUInt64LittleEndian(statusBytes.AsSpan(24, 8)) == 5,
            "STATUS fields sit at uptimeMs@0, activeSessions@8, loggedInUsers@12, totalConnections@16, totalPackets@24");
        check(InterProcessLink.Create(InterProcessMessageIds.Status, statusBytes).TotalLength == 43,
            "STATUS frame is 43 bytes: 10-byte header + record count + 32-byte payload");

        var notice = new NoticeChannelServerPayload(1, "127.0.0.1", 2311);
        var noticeBytes = notice.Encode();
        check(noticeBytes.Length == 24
                && NoticeChannelServerPayload.TryDecode(noticeBytes, out var noticeBack)
                && noticeBack == notice,
            "NOTICE_CHANNEL_SERVER round-trips through TryDecode to the same record (field order pinned)");
        check(BinaryPrimitives.ReadInt32LittleEndian(noticeBytes.AsSpan(0, 4)) == 1
                && Encoding.ASCII.GetString(noticeBytes.AsSpan(4, 9)) == "127.0.0.1"
                && noticeBytes.AsSpan(13, 7).IndexOfAnyExcept((byte)0) < 0
                && BinaryPrimitives.ReadInt32LittleEndian(noticeBytes.AsSpan(20, 4)) == 2311,
            "NOTICE_CHANNEL_SERVER fields sit at serverGroup@0, advertisedHost@4[16], port@20");
        check(InterProcessLink.Create(InterProcessMessageIds.NoticeChannelServer, noticeBytes).TotalLength == 35,
            "NOTICE_CHANNEL_SERVER frame is 35 bytes: 10-byte header + record count + 24-byte payload");

        var wrongCategory = new PacketFrame(0x00, InterProcessMessageIds.Register, 0, [1, 2, 3]);
        check(!InterProcessLink.IsLinkFrame(wrongCategory),
            "a frame from another link (category 0x00) is not accepted as a link frame");
    }
}
