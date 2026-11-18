using DFLegacy.Server;

internal static class NpcShopOffersSmokeTests
{
    public static void Run(Action<bool, string> check)
    {
        ArgumentNullException.ThrowIfNull(check);
        check(NpcShopOffers.InitialReinforcement(NpcShopOffers.ReinforcedKatanaItemId)
                  == NpcShopOffers.ReinforcedKatanaLevel
              && NpcShopOffers.InitialReinforcement(1) == 0,
            "only the configured NPC shop offer starts reinforced");

        var template = new CharacterMailAttachmentRecord(
            NpcShopOffers.ReinforcedKatanaItemId,
            1,
            Durability: 35);
        var applied = NpcShopOffers.ApplyReinforcement(
            NpcShopOffers.ReinforcedKatanaItemId,
            template);
        check(applied.State == NpcShopOffers.ReinforcedKatanaLevel
                && applied.ItemId == template.ItemId
                && applied.CountOrValue == template.CountOrValue
                && applied.Durability == template.Durability,
            "the shop purchase record carries +10 state and keeps the template fields");
        check((applied.State & CharacterItemSealing.ReinforcementMask)
                == NpcShopOffers.ReinforcedKatanaLevel,
            "the applied level fits the reinforcement state mask");

        var other = NpcShopOffers.ApplyReinforcement(
            1,
            template with { ItemId = 1 });
        check(other.State == 0 && other.ItemId == 1,
            "an item without a shop offer keeps the default state");
    }
}
