using DFLegacy.Protocol;

namespace DFLegacy.Server;

public sealed record RandomBoxOpeningResult(CharacterRecord? Character, GameServerPacket Reply);

public sealed class RandomBoxOpeningSession(ItemCatalog catalog)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly HashSet<(Guid Character, ushort Sequence)> committed = [];
    private readonly Queue<(Guid Character, ushort Sequence)> order = [];

    public async Task<RandomBoxOpeningResult> OpenAsync(JsonGameStore store, string account,
        CharacterRecord expected, GameRandomBoxCommand command, uint weightLimit,
        IDropRandomSource random, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var key = (expected.Id, command.Sequence);
            if (committed.Contains(key)) return Failure(17);
            if (!RandomBoxOpeningPlanner.TryCreate((expected.Inventory ?? []).ToDictionary(item => item.Slot),
                    command.BoxSlot, command.HammerSlot, expected.Level, catalog, weightLimit, random,
                    out var plan, out var error)) return Failure(error);
            CharacterRecord? saved;
            try
            {
                saved = await store.SaveCharacterItemSpacesAsync(account, expected.Id, expected.Gold,
                    plan.Inventory.Values, expected.Equipment ?? [], expected.Warehouse ?? [], cancellationToken,
                    expectedCharacter: expected);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return Failure(17);
            }
            if (saved is null) return Failure(17);
            // Match the existing bounded compound replay window. C2's
            // transport assigns a new u16 sequence to each outgoing command.
            if (order.Count == 256) committed.Remove(order.Dequeue());
            committed.Add(key);
            order.Enqueue(key);
            return new(saved, GameProtocolEngine.CreateRandomBoxReply(command.BoxSlot, command.HammerSlot, plan.Rewards));
        }
        finally { gate.Release(); }
    }

    private static RandomBoxOpeningResult Failure(byte error) => new(null, GameProtocolEngine.CreateRandomBoxError(error));
}
