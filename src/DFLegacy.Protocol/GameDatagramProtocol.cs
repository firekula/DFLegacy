using System.Buffers.Binary;
using System.Net;

namespace DFLegacy.Protocol;

public sealed record GameNatReport(
    byte NatType,
    IPAddress LocalAddress,
    IPAddress PublicAddress,
    ushort PublicPort,
    uint Mtu);

public sealed record GamePeerInfo(
    ushort UserId,
    IPAddress LocalAddress,
    IPAddress PublicAddress,
    ushort PublicPort,
    uint AccountId,
    byte NatType,
    uint Mtu);

public sealed record GameDatagramApplication(
    byte Mode,
    ushort ProtocolId,
    byte[] Payload);

public sealed record GameMtUdpFrame(
    byte BlockType,
    uint Sequence,
    byte SenderPartyIndex,
    byte Flag,
    byte[] Application);

public static class GameDatagramProtocol
{
    public const int NatReportLength = 17;
    public const byte NatResponseType = 2;
    public const int NatKeepAliveLength = 10;
    /// <summary>Primary/secondary reflector probes are exactly this many bytes.</summary>
    public const int NatProbeLength = 1;
    public const byte ReliableBlockType = 1;
    public const byte UnreliableBlockType = 2;
    public const ushort SetActiveObjectHpProtocol = 12;
    public const int ApplicationHeaderLength = 7;
    public const int MtUdpHeaderLength = 9;

    private const uint ApplicationCipherKey = 0x002F1214;
    private const byte ApplicationXorKey = (byte)(ApplicationCipherKey & 0xff);
    private const int ApplicationRotation = (int)((ApplicationCipherKey >> 8) & 7);

    public static bool TryParseNatReport(
        ReadOnlySpan<byte> commandBody,
        out GameNatReport report)
    {
        report = null!;
        if (commandBody.Length < NatReportLength)
        {
            return false;
        }

        report = new GameNatReport(
            commandBody[2],
            new IPAddress(commandBody.Slice(3, 4)),
            new IPAddress(commandBody.Slice(7, 4)),
            BinaryPrimitives.ReadUInt16BigEndian(commandBody.Slice(11, 2)),
            BinaryPrimitives.ReadUInt32LittleEndian(commandBody.Slice(13, 4)));
        return true;
    }

    /// <summary>
    /// A NAT probe is a single byte (1 = primary reflector, 5 = secondary).
    /// The length check is load bearing: an MTU framing block also begins with
    /// those values -- a nine-byte keepalive is
    /// <c>01 | sequence(4) | party | flag | applicationLength=0</c> -- and
    /// answering one of those with a NAT response makes the client send another,
    /// which is an endless echo. Measured 2026-10-05: two clients on one machine
    /// produced 1,315,632 nine-byte sends and 1,315,576 seven-byte replies in
    /// about two minutes, a 256 MB net dump, and an unresponsive client.
    /// </summary>
    public static bool TryCreateNatProbeResponse(
        ReadOnlySpan<byte> request,
        IPEndPoint remote,
        out byte[] response)
    {
        response = [];
        if (request.Length != NatProbeLength || request[0] is not (1 or 5))
        {
            return false;
        }

        var addressBytes = remote.Address.MapToIPv4().GetAddressBytes();
        response = new byte[7];
        response[0] = NatResponseType;
        for (var index = 0; index < addressBytes.Length; index++)
        {
            response[1 + index] = addressBytes[addressBytes.Length - index - 1];
        }

        BinaryPrimitives.WriteUInt16LittleEndian(
            response.AsSpan(5, 2),
            checked((ushort)remote.Port));
        return true;
    }

    /// <summary>
    /// Secondary-reflector keepalive: zero, the seven-byte NAT response
    /// (type 2, reflected IPv4/port), then two reserved zero bytes. A type-zero
    /// MT UDP selective ACK can also be ten bytes, so length/type alone loses
    /// reliable ACKs. The wire formats can overlap; callers must additionally
    /// restrict this classification to the secondary reflector, never gameplay.
    /// </summary>
    public static bool IsNatKeepAlive(ReadOnlySpan<byte> request) =>
        request.Length == NatKeepAliveLength
        && request[0] == 0
        && request[1] == NatResponseType
        && request[8] == 0
        && request[9] == 0;

    public static byte[] CreateSetActiveObjectHpApplication(
        ushort objectType,
        ushort objectId,
        uint hp,
        uint secondaryValue = 0,
        byte mode = 1)
    {
        Span<byte> payload = stackalloc byte[12];
        BinaryPrimitives.WriteUInt16LittleEndian(payload, objectType);
        BinaryPrimitives.WriteUInt16LittleEndian(payload[2..], objectId);
        BinaryPrimitives.WriteUInt32LittleEndian(payload[4..], hp);
        BinaryPrimitives.WriteUInt32LittleEndian(payload[8..], secondaryValue);
        return CreateApplication(mode, SetActiveObjectHpProtocol, payload);
    }

    public static byte[] CreateApplication(
        byte mode,
        ushort protocolId,
        ReadOnlySpan<byte> plaintextPayload)
    {
        var output = new byte[ApplicationHeaderLength + plaintextPayload.Length];
        output[0] = mode;
        BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(1, 2), protocolId);
        BinaryPrimitives.WriteUInt32LittleEndian(
            output.AsSpan(3, 4),
            Crc32.Compute(plaintextPayload));
        EncryptApplicationPayload(
            plaintextPayload,
            output.AsSpan(ApplicationHeaderLength));
        return output;
    }

    public static bool TryParseApplication(
        ReadOnlySpan<byte> application,
        out GameDatagramApplication parsed)
    {
        parsed = null!;
        if (application.Length < ApplicationHeaderLength)
        {
            return false;
        }

        var payload = new byte[application.Length - ApplicationHeaderLength];
        DecryptApplicationPayload(
            application[ApplicationHeaderLength..],
            payload);
        var declaredCrc = BinaryPrimitives.ReadUInt32LittleEndian(application.Slice(3, 4));
        if (Crc32.Compute(payload) != declaredCrc)
        {
            return false;
        }

        parsed = new GameDatagramApplication(
            application[0],
            BinaryPrimitives.ReadUInt16LittleEndian(application.Slice(1, 2)),
            payload);
        return true;
    }

    public static byte[] CreateUnreliableFrame(
        uint sequence,
        byte senderPartyIndex,
        byte flag,
        ReadOnlySpan<byte> application)
    {
        if (senderPartyIndex >= 8)
        {
            throw new ArgumentOutOfRangeException(nameof(senderPartyIndex));
        }

        if (application.Length > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(application));
        }

        var output = new byte[MtUdpHeaderLength + application.Length];
        output[0] = UnreliableBlockType;
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(1, 4), sequence);
        BinaryPrimitives.WriteUInt16LittleEndian(
            output.AsSpan(5, 2),
            checked((ushort)application.Length));
        output[7] = senderPartyIndex;
        output[8] = flag;
        application.CopyTo(output.AsSpan(MtUdpHeaderLength));
        return output;
    }

    public static bool TryReadBlock(
        ReadOnlySpan<byte> datagram,
        out GameMtUdpFrame? frame,
        out int consumed)
    {
        frame = null;
        consumed = 0;
        if (datagram.Length == 0)
        {
            return false;
        }

        if (datagram[0] == 0)
        {
            if (datagram.Length < 8)
            {
                return false;
            }

            var bitCount = datagram[6];
            consumed = 8 + (bitCount >> 3);
            return consumed <= datagram.Length;
        }

        if (datagram[0] is not (ReliableBlockType or UnreliableBlockType)
            || datagram.Length < MtUdpHeaderLength)
        {
            return false;
        }

        var applicationLength = BinaryPrimitives.ReadUInt16LittleEndian(
            datagram.Slice(5, 2));
        consumed = MtUdpHeaderLength + applicationLength;
        if (consumed > datagram.Length || datagram[7] >= 8)
        {
            consumed = 0;
            return false;
        }

        frame = new GameMtUdpFrame(
            datagram[0],
            BinaryPrimitives.ReadUInt32LittleEndian(datagram.Slice(1, 4)),
            datagram[7],
            datagram[8],
            datagram.Slice(MtUdpHeaderLength, applicationLength).ToArray());
        return true;
    }

    private static void EncryptApplicationPayload(
        ReadOnlySpan<byte> plaintext,
        Span<byte> ciphertext)
    {
        for (var index = 0; index < plaintext.Length; index++)
        {
            ciphertext[index] = RotateRight(
                (byte)(plaintext[index] ^ ApplicationXorKey),
                ApplicationRotation);
        }
    }

    private static void DecryptApplicationPayload(
        ReadOnlySpan<byte> ciphertext,
        Span<byte> plaintext)
    {
        for (var index = 0; index < ciphertext.Length; index++)
        {
            plaintext[index] = (byte)(
                RotateLeft(ciphertext[index], ApplicationRotation)
                ^ ApplicationXorKey);
        }
    }

    private static byte RotateLeft(byte value, int count) =>
        (byte)((value << count) | (value >> (8 - count)));

    private static byte RotateRight(byte value, int count) =>
        (byte)((value >> count) | (value << (8 - count)));
}
