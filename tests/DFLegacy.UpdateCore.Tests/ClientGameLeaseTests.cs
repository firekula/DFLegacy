namespace DFLegacy.UpdateCore.Tests;

internal static class ClientGameLeaseTests
{
    internal static void Run(string parent)
    {
        var root=Path.Combine(parent,"game-lease");
        using(var first=ClientGameLease.AcquireShared(root))
        using(var second=ClientGameLease.AcquireShared(root))
        {
            TestRun.Check(true,"multiple game sessions share one installation");
            try { using var denied=ClientGameLease.EnterMaintenance(root);throw new Exception("maintenance allowed while game active"); }
            catch(IOException){TestRun.Check(true,"active game blocks install");}
            using var other=ClientGameLease.EnterMaintenance(Path.Combine(parent,"unrelated-game"));
            TestRun.Check(true,"unrelated installations remain independent");
        }
        using(var maintenance=ClientGameLease.EnterMaintenance(root))
        {
            try { using var denied=ClientGameLease.AcquireShared(root);throw new Exception("game allowed during maintenance"); }
            catch(IOException){TestRun.Check(true,"install prevents concurrent game start");}
        }
        using var resumed=ClientGameLease.AcquireShared(root);
        TestRun.Check(true,"game lease recovers after maintenance disposal");
    }
}
