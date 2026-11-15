using DFLegacy.Protocol;
using DFLegacy.Server;

internal static class ChapterTwoTowerPartySmokeTests
{
    internal static void Run(Action<bool, string> check, DungeonCatalog catalog, ItemCatalog items)
    {
        if (!catalog.TryGetTower(DungeonCatalog.TowerOfIllusionId, out var tower)
            || !catalog.TryCreateTowerFloor(tower.DungeonId, 1, out var floor))
        {
            check(false, "tower party fixture requires the fixed first floor");
            return;
        }

        var leader = new CharacterRecord(Guid.NewGuid(), "TowerLeader", 0, 0, 50, 0, 0);
        var follower = new CharacterRecord(Guid.NewGuid(), "TowerFollower", 0, 0, 50, 0, 0);
        const ushort party = 32;
        var a = new PartySlotHandle(2, leader.Id, Guid.NewGuid(), 101, leader.Name,
            _ => ValueTask.CompletedTask, party);
        var b = new PartySlotHandle(0, follower.Id, Guid.NewGuid(), 102, follower.Name,
            _ => ValueTask.CompletedTask, party);
        var instance = new DungeonInstance(party, Guid.Empty, catalog, items);
        var run = new ChapterTwoTowerPartyRun(instance, tower, floor, leader.Id, [a, b], () => true);
        check(run.Apply(leader.Id, a.SessionId, new("a", leader)) is null && !run.AdmissionFor(leader.Id).IsReady,
            "tower admission cannot commit after only one isolated character votes");
        var applicants = run.Apply(follower.Id, b.SessionId, new("b", follower));
        check(applicants?.Length == 2 && run.IsAdmitting
            && run.Apply(follower.Id, b.SessionId, new("b", follower)) is null,
            "simultaneous tower applicants elect a single ticket commit");
        run.CompleteAdmission(new Dictionary<Guid, TowerPartyTicketResult>
        {
            [leader.Id] = new(leader, []),
            [follower.Id] = new(follower, [])
        });
        run.Join(leader.Id);
        run.Join(follower.Id);
        var first = run.TakePackets(leader.Id).Single();
        check(first.ProtocolId == GameProtocolEngine.ChapterTwoTowerFloorNotification
            && first.Payload.SequenceEqual(run.TakePackets(follower.Id).Single().Payload),
            "tower participants get a single shared floor, with identical seed and actor data");

        run.MarkLoaded(leader.Id, 1);
        check(run.TakePackets(leader.Id).Length == 0 && !run.ProcessStage(leader.Id, 1, leader.Id, 1, 100),
            "the leader cannot release loading or begin combat before the follower finishes");
        run.MarkLoaded(follower.Id, 1);
        check(run.TakePackets(leader.Id).Single().ProtocolId == GameProtocolEngine.FinishLoadingNotification
            && run.TakePackets(follower.Id).Single().ProtocolId == GameProtocolEngine.FinishLoadingNotification
            && !run.ProcessStage(follower.Id, 1, leader.Id, 1, 101)
            && run.ProcessStage(leader.Id, 1, leader.Id, 1, 102),
            "only the native leader begins the loaded tower stage");
        check(run.ReportDeath(follower.Id, 1, 120) && run.BeginReward(follower.Id, 6000) is null,
            "one death cannot start party settlement while another member remains alive");
        run.Depart(leader.Id, 6000);
        check(run.BeginReward(follower.Id, 10999) is null && run.BeginReward(follower.Id, 11000) is { Floor: 1 }
            && run.BeginReward(follower.Id, 12000) is null,
            "leaving as the last survivor triggers a single five-second defeat and one reward attempt");
        run.CompleteReward(follower.Id, new TowerPartyReward(123, [new(0, 52)]));
        run.PublishRewards(canRetry: false);
        var packets = run.TakePackets(follower.Id);
        var reward = packets.Single(p => p.ProtocolId == GameProtocolEngine.ChapterTwoTowerRewardNotification);
        check(reward.Payload is [123, 0, 0, 0, 1, 0, 0, 52, 0, 0, 0, 0, 0, 0]
            && packets.Single(p => p.ProtocolId == GameProtocolEngine.ChapterTwoTowerSettlementOptionsNotification).Payload is [0],
            "party rewards keep the real frozen slot and expose the shared retry gate");
    }
}
