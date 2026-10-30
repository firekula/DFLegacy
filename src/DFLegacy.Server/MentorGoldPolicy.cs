namespace DFLegacy.Server;

/// <summary>Monitor member capacity and float-store arithmetic for each master-gold batch.</summary>
internal static class MentorGoldPolicy
{
    internal static int Capacity(int masterLevel) => masterLevel switch
    {
        >= 10 and <= 19 => 5,
        >= 20 and <= 29 => 8,
        >= 30 and <= 99 => 10,
        _ => 0
    };

    internal static float Rate(byte relationshipLevel, int connectedChildren, int capacity)
    {
        if (relationshipLevel is < 1 or > 10) throw new ArgumentOutOfRangeException(nameof(relationshipLevel));
        if (connectedChildren < 0) throw new ArgumentOutOfRangeException(nameof(connectedChildren));
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));

        // Monitor 08082E30..08082E62 stores each component to binary32 before
        // adding them. The constants at 08131150/08131158 are binary64.
        var relationship = (float)(relationshipLevel * 0.01);
        var connected = (float)(((double)connectedChildren / capacity) * 0.05);
        return relationship + connected;
    }

    internal static uint Award(uint basis, float rate)
    {
        if (!float.IsFinite(rate) || rate < 0) throw new ArgumentOutOfRangeException(nameof(rate));
        if (basis == 0 || rate == 0) return 0;

        // A uint times a binary32 significand fits in 56 bits. Keeping that
        // product integral preserves the original truncation even where a
        // binary64 intermediate would round a near-integer product upward.
        var bits = BitConverter.SingleToUInt32Bits(rate);
        var exponent = (int)((bits >> 23) & 0xFF);
        var significand = bits & 0x7FFFFF;
        var shift = -149;
        if (exponent != 0)
        {
            significand |= 0x800000;
            shift = exponent - 150;
        }
        var product = (ulong)basis * significand;
        if (shift <= -64) return 0;
        if (shift < 0) return checked((uint)(product >> -shift));
        if (shift >= 32 || product > ((ulong)uint.MaxValue >> shift)) throw new OverflowException();
        return checked((uint)(product << shift));
    }
}
