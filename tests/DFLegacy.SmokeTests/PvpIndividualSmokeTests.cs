using System.Buffers.Binary;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;

internal static class PvpIndividualSmokeTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        Configuration(check);
        await RoomsAndMatches(check);
        await TimeoutsAndForfeits(check);
        await Persistence(check);
        await Faults(check);
        await RelayIsolation(check);
        await TerminalWinnerLeaves(check);
        await TransportAndEntry(check);
        await CrossMatchReplay(check);
        await SequenceWrapAndQueuedPhase(check);
        await FreshEntryModels(check);
        await PvpPartyAdmissionSmokeTests.RunAsync(check);
    }

    private static void Configuration(Action<bool,string> check)
    {
        var o = new ServerOptions();
        var old = EntranceMetadata.BuildSingleLocalChannel(new(o.Channel.Name,"127.0.0.1",7001));
        check(!o.Pvp.Enabled && EntranceMetadata.BuildLocalChannels(PvpChannelConfiguration.Channels(o)).SequenceEqual(old), "disabled PvP preserves default entrance bytes");
        o.Pvp.Enabled = true; o.Pvp.Validate(o);
        var ports = EntrancePortFamilies.Split(8080,[7001,7004,2311],PvpChannelConfiguration.Ports(o));
        check(ports.ChannelPorts.SequenceEqual(new[]{7001,7004}) && ports.EntrancePorts.SequenceEqual(new[]{8080,2311}), "both gameplay TCP ports are excluded from handshake family");
        var metadata = EntranceMetadata.BuildLocalChannels(PvpChannelConfiguration.Channels(o));
        check(BinaryPrimitives.ReadInt32LittleEndian(metadata.AsSpan(24))==2 && metadata.Length==4+9*(24+2*48), "all nine native groups contain both channel descriptors");
        o.Pvp.Port=7001; check(Throws(()=>o.Pvp.Validate(o)),"PvP rejects duplicate TCP listener");
        o.Pvp.Port=7004; o.Pvp.ChannelNumber=52; check(Throws(()=>o.Pvp.Validate(o)),"unverified PVF channel category rejected");
    }

    private static async Task RoomsAndMatches(Action<bool,string> check)
    {
        await using var f=await Fixture.Create();
        await f.Command(0,53,[1,3,0,0]);
        var room=f.RoomId(0);
        check(room>0 && f.Service.IsBusy(f.Characters[0].Id),"create reserves commercial ownership and exposes actual native room");
        f.Clear(); await f.Join(1,room);
        var packets=f.Packets[1];
        var membership=packets.FindIndex(p=>p.ProtocolId==43);
        check(membership>0 && packets.Take(membership).Count(p=>p.ProtocolId==2)==4 && packets.Take(membership).Any(p=>p.ProtocolId==11),"both identity/model pairs and actual peer table precede own membership");
        check(packets.Last(p=>p.ProtocolId==54).Payload.SequenceEqual(new byte[]{1,0,0,0,0,0,0,0,0}),"native join reply has exactly eight ready bytes");
        f.Clear(); await f.Command(1,55,[0,254]);
        check(f.Service.IsBusy(f.Characters[0].Id) && !f.Packets[0].Any(p=>p.ProtocolId==43),"nonhost cannot remove another occupied seat");
        await f.Command(0,56,[1]);
        check(!f.Packets[0].Any(p=>p.ProtocolId==45),"host cannot start before other participant readiness");
        await f.Command(1,56,[1]); await f.Command(0,56,[1]);
        check(f.Packets[0].Single(p=>p.ProtocolId==45).Payload.SequenceEqual(new byte[]{3,0}),"ready actual peers start individual native map3");
        check(f.Udp.PvpRelayTargets(f.Sessions[0].Id).Count==1,"live room permits exactly its peer endpoint");
        f.Udp.SetRelayParty(f.Sessions[0].Id,999); f.Udp.SetRelayDungeonInstance(f.Sessions[0].Id,new object());
        check(f.Udp.PvpRelayTargets(f.Sessions[0].Id).Count==1,"late party and dungeon callbacks cannot erase live PvP owner");
        f.Clear(); await f.Command(0,59,new byte[32]);
        check(!f.Packets[0].Any(p=>p.ProtocolId==49),"early host timeout cannot end a fresh match");
        await f.Command(1,58,U16(1)); await f.Command(1,58,U16(1));
        check(f.Packets[0].Count(p=>p.ProtocolId==46)==1 && f.Packets[0].Count(p=>p.ProtocolId==49)==1,"one real victim death chooses a terminal result and requests reports once");
        await f.Command(0,61,Rank(1,9)); await f.Command(1,61,Rank(2,20));
        var a=await f.Reload(0);var b=await f.Reload(1);
        check(a.VictoryPoints==4 && a.PvpPoints==1000 && a.PvpWins==1 && b.VictoryPoints==1 && b.PvpLosses==1,"canonical two-sided settlement follows winner despite higher loser score");
        var result=f.Packets[0].Last(p=>p.ProtocolId==47);
        check(result.Payload[0]==0 && BinaryPrimitives.ReadUInt32LittleEndian(result.Payload.AsSpan(1))==4 && BinaryPrimitives.ReadUInt16LittleEndian(result.Payload.AsSpan(11))==2,"result keeps winning seat separate from score-ranked first entry");
        check(f.Packets[0].FindLastIndex(p=>p.ProtocolId==48)<f.Packets[0].FindLastIndex(p=>p.ProtocolId==47),"canonical cumulative record precedes result");
        await f.Command(0,61,Rank(1,9)); await f.Command(1,58,U16(1));
        check((await f.Reload(0)).VictoryPoints==4,"late duplicate reports/death never grant again");
        await f.Command(0,60,[]);await f.Command(1,60,[]);
        f.Clear();await f.Command(0,56,[1]);
        check(!f.Packets[0].Any(p=>p.ProtocolId==45),"result ACK clears all ready state before second match");
        await f.Command(1,56,[1]);await f.Command(0,56,[1]);
        check(f.Packets[0].Count(p=>p.ProtocolId==45)==1,"same room can start a second real match");
        await f.Command(0,58,U16(2));await f.Command(0,61,Rank(1,5));await f.Command(1,61,Rank(1,8));
        await f.Command(0,60,[]);await f.Command(1,60,[]);
        check(f.Packets[1].Last(p=>p.ProtocolId==42).Payload[3]==1,"host migrates to still-present second-match winner");
        var old=f.Attachments[1];await f.Attach(1);await f.Service.DetachAsync(f.Characters[1].Id,old);
        await f.Command(1,53,[1,3,0,0]);
        check(f.Service.IsBusy(f.Characters[1].Id),"stale attachment cleanup cannot remove a replacement's new room");
    }

    private static async Task TimeoutsAndForfeits(Action<bool,string> check)
    {
        await using(var f=await Fixture.Create())
        {
            await f.Start();f.Clock.Advance(180);await f.Command(1,59,new byte[32]);
            check(!f.Packets[0].Any(p=>p.ProtocolId==49),"nonhost cannot assert timeout");
            await f.Command(0,59,new byte[32]);await f.Command(0,61,Rank(0,0));await f.Command(1,61,Rank(0,0));
            check((await f.Reload(0)).VictoryPoints==1 && (await f.Reload(1)).VictoryPoints==1 && (await f.Reload(0)).PvpLosses==1 && f.Packets[0].Last(p=>p.ProtocolId==47).Payload[^1]==1,"fresh all-eight-equal timeout is sourced loss/loss +1VP draw");
            f.Clock.Advance(20);await f.Service.SweepAsync();
            check(f.Packets[0].Last(p=>p.ProtocolId==42).Payload[2]==1,"zero ACK watchdog returns waiting without repeated award");
        }
        await using(var f=await Fixture.Create())
        {
            await f.Start();await f.Command(1,55,[1,254]);
            check(f.Service.IsBusy(f.Characters[1].Id),"departed participant stays economically reserved until atomic result");
            var blocked=await f.Store.SaveCharacterCeraShopPurchaseAsync("bobby",f.Characters[1].Id,1,1,[],[]);
            check(blocked is null,"pending departed participant cannot mutate a currency snapshot");
            await f.Command(0,61,Rank(1,10));
            check((await f.Reload(0)).VictoryPoints==4 && (await f.Reload(1)).VictoryPoints==0 && (await f.Reload(1)).PvpLosses==1 && !f.Service.IsBusy(f.Characters[1].Id),"forfeit retains original two-player winner VP and releases departed reservation only after commit");
            await f.Command(0,55,[0,254]);
            check((await f.Reload(0)).PvpLosses==0,"post-commit leave cannot apply a second penalty");
        }
        await using(var f=await Fixture.Create())
        {
            await f.Start();await f.Command(0,58,U16(2));f.Clock.Advance(20);await f.Service.SweepAsync();
            check((await f.Reload(0)).PvpLosses==1 && (await f.Reload(1)).PvpLosses==1 && (await f.Reload(1)).VictoryPoints==0 && !f.Service.IsBusy(f.Characters[0].Id),"zero-rank watchdog atomically penalizes both missing reports even after room deletion");
        }
        await using(var f=await Fixture.Create())
        {
            await f.Start();f.Clock.Advance(240);await f.Service.SweepAsync();
            check((await f.Reload(0)).PvpLosses==1 && (await f.Reload(1)).PvpLosses==1 && !f.Packets[0].Any(p=>p.ProtocolId==47),"overall watchdog forfeits lost combat without synthetic HP or winner results");
        }
    }

    private static async Task TerminalWinnerLeaves(Action<bool,string> check)
    {
        await using var f=await Fixture.Create();
        var root=JsonNode.Parse(await File.ReadAllTextAsync(f.Harness.Options.DataPath))!;
        foreach(var account in root["Accounts"]!.AsArray()) foreach(var c in account!["Characters"]!.AsArray())
            if(f.Characters.Take(2).Any(x=>x.Id==c!["Id"]!.GetValue<Guid>())){c!["PvpPoints"]=1000000;c["PvpGrade"]=20;}
        await File.WriteAllTextAsync(f.Harness.Options.DataPath,root.ToJsonString());await f.Store.InitializeAsync();
        await f.Start();await f.Command(0,58,U16(2));await f.Command(0,61,Rank(2,5));
        f.Clock.Advance(9);await f.Service.SweepAsync();check(!f.Packets[0].Any(p=>p.ProtocolId==47),"first rank grace does not expire before ten seconds");
        f.Clock.Advance(1);await f.Service.SweepAsync();
        var loser=await f.Reload(0);var winner=await f.Reload(1);var result=f.Packets[0].Last(p=>p.ProtocolId==47);
        check(loser.PvpPoints==999101 && loser.VictoryPoints==1 && loser.PvpLosses==1 && winner.PvpPoints==999101 && winner.VictoryPoints==0 && winner.PvpLosses==1,
            "selected winner missing rank forfeits once while reporting dead loser retains original matrix loss and one VP");
        check(result.Payload[0]==0 && result.Payload[^1]==1 && BinaryPrimitives.ReadInt32LittleEndian(result.Payload.AsSpan(6))==-899,
            "no surviving alive actor uses deterministic present display seat and draw banner without zeroing frozen losing EXP");
    }

    private static async Task TransportAndEntry(Action<bool,string> check)
    {
        await using var f=await Fixture.Create();
        f.Udp.RegisterChannelSession(f.Sessions[0],IPAddress.Loopback);
        await f.Command(0,53,[1,3,0,0]);
        check(!f.Service.IsBusy(f.Characters[0].Id) && !f.Packets[0].Any(p=>p.ProtocolId==41),"missing native peer endpoint rejects entry before any membership is published");
        f.Udp.SetNatReport(f.Sessions[0].Id,new(0,IPAddress.Loopback,IPAddress.Loopback,42000,1200));
        f.FailSend[0]=true;await f.Command(0,53,[1,3,0,0]);
        check(!f.Service.IsBusy(f.Characters[0].Id),"failed room transport retires its attachment and releases idle-room ownership");
        await f.Command(2,53,[1,3,0,0]);await f.Join(3,f.RoomId(2));
        check(f.Service.IsBusy(f.Characters[2].Id)&&f.Service.IsBusy(f.Characters[3].Id),"one failed transport does not block independent rooms");
    }

    private static async Task CrossMatchReplay(Action<bool,string> check)
    {
        await using var f=await Fixture.Create();await f.Start();
        await f.Command(1,58,U16(1));var death=f.LastBodies[1];
        await f.Command(0,61,Rank(1,10));var rank0=f.LastBodies[0];
        await f.Command(1,61,Rank(2,20));var rank1=f.LastBodies[1];
        await f.Command(0,60,[]);var ack0=f.LastBodies[0];await f.Command(1,60,[]);var ack1=f.LastBodies[1];
        await f.Command(1,56,[1]);await f.Command(0,56,[1]);f.Clear();
        await f.Replay(1,58,death);await f.Replay(0,61,rank0);await f.Replay(1,61,rank1);
        check(!f.Packets[0].Any(p=>p.ProtocolId is 46 or 49 or 47) && (await f.Reload(0)).VictoryPoints==4,
            "old distinct-sequence death/rank bytes cannot settle a second match on the same attachment");
        await f.Command(1,58,U16(1));await f.Command(0,61,Rank(1,10));await f.Command(1,61,Rank(2,20));f.Clear();
        await f.Replay(0,60,ack0);await f.Replay(1,60,ack1);
        check(!f.Packets[0].Any(p=>p.ProtocolId==42 && p.Payload[2]==1),"old result ACK bytes cannot reset the next match's result phase");
        await f.Command(0,60,[]);await f.Command(1,60,[]);
        check(f.Packets[0].Any(p=>p.ProtocolId==42 && p.Payload[2]==1),"fresh second-match ACKs still reset the actual room");
    }
    private static async Task SequenceWrapAndQueuedPhase(Action<bool,string> check)
    {
        var cursor=new PvpCommandSequence();check(cursor.TryConsume(53,[0,0]),"first ordinary command establishes attachment cursor");
        for(var i=1;i<65536;i++) if(!cursor.TryConsume(17,U16((ushort)i))) throw new InvalidOperationException("native monotonic cycle rejected");
        check(cursor.TryConsume(53,[0,0]) && !cursor.TryConsume(53,[0,0]),"a full observed ushort cycle permits legitimate wrapped sequence once");
        check(!cursor.TryConsume(0,U16(30000)) && !cursor.TryConsume(160,U16(30000)) && !cursor.TryConsume(161,U16(30000))
            && !cursor.TryConsume(162,U16(30000)) && cursor.TryConsume(56,U16(1)),"direct check-connection and independent plugin counters do not corrupt native command cursor");
        var sparse=new PvpCommandSequence();sparse.TryConsume(58,U16(100));sparse.TryConsume(17,U16(30000));sparse.TryConsume(17,U16(40000));
        check(!sparse.TryConsume(58,U16(100)) && sparse.TryConsume(61,U16(40001)),"known accepted sequence cannot fake wrap after a large gap and cannot advance the cursor");
        await using(var f=await Fixture.Create())
        {
            await f.Start();for(var i=0;i<40000;i++)f.Observe(0,17);f.Clock.Advance(180);
            await f.Command(0,59,new byte[32]);
            check(f.Packets[0].Any(p=>p.ProtocolId==49),"observing ordinary ingress permits PvP opcode gaps larger than the ushort half range");
        }
        var pause=new PausedWriter();await using(var f=await Fixture.Create(pause))
        {
            await f.Start();await f.Command(1,58,U16(1));await f.Command(0,61,Rank(1,10));pause.Armed=true;
            var settling=f.Command(1,61,Rank(2,20));await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var earlyAck=f.Command(0,60,[]);pause.Release.TrySetResult();await settling;await earlyAck;f.Clear();
            await f.Command(1,60,[]);
            check(!f.Packets[0].Any(p=>p.ProtocolId==42&&p.Payload[2]==1),"ACK admitted during paused ranking cannot rebind when queued work reaches results");
            await f.Command(0,60,[]);
            check(f.Packets[0].Any(p=>p.ProtocolId==42&&p.Payload[2]==1),"fresh ACK after the canonical result closes the actual result phase");
        }
    }
    private sealed class PausedWriter:IDungeonRewardSnapshotWriter
    {
        internal bool Armed;internal TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task WriteAsync(string path,ReadOnlyMemory<byte> snapshot,CancellationToken ct)
        {if(Armed){Entered.TrySetResult();await Release.Task.WaitAsync(ct);}await new DungeonRewardSnapshotWriter().WriteAsync(path,snapshot,ct);}
    }

    private static async Task FreshEntryModels(Action<bool,string> check)
    {
        await using var f=await Fixture.Create();f.Weapons[0]=14873;f.SkillLevels[0]=7;
        await f.Command(0,53,[1,3,0,0]);var room=f.RoomId(0);f.Weapons[1]=12874;f.SkillLevels[1]=9;f.Clear();
        await f.Join(1,room);
        foreach(var recipient in new[]{0,1})
        {
            var packets=f.Packets[recipient];var boundary=packets.FindIndex(p=>p.ProtocolId==43);
            var appearances=packets.Take(boundary).Where(p=>p.ProtocolId==2&&p.Payload[0]==0).ToArray();
            check(appearances.Length==2 && appearances.All(p=>ReadAppearance(p) is var d && d.Weapon==(d.Uid==1?14873:12874)),
                $"recipient {recipient} receives actual current mode0 weapon appearance before membership");
            var details=packets.Take(boundary).Where(p=>p.ProtocolId==2&&p.Payload[0]==1).ToArray();
            check(details.Length==2 && details.All(p=>ReadDetail(p) is var d && d.Experience==0
                && d.Weapon==(d.Uid==1?14873:12874) && d.Skill==(d.Uid==1?7:9)),
                $"recipient {recipient} receives actual updated weapon/skills with mode1 EXP0 before room membership");
        }
    }
    private static (ushort Uid,ushort Weapon) ReadAppearance(GameServerPacket packet)
    {
        using var r=new BinaryReader(new MemoryStream(packet.Payload));r.ReadByte();r.ReadUInt16();var uid=r.ReadUInt16();r.ReadBytes((int)r.ReadUInt32());
        r.ReadBytes(6);ushort weapon=0;var count=r.ReadByte();for(var i=0;i<count;i++){var slot=r.ReadByte();var item=r.ReadUInt16();r.ReadByte();if(slot==9)weapon=item;}
        return(uid,weapon);
    }
    private static (ushort Uid,uint Experience,ushort Weapon,byte Skill) ReadDetail(GameServerPacket packet)
    {
        var wire=packet.Encode();var bytes=wire[GameServerPacket.HeaderLength..];GamePayloadCipher.DecryptServerPayload(bytes);
        using var r=new BinaryReader(new MemoryStream(bytes));_ = r.ReadByte();_ = r.ReadUInt16();var uid=r.ReadUInt16();var xp=r.ReadUInt32();r.ReadBytes((int)r.ReadUInt32());
        ushort weapon=0;var n=r.ReadByte();for(int i=0;i<n;i++){var slot=r.ReadByte();var item=r.ReadUInt16();r.ReadUInt32();r.ReadByte();r.ReadUInt16();if(slot==9)weapon=item;}
        byte skill=0;n=r.ReadByte();for(int i=0;i<n;i++){var id=r.ReadByte();var level=r.ReadByte();if(id==5)skill=level;}
        return(uid,xp,weapon,skill);
    }

    private static async Task Persistence(Action<bool,string> check)
    {
        await using var f=await Fixture.Create();
        var request=new PvpSettlementRequest(Guid.NewGuid(),[new(f.Characters[0].Id,0,false,new ushort[11],new uint[8]),new(f.Characters[1].Id,0,false,new ushort[11],new uint[8])],f.Characters[0].Id,false);
        var first=await f.Store.CommitPvpMatchAsync(request,f.Rating);
        var duplicate=await f.Store.CommitPvpMatchAsync(request,f.Rating);
        var changed=await f.Store.CommitPvpMatchAsync(request with{Winner=f.Characters[1].Id},f.Rating);
        check(first.Status==DungeonRewardLedgerStatus.Committed && duplicate.Awards.SequenceEqual(first.Awards) && changed.Status==DungeonRewardLedgerStatus.Rejected,"canonical receipt replays identical payload and rejects altered winner");
        var stale=await f.Store.SaveCharacterCeraShopPurchaseAsync("alice",f.Characters[0].Id,50000,50000,[],[],victoryPoints:0,expectedVictoryPoints:0);
        check(stale is null && (await f.Reload(0)).VictoryPoints==4,"stale absolute Cera VP cannot roll back committed PvP credit");
        var spend=await f.Store.SaveCharacterCeraShopPurchaseAsync("alice",f.Characters[0].Id,50000,50000,[],[],victoryPoints:1,expectedVictoryPoints:4);
        check(spend?.VictoryPoints==1,"canonical expected VP permits genuine spending rather than max-balance protection");
        await f.Store.SaveCharacterProgressionAsync("alice",f.Characters[0].Id,2,1,3);
        var restart=new JsonGameStore(f.Harness.Options,NullLogger<JsonGameStore>.Instance);await restart.InitializeAsync();
        var replay=await restart.CommitPvpMatchAsync(request,f.Rating);
        check(replay.Status==DungeonRewardLedgerStatus.Committed && (await restart.GetCharacterByNameAsync(f.Characters[0].Name))!.VictoryPoints==1,"ordinary and Cera snapshot writers preserve persistent receipt across restart without recrediting");
    }

    private static async Task Faults(Action<bool,string> check)
    {
        foreach(var after in new[]{false,true})
        {
            await using var f=await Fixture.Create();var writer=new FaultWriter(after);
            var store=new JsonGameStore(f.Harness.Options,NullLogger<JsonGameStore>.Instance,writer);await store.InitializeAsync();writer.Armed=true;
            var request=new PvpSettlementRequest(Guid.NewGuid(),[new(f.Characters[0].Id,0,false,new ushort[11],new uint[8]),new(f.Characters[1].Id,0,false,new ushort[11],new uint[8])],f.Characters[0].Id,false);
            var result=await store.CommitPvpMatchAsync(request,f.Rating);
            check(result.Status==DungeonRewardLedgerStatus.Unknown && result.Awards.Length==0,"uncertain snapshot before/after replace never publishes success awards");
            var retry=await store.CommitPvpMatchAsync(request,f.Rating);
            check(retry.Status==DungeonRewardLedgerStatus.Unknown,"uncertain economic result quarantines automatic retries");
            var reload=new JsonGameStore(f.Harness.Options,NullLogger<JsonGameStore>.Instance);await reload.InitializeAsync();
            var a=(await reload.GetCharacterByNameAsync(f.Characters[0].Name))!;var b=(await reload.GetCharacterByNameAsync(f.Characters[1].Name))!;
            check(a.VictoryPoints==(after?4u:0u) && b.VictoryPoints==(after?1u:0u),"atomic snapshot contains either both PvP awards or neither");
            if(after){await reload.CommitPvpMatchAsync(request,f.Rating);check((await reload.GetCharacterByNameAsync(a.Name))!.VictoryPoints==4,"post-replace restart finds permanent receipt rather than granting again");}
        }
    }
    private sealed class FaultWriter(bool after):IDungeonRewardSnapshotWriter
    {
        internal bool Armed;
        public async Task WriteAsync(string path,ReadOnlyMemory<byte> snapshot,CancellationToken ct)
        {if(Armed&&!after)throw new IOException("before pvp replace");await new DungeonRewardSnapshotWriter().WriteAsync(path,snapshot,ct);if(Armed)throw new IOException("after pvp replace");}
    }
    private static async Task RelayIsolation(Action<bool,string> check)
    {
        await using var f=await Fixture.Create();
        await f.Command(0,53,[1,3,0,0]);await f.Join(1,f.RoomId(0));
        await f.Command(2,53,[1,3,0,0]);await f.Join(3,f.RoomId(2));
        check(f.Udp.PvpRelayTargets(f.Sessions[0].Id).SequenceEqual(new[]{new IPEndPoint(IPAddress.Loopback,42001)})
            && f.Udp.PvpRelayTargets(f.Sessions[2].Id).SequenceEqual(new[]{new IPEndPoint(IPAddress.Loopback,42003)}),"distinct room identities isolate real registered endpoint groups");
        f.Udp.SetNatReport(f.Sessions[2].Id,new(0,IPAddress.Loopback,IPAddress.Loopback,42001,1200));
        check(f.Udp.PvpRelayTargets(f.Sessions[0].Id).Count==0,"conflicting recipient endpoint never leaks datagram into another room");
        var old=f.Attachments[0];await f.Attach(0);await f.Service.DetachAsync(f.Characters[0].Id,old);
        check(f.Udp.PvpRelayTargets(f.Sessions[0].Id).Count==0,"attachment replacement removes old relay context");
    }

    private sealed class Clock:TimeProvider
    { private long value; public override long TimestampFrequency=>1; public override long GetTimestamp()=>value; public override DateTimeOffset GetUtcNow()=>DateTimeOffset.UnixEpoch.AddSeconds(value); internal void Advance(int seconds)=>value+=seconds; }
    private sealed class Fixture: IAsyncDisposable
    {
        internal PartyHarness Harness=null!;private JsonGameStore? _overrideStore;internal JsonGameStore Store=>_overrideStore??Harness.Store;
        internal GameplayDatagramService Udp=null!;internal PvpService Service=null!;internal PvpExperienceCatalog Rating=null!;
        internal Clock Clock=new();internal CharacterRecord[] Characters=[];internal RuntimeSession[] Sessions=[];
        internal ushort[] Sequences=new ushort[4];internal byte[][] LastBodies=[[],[],[],[]];
        internal ushort[] Weapons=[27093,27093,27093,27093];internal byte[] SkillLevels=[1,1,1,1];
        internal Guid[] Attachments=new Guid[4];internal bool[] FailSend=new bool[4];internal List<GameServerPacket>[] Packets=[[],[],[],[]];
        private static readonly string[] Accounts=["alice","bobby","carol","dave"];
        internal static async Task<Fixture> Create(IDungeonRewardSnapshotWriter? writer=null)
        {
            var f=new Fixture{Harness=await PartyHarness.CreateAsync()};
            if(writer is not null){f._overrideStore=new(f.Harness.Options,NullLogger<JsonGameStore>.Instance,writer);await f._overrideStore.InitializeAsync();}
            var dir=f.Harness.Root;Directory.CreateDirectory(Path.Combine(dir,"map"));Directory.CreateDirectory(Path.Combine(dir,"etc"));
            File.WriteAllText(Path.Combine(dir,"map","map.lst"),"50000 `random`\n50018 `one.map`\n50019 `two.map`\n50035 `three.map`");
            foreach(var name in new[]{"one","two","three"}) File.WriteAllText(Path.Combine(dir,"map",name+".map"),"[player number] 2 8");
            int[] thresholds=[2000,8000,19000,36000,59000,91000,131000,181000,240000,275000,315000,365000,422000,490000,566000,655000,753000,865000,986000,1081000,1259000,1526000,1889000,2355000,2930000,3620000,4431000,5370000,6444000,9999999];
            File.WriteAllText(Path.Combine(dir,"etc","pvp_ref.etc"),"[pvp experience max grade]\n30\n[pvp experience]\n"+string.Join('\n',thresholds.Select((v,i)=>$"{i+1} {v}"))+"\n[/pvp experience]");
            var scripts=new ScriptFileSystem(f.Harness.Options,NullLogger<ScriptFileSystem>.Instance);
            f.Rating=new(scripts,NullLogger<PvpExperienceCatalog>.Instance);
            var runtime=new RuntimeState();f.Udp=new(f.Harness.Options,runtime,NullLogger<GameplayDatagramService>.Instance);
            f.Service=new(f.Store,f.Udp,f.Rating,new(scripts),f.Clock,NullLogger<PvpService>.Instance);
            f.Characters=await Task.WhenAll(Accounts.Select(async a=>(await f.Store.GetCharactersAsync(a))[0]));
            f.Sessions=Accounts.Select(a=>runtime.Add("pvp-test",a)).ToArray();
            for(int i=0;i<4;i++)
            {
                f.Udp.RegisterChannelSession(f.Sessions[i],IPAddress.Loopback);
                f.Udp.SetNatReport(f.Sessions[i].Id,new(0,IPAddress.Loopback,IPAddress.Loopback,(ushort)(42000+i),1200));
                await f.Attach(i);
            }
            f.Clear();return f;
        }
        internal async Task Attach(int i)
        {
            Attachments[i]=Guid.NewGuid();var c=Characters[i];
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            PvpModelSnapshot Capture()
            {
                GameInventoryEntry[] worn=[new(9,Weapons[i],1,Durability:20)];
                var appearance=GameProtocolEngine.CreateCurrentCharacterInfo(new(0,Encoding.GetEncoding(936).GetBytes(c.Name),0,0,1),(ushort)(i+1),worn);
                var detail=PartyMemberDetailProjection.CreatePacket((ushort)(i+1),c,new(100,100,1,1,1,1,0,0,0,0,1000,0,0,100,100,100,0,0,0),worn,false,[new(5,SkillLevels[i])]);
                return new(appearance,detail);
            }
            await Service.AttachAsync(new(Attachments[i],Sessions[i].Id,Accounts[i],c.Id,(ushort)(i+1),c.AccountUid,()=>ValueTask.FromResult<PvpModelSnapshot?>(Capture()),
                p=>{if(FailSend[i])throw new IOException("transport gone");Packets[i].Add(p);return ValueTask.CompletedTask;},()=>true,()=>Task.CompletedTask,()=>{}));
        }
        internal Task Command(int i,byte command,byte[] payload)
        {var sequence=++Sequences[i];LastBodies[i]=[..U16(sequence),..payload];return Replay(i,command,LastBodies[i]);}
        internal bool Observe(int i,byte command)=>Service.ObserveCommand(Characters[i].Id,Attachments[i],command,U16(++Sequences[i]));
        internal Task Replay(int i,byte command,byte[] body)=>Service.HandleAsync(Characters[i].Id,Attachments[i],command,body);

        internal Task Join(int i,ushort room)=>Command(i,54,[..U16(room),0]);
        internal ushort RoomId(int i)=>BinaryPrimitives.ReadUInt16LittleEndian(Packets[i].Last(p=>p.ProtocolId==41&&p.Payload.Length>2).Payload.AsSpan(2));
        internal void Clear(){foreach(var p in Packets)p.Clear();}
        internal async Task Start(){await Command(0,53,[1,3,0,0]);await Join(1,RoomId(0));await Command(1,56,[1]);await Command(0,56,[1]);Clear();}
        internal async Task<CharacterRecord> Reload(int i)=>(await Store.GetCharacterByNameAsync(Characters[i].Name))!;
        public async ValueTask DisposeAsync(){Service.Dispose();Udp.Dispose();await Harness.DisposeAsync();}
    }
    private static byte[] U16(ushort value)=>[(byte)value,(byte)(value>>8)];
    private static byte[] Rank(ushort stat,uint score){var payload=new byte[54];BinaryPrimitives.WriteUInt16LittleEndian(payload,stat);BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(22),score);return payload;}
    private static bool Throws(Action action){try{action();return false;}catch(InvalidOperationException){return true;}}
}
