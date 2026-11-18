using DFLegacy.Server;

internal static class WeightAccountingSmokeTests
{
    // The client weighs non-stackable equipment as one piece per slot. A migrated
    // or damaged record with a larger CountOrValue must not inflate the server's
    // weight, which otherwise turns valid jar/pickup operations into "overweight".
    internal static void Run(Action<bool, string> check)
    {
        using var fixture = new ActiveDropItemFixture();
        var catalog = fixture.Catalog;

        var inflated = CharacterInventoryPlanner.CalculateWeight(
            [new CharacterItemRecord(9, 50010, 50_000)], catalog);
        check(inflated == 1,
            "a non-stackable slot weighs one piece regardless of a stale CountOrValue");

        var stack = CharacterInventoryPlanner.CalculateWeight(
            [new CharacterItemRecord(73, 3176, 5)], catalog);
        check(stack == 5, "a stackable slot still multiplies its unit weight by its count");

        var allowance = CharacterInventoryPlanner.CalculateMainInventoryWeightAllowance(
            1_000, [], [], catalog);
        check(allowance == 100,
            "the main allowance is the wire inventory limit divided by its x10 scale");

        check(CharacterInventoryPlanner.TryPlace(
                new Dictionary<ushort, CharacterItemRecord> { [73] = new(73, 3176, 99) },
                new(3176, 1), catalog, allowance,
                out _, out _),
            "a one-unit reward fits the remaining main allowance exactly");
        check(!CharacterInventoryPlanner.TryPlace(
                new Dictionary<ushort, CharacterItemRecord> { [73] = new(73, 3176, 99) },
                new(3176, 2), catalog, allowance,
                out _, out var failure) && failure == InventoryPlacementFailure.Overweight,
            "a reward is rejected only when the calculated main weight really exceeds the allowance");
    }
}
