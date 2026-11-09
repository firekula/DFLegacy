using System.Buffers.Binary;
using System.Text.Json;
using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;

internal static class PvpTeamRelaySmokeTests
{
    internal static async Task RunAsync(Action<bool,string> check)
    {
        await Settlement(check);
        await Rooms(check);
        await Relay(check);
        await EightPlayersAndDepartures(check);
        await TeamTimeout(check);
        await IndividualRanking(check);
    }

    private static async Task Settlement(Action<bool,string> check)
    {
        await using var f = await PvpIndividualSmokeTests.Fixture.Create();
        PvpSettlementRequest Request(Guid id, byte mode, byte? winningTeam, bool draw = false, int? forfeit = null)
        {
            var data = new { MatchId=id, Members=f.Characters.Select((c,i)=>new {
                CharacterId=c.Id,StartGrade=(byte)0,Forfeit=forfeit==i,
                Stats=forfeit==i ? null : new ushort[11], Scores=forfeit==i ? null : new uint[]{(uint)(100-i),0,0,0,0,0,0,0},
                Team=(byte)(i%2+1),Seat=(byte)i }).ToArray(),Winner=(Guid?)null,Draw=draw,Mode=mode,WinningTeam=winningTeam };
            return JsonSerializer.Deserialize<PvpSettlementRequest>(JsonSerializer.Serialize(data))!;
        }
        var request = Request(Guid.NewGuid(),1,1);
        var result = await f.Store.CommitPvpMatchAsync(request,f.Rating);
        check(result.Status == DungeonRewardLedgerStatus.Committed && result.Awards.Length == 4,
            "2v2 settlement atomically awards all four frozen participants");
        if (result.Status != DungeonRewardLedgerStatus.Committed) return;
        var chars=await Task.WhenAll(Enumerable.Range(0,4).Select(f.Reload));
        check(chars[0].VictoryPoints==4 && chars[2].VictoryPoints==2 && chars[1].VictoryPoints==1 && chars[3].VictoryPoints==1,
            "team MVP gets teamSize+2 VP; teammate teamSize; losing team one VP");
        check(chars[0].PvpWins==1 && chars[2].PvpWins==1 && chars[1].PvpLosses==1 && chars.All(c=>c.PvpPlayCount==1),
            "team result counts every teammate rather than the final surviving seat");
        check(chars[0].PvpPoints==1000 && chars[2].PvpPoints==1000 && chars[1].PvpPoints==0,
            "equal novice teams gain1000 and preserve novice loss protection");
        var retry=await f.Store.CommitPvpMatchAsync(request,f.Rating);
        check(retry.Status==DungeonRewardLedgerStatus.Committed && (await f.Reload(0)).VictoryPoints==4,
            "multiplayer receipt retries never duplicate rewards");
        var loaded=new JsonGameStore(f.Harness.Options,NullLogger<JsonGameStore>.Instance);await loaded.InitializeAsync();
        check((await loaded.CommitPvpMatchAsync(request,f.Rating)).Awards.SequenceEqual(result.Awards),
            "team receipt survives JSON reload with identical canonical awards");
        var conflict=request with { Draw=true };
        check((await f.Store.CommitPvpMatchAsync(conflict,f.Rating)).Status==DungeonRewardLedgerStatus.Rejected,
            "changed outcome cannot reuse a multiplayer match ID");
        var relay=await f.Store.CommitPvpMatchAsync(Request(Guid.NewGuid(),2,2,forfeit:0),f.Rating);
        check(relay.Status==DungeonRewardLedgerStatus.Committed && (await f.Reload(1)).PvpWins==1
            && (await f.Reload(3)).PvpWins==1 && (await f.Reload(0)).VictoryPoints==4,
            "relay team victory awards both teammates and no VP to forfeiter");
        var before=(await f.Reload(2)).PvpPoints;
        var draw=await f.Store.CommitPvpMatchAsync(Request(Guid.NewGuid(),1,null,draw:true),f.Rating);
        check(draw.Status==DungeonRewardLedgerStatus.Committed && (await f.Reload(2)).PvpPoints==before,
            "team draw changes no rating");
        var invalid=Request(Guid.NewGuid(),1,1) with { Members=request.Members.Take(3).ToArray() };
        check((await f.Store.CommitPvpMatchAsync(invalid,f.Rating)).Status==DungeonRewardLedgerStatus.Rejected,
            "durable team snapshot rejects unequal start teams");
    }
    private static async Task MakeRoom(PvpIndividualSmokeTests.Fixture f, byte mode)
    {
        await f.Command(0,53,[1,3,0,0]); var room=f.RoomId(0);
        for(int i=1;i<4;i++) await f.Join(i,room);
        await f.Command(0,57,[mode]); f.Clear();
    }
    private static async Task Ready(PvpIndividualSmokeTests.Fixture f)
    { for(int i=1;i<4;i++) await f.Command(i,56,[1]); await f.Command(0,56,[1]); }
    private static byte[] U16(ushort n)=>[(byte)n,(byte)(n>>8)];
    private static byte[] Rank(uint n)
    { var b=new byte[54]; BinaryPrimitives.WriteUInt16LittleEndian(b,1); BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(22),n);return b; }

    private static async Task Rooms(Action<bool,string> check)
    {
        await using var f=await PvpIndividualSmokeTests.Fixture.Create();
        await MakeRoom(f,1);
        check(Enumerable.Range(0,4).All(i=>f.Service.IsBusy(f.Characters[i].Id)),"four players can enter one eight-seat room");
        await f.Command(1,57,[2]);
        check(!f.Packets[0].Any(p=>p.ProtocolId==42 && p.Payload[^1]==2),"guest cannot change room mode");
        await f.Command(2,55,[2,2]); await Ready(f);
        check(!f.Packets[0].Any(p=>p.ProtocolId==45),"three-versus-one cannot start");
        await f.Command(2,55,[2,1]); f.Clear(); await f.Command(0,56,[1]);
        check(!f.Packets[0].Any(p=>p.ProtocolId==45),"changing team invalidates old readiness");
        await Ready(f);
        var start=f.Packets[0].LastOrDefault(p=>p.ProtocolId==45);
        check(start is not null && start.Payload.SequenceEqual(new byte[]{3,1}),"balanced ready2v2 starts native team mode");
        if(start is null) return;
        check(f.Udp.PvpRelayTargets(f.Sessions[0].Id).Count==3,"four-player UDP match sees all and only room peers");
        f.Clear(); await f.Command(1,58,U16(1));
        check(f.Packets[0].Count(p=>p.ProtocolId==46)==1 && !f.Packets[0].Any(p=>p.ProtocolId==49),"one eliminated opponent does not end team battle");
        await f.Command(3,58,U16(3));
        check(f.Packets[0].Count(p=>p.ProtocolId==49)==1,"last opposing teammate death ends team battle once");
        for(int i=0;i<4;i++) await f.Command(i,61,Rank((uint)(100-i)));
        check(f.Packets.All(ps=>ps.Any(p=>p.ProtocolId==47 && p.Payload[0]==0)),"all clients receive a representative winning red seat");
        check((await f.Reload(2)).PvpWins==1,"team result includes surviving teammate");
        for(int i=0;i<4;i++) await f.Command(i,60,[]);
        f.Clear();await f.Command(0,57,[2]);
        check(f.Packets[0].Any(p=>p.ProtocolId==42 && p.Payload[^1]==2),"result acknowledgements restore mutable waiting room");
    }

    private static (byte Red,byte Blue) Turn(GameServerPacket p)
    {
        var b=p.Payload;int cursor=1;var red=b[cursor++];cursor+=1+b[cursor]*3;
        return(red,b[cursor]);
    }
    private static async Task Relay(Action<bool,string> check)
    {
        await using var f=await PvpIndividualSmokeTests.Fixture.Create();await MakeRoom(f,2);await Ready(f);
        if(!f.Packets[0].Any(p=>p.ProtocolId==45)) { check(false,"relay2v2 starts");return; }
        f.Clear();await f.Command(2,122,[]);
        check(f.Packets[0].Any(p=>p.ProtocolId==125 && p.Payload.SequenceEqual(new byte[]{2})),"native request-to-fight toggle is broadcast");
        f.Clock.Advance(7);await f.Service.SweepAsync();
        var turn=f.Packets[0].LastOrDefault(p=>p.ProtocolId==124);
        check(turn is not null,"relay publishes ACT1 turn124 after native loading delay");if(turn is null)return;
        var active=Turn(turn);check(active.Red==2 && active.Blue is 1 or 3,"request priority selects waiting red fighter and one blue fighter");
        var blue=active.Blue;int bench=blue==1?3:1;
        f.Clear();await f.Command(bench,58,U16(3));await f.Command(2,58,U16(1));
        check(!f.Packets[0].Any(p=>p.ProtocolId==46),"waiting fighter and same-team killer cannot eliminate a relay participant");
        await f.Command(blue,58,U16(3));
        check(f.Packets[0].Any(p=>p.ProtocolId==46) && !f.Packets[0].Any(p=>p.ProtocolId==49),"relay first kill retains losing team's reserve");
        f.Clock.Advance(3);await f.Service.SweepAsync();
        var next=Turn(f.Packets[0].Last(p=>p.ProtocolId==124));
        check(next.Red==255 && next.Blue==bench,"relay retains winning fighter and rotates only eliminated side");
        await f.Command(bench,58,U16(3));
        for(int i=0;i<4;i++)await f.Command(i,61,Rank((uint)(100-i)));
        var result=f.Packets[0].LastOrDefault(p=>p.ProtocolId==47);
        check(result is not null && result.Payload[0]==0 && BinaryPrimitives.ReadUInt16LittleEndian(result.Payload.AsSpan(result.Payload.Length-7))==3,
            "relay result carries winning representative seat and actual all-kill fighter UID");
        check((await f.Reload(0)).PvpWins==1 && (await f.Reload(2)).PvpWins==1,"relay bench teammate receives whole-team victory");
    }

    private static async Task EightPlayersAndDepartures(Action<bool,string> check)
    {
        await using var f=await PvpIndividualSmokeTests.Fixture.Create(count:8);
        await f.Command(0,53,[1,3,0,0]);var room=f.RoomId(0);
        for(int i=1;i<8;i++)await f.Join(i,room);
        await f.Command(0,57,[1]);f.Clear();await f.Command(0,62,[4,0]);
        check(f.Packets[7].Any(p=>p.ProtocolId==42 && BinaryPrimitives.ReadUInt16LittleEndian(p.Payload.AsSpan(4))==4),
            "eight-player room can explicitly select native6..8-player map");
        for(int i=1;i<8;i++)await f.Command(i,56,[1]);await f.Command(0,56,[1]);
        check(f.Packets[7].Any(p=>p.ProtocolId==45 && p.Payload.SequenceEqual(new byte[]{4,1})),"full4v4 supports eighth native seat and all ready flags");
        check(f.Udp.PvpRelayTargets(f.Sessions[7].Id).Count==7,"eight-player UDP context exposes seven room peers");
        if(!f.Packets[7].Any(p=>p.ProtocolId==45))return;
        await f.Command(0,58,U16(2)); // dead teammate remains entitled to a subsequent team victory
        foreach(int i in new[]{1,3,5,7})await f.Command(i,58,U16(3));
        for(int i=0;i<8;i++)await f.Command(i,61,Rank((uint)(100-i)));
        check((await f.Reload(0)).PvpWins==1 && (await f.Reload(0)).VictoryPoints==6
            && (await f.Reload(2)).VictoryPoints==4,"dead4v4 teammate wins; top scorer receives6VP and teammate4VP");
        for(int i=0;i<8;i++)await f.Command(i,60,[]);
        // Remove a low seat: subsequent participants are sparse and cannot be indexed by list position.
        await f.Command(1,55,[1,254]);await f.Command(0,55,[0,254]);f.Clear();
        await f.Command(2,57,[2]);for(int i=3;i<8;i++)await f.Command(i,56,[1]);await f.Command(2,56,[1]);
        check(f.Packets[7].Any(p=>p.ProtocolId==45 && p.Payload[^1]==2),"sparse seats2..7 start balanced relay after host migration");
        if(!f.Packets[7].Any(p=>p.ProtocolId==45))return;
        foreach(int i in new[]{3,5,7})await f.Service.DetachAsync(f.Characters[i].Id,f.Attachments[i]);
        foreach(int i in new[]{2,4,6})await f.Command(i,61,Rank((uint)(100-i)));
        check((await f.Reload(4)).PvpWins==2 && !f.Service.IsBusy(f.Characters[7].Id),
            "entire opposing-team disconnect resolves once and releases all pending ownership");
    }

    private static async Task TeamTimeout(Action<bool,string> check)
    {
        await using var f=await PvpIndividualSmokeTests.Fixture.Create();await MakeRoom(f,1);await Ready(f);f.Clear();
        f.Clock.Advance(180);var hp=new byte[32];
        BinaryPrimitives.WriteUInt32LittleEndian(hp.AsSpan(0),10);BinaryPrimitives.WriteUInt32LittleEndian(hp.AsSpan(4),15);
        BinaryPrimitives.WriteUInt32LittleEndian(hp.AsSpan(8),30);BinaryPrimitives.WriteUInt32LittleEndian(hp.AsSpan(12),15);
        await f.Command(0,59,hp);
        for(int i=0;i<4;i++)await f.Command(i,61,Rank(1));
        check((await f.Reload(1)).PvpWins==1 && (await f.Reload(0)).PvpLosses==1,
            "team timeout compares whole-team damage, not the lowest individual damage");
    }

    private static async Task IndividualRanking(Action<bool,string> check)
    {
        foreach(var count in new[]{4,8})
        {
            await using var f=await PvpIndividualSmokeTests.Fixture.Create(count:count);
            var winner=count-2;
            var request=new PvpSettlementRequest(Guid.NewGuid(),f.Characters.Select((c,i)=>new PvpSettlementMember(
                c.Id,0,false,new ushort[11],[(uint)(100-i),0,0,0,0,0,0,0],Seat:(byte)i)).ToArray(),f.Characters[winner].Id,false);
            var result=await f.Store.CommitPvpMatchAsync(request,f.Rating);
            uint[] expected=count==4 ? [1,2,5,0] : [1,4,2,1,0,0,9,0];
            check(result.Status==DungeonRewardLedgerStatus.Committed && result.Awards.Select(a=>a.VictoryPoints).SequenceEqual(expected),
                $"{count}-player individual victory points follow score-rank divisors with winner outside rank0");
        }
    }

}
