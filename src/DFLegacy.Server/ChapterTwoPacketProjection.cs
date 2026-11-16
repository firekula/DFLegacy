using System.Buffers.Binary;
using DFLegacy.Protocol;

namespace DFLegacy.Server;

internal static class ChapterTwoPacketProjection
{
    public static GameServerPacket Apply(GameServerPacket packet)
    {
        if (packet.Payload.Length == 0)
        {
            return packet;
        }

        var payload = packet.Payload.AsSpan();
        if (packet.Type == GameProtocolEngine.CommandPacketType)
        {
            // The successful buy reply and each entered-shop row end with the extra item field.
            if (payload[0] == 1 && packet.ProtocolId == GameProtocolEngine.BuyPrivateStoreCommand && payload.Length == 24)
                return packet with { Payload = [.. packet.Payload, 0, 0] };
            if (payload[0] == 1 && packet.ProtocolId == GameProtocolEngine.EnterPrivateStoreCommand
                && payload.Length >= 4 && payload.Length == 4 + payload[3] * 15)
                return ExpandRecords(packet, 4, payload[3], 15, [0, 0]);
            return packet;
        }

        if (packet.Type != GameProtocolEngine.NotificationPacketType)
        {
            return packet;
        }

        if (packet.ProtocolId == GameProtocolEngine.UserStateNotification && payload.Length == 3)
        {
            // Ver_OB_216 reads a row count before the shared UID and state fields.
            return packet with { Payload = [1, .. packet.Payload] };
        }

        if (packet.ProtocolId == GameProtocolEngine.PrivateStoreInfoNotification
            && payload.Length >= 2 && payload.Length == 2 + payload[1] * 15)
        {
            return ExpandRecords(packet, 2, payload[1], 15, [0, 0]);
        }

        if (packet.ProtocolId == GameProtocolEngine.UdpPeerInfoNotification && payload.Length == 1 + payload[0] * 21)
        {
            // Town tables already carry their shared HP/MP percentage. Other peer tables
            // still need the extra byte consumed by Ver_OB_216 after each MTU.
            return ExpandRecords(packet, 1, payload[0], 21, [0]);
        }

        if ((packet.ProtocolId == GameProtocolEngine.DropItemNotification && payload.Length == 17)
            || (packet.ProtocolId == GameProtocolEngine.ChangeItemTradeItemNotification && payload.Length == 11))
        {
            // The ground and trade item readers pass an extra value to the Chapter Two constructor.
            // Chapter One supplies zero for the same constructor argument.
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
                ? ExpandRecords(packet, recordsOffset, count, 12, [0, 0], packet.ChapterTwoInventoryEnchantments)
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
                // Ver_OB_216 0x41e649 reads the clone's style before the enchantment ID.
                return ExpandRecords(packet, countOffset + 1, count, 10, [0xff, 0xff, 0, 0],
                    packet.ChapterTwoInventoryEnchantments, packet.ChapterTwoAvatarAppearances);
            }
        }

        return packet;
    }

    private static GameServerPacket ExpandRecords(
        GameServerPacket packet, int offset, int count, int stride, ReadOnlySpan<byte> extra,
        ushort[]? enchantments = null, ushort[]? avatarAppearances = null)
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
            if (avatarAppearances?.Length == count && avatarAppearances[index] != 0)
                BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(destination + stride), avatarAppearances[index]);
            if (enchantments?.Length == count)
                BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(destination + stride + extra.Length - sizeof(ushort)), enchantments[index]);
            offset += stride;
            destination += stride + extra.Length;
        }

        source[offset..].CopyTo(payload.AsSpan(destination));
        return packet with { Payload = payload };
    }
}
