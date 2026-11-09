using System.Net.Sockets;
namespace DFLegacy.Relay;

public enum RelayTerminationReason { Eof, Cancelled, ExpectedHandoff, SocketError, Timeout, Error, BindFailure }
public sealed record RelayTermination(RelayTerminationReason Reason,string Protocol,string Phase,int Port,long ConnectionId,
    string Direction,long ClientToServerBytes,long ServerToClientBytes,double ElapsedMilliseconds,SocketError? SocketError=null,int? NativeError=null,int Attempts=1)
{
    public bool IsIncident => Reason is RelayTerminationReason.SocketError or RelayTerminationReason.Timeout or RelayTerminationReason.Error or RelayTerminationReason.BindFailure;
    internal static void Deliver(Action<RelayTermination>? sink,RelayTermination value) { try { sink?.Invoke(value); } catch(Exception) { /* Diagnostics must not alter transport. */ } }
}
