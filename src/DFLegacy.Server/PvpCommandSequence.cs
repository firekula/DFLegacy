using System.Buffers.Binary;

namespace DFLegacy.Server;

/// <summary>
/// Attachment-owned native command cursor, independent of payload-cipher reseeding.
/// Observe the ordinary command stream, not just sparsely spaced PvP opcodes. Direct
/// CHECK_CONNECTION and the separate plugin sender are deliberately excluded.
/// The dictionary has at most 65536 entries. A previously consumed sequence cannot
/// advance the cursor into a fictitious wrap: other admitted traffic must first
/// establish a complete ushort cycle. Identical ancient bytes after that cycle, or
/// deliberately forged fresh sequence values, cannot be authenticated by this wire.
/// </summary>
internal sealed class PvpCommandSequence
{
    private readonly object _gate = new();
    private readonly Dictionary<ushort, long> _seen = [];
    private ushort _last;
    private long _position;
    private bool _initialized;

    internal bool TryConsume(byte command, byte[] body)
    {
        if (command is 0 or >= 160 and <= 162 || body.Length < 2) return false;
        var sequence = BinaryPrimitives.ReadUInt16LittleEndian(body);
        lock (_gate)
        {
            if (!_initialized)
            {
                _initialized = true; _last = sequence; _seen[sequence] = 0;
                return true;
            }
            var distance = unchecked((ushort)(sequence - _last));
            if (distance == 0 || distance >= 32768) return false;
            if (_seen.TryGetValue(sequence, out var consumedAt) && _position - consumedAt < 65535) return false;
            if (_position > long.MaxValue - distance) return false;
            _position += distance; _last = sequence; _seen[sequence] = _position;
            return true;
        }
    }
}
