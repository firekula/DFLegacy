using System.Buffers.Binary;
using DFLegacy.Protocol;

namespace DFLegacy.Server;

internal static class ChapterTwoPacketProjection
{
    public static GameServerPacket Apply(GameServerPacket packet)
    {
        if (packet.Type != GameProtocolEngine.NotificationPacketType || packet.Payload.Length == 0)
        {
            return packet;
        }

        var payload = packet.Payload.AsSpan();
        if (packet.ProtocolId == GameProtocolEngine.UdpPeerInfoNotification && payload.Length == 1 + payload[0] * 21)
        {
            // Ver_OB_216 reads an extra u8 after each peer's MTU and stores it at +0x118.
            return ExpandRecords(packet, 1, payload[0], 21, [0]);
        }

        if (packet.ProtocolId == GameProtocolEngine.DropItemNotification && payload.Length == 17)
        {
            // Ver_OB_216 reads a trailing u16 for the item constructor. ACT1 passes zero there.
            return packet with { Payload = [.. packet.Payload, 0, 0] };
        }

        if (packet.ProtocolId == GameProtocolEngine.GetItemNotification && payload.Length == 4 + 4 * 5)
        {
            // Gold is the 24-byte NOTI39 branch. The ordinary item branch is 12 bytes.
            // Ver_OB_216 reads presence then amount, with an adjustment count only for a present share.
            using var result = new MemoryStream();
            using var writer = new BinaryWriter(result);
            writer.Write(payload[..4]);
            for (var offset = 4; offset < payload.Length; offset += 5)
            {
                var amount = BinaryPrimitives.ReadUInt32LittleEndian(payload[offset..]);
                writer.Write((byte)(amount == 0 ? 0 : 1));
                writer.Write(amount);
                if (amount != 0)
                {
                    writer.Write(payload[offset + 4]);
                }
            }

            return packet with { Payload = result.ToArray() };
        }

        if (packet.ProtocolId is GameProtocolEngine.ItemListNotification or GameProtocolEngine.UpdateItemListNotification)
        {
            var countOffset = packet.ProtocolId == GameProtocolEngine.ItemListNotification && payload[0] == 2 ? 3 : 1;
            var recordsOffset = countOffset + sizeof(ushort);
            if (payload.Length < recordsOffset)
            {
                return packet;
            }

            var count = BinaryPrimitives.ReadUInt16LittleEndian(payload[countOffset..]);
            return payload.Length == recordsOffset + count * 12
                ? ExpandRecords(packet, recordsOffset, count, 12, [0, 0])
                : packet;
        }

        if (packet.ProtocolId == GameProtocolEngine.UserInfoNotification
            && payload[0] is 1 or 3 && payload.Length >= 14
            && BinaryPrimitives.ReadUInt16LittleEndian(payload[1..]) == 1)
        {
            var statsLength = BinaryPrimitives.ReadUInt32LittleEndian(payload[9..]);
            if (statsLength > payload.Length - 14)
            {
                return packet;
            }

            var countOffset = 13 + (int)statsLength;
            var count = payload[countOffset];
            if (payload.Length >= countOffset + 1 + count * 10)
            {
                // Native detail handling skips the optional item link for FFFF.
                return ExpandRecords(packet, countOffset + 1, count, 10, [0xff, 0xff, 0, 0]);
            }
        }

        return packet;
    }

    private static GameServerPacket ExpandRecords(
        GameServerPacket packet, int offset, int count, int stride, ReadOnlySpan<byte> extra)
    {
        if (count == 0)
        {
            return packet;
        }

        var source = packet.Payload.AsSpan();
        var payload = new byte[source.Length + count * extra.Length];
        source[..offset].CopyTo(payload);
        var destination = offset;
        for (var index = 0; index < count; index++)
        {
            source.Slice(offset, stride).CopyTo(payload.AsSpan(destination));
            extra.CopyTo(payload.AsSpan(destination + stride));
            offset += stride;
            destination += stride + extra.Length;
        }

        source[offset..].CopyTo(payload.AsSpan(destination));
        return packet with { Payload = payload };
    }
}
