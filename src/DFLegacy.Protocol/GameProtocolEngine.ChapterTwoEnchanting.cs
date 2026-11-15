using System.Buffers.Binary;

namespace DFLegacy.Protocol;

public static partial class GameProtocolEngine
{
    public const byte ConfirmEnchantProposalCommand = 179;
    public const byte SubmitEnchantRecipeCommand = 180;
    public const byte SetEnchantTermsCommand = 181;
    public const byte EnchantProposalNotification = 168;
    public const byte EnchantTermsNotification = 169;
    public const byte CloseEnchantWindowNotification = 170;

    public static bool TryReadEnchantPeer(ReadOnlySpan<byte> body, bool response,
        out ushort peerUid, out byte peerType)
    {
        peerUid = 0;
        peerType = 0;
        if (!HasCommandBodyLength(body, 9, allowChapterTwoPadding: true)
            || body[4] is not (6 or 7))
            return false;

        var expected = response || body[4] == 6 ? 0u : 1u;
        if (BinaryPrimitives.ReadUInt32LittleEndian(body[5..]) != expected)
            return false;

        peerUid = BinaryPrimitives.ReadUInt16LittleEndian(body[2..]);
        peerType = body[4];
        return peerUid != 0;
    }

    public static bool TryReadEnchantConsent(ReadOnlySpan<byte> body, out byte consent)
    {
        consent = 0;
        if (!HasCommandBodyLength(body, 3, allowChapterTwoPadding: true) || body[2] > 2)
            return false;
        consent = body[2];
        return true;
    }

    public static bool TryReadCooperativeEnchantRecipe(ReadOnlySpan<byte> body, out ushort slot, out bool cancel)
    {
        slot = 0;
        cancel = HasCommandBodyLength(body, 3, allowChapterTwoPadding: true) && body[2] == 2;
        if (cancel) return true;
        if (!HasCommandBodyLength(body, 7, allowChapterTwoPadding: true)
            || body[2] != 1 || body[3] != 1 || body[4] != 0)
            return false;

        // Mode 1 has one main-bag recipe entry. The mode-0 sender is the self-enchant flow.
        slot = BinaryPrimitives.ReadUInt16LittleEndian(body[5..]);
        return true;
    }

    public static bool TryReadEnchantTerms(ReadOnlySpan<byte> body, out uint? fee, out byte list, out ushort slot)
    {
        fee = null;
        list = 0;
        slot = 0;
        if (HasCommandBodyLength(body, 7, allowChapterTwoPadding: true) && body[2] == 0)
        {
            fee = BinaryPrimitives.ReadUInt32LittleEndian(body[3..]);
            return true;
        }

        if (!HasCommandBodyLength(body, 6, allowChapterTwoPadding: true)
            || body[2] != 1 || body[3] is not (0 or 9))
            return false;
        list = body[3];
        slot = BinaryPrimitives.ReadUInt16LittleEndian(body[4..]);
        return true;
    }

    public static GameServerPacket CreateEnchantPeerPacket(bool opening, ushort peerUid, byte mode, uint value = 0)
    {
        var payload = new byte[7];
        BinaryPrimitives.WriteUInt16LittleEndian(payload, peerUid);
        payload[2] = mode;
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(3), value);
        return new(NotificationPacketType, opening ? ResponsePeerNotification : RequestPeerNotification, payload);
    }

    public static GameServerPacket CreateEnchantTermsNotification(uint? fee, ushort equipmentId = 0)
    {
        if (fee is { } amount)
        {
            var payload = new byte[5];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(1), amount);
            return new(NotificationPacketType, EnchantTermsNotification, payload);
        }
        var equipment = new byte[3];
        equipment[0] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(equipment.AsSpan(1), equipmentId);
        return new(NotificationPacketType, EnchantTermsNotification, equipment);
    }

    public static GameServerPacket CreateEnchantProposal(ushort chance, ushort recipeId)
    {
        var payload = new byte[] { 1, 0, 0, 1, 0, 0 };
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(1), chance);
        // The original client resolves the final ID through the recipe's ingredient table.
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4), recipeId);
        return new(NotificationPacketType, EnchantProposalNotification, payload);
    }

    public static GameServerPacket CreateEnchantResult(bool succeeded, uint oldProficiency, uint newProficiency,
        IReadOnlyList<(byte ListType, ushort Slot, uint Count)> consumed)
    {
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write((byte)1);
        writer.Write((byte)0);
        writer.Write((byte)(succeeded ? 1 : 0));
        writer.Write(oldProficiency);
        writer.Write(newProficiency);
        writer.Write(checked((byte)consumed.Count));
        foreach (var (list, slot, count) in consumed)
        {
            writer.Write(list);
            writer.Write(slot);
            writer.Write(count);
        }
        return new(CommandPacketType, SubmitEnchantRecipeCommand, payload.ToArray());
    }

    public static GameServerPacket CreateEnchantDeclined() =>
        new(CommandPacketType, SubmitEnchantRecipeCommand, [1, 3]);

    public static GameServerPacket CreateEnchantReply(byte op, byte? consent = null) =>
        new(CommandPacketType, op, consent is { } accepted ? [1, accepted] : [1]);

    public static GameServerPacket CreateEnchantError(byte op, byte error) =>
        new(CommandPacketType, op, [0, error]);

    public static GameServerPacket CreateCloseEnchantWindow() =>
        new(NotificationPacketType, CloseEnchantWindowNotification, []);
}
