namespace DFLegacy.Server;

/// <summary>
/// NPC shop offers whose purchased record differs from the item template.
///
/// The shop <em>listing</em> is client-side PVF data (<c>itemshop/*.shp</c>); the
/// server accepts a purchase from any item with a positive price. Reinforcement
/// is item state, not a template field, and the generic shop path creates items
/// with <c>State = 0</c>, so an offer that is meant to arrive reinforced needs a
/// server-side rule here.
///
/// Current offer: Sinda sells the worn katana (27601) already at +10. The
/// client reads the level from the low five state bits and computes its own
/// stats from them; the server only has to hand over the right record.
/// </summary>
public static class NpcShopOffers
{
    public const ushort ReinforcedKatanaItemId = 27601;
    public const byte ReinforcedKatanaLevel = 10;

    private static readonly IReadOnlyDictionary<ushort, byte> Reinforced =
        new Dictionary<ushort, byte>
        {
            [ReinforcedKatanaItemId] = ReinforcedKatanaLevel
        };

    public static byte InitialReinforcement(ushort itemId) =>
        Reinforced.TryGetValue(itemId, out var level)
            ? Math.Min(level, CharacterItemSealing.ReinforcementMask)
            : (byte)0;

    /// <summary>Apply the offer's reinforcement to a purchase record before placement.</summary>
    public static CharacterMailAttachmentRecord ApplyReinforcement(
        ushort itemId,
        CharacterMailAttachmentRecord record)
    {
        var level = InitialReinforcement(itemId);
        return level == 0 ? record : record with { State = level };
    }
}
