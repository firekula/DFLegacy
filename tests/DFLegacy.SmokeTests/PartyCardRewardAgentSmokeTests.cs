using DFLegacy.Server;

internal static class PartyCardRewardAgentSmokeTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var instance = new DungeonInstance(1, Guid.Empty, null!, null!);
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        instance.AddMember(new(a, 1, 0, "A", _ => ValueTask.CompletedTask));
        instance.AddMember(new(b, 2, 1, "B", _ => ValueTask.CompletedTask));
        var agentA = new Agent(0, DungeonRewardLedgerStatus.Committed);
        var agentB = new Agent(1, DungeonRewardLedgerStatus.Unknown);
        instance.RegisterPartyCardRewardAgent(a, agentA);
        instance.RegisterPartyCardRewardAgent(b, agentB);
        var generation = instance.BeginPartyCardSettlement();
        instance.CompletePartyCardSelection(generation);
        await instance.SettleOwnPartyCardAsync(a, PartyCardType.Free, 0, generation, CancellationToken.None);
        await instance.SettleOwnPartyCardAsync(b, PartyCardType.Free, 1, generation, CancellationToken.None);
        await instance.SettleOwnPartyCardAsync(b, PartyCardType.Free, 1, generation, CancellationToken.None);
        check(agentA.Commits == 1 && agentB.Commits == 1, "agents commit own column once including Unknown");
        check(instance.TryTakePartyCardPublication(generation, out var snapshot) && snapshot!.Selection.FreeOwners[0] == 0 && snapshot.Selection.FreeOwners[1] == 255, "agent Unknown masks its column only");
        instance.RemoveMember(a);
        await instance.SettleOwnPartyCardAsync(a, PartyCardType.Free, 0, generation, CancellationToken.None);
        check(agentA.Commits == 1, "unbound member agent is never called");
    }

    private sealed class Agent(byte slot, DungeonRewardLedgerStatus status) : IPartyCardRewardAgent
    {
        public int Commits { get; private set; }
        public PartyCardContent RollOwnColumn(Guid settlementId, PartyCardType type, byte column, long generation) => new(type, column, slot, new(10, true, 20, 30, null, 40), PartyCardCommitStatus.Pending);
        public Task<PartyCardCommitOutcome> CommitOwnColumnAsync(Guid settlementId, PartyCardType type, byte column, long generation, PartyCardContent content, CancellationToken ct)
        {
            Commits++;
            return Task.FromResult(new PartyCardCommitOutcome(status, null));
        }
    }
}
