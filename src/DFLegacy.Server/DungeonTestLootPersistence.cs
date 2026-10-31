namespace DFLegacy.Server;

internal static class DungeonTestLootPersistence
{
    // Keeps the existing order explicit: accepted store write, adopt the returned state,
    // then publish evidence. Failed and null saves never call either observer.
    internal static async Task<CharacterRecord?> SaveAndInstallAsync(Func<Task<CharacterRecord?>> persist,
        Action<CharacterRecord> install, Action<CharacterRecord>? committed)
    {
        var saved = await persist();
        if (saved is null) return null;
        install(saved);
        committed?.Invoke(saved);
        return saved;
    }
}

internal static class DungeonTestLootJournal
{
    private static readonly object Gate = new();
    private static readonly Queue<DungeonTestLootResult> Results = [];
    internal static void Add(DungeonTestLootResult result)
    {
        lock (Gate)
        {
            Results.Enqueue(result);
            while (Results.Count > 512) Results.Dequeue();
        }
    }
    internal static void MarkPublicationFailure(Guid pickupId, string reason)
    {
        lock (Gate)
        {
            var updated = Results.Select(value => value.Context.PickupId == pickupId && value.Status == "committed"
                ? value with { PublicationError = reason } : value).ToArray();
            Results.Clear();
            foreach (var result in updated) Results.Enqueue(result);
        }
    }
    internal static DungeonTestLootResult[] Capture() { lock (Gate) return Results.ToArray(); }
}
