using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;

namespace DFLegacy.Server;

public sealed partial class FallbackRelayService
{
    // Keys are fixed internal reason codes, never remote payload or endpoints.
    private readonly ConcurrentDictionary<string, long> _dropReasons = new();

    private void Drop(string reason)
    {
        Interlocked.Increment(ref _dropped);
        if (!options.EnableConnectionDiagnostics) return;
        var count = _dropReasons.AddOrUpdate(reason, 1, (_, value) => value + 1);
        // First observation and powers of two: malformed traffic cannot produce
        // an unbounded line per packet or force payload logging.
        if ((count & (count - 1)) == 0)
            logger.LogInformation(new EventId(71050, "RelayDrop"),
                "[party-net] relay drop reason={Reason} count={Count}; this is a server rejection count, not an Internet loss rate.",
                reason, count);
    }

    private void ReportStall(RelayConnection connection)
    {
        if (!options.EnableConnectionDiagnostics) return;
        var queueAge = Age(Interlocked.Read(ref connection.QueueWaitSince));
        var writeAge = Age(Interlocked.Read(ref connection.WriteSince));
        if (Math.Max(queueAge, writeAge) < 5000 || Age(connection.LastStallLog) is > 0 and < 30000) return;
        connection.LastStallLog = Stopwatch.GetTimestamp();
        logger.LogWarning(new EventId(71051, "RelayStall"),
            "[party-net] relay stalled game={SessionId} relay={ConnectionId} account={AccountUid} queueTarget={TargetAccountUid} queueWaitMs={QueueWaitMs} writeWaitMs={WriteWaitMs} pending={Pending} rx={Received} tx={Sent}",
            connection.Admission?.SessionId, connection.Id, connection.Admission?.AccountUid,
            Volatile.Read(ref connection.QueueTarget), queueAge, writeAge, connection.Outbound.Reader.Count,
            Interlocked.Read(ref connection.ReceivedFrames), Interlocked.Read(ref connection.SentFrames));
    }

    private void LogTransportError(RelayConnection connection, string direction, Exception exception)
    {
        if (!options.EnableConnectionDiagnostics || exception is OperationCanceledException or ObjectDisposedException) return;
        logger.LogInformation(new EventId(71052, "RelayTransportError"),
            "[party-net] relay transport error game={SessionId} relay={ConnectionId} direction={Direction} error={ErrorType} socketError={SocketError}",
            connection.Admission?.SessionId, connection.Id, direction, exception.GetType().Name,
            (exception.GetBaseException() as SocketException)?.SocketErrorCode.ToString() ?? "none");
    }

    private void LogClosed(RelayConnection connection)
    {
        if (!options.EnableConnectionDiagnostics) return;
        logger.LogInformation(new EventId(71053, "RelayClosed"),
            "[party-net] relay closed game={SessionId} relay={ConnectionId} account={AccountUid} reason={Reason} rx={Received} tx={Sent} pending={Pending}",
            connection.Admission?.SessionId, connection.Id, connection.Admission?.AccountUid,
            connection.CloseReason, Interlocked.Read(ref connection.ReceivedFrames),
            Interlocked.Read(ref connection.SentFrames), connection.Outbound.Reader.Count);
    }

    private string TransportCloseReason(RelayConnection connection, Exception exception, string failure)
    {
        var socketError = (exception.GetBaseException() as SocketException)?.SocketErrorCode;
        var cancellation = exception is OperationCanceledException or ObjectDisposedException
            || socketError is SocketError.OperationAborted or SocketError.Interrupted;
        if (cancellation && _shutdown.IsCancellationRequested) return "server-stop";
        if (exception is OperationCanceledException && connection.Admission is null
            && !connection.Token.IsCancellationRequested) return "registration-timeout";
        // Concrete resets/refusals remain errors even if shutdown races them.
        return failure;
    }

    private static long Age(long start) => start == 0 ? 0 : (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
}
