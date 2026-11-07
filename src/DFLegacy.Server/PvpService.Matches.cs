using DFLegacy.Protocol;

namespace DFLegacy.Server;

public sealed partial class PvpService
{
    private static void InvalidateReady(Room room)
    {
        for(byte i=0;i<8;i++)
        {
            if(room.Ready[i]) BroadcastMembers(room,GameProtocolEngine.CreatePvpReadyState(i,false));
            room.Ready[i]=false;
        }
        AdvanceGeneration(room);
    }

    private static void DecideTimeout(Match match,uint[] hp)
    {
        if(match.Mode==0)
        {
            var available=match.Players.Where(p=>!p.Forfeit && p.Alive).ToArray();
            var least=available.Length==0 ? 0 : available.Min(p=>hp[p.Seat]);
            var winners=available.Where(p=>hp[p.Seat]==least).ToArray();
            match.Draw=winners.Length!=1;match.Winner=match.Draw ? null : winners[0].Seat;
        }
        else
        {
            var red=match.Players.Where(p=>p.Team==1 && !p.Forfeit).Sum(p=>(long)hp[p.Seat]);
            var blue=match.Players.Where(p=>p.Team==2 && !p.Forfeit).Sum(p=>(long)hp[p.Seat]);
            match.Draw=red==blue;match.WinningTeam=match.Draw ? null : (byte)(red<blue?1:2);
            match.Winner=match.Players.FirstOrDefault(p=>!p.Forfeit && p.Team==match.WinningTeam)?.Seat;
        }
        foreach(var p in match.Players) p.Alive=!match.Draw && !p.Forfeit
            && (match.Mode==0 ? p.Seat==match.Winner : p.Team==match.WinningTeam);
    }

    private void RelayTurn(Match match)
    {
        if(match.Mode!=2 || match.Phase!=Phase.Playing) return;
        var turns=new List<PvpRelayTeam>();
        for(byte team=1;team<=2;team++)
        {
            var waiting=match.Players.Where(p=>p.Team==team && p.Alive && !p.Forfeit)
                .OrderByDescending(p=>match.Room.Requested[p.Seat]).ThenBy(_=>Random.Shared.Next()).ToArray();
            if(waiting.Length==0) { FinishDecision(match);return; }
            var previous=match.Fighters[team-1];
            var retain=previous is {Alive:true,Forfeit:false};
            var next=retain ? previous! : waiting[0];
            match.Fighters[team-1]=next;
            turns.Add(new(retain ? (byte)255 : next.Seat,
                waiting.Select((p,i)=>new PvpRelayOrder(p.Seat,(ushort)(waiting.Length-i))).ToArray()));
        }
        Array.Clear(match.Room.Requested);
        match.TurnScheduled=null;match.InitialTurn=false;
        BroadcastMembers(match.Room,GameProtocolEngine.CreatePvpRelayTurn(turns));
        logger.LogInformation("PvP relay {Match} turn: redSeat={Red} blueSeat={Blue}.",match.Id,
            match.Fighters[0]!.Seat,match.Fighters[1]!.Seat);
    }
}
